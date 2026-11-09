using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Text;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Redacting;

/// <summary>
/// The pointer and key side of the redact mode of one tab. With the area tool a left drag of the mouse or the pen on a page makes an area (one
/// history step however long the drag), a drag inside an area moves it and a drag on a corner handle of the selected area resizes it; the
/// text, hit and area marks alike are selected by a press, but only areas move (a text mark is deleted and made again). A touch finger
/// scrolls. With the text tool the pointer belongs to the text selection (the text interaction), and Ctrl+Enter marks it. Keys, while the
/// page view has the focus: arrows move the selected area by a point (Shift resizes its right and bottom edge; a run within 400 ms is one
/// step), Tab and Shift+Tab walk the marks in reading order and leave the page when the walk is over, Delete removes the selected marks,
/// Esc drops a running drag, then the selection, and only then asks to leave the mode. Used on the UI thread only.
/// </summary>
internal sealed class RedactInteraction : IDisposable
{
    // A press that never got farther than this (in DIPs) from where it went down is a click.
    private const double ClickSlop = 4;

    // Arrow presses closer together than this are one run, one history step.
    private static readonly TimeSpan NudgeRun = TimeSpan.FromMilliseconds(400);

    private static Cursor? s_cross;

    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly RedactSession _session;
    private readonly SelectionController _selection;
    private readonly TimeProvider _time;
    private Gesture _gesture;
    private IPointer? _pointer;
    private Guid _id;
    private int _page;
    private Point _press;
    private PageRect _origin;
    private (double X, double Y) _anchor;
    private bool _started;
    private long _lastNudge;
    private Guid _nudgeId;
    private int _nudgeVersion = -1;
    private bool _markKeyDown;
    private bool _disposed;

    private enum Gesture
    {
        None,
        New,
        Move,
        Resize,
    }

