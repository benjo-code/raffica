using Avalonia.Headless.XUnit;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.Write.Viewing;
using Xunit;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>
/// An outline operation lays out through the last page in view and never through the page of the old caret (follow-up of milestone 4.4a): the thesis (309 pages), the caret on the last
/// page, the view at the first, then a chapter move or a promote. The recorded row <c>structure/move-last-to-first-far-caret</c> cost the whole layout (about 500 ms) before the change.
/// The pages the command laid out are read when the session says it changed, which is before the background layout has done anything worth a page.
/// </summary>
public sealed class OutlineFarCaretTests
{
    // The command may lay out the pages in view and a few more (the window is a page and a half high); the whole document is 309 pages.
    private const int AtMost = 15;

    private sealed record Far(ViewHarness Harness, EditSession Session, IReadOnlyList<OutlineHeading> Headings, TextPosition Caret, double OffsetBefore) : IDisposable
    {
        public PageView View => Harness.View;

        public int LaidOutAtChange { get; set; } = -1;

        public IReadOnlyList<OutlineHeading> Chapters => [.. Headings.Where(h => h.Entry.Level == 1)];

        public void Dispose() => Harness.Dispose();
    }

    // The thesis laid out completely, the caret on the last paragraph with text, the view at the first page.
    private static async Task<Far> OpenFarAsync(bool viewAtFirstHeading = false)
    {
        var harness = new ViewHarness(WindowFixture.Thesis, 900, 700);
        harness.View.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        await harness.LayOutEverythingAsync();
        Assert.True(harness.View.Runner.PageLayout.LaidOutPages >= 100);

        var model = harness.Tab.Docx.Model;
        var session = harness.Tab.EnsureEditor();
        _ = session.Selection;
        var last = Enumerable.Range(0, model.Body.Blocks.Count).Last(i => model.Body.Blocks[i] is Paragraph { SectionBreak: null } p && ParagraphProjection.Build(p, ProjectionHost.Plain).Text.Length > 0);
        var caret = new TextPosition(StoryRef.Body, (Paragraph)model.Body.Blocks[last], 0);
        session.SetSelection(new TextRange(caret, caret));
        harness.View.Focus();
        ViewHarness.Pump();
        harness.View.EnsureCaretVisible();
        ViewHarness.Pump();

        // The scene of the measurement: the caret is far below the view, the view is at the top.
        harness.View.ScrollToBlock(viewAtFirstHeading ? OutlineSource.ReadHeadings(model)[0].Entry.BodyBlockIndex : 0);
        ViewHarness.Pump();
        Assert.True(harness.View.CaretBounds(caret) is { } bar && bar.Top > 10 * 700, "the caret is not far below the view");

        var far = new Far(harness, session, OutlineSource.ReadHeadings(model), caret, harness.View.OffsetY);
        session.Changed += () => far.LaidOutAtChange = harness.View.Runner.PageLayout.LaidOutPages;
        return far;
    }

    private static string Text(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    // Every page of the finished layout reads as the page of a layout made from scratch over the same model.
    private static async Task AssertEqualToFreshAsync(Far far)
    {
        await far.Harness.LayOutEverythingAsync();
        var tab = far.Harness.Tab;
        var layout = tab.Runner.PageLayout;
        var fresh = DocumentLayout.Create(tab.Docx.Model, tab.Fonts, new LayoutOptions(LayoutMode.Pages, LayoutRunner.FlowTextWidth, tab.Images.Read));
        await fresh.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete && fresh.IsComplete);
        Assert.Equal(fresh.LaidOutPages, layout.LaidOutPages);
        for (var i = 0; i < fresh.LaidOutPages; i++)
        {
            Assert.Equal(fresh.Page(i).Text, layout.Page(i).Text);
        }
    }

    [AvaloniaFact]
    public async Task AMoveToTheTopWithTheCaretOnTheLastPageLaysOutThroughTheViewOnly()
    {
        using var far = await OpenFarAsync(viewAtFirstHeading: true);
        var chapters = far.Chapters;
        Assert.True(chapters.Count >= 2);
        var moved = Text(chapters[^1].Paragraph);

        Assert.True(far.View.RunOutlineDrop(chapters[^1].Paragraph, far.Headings[0].Paragraph, DropPlace.Before));

        Assert.True(far.LaidOutAtChange is >= 1 and <= AtMost, $"the command laid out {far.LaidOutAtChange} pages (the document has 309)");
        // The moved heading is at the top now, the caret is at its start and the view shows it.
        var caret = far.Session.Caret!.Value;
        Assert.Equal(moved, Text(caret.Paragraph));
        Assert.Equal(0, caret.Offset);
        var bar = far.View.CaretBounds(caret);
        Assert.NotNull(bar);
        Assert.InRange(bar.Value.Top, 0, 700);
        Assert.Equal(1, far.Session.Editor.History.UndoCount);
        await AssertEqualToFreshAsync(far);
    }

