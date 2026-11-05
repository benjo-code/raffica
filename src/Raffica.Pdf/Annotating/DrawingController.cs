using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The drawing tools (freehand, rectangle, ellipse, line, arrow). A drag on a page draws a live preview (<see cref="StrokePreview"/>); only
/// when the button comes up the shape is built (a freehand stroke is simplified and smoothed off the UI thread) and made through the
/// editor of the tab. The points live in page points of the page the drag started on and are clamped to it. A drag shorter than 4 DIPs
/// draws nothing. Shift makes a square or circle, or snaps a line or arrow to 45 degree steps. Strokes are applied in release order, one
/// after the other; each holds a pending handle of the tab from its release until it is made, so closing the tab right then still asks.
/// The tool stays active. Used on the UI thread only.
/// </summary>
internal sealed class DrawingController : IToolController
{
    /// <summary>The most points of one freehand stroke; further points are dropped.</summary>
    public const int MaxStrokePoints = 20_000;

    // Closer than this (in points) to the previous point, a point of a stroke is dropped.
    private const double MinPointDistance = 0.25;

    // A drag that never got farther than this (in DIPs) from where the button went down is a click.
    private const double ClickSlop = 4;

    // Douglas-Peucker tolerance in points.
    private const double SimplifyTolerance = 0.5;
    private const double SnapStepDegrees = 45;
    private static Cursor? s_cross;

    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly ToolState _tools;
    private readonly DialogQueue _dialogs;
    private readonly StrokePreview _preview = new();
    private readonly List<DisplayPoint> _points = [];
    private Task _chain = Task.CompletedTask;
    private bool _active;
    private bool _shift;
    private int _page;
    private PdfPageSize _size;
    private AnnotateTool _tool;
    private Point _pressView;
    private double _farthest;
    private DisplayPoint _anchor;
    private DisplayPoint _current;
    private bool _disposed;

    public DrawingController(PdfTab tab, PageView view, ToolState tools, DialogQueue dialogs)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(dialogs);
        _tab = tab;
        _view = view;
        _tools = tools;
        _dialogs = dialogs;
        _preview.Changed += OnPreviewChanged;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Whether a drag is running.</summary>
    public bool IsDrawing => _active;

    /// <summary>The number of points the running freehand stroke holds (for tests).</summary>
    public int PointCount => _points.Count;

    /// <summary>The task that ends when every stroke released so far is made (for tests).</summary>
    public Task Completion => _chain;

    public bool Handles(AnnotateTool tool) =>
        tool is AnnotateTool.Freehand or AnnotateTool.Rectangle or AnnotateTool.Ellipse or AnnotateTool.Line or AnnotateTool.Arrow;

    public void Render(DrawingContext context, PageView view) => _preview.Render(context, view);

    public Cursor? CursorAt(Point point) => s_cross ??= new Cursor(StandardCursorType.Cross);

    public void Press(Point point, int clickCount, KeyModifiers modifiers)
    {
        Reset();
        if (_disposed || !Handles(_tools.Tool) || !_view.FirstSharpPageShown || SpotAt(point) is not { } spot
            || _view.SizeOfPage(spot.Page) is not { } size)
        {
            return;
        }

        _active = true;
        _tool = _tools.Tool;
        _page = spot.Page;
        _size = size;
        _pressView = point;
        _farthest = 0;
        _shift = modifiers.HasFlag(KeyModifiers.Shift);
        _anchor = _current = Clamp(spot.X, spot.Y);
        _points.Add(_anchor);
        UpdatePreview(initial: true);
    }

    public void Move(Point point, KeyModifiers modifiers)
    {
        if (!_active)
        {
            return;
        }

        _shift = modifiers.HasFlag(KeyModifiers.Shift);
        Track(point);
        UpdatePreview(initial: false);
    }

