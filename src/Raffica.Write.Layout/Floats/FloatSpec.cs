using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

internal enum FloatKind
{
    /// <summary>A picture the layout can ask the renderer to draw.</summary>
    Image,

    /// <summary>A shape with text (<c>wps:txbx</c> or VML <c>v:textbox</c>).</summary>
    TextBox,

    /// <summary>Anything else (a shape without text, a group, a chart, a picture Skia cannot decode): a marked box of the stated size.</summary>
    Placeholder,
}

internal enum FloatWrap
{
    /// <summary>The text ignores the object (it floats in front of the text, or behind it).</summary>
    None,

    /// <summary>The text runs around the object's rectangle (tight and through are drawn as this).</summary>
    Square,

    /// <summary>No text beside the object: the text goes above and below it.</summary>
    TopBottom,
}

/// <summary>Which side of a square-wrapped object takes text.</summary>
internal enum WrapSide
{
    Both,
    Left,
    Right,
    Largest,
}

internal enum HorizontalRelative
{
    Page,
    Margin,
    Column,
    Character,
    LeftMargin,
    RightMargin,
}

internal enum VerticalRelative
{
    Page,
    Margin,
    Paragraph,
    Line,
    TopMargin,
    BottomMargin,
}

internal enum FloatAlign
{
    /// <summary>No alignment: an offset from the start of the range.</summary>
    None,
    Start,
    Center,
    End,
}

/// <summary>What a text box looks like around its text.</summary>
internal sealed record TextBoxLook(uint? Fill, uint? Line, float LineWidth, float InsetLeft, float InsetTop, float InsetRight, float InsetBottom, bool AutoFit);

/// <summary>
/// An anchored object (a floating picture or text box), read from the drawing of a run. It says where the object wants to be and how
/// the text treats it; where it ends up depends on the page and is worked out when its paragraph is placed.
/// </summary>
internal sealed class FloatSpec
{
    public FloatKind Kind { get; init; }

    public ImageRef? Image { get; init; }

    public float Width { get; init; }

    public float Height { get; init; }

    public FloatWrap Wrap { get; init; }

    public WrapSide Side { get; init; }

    public bool Behind { get; init; }

    public float DistanceTop { get; init; }

    public float DistanceBottom { get; init; }

    public float DistanceLeft { get; init; }

    public float DistanceRight { get; init; }

    public HorizontalRelative HorizontalFrom { get; init; }

    public float HorizontalOffset { get; init; }

    public FloatAlign HorizontalAlign { get; init; }

    public VerticalRelative VerticalFrom { get; init; }

    public float VerticalOffset { get; init; }

    public FloatAlign VerticalAlign { get; init; }

    /// <summary>The index of the paragraph item that follows the anchor (a float after a manual page break belongs to the part after it).</summary>
    public int ItemIndex { get; init; }

    /// <summary>The paragraphs of a text box (null for the other kinds).</summary>
    public IReadOnlyList<Paragraph>? Content { get; init; }

    public TextBoxLook? Look { get; init; }

    /// <summary>The paragraphs of a text box, planned by the block planner after the paragraph that holds the box (set once).</summary>
    public IReadOnlyList<ParagraphPlan>? Plans { get; set; }

    public FloatSpec WithItemIndex(int index) => new()
    {
        Kind = Kind,
        Image = Image,
        Width = Width,
        Height = Height,
        Wrap = Wrap,
        Side = Side,
        Behind = Behind,
        DistanceTop = DistanceTop,
        DistanceBottom = DistanceBottom,
        DistanceLeft = DistanceLeft,
        DistanceRight = DistanceRight,
        HorizontalFrom = HorizontalFrom,
        HorizontalOffset = HorizontalOffset,
        HorizontalAlign = HorizontalAlign,
        VerticalFrom = VerticalFrom,
        VerticalOffset = VerticalOffset,
        VerticalAlign = VerticalAlign,
        ItemIndex = index,
        Content = Content,
        Look = Look,
        Plans = Plans,
    };
}

