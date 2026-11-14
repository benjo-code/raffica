using Raffica.Write.Layout;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class HyphenatorTests
{
    private static readonly FontCatalog s_fonts = FontCatalog.CreateShipped(new NoSystemFonts());

    private sealed class NoSystemFonts : ISystemFonts
    {
        public FontFace? Match(string family, bool bold, bool italic) => null;
    }

    private static ResolvedRun Run(string? language = "en-US") =>
        new() { FontName = "Calibri", SizePoints = 11f, Language = language };

    private static ResolvedParagraph Para(string pPr)
    {
        var document = LayoutModel.Document($"<w:p><w:pPr>{pPr}</w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        var resolver = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        return resolver.ForParagraph(LayoutModel.Para(document, 0));
    }

    private static ParagraphInput Input(string text, string pPr = "", string? language = "en-US", bool hyphenate = true) =>
        new(Para(pPr), [new TextSpan(text, Run(language))], Run(language)) { AutoHyphenation = hyphenate };

    private static ParagraphLayout Lay(ParagraphInput input, float width) =>
        new ParagraphLayouter(s_fonts) { Hyphenator = Hyphenator.Default }.Layout(input, width);

    private static float Natural(string text) => Lay(Input(text, hyphenate: false), 100000f).Lines[0].Width;

    private static string Hyphenated(Hyphenator hyphenator, string word, string? language)
    {
        var points = new List<int>();
        hyphenator.Find(word, language, points);
        var result = word;
        foreach (var p in points.OrderDescending())
        {
            result = result.Insert(p, "-");
        }

        return result;
    }

    [Fact]
    public void EnglishPoints()
    {
        Assert.Equal("hy-phen-ation", Hyphenated(Hyphenator.Default, "hyphenation", "en-US"));
        // The British patterns also allow a break before the last syllable (hyph-en-gb.pat.txt: Hy-phen-a-tion).
        Assert.Equal("Hy-phen-a-tion", Hyphenated(Hyphenator.Default, "Hyphenation", "en-GB"));
        Assert.Equal("al-go-rithm", Hyphenated(Hyphenator.Default, "algorithm", "en-US"));
    }

    [Fact]
    public void GermanPoints()
    {
        Assert.Equal("Sil-ben-tren-nung", Hyphenated(Hyphenator.Default, "Silbentrennung", "de-DE"));
    }

    [Fact]
    public void ShortWordsAreNotBroken()
    {
        foreach (var word in new[] { "a", "tion", "pain", "Haus", "" })
        {
            Assert.Equal(word, Hyphenated(Hyphenator.Default, word, "en-US"));
        }
    }

    [Fact]
    public void UnknownLanguageHasNoPoints()
    {
        foreach (var language in new[] { "fr-FR", "en-AU", "", null, "xx" })
        {
            Assert.Equal("hyphenation", Hyphenated(Hyphenator.Default, "hyphenation", language));
        }
    }

    [Fact]
    public void WordsWithNonLettersAreNotBroken()
    {
        Assert.Equal("hyphen4tion", Hyphenated(Hyphenator.Default, "hyphen4tion", "en-US"));
    }

    [Fact]
    public void HyphenAddsAHyphenGlyphAtTheBreak()
    {
        var natural = Natural("go hyphen-");
        var width = natural + 1f;
        Assert.True(Natural("go hyphenation") > width);
        var layout = Lay(Input("go hyphenation", "<w:jc w:val=\"both\"/>"), width);
        Assert.Equal(["go hyphen", "ation"], layout.Lines.Select(layout.TextOf).ToList());

        var first = layout.Lines[0];
        Assert.True(first.Width <= width + 0.01f);
        Assert.True(first.Runs[^1].IsHyphen);
        Assert.Equal(1, first.Runs[^1].Glyphs.Length);
        Assert.Equal(first.Runs[^1].TextStart, first.Runs[^1].TextEnd);
        Assert.Equal(first.End, first.Runs[^1].TextEnd);

        // The hyphen is part of what the line measures and the justified line is stretched to the full width.
        var plain = Lay(Input("go hyphen", "<w:jc w:val=\"both\"/>", hyphenate: false), 100000f).Lines[0].Width;
        Assert.True(plain < width - 1f);
        Assert.Equal(width, first.Width, 0.05f);
        Assert.Equal(first.Runs[^1].X + first.Runs[^1].Width, first.X + first.Width, 0.05f);
    }

    [Fact]
    public void HyphenatedWordFillsLinesAndKeepsTheText()
    {
        var text = "Extraordinary hyphenation of international typography";
        var layout = Lay(Input(text), Natural("Extraordinary hyphen-") + 1f);
        Assert.True(layout.Lines.Count >= 3);
        var joined = string.Concat(layout.Lines.Select(l => layout.Text.Substring(l.Start, l.End - l.Start)));
        Assert.Equal(text.Replace(" ", string.Empty, StringComparison.Ordinal), joined.Replace(" ", string.Empty, StringComparison.Ordinal));
        foreach (var line in layout.Lines)
        {
            Assert.True(line.Width <= Natural("Extraordinary hyphen-") + 1.01f || line.Runs.Count == 1);
        }
    }

    [Fact]
    public void OffByDefault()
    {
        var width = Natural("go hyphen-") + 1f;
        var layout = Lay(Input("go hyphenation", hyphenate: false), width);
        Assert.Equal(["go", "hyphenation"], layout.Lines.Select(layout.TextOf).ToList());
        Assert.DoesNotContain(layout.Lines.SelectMany(l => l.Runs), r => r.IsHyphen);
        Assert.False(new ParagraphInput(Para(string.Empty), [], Run()).AutoHyphenation);
    }

    [Fact]
    public void SuppressAutoHyphensWins()
    {
        var width = Natural("go hyphen-") + 1f;
        var layout = Lay(Input("go hyphenation", "<w:suppressAutoHyphens/>"), width);
        Assert.Equal(["go", "hyphenation"], layout.Lines.Select(layout.TextOf).ToList());
        Assert.DoesNotContain(layout.Lines.SelectMany(l => l.Runs), r => r.IsHyphen);
    }

    [Fact]
    public void OtherLanguagesAreNotHyphenatedInTheLayout()
    {
        var width = Natural("go hyphen-") + 1f;
        var layout = Lay(Input("go hyphenation", language: "fr-FR"), width);
        Assert.Equal(["go", "hyphenation"], layout.Lines.Select(layout.TextOf).ToList());
    }

    [Fact]
    public void NoHyphenatorMeansNoHyphenation()
    {
        var width = Natural("go hyphen-") + 1f;
        var layout = new ParagraphLayouter(s_fonts).Layout(Input("go hyphenation"), width);
        Assert.Equal(["go", "hyphenation"], layout.Lines.Select(layout.TextOf).ToList());
    }

    [Fact]
    public void PatternLoadIsLazy()
    {
        var hyphenator = new Hyphenator();
        Assert.Equal(0, hyphenator.PatternSetsLoaded);
        Assert.Equal("pain", Hyphenated(hyphenator, "pain", "en-US"));
        Assert.Equal("word", Hyphenated(hyphenator, "word", "fr-FR"));
        Assert.Equal("hyphenation", Hyphenated(hyphenator, "hyphenation", "fr-FR"));
        Assert.Equal(0, hyphenator.PatternSetsLoaded);

        Assert.Equal("hy-phen-ation", Hyphenated(hyphenator, "hyphenation", "en-US"));
        Assert.Equal(1, hyphenator.PatternSetsLoaded);
        Hyphenated(hyphenator, "algorithm", "en-US");
        Assert.Equal(1, hyphenator.PatternSetsLoaded);
        Hyphenated(hyphenator, "Silbentrennung", "de-DE");
        Assert.Equal(2, hyphenator.PatternSetsLoaded);
    }

    [Fact]
    public void HugeWordsLayOutInBoundedTimeAndLoseNoText()
    {
        var huge = new string('a', 200_000);
        var many = string.Join(' ', Enumerable.Repeat("abcdefghijkl", 30_000));
        foreach (var text in new[] { huge, many })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var layout = Lay(Input(text), 200f);
            watch.Stop();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
            var length = layout.Lines.Sum(l => l.End - l.Start);
            Assert.Equal(text.Length, length);
        }
    }

    [Fact]
    public void WordsLongerThanTheCapAreNotHyphenated()
    {
        var width = Natural("go hyphen-") + 1f;
        var shorter = string.Concat(Enumerable.Repeat("hyphenation", 5)) + "hyph";
        var longer = shorter + "hy";
        Assert.Equal(59, shorter.Length);
        Assert.Equal(61, longer.Length);
        var allowed = Lay(Input("go " + shorter), width);
        Assert.Contains(allowed.Lines.SelectMany(l => l.Runs), r => r.IsHyphen);
        var refused = Lay(Input("go " + longer), width);
        Assert.DoesNotContain(refused.Lines.SelectMany(l => l.Runs), r => r.IsHyphen);
        Assert.Equal(["go", longer], refused.Lines.Select(refused.TextOf).ToList());
    }
}
