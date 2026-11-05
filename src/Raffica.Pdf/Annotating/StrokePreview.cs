using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The live shape of the drawing tools: what the user is drawing right now, in page points of the page it started on, drawn over the pages
/// in the pass of the page view. Nothing of it is in the document until the button comes up. Used on the UI thread only.
/// </summary>
internal sealed class StrokePreview : IPageOverlay
{
    private int _page;
    private AnnotationColor _color;
    private double _width;
    private IReadOnlyList<IReadOnlyList<DisplayPoint>> _strokes = [];
    private PageRect? _box;
    private bool _ellipse;

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Whether a shape is shown.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Shows polylines (a freehand stroke, a line, an arrow); the list may be changed by its owner, call <see cref="Invalidate"/> then.</summary>
    /// <param name="page">The page the shape lies on.</param>
    /// <param name="color">The colour.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="strokes">The strokes.</param>
    public void ShowStrokes(int page, AnnotationColor color, double width, IReadOnlyList<IReadOnlyList<DisplayPoint>> strokes)
    {
        (_page, _color, _width, _strokes, _box, _ellipse, IsActive) = (page, color, width, strokes, null, false, true);
        Changed?.Invoke();
    }

    /// <summary>Shows a rectangle or an ellipse outline.</summary>
    /// <param name="page">The page the shape lies on.</param>
    /// <param name="color">The colour.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="box">The box.</param>
    /// <param name="ellipse">Whether the outline is an ellipse.</param>
    public void ShowBox(int page, AnnotationColor color, double width, PageRect box, bool ellipse)
    {
        (_page, _color, _width, _strokes, _box, _ellipse, IsActive) = (page, color, width, [], box, ellipse, true);
        Changed?.Invoke();
    }

    /// <summary>Tells the view that a list given to <see cref="ShowStrokes"/> has changed.</summary>
    public void Invalidate()
    {
        if (IsActive)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Hides the shape.</summary>
    public void Clear()
    {
        if (!IsActive)
        {
            return;
        }

        (_strokes, _box, IsActive) = ([], null, false);
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Render(DrawingContext context, PageView view)
    {
        if (!IsActive || view.ToView(_page, 0, 0) is not { } origin || view.ToView(_page, 1, 0) is not { } unit)
        {
            return;
        }

        var scale = unit.X - origin.X;
        var brush = new ImmutableSolidColorBrush(Color.FromRgb(_color.R, _color.G, _color.B));
        var pen = new ImmutablePen(brush, Math.Max(1, _width * scale), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        if (_box is { } box)
        {
            if (view.ToView(_page, box) is not { } rect)
            {
                return;
            }

            if (_ellipse)
            {
                context.DrawEllipse(null, pen, rect);
            }
            else
            {
                context.DrawRectangle(null, pen, rect);
            }

            return;
        }

        foreach (var stroke in _strokes)
        {
            if (stroke.Count < 2)
            {
                continue;
            }

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                var first = true;
                foreach (var p in stroke)
                {
                    if (view.ToView(_page, p.X, p.Y) is not { } at)
                    {
                        break;
                    }

                    if (first)
                    {
                        g.BeginFigure(at, false);
                        first = false;
                    }
                    else
                    {
                        g.LineTo(at);
                    }
                }

                if (!first)
                {
                    g.EndFigure(false);
                }
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }
}
