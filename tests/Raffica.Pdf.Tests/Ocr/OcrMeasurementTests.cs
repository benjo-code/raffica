using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Ocr;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

/// <summary>
/// Measurement of M2.5 (<c>Docs/DECISIONS.md</c>, "Raffica PDF: redaction, OCR and text editing measurements (M2.5)"): the text recognition of 20 scanned
/// pages (the pages of <c>ocr-scan.pdf</c> repeated to 20, built in a temp folder) with the real library and the shipped language data, through the run
/// of the window. Runs only with <c>RAFFICA_MEASURE_OUT=&lt;folder&gt;</c> and needs the OCR library (skipped without it; a failure under
/// <c>RAFFICA_REQUIRE_OCR=1</c>); writes <c>ocr-measurements.txt</c>; information only, no time is asserted.
/// </summary>
public sealed class OcrMeasurementTests
{
    private const string OutputVariable = "RAFFICA_MEASURE_OUT";
    private const int Pages = 20;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public async Task RunOnTwentyScannedPages()
    {
        var output = Environment.GetEnvironmentVariable(OutputVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(output), $"Measurement only: set {OutputVariable}=<folder>.");
        output = Directory.CreateDirectory(output).FullName;
        OcrTestLibrary.Require();

        var folder = Directory.CreateTempSubdirectory("raffica-ocr-measure-").FullName;
        try
        {
            var scan = Path.Combine(folder, "scan20.pdf");
            await BuildAsync(folder, scan);

            using var fixture = WindowFixture.Create();
            fixture.Window.OcrLibraryProbe = () => true;
            fixture.Window.ShippedTessdataDirectory = OcrTestLibrary.Tessdata;
            var tab = await fixture.Window.Flow.OpenAsync(scan) ?? throw new InvalidOperationException("the scan did not open");
            await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
            Assert.Equal(Pages, tab.Session.Document!.PageCount);

            var jobs = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
            var watch = Stopwatch.StartNew();
            var run = fixture.Window.StartOcrRunAsync(tab, new OcrOptions("eng", Jobs: jobs));
            await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Any(b => b.Name == "PART_Ok"));
            var elapsed = watch.Elapsed.TotalMilliseconds;
            var card = fixture.Window.Host.ActiveDialog!;
            var summary = card.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().FirstOrDefault(b => b.Name == "PART_Summary")?.Text ?? string.Empty;
            OcrRig.Click(card, "PART_Ok");
            await run;
            var process = Process.GetCurrentProcess();
            process.Refresh();

            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture, $"Raffica PDF text recognition measurement (M2.5), {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
            text.AppendLine(CultureInfo.InvariantCulture, $"OS {Environment.OSVersion}, {Environment.ProcessorCount} logical processors, {(IsRelease() ? "Release" : "Debug")} build of the tests, headless window; single run, hot file cache");
            text.AppendLine(CultureInfo.InvariantCulture, $"{Pages} pages (the pages of ocr-scan.pdf repeated), language eng, {jobs} jobs, real library, shipped language data");
            text.AppendLine(CultureInfo.InvariantCulture, $"{"start of the run to the report card",-40}: {elapsed,9:0} ms ({elapsed / Pages:0} ms per page)");
            text.AppendLine(CultureInfo.InvariantCulture, $"{"report summary",-40}: {summary}");
            text.AppendLine(CultureInfo.InvariantCulture, $"{"peak working set of the test process",-40}: {process.PeakWorkingSet64 / (1024.0 * 1024.0),9:0} MiB");
            File.WriteAllText(Path.Combine(output, "ocr-measurements.txt"), text.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // Twenty pages out of the three of ocr-scan.pdf: the page plan duplicates the first page until there are twenty (the same path as the organize apply).
    private static async Task BuildAsync(string folder, string target)
    {
        var source = Path.Combine(folder, "one.pdf");
        File.Copy(TestData.Pdf("ocr-scan.pdf"), source);
        var engine = new PdfEngine();
        using var session = new ViewerSession(engine);
        await session.OpenAsync(source);
        var plan = PageEditPlan.Identity(session.Document!.PageCount);
        while (plan.Slots.Count < Pages)
        {
            plan = plan.Duplicate([0]);
        }

        var applied = await PageEditApplier.ApplyAsync(session, engine, new PageApplyRequest(plan, target, new Dictionary<PageSourceId, PageSourceFile>()), null, Ct);
        Assert.True(applied.Adopted);
    }

    private static bool IsRelease() =>
#if DEBUG
        false;
#else
        true;
#endif
}
