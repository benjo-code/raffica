using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Whether the headings of a document are numbered by their styles.</summary>
public enum HeadingNumberingState
{
    /// <summary>The Heading 1 style carries a list.</summary>
    On,

    /// <summary>The file defines Heading 1 and it carries no list.</summary>
    Off,

    /// <summary>The file has no style sheet or does not define Heading 1 (id Heading1, name "heading 1"); the styles Heading 2 to 9 may be missing.</summary>
    Unavailable,
}

/// <summary>The numbering switch of the headings: reads the state, and builds the style changes that switch it.</summary>
public static class HeadingNumbering
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    internal const int Levels = 9;

    public static HeadingNumberingState StateOf(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (HeadingStyles(document) is not { } styles)
        {
            return HeadingNumberingState.Unavailable;
        }

        return styles[0].Style.ParagraphProperties.NumberingId is > 0 ? HeadingNumberingState.On : HeadingNumberingState.Off;
    }

    /// <summary>
    /// The heading styles the file defines among Heading1 to Heading9 (style id <c>HeadingN</c>, name "heading N" in any case), each with its zero-based
    /// level, in level order; many Word files define only Heading1 to Heading3. Null when the file has no style sheet or no Heading1 (the first entry is
    /// then always level 0).
    /// </summary>
    internal static IReadOnlyList<(int Level, Style Style)>? HeadingStyles(WordDocument document)
    {
        if (document.Styles is not { } sheet)
        {
            return null;
        }

        var result = new List<(int, Style)>();
        for (var level = 1; level <= Levels; level++)
        {
            var number = level.ToString(CultureInfo.InvariantCulture);
            if (sheet.Find("Heading" + number) is { } style
                && string.Equals(style.Name, "heading " + number, StringComparison.OrdinalIgnoreCase))
            {
                result.Add((level - 1, style));
            }
        }

        return result.Count > 0 && result[0].Item1 == 0 ? result : null;
    }

    /// <summary>
    /// Gives each of the given levels of the definition that lacks one the <c>w:pStyle</c> link Heading1 to Heading9 (a level that already links to
    /// some style is left alone). Levels of styles the file does not define get no link.
    /// </summary>
    internal static void LinkLevels(AbstractNumbering definition, IEnumerable<int> levels)
    {
        foreach (var level in levels)
        {
            var item = definition.Levels.FirstOrDefault(l => l.Level == level);
            if (item is null || item.KeptChildren.Any(k => k.Content.Element.Name == W + "pStyle"))
            {
                continue;
            }

            item.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(
                new XElement(W + "pStyle", new XAttribute(W + "val", "Heading" + (level + 1).ToString(CultureInfo.InvariantCulture))))));
        }
    }

    internal static XElement NumPr(int numId, int level)
    {
        var numPr = new XElement(W + "numPr");
        if (level > 0)
        {
            numPr.Add(new XElement(W + "ilvl", new XAttribute(W + "val", level.ToString(CultureInfo.InvariantCulture))));
        }

        numPr.Add(new XElement(W + "numId", new XAttribute(W + "val", numId.ToString(CultureInfo.InvariantCulture))));
        return numPr;
    }
}
