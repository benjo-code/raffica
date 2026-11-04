using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Raffica.Core.Text;
using Raffica.Core.Viewing;
using Raffica.Pdf.Text;

namespace Raffica.Pdf.Viewing;

/// <summary>A place on a page: the page and the position in points from the top left of the displayed page (as <see cref="PageRect"/> counts).</summary>
internal readonly record struct PageSpot(int Page, double X, double Y);

/// <summary>
/// The text side of the page view: the mapping between the view and the points of a page, the marks drawn over the pages (search hits and
/// the selection, in the one drawing pass of <c>Render</c>) and the jumps to a destination or a rectangle. The page view owns no text:
/// <see cref="Search"/> and <see cref="Selection"/> hold it and say when it changed.
/// </summary>
internal sealed partial class PageView
{
    // Opacity of the ink over the page: the hits are light, the current hit and the selection are stronger.
    private const double HitOpacity = 0.22;
    private const double CurrentHitOpacity = 0.5;

    // How far a revealed rectangle keeps from the edge of the view, in DIPs.
    private const double RevealMargin = 24;

    private SearchSession? _search;
    private IBrush? _inkSource;
    private IBrush? _hitBrush;
    private IBrush? _currentBrush;
    private IPen? _currentPen;

    /// <summary>The search whose hits are drawn; the view scrolls to the current hit whenever the session says it changed.</summary>
    internal SearchSession? Search
    {
        get => _search;
        set
        {
            if (ReferenceEquals(value, _search))
            {
                return;
            }

            if (_search is not null)
            {
                _search.Changed -= InvalidateVisual;
                _search.CurrentChanged -= RevealCurrentHit;
            }

            _search = value;
            if (value is not null)
            {
                value.Changed += InvalidateVisual;
                value.CurrentChanged += RevealCurrentHit;
            }

            InvalidateVisual();
        }
    }

    /// <summary>The page and the position in points under a point of the view, or null when the point is on no page (or the page size is not known yet).</summary>
    internal PageSpot? PageAt(Point point)
    {
        var (first, last) = VisiblePages;
        for (var page = first; page <= last; page++)
        {
            if (PageBounds(page) is { } rect && rect.Contains(point))
            {
                return SpotOf(page, rect, point);
            }
        }

        return null;
    }

    /// <summary>
    /// The position on the visible page nearest to a point of the view; the position may lie outside the page (a drag that leaves the pages).
    /// Null when no page is visible.
    /// </summary>
    internal PageSpot? NearestSpot(Point point)
    {
        var (first, last) = VisiblePages;
        PageSpot? best = null;
        var bestDistance = double.PositiveInfinity;
        for (var page = first; page <= last; page++)
        {
            if (PageBounds(page) is not { } rect)
            {
                continue;
            }

            var dx = Math.Max(Math.Max(rect.Left - point.X, 0), point.X - rect.Right);
            var dy = Math.Max(Math.Max(rect.Top - point.Y, 0), point.Y - rect.Bottom);
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance && SpotOf(page, rect, point) is { } spot)
            {
                bestDistance = distance;
                best = spot;
            }
        }

