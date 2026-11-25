using Avalonia;
using Raffica.Write.Layout;

namespace Raffica.Write.Viewing;

/// <summary>What a drag of the pointer over an object does. Open on purpose: a later task adds the kind of its gesture (a column border, a row edge, a cell range ...) as one more record.</summary>
internal abstract record ObjectDragKind;

/// <summary>The picture itself is dragged to another place.</summary>
internal sealed record PictureMove : ObjectDragKind;

/// <summary>A handle of the selected picture is dragged.</summary>
internal sealed record PictureResize(HandleSlot Slot) : ObjectDragKind;

/// <summary>Where a drag began and where the pointer was released, in DIPs of the view.</summary>
internal readonly record struct ObjectDragEnd(Point Start, Point End);

/// <summary>Why a drag ended with no change.</summary>
internal enum ObjectDragCancel
{
    /// <summary>The Escape key.</summary>
    Escape,

    /// <summary>The platform took the pointer away (another window, a menu).</summary>
    CaptureLost,

    /// <summary>A save started.</summary>
    SaveStarted,

    /// <summary>The document changed (an edit, an undo, another document or another layout).</summary>
    DocumentChanged,

    /// <summary>The view changed under the pointer (its size, the pane beside it, the view mode).</summary>
    ViewChanged,

    /// <summary>The tab was closed.</summary>
    Disposed,
}

/// <summary>The phases of <see cref="ObjectDrag"/>.</summary>
internal enum ObjectDragPhase
{
    Idle,
    Pressed,
    Dragging,
    Releasing,
}

/// <summary>
/// The state machine of one pointer gesture on an object (milestone 4.5a): <see cref="ObjectDragPhase.Idle"/> to <see cref="ObjectDragPhase.Pressed"/> on the press, to
/// <see cref="ObjectDragPhase.Dragging"/> once the pointer moved <see cref="Threshold"/> DIPs, to <see cref="ObjectDragPhase.Releasing"/> while the release is committed, back to Idle. A gesture changes
/// the document at most once, in the commit callback of <see cref="Release"/>, and never while it is going on; <see cref="Cancel"/> ends it with no change for every outside reason (Escape,
/// capture lost, a save, an edit, a document or view change). A press that moved less than the threshold is a click and commits nothing. No visual, no model and no timer: the view owns the pointer capture and the drawing.
/// </summary>
internal sealed class ObjectDrag
{
    /// <summary>How far, in DIPs, the pointer must move after the press before the gesture is a drag (a click that shakes a little is still a click).</summary>
    public const double Threshold = 4;

    /// <summary>The phase now.</summary>
    public ObjectDragPhase Phase { get; private set; }

    /// <summary>What the gesture does; null while idle.</summary>
    public ObjectDragKind? Kind { get; private set; }

    /// <summary>Where the pointer was pressed.</summary>
    public Point Start { get; private set; }

    /// <summary>Where the pointer is now.</summary>
    public Point Current { get; private set; }

    /// <summary>The reason of the last cancel; null before one.</summary>
    public ObjectDragCancel? LastCancel { get; private set; }

    /// <summary>How many gestures were cancelled.</summary>
    public int Cancelled { get; private set; }

    /// <summary>How many gestures were committed.</summary>
    public int Committed { get; private set; }

    /// <summary>True from the press to the release or cancel (not while the release is committed).</summary>
    public bool IsActive => Phase is ObjectDragPhase.Pressed or ObjectDragPhase.Dragging;

    /// <summary>Raised when the phase or the pointer position of a gesture changed (the view draws the outline again).</summary>
    public event Action? Changed;

    /// <summary>Starts a gesture; false (nothing changes) when one is going on.</summary>
    public bool Press(ObjectDragKind kind, Point at)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (Phase != ObjectDragPhase.Idle)
        {
            return false;
        }

        Phase = ObjectDragPhase.Pressed;
        Kind = kind;
        Start = at;
        Current = at;
        Changed?.Invoke();
        return true;
    }

    /// <summary>The pointer moved; the gesture becomes a drag when it has moved the threshold.</summary>
    public void Move(Point at)
    {
        if (!IsActive || at == Current)
        {
            return;
        }

        Current = at;
        if (Phase == ObjectDragPhase.Pressed && Distance(Start, at) >= Threshold)
        {
            Phase = ObjectDragPhase.Dragging;
        }

        if (Phase == ObjectDragPhase.Dragging)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// The pointer was released at <paramref name="at"/>. A drag calls <paramref name="commit"/> once with its kind, start and end (the phase is Releasing meanwhile, so a change the commit makes does not cancel it)
    /// and returns what it returned; a press that never became a drag commits nothing and returns false. The gesture is idle afterwards, also when the commit throws.
    /// </summary>
    public bool Release(Point at, Func<ObjectDragKind, ObjectDragEnd, bool> commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (!IsActive)
        {
            return false;
        }

        if (Phase != ObjectDragPhase.Dragging)
        {
            End();
            return false;
        }

        Current = at;
        var kind = Kind!;
        var end = new ObjectDragEnd(Start, at);
        Phase = ObjectDragPhase.Releasing;
        var done = false;
        try
        {
            done = commit(kind, end);
        }
        finally
        {
            if (done)
            {
                Committed++;
            }

            End();
        }

        return done;
    }

    /// <summary>Ends the gesture with no change. Does nothing when idle or while the release is committed.</summary>
    public void Cancel(ObjectDragCancel reason)
    {
        if (!IsActive)
        {
            return;
        }

        LastCancel = reason;
        Cancelled++;
        End();
    }

    private void End()
    {
        Phase = ObjectDragPhase.Idle;
        Kind = null;
        Changed?.Invoke();
    }

    private static double Distance(Point a, Point b)
    {
        var (dx, dy) = (a.X - b.X, a.Y - b.Y);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
