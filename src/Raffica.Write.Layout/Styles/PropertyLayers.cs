using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Layout;

/// <summary>Reading helpers for WordprocessingML property elements.</summary>
internal static class Ooxml
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string? Val(XElement element) => (string?)element.Attribute(W + "val");

    public static string? Attr(XElement element, string local) => (string?)element.Attribute(W + local);

    public static int? Int(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    public static int? IntAttr(XElement element, string local) => Int(Attr(element, local));

    public static bool OnOff(XElement element) => Val(element) is not ("0" or "false" or "off");

    /// <summary>Twips (twentieths of a point) to points.</summary>
    public static float Points(int twips) => twips / 20f;

    /// <summary>A six-digit hex colour as 0xFFRRGGBB; null for "auto" and anything that is not a colour.</summary>
    public static uint? Rgb(string? text) =>
        text is { Length: 6 } && uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)
            ? 0xFF000000u | rgb
            : null;
}

/// <summary>The paragraph properties of one layer; null means "not set". Overlaying a layer sets what it names.</summary>
internal sealed class ParaProps
{
    public ParagraphAlignment? Alignment;
    public float? Left;
    public float? Right;
    public float? FirstLine;
    public float? Hanging;
    public float? Before;
    public float? After;
    public LineSpacingRule? LineRule;
    public float? LineValue;
    public bool? ContextualSpacing;
    public bool? KeepNext;
    public bool? KeepLines;
    public bool? PageBreakBefore;
    public bool? WidowControl;
    public bool? SuppressAutoHyphens;
    public int? OutlineLevel;
    public int? NumId;
    public int? Ilvl;
    public List<TabStop> TabStops = [];

    public ParaProps Clone()
    {
        var copy = (ParaProps)MemberwiseClone();
        copy.TabStops = [.. TabStops];
        return copy;
    }

    /// <summary>Applies only the numbering reference of a property list (used to find the list before the indents are decided).</summary>
    public void OverlayNumbering(IEnumerable<XElement> children)
    {
        foreach (var child in children)
        {
            if (child.Name == Ooxml.W + "numPr")
            {
                ReadNumbering(child);
            }
        }
    }

    public void Overlay(IEnumerable<XElement> children)
    {
        foreach (var child in children)
        {
            if (child.Name.Namespace != Ooxml.W)
            {
                continue;
            }

            switch (child.Name.LocalName)
            {
                case "jc":
                    Alignment = Ooxml.Val(child) switch
                    {
                        "center" => ParagraphAlignment.Center,
                        "right" or "end" => ParagraphAlignment.Right,
                        "both" or "distribute" or "thaiDistribute" or "lowKashida" or "mediumKashida" or "highKashida" => ParagraphAlignment.Justify,
                        _ => ParagraphAlignment.Left,
                    };
                    break;
                case "ind":
                    ReadIndent(child);
                    break;
                case "spacing":
                    ReadSpacing(child);
                    break;
                case "contextualSpacing":
                    ContextualSpacing = Ooxml.OnOff(child);
                    break;
                case "keepNext":
                    KeepNext = Ooxml.OnOff(child);
                    break;
                case "keepLines":
                    KeepLines = Ooxml.OnOff(child);
                    break;
                case "pageBreakBefore":
                    PageBreakBefore = Ooxml.OnOff(child);
                    break;
                case "widowControl":
                    WidowControl = Ooxml.OnOff(child);
                    break;
                case "suppressAutoHyphens":
                    SuppressAutoHyphens = Ooxml.OnOff(child);
                    break;
                case "outlineLvl":
                    if (Ooxml.Int(Ooxml.Val(child)) is { } level)
                    {
                        OutlineLevel = level is >= 0 and <= 8 ? level : -1;
                    }

                    break;
                case "numPr":
                    ReadNumbering(child);
                    break;
                case "tabs":
                    ReadTabs(child);
                    break;
            }
        }
    }

    private void ReadNumbering(XElement numPr)
    {
        if (numPr.Element(Ooxml.W + "numId") is { } numId && Ooxml.Int(Ooxml.Val(numId)) is { } id)
        {
            NumId = id;
        }

        if (numPr.Element(Ooxml.W + "ilvl") is { } ilvl && Ooxml.Int(Ooxml.Val(ilvl)) is { } level)
        {
            Ilvl = level;
        }
    }

    private void ReadIndent(XElement ind)
    {
        if ((Ooxml.IntAttr(ind, "start") ?? Ooxml.IntAttr(ind, "left")) is { } left)
        {
            Left = Ooxml.Points(left);
        }

        if ((Ooxml.IntAttr(ind, "end") ?? Ooxml.IntAttr(ind, "right")) is { } right)
        {
            Right = Ooxml.Points(right);
        }

        if (Ooxml.IntAttr(ind, "firstLine") is { } first)
        {
            FirstLine = Ooxml.Points(Math.Max(first, 0));
            Hanging = 0f;
        }

        if (Ooxml.IntAttr(ind, "hanging") is { } hanging)
        {
            Hanging = Ooxml.Points(Math.Max(hanging, 0));
            FirstLine = 0f;
        }
    }

