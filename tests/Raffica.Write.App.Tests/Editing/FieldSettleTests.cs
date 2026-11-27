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
        await h.FireUntilIdleAsync();
        var runner = h.Tab.Fields!;
        Assert.Equal(0, runner.Failures);
        Assert.All(s_chapters, (_, i) => Assert.EndsWith("\t1", Entries(h.Tab)[i], StringComparison.Ordinal));
        Assert.Equal("see page 1", PagerefText(h.Tab));
        var before = Entries(h.Tab);

        TypeLongText(h);
        var passes = runner.Passes;
        await h.FireUntilIdleAsync();

        Assert.True(runner.Passes - passes is >= 1 and <= FieldRefreshRunner.MaxPasses);
        Assert.Equal(0, runner.Unsettled);
        Assert.Equal(0, runner.PagePassesSkipped);
        Assert.Equal(0, runner.SyncLayoutCompletions);
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
        await h.FireUntilIdleAsync();
        var runner = h.Tab.Fields!;
        var model = h.Tab.Docx.Model;
        var passes = runner.Passes;
        var derived = model.Edits.DerivedVersion;

        // A letter in the first paragraph moves nothing: one pass, nothing written, the derived version stands.
        var session = h.Tab.EnsureEditor();
        var first = model.Body.Blocks.OfType<Paragraph>().First(p => TextOf(p) == "alpha text");
        var at = new TextPosition(StoryRef.Body, first, 0);
        Assert.NotNull(session.Apply(e => e.InsertText(new TextRange(at, at), "x"), 0));
        await h.FireUntilIdleAsync();

        Assert.Equal(1, runner.Passes - passes);
        Assert.Equal(1, runner.PagePassesSkipped);
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

        public string? ChapterNumber(Paragraph paragraph) => inner.ChapterNumber(paragraph);

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

        await h.FireUntilIdleAsync();

        Assert.Equal(FieldRefreshRunner.MaxPasses, runner.Passes);
        Assert.Equal(1, runner.Unsettled);
        Assert.Equal(0, runner.Failures);
        Assert.False(runner.IsPending);

        // The last result stays: the third pass wrote "111" after the entries.
        Assert.All(Entries(h.Tab), line => Assert.EndsWith("\t111", line, StringComparison.Ordinal));
        Assert.Equal("see page 111", PagerefText(h.Tab));
    }

    // The timer path waits for the background layout between passes and never lays the pages out on the UI thread; the bound of three passes and the count of unsettled bursts hold across the ticks.
    [AvaloniaFact]
    public async Task TheTimerPathNeverLaysTheLayoutOutOnTheUiThread()
    {
        using var h = await OpenAsync();
        InsertToc(h);
        TypeLongText(h);
        var runner = h.Tab.Fields!;

        await h.FireUntilIdleAsync();

        Assert.Equal(0, runner.SyncLayoutCompletions);
        Assert.Equal(0, runner.Unsettled);
        Assert.Equal(0, runner.Failures);
        Assert.InRange(runner.Passes, 2, FieldRefreshRunner.MaxPasses);
        var (_, pageOf) = await FreshAsync(h.Tab);
        var entries = Entries(h.Tab);
        for (var i = 0; i < 3; i++)
        {
            Assert.EndsWith("	" + pageOf(s_chapters[i]), entries[i], StringComparison.Ordinal);
        }

        // A pass that never settles also ends at the bound across ticks, without a synchronous layout.
        var growing = new GrowingHost(runner.Host);
        runner.Refresher = (editor, _, scope, blocks) =>
        {
            growing.Round++;
            return editor.RefreshFields(growing, scope, blocks);
        };
        var passes = runner.Passes;
        runner.Notify(null);
        await h.FireUntilIdleAsync();
        Assert.Equal(FieldRefreshRunner.MaxPasses, runner.Passes - passes);
        Assert.Equal(1, runner.Unsettled);
        Assert.Equal(0, runner.SyncLayoutCompletions);
    }

    // A rewrite that moves the pagination far from the page in view (the list at the start grows from two lines to sixty): the tick lays the pages out through the page in view only, the background layout goes on, and the next pass follows when it
    // is complete. The end state equals a full layout.
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task ATimerRefreshThatMovesThePaginationLaysOutOnlyThroughThePageInView(int inView)
    {
        using var h = await FieldHarness.OpenAsync(bytes: FieldTestDocument.LongTocBytes());
        var total = h.Tab.Runner.PageLayout.LaidOutPages;
        Assert.True(total > 15, "the document is long: " + total);
        if (inView > 0)
        {
            h.Tab.PageInView = _ => inView;
        }

        var runner = h.Tab.EnsureFields();
        var applied = new List<(int Pages, bool Complete)>();
        runner.AfterApply = layout => applied.Add((layout.LaidOutPages, layout.IsComplete));
        runner.Notify(null);

        await h.FireAsync();

        // What the tick left behind, read under its lease: the pages through the one in view, never the rest of the document.
        Assert.Equal(1, runner.Passes);
        var after = Assert.Single(applied);
        Assert.False(after.Complete, "the tick laid the document out to its end");
        Assert.InRange(after.Pages, inView + 1, inView + 3);
        Assert.True(runner.IsPending, "the next pass waits for the background layout");

        // The background layout goes on and the next pass runs when it is complete (the tick waits for that and would time out if nothing resumed it).
        await h.FireUntilIdleAsync();

        Assert.InRange(runner.Passes, 2, FieldRefreshRunner.MaxPasses);
        Assert.Equal(0, runner.Unsettled);
        Assert.Equal(0, runner.Failures);
        Assert.Equal(0, runner.SyncLayoutCompletions);
        Assert.Equal(FieldTestDocument.LongChapters, Entries(h.Tab).Count);
        await ViewHarness.WaitAsync(() => h.Tab.Runner.PageLayout.IsSettled, 90000);
        var (fresh, pageOf) = await FreshAsync(h.Tab);
        Assert.EndsWith("\t" + pageOf("Chapter 60"), Entries(h.Tab)[^1], StringComparison.Ordinal);
        var live = h.Tab.Runner.PageLayout;
        Assert.Equal(fresh.LaidOutPages, live.LaidOutPages);
        for (var p = 0; p < fresh.LaidOutPages; p++)
        {
            Assert.Equal(fresh.Page(p).Number, live.Page(p).Number);
            Assert.Equal(fresh.Page(p).Text, live.Page(p).Text);
        }
    }

    // The settle of a save goes the whole way at once: no tick follows it.
    [AvaloniaFact]
    public async Task ASaveSettlesALongDocumentInOneCall()
    {
        using var h = await FieldHarness.OpenAsync(bytes: FieldTestDocument.LongTocBytes());
        var runner = h.Tab.EnsureFields();
        runner.Notify(null);

        await h.Tab.SettleFieldsAsync(CancellationToken.None);

        Assert.False(runner.IsPending);
        Assert.Equal(0, runner.Unsettled);
        Assert.Equal(0, runner.Failures);
        var (_, pageOf) = await FreshAsync(h.Tab);
        Assert.EndsWith("\t" + pageOf("Chapter 60"), Entries(h.Tab)[^1], StringComparison.Ordinal);
    }

    // A page reference inserted in the writing view stores "?" (there are no pages there). The refresh that ran there looked at the edit without pages, so the next burst in the pages view must look at the page results again, although the page
    // table is the one the last page pass settled.
    private static async Task<(FieldHarness Harness, Paragraph Reference)> InsertPageReferenceInTheWritingViewAsync()
    {
        var h = await OpenAsync();
        h.Tab.EnsureFields().Notify(null);
        await h.FireUntilIdleAsync();
        var runner = h.Tab.Fields!;
        Assert.Equal("see page 1", PagerefText(h.Tab));

        var session = h.Tab.EnsureEditor();
        session.ActiveMode = LayoutMode.Flow;
        h.Tab.Runner.EnsureFlow();
        await ViewHarness.WaitAsync(() => h.Tab.Runner.FlowLayout is { IsComplete: true }, 30000);
        var model = h.Tab.Docx.Model;
        var reference = model.Body.Blocks.OfType<Paragraph>().First(p => TextOf(p) == "alpha text");
        var target = CrossReferences.Targets(model, runner.Host, CrossRefTargetKind.Heading)[0];
        var at = new TextPosition(StoryRef.Body, reference, TextOf(reference).Length);
        Assert.NotNull(session.Apply(e => e.InsertCrossReference(at, target, CrossRefKind.Page, runner.Host), 0));
        await h.FireUntilIdleAsync();
        Assert.Equal("alpha text?", TextOf(reference));
        return (h, reference);
    }

    // Back in the pages view, the layout of the pages made again and complete, and a letter in a plain paragraph.
    private static async Task SwitchBackAndTypeAsync(FieldHarness h)
    {
        var session = h.Tab.Editor!;
        session.ActiveMode = LayoutMode.Pages;
        await ViewHarness.WaitAsync(() => session.ActiveLayout is { IsComplete: true }, 30000);
        var plain = h.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().First(p => TextOf(p) == "beta text");
        var typed = new TextPosition(StoryRef.Body, plain, 0);
        Assert.NotNull(session.Apply(e => e.InsertText(new TextRange(typed, typed), "x"), 0));
    }

    [AvaloniaFact]
    public async Task APageResultInsertedInTheWritingViewIsFilledByTheNextBurstInThePagesView()
    {
        var (h, reference) = await InsertPageReferenceInTheWritingViewAsync();
        using var harness = h;
        await SwitchBackAndTypeAsync(h);

        await h.FireUntilIdleAsync();

        Assert.Equal("alpha text1", TextOf(reference));
        Assert.Equal(0, h.Tab.Fields!.PagePassesSkipped);
        Assert.Equal(0, h.Tab.Fields!.Failures);
    }

    [AvaloniaFact]
    public async Task APageResultInsertedInTheWritingViewIsFilledByTheNextSaveInThePagesView()
    {
        var (h, reference) = await InsertPageReferenceInTheWritingViewAsync();
        using var harness = h;
        await SwitchBackAndTypeAsync(h);

        await h.Tab.SettleFieldsAsync(CancellationToken.None);

        Assert.Equal("alpha text1", TextOf(reference));
        Assert.Equal(0, h.Tab.Fields!.Failures);
    }

    // Switching back is enough: the page results the writing view left owed are brought current by the next tick, without an edit. Nothing is refreshed in the switch itself, and the refresh is no edit.
    [AvaloniaFact]
    public async Task SwitchingBackWithNoEditFillsThePageResultsAtTheNextTick()
    {
        var (h, reference) = await InsertPageReferenceInTheWritingViewAsync();
        using var harness = h;
        var runner = h.Tab.Fields!;
        var edits = h.Tab.Docx.Model.Edits;
        var revision = edits.Revision;
        var passes = runner.Passes;
        Assert.False(h.Clock.IsArmed);

        h.Tab.Editor!.ActiveMode = LayoutMode.Pages;

        Assert.Equal(passes, runner.Passes);
        Assert.Equal("alpha text?", TextOf(reference));
        Assert.True(h.Clock.IsArmed, "the switch wakes the refresh");

        await h.FireUntilIdleAsync();

        Assert.Equal("alpha text1", TextOf(reference));
        Assert.Equal(revision, edits.Revision);
        Assert.False(runner.IsPending);
        Assert.False(h.Clock.IsArmed);
        Assert.Equal(0, runner.Failures);

        // Nothing is owed any more: another switch to and fro asks for nothing.
        var settled = runner.Passes;
        h.Tab.Editor!.ActiveMode = LayoutMode.Flow;
        h.Tab.Editor!.ActiveMode = LayoutMode.Pages;
        Assert.False(h.Clock.IsArmed);
        await h.Tab.SettleFieldsAsync(CancellationToken.None);
        Assert.Equal(settled, runner.Passes);
    }

    [AvaloniaFact]
    public async Task SwitchingBackWithNoEditThenSavingWritesThePageNumbers()
    {
        var (h, reference) = await InsertPageReferenceInTheWritingViewAsync();
        using var harness = h;
        var edits = h.Tab.Docx.Model.Edits;
        var revision = edits.Revision;
        h.Tab.Editor!.ActiveMode = LayoutMode.Pages;

        // No tick: the settle of the save brings the numbers up to date before the file is written.
        await h.Tab.SettleFieldsAsync(CancellationToken.None);
        var copy = System.IO.Path.Combine(h.Folder, "switched.docx");
        await h.Tab.Docx.SaveAsync(copy);

        Assert.Equal("alpha text1", TextOf(reference));
        Assert.Equal(revision, edits.Revision);
        Assert.False(h.Tab.Fields!.IsPending);
        using var again = DocxFile.Open(copy);
        Assert.Contains("alpha text1", again.Model.Body.Blocks.OfType<Paragraph>().Select(TextOf));
    }

    // A save made while the writing view is still followed has no pages to read: the results stay owed, and the save after the switch back writes them.
    [AvaloniaFact]
    public async Task ASaveInTheWritingViewKeepsThePageResultsOwed()
    {
        var (h, reference) = await InsertPageReferenceInTheWritingViewAsync();
        using var harness = h;

        await h.Tab.SettleFieldsAsync(CancellationToken.None);
        Assert.Equal("alpha text?", TextOf(reference));

        h.Tab.Editor!.ActiveMode = LayoutMode.Pages;
        await h.Tab.SettleFieldsAsync(CancellationToken.None);
        Assert.Equal("alpha text1", TextOf(reference));
    }

    [AvaloniaFact]
    public async Task AModeSwitchAloneMakesNoRefreshRunner()
    {
        using var h = await OpenAsync();
        var session = h.Tab.EnsureEditor();

        session.ActiveMode = LayoutMode.Flow;
        session.ActiveMode = LayoutMode.Pages;

        Assert.Null(h.Tab.Fields);
        Assert.Equal(0, h.Clock.Created);
        Assert.False(h.Tab.IsDirty);
    }

    // The view says which page it shows last, for the layout of its mode; without a view there is no answer.
    [AvaloniaFact]
    public async Task TheViewTellsTheRefreshWhichPageIsInView()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-write-inview-").FullName;
        var path = System.IO.Path.Combine(folder, "long.docx");
        File.WriteAllBytes(path, FieldTestDocument.LongTocBytes());
        var view = new ViewHarness(path);
        var tab = view.Tab;
        try
        {
            tab.Runner.Start();
            await ViewHarness.WaitAsync(() => tab.Runner.PageLayout.IsComplete, 30000);
            ViewHarness.Pump();
            Assert.NotNull(tab.PageInView);
            Assert.Equal(0, tab.PageInView!(LayoutMode.Pages));

            view.View.ScrollToPage(6);
            ViewHarness.Pump();

            Assert.InRange(tab.PageInView!(LayoutMode.Pages), 6, 8);
            Assert.Equal(0, tab.PageInView!(LayoutMode.Flow));
        }
        finally
        {
            view.Dispose();
            Directory.Delete(folder, recursive: true);
        }

        Assert.Null(tab.PageInView);
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

        Assert.True(runner.Passes is >= 1 and <= FieldRefreshRunner.MaxPasses);
        Assert.False(runner.IsPending, "the save's settle ends the work in one call");
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
        await h.FireUntilIdleAsync();

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

        await h.FireUntilIdleAsync();

        Assert.Equal(0, runner.Failures);
        Assert.Equal(0, runner.Unsettled);
        Assert.InRange(runner.Passes, 1, FieldRefreshRunner.MaxPasses);
        await AssertLiveLayoutEqualsFreshAsync(h);

        // A second burst on the settled thesis (a letter that moves nothing): one pass, nothing written. The tick runs on the UI thread after the 250 ms delay, never in a key handler; it skips the page pass (nothing can have moved a page).
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