/// <summary>Reads anchored objects from the raw XML the model keeps (<c>w:drawing</c>, <c>mc:AlternateContent</c>, VML <c>w:pict</c>). Never throws on content.</summary>
internal static class FloatReader
{
    private const float EmuPerPoint = 12700f;
    private const float DefaultSideDistance = 9f;
    private const float MaximumLength = 100_000f;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace W10 = "urn:schemas-microsoft-com:office:word";

    /// <summary>The floating object of a <c>w:drawing</c>; null for an inline drawing, one the layout cannot read, or one with no size.</summary>
    public static FloatSpec? FromDrawing(XElement drawing, ImageProbe probe, string story)
    {
        var anchor = drawing.Element(Wp + "anchor");
        if (anchor is null)
        {
            return null;
        }

        var extent = anchor.Element(Wp + "extent");
        var width = Emu(extent, "cx");
        var height = Emu(extent, "cy");
        var (kind, image, content, look) = Content(anchor, probe, story, ref width, ref height);
        if (width < 1f || height < 1f)
        {
            return null;
        }

        var (wrap, side) = WrapOf(anchor);
        var h = anchor.Element(Wp + "positionH");
        var v = anchor.Element(Wp + "positionV");
        var (hAlign, hOffset) = Position(h);
        var (vAlign, vOffset) = Position(v);
        return new FloatSpec
        {
            Kind = kind,
            Image = image,
            Width = width,
            Height = height,
            Wrap = wrap,
            Side = side,
            Behind = IsTrue((string?)anchor.Attribute("behindDoc")),
            DistanceTop = Emu(anchor, "distT"),
            DistanceBottom = Emu(anchor, "distB"),
            DistanceLeft = Emu(anchor, "distL"),
            DistanceRight = Emu(anchor, "distR"),
            HorizontalFrom = HorizontalOf((string?)h?.Attribute("relativeFrom")),
            HorizontalAlign = hAlign,
            HorizontalOffset = hOffset,
            VerticalFrom = VerticalOf((string?)v?.Attribute("relativeFrom")),
            VerticalAlign = vAlign,
            VerticalOffset = vOffset,
            Content = content,
            Look = look,
        };
    }

    /// <summary>The floating object of an <c>mc:AlternateContent</c>: its drawing choice, else its VML fallback; null when there is none.</summary>
    public static FloatSpec? FromAlternate(XElement alternate, ImageProbe probe, string story)
    {
        foreach (var choice in alternate.Elements(Mc + "Choice"))
        {
            if (choice.Descendants(W + "drawing").FirstOrDefault() is { } drawing)
            {
                return FromDrawing(drawing, probe, story);
            }
        }

        if (alternate.Element(Mc + "Fallback")?.Element(W + "pict") is { } pict)
        {
            return FromVml(pict, probe, story);
        }

        return null;
    }

