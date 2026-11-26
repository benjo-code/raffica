using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>
/// The page-based field results through the real runner (milestone 4.5b, task 11): the passes that write results, tell the layout and read the pages again (at most three), what a stable document costs, the settle before a save and the
/// file that comes out of it. A manual clock stands for the timer; the layout is the tab's own.
/// </summary>
public sealed class FieldSettleTests
{
    private static readonly TocOptions s_options = new(1, 3, true, true, TocLeader.Dots, null);

    private static readonly string[] s_chapters = ["Alpha", "Beta", "Gamma"];

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static Task<FieldHarness> OpenAsync() => FieldHarness.OpenAsync(bytes: FieldTestDocument.TocBytes());

    // The timer tick once the layout is complete (a refresh of a document with page results waits for that, and an edit makes the layout incomplete until its background pass is over).
    private static async Task FireAsync(FieldHarness h)
    {
        await ViewHarness.WaitAsync(() => h.Tab.Runner.IsFaulted || (h.Tab.Editor?.ActiveLayout ?? h.Tab.Runner.PageLayout) is { IsComplete: true }, 90000);
        h.Clock.Fire();
    }

    // A table of contents in the empty first paragraph, through the editor like the command does it (the refresh is notified by the edit).
    private static void InsertToc(FieldHarness h)
    {
        var session = h.Tab.EnsureEditor();
        var host = h.Tab.EnsureFields().Host;
        var first = (Paragraph)h.Tab.Docx.Model.Body.Blocks[0];
        var result = session.Apply(e => e.InsertToc(new TextPosition(StoryRef.Body, first, 0), s_options, host), 0);
        Assert.NotNull(result);
    }

    // Many words into the "intro" paragraph, between the table of contents and the first chapter: it grows by a page or more and pushes the chapters down.
    private static void TypeLongText(FieldHarness h)
    {
        var session = h.Tab.EnsureEditor();
        var model = h.Tab.Docx.Model;
        var before = model.Body.Blocks.OfType<Paragraph>().First(p => TextOf(p) == "intro");
        var at = new TextPosition(StoryRef.Body, before, 0);
        var text = string.Join(' ', Enumerable.Repeat("filler", 1200));
        Assert.NotNull(session.Apply(e => e.InsertText(new TextRange(at, at), text), 0));
    }

    // The entry lines of the table of contents (the paragraphs with a tab) and the stored page of the PAGEREF paragraph.
    private static List<string> Entries(WriteTab tab) => [.. tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(TextOf).Where(t => t.Contains('\t', StringComparison.Ordinal))];

    private static string PagerefText(WriteTab tab) => TextOf(tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Last());

    // The page a heading is on in a layout made from scratch over the same model: the number the page prints.
    private static async Task<(DocumentLayout Fresh, Func<string, string> PageOfHeading)> FreshAsync(WriteTab tab)
    {
        var model = tab.Docx.Model;
        var fresh = DocumentLayout.Create(model, tab.Fonts, new LayoutOptions());
        await fresh.LayOutRestAsync(null, CancellationToken.None);
        return (fresh, name =>
        {
            var heading = model.Body.Blocks.OfType<Paragraph>().First(p => TextOf(p) == name);
            var page = fresh.Locate(model.Body.Blocks.ToList().IndexOf(heading))!.Value.Page;
            return fresh.Page(page).Number;
        }
        );
    }

    // The live layout of the session equals a fresh one: the same pages, the same text and numbers on each (the app-level equal of the oracle's full comparison).
    private static async Task AssertLiveLayoutEqualsFreshAsync(FieldHarness h)
    {
        await ViewHarness.WaitAsync(() => h.Tab.Runner.IsFaulted || h.Tab.Editor!.ActiveLayout is { IsSettled: true }, 90000);
        var live = h.Tab.Editor!.ActiveLayout!;
        var (fresh, _) = await FreshAsync(h.Tab);
        Assert.Equal(fresh.LaidOutPages, live.LaidOutPages);
        for (var p = 0; p < fresh.LaidOutPages; p++)
        {
            Assert.True(live.TryGetPage(p, out var page) && page is not null);
            Assert.Equal(fresh.Page(p).Number, page!.Number);
            Assert.Equal(fresh.Page(p).Text, page.Text);
        }
    }

