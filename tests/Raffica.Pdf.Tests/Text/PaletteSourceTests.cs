using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Text;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Text;

public sealed class PaletteSourceTests
{
    private static RecentFile Recent(int n) => new($"file{n}.pdf", "C:\\docs", DateTimeOffset.UnixEpoch);

    private static PaletteContext Context(
        IReadOnlyList<PdfTab>? tabs = null,
        IReadOnlyList<OutlineEntry>? outline = null,
        int recent = 0,
        int pages = 5,
        List<string>? log = null,
        bool searchDocument = false) => new()
        {
            SearchDocument = searchDocument ? text => log?.Add("search " + text) : null,
            Tabs = () => tabs ?? [],
            ActiveTab = () => tabs is { Count: > 0 } ? tabs[0] : null,
            Select = tab => log?.Add("select " + tab.Title),
            Recent = count => [.. Enumerable.Range(1, recent).Take(count).Select(Recent)],
            OpenRecent = entry => log?.Add("open " + entry.Name),
            Outline = () => outline,
            PageCount = () => pages,
            GoToPage = page => log?.Add("page " + page),
            ShowDocumentInfo = () => log?.Add("info"),
        };

    [AvaloniaFact]
    public async Task SearchTheDocumentIsTheLastResultAndOpensTheFindBar()
    {
        using var fixture = WindowFixture.Create();
        var window = fixture.Window;
        var palette = window.TitleSearch.ExtraResults!;
        Assert.DoesNotContain(palette("abschnitt"), e => e.Name.StartsWith("Search the document", StringComparison.Ordinal));

        await fixture.OpenAsync("outline.pdf");
        await fixture.LoadRecentAsync();
        var found = palette("abschnitt");
        Assert.True(found.Count > 1);
        var last = found[^1];
        Assert.Equal(PdfStrings.PaletteGroupDocument, last.Group);
        Assert.Equal("Search the document for 'abschnitt'", last.Name);

        // The German entry has the typographic quotes.
        var previous = PdfStrings.Culture;
        try
        {
            PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Im Dokument nach „abschnitt“ suchen", palette("abschnitt")[^1].Name);
        }
        finally
        {
            PdfStrings.Culture = previous;
        }

        last.Run();
        ViewFixture.Pump();
        var bar = window.ShownSearchBar;
        Assert.NotNull(bar);
        Assert.Equal("abschnitt", bar.Query);

        // The search itself started, not only the text of the field.
        Assert.Equal("abschnitt", window.SearchOf(window.Workspace.Active!)!.Query);
    }

