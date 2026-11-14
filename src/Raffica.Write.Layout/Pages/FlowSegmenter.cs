namespace Raffica.Write.Layout;

/// <summary>
/// The writing view: the same layout with one wide column and no page chrome, cut into strips. A strip is a <see cref="LaidOutPage"/>
/// with <see cref="LaidOutPage.IsStrip"/>; blocks are cut between lines (never through one), manual page and column breaks end
/// nothing, and no header, footer, margin or section matters. The paragraph layouter is the same, so equal widths give equal lines.
/// </summary>
internal static class FlowSegmenter
{
    /// <summary>The tallest a strip gets, in points.</summary>
    public const float MaximumStripHeight = 2000f;

    private const float MinimumTextWidth = 50f;

    public static PageGeometry Geometry(LayoutOptions options)
    {
        var width = float.IsFinite(options.FlowTextWidth) ? Math.Max(MinimumTextWidth, options.FlowTextWidth) : 520f;
        return PageGeometry.ForStrips(width, MaximumStripHeight);
    }
}
