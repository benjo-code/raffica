using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class SelectToolTests
{
    private static readonly AnnotationColor Red = new(220, 38, 38);
    private static readonly AnnotationColor Blue = new(30, 80, 220);

    private static Point At(AnnotateFixture f, double x, double y) => f.View.ToView(0, x, y)!.Value;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<IReadOnlyList<PdfAnnotation>> AllAsync(AnnotateFixture f, int page = 0) =>
        (await f.Tab.Session.Document!.GetAnnotationsAsync(page, Cancel))
            .Where(a => a.Kind is AnnotationKind.Highlight or AnnotationKind.Ink or AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.TextBox)
            .ToList();

    // Lets everything that was started finish: queued loads and commits, posted callbacks, the dirty state.
    private static async Task SettleAsync(AnnotateFixture f)
    {
        for (var i = 0; i < 3; i++)
        {
            ViewFixture.Pump();
            await f.Interaction.Select.Completion;
            await f.SettleAsync();
        }
    }

    private static async Task<AnnotateFixture> OpenSelectAsync(string file = "text-rich.pdf")
    {
        var f = await AnnotateFixture.OpenAsync(file);
        f.Tools.Choose(AnnotateTool.Select);
        await SettleAsync(f);
        return f;
    }

    private static async Task<PdfAnnotation> AddRectangleAsync(AnnotateFixture f, PageRect box, double width = 2)
    {
        var made = await f.Tab.Annotations.Editor.ApplyDrawingAsync(0, DrawingGeometry.Rectangle(box, width), Red, null, null);
        await SettleAsync(f);
        return made!;
    }

    private static void Drag(AnnotateFixture f, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        f.Window.MouseDown(from, MouseButton.Left, modifiers);
        for (var i = 1; i <= 4; i++)
        {
            f.Window.MouseMove(new Point(from.X + ((to.X - from.X) * i / 4), from.Y + ((to.Y - from.Y) * i / 4)), modifiers);
        }

        f.Window.MouseUp(to, MouseButton.Left, modifiers);
        ViewFixture.Pump();
    }

    private static KeyEventArgs KeyOf(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };

    private static bool Send(AnnotateFixture f, Key key, KeyModifiers modifiers = KeyModifiers.None) => f.Interaction.HandleKey(KeyOf(key, modifiers));

    // Holds the first read of the marks of a page at a gate and records every read that is asked for after it.
    private static (List<int> Reads, TaskCompletionSource Gate) HoldReads(AnnotateFixture f)
    {
        var reads = new List<int>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Interaction.Select.ReadPage = async (document, page) =>
        {
            reads.Add(page);
            if (reads.Count == 1)
            {
                await gate.Task;
            }

            return await document.GetAnnotationsAsync(page, Cancel);
        };
        return (reads, gate);
    }

    [AvaloniaFact]
    public async Task APageThatScrolledOutOfViewIsNotReadWhenItsTurnComes()
    {
        using var f = await AnnotateFixture.OpenAsync("pages-marked.pdf");
        var (reads, gate) = HoldReads(f);
        f.Tools.Choose(AnnotateTool.Select);
        ViewFixture.Pump();
        var (firstShown, lastShown) = f.View.VisiblePages;
        var initial = Enumerable.Range(firstShown, lastShown - firstShown + 1).ToList();

        // A fast scroll: the view passes pages 2 and 3 on its way to page 5, while the first read is still going.
        var passed = new List<int>();
        foreach (var target in new[] { 2, 3, 5 })
        {
            f.View.GoToPage(target);
            ViewFixture.Pump();
            var (a, b) = f.View.VisiblePages;
            passed.AddRange(Enumerable.Range(a, b - a + 1));
        }

        var (first, last) = f.View.VisiblePages;
        var finalPages = Enumerable.Range(first, last - first + 1).ToList();
        var gone = passed.Distinct().Where(p => !finalPages.Contains(p) && !initial.Contains(p)).ToList();
        Assert.NotEmpty(gone);

        gate.SetResult();
        await SettleAsync(f);

        // Only the read that had started, and the pages that are in view now, were read; the pages on the way were skipped.
        Assert.All(reads, page => Assert.True(initial.Contains(page) || finalPages.Contains(page), $"page {page} was read though it is gone"));
        Assert.Empty(reads.Intersect(gone));
        Assert.All(finalPages, page => Assert.Contains(page, reads));
    }

    [AvaloniaFact]
    public async Task AViewThatChangesOftenQueuesAPageOnce()
    {
        using var f = await AnnotateFixture.OpenAsync("pages-marked.pdf");
        var (reads, gate) = HoldReads(f);
        f.Tools.Choose(AnnotateTool.Select);
        ViewFixture.Pump();

        // The view changes again and again while the chain is held: each page is queued once, not once per change.
        for (var i = 0; i < 6; i++)
        {
            f.View.GoToPage(i % 2 == 0 ? 4 : 5);
            ViewFixture.Pump();
        }

        f.View.GoToPage(4);
        ViewFixture.Pump();
        gate.SetResult();
        await SettleAsync(f);

        Assert.Equal(reads.Count, reads.Distinct().Count());
    }

    [AvaloniaFact]
    public async Task PicksTheTopmostEditableAnnotation()
    {
        using var f = await OpenSelectAsync();
        await f.Tab.Annotations.Editor.ApplyMarkupAsync(0, AnnotationKind.Highlight, [new PageRect(90, 95, 310, 115)], Red, null, null);
        var line = await f.Tab.Annotations.Editor.ApplyDrawingAsync(0, DrawingGeometry.Line(new DisplayPoint(100, 105), new DisplayPoint(300, 105), 2), Blue, null, null);
        await SettleAsync(f);

        // The drawing lies over the highlight where both are hit.
        f.Click(At(f, 200, 105));
        await SettleAsync(f);
        Assert.Equal(line!.Name, f.Interaction.Select.Selected!.Name);

        // Away from the line, inside the highlight: the highlight. A click on nothing clears the selection.
        f.Click(At(f, 200, 112));
        await SettleAsync(f);
        Assert.Equal(AnnotationKind.Highlight, f.Interaction.Select.Selected!.Kind);
        f.Click(At(f, 380, 380));
        await SettleAsync(f);
        Assert.False(f.Interaction.Select.HasSelection);
    }

    [AvaloniaFact]
    public async Task RectangleHitsOnTheOutlineOnly()
    {
        using var f = await OpenSelectAsync();
        await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));

        f.Click(At(f, 200, 250));
        await SettleAsync(f);
        Assert.False(f.Interaction.Select.HasSelection);

        f.Click(At(f, 200, 201));
        await SettleAsync(f);
        Assert.Equal(AnnotationKind.Rectangle, f.Interaction.Select.Selected!.Kind);
    }

    [AvaloniaFact]
    public async Task ADragThatEndsWhereItStartedMakesNoUndoStep()
    {
        using var f = await OpenSelectAsync();
        var rect = await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));

        // Out and back: the pointer went far enough to count as a drag, but the mark is where it was.
        var start = At(f, 200, 201);
        f.Window.MouseDown(start, MouseButton.Left);
        f.Window.MouseMove(At(f, 260, 241));
        f.Window.MouseMove(start);
        f.Window.MouseUp(start, MouseButton.Left);
        ViewFixture.Pump();
        await SettleAsync(f);

        // A single undo takes the rectangle away: no step was made for the drag.
        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        Assert.Empty(await AllAsync(f));
        Assert.NotNull(rect);
    }

    [AvaloniaFact]
    public async Task ADragThatIsClampedToTheSameBoxMakesNoUndoStep()
    {
        using var f = await OpenSelectAsync();
        await AddRectangleAsync(f, new PageRect(0, 200, 100, 300));

        // The mark already touches the left edge of the page: moving it further left changes nothing.
        Drag(f, At(f, 50, 201), At(f, -40, 201));
        await SettleAsync(f);
        var kept = Assert.Single(await AllAsync(f));
        Assert.Equal(0, kept.Drawing!.Box.Left, 0.5);

        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        Assert.Empty(await AllAsync(f));
    }

    [AvaloniaFact]
    public async Task DragMovesAsOneUndoStep()
    {
        using var f = await OpenSelectAsync();
        var rect = await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));

        Drag(f, At(f, 200, 201), At(f, 240, 231));
        await SettleAsync(f);

        var moved = Assert.Single(await AllAsync(f));
        Assert.Equal(rect.Name, moved.Name);
        Assert.Equal(140, moved.Drawing!.Box.Left, 1.5);
        Assert.Equal(230, moved.Drawing!.Box.Top, 1.5);
        Assert.Equal(200, moved.Drawing!.Box.Width, 0.5);
        Assert.Equal(rect.Name, f.Interaction.Select.Selected!.Name);
        Assert.True(f.Tab.IsDirty);

        // One drag is one history step: a single undo puts the rectangle back.
        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        var back = Assert.Single(await AllAsync(f));
        Assert.Equal(100, back.Drawing!.Box.Left, 0.5);
        Assert.Equal(200, back.Drawing!.Box.Top, 0.5);
    }

    [AvaloniaFact]
    public async Task ResizeKeepsMinimumSizeAndStaysOnThePage()
    {
        using var f = await OpenSelectAsync();
        await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));
        f.Click(At(f, 200, 201));
        await SettleAsync(f);
        var size = f.View.SizeOfPage(0)!.Value;

        // The bottom right handle dragged far beyond the page stops at the page edge.
        Drag(f, At(f, 300, 300), At(f, size.Width + 300, size.Height + 300));
        await SettleAsync(f);
        var grown = Assert.Single(await AllAsync(f)).Drawing!.Box;
        Assert.Equal(size.Width, grown.Right, 1);
        Assert.Equal(size.Height, grown.Bottom, 1);
        Assert.Equal(100, grown.Left, 1);

        // The top left handle dragged past the opposite corner leaves the minimum size (4 pt) behind, not a mirrored box.
        Drag(f, At(f, 100, 200), At(f, size.Width + 50, size.Height + 50));
        await SettleAsync(f);
        var shrunk = Assert.Single(await AllAsync(f)).Drawing!.Box;
        Assert.InRange(shrunk.Width, DrawingTransform.MinimumSize - 0.6, DrawingTransform.MinimumSize + 1);
        Assert.InRange(shrunk.Height, DrawingTransform.MinimumSize - 0.6, DrawingTransform.MinimumSize + 1);
        Assert.True(shrunk.Right <= size.Width + 0.5 && shrunk.Bottom <= size.Height + 0.5);
    }

    [AvaloniaFact]
    public async Task NudgesWithin400msAreOneUndoStep()
    {
        using var f = await OpenSelectAsync();
        await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));
        f.Click(At(f, 200, 201));
        await SettleAsync(f);

        Assert.True(Send(f, Key.Right));
        f.Time.Advance(100);
        Assert.True(Send(f, Key.Right));
        f.Time.Advance(100);
        Assert.True(Send(f, Key.Right));
        Assert.True(f.Tab.IsDirty);
        f.Time.Advance(500);
        await SettleAsync(f);
        Assert.Equal(103, Assert.Single(await AllAsync(f)).Drawing!.Box.Left, 0.5);

        // A key after the pause is the next step; Shift moves 10 points.
        Assert.True(Send(f, Key.Down, KeyModifiers.Shift));
        f.Time.Advance(500);
        await SettleAsync(f);
        Assert.Equal(210, Assert.Single(await AllAsync(f)).Drawing!.Box.Top, 0.5);

        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        var one = Assert.Single(await AllAsync(f)).Drawing!.Box;
        Assert.Equal(103, one.Left, 0.5);
        Assert.Equal(200, one.Top, 0.5);
        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        Assert.Equal(100, Assert.Single(await AllAsync(f)).Drawing!.Box.Left, 0.5);
    }

    [AvaloniaFact]
    public async Task ColourAndWidthChangeIsOneUndoStep()
    {
        using var f = await OpenSelectAsync();
        await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));
        f.Click(At(f, 200, 201));
        await SettleAsync(f);

        await f.Interaction.Select.SetSelectionStyleAsync(Blue, 4);
        await SettleAsync(f);
        var changed = Assert.Single(await AllAsync(f));
        Assert.Equal(Blue, changed.Color);
        Assert.Equal(4, changed.Drawing!.Width, 0.01);
        Assert.Equal(100, changed.Drawing!.Box.Left, 0.5);

        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        var back = Assert.Single(await AllAsync(f));
        Assert.Equal(Red, back.Color);
        Assert.Equal(2, back.Drawing!.Width, 0.01);
        Assert.Equal(back.Name, f.Interaction.Select.Selected!.Name);
    }

    [AvaloniaFact]
    public async Task DeleteThenUndoRestoresTheSameName()
    {
        using var f = await OpenSelectAsync();
        var rect = await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));
        f.Click(At(f, 200, 201));
        await SettleAsync(f);

        Assert.True(Send(f, Key.Delete));
        Assert.False(f.Interaction.Select.HasSelection);
        await SettleAsync(f);
        Assert.Empty(await AllAsync(f));

        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        Assert.Equal(rect.Name, Assert.Single(await AllAsync(f)).Name);

        // Backspace deletes as well.
        f.Click(At(f, 200, 201));
        await SettleAsync(f);
        Assert.True(Send(f, Key.Back));
        await SettleAsync(f);
        Assert.Empty(await AllAsync(f));
    }

    [AvaloniaFact]
    public async Task TabLeavesTheViewAfterTheLastAnnotation()
    {
        using var f = await OpenSelectAsync();
        var lower = await AddRectangleAsync(f, new PageRect(100, 300, 200, 350));
        var upper = await AddRectangleAsync(f, new PageRect(300, 100, 380, 150));

        // Reading order: top first, whatever the index.
        Assert.True(Send(f, Key.Tab));
        Assert.Equal(upper.Name, f.Interaction.Select.Selected!.Name);
        Assert.True(Send(f, Key.Tab));
        Assert.Equal(lower.Name, f.Interaction.Select.Selected!.Name);

        // After the last one the selection clears and the key is left unhandled: the focus moves on.
        var leave = KeyOf(Key.Tab);
        Assert.False(f.Interaction.HandleKey(leave));
        Assert.False(leave.Handled);
        Assert.False(f.Interaction.Select.HasSelection);

        // Shift+Tab walks back and leaves at the front.
        Assert.True(Send(f, Key.Tab, KeyModifiers.Shift));
        Assert.Equal(lower.Name, f.Interaction.Select.Selected!.Name);
        Assert.True(Send(f, Key.Tab, KeyModifiers.Shift));
        Assert.Equal(upper.Name, f.Interaction.Select.Selected!.Name);
        Assert.False(Send(f, Key.Tab, KeyModifiers.Shift));
        Assert.False(f.Interaction.Select.HasSelection);
    }

    [AvaloniaFact]
    public async Task LockedDrawingOffersOnlyCommentAndDelete()
    {
        using var f = await OpenSelectAsync("drawings-foreign.pdf");
        var locked = (await AllAsync(f)).Where(a => a.Drawing is not null && !a.GeometryEditable && a.Editable).ToList();
        Assert.NotEmpty(locked);

        // Tab until a locked drawing is selected.
        var guard = 0;
        while (f.Interaction.Select.Selected is not { GeometryEditable: false } && guard++ < 20)
        {
            Send(f, Key.Tab);
        }

        var selected = f.Interaction.Select.Selected!;
        Assert.False(selected.GeometryEditable);
        Assert.Equal(PdfStrings.SelectLockedReason, f.Interaction.Select.LockReason);
        Assert.Equal(0, f.Interaction.Select.HandleCount);

        // No nudge, no recolour, no move: nothing enters the history.
        Assert.False(Send(f, Key.Right));
        await f.Interaction.Select.SetSelectionStyleAsync(Blue, 4);
        await SettleAsync(f);
        Assert.False(f.Tab.Annotations.CanUndo);
        Assert.False(f.Tab.IsDirty);

        // Delete works.
        Assert.True(Send(f, Key.Delete));
        await SettleAsync(f);
        Assert.True(f.Tab.Annotations.CanUndo);
        Assert.DoesNotContain(await AllAsync(f), a => a.Name == selected.Name && a.Index == selected.Index && a.Kind == selected.Kind);
    }

    [AvaloniaFact]
    public async Task TextBoxIsPickable()
    {
        using var f = await OpenSelectAsync();
        var box = new PageRect(100, 100, 260, 160);
        var made = await f.Tab.Annotations.Editor.ApplyTextBoxAsync(0, new TextBoxSpec(box, "Hello", 12, Blue), null);
        await SettleAsync(f);

        // Inside the box, away from any outline.
        f.Click(At(f, 180, 130));
        await SettleAsync(f);
        Assert.Equal(made!.Name, f.Interaction.Select.Selected!.Name);
        Assert.Equal(AnnotationKind.TextBox, f.Interaction.Select.Selected!.Kind);

        // A drag moves the box as one step.
        Drag(f, At(f, 200, 145), At(f, 240, 165));
        await SettleAsync(f);
        var moved = Assert.Single(await AllAsync(f)).TextBox!.Box;
        Assert.Equal(140, moved.Left, 1.5);
        Assert.Equal(120, moved.Top, 1.5);
        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        Assert.Equal(100, Assert.Single(await AllAsync(f)).TextBox!.Box.Left, 0.5);

        // Enter opens the field on an own text box.
        Assert.True(Send(f, Key.Enter));
        await ViewFixture.WaitAsync(() => f.Interaction.TextBox.IsEditing);
        Assert.True(f.Interaction.TextBox.IsEditing);
    }

    [AvaloniaFact]
    public async Task SelectionSurvivesUndoByName()
    {
        using var f = await OpenSelectAsync();
        var rect = await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));
        f.Click(At(f, 200, 201));
        await SettleAsync(f);
        Assert.True(Send(f, Key.Left, KeyModifiers.Shift));
        f.Time.Advance(500);
        await SettleAsync(f);
        Assert.Equal(90, f.Interaction.Select.Selected!.Drawing!.Box.Left, 0.5);

        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        Assert.Equal(rect.Name, f.Interaction.Select.Selected!.Name);
        Assert.Equal(100, f.Interaction.Select.Selected!.Drawing!.Box.Left, 0.5);

        await f.Tab.Annotations.RedoAsync();
        await SettleAsync(f);
        Assert.Equal(rect.Name, f.Interaction.Select.Selected!.Name);
        Assert.Equal(90, f.Interaction.Select.Selected!.Drawing!.Box.Left, 0.5);
    }

    [AvaloniaFact]
    public async Task UndoAndRedoShareOneHistoryAcrossAllKinds()
    {
        using var f = await OpenSelectAsync();
        var editor = f.Tab.Annotations.Editor;
        var markup = await editor.ApplyMarkupAsync(0, AnnotationKind.Highlight, [new PageRect(90, 40, 310, 60)], Red, null, null);
        var rect = await editor.ApplyDrawingAsync(0, DrawingGeometry.Rectangle(new PageRect(100, 200, 300, 300), 2), Red, null, null);
        var box = await editor.ApplyTextBoxAsync(0, new TextBoxSpec(new PageRect(100, 100, 260, 160), "Hi", 12, Blue), null);
        await SettleAsync(f);

        // Move the rectangle, recolour the markup, delete the text box: three more steps through the select tool.
        Drag(f, At(f, 200, 201), At(f, 230, 201));
        await SettleAsync(f);
        f.Click(At(f, 150, 50));
        await SettleAsync(f);
        Assert.Equal(markup!.Name, f.Interaction.Select.Selected!.Name);
        await f.Interaction.Select.SetSelectionStyleAsync(Blue, null);
        await SettleAsync(f);
        f.Click(At(f, 180, 140));
        await SettleAsync(f);
        Assert.Equal(box!.Name, f.Interaction.Select.Selected!.Name);
        Assert.True(Send(f, Key.Delete));
        await SettleAsync(f);
        Assert.Equal(2, (await AllAsync(f)).Count);

        for (var i = 0; i < 6; i++)
        {
            Assert.True(f.Tab.Annotations.CanUndo);
            await f.Tab.Annotations.UndoAsync();
            await SettleAsync(f);
        }

        Assert.False(f.Tab.Annotations.CanUndo);
        Assert.Empty(await AllAsync(f));

        for (var i = 0; i < 6; i++)
        {
            Assert.True(f.Tab.Annotations.CanRedo);
            await f.Tab.Annotations.RedoAsync();
            await SettleAsync(f);
        }

        var after = await AllAsync(f);
        Assert.Equal(2, after.Count);
        Assert.Equal(Blue, after.Single(a => a.Kind == AnnotationKind.Highlight).Color);
        Assert.Equal(130, after.Single(a => a.Kind == AnnotationKind.Rectangle).Drawing!.Box.Left, 1.5);
        Assert.Equal(rect!.Name, after.Single(a => a.Kind == AnnotationKind.Rectangle).Name);
    }

    [AvaloniaFact]
    public async Task StyleChangeRightAfterANudgeKeepsTheNudgedPosition()
    {
        // A drawing: the width change comes while the nudges are still waiting for their pause.
        using (var f = await OpenSelectAsync())
        {
            await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));
            f.Click(At(f, 200, 201));
            await SettleAsync(f);
            Assert.True(Send(f, Key.Right));
            Assert.True(Send(f, Key.Right));
            await f.Interaction.Select.SetSelectionStyleAsync(null, 4);
            await SettleAsync(f);
            var drawing = Assert.Single(await AllAsync(f));
            Assert.Equal(4, drawing.Drawing!.Width, 0.01);
            Assert.Equal(102, drawing.Drawing!.Box.Left, 0.5);
        }

        // A text box: the colour change must not put the box back.
        using (var f = await OpenSelectAsync())
        {
            await f.Tab.Annotations.Editor.ApplyTextBoxAsync(0, new TextBoxSpec(new PageRect(100, 100, 260, 160), "Hello", 12, Blue), null);
            await SettleAsync(f);
            f.Click(At(f, 180, 130));
            await SettleAsync(f);
            Assert.True(Send(f, Key.Down, KeyModifiers.Shift));
            await f.Interaction.Select.SetSelectionStyleAsync(Red, null);
            await SettleAsync(f);
            var box = Assert.Single(await AllAsync(f)).TextBox!;
            Assert.Equal(Red, box.Color);
            Assert.Equal(110, box.Box.Top, 0.5);
        }
    }

    [AvaloniaFact]
    public async Task DeleteRightAfterANudgeDeletesTheMarkAndLeavesNoPendingHandle()
    {
        using var f = await OpenSelectAsync();
        var rect = await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));
        f.Click(At(f, 200, 201));
        await SettleAsync(f);
        Assert.True(Send(f, Key.Right));
        Assert.True(Send(f, Key.Delete));
        f.Time.Advance(600);
        await SettleAsync(f);
        Assert.Empty(await AllAsync(f));

        // No handle is left behind: a flush (which waits for them) ends.
        await f.Tab.Annotations.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5), Cancel);

        // The nudge and the delete are two steps; undoing the delete brings the mark back at its nudged place.
        await f.Tab.Annotations.UndoAsync();
        await SettleAsync(f);
        var back = Assert.Single(await AllAsync(f));
        Assert.Equal(rect.Name, back.Name);
        Assert.Equal(101, back.Drawing!.Box.Left, 0.5);
    }

    [AvaloniaFact]
    public async Task PressRightAfterANudgeDragsTheNudgedPlace()
    {
        using var f = await OpenSelectAsync();
        await AddRectangleAsync(f, new PageRect(100, 200, 300, 300));
        f.Click(At(f, 200, 201));
        await SettleAsync(f);
        Assert.True(Send(f, Key.Right, KeyModifiers.Shift));
        Assert.True(Send(f, Key.Right, KeyModifiers.Shift));

        // The press lands on the new bottom edge (120 .. 320), beyond the old one, and the drag moves from there, not from the old place.
        Drag(f, At(f, 308, 300), At(f, 308, 320));
        await SettleAsync(f);
        var box = Assert.Single(await AllAsync(f)).Drawing!.Box;
        Assert.Equal(120, box.Left, 1.5);
        Assert.Equal(220, box.Top, 1.5);
    }

    [AvaloniaFact]
    public async Task TabInsideTheOpenTextBoxFieldDoesNotStepTheSelection()
    {
        using var f = await OpenSelectAsync();
        await AddRectangleAsync(f, new PageRect(300, 200, 380, 260));
        var made = await f.Tab.Annotations.Editor.ApplyTextBoxAsync(0, new TextBoxSpec(new PageRect(100, 100, 260, 160), "Hello", 12, Blue), null);
        await SettleAsync(f);
        f.Click(At(f, 180, 130));
        await SettleAsync(f);
        Assert.True(Send(f, Key.Enter));
        await ViewFixture.WaitAsync(() => f.Interaction.TextBox.IsEditing);

        // The field owns its keys: nothing steps, moves or deletes while it is open.
        Assert.False(Send(f, Key.Tab));
        Assert.False(Send(f, Key.Tab, KeyModifiers.Shift));
        Assert.False(Send(f, Key.Right));
        Assert.False(Send(f, Key.Delete));
        Assert.False(Send(f, Key.Back));
        Assert.Equal(made!.Name, f.Interaction.Select.Selected!.Name);
        f.Time.Advance(600);
        await SettleAsync(f);
        var box = Assert.Single(await AllAsync(f), a => a.Kind == AnnotationKind.TextBox);
        Assert.Equal(100, box.TextBox!.Box.Left, 0.5);
        Assert.Equal(2, (await AllAsync(f)).Count);
    }
}
