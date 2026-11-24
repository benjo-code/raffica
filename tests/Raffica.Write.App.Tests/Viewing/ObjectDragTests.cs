using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Xunit;
using static Raffica.WriteApp.Tests.Viewing.PageViewObjectTests;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The state machine of a pointer gesture on an object and how the view cancels it (milestone 4.5a, Task 5).</summary>
public sealed class ObjectDragTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-drag-").FullName;

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

    private sealed class Fake
    {
        public List<(ObjectDragKind Kind, ObjectDragEnd End)> Commits { get; } = [];
    }

    // A view with the picture selected and a commit callback that only records; the pointer is pressed on the bottom right handle and moved far enough to be a drag.
    private async Task<(ViewHarness H, Fake Commits, Point Handle)> DraggingAsync()
    {
        var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var box = ThePicture(h);
        Click(h, Center(box, view));
        var fake = new Fake();
        view.ObjectDragCommit = (kind, end) =>
        {
            fake.Commits.Add((kind, end));
            return true;
        };
        var handle = ViewPoint(view, 0, box.X + box.Width, box.Y + box.Height);
        h.Window.MouseDown(handle, MouseButton.Left);
        h.Window.MouseMove(new Point(handle.X + 12, handle.Y + 9));
        ViewHarness.Pump();
        Assert.Equal(ObjectDragPhase.Dragging, view.ObjectGesture!.Phase);
        Assert.Equal(new PictureResize(HandleSlot.BottomRight), view.ObjectGesture.Kind);
        return (h, fake, handle);
    }

    [Fact]
    public void AMachineBecomesADragAfterTheThresholdAndCommitsOnceOnRelease()
    {
        var drag = new ObjectDrag();
        Assert.Equal(ObjectDragPhase.Idle, drag.Phase);
        Assert.True(drag.Press(new PictureMove(), new Point(10, 10)));
        Assert.False(drag.Press(new PictureMove(), new Point(0, 0)));
        Assert.Equal(ObjectDragPhase.Pressed, drag.Phase);

        drag.Move(new Point(12, 12));
        Assert.Equal(ObjectDragPhase.Pressed, drag.Phase);
        drag.Move(new Point(10 + ObjectDrag.Threshold, 10));
        Assert.Equal(ObjectDragPhase.Dragging, drag.Phase);

        var seen = new List<ObjectDragPhase>();
        var calls = 0;
        var done = drag.Release(
            new Point(30, 40),
            (kind, end) =>
            {
                calls++;
                seen.Add(drag.Phase);
                Assert.IsType<PictureMove>(kind);
                Assert.Equal(new ObjectDragEnd(new Point(10, 10), new Point(30, 40)), end);

                // A cancel while the release is committed changes nothing: the commit goes on.
                drag.Cancel(ObjectDragCancel.DocumentChanged);
                return true;
            });

        Assert.True(done);
        Assert.Equal(1, calls);
        Assert.Equal([ObjectDragPhase.Releasing], seen);
        Assert.Equal(ObjectDragPhase.Idle, drag.Phase);
        Assert.Equal(1, drag.Committed);
        Assert.Equal(0, drag.Cancelled);
    }

    [Fact]
    public void AClickCommitsNothing()
    {
        var drag = new ObjectDrag();
        drag.Press(new PictureMove(), new Point(5, 5));
        drag.Move(new Point(7, 6));
        Assert.False(drag.Release(new Point(7, 6), (_, _) => throw new InvalidOperationException("a click commits nothing")));
        Assert.Equal(ObjectDragPhase.Idle, drag.Phase);
        Assert.Equal(0, drag.Committed);
    }

    [Fact]
    public void EveryReasonEndsAGestureWithNoCommitAndIdleDoesNothing()
    {
        foreach (var reason in Enum.GetValues<ObjectDragCancel>())
        {
            var drag = new ObjectDrag();
            drag.Cancel(reason);
            Assert.Null(drag.LastCancel);
            drag.Press(new PictureResize(HandleSlot.Top), new Point(0, 0));
            drag.Move(new Point(50, 50));
            drag.Cancel(reason);
            Assert.Equal(ObjectDragPhase.Idle, drag.Phase);
            Assert.Equal(reason, drag.LastCancel);
            Assert.Equal(1, drag.Cancelled);
            Assert.False(drag.Release(new Point(60, 60), (_, _) => true));
            Assert.Equal(0, drag.Committed);
        }
    }

    [Fact]
    public void AFailingCommitStillEndsTheGesture()
    {
        var drag = new ObjectDrag();
        drag.Press(new PictureMove(), new Point(0, 0));
        drag.Move(new Point(40, 0));
        Assert.Throws<InvalidOperationException>(() => drag.Release(new Point(40, 0), (_, _) => throw new InvalidOperationException()));
        Assert.Equal(ObjectDragPhase.Idle, drag.Phase);
        Assert.Equal(0, drag.Committed);
    }

    [AvaloniaFact]
    public async Task APressOnTheHandleOfASelectedPictureStartsADragThatCommitsOnceOnRelease()
    {
        var (h, fake, handle) = await DraggingAsync();
        using (h)
        {
            var view = h.View;
            var revision = view.Tab.Docx.Model.Edits.Revision;
            Assert.Empty(fake.Commits);
            Assert.Same(view, view.ObjectPointer!.Captured);

            var end = new Point(handle.X + 20, handle.Y + 15);
            h.Window.MouseMove(end);
            h.Window.MouseUp(end, MouseButton.Left);
            ViewHarness.Pump();

            var (kind, range) = Assert.Single(fake.Commits);
            Assert.Equal(new PictureResize(HandleSlot.BottomRight), kind);
            Assert.Equal(handle, range.Start);
            Assert.Equal(end, range.End);
            Assert.Equal(ObjectDragPhase.Idle, view.ObjectGesture!.Phase);
            Assert.Null(view.ObjectPointer);
            Assert.Equal(revision, view.Tab.Docx.Model.Edits.Revision);
            Assert.NotNull(view.Tab.Editor!.ObjectSelection);
        }
    }

    [AvaloniaFact]
    public async Task EscapeEndsADragWithNoChange()
    {
        var (h, fake, handle) = await DraggingAsync();
        using (h)
        {
            var view = h.View;
            var revision = view.Tab.Docx.Model.Edits.Revision;

            Stroke(h, PhysicalKey.Escape);

            Assert.Equal(ObjectDragPhase.Idle, view.ObjectGesture!.Phase);
            Assert.Equal(ObjectDragCancel.Escape, view.ObjectGesture.LastCancel);
            Assert.Null(view.ObjectPointer);
            // The first Escape ends the drag only: the picture is still selected, and a second one ends the selection.
            Assert.NotNull(view.Tab.Editor!.ObjectSelection);
            h.Window.MouseUp(new Point(handle.X + 12, handle.Y + 9), MouseButton.Left);
            ViewHarness.Pump();
            Assert.Empty(fake.Commits);
            Assert.Equal(revision, view.Tab.Docx.Model.Edits.Revision);

            Stroke(h, PhysicalKey.Escape);
            Assert.Null(view.Tab.Editor.ObjectSelection);
        }
    }

    [AvaloniaFact]
    public async Task CaptureLostEndsADragWithNoChange()
    {
        var (h, fake, handle) = await DraggingAsync();
        using (h)
        {
            var view = h.View;
            var revision = view.Tab.Docx.Model.Edits.Revision;

            // The platform takes the pointer away: the real event, through the capture.
            view.ObjectPointer!.Capture(null);
            ViewHarness.Pump();

            Assert.Equal(ObjectDragPhase.Idle, view.ObjectGesture!.Phase);
            Assert.Equal(ObjectDragCancel.CaptureLost, view.ObjectGesture.LastCancel);
            h.Window.MouseUp(new Point(handle.X + 12, handle.Y + 9), MouseButton.Left);
            ViewHarness.Pump();
            Assert.Empty(fake.Commits);
            Assert.Equal(revision, view.Tab.Docx.Model.Edits.Revision);
        }
    }

    [AvaloniaFact]
    public async Task ASaveStartingMidDragCancelsIt()
    {
        var (h, fake, handle) = await DraggingAsync();
        using (h)
        {
            var view = h.View;
            var session = view.Tab.Editor!;
            using (session.BeginSave())
            {
                Assert.Equal(ObjectDragPhase.Idle, view.ObjectGesture!.Phase);
                Assert.Equal(ObjectDragCancel.SaveStarted, view.ObjectGesture.LastCancel);
                h.Window.MouseUp(new Point(handle.X + 12, handle.Y + 9), MouseButton.Left);
                ViewHarness.Pump();
            }

            Assert.Empty(fake.Commits);
        }
    }

    [AvaloniaFact]
    public async Task AnUndoMidDragCancelsIt()
    {
        var h = await OpenAsync(ObjectDocx.Write(_folder));
        using (h)
        {
            var view = h.View;
            var session = view.Tab.EnsureEditor();
            var start = new TextPosition(StoryRef.Body, P(h, 0), 0);
            session.SetSelection(new TextRange(start, start));
            Assert.NotNull(session.Apply(ed => ed.InsertText(session.Selection!.Value, "x"), 1));
            Assert.True(session.CanUndo);

            var box = ThePicture(h);
            Click(h, Center(box, view));
            var fake = new Fake();
            view.ObjectDragCommit = (kind, end) =>
            {
                fake.Commits.Add((kind, end));
                return true;
            };
            var handle = ViewPoint(view, 0, box.X + box.Width, box.Y + box.Height);
            h.Window.MouseDown(handle, MouseButton.Left);
            h.Window.MouseMove(new Point(handle.X + 14, handle.Y + 14));
            Assert.Equal(ObjectDragPhase.Dragging, view.ObjectGesture!.Phase);

            Assert.NotNull(session.Apply(ed => ed.History.Undo(), 1));

            Assert.Equal(ObjectDragPhase.Idle, view.ObjectGesture.Phase);
            Assert.Equal(ObjectDragCancel.DocumentChanged, view.ObjectGesture.LastCancel);
            h.Window.MouseUp(new Point(handle.X + 14, handle.Y + 14), MouseButton.Left);
            ViewHarness.Pump();
            Assert.Empty(fake.Commits);
        }
    }

    [AvaloniaFact]
    public async Task APressDuringASaveOnlySelects()
    {
        var h = await OpenAsync(ObjectDocx.Write(_folder));
        using (h)
        {
            var view = h.View;
            var session = view.Tab.EnsureEditor();
            view.ObjectDragCommit = (_, _) => true;
            using (session.BeginSave())
            {
                Click(h, Center(ThePicture(h), view));
                Assert.IsType<PictureSelection>(session.ObjectSelection);
                Assert.True(view.ObjectGesture is null || view.ObjectGesture.Phase == ObjectDragPhase.Idle);
            }
        }
    }

    [AvaloniaFact]
    public async Task WithNoCommitRegisteredAPressOnlySelects()
    {
        var h = await OpenAsync(ObjectDocx.Write(_folder));
        using (h)
        {
            var view = h.View;
            var box = ThePicture(h);
            Click(h, Center(box, view));
            var handle = ViewPoint(view, 0, box.X + box.Width, box.Y + box.Height);
            h.Window.MouseDown(handle, MouseButton.Left);
            h.Window.MouseMove(new Point(handle.X + 12, handle.Y + 12));
            h.Window.MouseUp(handle, MouseButton.Left);
            Assert.Null(view.ObjectGesture);
            Assert.IsType<PictureSelection>(view.Tab.Editor!.ObjectSelection);
        }
    }

    [AvaloniaFact]
    public async Task AChangeOfTheViewSizeEndsADrag()
    {
        var (h, fake, _) = await DraggingAsync();
        using (h)
        {
            h.Window.Width = 800;
            ViewHarness.Pump();
            Assert.Equal(ObjectDragPhase.Idle, h.View.ObjectGesture!.Phase);
            Assert.Equal(ObjectDragCancel.ViewChanged, h.View.ObjectGesture.LastCancel);
            Assert.Empty(fake.Commits);
        }
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];
}