    public RedactInteraction(PdfTab tab, PageView view, RedactSession session, SelectionController selection, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(selection);
        _tab = tab;
        _view = view;
        _session = session;
        _selection = selection;
        _time = time ?? TimeProvider.System;
        session.SelectionSource = () => _selection;
        session.PageBounds = page => view.SizeOfPage(page) is { } size ? new PageRect(0, 0, size.Width, size.Height) : null;
        view.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble);
        // Tunnel, not bubble: the page view's own handler scrolls with the arrows and marks them handled, so the keys of the marks must come first.
        view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        view.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
    }

    /// <summary>Raised when Esc found nothing left to drop: the user asks to leave the mode (the window asks about the marks).</summary>
    public event Action? EndRequested;

    /// <summary>Raised when an operation the user started was refused (the window says why in the pane).</summary>
    public event Action<RedactResult>? Refused;

    /// <summary>Whether a drag is running.</summary>
    public bool IsDragging => _gesture != Gesture.None;

    /// <summary>The last text marking that was started (for tests and for whoever must wait for it).</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Marks the selected text (Ctrl+Enter, the Mark selection command); a refusal is raised as <see cref="Refused"/>.</summary>
    public Task MarkSelection()
    {
        Completion = MarkSelectionCoreAsync();
        return Completion;
    }

    /// <summary>
    /// Drops what Esc may drop, in this order: a running drag (and the mark or step it made), the selected marks, an open text selection. Returns
    /// whether something was dropped; when nothing was, the same Esc asks to leave the mode. The step of a cancelled drag is undone but stays on
    /// the redo list (Core's history cannot drop it), so a Redo brings the cancelled drag back.
    /// </summary>
    public bool Cancel()
    {
        if (_gesture != Gesture.None)
        {
            var made = _started;
            EndGesture();
            if (made)
            {
                _session.Undo();
            }

            return true;
        }

        if (_session.Selection.Count > 0)
        {
            _session.Selection.Clear();
            return true;
        }

        if (_selection.HasSelection)
        {
            _selection.Clear();
            return true;
        }

        return false;
    }

    /// <summary>Takes a key while the mode is on (the page view has the focus). Sets <c>Handled</c> when the key was used.</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!_session.IsActive || _disposed)
        {
            return false;
        }

        var used = KeyCore(e);
        if (used)
        {
            e.Handled = true;
        }

        return used;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EndGesture();
        _view.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        _view.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _view.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        _view.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        _view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _view.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        if (ReferenceEquals(_view.Cursor, s_cross))
        {
            _view.Cursor = null;
        }

        _session.SelectionSource = null;
        _session.PageBounds = null;
    }

    private bool KeyCore(KeyEventArgs e)
    {
        var modifiers = e.KeyModifiers;
        switch (e.Key)
        {
            case Key.Escape when modifiers == KeyModifiers.None:
                if (!Cancel())
                {
                    EndRequested?.Invoke();
                }

                return true;
            case Key.Enter when (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0:
                // A held key repeats the press: only the first one marks, the key going up re-arms it.
                if (!_markKeyDown)
                {
                    _markKeyDown = true;
                    _ = MarkSelection();
                }

                return true;
            case Key.Delete or Key.Back when modifiers == KeyModifiers.None && _session.Selection.Count > 0:
                Report(_session.Delete(new HashSet<Guid>(_session.Selection.Ids)));
                return true;
            case Key.Tab when modifiers is KeyModifiers.None or KeyModifiers.Shift:
                return WalkTo(RedactSelection.Next(_session.Plan, _session.Selection.Primary, forward: modifiers == KeyModifiers.None));
            case Key.Left or Key.Right or Key.Up or Key.Down when modifiers is KeyModifiers.None or KeyModifiers.Shift:
                return Nudge(e.Key, resize: modifiers == KeyModifiers.Shift);
            default:
                return false;
        }
    }

    private bool WalkTo(Guid? next)
    {
        if (next is not { } id)
        {
            // Past the last (or before the first) mark: the key goes on, so the focus is not trapped on the page.
            return false;
        }

        _session.Selection.Select(id);
        if (_session.Plan.Marks.FirstOrDefault(m => m.Id == id) is { } mark)
        {
            _view.Reveal(mark.Page, mark.Areas[0]);
        }

        return true;
    }

    private bool Nudge(Key key, bool resize)
    {
        if (_session.Selection.Single is not { } id || _session.Plan.Marks.FirstOrDefault(m => m.Id == id) is not { Kind: RedactionKind.Area } mark)
        {
            return false;
        }

        var dx = key == Key.Left ? -1.0 : key == Key.Right ? 1.0 : 0.0;
        var dy = key == Key.Up ? -1.0 : key == Key.Down ? 1.0 : 0.0;
        var now = _time.GetTimestamp();
        var continuing = _nudgeId == id && _nudgeVersion == _session.Version && _time.GetElapsedTime(_lastNudge, now) <= NudgeRun;
        var area = mark.Areas[0];
        var versionBefore = _session.Version;
        var result = resize
            ? _session.ResizeArea(id, new PageRect(area.Left, area.Top, area.Right + dx, area.Bottom + dy), continuing)
            : _session.MoveArea(id, dx, dy, continuing);
        if (_session.Version != versionBefore)
        {
            // Only a change starts or continues a run: a nudge that was clamped to nothing must not let the next one amend an unrelated step.
            _lastNudge = now;
            _nudgeId = id;
            _nudgeVersion = _session.Version;
        }

        Report(result);
        return true;
    }

    private async Task MarkSelectionCoreAsync()
    {
        var result = await _session.MarkSelectionAsync();
        Report(result);
    }

    private void Report(RedactResult result)
    {
        // A second marking while one runs is a repeat of the same request, not something to tell the user.
        if (!result.IsDone && result.Problem != RedactProblem.Busy)
        {
            Refused?.Invoke(result);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // The keys of a text field in the view (the editing field), of the badge's buttons and of any other text box are theirs.
        if (!e.Handled && !_view.IsInBadge(e.Source) && !_view.IsInField(e.Source) && e.Source is not Avalonia.Controls.TextBox)
        {
            HandleKey(e);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.LeftCtrl or Key.RightCtrl or Key.LWin or Key.RWin)
        {
            _markKeyDown = false;
        }
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_session.IsActive || _session.Tool != RedactTool.Area || _view.IsInBadge(e.Source) || _view.IsInField(e.Source))
        {
            return;
        }

        // Touch scrolls and zooms the page; only the mouse and the pen work on it.
        if (e.Pointer.Type == PointerType.Touch)
        {
            return;
        }

        var point = e.GetCurrentPoint(_view);
        if (!point.Properties.IsLeftButtonPressed || _view.IsOverScrollBar(point.Position))
        {
            return;
        }

        if (Press(point.Position))
        {
            _pointer = e.Pointer;
            e.Pointer.Capture(_view);
        }
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_session.IsActive || _session.Tool != RedactTool.Area)
        {
            return;
        }

        var position = e.GetPosition(_view);
        if (_gesture != Gesture.None)
        {
            Move(position);
            return;
        }

        var cursor = _view.IsOverScrollBar(position) || _view.IsInBadge(e.Source) ? null : (s_cross ??= new Cursor(StandardCursorType.Cross));
        if (!ReferenceEquals(_view.Cursor, cursor))
        {
            _view.Cursor = cursor;
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_gesture == Gesture.None || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        Move(e.GetPosition(_view));
        EndGesture();
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // The pointer was taken away (a dialog opened, the window lost the mouse): the gesture ends where it stands.
        if (_gesture != Gesture.None)
        {
            _gesture = Gesture.None;
            _pointer = null;
        }
    }

    // Handles a press at a point of the view: selects a mark, or starts a gesture. Returns whether a gesture started (the pointer is then captured).
    private bool Press(Point point)
    {
        EndGesture();
        if (!_view.FirstSharpPageShown)
        {
            return false;
        }

        // A corner handle of the one selected area resizes it.
        if (_session.Selection.Single is { } selected && _session.Plan.Marks.FirstOrDefault(m => m.Id == selected) is { Kind: RedactionKind.Area } chosen)
        {
            var corners = RedactOverlay.CornersOf(chosen.Areas[0]);
            for (var i = 0; i < corners.Count; i++)
            {
                if (_view.ToView(chosen.Page, corners[i].X, corners[i].Y) is { } at && SelectionOverlay.HitAreaOf(at).Contains(point))
                {
                    var opposite = corners[(i + 2) % 4];
                    Begin(Gesture.Resize, chosen.Id, chosen.Page, point, chosen.Areas[0], opposite);
                    return true;
                }
            }
        }

        // A press on a mark selects it; an area also starts to move.
        var (first, last) = _view.VisiblePages;
        for (var i = _session.Plan.Marks.Count - 1; i >= 0; i--)
        {
            var mark = _session.Plan.Marks[i];
            if (mark.Page < first || mark.Page > last)
            {
                continue;
            }

            foreach (var area in mark.Areas)
            {
                if (_view.ToView(mark.Page, area) is { } rect && rect.Inflate(2).Contains(point))
                {
                    _session.Selection.Select(mark.Id);
                    if (mark.Kind != RedactionKind.Area)
                    {
                        return false;
                    }

                    Begin(Gesture.Move, mark.Id, mark.Page, point, area, (0, 0));
                    return true;
                }
            }
        }

        _session.Selection.Clear();
        if (_view.PageAt(point) is not { } spot)
        {
            return false;
        }

        Begin(Gesture.New, Guid.Empty, spot.Page, point, default, (spot.X, spot.Y));
        return true;
    }

    private void Begin(Gesture gesture, Guid id, int page, Point press, PageRect origin, (double X, double Y) anchor)
    {
        _gesture = gesture;
        _id = id;
        _page = page;
        _press = press;
        _origin = origin;
        _anchor = anchor;
        _started = false;
    }

    private void Move(Point point)
    {
        if (_gesture == Gesture.None || ToPage(_page, point) is not { } at)
        {
            return;
        }

        if (!_started && Math.Abs(point.X - _press.X) < ClickSlop && Math.Abs(point.Y - _press.Y) < ClickSlop)
        {
            return;
        }

        switch (_gesture)
        {
            case Gesture.New:
                var drawn = new PageRect(Math.Min(_anchor.X, at.X), Math.Min(_anchor.Y, at.Y), Math.Max(_anchor.X, at.X), Math.Max(_anchor.Y, at.Y));
                if (!_started)
                {
                    var added = _session.AddArea(_page, drawn);
                    if (added.MarkId is { } id)
                    {
                        _id = id;
                        _started = true;
                    }
                    else if (added.Problem is RedactProblem.TooManyMarks or RedactProblem.NotActive)
                    {
                        Report(added);
                        EndGesture();
                    }
                }
                else
                {
                    _session.ResizeArea(_id, drawn, continuing: true);
                }

                break;
            case Gesture.Resize:
                var resized = new PageRect(Math.Min(_anchor.X, at.X), Math.Min(_anchor.Y, at.Y), Math.Max(_anchor.X, at.X), Math.Max(_anchor.Y, at.Y));
                var versionBeforeResize = _session.Version;
                _session.ResizeArea(_id, resized, continuing: _started);
                _started |= _session.Version != versionBeforeResize;

                break;
            case Gesture.Move:
                if (ToPage(_page, _press) is { } from && _session.Plan.Marks.FirstOrDefault(m => m.Id == _id) is { } mark)
                {
                    var current = mark.Areas[0];
                    var dx = _origin.Left + (at.X - from.X) - current.Left;
                    var dy = _origin.Top + (at.Y - from.Y) - current.Top;
                    var versionBeforeMove = _session.Version;
                    _session.MoveArea(_id, dx, dy, continuing: _started);
                    _started |= _session.Version != versionBeforeMove;
                }

                break;
        }
    }

    private void EndGesture()
    {
        _gesture = Gesture.None;
        _started = false;
        _pointer?.Capture(null);
        _pointer = null;
    }

    // The position on a page, in points, of a point of the view (also outside the page, so a drag that leaves it still sizes the area).
    private (double X, double Y)? ToPage(int page, Point point)
    {
        if (_view.ToView(page, 0, 0) is not { } origin || _view.ToView(page, 1, 1) is not { } unit)
        {
            return null;
        }

        var sx = unit.X - origin.X;
        var sy = unit.Y - origin.Y;
        return sx > 0 && sy > 0 ? ((point.X - origin.X) / sx, (point.Y - origin.Y) / sy) : null;
    }
}