    /// <summary>The floating object of a VML <c>w:pict</c>: a shape with absolute position (a picture or a text box); null for anything else.</summary>
    public static FloatSpec? FromVml(XElement pict, ImageProbe probe, string story)
    {
        var shape = pict.Elements().FirstOrDefault(e => e.Name.Namespace == V && e.Name.LocalName is "shape" or "rect" or "roundrect" or "oval");
        if (shape is null)
        {
            return null;
        }

        var style = ParseStyle((string?)shape.Attribute("style"));
        if (!style.TryGetValue("position", out var position) || position != "absolute")
        {
            return null;
        }

        var width = Length(style, "width");
        var height = Length(style, "height");
        var textbox = shape.Element(V + "textbox")?.Element(W + "txbxContent");
        var imageData = shape.Element(V + "imagedata");
        var kind = FloatKind.Placeholder;
        ImageRef? image = null;
        IReadOnlyList<Paragraph>? content = null;
        TextBoxLook? look = null;
        if (textbox is not null)
        {
            kind = FloatKind.TextBox;
            content = TextBoxContent.Read(textbox);
            look = new TextBoxLook(Color((string?)shape.Attribute("fillcolor")), Color((string?)shape.Attribute("strokecolor")) ?? 0xFF000000, 0.75f, 7.2f, 3.6f, 7.2f, 3.6f, false);
        }
        else if (imageData is not null && (string?)imageData.Attribute(R + "id") is { } rid)
        {
            var id = ImageRef.Qualify(story, rid);
            var info = probe.Info(id);
            kind = info is null ? FloatKind.Placeholder : FloatKind.Image;
            image = new ImageRef(id);
            if (info is { Width: > 0 } px && (width < 1f || height < 1f))
            {
                width = px.Width * 0.75f;
                height = px.Height * 0.75f;
            }
        }

        if (width < 1f || height < 1f)
        {
            return null;
        }

        var wrapType = (string?)shape.Element(W10 + "wrap")?.Attribute("type");
        var wrap = wrapType switch
        {
            "topAndBottom" => FloatWrap.TopBottom,
            "square" or "tight" or "through" => FloatWrap.Square,
            _ => FloatWrap.None,
        };
        var hRel = style.GetValueOrDefault("mso-position-horizontal-relative") switch
        {
            "page" => HorizontalRelative.Page,
            "text" => HorizontalRelative.Column,
            "char" => HorizontalRelative.Character,
            "left-margin-area" => HorizontalRelative.LeftMargin,
            "right-margin-area" => HorizontalRelative.RightMargin,
            _ => HorizontalRelative.Margin,
        };
        var vRel = style.GetValueOrDefault("mso-position-vertical-relative") switch
        {
            "page" => VerticalRelative.Page,
            "text" => VerticalRelative.Paragraph,
            "line" => VerticalRelative.Line,
            "top-margin-area" => VerticalRelative.TopMargin,
            "bottom-margin-area" => VerticalRelative.BottomMargin,
            _ => VerticalRelative.Margin,
        };
        var hAlign = style.GetValueOrDefault("mso-position-horizontal") switch
        {
            "left" or "inside" => FloatAlign.Start,
            "center" => FloatAlign.Center,
            "right" or "outside" => FloatAlign.End,
            _ => FloatAlign.None,
        };
        var vAlign = style.GetValueOrDefault("mso-position-vertical") switch
        {
            "top" or "inside" => FloatAlign.Start,
            "center" => FloatAlign.Center,
            "bottom" or "outside" => FloatAlign.End,
            _ => FloatAlign.None,
        };
        var zIndex = style.TryGetValue("z-index", out var z) && int.TryParse(z, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zValue) ? zValue : 0;
        return new FloatSpec
        {
            Kind = kind,
            Image = image,
            Width = width,
            Height = height,
            Wrap = wrap,
            Side = WrapSide.Largest,
            Behind = zIndex < 0,
            DistanceTop = Length(style, "mso-wrap-distance-top", 0f),
            DistanceBottom = Length(style, "mso-wrap-distance-bottom", 0f),
            DistanceLeft = Length(style, "mso-wrap-distance-left", DefaultSideDistance),
            DistanceRight = Length(style, "mso-wrap-distance-right", DefaultSideDistance),
            HorizontalFrom = hRel,
            HorizontalAlign = hAlign,
            HorizontalOffset = Length(style, "margin-left", 0f),
            VerticalFrom = vRel,
            VerticalAlign = vAlign,
            VerticalOffset = Length(style, "margin-top", 0f),
            Content = content,
            Look = look,
        };
    }

    // What the drawing shows: a picture, a text box or something the layout cannot draw. A missing extent takes the pixel size of a picture.
    private static (FloatKind Kind, ImageRef? Image, IReadOnlyList<Paragraph>? Content, TextBoxLook? Look) Content(XElement anchor, ImageProbe probe, string story, ref float width, ref float height)
    {
        var graphic = anchor.Element(A + "graphic")?.Element(A + "graphicData");
        if (graphic?.Descendants(Wps + "txbx").FirstOrDefault()?.Element(W + "txbxContent") is { } box)
        {
            var shape = graphic.Descendants(Wps + "wsp").FirstOrDefault();
            return (FloatKind.TextBox, null, TextBoxContent.Read(box), LookOf(shape));
        }

        var blip = graphic?.Descendants(A + "blip").FirstOrDefault();
        if (blip is not null && (string?)blip.Attribute(R + "embed") is { } rid)
        {
            var id = ImageRef.Qualify(story, rid);
            var info = probe.Info(id);
            if (info is { Width: > 0, Height: > 0 } px && (width < 1f || height < 1f))
            {
                width = px.Width * 0.75f;
                height = px.Height * 0.75f;
            }

            return (info is null ? FloatKind.Placeholder : FloatKind.Image, new ImageRef(id), null, null);
        }

        return (FloatKind.Placeholder, null, null, null);
    }

