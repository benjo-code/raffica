using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class OpenFailuresTests : IDisposable
{
    private readonly PdfEngine _engine = new();
    private readonly Workspace _workspace = new(new CloseGuard(_ => Task.FromResult(CloseChoice.DontSave), _ => Task.FromResult(true)));
    private readonly string _temp = Directory.CreateTempSubdirectory("raffica-open-failures-").FullName;

    public void Dispose()
    {
        foreach (var tab in _workspace.Tabs.ToList())
        {
            tab.Dispose();
        }

        Directory.Delete(_temp, recursive: true);
    }

    private List<string> NotPdfs(int count)
    {
        var paths = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var path = Path.Combine(_temp, $"note-{i:00}.txt");
            File.WriteAllText(path, "this is not a pdf");
            paths.Add(path);
        }

        return paths;
    }

    [Fact]
    public async Task TwentyFilesThatAreNoPdfsEndInOneSummaryAndNoSingleErrors()
    {
        var single = new List<string>();
        var summaries = new List<IReadOnlyList<OpenFailure>>();
        var flow = new OpenFlow(_workspace, () => _engine, (_, _) => Task.FromResult<string?>(null), (path, _) => single.Add(path), summaries.Add);

        await flow.OpenManyAsync(NotPdfs(20));

        Assert.Empty(single);
        var summary = Assert.Single(summaries);
        Assert.Equal(20, summary.Count);
        Assert.All(summary, failure => Assert.Equal(PdfOpenError.NotAPdf, failure.Error));
        Assert.Empty(_workspace.Tabs);
    }

    [Fact]
    public async Task OneFailureAmongGoodFilesStaysAnOrdinaryErrorCardAndTheGoodOnesOpen()
    {
        var single = new List<string>();
        var summaries = new List<IReadOnlyList<OpenFailure>>();
        var flow = new OpenFlow(_workspace, () => _engine, (_, _) => Task.FromResult<string?>(null), (path, _) => single.Add(path), summaries.Add);
        var bad = NotPdfs(1)[0];

        await flow.OpenManyAsync([TestData.Pdf("hello_world.pdf"), bad, TestData.Pdf("text-rich.pdf")]);

        Assert.Equal([bad], single);
        Assert.Empty(summaries);
        Assert.Equal(2, _workspace.Tabs.Count);
    }

    [Fact]
    public async Task WithoutASummaryHandlerEveryFailureIsItsOwnError()
    {
        var single = new List<string>();
        var flow = new OpenFlow(_workspace, () => _engine, (_, _) => Task.FromResult<string?>(null), (path, _) => single.Add(path));
        var bad = NotPdfs(3);

        await flow.OpenManyAsync(bad);

        Assert.Equal(bad, single);
    }

    [Fact]
    public async Task OpenReportsTheReasonToItsObserver()
    {
        var flow = new OpenFlow(_workspace, () => _engine, (_, _) => Task.FromResult<string?>(null), (_, _) => { });
        PdfOpenError? missing = null;
        PdfOpenError? notPdf = null;

        Assert.Null(await flow.OpenAsync(Path.Combine(_temp, "gone.pdf"), error => missing = error));
        Assert.Null(await flow.OpenAsync(NotPdfs(1)[0], error => notPdf = error));

        Assert.Equal(PdfOpenError.FileNotFound, missing);
        Assert.Equal(PdfOpenError.NotAPdf, notPdf);
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(5, 5, false)]
    [InlineData(6, 6, true)]
    [InlineData(20, 6, true)]
    public void TheCardListsUpToFiveNamesThenAndNMore(int count, int lines, bool more)
    {
        foreach (var culture in new[] { "en", "de" })
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo(culture);
            var names = Enumerable.Range(1, count).Select(i => $"f{i}.pdf").ToList();

            var result = OpenFailuresCard.Lines(names);

            Assert.Equal(lines, result.Count);
            Assert.Equal(names.Take(5), result.Take(Math.Min(5, count)));
            if (more)
            {
                Assert.Equal(PdfStrings.Fill(PdfStrings.OpenFailuresMoreFormat, count - 5), result[^1]);
                Assert.Contains((count - 5).ToString(CultureInfo.InvariantCulture), result[^1], StringComparison.Ordinal);
            }
        }

        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Equal("and 15 more", PdfStrings.Fill(PdfStrings.OpenFailuresMoreFormat, 15));
    }

    [AvaloniaFact]
    public async Task DroppingTwentyNonPdfFilesShowsExactlyOneCard()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        using var fixture = WindowFixture.Create();
        var window = fixture.Window;

        await window.OpenFilesAsync(NotPdfs(20));
        ViewFixture.Pump();

        Assert.True(window.Host.IsOpen);
        Assert.Equal(PdfStrings.OpenFailuresTitle, window.Host.ActiveDialog!.Title);
        var texts = window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("note-00.txt", texts);
        Assert.Contains("note-04.txt", texts);
        Assert.DoesNotContain("note-05.txt", texts);
        Assert.Contains("and 15 more", texts);

        window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Assert.False(window.Host.IsOpen, "no second card waits behind the summary");
    }

    [AvaloniaFact]
    public async Task ARecentEntryOfAFileThatCannotBeOpenedStaysInTheList()
    {
        var damaged = NotPdfs(1)[0];
        var here = Path.GetFullPath(TestData.Pdf("text-basic.pdf"));
        using var fixture = WindowFixture.Create(folder =>
        {
            var recent = new Raffica.Core.Settings.RecentFiles();
            recent.Touch(here, DateTimeOffset.UtcNow.AddMinutes(-1));
            recent.Touch(damaged, DateTimeOffset.UtcNow);
            Assert.True(new Raffica.Core.Settings.RecentFilesStore(Path.Combine(folder, "recent.json")).TrySave(recent));
        });
        await fixture.LoadRecentAsync();
        Assert.Equal(2, fixture.Window.Start.Recent.Count);

        var tab = await fixture.Window.OpenRecentAsync(fixture.Window.Start.Recent[0]);
        ViewFixture.Pump();

        Assert.Null(tab);
        Assert.Equal(2, fixture.Window.Start.Recent.Count);
    }
}
