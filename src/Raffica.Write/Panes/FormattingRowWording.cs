using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Raffica.Write.Editing;

namespace Raffica.Write.Panes;

/// <summary>
/// The words of the formatting pane's rows: what each override row of the inspector says in the UI language ("Bold", "Space after 12 pt", "Font Carlito"; sizes and spacing in points).
/// Every property element Write knows of a paragraph or a run has wording in both languages (<see cref="KnownKeys"/>, pinned by a guard test); any other element is named by its local
/// name and nothing else, so the pane never shows text of the document: a value shown is a short token of the formatting itself (a font, a color, a language tag), cleaned of control
/// characters and cut at <see cref="MaxValueLength"/>.
/// </summary>
internal static class FormattingRowWording
{
    /// <summary>The longest value (a font name, a language tag) the wording repeats; longer ones are cut.</summary>
    internal const int MaxValueLength = 40;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly Regex s_attributeBreak = new(" (?=[A-Za-z]+=)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // How an element is worded: a label alone, a label with an on/off state, or a label with a value (the text is built by hand below).
    private enum Kind
    {
        Label,
        Switch,
        Valued,
    }

    private static readonly Dictionary<string, Kind> s_paragraph = new(StringComparer.Ordinal)
    {
        ["keepNext"] = Kind.Switch,
        ["keepLines"] = Kind.Switch,
        ["pageBreakBefore"] = Kind.Switch,
        ["framePr"] = Kind.Label,
        ["widowControl"] = Kind.Switch,
        ["suppressLineNumbers"] = Kind.Switch,
        ["pBdr"] = Kind.Label,
        ["shd"] = Kind.Label,
        ["tabs"] = Kind.Label,
        ["suppressAutoHyphens"] = Kind.Switch,
        ["kinsoku"] = Kind.Switch,
        ["wordWrap"] = Kind.Switch,
        ["overflowPunct"] = Kind.Switch,
        ["topLinePunct"] = Kind.Switch,
        ["autoSpaceDE"] = Kind.Switch,
        ["autoSpaceDN"] = Kind.Switch,
        ["bidi"] = Kind.Switch,
        ["adjustRightInd"] = Kind.Switch,
        ["snapToGrid"] = Kind.Switch,
        ["spacing"] = Kind.Valued,
        ["ind"] = Kind.Valued,
        ["contextualSpacing"] = Kind.Switch,
        ["mirrorIndents"] = Kind.Switch,
        ["suppressOverlap"] = Kind.Switch,
        ["jc"] = Kind.Valued,
        ["textDirection"] = Kind.Label,
        ["textAlignment"] = Kind.Label,
        ["textboxTightWrap"] = Kind.Label,
        ["outlineLvl"] = Kind.Valued,
        ["divId"] = Kind.Label,
        ["cnfStyle"] = Kind.Label,
    };

    private static readonly Dictionary<string, Kind> s_run = new(StringComparer.Ordinal)
    {
        ["rFonts"] = Kind.Valued,
        ["b"] = Kind.Switch,
        ["bCs"] = Kind.Switch,
        ["i"] = Kind.Switch,
        ["iCs"] = Kind.Switch,
        ["caps"] = Kind.Switch,
        ["smallCaps"] = Kind.Switch,
        ["strike"] = Kind.Switch,
        ["dstrike"] = Kind.Switch,
        ["outline"] = Kind.Switch,
        ["shadow"] = Kind.Switch,
        ["emboss"] = Kind.Switch,
        ["imprint"] = Kind.Switch,
        ["noProof"] = Kind.Switch,
        ["snapToGrid"] = Kind.Switch,
        ["vanish"] = Kind.Switch,
        ["webHidden"] = Kind.Switch,
        ["color"] = Kind.Valued,
        ["spacing"] = Kind.Valued,
        ["w"] = Kind.Valued,
        ["kern"] = Kind.Valued,
        ["position"] = Kind.Valued,
        ["sz"] = Kind.Valued,
        ["szCs"] = Kind.Valued,
        ["highlight"] = Kind.Valued,
        ["u"] = Kind.Valued,
        ["effect"] = Kind.Label,
        ["bdr"] = Kind.Label,
        ["shd"] = Kind.Label,
        ["fitText"] = Kind.Label,
        ["vertAlign"] = Kind.Valued,
        ["rtl"] = Kind.Switch,
        ["cs"] = Kind.Switch,
        ["em"] = Kind.Label,
        ["lang"] = Kind.Valued,
        ["eastAsianLayout"] = Kind.Label,
        ["specVanish"] = Kind.Switch,
        ["oMath"] = Kind.Switch,
    };

    private static readonly HashSet<string> s_highlights = new(StringComparer.Ordinal)
    {
        "black", "blue", "cyan", "green", "magenta", "red", "yellow", "white", "darkBlue", "darkCyan", "darkGreen", "darkMagenta", "darkRed", "darkYellow", "darkGray", "lightGray",
    };

    private static readonly Dictionary<string, string> s_underlines = new(StringComparer.Ordinal)
    {
        ["double"] = "UDouble",
        ["thick"] = "UThick",
        ["dotted"] = "UDotted",
        ["dash"] = "UDash",
        ["wave"] = "UWave",
        ["words"] = "UWords",
        ["none"] = "UNone",
    };

    // The keys of the values and states that no element key covers: the "off" and "paragraph mark" wording, the valued elements' own variants and the small word lists.
    private static readonly string[] s_extraKeys =
    [
        "Off", "Mark",
        "PSpacingBefore", "PSpacingAfter", "PSpacingLineMultiple", "PSpacingLineAtLeast", "PSpacingLineExact",
        "PIndLeft", "PIndRight", "PIndFirstLine", "PIndHanging",
        "PJcValue", "PJcLeft", "PJcCenter", "PJcRight", "PJcBoth", "PJcDistribute",
        "POutlineLvlValue", "POutlineLvlBody",
        "RRFontsValue", "RRFontsTheme", "RColorValue", "RColorAuto", "RColorTheme", "RSpacingValue", "RWValue", "RKernValue", "RPositionUp", "RPositionDown",
        "RSzValue", "RSzCsValue", "RHighlightValue", "RUValue", "UDouble", "UThick", "UDotted", "UDash", "UWave", "UWords", "UNone",
        "RVertAlignSuper", "RVertAlignSub", "RVertAlignBaseline", "RLangValue",
    ];

    /// <summary>Every key (without the <c>FmtRow</c> prefix) of the row wording in the resource files; the guard test checks each one in both languages.</summary>
    internal static IReadOnlyList<string> KnownKeys { get; } =
    [
        .. s_paragraph.Keys.Select(name => KeyOf(paragraph: true, name)),
        .. s_run.Keys.Select(name => KeyOf(paragraph: false, name)),
        .. s_extraKeys,
    ];

    /// <summary>Whether Write has wording of its own for the element (otherwise the row is named by the element's local name).</summary>
    internal static bool IsKnown(OverrideScope scope, XName element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Namespace == W && (scope == OverrideScope.Paragraph ? s_paragraph : s_run).ContainsKey(element.LocalName);
    }

    /// <summary>The words of a row in the UI language (see the class remarks).</summary>
    public static string Describe(OverrideRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // A mixed row has no one value to tell: the first value found would only mislead.
        var text = DescribeElement(row.Scope == OverrideScope.Paragraph, row.Element, row.Presence == OverridePresence.Mixed ? null : row.Value);
        return row.Scope == OverrideScope.ParagraphMark ? WriteStrings.Fill(Get("Mark"), text) : text;
    }

    /// <summary>What the state of a row says: null when the override is on the whole selection with one value.</summary>
    public static string? PresenceText(OverridePresence presence) => presence switch
    {
        OverridePresence.Partly => WriteStrings.FormattingPartly,
        OverridePresence.Mixed => WriteStrings.FormattingMixedValues,
        _ => null,
    };

    private static string Get(string key) => WriteStrings.Get("FmtRow" + key);

    private static string KeyOf(bool paragraph, string local) => (paragraph ? "P" : "R") + char.ToUpperInvariant(local[0]) + local[1..];

    private static string DescribeElement(bool paragraph, XName name, string? value)
    {
        if (name.Namespace != W || !(paragraph ? s_paragraph : s_run).TryGetValue(name.LocalName, out var kind))
        {
            return Clean(name.LocalName);
        }

        var key = KeyOf(paragraph, name.LocalName);
        switch (kind)
        {
            case Kind.Label:
                return Get(key);
            case Kind.Switch:
                return IsOff(value) ? WriteStrings.Fill(Get("Off"), Get(key)) : Get(key);
            default:
                break;
        }

        var attributes = AttributesOf(value);
        var text = paragraph ? ParagraphValued(name.LocalName, attributes) : RunValued(name.LocalName, attributes);
        return text ?? Get(key);
    }

    private static bool IsOff(string? value) => value is "0" or "false" or "off";

    // The text of a valued paragraph element, or null when no usable value is there (the label alone is then said).
    private static string? ParagraphValued(string local, Dictionary<string, string> attributes)
    {
        switch (local)
        {
            case "spacing":
                return JoinParts(
                    TwipsPart(attributes, "before", "PSpacingBefore"),
                    TwipsPart(attributes, "after", "PSpacingAfter"),
                    LineSpacing(attributes));
            case "ind":
                return JoinParts(
                    TwipsPart(attributes, "left", "PIndLeft") ?? TwipsPart(attributes, "start", "PIndLeft"),
                    TwipsPart(attributes, "right", "PIndRight") ?? TwipsPart(attributes, "end", "PIndRight"),
                    TwipsPart(attributes, "firstLine", "PIndFirstLine"),
                    TwipsPart(attributes, "hanging", "PIndHanging"));
            case "jc":
                if (!attributes.TryGetValue("val", out var jc))
                {
                    return null;
                }

                return jc switch
                {
                    "left" or "start" => Get("PJcLeft"),
                    "center" => Get("PJcCenter"),
                    "right" or "end" => Get("PJcRight"),
                    "both" => Get("PJcBoth"),
                    "distribute" => Get("PJcDistribute"),
                    _ => WriteStrings.Fill(Get("PJcValue"), Clean(jc)),
                };
            case "outlineLvl":
                if (!attributes.TryGetValue("val", out var level) || !int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    return null;
                }

                return number switch
                {
                    <= 8 => WriteStrings.Fill(Get("POutlineLvlValue"), number + 1),
                    _ => Get("POutlineLvlBody"),
                };
            default:
                return null;
        }
    }

    // The text of a valued run element, or null when no usable value is there.
    private static string? RunValued(string local, Dictionary<string, string> attributes)
    {
        switch (local)
        {
            case "rFonts":
                foreach (var attribute in new[] { "ascii", "hAnsi", "eastAsia", "cs" })
                {
                    if (attributes.TryGetValue(attribute, out var font) && font.Length > 0)
                    {
                        return WriteStrings.Fill(Get("RRFontsValue"), Clean(font));
                    }
                }

                return attributes.Keys.Any(k => k.EndsWith("Theme", StringComparison.Ordinal)) ? Get("RRFontsTheme") : null;
            case "color":
                if (attributes.TryGetValue("val", out var color) && color.Length > 0)
                {
                    if (string.Equals(color, "auto", StringComparison.Ordinal))
                    {
                        return Get("RColorAuto");
                    }

                    if (color.Length == 6 && color.All(char.IsAsciiHexDigit))
                    {
                        return WriteStrings.Fill(Get("RColorValue"), "#" + color.ToUpperInvariant());
                    }
                }

                return attributes.Keys.Any(k => k.StartsWith("theme", StringComparison.Ordinal)) ? Get("RColorTheme") : null;
            case "spacing":
                return Number(attributes, "val", 20) is { } spacing ? WriteStrings.Fill(Get("RSpacingValue"), spacing) : null;
            case "w":
                return Number(attributes, "val", 1) is { } width ? WriteStrings.Fill(Get("RWValue"), width) : null;
            case "kern":
                return Number(attributes, "val", 2) is { } kern ? WriteStrings.Fill(Get("RKernValue"), kern) : null;
            case "position":
                if (Number(attributes, "val", 2) is not { } position || position == 0)
                {
                    return null;
                }

                return position > 0 ? WriteStrings.Fill(Get("RPositionUp"), position) : WriteStrings.Fill(Get("RPositionDown"), -position);
            case "sz":
                return Number(attributes, "val", 2) is { } size ? WriteStrings.Fill(Get("RSzValue"), size) : null;
            case "szCs":
                return Number(attributes, "val", 2) is { } sizeCs ? WriteStrings.Fill(Get("RSzCsValue"), sizeCs) : null;
            case "highlight":
                if (!attributes.TryGetValue("val", out var highlight) || highlight.Length == 0)
                {
                    return null;
                }

                return WriteStrings.Fill(Get("RHighlightValue"), s_highlights.Contains(highlight) ? WriteStrings.Get("Hl" + char.ToUpperInvariant(highlight[0]) + highlight[1..]) : Clean(highlight));
            case "u":
                if (!attributes.TryGetValue("val", out var underline) || underline is "single" or "")
                {
                    return null;
                }

                return WriteStrings.Fill(Get("RUValue"), s_underlines.TryGetValue(underline, out var known) ? Get(known) : Clean(underline));
            case "vertAlign":
                return attributes.GetValueOrDefault("val") switch
                {
                    "superscript" => Get("RVertAlignSuper"),
                    "subscript" => Get("RVertAlignSub"),
                    "baseline" => Get("RVertAlignBaseline"),
                    _ => null,
                };
            case "lang":
                return attributes.TryGetValue("val", out var language) && language.Length > 0 ? WriteStrings.Fill(Get("RLangValue"), Clean(language)) : null;
            default:
                return null;
        }
    }

    // The line spacing of a w:spacing element: a multiple of a line (auto, 240 = one line) or points (at least, exact).
    private static string? LineSpacing(Dictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue("line", out var text) || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var line) || line <= 0)
        {
            return null;
        }