    private static TextBoxLook LookOf(XElement? shape)
    {
        var properties = shape?.Element(Wps + "spPr");
        uint? fill = null;
        if (properties?.Element(A + "solidFill") is { } solid)
        {
            fill = SolidColor(solid);
        }

        uint? line = null;
        var width = 0.75f;
        var outline = properties?.Element(A + "ln");
        if (outline is not null && outline.Element(A + "noFill") is null && outline.Element(A + "solidFill") is { } stroke)
        {
            line = SolidColor(stroke);
            if (long.TryParse((string?)outline.Attribute("w"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var emu) && emu > 0)
            {
                width = Math.Clamp(emu / EmuPerPoint, 0.25f, 12f);
            }
        }

        var body = shape?.Element(Wps + "bodyPr");
        return new TextBoxLook(
            fill,
            line,
            width,
            Inset(body, "lIns", 7.2f),
            Inset(body, "tIns", 3.6f),
            Inset(body, "rIns", 7.2f),
            Inset(body, "bIns", 3.6f),
            body?.Element(A + "spAutoFit") is not null);
    }

    private static float Inset(XElement? body, string name, float fallback) =>
        long.TryParse((string?)body?.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var emu) ? Math.Clamp(emu / EmuPerPoint, 0f, 200f) : fallback;

    // The colour of a solid fill: an RGB value, or a scheme colour that is read as white, black or grey (the theme is not loaded).
    private static uint? SolidColor(XElement fill)
    {
        if (fill.Element(A + "srgbClr") is { } rgb && Hex((string?)rgb.Attribute("val")) is { } value)
        {
            return 0xFF000000 | value;
        }

        if (fill.Element(A + "schemeClr") is { } scheme)
        {
            var shaded = scheme.Element(A + "shade") is not null;
            return (string?)scheme.Attribute("val") switch
            {
                "lt1" or "bg1" => shaded ? 0xFF808080u : 0xFFFFFFFFu,
                "dk1" or "tx1" => 0xFF000000u,
                _ => 0xFF808080u,
            };
        }

        return null;
    }

    private static uint? Hex(string? text) =>
        text is { Length: 6 } && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static uint? Color(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var hex = text.TrimStart('#');
        if (Hex(hex) is { } value)
        {
            return 0xFF000000 | value;
        }

        return hex switch
        {
            "white" => 0xFFFFFFFFu,
            "black" => 0xFF000000u,
            "red" => 0xFFFF0000u,
            "yellow" => 0xFFFFFF00u,
            "blue" => 0xFF0000FFu,
            "green" => 0xFF008000u,
            _ => null,
        };
    }

    private static (FloatWrap, WrapSide) WrapOf(XElement anchor)
    {
        foreach (var child in anchor.Elements())
        {
            if (child.Name.Namespace != Wp)
            {
                continue;
            }

            switch (child.Name.LocalName)
            {
                case "wrapNone":
                    return (FloatWrap.None, WrapSide.Both);
                case "wrapTopAndBottom":
                    return (FloatWrap.TopBottom, WrapSide.Both);
                case "wrapSquare" or "wrapTight" or "wrapThrough":
                    return (FloatWrap.Square, (string?)child.Attribute("wrapText") switch
                    {
                        "left" => WrapSide.Left,
                        "right" => WrapSide.Right,
                        "largest" => WrapSide.Largest,
                        _ => WrapSide.Both,
                    });
                default:
                    break;
            }
        }

        return (FloatWrap.None, WrapSide.Both);
    }

    private static (FloatAlign, float) Position(XElement? position)
    {
        if (position?.Element(Wp + "align") is { } align)
        {
            return (align.Value.Trim() switch
            {
                "left" or "top" or "inside" => FloatAlign.Start,
                "center" => FloatAlign.Center,
                "right" or "bottom" or "outside" => FloatAlign.End,
                _ => FloatAlign.None,
            }, 0f);
        }

        return (FloatAlign.None, long.TryParse(position?.Element(Wp + "posOffset")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var emu) ? Clamp(emu / EmuPerPoint) : 0f);
    }

    private static HorizontalRelative HorizontalOf(string? value) => value switch
    {
        "page" => HorizontalRelative.Page,
        "column" => HorizontalRelative.Column,
        "character" => HorizontalRelative.Character,
        "leftMargin" or "insideMargin" => HorizontalRelative.LeftMargin,
        "rightMargin" or "outsideMargin" => HorizontalRelative.RightMargin,
        _ => HorizontalRelative.Margin,
    };

    private static VerticalRelative VerticalOf(string? value) => value switch
    {
        "page" => VerticalRelative.Page,
        "paragraph" => VerticalRelative.Paragraph,
        "line" => VerticalRelative.Line,
        "topMargin" or "insideMargin" => VerticalRelative.TopMargin,
        "bottomMargin" or "outsideMargin" => VerticalRelative.BottomMargin,
        _ => VerticalRelative.Margin,
    };

    private static float Emu(XElement? element, string name) =>
        long.TryParse((string?)element?.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var emu) ? Math.Max(0f, Clamp(emu / EmuPerPoint)) : 0f;

    private static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, -MaximumLength, MaximumLength) : 0f;