    public void Release(Point point, KeyModifiers modifiers)
    {
        if (!_active)
        {
            return;
        }

        _shift = modifiers.HasFlag(KeyModifiers.Shift);
        Track(point);
        var tool = _tool;
        var page = _page;
        var size = _size;
        var anchor = _anchor;
        var end = _current;
        var shift = _shift;
        var click = _farthest < ClickSlop;
        var points = tool == AnnotateTool.Freehand ? _points.ToArray() : [];
        Reset();
        if (click || _disposed)
        {
            return;
        }

        var style = _tools.StyleOf(tool);
        var color = DrawPalette.ColorOf(style.Color);
        var width = StrokeWidths.PointsOf(style.Width);
        var author = _tools.Author;

        // Taken now, before anything runs off the UI thread: closing the tab waits for it. Released when the stroke is made or failed.
        var pending = _tab.Annotations.BeginPending();
        var build = Task.Run(() => Build(tool, points, anchor, end, shift, size, width));
        _chain = ApplyAsync(_chain, build, page, color, author, pending);
    }

    public bool Key(KeyEventArgs e)
    {
        // Only Shift during a drag of this controller is looked at (it changes the shape); the key itself is never taken.
        if (_active && e.Key is Avalonia.Input.Key.LeftShift or Avalonia.Input.Key.RightShift && !_shift)
        {
            _shift = true;
            UpdatePreview(initial: false);
        }

        return false;
    }

    public bool Cancel()
    {
        var was = _active;
        Reset();
        return was;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Reset();
        _preview.Changed -= OnPreviewChanged;
    }

    /// <summary>
    /// Builds the drawing of a gesture in page points: a freehand stroke is simplified and smoothed, a line or arrow snaps to 45 degrees
    /// and a rectangle or ellipse becomes a square or circle with <paramref name="shift"/>; all of it stays on the page.
    /// </summary>
    /// <param name="tool">A drawing tool.</param>
    /// <param name="points">The points of a freehand stroke.</param>
    /// <param name="anchor">Where the drag started.</param>
    /// <param name="end">Where it ended.</param>
    /// <param name="shift">Whether Shift was held.</param>
    /// <param name="size">The size of the page.</param>
    /// <param name="width">The stroke width in points.</param>
    /// <returns>The geometry, or <see langword="null"/> for none.</returns>
    internal static DrawingGeometry? Build(AnnotateTool tool, IReadOnlyList<DisplayPoint> points, DisplayPoint anchor, DisplayPoint end, bool shift, PdfPageSize size, double width)
    {
        switch (tool)
        {
            case AnnotateTool.Freehand:
                var stroke = StrokeSimplifier.Smooth(StrokeSimplifier.Simplify(points, SimplifyTolerance));
                return DrawingGeometry.Ink([stroke], width);
            case AnnotateTool.Rectangle:
                return DrawingGeometry.Rectangle(BoxOf(anchor, end, shift, size), width);
            case AnnotateTool.Ellipse:
                return DrawingGeometry.Ellipse(BoxOf(anchor, end, shift, size), width);
            case AnnotateTool.Line:
                return DrawingGeometry.Line(anchor, EndOf(anchor, end, shift, size), width);
            case AnnotateTool.Arrow:
                return DrawingGeometry.Arrow(anchor, EndOf(anchor, end, shift, size), width);
            default:
                return null;
        }
    }

    private static PageRect BoxOf(DisplayPoint anchor, DisplayPoint end, bool shift, PdfPageSize size)
    {
        if (!shift)
        {
            return new PageRect(Math.Min(anchor.X, end.X), Math.Min(anchor.Y, end.Y), Math.Max(anchor.X, end.X), Math.Max(anchor.Y, end.Y));
        }

        // The square grows towards the pointer, but only as far as the page allows in both directions.
        var dx = end.X - anchor.X;
        var dy = end.Y - anchor.Y;
        var roomX = dx < 0 ? anchor.X : size.Width - anchor.X;
        var roomY = dy < 0 ? anchor.Y : size.Height - anchor.Y;
        var side = Math.Min(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Min(roomX, roomY));
        return ArrowGeometry.SquareBox(anchor, new DisplayPoint(anchor.X + ((dx < 0 ? -1 : 1) * side), anchor.Y + ((dy < 0 ? -1 : 1) * side)));
    }