    private void ReadSpacing(XElement spacing)
    {
        if (Ooxml.Attr(spacing, "beforeAutospacing") is "1" or "true" or "on")
        {
            Before = 14f;
        }
        else if (Ooxml.IntAttr(spacing, "before") is { } before)
        {
            Before = Ooxml.Points(Math.Max(before, 0));
        }

        if (Ooxml.Attr(spacing, "afterAutospacing") is "1" or "true" or "on")
        {
            After = 14f;
        }
        else if (Ooxml.IntAttr(spacing, "after") is { } after)
        {
            After = Ooxml.Points(Math.Max(after, 0));
        }

        if (Ooxml.IntAttr(spacing, "line") is { } line)
        {
            switch (Ooxml.Attr(spacing, "lineRule"))
            {
                case "atLeast":
                    LineRule = LineSpacingRule.AtLeast;
                    LineValue = Ooxml.Points(Math.Max(line, 0));
                    break;
                case "exact":
                    LineRule = LineSpacingRule.Exact;
                    LineValue = Ooxml.Points(Math.Max(line, 0));
                    break;
                default:
                    // Auto: the value is in 240ths of a line.
                    LineRule = LineSpacingRule.Auto;
                    LineValue = line > 0 ? line / 240f : 1f;
                    break;
            }
        }
    }

    private void ReadTabs(XElement tabs)
    {
        foreach (var tab in tabs.Elements(Ooxml.W + "tab"))
        {
            if (Ooxml.IntAttr(tab, "pos") is not { } pos)
            {
                continue;
            }

            var position = Ooxml.Points(pos);
            TabStops.RemoveAll(t => t.Position == position);
            var kind = Ooxml.Val(tab);
            if (kind == "clear")
            {
                continue;
            }

            var alignment = kind switch
            {
                "center" => TabAlignment.Center,
                "right" or "end" => TabAlignment.Right,
                "decimal" => TabAlignment.DecimalPoint,
                "bar" => TabAlignment.Bar,
                _ => TabAlignment.Left,
            };
            var leader = Ooxml.Attr(tab, "leader") switch
            {
                "dot" => TabLeader.Dot,
                "hyphen" => TabLeader.Hyphen,
                "underscore" => TabLeader.Underscore,
                "heavy" => TabLeader.Heavy,
                "middleDot" => TabLeader.MiddleDot,
                _ => TabLeader.None,
            };
            TabStops.Add(new TabStop(position, alignment, leader));
        }
    }
}

/// <summary>The run properties of one layer; null means "not set". The colours use -1 for an explicit "auto" or "none".</summary>
internal sealed class RunProps
{
    public string? Font;
    public float? Size;
    public bool? Bold;
    public bool? Italic;
    public bool? Strike;
    public bool? Caps;
    public bool? SmallCaps;
    public bool? Hidden;
    public UnderlineKind? Underline;
    public long? Color;
    public long? Highlight;
    public RunVerticalAlignment? VerticalAlignment;
    public string? Language;

    public RunProps Clone() => (RunProps)MemberwiseClone();

    /// <summary>Sets what the elements name, direct-formatting style: a toggle's value replaces the old one.</summary>
    public void Overlay(IEnumerable<XElement> children)
    {
        foreach (var child in children)
        {
            if (child.Name.Namespace != Ooxml.W)
            {
                continue;
            }

            switch (child.Name.LocalName)
            {
                case "rFonts":
                    Font = FontName(child) ?? Font;
                    break;
                case "sz":
                    if (Ooxml.Int(Ooxml.Val(child)) is { } halfPoints && halfPoints > 0)
                    {
                        Size = halfPoints / 2f;
                    }

                    break;
                case "b":
                    Bold = Ooxml.OnOff(child);
                    break;
                case "i":
                    Italic = Ooxml.OnOff(child);
                    break;
                case "caps":
                    Caps = Ooxml.OnOff(child);
                    break;
                case "smallCaps":
                    SmallCaps = Ooxml.OnOff(child);
                    break;
                case "strike" or "dstrike":
                    Strike = Ooxml.OnOff(child);
                    break;
                case "vanish":
                    Hidden = Ooxml.OnOff(child);
                    break;
                case "u":
                    Underline = UnderlineOf(Ooxml.Val(child));
                    break;
                case "color":
                    Color = Ooxml.Rgb(Ooxml.Val(child)) is { } rgb ? rgb : -1;
                    break;
                case "highlight":
                    Highlight = HighlightOf(Ooxml.Val(child));
                    break;
                case "vertAlign":
                    VerticalAlignment = Ooxml.Val(child) switch
                    {
                        "superscript" => RunVerticalAlignment.Superscript,
                        "subscript" => RunVerticalAlignment.Subscript,
                        _ => RunVerticalAlignment.Baseline,
                    };
                    break;
                case "lang":
                    Language = Ooxml.Val(child) ?? Language;
                    break;
            }
        }
    }

