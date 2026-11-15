using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Shell;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Xunit;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The caret, the selection and the pointer rules of the page view (Task 15 of milestone 4.3a).</summary>
public sealed class PageViewEditingTests : IDisposable
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-pointer-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The system cleans the temp folder.
        }
    }

    // A harness whose whole model is loaded (the layout may still run in the background).
    private static async Task<ViewHarness> OpenWholeAsync(string path, double width = 900, double height = 600, WriteView mode = WriteView.Page)
    {
        var harness = new ViewHarness(path, width, height, mode);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        var layout = harness.View.Runner.PageLayout;
        await ViewHarness.WaitAsync(() => layout.IsComplete || layout.LaidOutPages >= 8, 30000);
        return harness;
    }

    private static double Scale(PageView view) => view.Zoom * 96 / 72;

    // The point of the view for a point of a page (points).
    private static Point ViewPoint(PageView view, int page, double x, double y)
    {
        var rect = view.PageBounds(page)!.Value;
        return new Point(rect.X + (x * Scale(view)), rect.Y + (y * Scale(view)));
    }

    private static void Click(ViewHarness h, Point point, int times = 1, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        for (var i = 0; i < times; i++)
        {
            h.Window.MouseDown(point, MouseButton.Left, modifiers);
            h.Window.MouseUp(point, MouseButton.Left, modifiers);
        }

        ViewHarness.Pump();
    }

    private static string TextOf(DocumentLayout layout, TextPosition position) => ParagraphProjection.Build(position.Paragraph, layout.ProjectionHost(position.Story, position.Paragraph)).Text;

    private static string SelectedText(PageView view, DocumentLayout layout)
    {
        var selection = view.Selection!.Value;
        Assert.Same(selection.Start.Paragraph, selection.End.Paragraph);
        var (a, b) = (Math.Min(selection.Start.Offset, selection.End.Offset), Math.Max(selection.Start.Offset, selection.End.Offset));
        return TextOf(layout, selection.Start)[a..b];
    }

    // Scrolls so that a position is in the middle of the view; the layout must show it.
    private static Rect Reveal(PageView view, TextPosition position)
    {
        var bounds = view.CaretBounds(position);
        Assert.NotNull(bounds);
        view.ScrollByWheel(-((bounds!.Value.Y - (view.Viewport.Height / 2)) / PageScroller.WheelStep));
        ViewHarness.Pump();
        bounds = view.CaretBounds(position);
        Assert.NotNull(bounds);
        return bounds!.Value;
    }

    private static Point Center(Rect bar) => new(bar.X + 1, bar.Y + (bar.Height / 2));

    private static Paragraph FirstLongParagraph(WordDocument model, DocumentLayout layout, int minLength, int skip = 0)
    {
        foreach (var block in model.Body.Blocks)
        {
            if (block is Paragraph paragraph && TextOf(layout, new TextPosition(StoryRef.Body, paragraph, 0)).Length >= minLength && skip-- <= 0)
            {
                return paragraph;
            }
        }

        throw new InvalidOperationException("no such paragraph");
    }

    [AvaloniaFact]
    public async Task ClickPlacesTheCaretWhereTheHitTestSays()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        Assert.Null(view.Caret);

        var point = ViewPoint(view, 0, 280, 250);
        Click(h, point);
        var scale = Scale(view);
        var rect = view.PageBounds(0)!.Value;
        var expected = view.Runner.PageLayout.HitTest(0, (float)((point.X - rect.X) / scale), (float)((point.Y - rect.Y) / scale));
        Assert.NotNull(expected);
        Assert.NotNull(view.Caret);
        Assert.Same(expected!.Value.Position.Paragraph, view.Caret!.Value.Paragraph);
        Assert.Equal(expected.Value.Position.Offset, view.Caret.Value.Offset);
        Assert.True(view.Selection!.Value.IsEmpty);

        // The bar is drawn at the caret.
        var bar = view.CaretBarBounds;
        Assert.NotNull(bar);
        Assert.InRange(Math.Abs(bar!.Value.X - point.X), 0, 40);
    }

    [AvaloniaFact]
    public async Task DragSelectsAndStopsAtTheCellEdge()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        var model = view.Tab.Docx.Model;

        // A table with two cells that hold text, side by side in the first row.
        TableCell[]? pair = null;
        var tableIndex = -1;
        for (var i = 0; i < model.Body.Blocks.Count && pair is null; i++)
        {
            if (model.Body.Blocks[i] is Table { Rows: [TableRow row, ..] })
            {
                var cells = row.Items.OfType<TableCell>().Where(c => c.Blocks.OfType<Paragraph>().Any(p => TextOf(layout, new TextPosition(StoryRef.Body, p, 0)).Length > 2)).Take(2).ToArray();
                if (cells.Length == 2)
                {
                    pair = cells;
                    tableIndex = i;
                }
            }
        }

        Assert.NotNull(pair);
        view.ScrollToBlock(tableIndex);
        await ViewHarness.WaitAsync(() => !view.IsWaitingForAnchor);
        var first = pair![0].Blocks.OfType<Paragraph>().First(p => TextOf(layout, new TextPosition(StoryRef.Body, p, 0)).Length > 2);
        var second = pair[1].Blocks.OfType<Paragraph>().First(p => TextOf(layout, new TextPosition(StoryRef.Body, p, 0)).Length > 2);
        var startPosition = new TextPosition(StoryRef.Body, first, 0);
        var other = new TextPosition(StoryRef.Body, second, 1);
        var from = Center(Reveal(view, startPosition));
        var to = Center(view.CaretBounds(other)!.Value);

        h.Window.MouseDown(from, MouseButton.Left);
        h.Window.MouseMove(to);
        h.Window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();

        var selection = view.Selection!.Value;
        Assert.Same(first, selection.Start.Paragraph);
        // The end is in the first cell, at the end of its last paragraph: the drag never entered the second cell.
        var lastInFirstCell = pair[0].Blocks.OfType<Paragraph>().Last();
        Assert.Contains(selection.End.Paragraph, pair[0].Blocks);
        Assert.Same(lastInFirstCell, selection.End.Paragraph);
        Assert.Equal(TextOf(layout, selection.End).Length, selection.End.Offset);
        Assert.False(view.IsDragging);
    }

    [AvaloniaFact]
    public async Task DoubleClickSelectsAWordTripleAParagraph()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        var paragraph = FirstLongParagraph(view.Tab.Docx.Model, layout, 120);
        var text = TextOf(layout, new TextPosition(StoryRef.Body, paragraph, 0));
        var inside = text.IndexOf(' ', 30) + 2;
        var position = new TextPosition(StoryRef.Body, paragraph, inside);
        var point = Center(Reveal(view, position));

        Click(h, point, 2);
        var word = SelectedText(view, layout);
        Assert.False(string.IsNullOrEmpty(word));
        Assert.DoesNotContain(' ', word);
        Assert.Contains(word, text, StringComparison.Ordinal);
        Assert.Same(paragraph, view.Selection!.Value.Start.Paragraph);

        Click(h, point, 3);
        Assert.Equal(text, SelectedText(view, layout));
    }

    [AvaloniaFact]
    public async Task ShiftClickExtends()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        var model = view.Tab.Docx.Model;
        var a = FirstLongParagraph(model, layout, 120);
        var b = FirstLongParagraph(model, layout, 120, skip: 1);
        var pa = new TextPosition(StoryRef.Body, a, 5);
        var pb = new TextPosition(StoryRef.Body, b, 5);

        // Both ends in view: the paragraphs are close in the thesis' front matter or the view is scrolled between the two clicks.
        Click(h, Center(Reveal(view, pa)));
        Assert.Same(a, view.Caret!.Value.Paragraph);
        var target = Center(Reveal(view, pb));
        Click(h, target, modifiers: RawInputModifiers.Shift);

        var selection = view.Selection!.Value;
        Assert.Same(a, selection.Start.Paragraph);
        Assert.Equal(5, selection.Start.Offset);
        Assert.Same(b, selection.End.Paragraph);
        Assert.False(selection.IsEmpty);
    }

    [AvaloniaFact]
    public async Task CaretIsKeptVisibleByScrolling()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        await ViewHarness.WaitAsync(() => layout.LaidOutPages > 5, 30000);
        Click(h, ViewPoint(view, 0, 280, 250));
        var session = view.Tab.Editor!;

        // A paragraph three pages down.
        Paragraph? far = null;
        foreach (var block in view.Tab.Docx.Model.Body.Blocks)
        {
            if (block is Paragraph p && layout.Caret(new TextPosition(StoryRef.Body, p, 0), 0) is { Page: >= 3 })
            {
                far = p;
                break;
            }
        }

        Assert.NotNull(far);
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, far!, 0), new TextPosition(StoryRef.Body, far!, 0)));
        var before = view.OffsetY;
        Assert.False(view.CaretBarBounds is { } off && off.Bottom <= view.Viewport.Height && off.Top >= 0);
        view.EnsureCaretVisible();
        Assert.True(view.OffsetY > before);
        var bar = view.CaretBarBounds!.Value;
        Assert.InRange(bar.Top, 0, view.Viewport.Height);
        Assert.InRange(bar.Bottom, 0, view.Viewport.Height);
    }

    [AvaloniaFact]
    public async Task RenderTouchesNoVisual()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        var paragraph = FirstLongParagraph(view.Tab.Docx.Model, layout, 120);
        Click(h, Center(Reveal(view, new TextPosition(StoryRef.Body, paragraph, 3))));
        Click(h, Center(view.CaretBarBounds!.Value), 2);
        Assert.False(view.Selection!.Value.IsEmpty);
        ViewHarness.Pump();

        await view.Runner.PageTask;
        ViewHarness.Pump();
        var scroller = view.Scroller;
        var children = view.GetVisualChildren().Count();
        var changes = 0;
        view.ViewChanged += () => changes++;
        var offset = view.OffsetY;
        h.Capture();
        h.Capture();
        view.TickBlink();
        h.Capture();

        // Drawing the pages, the selection and the caret read the layout and invalidate nothing: no change event, the same scroller, the same tree, a valid layout pass.
        Assert.Equal(0, changes);
        Assert.Same(scroller, view.Scroller);
        Assert.Equal(children, view.GetVisualChildren().Count());
        Assert.Equal(offset, view.OffsetY);
        Assert.True(view.IsMeasureValid && view.IsArrangeValid);
    }

    [AvaloniaFact]
    public async Task SwitchingViewKeepsTheCaretParagraph()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        var paragraph = FirstLongParagraph(view.Tab.Docx.Model, layout, 200, skip: 2);
        var position = new TextPosition(StoryRef.Body, paragraph, 77);
        Click(h, Center(Reveal(view, position)));
        view.Tab.Editor!.SetSelection(new TextRange(position, position));
        var anchor = view.CaretAnchor;
        Assert.NotNull(anchor);
        Assert.Equal(77, anchor!.Value.Character);
        Assert.Same(paragraph, view.Tab.Docx.Model.Body.Blocks[anchor.Value.Block]);

        view.Mode = WriteView.Writing;
        await ViewHarness.WaitAsync(() => !view.IsWaitingForAnchor && view.CaretBarBounds is { } bar && bar.Top >= 0 && bar.Bottom <= view.Viewport.Height, 30000);
        Assert.Equal(LayoutMode.Flow, view.Tab.Editor!.ActiveMode);
        Assert.Equal(anchor, view.CaretAnchor);
        Assert.Same(paragraph, view.Caret!.Value.Paragraph);
        Assert.Equal(77, view.Caret.Value.Offset);

        view.Mode = WriteView.Page;
        await ViewHarness.WaitAsync(() => !view.IsWaitingForAnchor && view.CaretBarBounds is { } bar2 && bar2.Top >= 0 && bar2.Bottom <= view.Viewport.Height, 30000);
        Assert.Equal(LayoutMode.Pages, view.Tab.Editor.ActiveMode);
        Assert.Equal(anchor, view.CaretAnchor);

        // An anchor with a character offset scrolls to the line of that character.
        view.ScrollToStart();
        view.ScrollToAnchor(anchor.Value);
        await ViewHarness.WaitAsync(() => !view.IsWaitingForAnchor, 30000);
        Assert.InRange(view.CaretBarBounds!.Value.Top, 0, view.Viewport.Height);
    }

    [AvaloniaFact]
    public async Task NoBlinkTimerBeforeTheFirstCaret()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        Assert.False(view.BlinkTimerCreated);
        Assert.Null(view.Tab.Editor);
        view.Focus();
        ViewHarness.Pump();
        Assert.False(view.BlinkTimerCreated);

        Click(h, ViewPoint(view, 0, 280, 250));
        Assert.True(view.BlinkTimerCreated);
        Assert.True(view.BlinkRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(530), PageView.BlinkInterval);
    }

    [AvaloniaFact]
    public async Task TheBlinkRestartsOnAMoveAndStopsWithoutFocusOrInHighContrast()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        Click(h, ViewPoint(view, 0, 280, 250));
        Assert.True(view.BlinkRunning);

        // A tick turns the caret off, a move makes it solid again.
        view.TickBlink();
        Assert.False(view.CaretShown);
        Click(h, ViewPoint(view, 0, 200, 200));
        Assert.True(view.CaretShown);

        // No focus, no timer; the focus is back, the timer runs again.
        TopLevel.GetTopLevel(view)!.FocusManager!.Focus(null);
        ViewHarness.Pump();
        Assert.False(view.IsFocused);
        Assert.False(view.BlinkRunning);
        Assert.True(view.CaretShown);
        view.Focus();
        ViewHarness.Pump();
        Assert.True(view.BlinkRunning);

        // High contrast: solid, no ticking at all; back to normal, it ticks again.
        h.Window.RequestedThemeVariant = ShellTheme.HighContrastVariant;
        ViewHarness.Pump();
        Assert.False(view.BlinkRunning);
        Assert.True(view.CaretShown);
        h.Window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        ViewHarness.Pump();
        Assert.True(view.BlinkRunning);

        // The app can ask for no blinking too.
        view.ReduceMotion = true;
        Assert.False(view.BlinkRunning);
        view.ReduceMotion = false;
        Assert.True(view.BlinkRunning);
    }

    [AvaloniaFact]
    public async Task AClickAtTheEndOfAWrappedLineIsUpstreamAndAClickAtTheStartOfTheNextIsDownstream()
    {
        var path = WriteDocument(string.Concat(Enumerable.Repeat("abcdefgh-", 60)), "Header text", "Footer text");
        using var h = await OpenWholeAsync(path);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        var paragraph = (Paragraph)view.Tab.Docx.Model.Body.Blocks[0];
        var start = new TextPosition(StoryRef.Body, paragraph, 0);
        var first = layout.Caret(start, 0)!.Value;

        // Past the right end of the first line (the caret of its end offset is the start of the second line).
        Click(h, ViewPoint(view, 0, 600, first.Top + (first.Height / 2)));
        var upstream = view.Caret!.Value;
        var dbg = layout.HitTest(0, 600, first.Top + (first.Height / 2));
        Assert.Equal(CaretAffinity.Upstream, view.Affinity);
        var bar = view.CaretBarBounds!.Value;
        Assert.InRange(bar.Y, view.PageBounds(0)!.Value.Y + (first.Top * Scale(view)) - 1, view.PageBounds(0)!.Value.Y + (first.Top * Scale(view)) + 1);

        // At the left edge of the second line: the same offset, downstream, one line lower.
        var second = layout.Caret(upstream, 0)!.Value;
        Click(h, ViewPoint(view, 0, 60, second.Top + (second.Height / 2)));
        Assert.Equal(upstream.Offset, view.Caret!.Value.Offset);
        Assert.Equal(CaretAffinity.Downstream, view.Affinity);
        Assert.True(view.CaretBarBounds!.Value.Y > bar.Y + 1);

        // A caret that the session moves on its own is downstream: the affinity belongs to the position it was set for.
        view.Tab.Editor!.SetSelection(new TextRange(upstream, upstream));
        Click(h, ViewPoint(view, 0, 600, first.Top + (first.Height / 2)));
        Assert.Equal(CaretAffinity.Upstream, view.Affinity);
        var moved = layout.Move(upstream, CaretMove.Right, null, 0)!.Value;
        view.Tab.Editor.SetSelection(new TextRange(moved, moved));
        Assert.Equal(CaretAffinity.Downstream, view.Affinity);
    }

    [AvaloniaFact]
    public async Task ADoubleClickInTheHeaderEntersItAndEscLeavesIt()
    {
        var path = WriteDocument("Body text of the first paragraph.", "Header text", "Footer text");
        using var h = await OpenWholeAsync(path, 900, 1400);
        var view = h.View;

        // Blank space of the header region (right of its text): a single click does nothing, a double click enters.
        var inHeader = ViewPoint(view, 0, 500, 41);
        Click(h, inHeader);
        Assert.Equal(StoryKind.Body, view.Caret!.Value.Story.Kind);
        Assert.Null(view.ActiveStory);

        // A body click first, so that Esc has a place to go back to.
        Click(h, ViewPoint(view, 0, 120, 100));
        Assert.Equal(StoryKind.Body, view.Caret!.Value.Story.Kind);
        var bodyCaret = view.Caret.Value;

        Click(h, inHeader, 2);
        Assert.Equal(StoryKind.HeaderFooter, view.Caret!.Value.Story.Kind);
        Assert.Equal(view.Caret.Value.Story, view.ActiveStory);

        // Esc leaves the header and brings the body caret back.
        h.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Null(view.ActiveStory);
        Assert.Equal(StoryKind.Body, view.Caret!.Value.Story.Kind);
        Assert.Same(bodyCaret.Paragraph, view.Caret.Value.Paragraph);

        // The footer too; a click in the body leaves it.
        var inFooter = ViewPoint(view, 0, 500, 792 - 41);
        Click(h, inFooter, 2);
        Assert.Equal(StoryKind.HeaderFooter, view.Caret!.Value.Story.Kind);
        Click(h, ViewPoint(view, 0, 120, 100));
        Assert.Null(view.ActiveStory);
        Assert.Equal(StoryKind.Body, view.Caret!.Value.Story.Kind);

        // A drag that starts in the header does not reach the body.
        Click(h, inHeader, 2);
        var headerStart = view.Caret!.Value;
        h.Window.MouseDown(ViewPoint(view, 0, 120, 41), MouseButton.Left);
        h.Window.MouseMove(ViewPoint(view, 0, 120, 100));
        h.Window.MouseUp(ViewPoint(view, 0, 120, 100), MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(StoryKind.HeaderFooter, view.Selection!.Value.End.Story.Kind);
        Assert.Equal(StoryKind.HeaderFooter, headerStart.Story.Kind);
    }

    [AvaloniaFact]
    public async Task TheWritingViewUsesTheFlowLayout()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis, mode: WriteView.Writing);
        var view = h.View;
        await ViewHarness.WaitAsync(() => view.Runner.FlowLayout is { LaidOutPages: > 0 }, 30000);
        var flow = view.Runner.FlowLayout!;
        Assert.NotSame(flow, view.Runner.PageLayout);

        var point = ViewPoint(view, 0, 200, 60);
        Click(h, point);
        Assert.Equal(LayoutMode.Flow, view.Tab.Editor!.ActiveMode);
        var rect = view.PageBounds(0)!.Value;
        var expected = flow.HitTest(0, (float)((point.X - rect.X) / Scale(view)), (float)((point.Y - rect.Y) / Scale(view)));
        Assert.NotNull(expected);
        Assert.Same(expected!.Value.Position.Paragraph, view.Caret!.Value.Paragraph);
        Assert.Equal(expected.Value.Position.Offset, view.Caret.Value.Offset);

        // The caret, a selection and the auto-scroll math all read the strips.
        Assert.NotNull(view.CaretBarBounds);
        Click(h, Center(view.CaretBarBounds!.Value), 2);
        Assert.False(view.Selection!.Value.IsEmpty);
    }

    [AvaloniaFact]
    public async Task ASelectionMadeDuringASaveIsQueuedInArrivalOrder()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        Click(h, ViewPoint(view, 0, 280, 250));
        var session = view.Tab.Editor!;
        var before = view.Selection!.Value;

        using (var save = session.BeginSave())
        {
            Click(h, ViewPoint(view, 0, 200, 150));
            // Queued: the caret has not moved yet.
            Assert.Equal(EditBlockedReason.Saving, session.BlockedReason);
            Assert.Equal(1, session.PendingCount);
            Assert.Equal(before, view.Selection);
            Click(h, ViewPoint(view, 0, 300, 200));
            Assert.Equal(2, session.PendingCount);
            Assert.Equal(before, view.Selection);
        }

        // The save ended: both ran in order, the last click is the caret.
        ViewHarness.Pump();
        Assert.Equal(0, session.PendingCount);
        var rect = view.PageBounds(0)!.Value;
        var point = ViewPoint(view, 0, 300, 200);
        var expected = view.Runner.PageLayout.HitTest(0, (float)((point.X - rect.X) / Scale(view)), (float)((point.Y - rect.Y) / Scale(view)));
        Assert.Same(expected!.Value.Position.Paragraph, view.Caret!.Value.Paragraph);
        Assert.Equal(expected.Value.Position.Offset, view.Caret.Value.Offset);
    }

    [AvaloniaFact]
    public void APressWhileOnlyAPrefixIsOpenChangesNothing()
    {
        using var h = new ViewHarness(WindowFixture.Thesis);
        var view = h.View;
        Assert.False(view.Runner.FullModelReady);
        Click(h, ViewPoint(view, 0, 280, 250));
        Assert.Null(view.Caret);
        Assert.Null(view.Tab.Editor);
        Assert.False(view.BlinkTimerCreated);

        // The view still scrolls and zooms.
        Assert.True(view.ScrollByWheel(-3));
    }

    [AvaloniaFact]
    public async Task AFaultedSessionShowsNoCaretAndTheViewKeepsWorking()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        Click(h, ViewPoint(view, 0, 280, 250));
        Assert.NotNull(view.Caret);

        view.Runner.FailEdit(new InvalidOperationException("layout failed"));
        ViewHarness.Pump();
        Assert.Null(view.Caret);
        Assert.Null(view.CaretBarBounds);
        Assert.False(view.BlinkRunning);
        h.Capture();

        // A press does nothing and does not throw; the wheel still works.
        Click(h, ViewPoint(view, 0, 200, 200));
        Assert.Null(view.Caret);
        Assert.True(view.ScrollByWheel(-1) || view.OffsetY >= 0);
    }

    [AvaloniaFact]
    public async Task ADragBelowTheViewScrollsOnTicksAndStopsOnRelease()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        await ViewHarness.WaitAsync(() => layout.LaidOutPages > 3, 30000);
        Click(h, ViewPoint(view, 0, 200, 200));
        Assert.True(view.Selection!.Value.IsEmpty);

        h.Window.MouseDown(ViewPoint(view, 0, 120, 300), MouseButton.Left);
        Assert.True(view.IsDragging);
        var anchor = view.Caret!.Value;
        h.Window.MouseMove(new Point(300, view.Viewport.Height + 80));
        ViewHarness.Pump();
        Assert.True(view.AutoScrolling);

        var offset = view.OffsetY;
        view.AutoScrollTick();
        view.AutoScrollTick();
        Assert.True(view.OffsetY > offset);
        Assert.Equal(anchor.Paragraph, view.Selection!.Value.Start.Paragraph);
        Assert.False(view.Selection.Value.IsEmpty);

        h.Window.MouseUp(new Point(300, view.Viewport.Height + 80), MouseButton.Left);
        ViewHarness.Pump();
        Assert.False(view.AutoScrolling);
        Assert.False(view.IsDragging);
        var stopped = view.OffsetY;
        view.AutoScrollTick();
        Assert.Equal(stopped, view.OffsetY);
    }

    [AvaloniaFact]
    public async Task ARightClickInsideTheSelectionKeepsItAndOutsideMovesTheCaret()
    {
        using var h = await OpenWholeAsync(WindowFixture.Thesis);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        var paragraph = FirstLongParagraph(view.Tab.Docx.Model, layout, 120);
        var point = Center(Reveal(view, new TextPosition(StoryRef.Body, paragraph, 40)));
        Click(h, point, 3);
        var selection = view.Selection!.Value;
        Assert.False(selection.IsEmpty);

        h.Window.MouseDown(point, MouseButton.Right);
        h.Window.MouseUp(point, MouseButton.Right);
        ViewHarness.Pump();
        Assert.Equal(selection, view.Selection);

        var outside = ViewPoint(view, view.CurrentPage, 100, 30);
        h.Window.MouseDown(outside, MouseButton.Right);
        h.Window.MouseUp(outside, MouseButton.Right);
        ViewHarness.Pump();
        Assert.True(view.Selection!.Value.IsEmpty);
    }

    // A one-paragraph document with a header and a footer.
    private string WriteDocument(string bodyText, string header, string footer)
    {
        var path = Path.Combine(_folder, "edit.docx");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Entry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
                  <Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
                </Types>
                """);
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>
                </Relationships>
                """);
            Entry(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:r><w:t>{bodyText}</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Second paragraph of the body.</w:t></w:r></w:p>
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rId1"/>
                      <w:footerReference w:type="default" r:id="rId2"/>
                      <w:pgSz w:w="12240" w:h="15840"/>
                      <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
                    </w:sectPr>
                  </w:body>
                </w:document>
                """);
            Entry(zip, "word/header1.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>{header}</w:t></w:r></w:p></w:hdr>
                """);
            Entry(zip, "word/footer1.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:r><w:t>{footer}</w:t></w:r></w:p></w:ftr>
                """);
        }

        return path;
    }

    private static void Entry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text.TrimStart());
    }
}