    [AvaloniaFact]
    public async Task AMoveToTheEndWithTheCaretFarAwayLaysOutThroughTheViewOnlyAndTheViewStays()
    {
        using var far = await OpenFarAsync();
        var chapters = far.Chapters;
        var moved = Text(chapters[0].Paragraph);

        Assert.True(far.View.RunOutlineDrop(chapters[0].Paragraph, far.Headings[^1].Paragraph, DropPlace.After));

        // Nobody looks at the end of the document: the command did not lay it out and the view did not go there.
        Assert.True(far.LaidOutAtChange is >= 1 and <= AtMost, $"the command laid out {far.LaidOutAtChange} pages (the document has 309)");
        Assert.Equal(far.OffsetBefore, far.View.OffsetY);
        var caret = far.Session.Caret!.Value;
        Assert.Equal(moved, Text(caret.Paragraph));

        // The pages come in the background and the caret can then be shown where the heading landed.
        await AssertEqualToFreshAsync(far);
        Assert.NotNull(far.View.CaretBounds(far.Session.Caret!.Value));
    }

    [AvaloniaFact]
    public async Task AMoveByTheKeysWithTheCaretOnTheLastPageLaysOutThroughTheViewOnly()
    {
        using var far = await OpenFarAsync(viewAtFirstHeading: true);
        var chapters = far.Chapters;
        var middle = chapters[1];
        far.View.OutlineParagraphSource = () => middle.Paragraph;

        Assert.True(far.View.RunStructureCommand(WriteCommandIds.MoveUp));

        Assert.True(far.LaidOutAtChange is >= 1 and <= AtMost, $"the command laid out {far.LaidOutAtChange} pages (the document has 309)");
        await AssertEqualToFreshAsync(far);
    }

    [AvaloniaFact]
    public async Task APromoteWithTheCaretOnTheLastPageKeepsTheCaretAndTheViewAndLaysOutThroughTheViewOnly()
    {
        using var far = await OpenFarAsync();
        var heading = far.Headings.First(h => h.Entry.Level == 2);
        far.View.OutlineParagraphSource = () => heading.Paragraph;

        Assert.True(far.View.RunStructureCommand(WriteCommandIds.Promote));

        Assert.True(far.LaidOutAtChange is >= 1 and <= AtMost, $"the command laid out {far.LaidOutAtChange} pages (the document has 309)");
        // A promote keeps the selection where it is and the view where it is.
        var caret = far.Session.Caret!.Value;
        Assert.Equal(Text(far.Caret.Paragraph), Text(caret.Paragraph));
        Assert.Equal(far.Caret.Offset, caret.Offset);
        Assert.Equal(far.OffsetBefore, far.View.OffsetY);
        Assert.Equal(1, far.Session.Editor.History.UndoCount);

        await AssertEqualToFreshAsync(far);
        Assert.NotNull(far.View.CaretBounds(far.Session.Caret!.Value));
    }

    [AvaloniaFact]
    public async Task AnOutlineMoveWithTheCaretInViewStillLaysOutThroughTheViewAndShowsTheResult()
    {
        using var far = await OpenFarAsync(viewAtFirstHeading: true);
        var chapters = far.Chapters;
        // The caret goes to the first heading, which is in view.
        var first = far.Headings[0];
        var start = new TextPosition(StoryRef.Body, first.Paragraph, 0);
        far.Session.SetSelection(new TextRange(start, start));
        ViewHarness.Pump();
        far.View.EnsureCaretVisible();
        far.LaidOutAtChange = -1;
        var middle = chapters[1];
        var text = Text(middle.Paragraph);
        far.View.OutlineParagraphSource = () => middle.Paragraph;

        Assert.True(far.View.RunStructureCommand(WriteCommandIds.MoveUp));

        Assert.True(far.LaidOutAtChange is >= 1 and <= AtMost, $"the command laid out {far.LaidOutAtChange} pages (the document has 309)");
        Assert.Equal(text, Text(far.Session.Caret!.Value.Paragraph));
        await AssertEqualToFreshAsync(far);
    }

    [AvaloniaFact]
    public async Task AMoveQueuedBehindASaveLaysOutThroughTheViewOnlyWhenItIsReplayed()
    {
        using var far = await OpenFarAsync(viewAtFirstHeading: true);
        var chapters = far.Chapters;
        var moved = Text(chapters[^1].Paragraph);

        using (far.Session.BeginSave())
        {
            // The operation waits in the queue with the page it is to lay out through (the last page in view when it was asked for).
            Assert.True(far.View.RunOutlineDrop(chapters[^1].Paragraph, far.Headings[0].Paragraph, DropPlace.Before));
            Assert.Equal(1, far.Session.PendingCount);
            Assert.Equal(-1, far.LaidOutAtChange);
        }

        Assert.Equal(0, far.Session.PendingCount);
        Assert.True(far.LaidOutAtChange is >= 1 and <= AtMost, $"the replay laid out {far.LaidOutAtChange} pages (the document has 309)");
        Assert.Equal(moved, Text(far.Session.Caret!.Value.Paragraph));
        await AssertEqualToFreshAsync(far);
    }
}
