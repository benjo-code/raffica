namespace Raffica.Write.Layout;

/// <summary>Where a paragraph is being placed: what a floating object anchored in it needs to find its position.</summary>
internal sealed class FloatContext
{
    public FloatContext(PageGeometry geometry, float columnLeft, float columnWidth, float y, float bottom, PageFloats page)
    {
        Geometry = geometry;
        ColumnLeft = columnLeft;
        ColumnWidth = columnWidth;
        Y = y;
        Bottom = bottom;
        Page = page;
    }

    public PageGeometry Geometry { get; }

    /// <summary>The x of the column the paragraph is in, from the left edge of the page.</summary>
    public float ColumnLeft { get; }

    public float ColumnWidth { get; }

    /// <summary>The y of the top of the paragraph (after its space before), from the top of the page.</summary>
    public float Y { get; }

    /// <summary>The bottom of the room the paragraph is placed in; lines below it are laid out again on the next page.</summary>
    public float Bottom { get; }

    /// <summary>The objects already placed on the page.</summary>
    public PageFloats Page { get; }
}

/// <summary>
/// Works out where floating objects land. The rules are Word's as far as the compatibility corpus asks: an offset or alignment
/// inside the range its anchor names (page, margin, column, paragraph), the size shrunk to fit the page, the whole rectangle moved
/// back inside the page, and a text box laid out in the room its insets leave.
/// </summary>
internal static class FloatPlanner
{
    /// <summary>The boxes of all <paramref name="specs"/> for a paragraph placed at <paramref name="context"/>.</summary>
    public static List<FloatBox> Place(IReadOnlyList<FloatSpec> specs, FloatContext context)
    {
        var boxes = new List<FloatBox>(specs.Count);
        foreach (var spec in specs)
        {
            boxes.Add(PlaceOne(spec, context));
        }

        return boxes;
    }

    private static FloatBox PlaceOne(FloatSpec spec, FloatContext context)
    {
        var geometry = context.Geometry;
        var (width, height) = Fit(spec, geometry.Width, geometry.Height);
        IReadOnlyList<(IBlockFlow Flow, float Y)>? lines = null;
        var insets = spec.Look;
        if (spec.Kind == FloatKind.TextBox && spec.Plans is { } plans)
        {
            (lines, height) = LayOutText(plans, width, height, insets ?? new TextBoxLook(null, null, 0.75f, 7.2f, 3.6f, 7.2f, 3.6f, false), geometry.Height);
        }

        var (hStart, hEnd) = HorizontalRange(spec.HorizontalFrom, context);
        var (vStart, vEnd) = VerticalRange(spec.VerticalFrom, context);
        var x = Position(hStart, hEnd, width, spec.HorizontalAlign, spec.HorizontalOffset);
        var y = Position(vStart, vEnd, height, spec.VerticalAlign, spec.VerticalOffset);

        // An object never leaves the page.
        x = Math.Clamp(x, 0f, Math.Max(0f, geometry.Width - width));
        y = Math.Clamp(y, 0f, Math.Max(0f, geometry.Height - height));
        var textLeft = x + (insets?.InsetLeft ?? 0f);
        return new FloatBox(spec, x, y, width, height, lines, textLeft);
    }

    // The size in points, never bigger than the page (a picture keeps its aspect ratio).
    private static (float Width, float Height) Fit(FloatSpec spec, float pageWidth, float pageHeight)
    {
        var width = Finite(spec.Width);
        var height = Finite(spec.Height);
        if (spec.Kind == FloatKind.TextBox)
        {
            return (Math.Min(width, pageWidth), Math.Min(height, pageHeight));
        }

        var scale = Math.Min(1f, Math.Min(pageWidth / width, pageHeight / height));
        return (width * scale, height * scale);
    }

    private static float Finite(float value) => float.IsFinite(value) && value >= 1f ? value : 1f;

    private static (float Start, float End) HorizontalRange(HorizontalRelative from, FloatContext context)
    {
        var g = context.Geometry;
        return from switch
        {
            HorizontalRelative.Page => (0f, g.Width),
            HorizontalRelative.Column or HorizontalRelative.Character => (context.ColumnLeft, context.ColumnLeft + context.ColumnWidth),
            HorizontalRelative.LeftMargin => (0f, g.Left),
            HorizontalRelative.RightMargin => (g.Width - g.Right, g.Width),
            _ => (g.Left, g.Width - g.Right),
        };
    }

    private static (float Start, float End) VerticalRange(VerticalRelative from, FloatContext context)
    {
        var g = context.Geometry;
        return from switch
        {
            VerticalRelative.Page => (0f, g.Height),
            VerticalRelative.Paragraph or VerticalRelative.Line => (context.Y, context.Y),
            VerticalRelative.TopMargin => (0f, g.Top),
            VerticalRelative.BottomMargin => (g.Height - g.Bottom, g.Height),
            _ => (g.Top, g.Height - g.Bottom),
        };
    }

    private static float Position(float start, float end, float size, FloatAlign align, float offset) => align switch
    {
        FloatAlign.Start => start,
        FloatAlign.Center => ((start + end) / 2f) - (size / 2f),
        FloatAlign.End => end - size,
        _ => start + offset,
    };

    // The paragraphs of a text box stacked in the room inside the insets; what does not fit is cut off (the model keeps it), unless
    // the box grows with its text.
    private static (List<(IBlockFlow, float)> Lines, float Height) LayOutText(IReadOnlyList<ParagraphPlan> plans, float width, float height, TextBoxLook look, float pageHeight)
    {
        var inner = Math.Max(1f, width - look.InsetLeft - look.InsetRight);
        var room = look.AutoFit ? pageHeight : Math.Max(0f, height - look.InsetTop - look.InsetBottom);
        var lines = new List<(IBlockFlow, float)>();
        var y = look.InsetTop;
        var first = true;
        foreach (var plan in plans)
        {
            var flow = plan.Layout(inner);
            var gap = first ? 0f : flow.SpaceBefore;
            var available = look.InsetTop + room - (y + gap);
            var split = flow.Split(available, false);
            if (split.Head is not { } head)
            {
                break;
            }

            y += gap;
            lines.Add((head, y));
            y += head.Height + head.SpaceAfter;
            first = false;
            if (split.Tail is not null)
            {
                break;
            }
        }

        var grown = look.AutoFit ? Math.Min(pageHeight, Math.Max(height, y + look.InsetBottom)) : height;
        return (lines, grown);
    }
}
