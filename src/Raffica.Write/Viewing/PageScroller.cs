using Avalonia;

namespace Raffica.Write.Viewing;

/// <summary>
/// The pure math of the page view: where each page lies in a column of pages, which pages a viewport sees, and how a scroll offset, a place in the document
/// (<see cref="ViewAnchor"/>) and a zoom point map onto each other. Every length is in DIPs, from the top left of the content; page sizes come in points and
/// are scaled by <c>zoom * 96 / 72</c>. A scroller is a snapshot of the pages published so far: when more pages are published a new one is made, and what lay
/// above stays where it was. It knows no control and no layout (the block questions go through <see cref="IBlockMap"/>).
/// </summary>
internal sealed class PageScroller
{
    /// <summary>DIPs per point at a zoom of 1 (96 dpi).</summary>
    public const double DipsPerPoint = 96.0 / 72.0;

    /// <summary>One notch of the wheel and one arrow key, in DIPs.</summary>
    public const double WheelStep = 48;

    private readonly (float Width, float Height)[] _pages;
    private readonly double[] _tops;
    private readonly IBlockMap? _map;

    /// <param name="pages">The size of each page in points.</param>
    /// <param name="gap">The space between two pages (DIPs); 0 for the strips of the writing view.</param>
    /// <param name="zoom">The zoom, 1 = 100 %.</param>
    /// <param name="map">Where the blocks are (null: no anchors).</param>
    /// <param name="edge">The space above the first page, below the last one and left and right of the widest (DIPs); the gap when negative.</param>
    public PageScroller(IReadOnlyList<(float Width, float Height)> pages, float gap, double zoom, IBlockMap? map = null, float edge = -1)
    {
        ArgumentNullException.ThrowIfNull(pages);
        _pages = [.. pages];
        _map = map;
        Gap = Math.Max(0, gap);
        Edge = edge < 0 ? Gap : edge;
        Zoom = zoom;
        Scale = zoom * DipsPerPoint;
        _tops = new double[_pages.Length];
        var y = Edge;
        var widest = 0.0;
        for (var i = 0; i < _pages.Length; i++)
        {
            _tops[i] = y;
            y += (_pages[i].Height * Scale) + Gap;
            widest = Math.Max(widest, _pages[i].Width * Scale);
        }

        // The last page has the edge below it, not a gap.
        ContentHeight = _pages.Length == 0 ? 0 : y - Gap + Edge;
        ContentWidth = _pages.Length == 0 ? 0 : widest + (2 * Edge);
    }

    public int Count => _pages.Length;

    public double Zoom { get; }

    /// <summary>DIPs per point.</summary>
    public double Scale { get; }

    public double Gap { get; }

    public double Edge { get; }

    public double ContentHeight { get; }

    public double ContentWidth { get; }

    /// <summary>The same pages at another zoom (and the same gap, edge and map).</summary>
    public PageScroller WithZoom(double zoom) => new(_pages, (float)Gap, zoom, _map, (float)Edge);

    /// <summary>The size of a page in points.</summary>
    public (float Width, float Height) SizeOf(int page) => _pages[page];

    /// <summary>The page rectangle in content coordinates; pages narrower than the widest one are centred.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no such page.</exception>
    public Rect RectOf(int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(page, _pages.Length);
        var width = _pages[page].Width * Scale;
        return new Rect((ContentWidth - width) / 2, _tops[page], width, _pages[page].Height * Scale);
    }

    /// <summary>The first and last page that show in a viewport of <paramref name="viewport"/> DIPs at <paramref name="offset"/>; (0, -1) when none does.</summary>
    public (int First, int Last) Visible(double offset, double viewport)
    {
        if (_pages.Length == 0 || viewport <= 0 || !double.IsFinite(offset))
        {
            return (0, -1);
        }

        var bottom = offset + viewport;
        var first = -1;
        var last = -1;

        // The first page whose bottom is below the offset.
        var lo = 0;
        var hi = _pages.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_tops[mid] + (_pages[mid].Height * Scale) > offset)
            {
                first = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        if (first < 0 || _tops[first] >= bottom)
        {
            return (0, -1);
        }

        // The last page whose top is above the bottom of the viewport.
        lo = first;
        hi = _pages.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_tops[mid] < bottom)
            {
                last = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return (first, last);
    }