    [AvaloniaFact]
    public async Task TypingBeforeAHeadingMovesItsEntryAfterTheLayoutSettles()
    {
        using var h = await OpenAsync();
        InsertToc(h);
        await FireAsync(h);
        var runner = h.Tab.Fields!;
        Assert.Equal(0, runner.Failures);
        Assert.All(s_chapters, (_, i) => Assert.EndsWith("\t1", Entries(h.Tab)[i], StringComparison.Ordinal));
        Assert.Equal("see page 1", PagerefText(h.Tab));
        var before = Entries(h.Tab);

        TypeLongText(h);
        var passes = runner.Passes;
        await FireAsync(h);

        Assert.True(runner.Passes - passes is >= 1 and <= FieldRefreshRunner.MaxPasses);
        Assert.Equal(0, runner.Unsettled);
        Assert.Equal(0, runner.Failures);
        var (_, pageOf) = await FreshAsync(h.Tab);
        var after = Entries(h.Tab);
        Assert.Equal(3, after.Count);
        Assert.NotEqual(before[0], after[0]);
        for (var i = 0; i < 3; i++)
        {
            var name = s_chapters[i];
            Assert.EndsWith("\t" + pageOf(name), after[i], StringComparison.Ordinal);
        }

        Assert.Equal("see page " + pageOf("the target"), PagerefText(h.Tab));
        await AssertLiveLayoutEqualsFreshAsync(h);
    }

    [AvaloniaFact]
    public async Task AStableDocumentTakesOnePassAndWritesNothing()
    {
        using var h = await OpenAsync();
        InsertToc(h);
        await FireAsync(h);
        var runner = h.Tab.Fields!;
        var model = h.Tab.Docx.Model;
        var passes = runner.Passes;
        var derived = model.Edits.DerivedVersion;

        // A letter in the first paragraph moves nothing: one pass, nothing written, the derived version stands.
        var session = h.Tab.EnsureEditor();
        var first = model.Body.Blocks.OfType<Paragraph>().First(p => TextOf(p) == "alpha text");
        var at = new TextPosition(StoryRef.Body, first, 0);
        Assert.NotNull(session.Apply(e => e.InsertText(new TextRange(at, at), "x"), 0));
        await FireAsync(h);

        Assert.Equal(1, runner.Passes - passes);
        Assert.Equal(derived, model.Edits.DerivedVersion);
        Assert.Equal(0, runner.Unsettled);
        Assert.Equal(0, runner.Failures);
    }

    // A fake host whose page text grows with every refresh (its own length changes, so the lines the table of contents needs would too): the numbers never settle.
    private sealed class GrowingHost(IFieldHost inner) : IFieldHost
    {
        public int Round { get; set; }

        public bool IsComplete => inner.IsComplete;

        public string? NumberLabel(Paragraph paragraph) => inner.NumberLabel(paragraph);

        public (int PageIndex, string PageText)? PageOf(TextPosition position) => (Round, new string('1', Round));

        public string? NoteMarkText(int noteId, NoteType type) => inner.NoteMarkText(noteId, type);
    }

    [AvaloniaFact]
    public async Task ThePassesEndAfterThreeWhenTheNumbersKeepChangingTheirOwnLength()
    {
        using var h = await OpenAsync();
        InsertToc(h);
        var runner = h.Tab.Fields!;
        var growing = new GrowingHost(runner.Host);
        runner.Refresher = (editor, _, scope, blocks) =>
        {
            growing.Round++;
            return editor.RefreshFields(growing, scope, blocks);
        };

        await FireAsync(h);

        Assert.Equal(FieldRefreshRunner.MaxPasses, runner.Passes);
        Assert.Equal(1, runner.Unsettled);
        Assert.Equal(0, runner.Failures);
        Assert.False(runner.IsPending);

        // The last result stays: the third pass wrote "111" after the entries.
        Assert.All(Entries(h.Tab), line => Assert.EndsWith("\t111", line, StringComparison.Ordinal));
        Assert.Equal("see page 111", PagerefText(h.Tab));
    }

