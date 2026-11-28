using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// The list numbers of a document's body, counted from the model alone and written from Word's documented behaviour, so that a test can compare
/// them with what the layout draws without the layout's own counter checking itself. It uses neither <c>NumberingCounter</c> nor
/// <c>StyleResolver</c>: it reads the numbering definitions and the style chain (<c>basedOn</c>) directly.
/// <para>
/// The rules: the effective <c>numPr</c> of a paragraph is its own, per child, over the one of its paragraph style chain (the default paragraph
/// style when it has none); <c>numId</c> 0 means no list; lists that share an abstract definition share their counters; counting a level restarts
/// every deeper level (unless that level's <c>w:lvlRestart</c> says otherwise); a <c>w:startOverride</c> of a <c>w:num</c> applies the first time that
/// <c>w:num</c> is used at that level; <c>%1</c> to <c>%9</c> in <c>lvlText</c> give the number of that level (the start value of a level that
/// has not counted yet), in decimal when the level is <c>w:isLgl</c>.
/// </para>
/// </summary>
internal static class ReferenceNumbering
{
    private const int Levels = 9;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static IReadOnlyList<(Paragraph Paragraph, string Label)> Labels(WordDocument document) => [.. Count(document).Select(c => (c.Paragraph, c.Label))];

    /// <summary>What <c>STYLEREF n \s</c> shows for the numbered paragraphs: the level text cut from its first to its last placeholder, with the numbers filled in (a bullet and a text without a number give nothing).</summary>
    public static IReadOnlyList<(Paragraph Paragraph, string Numbers)> ChapterNumbers(WordDocument document) => [.. Count(document).Where(c => c.Numbers.Length > 0).Select(c => (c.Paragraph, c.Numbers))];

    private static List<(Paragraph Paragraph, string Label, string Numbers)> Count(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<(Paragraph, string, string)>();
        if (document.Numbering is not { } numbering)
        {
            return result;
        }

        var styles = document.Styles;
        var defaultStyle = styles?.Styles.FirstOrDefault(s => s.IsDefault && (s.Type ?? "paragraph") == "paragraph");
        var counters = new Dictionary<int, int[]>();
        var started = new Dictionary<int, bool[]>();
        var overridden = new HashSet<(int, int)>();

        foreach (var block in ModelWalk.Blocks(document.Body.Blocks))
        {
            if (block is not Paragraph paragraph)
            {
                continue;
            }

            var (numId, level) = EffectiveNumbering(paragraph, styles, defaultStyle);
            if (numId <= 0 || level is < 0 or >= Levels || numbering.FindInstance(numId) is not { AbstractNumId: { } abstractId } instance)
            {
                continue;
            }

            var facts = LevelOf(numbering, instance, level);
            if (facts is null)
            {
                continue;
            }

            if (!counters.TryGetValue(abstractId, out var count))
            {
                count = new int[Levels];
                counters[abstractId] = count;
                started[abstractId] = new bool[Levels];
            }

            var has = started[abstractId];
            int value;
            if (overridden.Add((numId, level)) && instance.Overrides.FirstOrDefault(o => o.Level == level && o.StartOverride is not null)?.StartOverride is { } over)
            {
                value = over;
            }
            else
            {
                value = has[level] ? count[level] + 1 : facts.Start;
            }

            count[level] = value;
            has[level] = true;
            for (var deeper = level + 1; deeper < Levels; deeper++)
            {
                var restart = LevelOf(numbering, instance, deeper)?.Restart;
                if (restart is null || (restart > 0 && level < restart))
                {
                    has[deeper] = false;
                }
            }

            var from = facts.Text.IndexOf('%', StringComparison.Ordinal);
            var cut = facts.Format == "bullet" || from < 0 ? null : facts.Text[from..(facts.Text.LastIndexOf('%') + 2)];
            result.Add((paragraph, Text(numbering, instance, facts, facts.Text, level, count, has), cut is null ? string.Empty : Text(numbering, instance, facts, cut, level, count, has)));
        }

        return result;
    }

    /// <summary>True when the level of the paragraph's list puts a tab after its label: no <c>w:suff</c>, or one that names neither a space nor nothing.</summary>
    public static bool TabFollows(WordDocument document, Paragraph paragraph)
    {
        var defaultStyle = document.Styles?.Styles.FirstOrDefault(s => s.IsDefault && (s.Type ?? "paragraph") == "paragraph");
        var (numId, level) = EffectiveNumbering(paragraph, document.Styles, defaultStyle);
        if (numId <= 0 || document.Numbering is not { } numbering || numbering.FindInstance(numId) is not { } instance)
        {
            return false;
        }

        var definition = instance.Overrides.FirstOrDefault(o => o.Level == level && o.Definition is not null)?.Definition
            ?? (instance.AbstractNumId is { } id ? numbering.FindAbstract(id)?.Levels.FirstOrDefault(l => l.Level == level) : null);
        return definition is not null
            && !definition.KeptChildren.Any(c => c.Content.Element.Name == W + "suff" && c.Content.Element.Attribute(W + "val")?.Value is "space" or "nothing");
    }

