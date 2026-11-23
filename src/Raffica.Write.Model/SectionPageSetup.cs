using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>Which groups of <see cref="SectionPageSetup"/> a write touches.</summary>
[Flags]
public enum PageSetupParts
{
    None = 0,

    /// <summary>The page width and height (<c>w:pgSz</c> <c>w:w</c> and <c>w:h</c>).</summary>
    Size = 1,

    /// <summary>The orientation (<c>w:orient</c>), which goes with the width and height: those are written as well.</summary>
    Orientation = 2,

    /// <summary>The four margins (<c>w:pgMar</c> top, bottom, left, right).</summary>
    Margins = 4,

    /// <summary>The number of columns, their gap and the line between them (<c>w:cols</c>); a write makes the columns equal.</summary>
    Columns = 8,
}

/// <summary>
/// The page setup of one section as typed values in twips, read from and written to its <see cref="SectionProperties"/>. A missing element reads the layout's defaults
/// (A4, 1 inch margins, one column), so what the model says and what the layout draws agree. Header and footer distances, the gutter and every unknown attribute are never written.
/// </summary>
/// <param name="WidthTwips">The page width as it stands in <c>w:pgSz</c>.</param>
/// <param name="HeightTwips">The page height as it stands in <c>w:pgSz</c>.</param>
/// <param name="Landscape">Whether <c>w:orient</c> says landscape.</param>
/// <param name="TopTwips">The top margin.</param>
/// <param name="BottomTwips">The bottom margin.</param>
/// <param name="LeftTwips">The left margin (without the gutter).</param>
/// <param name="RightTwips">The right margin.</param>
/// <param name="Columns">The number of equal columns.</param>
/// <param name="ColumnSpaceTwips">The gap between columns.</param>
/// <param name="ColumnLine">Whether a line is drawn between columns.</param>
public sealed record SectionPageSetup(
    int WidthTwips,
    int HeightTwips,
    bool Landscape,
    int TopTwips,
    int BottomTwips,
    int LeftTwips,
    int RightTwips,
    int Columns,
    int ColumnSpaceTwips,
    bool ColumnLine)
{
    private const int DefaultWidth = 11906; // A4, 595.3 pt
    private const int DefaultHeight = 16838; // 841.9 pt
    private const int DefaultMargin = 1440;
    private const int DefaultColumnSpace = 720;
    private const int DefaultHeaderFooterDistance = 720;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The setup of a section; null, or a part that is missing, reads the defaults.</summary>
    public static SectionPageSetup Read(SectionProperties? properties)
    {
        var size = properties?.Get(W + "pgSz");
        var margins = properties?.Get(W + "pgMar");
        var columns = properties?.Get(W + "cols");
        return new SectionPageSetup(
            Twips(size, "w", DefaultWidth),
            Twips(size, "h", DefaultHeight),
            string.Equals((string?)size?.Attribute(W + "orient"), "landscape", StringComparison.Ordinal),
            Twips(margins, "top", DefaultMargin),
            Twips(margins, "bottom", DefaultMargin),
            Twips(margins, "left", DefaultMargin),
            Twips(margins, "right", DefaultMargin),
            Math.Max(1, Twips(columns, "num", 1)),
            Twips(columns, "space", DefaultColumnSpace),
            columns?.Attribute(W + "sep") is { } line && line.Value is not ("0" or "false" or "off"));
    }

    /// <summary>The same page turned: width and height swapped when the orientation changes, <see cref="Landscape"/> set.</summary>
    public SectionPageSetup WithLandscape(bool landscape) =>
        landscape == Landscape ? this : this with { Landscape = landscape, WidthTwips = HeightTwips, HeightTwips = WidthTwips };

    /// <summary>Writes the named parts into the section's properties; an element that is not there is made in schema order, one that is there keeps its other attributes.</summary>
    public void ApplyTo(SectionProperties properties, PageSetupParts parts)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if ((parts & (PageSetupParts.Size | PageSetupParts.Orientation)) != 0)
        {
            var size = Ensure(properties, "pgSz");
            size.SetAttributeValue(W + "w", Text(WidthTwips));
            size.SetAttributeValue(W + "h", Text(HeightTwips));
            if ((parts & PageSetupParts.Orientation) != 0)
            {
                size.SetAttributeValue(W + "orient", Landscape ? "landscape" : "portrait");
            }
        }

        if ((parts & PageSetupParts.Margins) != 0)
        {
            var margins = properties.Get(W + "pgMar");
            if (margins is null)
            {
                // A new w:pgMar needs all seven attributes; the distances and the gutter get the defaults Word writes.
                margins = new XElement(W + "pgMar");
                margins.SetAttributeValue(W + "header", Text(DefaultHeaderFooterDistance));
                margins.SetAttributeValue(W + "footer", Text(DefaultHeaderFooterDistance));
                margins.SetAttributeValue(W + "gutter", "0");
                properties.Set(W + "pgMar", margins);
            }

            margins.SetAttributeValue(W + "top", Text(TopTwips));
            margins.SetAttributeValue(W + "right", Text(RightTwips));
            margins.SetAttributeValue(W + "bottom", Text(BottomTwips));
            margins.SetAttributeValue(W + "left", Text(LeftTwips));
        }

        if ((parts & PageSetupParts.Columns) != 0)
        {
            var cols = Ensure(properties, "cols");
            cols.SetAttributeValue(W + "num", Text(Columns));
            cols.SetAttributeValue(W + "space", Text(ColumnSpaceTwips));
            cols.SetAttributeValue(W + "sep", ColumnLine ? "1" : null);
            cols.SetAttributeValue(W + "equalWidth", null);
            cols.Elements(W + "col").Remove();
        }
    }

    private static XElement Ensure(SectionProperties properties, string local)
    {
        var name = W + local;
        if (properties.Get(name) is { } existing)
        {
            return existing;
        }

        var made = new XElement(name);
        properties.Set(name, made);
        return made;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Twips(XElement? element, string name, int fallback)
    {
        var text = (string?)element?.Attribute(W + name);
        if (text is not null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
        {
            return (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue);
        }

        return fallback;
    }
}
