using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The select tool: a click picks the topmost selectable mark of a page (<see cref="DrawingHitTest"/>), a drag moves it or, from a handle,
/// resizes it, the arrow keys nudge it, Delete removes it, and a colour or width can be set on it. The selection is held by the
/// <see cref="AnnotationLocator"/> (the name of the mark, never an index) and follows the mark across undo and redo: the cache of the
/// page's annotations is read again when the session reports the page as changed, and the selected mark is looked up by its name.
/// Every change is made through the editor of the tab, one history step each (a drag, a run of nudges within 400 ms, a colour and width
/// together), as a task that holds a pending handle of the tab until it is done, so closing the tab right then still asks. A mark that
/// another program drew in a way Raffica cannot redraw without loss is selected with a lock sign: it can be commented on and deleted,
/// not moved. Tab and Shift+Tab step through the marks of the page and leave the view after the last one. Used on the UI thread only.
/// </summary>
internal sealed class SelectController : IToolController
{
    /// <summary>The pause after the last arrow key before the nudges are made as one step.</summary>
    public static readonly TimeSpan NudgeDelay = TimeSpan.FromMilliseconds(400);

    // A pointer closer than this (in DIPs) to a stroke still hits it.
    private const double HitSlop = 6;

    // A drag that never got farther than this (in DIPs) from where the button went down is a click.
    private const double ClickSlop = 4;
    private const double NudgeStep = 1;
    private const double NudgeStepShift = 10;
    private const int CacheLimit = 32;
    private static readonly Dictionary<StandardCursorType, Cursor> s_cursors = [];

    // The dispatcher of the UI thread this was made on: events that arrive on pool threads post through it, never through the static
    // Dispatcher.UIThread (which may not exist, or be half made, when the UI thread is gone).
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly ToolState _tools;
    private readonly TextBoxController _textBox;
    private readonly DialogQueue _dialogs;
    private readonly TimeProvider _time;
    private readonly SelectionOverlay _overlay = new();
    private readonly Dictionary<int, IReadOnlyList<PdfAnnotation>> _cache = [];

    // The pages a prefetch has queued and that are not read yet: a page is queued once, however often the view changes meanwhile.
    private readonly HashSet<int> _prefetching = [];
    private Task _chain = Task.CompletedTask;
    private PdfAnnotation? _selected;
    private Gesture? _gesture;
    private Nudge? _nudge;
    private KeyEventArgs? _lastOffered;
    private bool _down;
    private long _pressId;
    private int _inFlight;
    private bool _disposed;