    private static (int NumId, int Level) EffectiveNumbering(Paragraph paragraph, StyleSheet? styles, Style? defaultStyle)
    {
        var numId = -1;
        var level = 0;
        var styleId = paragraph.Properties.Get(W + "pStyle")?.Attribute(W + "val")?.Value;
        var start = styleId is not null && styles?.Find(styleId) is { } named ? named : defaultStyle;
        var chain = new List<Style>();
        for (var style = start; style is not null && !chain.Contains(style); style = style.BasedOn is { } parent ? styles?.Find(parent) : null)
        {
            chain.Add(style);
        }

        chain.Reverse();
        foreach (var style in chain)
        {
            Overlay(style.ParagraphProperties, ref numId, ref level);
        }

        Overlay(paragraph.Properties, ref numId, ref level);
        return (numId, level);
    }

    private static void Overlay(PropertyBag bag, ref int numId, ref int level)
    {
        if (bag.Get(W + "numPr") is not { } numPr)
        {
            return;
        }

        if (Int(numPr.Element(W + "numId")) is { } id)
        {
            numId = id;
        }

        if (Int(numPr.Element(W + "ilvl")) is { } ilvl)
        {
            level = ilvl;
        }
    }

    private static int? Int(XElement? element) =>
        element?.Attribute(W + "val")?.Value is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static LevelFacts? LevelOf(NumberingDefinitions numbering, NumberingInstance instance, int level)
    {
        // A level definition inside a w:lvlOverride replaces the abstract one for that w:num.
        var definition = instance.Overrides.FirstOrDefault(o => o.Level == level && o.Definition is not null)?.Definition
            ?? (instance.AbstractNumId is { } id ? numbering.FindAbstract(id)?.Levels.FirstOrDefault(l => l.Level == level) : null);
        return definition is null ? null : new LevelFacts(definition);
    }

    private static string Text(NumberingDefinitions numbering, NumberingInstance instance, LevelFacts facts, string text, int level, int[] count, bool[] has)
    {
        if (facts.Format == "bullet")
        {
            return text;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 1 < text.Length && text[i + 1] is >= '1' and <= '9')
            {
                var referenced = text[i + 1] - '1';
                i++;
                var other = referenced == level ? facts : LevelOf(numbering, instance, referenced);
                var number = has[referenced] ? count[referenced] : other?.Start ?? 1;
                builder.Append(Format(facts.IsLegal ? "decimal" : other?.Format ?? "decimal", number));
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    private static string Format(string format, int number) => format switch
    {
        "lowerLetter" when number > 0 => Letters(number, 'a'),
        "upperLetter" when number > 0 => Letters(number, 'A'),
        "lowerRoman" when number is > 0 and < 4000 => Roman(number).ToLowerInvariant(),
        "upperRoman" when number is > 0 and < 4000 => Roman(number),
        "decimalZero" when number is >= 0 and < 10 => "0" + number.ToString(CultureInfo.InvariantCulture),
        "none" => string.Empty,
        _ => number.ToString(CultureInfo.InvariantCulture),
    };

    // Word counts a, b, ... z, aa, bb, ... zz, aaa, ...
    private static string Letters(int number, char first) => new((char)(first + ((number - 1) % 26)), ((number - 1) / 26) + 1);

    private static string Roman(int number)
    {
        (int Value, string Symbol)[] table = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var builder = new StringBuilder();
        foreach (var (value, symbol) in table)
        {
            while (number >= value)
            {
                builder.Append(symbol);
                number -= value;
            }
        }

        return builder.ToString();
    }

    private sealed class LevelFacts
    {
        public LevelFacts(NumberingLevel level)
        {
            Format = level.Format ?? "decimal";
            Text = level.Text ?? string.Empty;
            Start = level.Start ?? 1;
            foreach (var kept in level.KeptChildren)
            {
                var element = kept.Content.Element;
                if (element.Name == W + "lvlRestart")
                {
                    Restart = Int(element);
                }
                else if (element.Name == W + "isLgl")
                {
                    IsLegal = element.Attribute(W + "val")?.Value is null or "1" or "true" or "on";
                }
            }
        }

        public string Format { get; }

        public string Text { get; }

        public int Start { get; }

        /// <summary><c>w:lvlRestart</c>: null when absent (restarts after any higher level), 0 never, n after level n (one based) or a higher one.</summary>
        public int? Restart { get; }

        public bool IsLegal { get; }
    }
}