    private static bool IsTrue(string? value) => value is "1" or "true" or "on";

    private static Dictionary<string, string> ParseStyle(string? style)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (style is null)
        {
            return map;
        }

        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = part.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                map[part[..colon].Trim()] = part[(colon + 1)..].Trim();
            }
        }

        return map;
    }

    // A VML length in points: pt, in, cm, mm, px (a bare number is pixels at 96 per inch... Word writes pt).
    private static float Length(Dictionary<string, string> style, string key, float fallback = 0f)
    {
        if (!style.TryGetValue(key, out var text))
        {
            return fallback;
        }

        var units = new (string Suffix, float Factor)[] { ("pt", 1f), ("in", 72f), ("cm", 72f / 2.54f), ("mm", 72f / 25.4f), ("px", 0.75f) };
        foreach (var (suffix, factor) in units)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal) && float.TryParse(text.AsSpan(0, text.Length - suffix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return Clamp(number * factor);
            }
        }

        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) ? Clamp(plain * 0.75f) : fallback;
    }
}

/// <summary>Reads the paragraphs of a <c>w:txbxContent</c> into model paragraphs (text, tabs, breaks and their run and paragraph properties); the rest stays out of the picture.</summary>
internal static class TextBoxContent
{
    private const int MaximumParagraphs = 500;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static List<Paragraph> Read(XElement content)
    {
        var paragraphs = new List<Paragraph>();
        foreach (var p in content.Elements(W + "p"))
        {
            if (paragraphs.Count >= MaximumParagraphs)
            {
                break;
            }

            var paragraph = new Paragraph();
            if (p.Element(W + "pPr") is { } properties)
            {
                foreach (var child in properties.Elements().Where(e => e.Name != W + "sectPr"))
                {
                    paragraph.Properties.AddLoaded(child);
                }
            }

            AddRuns(p, paragraph);
            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }

    private static void AddRuns(XElement parent, Paragraph paragraph)
    {
        foreach (var child in parent.Elements())
        {
            if (child.Name == W + "r")
            {
                var run = new Run();
                if (child.Element(W + "rPr") is { } rPr)
                {
                    foreach (var property in rPr.Elements())
                    {
                        run.Properties.AddLoaded(property);
                    }
                }

                foreach (var item in child.Elements())
                {
                    switch (item.Name.LocalName)
                    {
                        case "t":
                            run.Content.Add(new TextContent(item.Value));
                            break;
                        case "tab":
                            run.Content.Add(new TabContent());
                            break;
                        case "br":
                            // A page or column break inside a text box is a line break: the box has no pages of its own.
                            run.Content.Add(new BreakContent(Model.BreakKind.TextWrapping));
                            break;
                        default:
                            break;
                    }
                }

                paragraph.Inlines.Add(run);
            }
            else if (child.Name == W + "hyperlink" || child.Name == W + "ins" || child.Name == W + "smartTag")
            {
                AddRuns(child, paragraph);
            }
        }
    }
}