    public SelectController(PdfTab tab, PageView view, ToolState tools, TextBoxController textBox, DialogQueue dialogs, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(dialogs);
        _tab = tab;
        _view = view;
        _tools = tools;
        _textBox = textBox;
        _dialogs = dialogs;
        _time = time ?? TimeProvider.System;
        _overlay.Changed += OnOverlayChanged;
        _tools.Changed += OnToolChanged;
        _view.ViewChanged += OnViewChanged;
        _tab.Session.PagesInvalidated += OnPagesInvalidated;
        _tab.Annotations.FlushRequested += OnFlushRequested;
        Prefetch();
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Raised on the UI thread when a mark is selected or the selection is gone.</summary>
    public event Action? SelectionChanged;

    /// <summary>Whether a mark is selected.</summary>
    public bool HasSelection => _selected is not null;

    /// <summary>The selected mark as last read (its geometry follows a drag or a nudge at once), or <see langword="null"/>.</summary>
    public PdfAnnotation? Selected => _selected;

    /// <summary>The reason the selected mark cannot be changed (shown with a lock sign), or <see langword="null"/>.</summary>
    public string? LockReason => _overlay.LockReason;

    /// <summary>Whether the selected mark can take a new colour (and a new width, for a drawing): false without a selection and for a locked mark.</summary>
    public bool CanChangeSelectionStyle => _selected is { } selected && CanChangeStyle(selected);

    /// <summary>The number of handles shown on the selection.</summary>
    public int HandleCount => _overlay.Handles.Count;

    /// <summary>The task that ends when every read and change started so far is done (for tests).</summary>
    public Task Completion => _chain;

    /// <summary>Replaces the read of the marks of a page (tests hold or count the reads); <see langword="null"/>: the document.</summary>
    internal Func<PdfDocument, int, Task<IReadOnlyList<PdfAnnotation>>>? ReadPage { get; set; }

    public bool Handles(AnnotateTool tool) => tool == AnnotateTool.Select;

    public void Render(DrawingContext context, PageView view) => _overlay.Render(context, view);

    public Cursor? CursorAt(Point point)
    {
        if (_gesture is not null)
        {
            return CursorOf(StandardCursorType.SizeAll);
        }

        if (_view.PageAt(point) is not { } spot)
        {
            return null;
        }

        if (HandleAt(point, spot.Page) is { } handle)
        {
            return CursorOf(CursorOfHandle(handle.Kind));
        }

        if (_cache.TryGetValue(spot.Page, out var list)
            && DrawingHitTest.Pick(list, new DisplayPoint(spot.X, spot.Y), ToleranceAt(spot.Page)) is { } hit)
        {
            return CursorOf(CanChange(hit) ? StandardCursorType.SizeAll : StandardCursorType.Hand);
        }

        return null;
    }

    public void Press(Point point, int clickCount, KeyModifiers modifiers)
    {
        _gesture = null;
        _down = true;
        _lastOffered = null;
        if (_disposed)
        {
            return;
        }

        // A text in the editing field ends first (a press away from the field normally took its focus and made the box already).
        _textBox.CommitOrDrop();
        CommitNudge();
        if (_view.PageAt(point) is not { } spot)
        {
            Deselect();
            return;
        }

        if (!_cache.ContainsKey(spot.Page))
        {
            // The marks of the page are not read yet: read them, then handle the press (as a click when the button came up meanwhile).
            var id = ++_pressId;
            EnqueueLoad(spot.Page, () =>
            {
                if (_pressId == id && !_disposed)
                {
                    ApplyPress(point, clickCount, _down);
                }
            });
            return;
        }

        ApplyPress(point, clickCount, true);
    }

    public void Move(Point point, KeyModifiers modifiers)
    {
        if (_gesture is not { } gesture)
        {
            return;
        }

        gesture.Shift = modifiers.HasFlag(KeyModifiers.Shift);
        Track(gesture, point);
    }

    public void Release(Point point, KeyModifiers modifiers)
    {
        _down = false;
        if (_gesture is not { } gesture)
        {
            return;
        }

        gesture.Shift = modifiers.HasFlag(KeyModifiers.Shift);
        Track(gesture, point);
        _gesture = null;

        // A drag that ends where it started, or is clamped to the same box, changes nothing: no history step, the tab stays as it was.
        if (gesture.Farthest >= ClickSlop && gesture.Shape is { } shape && !_disposed && Differs(gesture.Origin, shape))
        {
            CommitGeometry(gesture.Target, shape, _tab.Annotations.BeginPending());
        }

        UpdateOverlay();
    }

    public bool Key(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // The window offers a key to the mode and the focused view offers it again: an event is looked at once.
        if (ReferenceEquals(e, _lastOffered))
        {
            return false;
        }

        _lastOffered = e;

        // The editing field owns its keys (Tab leaves it, Delete and the arrows edit its text), whatever the window decided.
        if (_disposed || _gesture is not null || _textBox.IsEditing || _view.IsInField(e.Source))
        {
            return false;
        }

        var modifiers = e.KeyModifiers;
        var plainOrShift = modifiers is KeyModifiers.None or KeyModifiers.Shift;
        switch (e.Key)
        {
            case Avalonia.Input.Key.Tab when plainOrShift:
                return StepTab(modifiers == KeyModifiers.Shift);
            case Avalonia.Input.Key.Left when plainOrShift:
                return NudgeBy(-NudgeStepOf(modifiers), 0);
            case Avalonia.Input.Key.Right when plainOrShift:
                return NudgeBy(NudgeStepOf(modifiers), 0);
            case Avalonia.Input.Key.Up when plainOrShift:
                return NudgeBy(0, -NudgeStepOf(modifiers));
            case Avalonia.Input.Key.Down when plainOrShift:
                return NudgeBy(0, NudgeStepOf(modifiers));
            case Avalonia.Input.Key.Delete or Avalonia.Input.Key.Back when modifiers == KeyModifiers.None:
                if (_selected is null)
                {
                    return false;
                }

                _ = DeleteSelectionAsync();
                return true;
            case Avalonia.Input.Key.Enter when modifiers == KeyModifiers.None:
                if (_selected is { Kind: AnnotationKind.TextBox, Editable: true, GeometryEditable: true } box)
                {
                    _ = EditTextBoxAsync(LocatorOf(box));
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    public bool Cancel()
    {
        _down = false;
        ++_pressId;
        if (_gesture is not null)
        {
            _gesture = null;
            UpdateOverlay();
            return true;
        }

        CommitNudge();
        if (_selected is not null)
        {
            Deselect();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Selects a mark that is named by a locator (the marks pane does): the marks of its page are read first when they are not known yet.
    /// A mark that cannot be selected (not editable, or gone) leaves the selection as it is. The selection is shown only while the Select
    /// tool is active; the caller chooses the tool. Never throws; the task ends when the selection is made.
    /// </summary>
    /// <param name="locator">The mark; found by name when it has one, else by index.</param>
    /// <returns>The task that ends when the read and the selection are done.</returns>
    public Task SelectAsync(AnnotationLocator locator)
    {
        if (_disposed)
        {
            return _chain;
        }

        EnqueueLoad(locator.Page, () =>
        {
            if (_cache.TryGetValue(locator.Page, out var list)
                && list.FirstOrDefault(a => locator.Name is not null ? a.Name == locator.Name : a.Name is null && a.Index == locator.Index) is { } found
                && DrawingHitTest.IsSelectable(found))
            {
                Select(found);
            }
        });
        return _chain;
    }

    /// <summary>Deletes the selected mark (one history step). Never throws; the task ends when the deletion is made.</summary>
    /// <returns>The task that ends with the deletion.</returns>
    public Task DeleteSelectionAsync()
    {
        if (_disposed || _selected is null)
        {
            return _chain;
        }

        CommitNudge();
        if (_selected is not { } selected)
        {
            return _chain;
        }

        Deselect();
        var locator = LocatorOf(selected);
        Enqueue(selected.Page, _tab.Annotations.BeginPending(), editor => editor.DeleteAsync(locator));
        return _chain;
    }

    /// <summary>
    /// Gives the selected mark a colour, a width, or both (one history step). A markup takes the colour only; a text box takes the colour;
    /// a drawing takes both. A mark that cannot be changed, and a value that is already in force, change nothing. Never throws.
    /// </summary>
    /// <param name="color">The new colour, or <see langword="null"/> to keep it.</param>
    /// <param name="width">The new stroke width in points, or <see langword="null"/> to keep it.</param>
    /// <returns>The task that ends when the change is made.</returns>
    public Task SetSelectionStyleAsync(AnnotationColor? color, double? width)
    {
        if (_disposed || _selected is null)
        {
            return _chain;
        }

        // A nudge in progress is made first and moves the selection to its new place: the edit is built from that, not from the place before.
        CommitNudge();
        if (_selected is not { } selected || !CanChangeStyle(selected))
        {
            return _chain;
        }

        var locator = LocatorOf(selected);
        if (selected.TextBox is { } spec)
        {
            if (color is not { } textColor || textColor == spec.Color)
            {
                return _chain;
            }

            var changed = spec with { Color = textColor };
            Enqueue(selected.Page, _tab.Annotations.BeginPending(), editor => editor.ReplaceTextBoxAsync(locator, changed));
            return _chain;
        }

        var newColor = color is { } c && c != selected.Color ? c : (AnnotationColor?)null;
        DrawingGeometry? newShape = null;
        if (selected.Drawing is { } drawing && width is { } w && double.IsFinite(w) && w > 0 && Math.Abs(w - drawing.Width) > 0.001)
        {
            newShape = WithWidth(drawing, w);
        }

        if (newColor is null && newShape is null)
        {
            return _chain;
        }

        var edit = new AnnotationEdit(Color: newColor, Geometry: newShape);
        Enqueue(selected.Page, _tab.Annotations.BeginPending(), editor => editor.UpdateAsync(locator, edit));
        return _chain;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tools.Changed -= OnToolChanged;
        _view.ViewChanged -= OnViewChanged;
        _tab.Session.PagesInvalidated -= OnPagesInvalidated;
        _tab.Annotations.FlushRequested -= OnFlushRequested;
        _gesture = null;
        if (_nudge is { } nudge)
        {
            _nudge = null;
            nudge.Timer?.Dispose();
            nudge.Pending.Dispose();
        }

        _selected = null;
        _overlay.Changed -= OnOverlayChanged;
        _overlay.Clear();
    }

    private static double NudgeStepOf(KeyModifiers modifiers) => modifiers.HasFlag(KeyModifiers.Shift) ? NudgeStepShift : NudgeStep;

    private static AnnotationLocator LocatorOf(PdfAnnotation a) => new(a.Page, a.Index, a.Name);

    private static bool IsMarkup(AnnotationKind kind) => kind is AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.StrikeOut;

    // A mark Raffica can move, resize and recolour: it has a shape it can redraw without loss.
    // (an imported signature has no Drawing, but its bounds are a shape: SelectionShape)
    private static bool CanChange(PdfAnnotation a) => a.Editable && a.GeometryEditable && a.SelectionShape is not null;

    // An imported signature is a picture: it has neither a colour nor a stroke width to change (it can still be moved and scaled).
    private static bool CanChangeStyle(PdfAnnotation a) =>
        a.Editable && !a.IsSignatureImage && (IsMarkup(a.Kind) || CanChange(a));

    private static PageRect FrameOf(PdfAnnotation a) => a.SelectionShape?.Box ?? a.Bounds;

    private static bool Same(PdfAnnotation a, PdfAnnotation b) =>
        a.Page == b.Page && (a.Name is not null ? a.Name == b.Name : b.Name is null && a.Index == b.Index);

    private static PdfAnnotation WithShape(PdfAnnotation a, DrawingGeometry shape) =>
        a.TextBox is { } spec
            ? a with { TextBox = spec with { Box = shape.Box }, Bounds = shape.Box }
            : a.IsSignatureImage ? a with { Bounds = shape.Box } : a with { Drawing = shape, Bounds = shape.Box };

    private static bool Differs(DrawingGeometry a, DrawingGeometry b) =>
        Math.Abs(a.Box.Left - b.Box.Left) > 0.001 || Math.Abs(a.Box.Top - b.Box.Top) > 0.001
        || Math.Abs(a.Box.Right - b.Box.Right) > 0.001 || Math.Abs(a.Box.Bottom - b.Box.Bottom) > 0.001;

    // The same drawing with another stroke width; null for a shape that cannot be rebuilt from what it holds.
    private static DrawingGeometry? WithWidth(DrawingGeometry g, double width) => g.Kind switch
    {
        AnnotationKind.Ink => DrawingGeometry.Ink(g.Strokes, width),
        AnnotationKind.Signature => DrawingGeometry.Signature(g.Strokes, width),
        AnnotationKind.Rectangle => DrawingGeometry.Rectangle(g.Box, width),
        AnnotationKind.Ellipse => DrawingGeometry.Ellipse(g.Box, width),
        AnnotationKind.Line when g.Strokes.Count > 0 && g.Strokes[0].Count == 2 => DrawingGeometry.Line(g.Strokes[0][0], g.Strokes[0][1], width),
        AnnotationKind.Arrow when g.Strokes.Count > 0 && g.Strokes[0].Count == 2 => DrawingGeometry.Arrow(g.Strokes[0][0], g.Strokes[0][1], width),
        _ => null,
    };

    private static StandardCursorType CursorOfHandle(HandleKind kind) => kind switch
    {
        HandleKind.TopLeft => StandardCursorType.TopLeftCorner,
        HandleKind.Top => StandardCursorType.TopSide,
        HandleKind.TopRight => StandardCursorType.TopRightCorner,
        HandleKind.Right => StandardCursorType.RightSide,
        HandleKind.BottomRight => StandardCursorType.BottomRightCorner,
        HandleKind.Bottom => StandardCursorType.BottomSide,
        HandleKind.BottomLeft => StandardCursorType.BottomLeftCorner,
        HandleKind.Left => StandardCursorType.LeftSide,
        _ => StandardCursorType.Cross,
    };

    private static Cursor CursorOf(StandardCursorType type)
    {
        if (!s_cursors.TryGetValue(type, out var cursor))
        {
            s_cursors[type] = cursor = new Cursor(type);
        }

        return cursor;
    }

    // The tolerance of a pick in page points: 6 DIPs at the zoom of the page (the half stroke width is added by the hit test).
    private double ToleranceAt(int page) =>
        _view.ToView(page, 0, 0) is { } origin && _view.ToView(page, 1, 0) is { } unit && unit.X - origin.X > 0.0001 ? HitSlop / (unit.X - origin.X) : HitSlop;

    private PageRect? PageRectOf(int page) => _view.SizeOfPage(page) is { } size ? new PageRect(0, 0, size.Width, size.Height) : null;

    private void ApplyPress(Point point, int clickCount, bool allowDrag)
    {
        if (_view.PageAt(point) is not { } spot || !_cache.TryGetValue(spot.Page, out var list))
        {
            return;
        }

        // A handle of the selection first.
        if (allowDrag && _selected is { } current && current.Page == spot.Page && CanChange(current) && HandleAt(point, spot.Page) is { } handle
            && current.SelectionShape is { } origin && _view.SizeOfPage(spot.Page) is { } size)
        {
            _gesture = new Gesture(
                handle.Kind is HandleKind.Start or HandleKind.End ? GestureKind.Endpoint : GestureKind.Resize,
                current, origin, spot.Page, size, point, new DisplayPoint(spot.X, spot.Y), handle.Kind);
            return;
        }

        var picked = DrawingHitTest.Pick(list, new DisplayPoint(spot.X, spot.Y), ToleranceAt(spot.Page));
        if (picked is null)
        {
            Deselect();
            return;
        }

        Select(picked);
        if (clickCount >= 2 && picked is { Kind: AnnotationKind.TextBox, Editable: true, GeometryEditable: true })
        {
            _ = EditTextBoxAsync(LocatorOf(picked));
            return;
        }

        if (allowDrag && CanChange(picked) && picked.SelectionShape is { } shape && _view.SizeOfPage(spot.Page) is { } pageSize)
        {
            _gesture = new Gesture(GestureKind.Move, picked, shape, spot.Page, pageSize, point, new DisplayPoint(spot.X, spot.Y), null);
        }
    }

    // The handle of the selection under a point of the view (24 DIPs around each), the nearest one.
    private Handle? HandleAt(Point point, int page)
    {
        if (_selected is not { } selected || selected.Page != page || !CanChange(selected))
        {
            return null;
        }

        Handle? best = null;
        var bestDistance = double.MaxValue;
        foreach (var handle in SelectionHandles.For(Shown(selected)))
        {
            if (_view.ToView(page, handle.Position.X, handle.Position.Y) is not { } at || !SelectionOverlay.HitAreaOf(at).Contains(point))
            {
                continue;
            }

            var distance = ((at.X - point.X) * (at.X - point.X)) + ((at.Y - point.Y) * (at.Y - point.Y));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = handle;
            }
        }

        return best;
    }

    private void Track(Gesture gesture, Point point)
    {
        gesture.Farthest = Math.Max(gesture.Farthest, Math.Sqrt(Math.Pow(point.X - gesture.PressView.X, 2) + Math.Pow(point.Y - gesture.PressView.Y, 2)));
        if (_view.PageBounds(gesture.Page) is not { } rect || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var to = new DisplayPoint((point.X - rect.X) * gesture.Size.Width / rect.Width, (point.Y - rect.Y) * gesture.Size.Height / rect.Height);
        if (gesture.Farthest < ClickSlop)
        {
            return;
        }

        var page = new PageRect(0, 0, gesture.Size.Width, gesture.Size.Height);
        switch (gesture.Kind)
        {
            case GestureKind.Move:
                gesture.Shape = DrawingTransform.Move(gesture.Origin, to.X - gesture.Anchor.X, to.Y - gesture.Anchor.Y, page);
                break;
            case GestureKind.Resize:
                var box = SelectionHandles.ResizeBy(gesture.Origin.Box, gesture.Handle!.Value, to, SelectionHandles.KeepsAspect(gesture.Target, gesture.Shift), page);
                gesture.Shape = DrawingTransform.Resize(gesture.Origin, box, page);
                break;
            default:
                gesture.Shape = DrawingTransform.MoveEndpoint(gesture.Origin, gesture.Handle == HandleKind.Start, to, page);
                break;
        }

        UpdateOverlay();
    }

    private void Select(PdfAnnotation annotation)
    {
        CommitNudge();
        var changed = _selected is null || !Same(_selected, annotation);
        _selected = annotation;
        UpdateOverlay();
        if (changed)
        {
            SelectionChanged?.Invoke();
        }
    }

    private void Deselect()
    {
        CommitNudge();
        _gesture = null;
        if (_selected is null)
        {
            return;
        }

        _selected = null;
        UpdateOverlay();
        SelectionChanged?.Invoke();
    }

    // The selected mark as it is shown now: with the shape of a running drag or nudge.
    private PdfAnnotation Shown(PdfAnnotation annotation)
    {
        if (_gesture is { Shape: { } dragged })
        {
            return WithShape(annotation, dragged);
        }

        return _nudge is { } nudge && Same(nudge.Target, annotation) ? WithShape(annotation, nudge.Shape) : annotation;
    }

    private void UpdateOverlay()
    {
        if (_selected is not { } selected)
        {
            _overlay.Clear();
            return;
        }

        var shown = Shown(selected);
        var ghost = _gesture?.Shape ?? (_nudge is { } nudge && Same(nudge.Target, selected) ? nudge.Shape : null);
        var locked = !IsMarkup(shown.Kind) && !CanChange(shown) && shown.Editable;
        _overlay.Show(
            shown.Page,
            FrameOf(shown),
            locked ? [] : SelectionHandles.For(shown),
            locked ? PdfStrings.SelectLockedReason : null,
            ghost,
            shown.Color ?? new AnnotationColor(0, 0, 0));
    }

    private bool StepTab(bool back)
    {
        CommitNudge();
        var page = _selected?.Page ?? _view.CurrentPage;
        if (!_cache.TryGetValue(page, out var list))
        {
            return false;
        }

        var order = list.Where(DrawingHitTest.IsSelectable)
            .OrderBy(a => FrameOf(a).Top).ThenBy(a => FrameOf(a).Left).ThenBy(a => a.Index).ToList();
        var at = _selected is null ? -1 : order.FindIndex(a => Same(a, _selected));
        var next = back ? (at < 0 ? order.Count - 1 : at - 1) : at + 1;
        if (next >= 0 && next < order.Count)
        {
            Select(order[next]);
            return true;
        }

        // After the last (or before the first) one the selection is gone and the key goes on to the next control.
        Deselect();
        return false;
    }

    private bool NudgeBy(double dx, double dy)
    {
        if (_selected is not { } selected || !CanChange(selected) || selected.SelectionShape is not { } shape || PageRectOf(selected.Page) is not { } page)
        {
            return false;
        }

        if (_nudge is null || !Same(_nudge.Target, selected))
        {
            CommitNudge();
            _nudge = new Nudge(selected, shape, _tab.Annotations.BeginPending());
        }

        var nudge = _nudge;
        nudge.Shape = DrawingTransform.Move(nudge.Shape, dx, dy, page);
        nudge.Timer?.Dispose();
        nudge.Timer = _time.CreateTimer(
            _ => _dispatcher.Post(() =>
            {
                if (ReferenceEquals(_nudge, nudge))
                {
                    CommitNudge();
                }
            }),
            null,
            NudgeDelay,
            Timeout.InfiniteTimeSpan);
        UpdateOverlay();
        return true;
    }

    // Makes the nudges so far as one step (a pause, another key, a save or an undo is about to wait for them).
    private void CommitNudge()
    {
        if (_nudge is not { } nudge)
        {
            return;
        }

        _nudge = null;
        nudge.Timer?.Dispose();
        if (_disposed || !Differs(nudge.Origin, nudge.Shape))
        {
            nudge.Pending.Dispose();
            UpdateOverlay();
            return;
        }

        CommitGeometry(nudge.Target, nudge.Shape, nudge.Pending);
    }

    private void CommitGeometry(PdfAnnotation target, DrawingGeometry shape, IDisposable pending)
    {
        var locator = LocatorOf(target);
        var spec = target.TextBox is { } box ? box with { Box = shape.Box } : null;

        // The overlay and the next key already work on the new place; the read after the change brings the truth.
        if (_selected is { } selected && Same(selected, target))
        {
            _selected = WithShape(target, shape);
            PatchCache(_selected);
            UpdateOverlay();
        }

        Enqueue(target.Page, pending, editor => spec is not null ? editor.ReplaceTextBoxAsync(locator, spec) : editor.ReplaceGeometryAsync(locator, shape));
    }

    // The cache shows a mark where it is going to be, so a press right after a nudge or drag hits and drags the new place.
    private void PatchCache(PdfAnnotation annotation)
    {
        if (_cache.TryGetValue(annotation.Page, out var list))
        {
            _cache[annotation.Page] = list.Select(a => Same(a, annotation) && a.Kind == annotation.Kind ? annotation : a).ToList();
        }
    }

    private async Task EditTextBoxAsync(AnnotationLocator locator)
    {
        try
        {
            CommitNudge();
            await _tab.Annotations.FlushAsync();
            await _textBox.EditAsync(locator);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The text box went away meanwhile; no field opens.
        }
    }

    // Puts a change in the queue: it runs after the ones before it, holds the pending handle until it is done, and reads the page again.
    private void Enqueue(int page, IDisposable pending, Func<AnnotationEditor, Task> work)
    {
        _inFlight++;
        _chain = RunAsync(_chain, page, pending, work);
    }

    private async Task RunAsync(Task previous, int page, IDisposable pending, Func<AnnotationEditor, Task> work)
    {
        string? failure = null;
        try
        {
            await previous;
            await work(_tab.Annotations.Editor);
        }
        catch (OperationCanceledException)
        {
            // The document went away or the work was canceled: nothing more to do.
        }
        catch (PdfAnnotationLimitException)
        {
            failure = PdfStrings.AnnotateLimitReached;
        }
        catch (Exception ex) when (ex is PdfAnnotationNotFoundException or InvalidOperationException or ArgumentException)
        {
            // The mark is gone (undone meanwhile) or cannot be changed any more: the read below shows how it is.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = PdfStrings.SelectFailed;
        }

        try
        {
            _inFlight--;
            await LoadAsync(page);
        }
        finally
        {
            pending.Dispose();
        }

        if (failure is not null && !_disposed)
        {
            _ = ShowNoticeAsync(failure);
        }
    }

    private async Task ShowNoticeAsync(string message)
    {
        try
        {
            await _dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.AnnotateCardTitle, message, () => done(true)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The card could not be shown; the change was not made either way.
        }
    }

    private void EnqueueLoad(int page, Action? then = null, bool speculative = false) => _chain = LoadChainedAsync(_chain, page, then, speculative);

    private async Task LoadChainedAsync(Task previous, int page, Action? then, bool speculative)
    {
        await previous;

        // A page that was only queued ahead of need is read if it is still wanted when its turn comes: a fast scroll must not leave a press
        // waiting behind the reads of pages that are long gone.
        if (speculative)
        {
            _prefetching.Remove(page);
            var (first, last) = _view.VisiblePages;
            if (_disposed || _tools.Tool != AnnotateTool.Select || ((page < first || page > last) && _selected?.Page != page))
            {
                return;
            }
        }

        await LoadAsync(page);
        if (then is not null && !_disposed)
        {
            then();
        }
    }

    // Reads the marks of a page into the cache and finds the selection again by its name. UI thread.
    private async Task LoadAsync(int page)
    {
        if (_disposed || _tab.Session.Document is not { } document)
        {
            return;
        }

        IReadOnlyList<PdfAnnotation> list;
        try
        {
            list = await (ReadPage?.Invoke(document, page) ?? document.GetAnnotationsAsync(page));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The document went away or the page cannot be read: the cache keeps what it had.
            return;
        }

        // A change is still on its way: this read may show the place before it. The read after the change brings the truth.
        if (_disposed || _inFlight > 0)
        {
            return;
        }

        _cache[page] = list;
        if (_selected is { } selected && selected.Page == page)
        {
            var found = list.FirstOrDefault(a => Same(a, selected) && a.Kind == selected.Kind);
            if (found is null)
            {
                Deselect();
            }
            else
            {
                _selected = found;
                UpdateOverlay();
            }
        }
    }

    private void Prefetch()
    {
        if (_disposed || _tools.Tool != AnnotateTool.Select)
        {
            return;
        }

        var (first, last) = _view.VisiblePages;
        for (var page = first; page <= last; page++)
        {
            if (!_cache.ContainsKey(page) && _prefetching.Add(page))
            {
                EnqueueLoad(page, speculative: true);
            }
        }

        if (_cache.Count > CacheLimit)
        {
            foreach (var key in _cache.Keys.Where(k => (k < first - 2 || k > last + 2) && _selected?.Page != k).ToList())
            {
                _cache.Remove(key);
            }
        }
    }

    private void OnToolChanged()
    {
        if (_tools.Tool == AnnotateTool.Select)
        {
            Prefetch();
        }
    }

    private void OnViewChanged() => Prefetch();

    // Any thread: only posts. The marks of the pages are read again (the selection follows its name), and never waited for here.
    private void OnPagesInvalidated(IReadOnlySet<int> pages)
    {
        var copy = pages.ToArray();
        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            foreach (var page in copy)
            {
                if (_cache.ContainsKey(page) || _selected?.Page == page)
                {
                    EnqueueLoad(page);
                }
            }
        });
    }

    private void OnFlushRequested() => CommitNudge();

    private void OnOverlayChanged() => Changed?.Invoke();

    private enum GestureKind
    {
        Move,
        Resize,
        Endpoint,
    }

    private sealed class Gesture(GestureKind kind, PdfAnnotation target, DrawingGeometry origin, int page, PdfPageSize size, Point pressView, DisplayPoint anchor, HandleKind? handle)
    {
        public GestureKind Kind { get; } = kind;

        public PdfAnnotation Target { get; } = target;

        public DrawingGeometry Origin { get; } = origin;

        public int Page { get; } = page;

        public PdfPageSize Size { get; } = size;

        public Point PressView { get; } = pressView;

        public DisplayPoint Anchor { get; } = anchor;

        public HandleKind? Handle { get; } = handle;

        public double Farthest { get; set; }

        public bool Shift { get; set; }

        public DrawingGeometry? Shape { get; set; }
    }

    private sealed class Nudge(PdfAnnotation target, DrawingGeometry origin, IDisposable pending)
    {
        public PdfAnnotation Target { get; } = target;

        public DrawingGeometry Origin { get; } = origin;

        public IDisposable Pending { get; } = pending;

        public DrawingGeometry Shape { get; set; } = origin;

        public ITimer? Timer { get; set; }
    }
}