    /// <summary>
    /// Applies a style layer on top: ordinary properties replace, but a toggle property (bold, italic, caps, small caps, strike,
    /// hidden) that the layer turns on flips the current value, as the OOXML toggle rule says for styles of different kinds.
    /// </summary>
    public void CombineStyleLayer(RunProps layer)
    {
        Font = layer.Font ?? Font;
        Size = layer.Size ?? Size;
        Underline = layer.Underline ?? Underline;
        Color = layer.Color ?? Color;
        Highlight = layer.Highlight ?? Highlight;
        VerticalAlignment = layer.VerticalAlignment ?? VerticalAlignment;
        Language = layer.Language ?? Language;
        Bold = Toggle(Bold, layer.Bold);
        Italic = Toggle(Italic, layer.Italic);
        Strike = Toggle(Strike, layer.Strike);
        Caps = Toggle(Caps, layer.Caps);
        SmallCaps = Toggle(SmallCaps, layer.SmallCaps);
        Hidden = Toggle(Hidden, layer.Hidden);
    }

    /// <summary>Replaces every property the other layer sets (a layer inside one style chain, where the derived style wins).</summary>
    public void Merge(RunProps layer)
    {
        Font = layer.Font ?? Font;
        Size = layer.Size ?? Size;
        Underline = layer.Underline ?? Underline;
        Color = layer.Color ?? Color;
        Highlight = layer.Highlight ?? Highlight;
        VerticalAlignment = layer.VerticalAlignment ?? VerticalAlignment;
        Language = layer.Language ?? Language;
        Bold = layer.Bold ?? Bold;
        Italic = layer.Italic ?? Italic;
        Strike = layer.Strike ?? Strike;
        Caps = layer.Caps ?? Caps;
        SmallCaps = layer.SmallCaps ?? SmallCaps;
        Hidden = layer.Hidden ?? Hidden;
    }

    public ResolvedRun ToResolved(bool paragraphMarkOnly) => new()
    {
        FontName = Font ?? "Times New Roman",
        SizePoints = Size ?? 10f,
        Bold = Bold ?? false,
        Italic = Italic ?? false,
        Underline = Underline ?? UnderlineKind.None,
        Strike = Strike ?? false,
        Color = Color is >= 0 ? (uint)Color.Value : null,
        Highlight = Highlight is >= 0 ? (uint)Highlight.Value : null,
        Caps = Caps ?? false,
        SmallCaps = SmallCaps ?? false,
        VerticalAlignment = VerticalAlignment ?? RunVerticalAlignment.Baseline,
        Language = Language,
        Hidden = Hidden ?? false,
        ParagraphMarkOnly = paragraphMarkOnly,
    };

    private static bool? Toggle(bool? current, bool? layer) => layer == true ? !(current ?? false) : current;

    private static string? FontName(XElement fonts)
    {
        if (Ooxml.Attr(fonts, "ascii") is { Length: > 0 } ascii)
        {
            return ascii;
        }

        if (Ooxml.Attr(fonts, "hAnsi") is { Length: > 0 } hAnsi)
        {
            return hAnsi;
        }

        // The theme part is not part of the document model, so a theme font becomes the Office default of its slot.
        var theme = Ooxml.Attr(fonts, "asciiTheme") ?? Ooxml.Attr(fonts, "hAnsiTheme");
        return theme switch
        {
            null => null,
            _ when theme.StartsWith("major", StringComparison.Ordinal) => "Calibri Light",
            _ => "Calibri",
        };
    }

    private static UnderlineKind UnderlineOf(string? value) => value switch
    {
        "none" => UnderlineKind.None,
        "words" => UnderlineKind.Words,
        "double" => UnderlineKind.DoubleLine,
        "thick" => UnderlineKind.Thick,
        "dotted" or "dottedHeavy" => UnderlineKind.Dotted,
        "dash" or "dashedHeavy" or "dashLong" or "dashLongHeavy" => UnderlineKind.Dashed,
        "dotDash" or "dashDotHeavy" or "dotDotDash" or "dashDotDotHeavy" => UnderlineKind.DotDash,
        "wave" or "wavyHeavy" or "wavyDouble" => UnderlineKind.Wave,
        _ => UnderlineKind.Solid,
    };

    private static long HighlightOf(string? value) => value switch
    {
        "yellow" => 0xFFFFFF00,
        "green" => 0xFF00FF00,
        "cyan" => 0xFF00FFFF,
        "magenta" => 0xFFFF00FF,
        "blue" => 0xFF0000FF,
        "red" => 0xFFFF0000,
        "darkBlue" => 0xFF000080,
        "darkCyan" => 0xFF008080,
        "darkGreen" => 0xFF008000,
        "darkMagenta" => 0xFF800080,
        "darkRed" => 0xFF800000,
        "darkYellow" => 0xFF808000,
        "darkGray" => 0xFF808080,
        "lightGray" => 0xFFC0C0C0,
        "black" => 0xFF000000,
        "white" => 0xFFFFFFFF,
        _ => -1,
    };
}