    /// <summary>The page at a y of the content: the page whose top is the last one above it (a gap belongs to the page above it), the first page above all, the last page below all. 0 without pages.</summary>
    public int PageAt(double contentY)
    {
        if (_pages.Length == 0)
        {
            return 0;
        }

        var lo = 0;
        var hi = _pages.Length - 1;
        var found = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_tops[mid] <= contentY)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }

    /// <summary>
    /// The place at the top of a view scrolled to <paramref name="offset"/>: the block at the top of the page there or, when that block started on an earlier
    /// page, the next one (so going back to the anchor never jumps up a page). <see cref="ViewAnchor.Top"/> without a map or a page.
    /// </summary>
    public ViewAnchor AnchorAt(double offset)
    {
        if (_map is null || _pages.Length == 0)
        {
            return ViewAnchor.Top;
        }

        var page = PageAt(offset);
        var y = (float)Math.Max(0, (offset - _tops[page]) / Scale);
        var block = _map.BlockAt(page, y);
        if (block < 0)
        {
            return ViewAnchor.Top;
        }

        if (_map.Locate(block) is { } start && start.Page < page && _map.Locate(block + 1) is not null)
        {
            block++;
        }

        return new ViewAnchor(block, 0);
    }

    /// <summary>The offset at which an anchor lies at the top of the view, or null when its page is not laid out (yet) or the scroller has no map.</summary>
    public double? OffsetOf(ViewAnchor anchor)
    {
        if (_map?.Locate(anchor.Block) is not { } at || at.Page >= _pages.Length)
        {
            return null;
        }

        return _tops[at.Page] + (at.Y * Scale);
    }

    /// <summary>
    /// The vertical offset in <paramref name="next"/> (the same pages at another zoom) that keeps the page and the fraction of the page that are under the
    /// pixel <paramref name="cursor"/> of a view scrolled to <paramref name="offset"/> under that pixel.
    /// </summary>
    public double OffsetKeepingPoint(double offset, double cursor, PageScroller next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (_pages.Length == 0 || next.Count == 0)
        {
            return offset;
        }

        var contentY = offset + cursor;
        var page = Math.Min(PageAt(contentY), next.Count - 1);
        var height = _pages[page].Height * Scale;
        var fraction = height > 0 ? (contentY - _tops[page]) / height : 0;
        return next._tops[page] + (fraction * next._pages[page].Height * next.Scale) - cursor;
    }

    /// <summary>The horizontal offset in <paramref name="next"/> that keeps the fraction of the content width under the pixel <paramref name="cursor"/> there.</summary>
    public double HorizontalOffsetKeepingPoint(double offset, double cursor, PageScroller next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var inner = ContentWidth - (2 * Edge);
        if (inner <= 0)
        {
            return offset;
        }

        var fraction = (offset + cursor - Edge) / inner;
        return next.Edge + (fraction * (next.ContentWidth - (2 * next.Edge))) - cursor;
    }

    /// <summary>The offset limited to the scrollable range 0 to extent minus viewport; content smaller than the viewport does not scroll, NaN gives 0.</summary>
    public static double Clamp(double offset, double extent, double viewport) =>
        double.IsNaN(offset) ? 0 : Math.Clamp(offset, 0, MaxOffset(extent, viewport));

    /// <summary>The largest offset: the content that does not fit into the viewport; 0 when it fits.</summary>
    public static double MaxOffset(double extent, double viewport) =>
        double.IsFinite(extent) && double.IsFinite(viewport) ? Math.Max(0, extent - viewport) : 0;

    /// <summary>The distance of PageUp and PageDown in the writing view: a viewport less one step, at least one step.</summary>
    public static double PageStep(double viewport) => Math.Max(WheelStep, viewport - WheelStep);
}
