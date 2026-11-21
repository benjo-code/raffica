using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Whether the headings of a document are numbered by their styles.</summary>
public enum HeadingNumberingState
{
    /// <summary>The Heading 1 style carries a list.</summary>
    On,

    /// <summary>The nine heading styles exist and Heading 1 carries no list.</summary>
    Off,

    /// <summary>The file lacks a style sheet or one of the styles Heading1 to Heading9 (named "heading 1" to "heading 9").</summary>
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

        return styles[0].ParagraphProperties.NumberingId is > 0 ? HeadingNumberingState.On : HeadingNumberingState.Off;
    }

    /// <summary>The styles Heading1 to Heading9 by position, or null when the file lacks one of them or its name does not say "heading n".</summary>
    internal static Style[]? HeadingStyles(WordDocument document)
    {
        if (document.Styles is not { } sheet)
        {
            return null;
        }

        var result = new Style[Levels];
        for (var level = 1; level <= Levels; level++)
        {
            var number = level.ToString(CultureInfo.InvariantCulture);
            if (sheet.Find("Heading" + number) is not { } style
                || !string.Equals(style.Name, "heading " + number, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            result[level - 1] = style;
        }

        return result;
    }

    /// <summary>Gives every level of the definition that lacks one the <c>w:pStyle</c> link Heading1 to Heading9 (a level that already links to some style is left alone).</summary>
    internal static void LinkLevels(AbstractNumbering definition)
    {
        for (var level = 0; level < Levels; level++)
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