    private static DisplayPoint EndOf(DisplayPoint anchor, DisplayPoint end, bool shift, PdfPageSize size)
    {
        if (!shift)
        {
            return end;
        }

        var snapped = ArrowGeometry.SnapToAngle(anchor, end, SnapStepDegrees);

        // Snapping keeps the length and may leave the page: shorten along the ray instead of bending it.
        var factor = 1.0;
        var dx = snapped.X - anchor.X;
        var dy = snapped.Y - anchor.Y;
        if (dx > 0 && anchor.X + dx > size.Width)
        {
            factor = Math.Min(factor, (size.Width - anchor.X) / dx);
        }
        else if (dx < 0 && anchor.X + dx < 0)
        {
            factor = Math.Min(factor, anchor.X / -dx);
        }

        if (dy > 0 && anchor.Y + dy > size.Height)
        {
            factor = Math.Min(factor, (size.Height - anchor.Y) / dy);
        }
        else if (dy < 0 && anchor.Y + dy < 0)
        {
            factor = Math.Min(factor, anchor.Y / -dy);
        }

        return new DisplayPoint(anchor.X + (dx * factor), anchor.Y + (dy * factor));
    }

    private static double Distance(DisplayPoint a, DisplayPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    // Waits for the strokes released before this one, then makes it. Never throws: the handle is always released.
    private async Task ApplyAsync(Task previous, Task<DrawingGeometry?> build, int page, AnnotationColor color, string? author, IDisposable pending)
    {
        string? failure = null;
        try
        {
            await previous;
            if (await build is { } geometry)
            {
                await _tab.Annotations.Editor.ApplyDrawingAsync(page, geometry, color, null, author);
            }
        }
        catch (OperationCanceledException)
        {
            // The document went away or the work was canceled: nothing more to do.
        }
        catch (PdfAnnotationLimitException)
        {
            failure = PdfStrings.AnnotateLimitReached;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = PdfStrings.AnnotateFailed;
        }
        finally
        {
            pending.Dispose();
        }

        if (failure is not null && !_disposed)
        {
            // Not awaited: the card must not hold back the strokes that follow in the chain.
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
            // The card could not be shown; the drawing was not made either way.
        }
    }

    // A press on a page, or in the margin just around it.
    private PageSpot? SpotAt(Point point)
    {
        if (_view.PageAt(point) is { } spot)
        {
            return spot;
        }

        if (_view.NearestSpot(point) is { } near && _view.PageBounds(near.Page) is { } rect && rect.Inflate(ClickSlop * 2).Contains(point))
        {
            return near;
        }

        return null;
    }

    private DisplayPoint Clamp(double x, double y) =>
        new(Math.Clamp(double.IsFinite(x) ? x : 0, 0, _size.Width), Math.Clamp(double.IsFinite(y) ? y : 0, 0, _size.Height));

    private void Track(Point point)
    {
        _farthest = Math.Max(_farthest, Math.Sqrt(Math.Pow(point.X - _pressView.X, 2) + Math.Pow(point.Y - _pressView.Y, 2)));
        if (_view.PageBounds(_page) is not { } rect || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        _current = Clamp((point.X - rect.X) * _size.Width / rect.Width, (point.Y - rect.Y) * _size.Height / rect.Height);
        if (_tool == AnnotateTool.Freehand && _points.Count < MaxStrokePoints && Distance(_current, _points[^1]) >= MinPointDistance)
        {
            _points.Add(_current);
        }
    }

    private void UpdatePreview(bool initial)
    {
        var style = _tools.StyleOf(_tool);
        var color = DrawPalette.ColorOf(style.Color);
        var width = StrokeWidths.PointsOf(style.Width);
        switch (_tool)
        {
            case AnnotateTool.Freehand:
                if (initial)
                {
                    _preview.ShowStrokes(_page, color, width, [_points]);
                }
                else
                {
                    _preview.Invalidate();
                }

                break;
            case AnnotateTool.Rectangle or AnnotateTool.Ellipse:
                _preview.ShowBox(_page, color, width, BoxOf(_anchor, _current, _shift, _size), _tool == AnnotateTool.Ellipse);
                break;
            default:
                _preview.ShowStrokes(_page, color, width, ArrowGeometry.Strokes(_anchor, EndOf(_anchor, _current, _shift, _size), width, _tool == AnnotateTool.Arrow));
                break;
        }
    }

    private void Reset()
    {
        _active = false;
        _points.Clear();
        _preview.Clear();
    }

    private void OnPreviewChanged() => Changed?.Invoke();
}