        return attributes.GetValueOrDefault("lineRule") switch
        {
            "atLeast" => WriteStrings.Fill(Get("PSpacingLineAtLeast"), Round(line / 20)),
            "exact" => WriteStrings.Fill(Get("PSpacingLineExact"), Round(line / 20)),
            _ => WriteStrings.Fill(Get("PSpacingLineMultiple"), Round(line / 240)),
        };
    }

    // "Space after 12 pt": the attribute is in twentieths of a point.
    private static string? TwipsPart(Dictionary<string, string> attributes, string attribute, string key) =>
        Number(attributes, attribute, 20) is { } points ? WriteStrings.Fill(Get(key), points) : null;

    // The attribute divided by a unit (twentieths of a point: 20, half-points: 2, plain: 1), rounded to two places; null when it is missing or not a number.
    private static double? Number(Dictionary<string, string> attributes, string attribute, double unit) =>
        attributes.TryGetValue(attribute, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? Round(value / unit)
            : null;

    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? JoinParts(params string?[] parts)
    {
        var present = parts.OfType<string>().ToList();
        return present.Count == 0 ? null : string.Join("; ", present);
    }

    // The inspector's value is the w:val, or "name=value" pairs joined by a space (a value may hold spaces, as a font name does).
    private static Dictionary<string, string> AttributesOf(string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value is null)
        {
            return result;
        }

        if (!value.Contains('=', StringComparison.Ordinal))
        {
            result["val"] = value;
            return result;
        }

        foreach (var pair in s_attributeBreak.Split(value))
        {
            var at = pair.IndexOf('=', StringComparison.Ordinal);
            if (at > 0)
            {
                result[pair[..at]] = pair[(at + 1)..];
            }
        }

        return result;
    }

    // A token of the file that is repeated in the pane: no control characters, no longer than MaxValueLength.
    private static string Clean(string text)
    {
        var builder = new StringBuilder(Math.Min(text.Length, MaxValueLength + 1));
        foreach (var c in text)
        {
            if (builder.Length >= MaxValueLength)
            {
                builder.Append('…');
                break;
            }

            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        return builder.ToString();
    }
}
