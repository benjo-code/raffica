using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Write's own numbering definitions (the heading list, the bullet list and the numbered list), built with Model types. They equal the
/// definitions of the blank document level by level; a test compares them, so the two cannot drift.
/// </summary>
internal static class WriteListDefinitions
{
    public const int LevelCount = 9;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The multilevel decimal list of the headings: <c>%1.</c>, <c>%1.%2.</c> ... with <c>w:pStyle</c> Heading1 to Heading9 on its levels.</summary>
    public static AbstractNumbering Heading(int abstractNumId)
    {
        var definition = New(abstractNumId, "multilevel");
        for (var level = 0; level < LevelCount; level++)
        {
            var text = new StringBuilder();
            for (var k = 1; k <= level + 1; k++)
            {
                text.Append('%').Append(I(k)).Append('.');
            }

            var indent = 432 + (level * 144);
            var item = Level(level, "decimal", text.ToString(), indent, indent);
            item.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(
                new XElement(W + "pStyle", new XAttribute(W + "val", "Heading" + I(level + 1))))));
            definition.Levels.Add(item);
        }

        return definition;
    }

    /// <summary>Bullets: Symbol, Courier New and Wingdings in turn; each level steps 360 twips in from 720.</summary>
    public static AbstractNumbering Bullet(int abstractNumId)
    {
        var definition = New(abstractNumId, "hybridMultilevel");
        for (var level = 0; level < LevelCount; level++)
        {
            var (font, glyph) = (level % 3) switch
            {
                0 => ("Symbol", ""),
                1 => ("Courier New", "o"),
                _ => ("Wingdings", ""),
            };
            var item = Level(level, "bullet", glyph, 720 + (level * 360), 360);
            item.RunProperties.Set(W + "rFonts", new XElement(
                W + "rFonts",
                new XAttribute(W + "ascii", font),
                new XAttribute(W + "hAnsi", font),
                new XAttribute(W + "hint", "default")));
            definition.Levels.Add(item);
        }

        return definition;
    }

    /// <summary>Numbers: decimal, lower letter and lower roman in turn; each level steps 360 twips in from 720.</summary>
    public static AbstractNumbering Numbered(int abstractNumId)
    {
        var definition = New(abstractNumId, "hybridMultilevel");
        string[] formats = ["decimal", "lowerLetter", "lowerRoman"];
        for (var level = 0; level < LevelCount; level++)
        {
            definition.Levels.Add(Level(level, formats[level % 3], "%" + I(level + 1) + ".", 720 + (level * 360), 360));
        }

        return definition;
    }

    private static AbstractNumbering New(int abstractNumId, string multiLevelType)
    {
        var definition = new AbstractNumbering { AbstractNumId = abstractNumId };
        definition.KeptChildren.Add(new KeptChild(OrderedElement.NewPosition, new UnknownContent(
            new XElement(W + "multiLevelType", new XAttribute(W + "val", multiLevelType)))));
        return definition;
    }

    private static NumberingLevel Level(int level, string format, string text, int left, int hanging)
    {
        var item = new NumberingLevel { Level = level, Start = 1, Format = format, Text = text, Justification = "left" };
        item.ParagraphProperties.Set(W + "ind", new XElement(
            W + "ind",
            new XAttribute(W + "left", I(left)),
            new XAttribute(W + "hanging", I(hanging))));
        return item;
    }

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
}
