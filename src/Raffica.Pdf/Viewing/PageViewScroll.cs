using Avalonia.Input;
using Raffica.Core.Viewing;

namespace Raffica.Pdf.Viewing;

/// <summary>
/// The pure part of scrolling the page view: how far the wheel and the keys move the offset, where the offset is clamped, and where the
/// top of the view lies again after the layout changed (a resize, a zoom, page sizes that arrived). No controls, no state: every method
/// takes the content extent and the viewport size in DIPs and returns the new offset.
/// </summary>
internal static class PageViewScroll
{
    /// <summary>One step of the wheel and of an arrow key, in DIPs.</summary>
    public const double WheelStep = 48;

    /// <summary>The offset limited to the scrollable range 0 to extent minus viewport; content smaller than the viewport does not scroll.</summary>
    public static double Clamp(double offset, double extent, double viewport) =>
        double.IsNaN(offset) ? 0 : Math.Clamp(offset, 0, MaxOffset(extent, viewport));

    /// <summary>The largest offset: the content that does not fit into the viewport; 0 when it fits.</summary>
    public static double MaxOffset(double extent, double viewport) =>
        double.IsFinite(extent) && double.IsFinite(viewport) ? Math.Max(0, extent - viewport) : 0;

    /// <summary>The offset after <paramref name="notches"/> wheel notches (positive scrolls up, as the wheel reports it), in steps of <see cref="WheelStep"/>.</summary>
    public static double ByWheel(double offset, double notches, double extent, double viewport) =>
        Clamp(offset - (notches * WheelStep), extent, viewport);

    /// <summary>The distance of PageUp and PageDown: a viewport less one step, so the eye keeps a line of context; at least one step.</summary>
    public static double PageStep(double viewport) => Math.Max(WheelStep, viewport - WheelStep);

    /// <summary>
    /// The offset a navigation key leads to (Up, Down, PageUp, PageDown, Home, End), or <see langword="null"/> for any other key.
    /// Home is the top of the content, End its bottom.
    /// </summary>
    public static double? ByKey(Key key, double offset, double extent, double viewport) => key switch
    {
        Key.Up => Clamp(offset - WheelStep, extent, viewport),
        Key.Down => Clamp(offset + WheelStep, extent, viewport),
        Key.PageUp => Clamp(offset - PageStep(viewport), extent, viewport),
        Key.PageDown => Clamp(offset + PageStep(viewport), extent, viewport),
        Key.Home => 0,
        Key.End => MaxOffset(extent, viewport),
        _ => null,
    };

    /// <summary>The offset of the left edge (Left, Right keys and the horizontal wheel use the same step); other keys give <see langword="null"/>.</summary>
    public static double? ByHorizontalKey(Key key, double offset, double extent, double viewport) => key switch
    {
        Key.Left => Clamp(offset - WheelStep, extent, viewport),
        Key.Right => Clamp(offset + WheelStep, extent, viewport),
        _ => null,
    };

    /// <summary>
    /// The vertical offset at which <paramref name="anchor"/> (a page and the fraction of its height) lies at the top of the view in the
    /// current layout, limited to the scrollable range. After a resize, a zoom or new page sizes, this keeps the top of the view where it was.
    /// </summary>
    public static double KeepAnchor(DocumentLayout layout, PageAnchor anchor, double viewport)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Clamp(layout.GetOffset(anchor), layout.ContentHeight, viewport);
    }
}