        return best;
    }

    /// <summary>The place of a position of a page in the view, or null when the page is not shown or its size is not known.</summary>
    internal Point? ToView(int page, double x, double y)
    {
        if (_layout is null || PageBounds(page) is not { } rect || !_layout.IsSizeKnown(page))
        {
            return null;
        }

        var size = _layout.GetPageSize(page);
        return new Point(rect.X + (x * rect.Width / size.Width), rect.Y + (y * rect.Height / size.Height));
    }

    /// <summary>The place of a rectangle of a page in the view, or null when the page is not shown or its size is not known.</summary>
    internal Rect? ToView(int page, PageRect rect)
    {
        if (ToView(page, rect.Left, rect.Top) is not { } topLeft || ToView(page, rect.Right, rect.Bottom) is not { } bottomRight)
        {
            return null;
        }

        return new Rect(topLeft, bottomRight);
    }

    /// <summary>
    /// Scrolls to a destination of a page: <paramref name="top"/> points from the top of the page (null or at the top: the page itself,
    /// as <see cref="GoToPage"/>), with one margin of context above it. In the single-page layout the page is shown.
    /// </summary>
    internal void GoToDestination(int page, double? top)
    {
        if (_layout is null || _layout.PageCount == 0)
        {
            return;
        }

        page = Math.Clamp(page, 0, _layout.PageCount - 1);
        if (top is not { } t || !double.IsFinite(t) || t <= 0 || !_layout.IsSizeKnown(page))
        {
            GoToPage(page);
            return;
        }

        if (_layoutMode == PageLayoutMode.SinglePage)
        {
            _layout.ShowPage(page);
        }

        var height = _layout.GetPageSize(page).Height;
        var fraction = height > 0 ? Math.Clamp(t / height, 0, 1) : 0;
        CancelAnimation();
        _anchor = _layout.GetAnchor(Math.Max(0, _layout.GetOffset(new PageAnchor(page, fraction)) - _layout.PageMargin));
        _hasAnchor = true;
        _navigatedPage = page;
        Relayout();
    }

    /// <summary>
    /// Brings a rectangle of a page into view: nothing moves while it is inside the view (with a margin); otherwise the view scrolls so
    /// that the rectangle lies in the middle, in the direction that was out. In the single-page layout the page is shown first.
    /// </summary>
    internal void Reveal(int page, PageRect rect)
    {
        if (_layout is null || _layout.PageCount == 0 || page < 0 || page >= _layout.PageCount)
        {
            return;
        }

        if (!_layout.IsShown(page) || !_layout.IsSizeKnown(page))
        {
            GoToPage(page);
        }

        if (!_layout.IsSizeKnown(page) || _viewport.Width <= 0 || _viewport.Height <= 0)
        {
            return;
        }

        var size = _layout.GetPageSize(page);
        var area = _layout.GetPageRect(page);
        var scaleX = area.Width / size.Width;
        var scaleY = area.Height / size.Height;

        // In the coordinates of the content (the offsets are measured from them).
        var left = area.X + LeftGap() + (rect.Left * scaleX);
        var right = area.X + LeftGap() + (rect.Right * scaleX);
        var top = area.Y + (rect.Top * scaleY);
        var bottom = area.Y + (rect.Bottom * scaleY);
        var x = _offsetX;
        var y = _offsetY;
        if (top < _offsetY + RevealMargin || bottom > _offsetY + _viewport.Height - RevealMargin)
        {
            y = ((top + bottom) / 2) - (_viewport.Height / 2);
        }

        if (left < _offsetX + RevealMargin || right > _offsetX + _viewport.Width - RevealMargin)
        {
            x = ((left + right) / 2) - (_viewport.Width / 2);
        }

        if (x != _offsetX || y != _offsetY)
        {
            CancelAnimation();
            ScrollTo(x, y);
        }
    }

    private PageSpot? SpotOf(int page, Rect rect, Point point)
    {
        if (_layout is null || !_layout.IsSizeKnown(page) || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        var size = _layout.GetPageSize(page);
        return new PageSpot(page, (point.X - rect.X) * size.Width / rect.Width, (point.Y - rect.Y) * size.Height / rect.Height);
    }

    private void RevealCurrentHit()
    {
        if (_search?.CurrentHit is { Rects.Count: > 0 } hit)
        {
            Reveal(hit.PageIndex, hit.Rects[0]);
        }
    }

    // The marks of one page in the pass that draws the page: the hits, the current hit on top of them, the selection last. The brushes
    // are made again only when the ink changed (the theme, the app ink), so a pass allocates nothing for them.
    private void DrawMarks(DrawingContext context, int page, Rect rect)
    {
        var hits = _search?.HitsOn(page);
        var hasHits = hits is { Count: > 0 };
        if (!hasHits || _layout is null || !_layout.IsSizeKnown(page))
        {
            return;
        }

        EnsureMarkBrushes();
        var size = _layout.GetPageSize(page);
        var scaleX = rect.Width / size.Width;
        var scaleY = rect.Height / size.Height;
        if (hasHits)
        {
            var current = _search!.CurrentHit;
            foreach (var hit in hits!)
            {
                if (ReferenceEquals(hit, current))
                {
                    continue;
                }

                foreach (var r in hit.Rects)
                {
                    context.FillRectangle(_hitBrush!, MarkRect(rect, r, scaleX, scaleY));
                }
            }

            if (current is not null && current.PageIndex == page)
            {
                foreach (var r in current.Rects)
                {
                    context.DrawRectangle(_currentBrush, _currentPen, MarkRect(rect, r, scaleX, scaleY));
                }
            }
        }
    }

    private static Rect MarkRect(Rect page, PageRect r, double scaleX, double scaleY) =>
        new(page.X + (r.Left * scaleX), page.Y + (r.Top * scaleY), Math.Max(0, r.Width * scaleX), Math.Max(0, r.Height * scaleY));

    private void EnsureMarkBrushes()
    {
        if (!this.TryFindResource("Raffica.Brush.Ink", ActualThemeVariant, out var found) || found is not ISolidColorBrush)
        {
            this.TryFindResource("Raffica.Brush.Ink.Pdf", ActualThemeVariant, out found);
        }

        var source = found as ISolidColorBrush;
        if (_hitBrush is not null && ReferenceEquals(source, _inkSource))
        {
            return;
        }

        var color = (source as ISolidColorBrush)?.Color ?? Colors.DarkMagenta;
        _inkSource = source;
        _hitBrush = new ImmutableSolidColorBrush(color, HitOpacity);
        _currentBrush = new ImmutableSolidColorBrush(color, CurrentHitOpacity);
        _currentPen = new ImmutablePen(new ImmutableSolidColorBrush(color), 1.5);
    }
}
