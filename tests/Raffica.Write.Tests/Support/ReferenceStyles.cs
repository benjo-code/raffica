using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// The styles the field oracles ask about, read from the model alone and written from what Word 16 does (<c>Docs/write-word-check.md</c>). It shares no code with <c>FieldStyles</c>; change it only to follow Word.
/// <para>
/// Word knows a built-in style by its name, not by its id: a paragraph style (also one without a <c>w:type</c>) that is no custom style and is named exactly so, in any case. A paragraph has the paragraph style its
/// <c>w:pStyle</c> names, else the default paragraph style. A heading is a paragraph of a built-in style <c>heading 1</c> to <c>heading 9</c>. A style gives the outline level of the first style of its <c>basedOn</c> chain that has
/// a <c>w:outlineLvl</c> (9 is body text) or is a heading style. A table of contents lists a heading at its level; another paragraph at the level its style gives, with <c>\u</c> at its own <c>w:outlineLvl</c> when it has one.
/// </para>
/// </summary>
internal static class ReferenceStyles
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The heading level (1 to 9) of the paragraph, or null.</summary>
    public static int? Heading(WordDocument document, Paragraph paragraph) => LevelOfHeadingStyle(StyleOf(document, paragraph));

    /// <summary>The level a table of contents over headings lists the paragraph at, or null.</summary>
    public static int? Level(WordDocument document, Paragraph paragraph, bool outline)
    {
        if (Heading(document, paragraph) is { } heading)
        {
            return heading;
        }

        if (outline && paragraph.Properties.Get(W + "outlineLvl") is { } own)
        {
            return Outline(own);
        }

        var seen = new List<Style>();
        for (var style = StyleOf(document, paragraph); style is not null && !seen.Contains(style); style = style.BasedOn is { } parent ? First(document, parent) : null)
        {
            seen.Add(style);
            if (style.ParagraphProperties.Get(W + "outlineLvl") is { } styled)
            {
                return Outline(styled);
            }

            if (LevelOfHeadingStyle(style) is { } level)
            {
                return level;
            }
        }

        return null;
    }

    /// <summary>The id of the first built-in paragraph style of that name, or null.</summary>
    public static string? BuiltIn(WordDocument document, string name) =>
        document.Styles?.Styles.FirstOrDefault(s => s.Id.Length > 0 && IsBuiltIn(s) && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;

    /// <summary>The id the entries of that level, or of a list of figures, must have: the one of the built-in style, else the first id of Word's own name followed by nothing, 1, 2, ... that no style has.</summary>
    public static string EntryStyle(WordDocument document, int level, bool figures)
    {
        var number = level.ToString(CultureInfo.InvariantCulture);
        if (BuiltIn(document, figures ? "table of figures" : "toc " + number) is { } id)
        {
            return id;
        }

        var wanted = figures ? "TableofFigures" : "TOC" + number;
        return Enumerable.Range(0, 100).Select(n => n == 0 ? wanted : wanted + n.ToString(CultureInfo.InvariantCulture)).First(free => First(document, free) is null);
    }

    private static Style? First(WordDocument document, string id) => document.Styles?.Styles.FirstOrDefault(s => s.Id == id);

    private static Style? StyleOf(WordDocument document, Paragraph paragraph)
    {
        var named = paragraph.Properties.StyleId is { } id ? First(document, id) : null;
        return named is not null && IsParagraph(named) ? named : document.Styles?.Styles.FirstOrDefault(s => s.IsDefault && IsParagraph(s));
    }

    private static bool IsParagraph(Style style) => string.Equals(style.Type ?? "paragraph", "paragraph", StringComparison.OrdinalIgnoreCase);

    private static bool IsBuiltIn(Style style) => IsParagraph(style) && !style.UnknownAttributes.Any(a => a.Name == W + "customStyle" && a.Value is "1" or "true" or "on");

    private static int? LevelOfHeadingStyle(Style? style) =>
        style is not null && IsBuiltIn(style)
            ? Enumerable.Range(1, 9).Where(n => string.Equals(style.Name, "heading " + n.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)).Select(n => (int?)n).FirstOrDefault()
            : null;

    private static int? Outline(XElement element) =>
        int.TryParse((string?)element.Attribute(W + "val"), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 8 ? value + 1 : null;
}
