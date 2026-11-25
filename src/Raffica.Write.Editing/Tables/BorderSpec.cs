namespace Raffica.Write.Editing;

/// <summary>The sides of a range of cells a border command names: the four outer sides of the range and the lines between its cells.</summary>
[Flags]
public enum BorderSides
{
    /// <summary>No side.</summary>
    None = 0,

    /// <summary>The top edge of the range.</summary>
    Top = 1,

    /// <summary>The left edge of the range.</summary>
    Left = 2,

    /// <summary>The bottom edge of the range.</summary>
    Bottom = 4,

    /// <summary>The right edge of the range.</summary>
    Right = 8,

    /// <summary>The horizontal lines between the rows of the range.</summary>
    InsideHorizontal = 16,

    /// <summary>The vertical lines between the columns of the range.</summary>
    InsideVertical = 32,

    /// <summary>The four edges of the range.</summary>
    Outer = Top | Left | Bottom | Right,

    /// <summary>The lines between the cells of the range.</summary>
    Inside = InsideHorizontal | InsideVertical,

    /// <summary>Every side.</summary>
    All = Outer | Inside,
}

/// <summary>The line styles of a border (the values of <c>w:val</c> Write writes).</summary>
public enum TableBorderStyle
{
    /// <summary>A single line.</summary>
    Solid,

    /// <summary>A double line.</summary>
    DoubleLine,

    /// <summary>A dashed line.</summary>
    Dashed,

    /// <summary>A dotted line.</summary>
    Dotted,
}

/// <summary>
/// What a border command writes: the sides that get a line (<paramref name="Set"/>) with its style, width and colour, and the sides that are turned off (<paramref name="Cleared"/>, written as <c>w:val="nil"</c>,
/// which also overrides the lines of a table style). A side named in neither is left as it is. The presets are the four factory methods; a per-side spec is <see cref="Sides"/>.
/// <paramref name="WidthEighths"/> is the width in eighths of a point (<c>w:sz</c>, 2 to 96, so 4 is 0.5 pt); <paramref name="Color"/> is the colour as 0xAARRGGBB (the alpha is ignored; the caller offers the fixed colour
/// list of the Home tab) or null for the automatic colour.
/// </summary>
public sealed record BorderSpec(BorderSides Set, TableBorderStyle Style = TableBorderStyle.Solid, int WidthEighths = 4, uint? Color = null, BorderSides Cleared = BorderSides.None)
{
    /// <summary>The smallest width: a quarter of a point.</summary>
    public const int MinWidthEighths = 2;

    /// <summary>The largest width: twelve points.</summary>
    public const int MaxWidthEighths = 96;

    /// <summary>No borders: every side of every cell in the range is turned off (the outer edges and the lines between the cells).</summary>
    public static BorderSpec None() => new(BorderSides.None, Cleared: BorderSides.All);

    /// <summary>Every side of every cell: the outer edges and the lines between the cells.</summary>
    public static BorderSpec All(TableBorderStyle style = TableBorderStyle.Solid, int widthEighths = 4, uint? color = null) => new(BorderSides.All, style, widthEighths, color);

    /// <summary>The outline of the range and nothing else touched (Word's "Outside Borders" of the ribbon): the outer edges get the line and the lines between the cells stay as they are.</summary>
    public static BorderSpec Outside(TableBorderStyle style = TableBorderStyle.Solid, int widthEighths = 4, uint? color = null) => new(BorderSides.Outer, style, widthEighths, color);

    /// <summary>A box (Word's "Box" of the Borders and Shading dialog): the outer edges get the line and the lines between the cells of the range are turned off, so the range is an outline and nothing else.</summary>
    public static BorderSpec Box(TableBorderStyle style = TableBorderStyle.Solid, int widthEighths = 4, uint? color = null) =>
        new(BorderSides.Outer, style, widthEighths, color, BorderSides.Inside);

    /// <summary>The named sides get the line; nothing else changes.</summary>
    public static BorderSpec Sides(BorderSides sides, TableBorderStyle style = TableBorderStyle.Solid, int widthEighths = 4, uint? color = null) => new(sides, style, widthEighths, color);

    /// <summary>The named sides are turned off and nothing else changes (a per-side "none").</summary>
    public static BorderSpec Off(BorderSides sides) => new(BorderSides.None, Cleared: sides);

    /// <summary>True when the spec names at least one side, no side is both set and cleared, the style is defined and the width is in range.</summary>
    public bool IsValid =>
        (Set | Cleared) != BorderSides.None
        && (Set & Cleared) == BorderSides.None
        && (Set & ~BorderSides.All) == BorderSides.None
        && (Cleared & ~BorderSides.All) == BorderSides.None
        && Enum.IsDefined(Style)
        && (Set == BorderSides.None || (WidthEighths >= MinWidthEighths && WidthEighths <= MaxWidthEighths));
}
