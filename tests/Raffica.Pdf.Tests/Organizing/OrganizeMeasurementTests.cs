using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>
/// Measurements of M2.4 (<c>Docs/DECISIONS.md</c>, "Raffica PDF: page organizing measurements (M2.4)"): entering the organize mode on a
/// 1000-page file, scrolling its list, rotating, deleting and moving 100 pages, and the analyze and apply of a rotation of all 1000 pages. Runs
/// only with <c>RAFFICA_MEASURE_OUT=&lt;folder&gt;</c> (best in Release:
/// <c>dotnet test tests/Raffica.Pdf.Tests -c Release --filter-class "*OrganizeMeasurementTests"</c>) and writes
/// <c>organize-measurements.txt</c> into that folder; measures, never asserts a time. The headless window renders without a display, so the
/// numbers are those of the code path (the plan, the list and its items), not of a graphics device.
/// </summary>
public sealed class OrganizeMeasurementTests
{
    private const string OutputVariable = "RAFFICA_MEASURE_OUT";
    private const int Runs = 3;
    private const int ScrollSteps = 50;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public async Task OrganizeAThousandPagesIsMeasured()
    {
        var output = Environment.GetEnvironmentVariable(OutputVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(output), $"Measurement only: set {OutputVariable}=<folder>.");
        output = Directory.CreateDirectory(output).FullName;

        var steps = new Dictionary<string, List<double>>();
        void Add(string name, double ms)
        {
            if (!steps.TryGetValue(name, out var list))
            {
                steps[name] = list = [];
            }

            list.Add(ms);
        }

        for (var run = 0; run < Runs + 1; run++)
        {
            var measured = run > 0;
            var watch = Stopwatch.StartNew();
            using (var f = await OrganizeListFixture.OpenAsync("thousand-pages.pdf", height: 700))
            {
                ViewFixture.Pump();
                var enter = watch.Elapsed.TotalMilliseconds;
                Assert.Equal(1000, f.List.Items.Count);

                var scroll = f.List.List.GetVisualDescendants().OfType<ScrollViewer>().First();
                var step = Math.Max(1, (scroll.Extent.Height - scroll.Viewport.Height) / ScrollSteps);
                watch.Restart();
                for (var i = 1; i <= ScrollSteps; i++)
                {
                    scroll.Offset = scroll.Offset.WithY(step * i);
                    ViewFixture.Pump();
                }

                var scrolled = watch.Elapsed.TotalMilliseconds;

                f.Session.Selection.Select(0);
                f.Session.Selection.Extend(99);
                watch.Restart();
                Assert.True(f.Session.Rotate(90).IsDone);
                ViewFixture.Pump();
                var rotated = watch.Elapsed.TotalMilliseconds;

                f.Session.Selection.Select(100);
                f.Session.Selection.Extend(199);
                watch.Restart();
                Assert.True(f.Session.Delete().IsDone);
                ViewFixture.Pump();
                var deleted = watch.Elapsed.TotalMilliseconds;

                f.Session.Selection.Select(0);
                f.Session.Selection.Extend(99);
                watch.Restart();
                Assert.True(f.Session.Move(800).IsDone);
                ViewFixture.Pump();
                var moved = watch.Elapsed.TotalMilliseconds;
                Assert.Equal(900, f.List.Items.Count);

                if (measured)
                {
                    Add("enter the mode (open window and list, 1000 items)", enter);
                    Add($"scroll the list in {ScrollSteps} steps, whole run", scrolled);
                    Add("rotate a selection of 100 pages", rotated);
                    Add("delete 100 pages", deleted);
                    Add("move 100 pages to the gap 800", moved);
                }
            }

            using var rig = await ApplyRig.OpenAsync("thousand-pages.pdf");
            rig.Session.Selection.SelectAll();
            Assert.True(rig.Session.Rotate(90).IsDone);
            var plan = rig.Session.Plan;
            var document = rig.Tab.Session.Document!;
            watch.Restart();
            var report = await PageEditExecutor.AnalyzeAsync(plan, document, new Dictionary<PageSourceId, PdfDocument>(), Cancel);
            var analyzed = watch.Elapsed.TotalMilliseconds;
            Assert.False(report.IsRefused);

            var target = Path.Combine(rig.Folder, "rotated.pdf");
            var engine = new PdfEngine();
            watch.Restart();
            var applied = await PageEditApplier.ApplyAsync(rig.Tab.Session, engine, new PageApplyRequest(plan, target, new Dictionary<PageSourceId, PageSourceFile>()), null, Cancel);
            var wrote = watch.Elapsed.TotalMilliseconds;
            Assert.True(applied.Adopted);
            if (measured)
            {
                Add("analyze a rotation of all 1000 pages", analyzed);
                Add("apply it (rewrite, check, adopt the written file)", wrote);
            }
        }

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Raffica PDF organize measurements (M2.4), {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
        text.AppendLine(CultureInfo.InvariantCulture, $"OS {Environment.OSVersion}, {Environment.ProcessorCount} logical processors, {(IsRelease() ? "Release" : "Debug")} build of the tests, headless window");
        text.AppendLine(CultureInfo.InvariantCulture, $"thousand-pages.pdf; median / min / max in ms over {Runs} runs after one warm-up run, each in a fresh window; every UI step includes the pump of the UI queue");
        foreach (var (name, values) in steps)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{name,-55}: {Stats(values)}");
        }

        File.WriteAllText(Path.Combine(output, "organize-measurements.txt"), text.ToString());
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoOrganizeWork()
    {
        using var f = WindowFixture.Create();
        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => tab.Session.State == ViewerState.Ready);
        await Task.Delay(300, Cancel);
        ViewFixture.Pump();

        // The tab never made the organize session (no plan, no history, no inserted document); the window made none of the flows of the
        // organize and output commands.
        Assert.Null(tab.OrganizeIfCreated);
        foreach (var name in new[] { "_insertFlow", "_applyFlow", "_outputFlow", "_exportFlow" })
        {
            var field = typeof(PdfWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException(name + " is gone");
            Assert.Null(field.GetValue(f.Window));
        }
    }

    private static bool IsRelease() =>
#if DEBUG
        false;
#else
        true;
#endif

    private static string Stats(List<double> values)
    {
        var sorted = values.Order().ToList();
        return string.Create(CultureInfo.InvariantCulture, $"{sorted[sorted.Count / 2],8:0.0} / {sorted[0],8:0.0} / {sorted[^1],8:0.0}");
    }
}