    [AvaloniaFact]
    public void SearchTheDocumentNeedsADocumentAndTheHook()
    {
        Assert.DoesNotContain(new PdfPaletteSource(Context(searchDocument: true)).Find("intro"), e => e.Name.StartsWith("Search the document", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ListsTabsRecentOutlineAndGoToPage()
    {
        using var fixture = WindowFixture.Create();
        var outlined = await fixture.OpenAsync("outline.pdf");
        var rich = await fixture.OpenAsync("text-rich.pdf");
        await fixture.LoadRecentAsync();
        var window = fixture.Window;
        var palette = window.TitleSearch.ExtraResults!;
        Assert.NotNull(palette);

        // Open documents: both tabs; choosing one makes it the active tab.
        var tabs = palette("pdf").Where(e => e.Group == PdfStrings.PaletteGroupTabs).ToList();
        Assert.Equal(["outline.pdf", "text-rich.pdf"], tabs.Select(e => e.Name).Order().ToArray());
        tabs.Single(e => e.Name == "outline.pdf").Run();
        Assert.Same(outlined, window.Workspace.Active);

        // Recent files: the opened files, with their folder.
        var recent = palette("text-rich").Where(e => e.Group == PdfStrings.PaletteGroupRecent).ToList();
        var entry = Assert.Single(recent);
        Assert.Equal("text-rich.pdf", entry.Name);
        Assert.False(string.IsNullOrEmpty(entry.Detail));

        // The outline of the active tab, once it was read after the first sharp page.
        await ViewFixture.WaitAsync(() => window.OutlineOf(outlined)?.Flat is not null);
        var outline = palette("Abschnitt 1.1").Where(e => e.Group == PdfStrings.PaletteGroupOutline).ToList();
        var section = Assert.Single(outline);
        Assert.Equal("Abschnitt 1.1", section.Name);
        Assert.Equal(PdfStrings.Fill(PdfStrings.PaletteOutlinePageFormat, 2), section.Detail);
        section.Run();
        ViewFixture.Pump();
        Assert.Equal(1, window.ViewOf(outlined)!.CurrentPage);

        // The outline of another tab does not show.
        window.Workspace.Select(rich);
        ViewFixture.Pump();
        Assert.DoesNotContain(palette("Abschnitt"), e => e.Group == PdfStrings.PaletteGroupOutline);

        // Go to page N: in range only, for the active tab.
        var go = Assert.Single(palette("2"), e => e.Group == PdfStrings.PaletteGroupGoTo);
        Assert.Equal("Go to page 2", go.Name);
        Assert.Equal("of 2", go.Detail);
        go.Run();
        ViewFixture.Pump();
        Assert.Equal(1, window.ViewOf(rich)!.CurrentPage);
        Assert.DoesNotContain(palette("3"), e => e.Group == PdfStrings.PaletteGroupGoTo);
        Assert.DoesNotContain(palette("0"), e => e.Group == PdfStrings.PaletteGroupGoTo);
        Assert.DoesNotContain(palette("-1"), e => e.Group == PdfStrings.PaletteGroupGoTo);
    }

    [AvaloniaFact]
    public async Task PaletteListsThePagesCommands()
    {
        using var fixture = WindowFixture.Create();
        await fixture.OpenAsync("text-rich.pdf");
        var source = fixture.Window.TitleSearch.Source;
        Assert.NotNull(source);
        var tab = Assert.Single(source.Tabs, t => t.Letter == "P");
        Assert.Equal("Pages", tab.Name);
        var ids = tab.Groups.SelectMany(g => g.Commands).Select(c => c.Id).ToArray();
        Assert.Equal(16, ids.Length);
        Assert.Contains("pages.organize", ids);
        Assert.Contains("pages.exportImages", ids);
    }

    [AvaloniaFact]
    public async Task DocumentInfoOpensTheCard()
    {
        using var fixture = WindowFixture.Create();
        await fixture.OpenAsync("text-rich.pdf");
        var window = fixture.Window;
        var info = Assert.Single(window.TitleSearch.ExtraResults!("info"), e => e.Group == PdfStrings.PaletteGroupDocument && e.Name == PdfStrings.InfoTitle);

        info.Run();
        await ViewFixture.WaitAsync(() => window.Host.IsOpen);
        Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(window.Host.ActiveDialog!)));
    }

    [AvaloniaFact]
    public void TheGermanTextsFollowTheLanguage()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        var source = new PdfPaletteSource(Context(pages: 9));
        var go = Assert.Single(source.Find("3"));
        Assert.Equal("Gehe zu Seite 3", go.Name);
        Assert.Equal("von 9", go.Detail);
        Assert.Equal("Gehe zu", go.Group);
    }

    [AvaloniaFact]
    public void RecentFilesAreLimitedToTen()
    {
        var source = new PdfPaletteSource(Context(recent: 40));
        var recent = source.Find("file").Where(e => e.Group == PdfStrings.PaletteGroupRecent).ToList();
        Assert.Equal(PdfPaletteSource.MaxRecent, recent.Count);
        Assert.Equal(10, PdfPaletteSource.MaxRecent);
    }

    [AvaloniaFact]
    public void OutlineEntriesAreLimitedToFiftyAndTheBestMatchComesFirst()
    {
        var entries = Enumerable.Range(1, 400).Select(i => new OutlineEntry($"Chapter number {i}", i - 1)).Append(new OutlineEntry("Number", 0)).ToList();
        var source = new PdfPaletteSource(Context(outline: entries));

        var found = source.Find("number").Where(e => e.Group == PdfStrings.PaletteGroupOutline).ToList();

        Assert.Equal(PdfPaletteSource.MaxOutline, found.Count);
        Assert.Equal("Number", found[0].Name);
    }

    [AvaloniaFact]
    public void AQueryThatMatchesNothingGivesNothing()
    {
        var source = new PdfPaletteSource(Context(outline: [new OutlineEntry("Intro", 0)], recent: 3));
        Assert.Empty(source.Find("zzzzzz"));
        Assert.Empty(source.Find(string.Empty));
        Assert.Empty(source.Find("   "));
    }

