using Raffica.Write.Layout;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class NumberingCounterTests
{
    private static string Level(int level, string format, string text, string extra = "", string rPr = "") =>
        $"<w:lvl w:ilvl=\"{level}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"{format}\"/>{extra}<w:lvlText w:val=\"{text}\"/>{rPr}</w:lvl>";

    private static string Item(int numId, int level) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{numId}\"/></w:numPr></w:pPr></w:p>";

    private static List<NumberingLabel?> Run(string numbering, params (int NumId, int Level)[] items)
    {
        var document = LayoutModel.Document(
            string.Concat(items.Select(i => Item(i.NumId, i.Level))),
            LayoutModel.StylesPart("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"/>"),
            LayoutModel.NumberingPart(numbering));
        var resolver = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        var counter = new NumberingCounter(document.Numbering);
        var result = new List<NumberingLabel?>();
        for (var i = 0; i < items.Length; i++)
        {
            result.Add(counter.Next(resolver.ForParagraph(LayoutModel.Para(document, i))));
        }

        return result;
    }

    private static List<string?> Texts(string numbering, params (int NumId, int Level)[] items) =>
        Run(numbering, items).Select(l => l?.Text).ToList();

    private static string Multi(string levels) =>
        "<w:abstractNum w:abstractNumId=\"0\">" + levels + "</w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";

    [Fact]
    public void HeadingNumbersCount()
    {
        var texts = Texts(
            Multi(Level(0, "decimal", "%1") + Level(1, "decimal", "%1.%2")),
            (1, 0), (1, 1), (1, 1), (1, 0), (1, 1));
        Assert.Equal<string?>(["1", "1.1", "1.2", "2", "2.1"], texts);
    }

    [Fact]
    public void LowerRomanAndLetters()
    {
        var texts = Texts(
            Multi(Level(0, "lowerRoman", "%1") + Level(1, "lowerLetter", "%2")),
            (1, 0), (1, 0), (1, 0), (1, 0));
        Assert.Equal<string?>(["i", "ii", "iii", "iv"], texts);
        var letters = Texts(
            Multi(Level(0, "decimal", "%1") + Level(1, "lowerLetter", "%2") + Level(2, "upperRoman", "%3") + Level(3, "upperLetter", "%4")),
            (1, 1), (1, 1), (1, 1), (1, 2), (1, 2), (1, 2), (1, 2), (1, 3));
        Assert.Equal<string?>(["a", "b", "c", "I", "II", "III", "IV", "A"], letters);
        var wide = Texts(Multi(Level(0, "lowerLetter", "%1")), Enumerable.Repeat((1, 0), 28).ToArray());
        Assert.Equal("z", wide[25]);
        Assert.Equal("aa", wide[26]);
        Assert.Equal("bb", wide[27]);
        Assert.Equal<string?>(["01", "02"], Texts(Multi(Level(0, "decimalZero", "%1")), (1, 0), (1, 0)));
    }

    [Fact]
    public void RestartAfterHigherLevel()
    {
        var plain = Texts(Multi(Level(0, "decimal", "%1") + Level(1, "decimal", "%2")), (1, 1), (1, 1), (1, 0), (1, 1));
        Assert.Equal<string?>(["1", "2", "1", "1"], plain);
        var never = Texts(
            Multi(Level(0, "decimal", "%1") + Level(1, "decimal", "%2", "<w:lvlRestart w:val=\"0\"/>")),
            (1, 1), (1, 1), (1, 0), (1, 1));
        Assert.Equal<string?>(["1", "2", "1", "3"], never);
    }

    [Fact]
    public void StartOverrideApplies()
    {
        var numbering = "<w:abstractNum w:abstractNumId=\"0\">" + Level(0, "decimal", "%1") + "</w:abstractNum>"
            + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>"
            + "<w:num w:numId=\"2\"><w:abstractNumId w:val=\"0\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"5\"/></w:lvlOverride></w:num>";
        Assert.Equal<string?>(["1", "2", "5", "6"], Texts(numbering, (1, 0), (1, 0), (2, 0), (2, 0)));
    }

    [Fact]
    public void LevelStartIsUsed()
    {
        var numbering = Multi("<w:lvl w:ilvl=\"0\"><w:start w:val=\"3\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl>");
        Assert.Equal<string?>(["3.", "4."], Texts(numbering, (1, 0), (1, 0)));
    }

    [Fact]
    public void BulletHasNoNumber()
    {
        var numbering = Multi(
            Level(0, "bullet", "", "<w:suff w:val=\"space\"/>", "<w:rPr><w:rFonts w:ascii=\"Symbol\" w:hAnsi=\"Symbol\"/><w:b/></w:rPr>")
            + Level(1, "decimal", "%2"));
        var labels = Run(numbering, (1, 0), (1, 1), (1, 0));
        Assert.Equal("•", labels[0]!.Text);
        Assert.Equal(NumberSuffix.Space, labels[0]!.Suffix);
        Assert.True(labels[0]!.Run.Bold);
        Assert.NotEqual("Symbol", labels[0]!.Run.FontName);
        Assert.Equal("1", labels[1]!.Text);
        Assert.Equal(NumberSuffix.Tab, labels[1]!.Suffix);
    }

    [Fact]
    public void EveryPrivateUseBulletOfSymbolAndWingdingsDrawsAsTheRoundBullet()
    {
        // Symbol U+F0B7 and the Wingdings square U+F0A7: the square has no glyph in Caladea, so both are the one bullet every shipped face has.
        var numbering = Multi(
            Level(0, "bullet", "", string.Empty, "<w:rPr><w:rFonts w:ascii=\"Symbol\" w:hAnsi=\"Symbol\"/></w:rPr>")
            + Level(1, "bullet", "", string.Empty, "<w:rPr><w:rFonts w:ascii=\"Wingdings\" w:hAnsi=\"Wingdings\"/></w:rPr>"));
        var labels = Run(numbering, (1, 0), (1, 1));
        Assert.Equal(["•", "•"], labels.Select(l => l!.Text).ToArray());
    }

    [Fact]
    public void UnknownNumIdGivesNoLabel()
    {
        var numbering = Multi(Level(0, "decimal", "%1"));
        Assert.Equal<string?>([null, null, "1"], Texts(numbering, (99, 0), (0, 0), (1, 0)));
        var document = LayoutModel.Document("<w:p/>");
        var counter = new NumberingCounter(null);
        Assert.Null(counter.Next(new StyleResolver(null, null).ForParagraph(LayoutModel.Para(document, 0))));
        Assert.Equal<string?>([null], Texts(numbering, (1, 12)));
    }

    [Fact]
    public void StyleNumberingResolvesIntoTheParagraph()
    {
        var document = LayoutModel.Document(
            "<w:p><w:pPr><w:pStyle w:val=\"H1\"/></w:pPr></w:p>",
            LayoutModel.StylesPart("<w:style w:type=\"paragraph\" w:styleId=\"H1\"><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>"),
            LayoutModel.NumberingPart(Multi("<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1\"/><w:pPr><w:ind w:left=\"432\" w:hanging=\"432\"/></w:pPr></w:lvl>")));
        var resolver = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        var resolved = resolver.ForParagraph(LayoutModel.Para(document, 0));
        Assert.Equal(1, resolved.NumberingId);
        Assert.Equal(0, resolved.NumberingLevel);
        Assert.Equal(21.6f, resolved.LeftIndent);
        Assert.Equal(21.6f, resolved.HangingIndent);
        Assert.Equal("1", new NumberingCounter(document.Numbering).Next(resolved)!.Text);
    }
}
