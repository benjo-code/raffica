using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Core.Annotations;

namespace Raffica.Pdf.Signing;

/// <summary>
/// The drawing area of the new-signature card: a canvas (not the page) on which a mouse, a pen or a finger draws strokes, with a guide line
/// to write on. A stroke is thinned with <see cref="StrokeSimplifier"/> when the button comes up; <see cref="Strokes"/> are in the pad's
/// own DIP coordinates (the store moves and scales them into the unit square). A press without a move (a tap) draws nothing. The pad takes at
/// most <see cref="MaxPoints"/> points in all, further points of a stroke are dropped (the store accepts 10 000). Pressure is ignored.
/// Used on the UI thread only.
/// </summary>
internal sealed class SignaturePad : Control
{
    /// <summary>The most points the pad keeps before thinning (the store's own limit is higher, so a thinned drawing always fits).</summary>
    public const int MaxPoints = 8_000;

    /// <summary>The most strokes the pad keeps.</summary>
    public const int MaxStrokes = 200;

    // Closer than this (in DIPs) to the previous point, a point is dropped; the Douglas-Peucker tolerance is the same size.
    private const double MinPointDistance = 0.75;
    private const double SimplifyTolerance = 0.6;
    private const double InkWidth = 2.5;
    private const double GuideRatio = 0.72;

    private readonly List<IReadOnlyList<DisplayPoint>> _strokes = [];
    private readonly List<DisplayPoint> _current = [];
    private int _total;
    private IPointer? _drawing;
    private DrawColor _color = DrawColor.Black;

    public SignaturePad()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = false;
        ClipToBounds = true;
        MinHeight = 80;
        AutomationProperties.SetAutomationId(this, "SignaturePad");
    }

    /// <summary>Raised when the strokes or the colour change.</summary>
    public event Action? Changed;

    /// <summary>Gets or sets the ink (black by default).</summary>
    public DrawColor Color
    {
        get => _color;
        set
        {
            if (_color == value)
            {
                return;
            }

            _color = value;
            InvalidateVisual();
            Changed?.Invoke();
        }
    }

    /// <summary>Gets a copy of the finished strokes in pad coordinates (the stroke being drawn is not in it).</summary>
    public IReadOnlyList<IReadOnlyList<DisplayPoint>> Strokes => [.. _strokes.Select(s => (IReadOnlyList<DisplayPoint>)[.. s])];

    /// <summary>Gets whether a finished stroke exists.</summary>
    public bool HasInk => _strokes.Count > 0;

    /// <summary>Removes the last stroke (nothing happens on an empty pad).</summary>
    public void Back()
    {
        if (_strokes.Count == 0)
        {
            return;
        }

        _total -= _strokes[^1].Count;
        _strokes.RemoveAt(_strokes.Count - 1);
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <summary>Removes every stroke (nothing happens on an empty pad).</summary>
    public void Clear()
    {
        if (_strokes.Count == 0)
        {
            return;
        }

        _strokes.Clear();
        _total = 0;
        InvalidateVisual();
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(Find("Raffica.Brush.Surface.Page"), new Pen(Find("Raffica.Brush.Border.Control"), 1), bounds.Deflate(0.5));
        var guideY = Math.Round(bounds.Height * GuideRatio) + 0.5;
        context.DrawLine(new Pen(Find("Raffica.Brush.Rule"), 1, new DashStyle([4, 3], 0)), new Point(12, guideY), new Point(bounds.Width - 12, guideY));

        var rgb = DrawPalette.ColorOf(_color);
        var pen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B)), InkWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        foreach (var stroke in _strokes)
        {
            DrawStroke(context, pen, stroke);
        }

        DrawStroke(context, pen, _current);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_drawing is not null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _strokes.Count >= MaxStrokes)
        {
            return;
        }

        _drawing = e.Pointer;
        e.Pointer.Capture(this);
        _current.Clear();
        Add(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drawing is null || !ReferenceEquals(e.Pointer, _drawing))
        {
            return;
        }

        Add(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drawing is null || !ReferenceEquals(e.Pointer, _drawing))
        {
            return;
        }

        Add(e.GetPosition(this));
        e.Pointer.Capture(null);
        Finish();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drawing is not null && ReferenceEquals(e.Pointer, _drawing))
        {
            Finish();
        }
    }

    private static void DrawStroke(DrawingContext context, Pen pen, IReadOnlyList<DisplayPoint> stroke)
    {
        if (stroke.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(stroke[0].X, stroke[0].Y), false);
            for (var i = 1; i < stroke.Count; i++)
            {
                g.LineTo(new Point(stroke[i].X, stroke[i].Y));
            }

            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void Add(Point point)
    {
        if (_total + _current.Count >= MaxPoints)
        {
            return;
        }

        var next = new DisplayPoint(point.X, point.Y);
        if (_current.Count > 0)
        {
            var last = _current[^1];
            if (Math.Abs(last.X - next.X) < MinPointDistance && Math.Abs(last.Y - next.Y) < MinPointDistance)
            {
                return;
            }
        }

        _current.Add(next);
        InvalidateVisual();
    }

    private void Finish()
    {
        _drawing = null;
        var points = _current.ToArray();
        _current.Clear();
        if (points.Length >= 2)
        {
            _strokes.Add(StrokeSimplifier.Simplify(points, SimplifyTolerance));
            _total += _strokes[^1].Count;
            Changed?.Invoke();
        }

        InvalidateVisual();
    }

    private IBrush Find(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Transparent;
}