    [AvaloniaFact]
    public void AFailingContextNeverThrowsAndEverythingElseStillShows()
    {
        var context = new PaletteContext
        {
            Tabs = () => throw new InvalidOperationException("tabs"),
            ActiveTab = () => throw new InvalidOperationException("active"),
            Select = _ => throw new InvalidOperationException("select"),
            Recent = count => [.. Enumerable.Range(1, 2).Take(count).Select(Recent)],
            OpenRecent = _ => throw new InvalidOperationException("open"),
            Outline = () => throw new ObjectDisposedException("outline"),
            PageCount = () => throw new InvalidOperationException("pages"),
            GoToPage = _ => throw new InvalidOperationException("go"),
            ShowDocumentInfo = () => throw new InvalidOperationException("info"),
        };
        var source = new PdfPaletteSource(context);

        var found = source.Find("file");
        Assert.Equal(2, found.Count);

        // Running an entry that fails does not throw either.
        foreach (var entry in found.Concat(source.Find("5")))
        {
            entry.Run();
        }

        Assert.Empty(source.Find("info"));
    }

    [AvaloniaFact]
    public void HostileQueriesAreHandled()
    {
        var source = new PdfPaletteSource(Context(outline: [new OutlineEntry("(a+)+$", 0)], recent: 2));
        foreach (var query in new[] { ".*", "(", "[", "\\", new string('x', 100_000), "\0", "99999999999999999999", "\u202e" })
        {
            var found = source.Find(query);
            Assert.NotNull(found);
        }

        Assert.Single(source.Find("(a+)+$"), e => e.Group == PdfStrings.PaletteGroupOutline);
    }

    [AvaloniaFact]
    public void TheRunsDoWhatTheEntriesSay()
    {
        var log = new List<string>();
        var source = new PdfPaletteSource(Context(outline: [new OutlineEntry("Intro", 3)], recent: 1, pages: 8, log: log));
        foreach (var entry in source.Find("intro").Concat(source.Find("file")).Concat(source.Find("7")))
        {
            entry.Run();
        }

        Assert.Equal(["page 3", "open file1.pdf", "page 6"], log);
    }

    [AvaloniaFact]
    public async Task TheOutlineIsReadOncePerTabAndSharedWithThePane()
    {
        using var fixture = WindowFixture.Create();
        var tab = await fixture.OpenAsync("outline.pdf");
        var window = fixture.Window;
        await ViewFixture.WaitAsync(() => window.OutlineOf(tab)?.Flat is not null);
        var source = window.OutlineOf(tab)!;
        Assert.Equal(1, source.ReadCount);

        window.ShowOutlinePane();
        ViewFixture.Pump();
        var pane = Assert.IsType<OutlinePane>(window.NavigationPane.Content);
        await pane.LoadTask.WaitAsync(TimeSpan.FromSeconds(10));
        ViewFixture.Pump();

        Assert.Equal(1, source.ReadCount);
        Assert.False(pane.IsEmptyHintShown);
        Assert.True(pane.RowCount > 0);
    }

    [AvaloniaFact]
    public async Task TheOutlineIsFlattenedInOrderAndSkipsEntriesWithoutAPage()
    {
        var chapter = new PdfOutlineItem("Chapter", 0, true, [new PdfOutlineItem("Section", 2, false, [new PdfOutlineItem("Deep", 4, false, [])]), new PdfOutlineItem("No page", null, false, [])]);
        var after = new PdfOutlineItem("After", 7, false, []);
        using var source = new OutlineSource(_ => Task.FromResult<IReadOnlyList<PdfOutlineItem>>([chapter, after]));

        source.Start();
        await source.LoadTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["Chapter", "Section", "Deep", "After"], source.Flat!.Select(e => e.Title));
        Assert.Equal([0, 2, 4, 7], source.Flat!.Select(e => e.Page));
    }

    [AvaloniaFact]
    public async Task ACyclicOutlineEndsAndAFailingReadGivesAnEmptyList()
    {
        var children = new List<PdfOutlineItem>();
        var loop = new PdfOutlineItem("Loop", 1, true, children);
        children.Add(loop);
        using var cyclic = new OutlineSource(_ => Task.FromResult<IReadOnlyList<PdfOutlineItem>>([loop]));
        cyclic.Start();
        await cyclic.LoadTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(cyclic.Flat!);

        using var failing = new OutlineSource(_ => throw new InvalidDataException("bad"));
        failing.Start();
        await failing.LoadTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(failing.Flat!);
        Assert.Empty(await failing.ReadAsync(CancellationToken.None));
    }
}