    [AvaloniaFact]
    public async Task ASaveSettlesFirstAndWritesTheSettledNumbers()
    {
        using var h = await OpenAsync();
        InsertToc(h);
        TypeLongText(h);
        var runner = h.Tab.Fields!;
        var stale = Entries(h.Tab);

        // No timer tick: the save's settle alone brings the numbers (and the layout it needs) up to date, synchronously.
        await h.Tab.SettleFieldsAsync(CancellationToken.None);

        Assert.True(runner.Passes >= 1);
        Assert.Equal(0, runner.Unsettled);
        Assert.Equal(0, runner.Failures);
        var (_, pageOf) = await FreshAsync(h.Tab);
        var entries = Entries(h.Tab);
        Assert.NotEqual(stale[0], entries[0]);
        for (var i = 0; i < 3; i++)
        {
            Assert.EndsWith("\t" + pageOf(s_chapters[i]), entries[i], StringComparison.Ordinal);
        }

        Assert.Equal("see page " + pageOf("the target"), PagerefText(h.Tab));
        await AssertLiveLayoutEqualsFreshAsync(h);
    }

    [AvaloniaFact]
    public async Task TheSavedFileReadAgainEqualsTheModel()
    {
        using var h = await OpenAsync();
        InsertToc(h);
        TypeLongText(h);
        await h.Tab.SettleFieldsAsync(CancellationToken.None);
        var copy = System.IO.Path.Combine(h.Folder, "saved.docx");

        await h.Tab.Docx.SaveAsync(copy);

        using var again = DocxFile.Open(copy);
        var live = h.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(TextOf).ToList();
        var saved = again.Model.Body.Blocks.OfType<Paragraph>().Select(TextOf).ToList();
        Assert.Equal(live, saved);
        Assert.Contains(saved, line => line.StartsWith("Alpha\t", StringComparison.Ordinal));
        Assert.DoesNotContain(saved, line => line.EndsWith("\t77", StringComparison.Ordinal) || line == "see page 77");
    }

    [AvaloniaFact]
    public async Task OpeningAStaleDocumentRefreshesItsPageNumbersAtTheFirstCompleteLayout()
    {
        using var h = await OpenAsync();
        Assert.Equal("see page 77", PagerefText(h.Tab));

        // What the window does when the layout is complete: the first look at the whole body.
        h.Tab.EnsureFields().Notify(null);
        await FireAsync(h);

        Assert.Equal("see page 1", PagerefText(h.Tab));
        Assert.Equal(0, h.Tab.Fields!.Failures);
        Assert.Equal(0, h.Tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task TheRealRefreshOnTheThesisSettlesAndTheLiveLayoutEqualsAFreshOne()
    {
        using var h = await FieldHarness.OpenAsync(bytes: File.ReadAllBytes(WindowFixture.Thesis));
        h.Type("x");
        var runner = h.Tab.Fields!;
        runner.Notify(null);

        await FireAsync(h);

        Assert.Equal(0, runner.Failures);
        Assert.Equal(0, runner.Unsettled);
        Assert.InRange(runner.Passes, 1, FieldRefreshRunner.MaxPasses);
        await AssertLiveLayoutEqualsFreshAsync(h);

        // A second burst on the settled thesis (a letter that moves nothing): one pass, nothing written. The tick runs on the UI thread after the 250 ms delay, never in a key handler (about 130 ms on this thesis in the container: the whole body is walked, see the report of task 11).
        var session = h.Tab.EnsureEditor();
        var derived = h.Tab.Docx.Model.Edits.DerivedVersion;
        var passes = runner.Passes;
        var paragraph = h.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().First(p => TextOf(p).Length > 20);
        var at = new TextPosition(StoryRef.Body, paragraph, 0);
        Assert.NotNull(session.Apply(e => e.InsertText(new TextRange(at, at), "y"), 0));
        await ViewHarness.WaitAsync(() => session.ActiveLayout is { IsComplete: true }, 90000);
        h.Clock.Fire();
        Assert.Equal(1, runner.Passes - passes);
        Assert.Equal(derived, h.Tab.Docx.Model.Edits.DerivedVersion);
    }
}
