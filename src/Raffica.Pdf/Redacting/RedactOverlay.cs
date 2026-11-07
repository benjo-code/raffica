using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Redacting;

/// <summary>
/// The marks of the redact plan, drawn over the pages in the one pass of the page view (never part of the file, never printed). A mark is
/// hatched, not filled, so the text below stays readable while the user decides, and has an edge of a light line under a dark one, which reads
/// at 3:1 on a light page and on a dark one; it never rests on colour alone. A selected mark gets a heavier dashed frame, and a selected area
/// four corner handles. The shell tokens only (<c>Raffica.Brush.Ink</c>, <c>Raffica.Brush.Surface.Band</c>). Used on the UI thread only.
/// </summary>
internal sealed class RedactOverlay : IPageOverlay, IDisposable
{
    /// <summary>The distance between two hatch lines, in DIPs.</summary>
    private const double HatchStep = 6;

    private readonly RedactSession _session;

    public RedactOverlay(RedactSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        session.Changed += OnChanged;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>The name of a mark for the list and for assistive technology: the page, never the marked text.</summary>
    public static string NameOf(RedactionMark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        return PdfStrings.RedactMarkNameFormat.Replace("{0}", (mark.Page + 1).ToString(PdfStrings.Culture), StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Render(DrawingContext context, PageView view)
    {
        if (!_session.IsActive || _session.Plan.Marks.Count == 0)
        {
            return;
        }

        var (first, last) = view.VisiblePages;
        if (last < first)
        {
            return;
        }

        var ink = BrushOf(view, "Raffica.Brush.Ink", Colors.Black);
        var band = BrushOf(view, "Raffica.Brush.Surface.Band", Colors.White);
        var hatch = new Pen(ink, 1);
        var edgeUnder = new Pen(band, 4);
        var edgeOver = new Pen(ink, 2);
        var selectedUnder = new Pen(band, 5);
        var selectedOver = new Pen(ink, 2.5, new ImmutableDashStyle([4, 3], 0));
        var selection = _session.Selection;
        foreach (var mark in _session.Plan.Marks)
        {
            if (mark.Page < first || mark.Page > last)
            {
                continue;
            }

            var selected = selection.Contains(mark.Id);
            foreach (var area in mark.Areas)
            {
                if (view.ToView(mark.Page, area) is not { } rect)
                {
                    continue;
                }

                DrawHatched(context, rect, band, hatch);
                context.DrawRectangle(null, edgeUnder, rect);
                context.DrawRectangle(null, edgeOver, rect);
                if (selected)
                {
                    var frame = rect.Inflate(3);
                    context.DrawRectangle(null, selectedUnder, frame);
                    context.DrawRectangle(null, selectedOver, frame);
                }
            }

            if (selected && mark.Kind == RedactionKind.Area && selection.Count == 1 && mark.Areas.Count == 1)
            {
                DrawHandles(context, view, mark.Page, mark.Areas[0], ink, band);
            }
        }
    }

    public void Dispose() => _session.Changed -= OnChanged;

    /// <summary>The four corners of an area in page points, clockwise from the top left.</summary>
    internal static IReadOnlyList<(double X, double Y)> CornersOf(PageRect area) =>
        [(area.Left, area.Top), (area.Right, area.Top), (area.Right, area.Bottom), (area.Left, area.Bottom)];

    private static void DrawHatched(DrawingContext context, Rect rect, IBrush band, IPen hatch)
    {
        using (context.PushOpacity(0.5))
        {
            context.DrawRectangle(band, null, rect);
        }

        using (context.PushClip(rect))
        {
            // Lines at 45 degrees from the bottom left to the top right, a step apart.
            for (var offset = -rect.Height; offset < rect.Width; offset += HatchStep)
            {
                context.DrawLine(hatch, new Point(rect.Left + offset, rect.Bottom), new Point(rect.Left + offset + rect.Height, rect.Top));
            }
        }
    }

    private static void DrawHandles(DrawingContext context, PageView view, int page, PageRect area, IBrush ink, IBrush band)
    {
        var pen = new Pen(ink, 1.5);
        foreach (var (x, y) in CornersOf(area))
        {
            if (view.ToView(page, x, y) is not { } at)
            {
                continue;
            }

            var square = new Rect(at.X - (SelectionOverlay.HandleDraw / 2), at.Y - (SelectionOverlay.HandleDraw / 2), SelectionOverlay.HandleDraw, SelectionOverlay.HandleDraw);
            context.DrawRectangle(band, pen, square);
        }
    }

    private static IBrush BrushOf(PageView view, string key, Color fallback) =>
        view.TryFindResource(key, view.ActualThemeVariant, out var value) && value is IBrush brush ? brush : new ImmutableSolidColorBrush(fallback);

    private void OnChanged() => Changed?.Invoke();
}
