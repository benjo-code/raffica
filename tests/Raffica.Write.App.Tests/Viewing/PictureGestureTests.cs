using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Xunit;
using static Raffica.WriteApp.Tests.Viewing.PageViewObjectTests;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>
/// The gestures on a picture (milestone 4.5a, Task 12): the eight handles, the drag of a floating picture to another paragraph, the arrows, Escape and a save. Every gesture changes the document once, on
/// release, as one history step. The document is <see cref="FloatingDocx"/>.
/// </summary>
public sealed class PictureGestureTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-picture-gestures-").FullName;

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

    private static PictureBox Floating(ViewHarness h) => h.View.Runner.PageLayout.Page(0).Objects.Pictures.Single(p => p.Floating);

    private static PictureBox Inline(ViewHarness h) => h.View.Runner.PageLayout.Page(0).Objects.Pictures.Single(p => !p.Floating);

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    private static int Revision(ViewHarness h) => h.View.Tab.Docx.Model.Edits.Revision;

    private static EditSession Session(ViewHarness h) => h.View.Tab.EnsureEditor();

    // A point inside the box that is not its centre: a press at the very point of the click that selected it would be a double click.
    private static Point Inside(PictureBox box, PageView view) => Center(box, view) + new Vector(12, 12);

    private static Point At(ViewHarness h, PictureBox box, HandleSlot slot)
    {
        var (x, y) = box.HandleCentre(slot);
        return ViewPoint(h.View, 0, x, y);
    }

    private static PictureInfo Info(ViewHarness h) => PictureView.Of(Session(h).SelectedDrawing!)!;

    private static void Move(ViewHarness h, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None, int steps = 6)
    {
        for (var i = 1; i <= steps; i++)
        {
            h.Window.MouseMove(new Point(from.X + ((to.X - from.X) * i / steps), from.Y + ((to.Y - from.Y) * i / steps)), modifiers);
        }

        ViewHarness.Pump();
    }

    private static void Drag(ViewHarness h, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        h.Window.MouseDown(from, MouseButton.Left, modifiers);
        Move(h, from, to, modifiers);
        h.Window.MouseUp(to, MouseButton.Left, modifiers);
        ViewHarness.Pump();
    }

    private async Task<ViewHarness> OpenFloatingAsync(bool aligned = false, string? positionVertical = null) =>
        await OpenAsync(FloatingDocx.Write(_folder, aligned: aligned, positionVertical: positionVertical));

    private static void Near(long expected, long actual, long tolerance) => Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected} within {tolerance}, was {actual}");

    private static double Ratio(PictureInfo info) => info.ExtentEmu.Width / (double)info.ExtentEmu.Height;

    // ---- resize ----------------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CornerDragKeepsTheRatioAndShiftFreesIt()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        Click(h, Center(Inline(h), view));
        var session = Session(h);
        Assert.IsType<PictureSelection>(session.ObjectSelection);
        var start = Info(h);
        Assert.Equal(2.0, Ratio(start), 3);
        var steps = session.Editor.History.UndoCount;
        var revision = Revision(h);

        // The bottom right corner, dragged by 60 across and 5 down: the side that moved more decides and the proportions stay.
        var from = At(h, Inline(h), HandleSlot.BottomRight);
        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, new Point(from.X + 60, from.Y + 5));
        Assert.Equal("PictureResize", view.ObjectGesture?.Kind?.GetType().Name);
        Assert.Equal(revision, Revision(h));
        h.Window.MouseUp(new Point(from.X + 60, from.Y + 5), MouseButton.Left);
        ViewHarness.Pump();

        var grown = Info(h);
        Assert.Equal(2.0, Ratio(grown), 2);
        Near(start.ExtentEmu.Width + (long)Math.Round(60 / Scale(view) * 12700), grown.ExtentEmu.Width, 12700);
        Assert.Equal(revision + 1, Revision(h));
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        Assert.IsType<PictureSelection>(session.ObjectSelection);

        // Shift frees the proportions.
        var corner = At(h, Inline(h), HandleSlot.BottomRight);
        Drag(h, corner, new Point(corner.X + 30, corner.Y + 30), RawInputModifiers.Shift);
        var free = Info(h);
        Assert.NotEqual(2.0, Ratio(free), 2);
        Near(grown.ExtentEmu.Width + (long)Math.Round(30 / Scale(view) * 12700), free.ExtentEmu.Width, 12700);
        Near(grown.ExtentEmu.Height + (long)Math.Round(30 / Scale(view) * 12700), free.ExtentEmu.Height, 12700);
        Assert.Equal(steps + 2, session.Editor.History.UndoCount);

        Assert.NotNull(session.Editor.History.Undo());
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Equal(start.ExtentEmu, PictureView.Of(((Run)P(h, 4).Inlines[0]).Content.OfType<Picture>().Single())!.ExtentEmu);
    }

    [AvaloniaFact]
    public async Task AnEdgeHandleChangesOneSideOnly()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        Click(h, Center(Inline(h), view));
        var start = Info(h);
        var from = At(h, Inline(h), HandleSlot.Right);

        Drag(h, from, new Point(from.X + 40, from.Y + 25));

        var after = Info(h);
        Assert.Equal(start.ExtentEmu.Height, after.ExtentEmu.Height);
        Near(start.ExtentEmu.Width + (long)Math.Round(40 / Scale(view) * 12700), after.ExtentEmu.Width, 12700);

        // The top handle changes the height only, and the opposite edge stays where it was.
        var before = Inline(h);
        var top = At(h, before, HandleSlot.Top);
        Drag(h, top, new Point(top.X + 30, top.Y - 20));
        var taller = Info(h);
        Assert.Equal(after.ExtentEmu.Width, taller.ExtentEmu.Width);
        Near(after.ExtentEmu.Height + (long)Math.Round(20 / Scale(view) * 12700), taller.ExtentEmu.Height, 12700);
    }

    [AvaloniaFact]
    public async Task ResizeHasAMinimumSize()
    {
        using var h = await OpenFloatingAsync();
        Click(h, Center(Inline(h), h.View));
        var from = At(h, Inline(h), HandleSlot.BottomRight);

        Drag(h, from, new Point(from.X - 2000, from.Y - 2000));

        var info = Info(h);
        var minimum = (long)Math.Round(PictureGestures.MinSizePt * 12700);
        Near(minimum, Math.Min(info.ExtentEmu.Width, info.ExtentEmu.Height), 2);
        Assert.Equal(2.0, Ratio(info), 1);

        // Free, the width and the height each stop at the minimum.
        var again = At(h, Inline(h), HandleSlot.BottomRight);
        Drag(h, again, new Point(again.X - 500, again.Y - 500), RawInputModifiers.Shift);
        var free = Info(h);
        Assert.True(free.ExtentEmu.Width >= minimum - 2 && free.ExtentEmu.Height >= minimum - 2);
    }

    [AvaloniaFact]
    public async Task EscapeCancelsAResize()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        Click(h, Center(Inline(h), view));
        var session = Session(h);
        var before = Info(h);
        var revision = Revision(h);
        var from = At(h, Inline(h), HandleSlot.BottomRight);

        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, new Point(from.X + 50, from.Y + 20));
        Assert.Equal(ObjectDragPhase.Dragging, view.ObjectGesture!.Phase);
        Stroke(h, PhysicalKey.Escape);
        Assert.Equal(ObjectDragCancel.Escape, view.ObjectGesture.LastCancel);
        Assert.Equal(ObjectDragPhase.Idle, view.ObjectGesture.Phase);
        Assert.Null(view.ObjectPointer);
        h.Window.MouseUp(new Point(from.X + 50, from.Y + 20), MouseButton.Left);
        ViewHarness.Pump();

        Assert.Equal(revision, Revision(h));
        Assert.Equal(before.ExtentEmu, Info(h).ExtentEmu);
        Assert.IsType<PictureSelection>(session.ObjectSelection);

        // The same for a drag of a floating picture.
        Click(h, Center(Floating(h), view));
        var floating = Floating(h);
        var start = Inside(floating, view);
        h.Window.MouseDown(start, MouseButton.Left);
        Move(h, start, new Point(start.X + 20, start.Y + 120));
        Stroke(h, PhysicalKey.Escape);
        h.Window.MouseUp(new Point(start.X + 20, start.Y + 120), MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(revision, Revision(h));
        Assert.Equal(ObjectDragCancel.Escape, view.ObjectGesture!.LastCancel);
    }

    // ---- move ------------------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ADragOfAFloatingPictureMovesItsAnchorToTheParagraphUnderThePointer()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        var session = Session(h);
        var box = Floating(h);
        Click(h, Center(box, view));
        var drawing = session.SelectedDrawing!;
        var revision = Revision(h);
        var steps = session.Editor.History.UndoCount;
        Assert.Same(drawing, ((Run)P(h, 1).Inlines[0]).Content.OfType<Picture>().Single());

        // Drag it to the first line of paragraph 3.
        var target = view.CaretBounds(new TextPosition(StoryRef.Body, P(h, 3), 0))!.Value;
        var from = Inside(box, view);
        var to = new Point(target.X + 150, target.Y + (target.Height / 2));
        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, to);
        Assert.Equal("PictureMove", view.ObjectGesture?.Kind?.GetType().Name);
        Assert.Equal(StandardCursorType.SizeAll, view.CursorTypeNow);
        Assert.Equal(revision, Revision(h));
        h.Window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();

        // The picture sits in paragraph 3 now (at its start), paragraph 1 has none, and it is selected again, the same drawing.
        Assert.Equal(revision + 1, Revision(h));
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        Assert.DoesNotContain(P(h, 1).Inlines.OfType<Run>(), r => r.Content.OfType<Picture>().Any());
        var moved = P(h, 3).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>().Single();
        var selected = Assert.IsType<PictureSelection>(session.ObjectSelection);
        Assert.Equal(3, selected.Id.Paragraph);
        Assert.Same(moved, session.SelectedDrawing);

        // Sideways it is where the pointer carried it (the grab point stayed under the pointer); the height follows the paragraph, which moved up when the picture left the room it had held in paragraph 1 (see the next test).
        var after = Floating(h);
        Assert.True(Math.Abs(box.X + ((to.X - from.X) / Scale(view)) - after.X) <= 2.5, $"x: box {box.X}, after {after.X}");

        // One undo takes it back to paragraph 1.
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Single(P(h, 1).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>());
    }

    [AvaloniaFact]
    public async Task AFloatingPictureDroppedAboveItLandsWhereThePointerCarriedIt()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        var session = Session(h);
        var box = Floating(h);
        Click(h, Center(box, view));
        var target = view.CaretBounds(new TextPosition(StoryRef.Body, P(h, 0), 0))!.Value;
        var from = Inside(box, view);
        var to = new Point(target.X + 60, target.Y + 4);

        Drag(h, from, to);

        // The paragraph it went to lies above everything that moved, so its place on the page is where the pointer carried the grab point, to a point or two (the offsets are measured from the new paragraph).
        var after = Floating(h);
        var (dx, dy) = ((to.X - from.X) / Scale(view), (to.Y - from.Y) / Scale(view));
        Assert.True(Math.Abs(box.X + dx - after.X) <= 2.5 && Math.Abs(box.Y + dy - after.Y) <= 2.5, $"box ({box.X},{box.Y}) moved by ({dx},{dy}) landed at ({after.X},{after.Y})");
        Assert.Equal(0, ((PictureSelection)session.ObjectSelection!).Id.Paragraph);
        Assert.Single(P(h, 0).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>());
        var anchor = PictureView.Of(session.SelectedDrawing!)!.Anchor!;
        Assert.Equal("paragraph", anchor.VerticalFrom);
        Assert.Equal("column", anchor.HorizontalFrom);
    }

    [AvaloniaFact]
    public async Task AnInlinePictureIsNotDraggedAndAPressOnItOnlySelectsIt()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        var box = Inline(h);
        var revision = Revision(h);
        var from = Inside(box, view);

        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, new Point(from.X + 20, from.Y - 150));
        Assert.True(view.ObjectGesture is null or { IsActive: false });
        h.Window.MouseUp(new Point(from.X + 20, from.Y - 150), MouseButton.Left);
        ViewHarness.Pump();

        Assert.Equal(revision, Revision(h));
        Assert.IsType<PictureSelection>(Session(h).ObjectSelection);
        Assert.Single(P(h, 4).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>());
    }

    [AvaloniaFact]
    public async Task ADropOnACellShowsNoDropAndDoesNothing()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        var session = Session(h);
        var box = Floating(h);
        Click(h, Center(box, view));
        var revision = Revision(h);
        var cell = Grid(h).Cells[0];
        var cellPoint = ViewPoint(view, 0, cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));
        var from = Inside(box, view);

        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, cellPoint);
        Assert.True(view.ObjectGesture is { Phase: ObjectDragPhase.Dragging }, $"gesture {view.ObjectGesture?.Phase} {view.ObjectGesture?.LastCancel} from {from} to {cellPoint}");
        Assert.Equal(StandardCursorType.No, view.CursorTypeNow);
        h.Window.MouseUp(cellPoint, MouseButton.Left);
        ViewHarness.Pump();

        Assert.Equal(revision, Revision(h));
        Assert.Single(P(h, 1).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>());
        Assert.IsType<PictureSelection>(session.ObjectSelection);
        Assert.Equal(0, session.Editor.History.UndoCount);

        // The desk beside the page is no drop either.
        var again = from - new Vector(24, 24);
        var desk = new Point(2, again.Y);
        h.Window.MouseDown(again, MouseButton.Left);
        Move(h, again, desk);
        Assert.Equal(StandardCursorType.No, view.CursorTypeNow);
        h.Window.MouseUp(desk, MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(revision, Revision(h));
    }

    // ---- keys ------------------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ArrowKeysNudgeASelectedPicture()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        var session = Session(h);
        Click(h, Center(Floating(h), view));
        var steps = session.Editor.History.UndoCount;
        var revision = Revision(h);
        var start = Info(h).Anchor!;

        Stroke(h, PhysicalKey.ArrowRight);
        Assert.Equal(start.HorizontalOffsetEmu + 12700, Info(h).Anchor!.HorizontalOffsetEmu);
        Assert.Equal(start.VerticalOffsetEmu, Info(h).Anchor!.VerticalOffsetEmu);
        Stroke(h, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Assert.Equal(start.VerticalOffsetEmu + 127000, Info(h).Anchor!.VerticalOffsetEmu);
        Stroke(h, PhysicalKey.ArrowLeft);
        Stroke(h, PhysicalKey.ArrowUp);
        Assert.Equal(start.HorizontalOffsetEmu, Info(h).Anchor!.HorizontalOffsetEmu);
        Assert.Equal(start.VerticalOffsetEmu + 127000 - 12700, Info(h).Anchor!.VerticalOffsetEmu);

        // The nudges of one second are one history step, the revision moves with every nudge, and the picture stays selected.
        Assert.True(Revision(h) > revision);
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        Assert.IsType<PictureSelection>(session.ObjectSelection);
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Equal(start.HorizontalOffsetEmu, PictureView.Of(((Run)P(h, 1).Inlines[0]).Content.OfType<Picture>().Single())!.Anchor!.HorizontalOffsetEmu);
    }

    [AvaloniaFact]
    public async Task ArrowKeysOnAnInlinePictureMoveTheCaretAsEverAndEndTheSelection()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        var session = Session(h);
        Click(h, Center(Inline(h), view));
        Assert.IsType<PictureSelection>(session.ObjectSelection);
        var revision = Revision(h);

        Stroke(h, PhysicalKey.ArrowRight);

        Assert.Null(session.ObjectSelection);
        Assert.Equal(revision, Revision(h));
    }

    [AvaloniaFact]
    public async Task APictureThatIsPlacedByAnAlignmentIsNotNudgedAndSaysWhy()
    {
        using var h = await OpenFloatingAsync(aligned: true);
        var view = h.View;
        var session = Session(h);
        Click(h, Center(Floating(h), view));
        var notes = new List<string>();
        view.StructureNote += notes.Add;
        var revision = Revision(h);

        Stroke(h, PhysicalKey.ArrowRight);

        Assert.Equal(revision, Revision(h));
        Assert.Equal([WriteStrings.ReasonPictureAlignedPosition], notes);
        Assert.IsType<PictureSelection>(session.ObjectSelection);
    }

    [AvaloniaFact]
    public async Task DeleteWithACellSelectionIsClaimedWithANoteAndChangesNothing()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        var session = Session(h);
        var grid = Grid(h);
        Assert.True(session.SelectCells(TableOf(grid), (0, 0), (1, 1)));
        var notes = new List<string>();
        view.StructureNote += notes.Add;
        var revision = Revision(h);
        var text = ParagraphProjection.Build((Paragraph)((TableCell)((TableRow)((Table)h.View.Tab.Docx.Model.Body.Blocks[6]).Rows[0]).Items[0]).Blocks[0], ProjectionHost.Plain).Text;

        Stroke(h, PhysicalKey.Delete);
        Stroke(h, PhysicalKey.Backspace);

        Assert.Equal(revision, Revision(h));
        Assert.Equal([WriteStrings.NoteCellsDeleteKey, WriteStrings.NoteCellsDeleteKey], notes);
        Assert.IsType<CellSelection>(session.ObjectSelection);
        Assert.Equal("c00", text);
    }

    // ---- saves, cursors, cost --------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task NoGestureDuringASave()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        var session = Session(h);
        Click(h, Center(Floating(h), view));
        var revision = Revision(h);
        var handle = At(h, Floating(h), HandleSlot.BottomRight);

        using (session.BeginSave())
        {
            // A press on the handle and one on the picture itself only select: no gesture, no capture, no change.
            h.Window.MouseDown(handle, MouseButton.Left);
            Assert.True(view.ObjectGesture is null or { IsActive: false });
            Assert.Null(view.ObjectPointer);
            Move(h, handle, new Point(handle.X + 60, handle.Y + 60));
            h.Window.MouseUp(new Point(handle.X + 60, handle.Y + 60), MouseButton.Left);
            ViewHarness.Pump();
            Assert.Equal(revision, Revision(h));
            Assert.IsType<PictureSelection>(session.ObjectSelection);
        }

        // A save that starts in the middle of a gesture ends it with no change.
        var again = At(h, Floating(h), HandleSlot.TopLeft);
        h.Window.MouseDown(again, MouseButton.Left);
        Move(h, again, new Point(again.X + 20, again.Y + 20));
        Assert.Equal(ObjectDragPhase.Dragging, view.ObjectGesture!.Phase);
        using (session.BeginSave())
        {
            Assert.Equal(ObjectDragCancel.SaveStarted, view.ObjectGesture.LastCancel);
        }

        h.Window.MouseUp(new Point(again.X + 20, again.Y + 20), MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(revision, Revision(h));
    }

    [AvaloniaFact]
    public async Task TheCursorOfEachHandleIsItsResizeArrowAndTheBodyOfAFloatingPictureTheMoveCross()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        Click(h, Center(Inline(h), view));
        var box = Inline(h);
        var expected = new Dictionary<HandleSlot, StandardCursorType>
        {
            [HandleSlot.TopLeft] = StandardCursorType.TopLeftCorner,
            [HandleSlot.BottomRight] = StandardCursorType.BottomRightCorner,
            [HandleSlot.TopRight] = StandardCursorType.TopRightCorner,
            [HandleSlot.BottomLeft] = StandardCursorType.BottomLeftCorner,
            [HandleSlot.Top] = StandardCursorType.SizeNorthSouth,
            [HandleSlot.Bottom] = StandardCursorType.SizeNorthSouth,
            [HandleSlot.Left] = StandardCursorType.SizeWestEast,
            [HandleSlot.Right] = StandardCursorType.SizeWestEast,
        };
        foreach (var (slot, cursor) in expected)
        {
            h.Window.MouseMove(At(h, box, slot));
            ViewHarness.Pump();
            Assert.Equal(cursor, view.CursorTypeNow);
        }

        // The body of a selected inline picture asks for the text cursor; the body of a selected floating one for the move cross.
        h.Window.MouseMove(Center(box, view));
        ViewHarness.Pump();
        Assert.Equal(StandardCursorType.Ibeam, view.CursorTypeNow);
        Click(h, Center(Floating(h), view));
        h.Window.MouseMove(Center(Floating(h), view));
        ViewHarness.Pump();
        Assert.Equal(StandardCursorType.SizeAll, view.CursorTypeNow);
    }

    [AvaloniaFact]
    public async Task ThePointerMoveAndTheMoveDragCostAtMost16msP95()
    {
        using var h = await OpenFloatingAsync();
        var view = h.View;
        Click(h, Center(Floating(h), view));
        var box = Floating(h);
        var from = Inside(box, view);
        var targets = new List<Point>();
        for (var i = 0; i < 240; i++)
        {
            targets.Add(new Point(from.X + (i % 40 * 4) - 80, from.Y + (i / 40 * 40) - 20));
        }

        // The hover: no button down, the selected picture asks for its cursor.
        BestP95("pointer moves over a selected picture", targets, target => h.Window.MouseMove(target));

        // The drag: every move of a floating picture works out where it would land (a text hit test) and draws its outline.
        h.Window.MouseDown(from, MouseButton.Left);
        BestP95("drag moves of a floating picture", targets, target =>
        {
            h.Window.MouseMove(target);
            ViewHarness.Pump();
        });
        h.Window.MouseUp(from, MouseButton.Left);
        ViewHarness.Pump();
    }

    // The plan's 16 ms of CPU per pointer move, with up to three attempts (each after a warm-up pass); the test fails only when the best attempt's p95 is above the budget.
    private static void BestP95(string label, List<Point> targets, Action<Point> move)
    {
        var best = double.MaxValue;
        var notes = new List<string>();
        for (var attempt = 0; attempt < 3 && best > 16.0; attempt++)
        {
            foreach (var warm in targets)
            {
                move(warm);
            }

            GC.Collect();
            ViewHarness.Pump();
            var samples = new List<double>();
            foreach (var target in targets)
            {
                var clock = Stopwatch.StartNew();
                move(target);
                samples.Add(clock.Elapsed.TotalMilliseconds);
            }

            samples.Sort();
            var p95 = samples[(int)(samples.Count * 0.95)];
            best = Math.Min(best, p95);
            notes.Add($"attempt {attempt}: p95 {p95:F3} ms, median {samples[samples.Count / 2]:F3} ms");
        }

        Assert.True(best <= 16.0, $"{label}: best p95 {best:F3} ms (budget 16); {string.Join("; ", notes)}");
    }
}
