namespace Raffica.Write.Layout;

/// <summary>
/// One thing to draw on a page. All positions and sizes are points from the top left of the page. A page is a flat list of these in
/// painting order (fills first, then text, then rules); every item is immutable once its page is published.
/// </summary>
public abstract class PageItem
{
}

/// <summary>Glyphs of one face on one baseline. <see cref="Xs"/> are offsets from <see cref="X"/>; the arrays are shared with the layout and must not be changed.</summary>
public sealed class GlyphRunItem : PageItem
{
    internal GlyphRunItem(FontFace face, float size, uint color, ReadOnlyMemory<ushort> glyphs, ReadOnlyMemory<float> xs, float x, float baseline)
    {
        Face = face;
        Size = size;
        Color = color;
        Glyphs = glyphs;
        Xs = xs;
        X = x;
        Baseline = baseline;
    }

    public FontFace Face { get; }

    public float Size { get; }

    /// <summary>0xAARRGGBB; opaque black for automatic colour.</summary>
    public uint Color { get; }

    public ReadOnlyMemory<ushort> Glyphs { get; }

    public ReadOnlyMemory<float> Xs { get; }

    public float X { get; }

    /// <summary>The y of the baseline.</summary>
    public float Baseline { get; }
}

/// <summary>A filled rectangle used as a line: an underline, a strike-through, a rule between columns or a border.</summary>
public sealed class RuleItem : PageItem
{
    internal RuleItem(float x, float y, float width, float height, uint color)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Color = color;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public uint Color { get; }
}

/// <summary>A filled rectangle behind text or cells (highlight, shading).</summary>
public sealed class FillItem : PageItem
{
    internal FillItem(float x, float y, float width, float height, uint color)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Color = color;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public uint Color { get; }
}

/// <summary>A picture; the caller resolves <see cref="Image"/> (a relationship id) to the bytes.</summary>
public sealed class ImageItem : PageItem
{
    internal ImageItem(ImageRef image, float x, float y, float width, float height)
    {
        Image = image;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public ImageRef Image { get; }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }
}

/// <summary>A marked box for content the layout cannot draw; the model keeps the content.</summary>
public sealed class PlaceholderItem : PageItem
{
    internal PlaceholderItem(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }
}
