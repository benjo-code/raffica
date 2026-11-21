using System.Diagnostics;
using Raffica.Write.Layout;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class ParagraphLayoutTests
{
    private const string Sentence = "The quick brown fox jumps over the lazy dog and keeps running far away";

    private static readonly FontCatalog s_fonts = FontCatalog.CreateShipped(new NoSystemFonts());

    private sealed class NoSystemFonts : ISystemFonts
    {
        public FontFace? Match(string family, bool bold, bool italic) => null;
    }

    private sealed class NarrowingWrap(float leftCut, float rightCut) : IWrapSource
    {
        public List<float> Asked { get; } = [];

        public LineBounds BoundsAt(float y, float lineHeight, float left, float right)
        {
            Asked.Add(y);
            return new LineBounds(left + leftCut, right - rightCut);
        }
    }

    private static ResolvedRun Run(string font = "Calibri", float size = 11f, bool bold = false, bool italic = false) =>
        new() { FontName = font, SizePoints = size, Bold = bold, Italic = italic };

    private static (ResolvedParagraph Paragraph, LayoutDocument Document) Resolve(string pPr, string? numbering = null)
    {
        var body = $"<w:p><w:pPr>{pPr}</w:pPr><w:r><w:t>x</w:t></w:r></w:p>";
        var document = LayoutModel.Document(body, numbering: numbering);
        var resolver = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        return (resolver.ForParagraph(LayoutModel.Para(document, 0)), new LayoutDocument(document, resolver));
    }

    private sealed record LayoutDocument(Raffica.Write.Model.WordDocument Model, StyleResolver Resolver);

    private static ResolvedParagraph Para(string pPr = "") => Resolve(pPr).Paragraph;

    private static ParagraphInput Input(string text, string pPr = "", ResolvedRun? run = null) =>
        new(Para(pPr), [new TextSpan(text, run ?? Run())], run ?? Run());

    private static ParagraphLayout Lay(ParagraphInput input, float width, IWrapSource? wrap = null) =>
        new ParagraphLayouter(s_fonts).Layout(input, width, wrap);

    private static List<string> Lines(ParagraphLayout layout) => layout.Lines.Select(layout.TextOf).ToList();

    private static float Natural(string text, ResolvedRun? run = null)
    {
        var layout = Lay(Input(text, run: run), 100000f);
        return layout.Lines[0].Width;
    }

    private static GlyphRun RunAt(LineBox line, int textStart) => line.Runs.First(r => !r.IsLeader && !r.IsLabel && r.TextStart == textStart);

    [Fact]
    public void GreedyBreakAtSpaces()
    {
        var layout = Lay(Input(Sentence), 130f);
        var lines = Lines(layout);
        Assert.Equal(["The quick brown fox jumps", "over the lazy dog and keeps", "running far away"], lines);

        // Greedy: each line fits, and the first word of the next line would not have fitted.
        for (var i = 0; i < lines.Count; i++)
        {
            Assert.True(layout.Lines[i].Width <= 130.01f);
            if (i + 1 < lines.Count)
            {
                var nextWord = lines[i + 1].Split(' ')[0];
                Assert.True(Natural(lines[i] + " " + nextWord) > 130f);
            }
        }

        Assert.Equal(0, layout.Lines[0].Start);
        Assert.Equal(layout.Lines[1].Start, layout.Lines[0].End);
        Assert.Equal(Sentence.Length, layout.Lines[^1].End);
    }

    [Fact]
    public void LineHeightSingleIsFontHeight()
    {
        var face = s_fonts.Resolve("Calibri", false, false);
        var expected = (face.Ascent + face.Descent + face.LineGap) * 11f;
        var layout = Lay(Input("Hello"), 200f);
        Assert.Equal(expected, layout.Lines[0].Height, 0.01f);
        Assert.Equal(expected, layout.Height, 0.01f);
        Assert.Equal(face.Ascent * 11f, layout.Lines[0].Ascent, 0.01f);
        Assert.Equal(layout.Lines[0].Height - (face.Descent * 11f), layout.Lines[0].Baseline, 0.01f);
    }

    // Word puts the extra room of a multiple above single below the text of the line (measured on the PDFs of the thesis: the first line of a page stands one
    // ascent below the top margin whatever the multiple), and lets it hang below the text area at the end of a page.
    [Fact]
    public void TheExtraRoomOfAMultipleIsBelowTheTextAndIsNotNeededToFit()
    {
        var face = s_fonts.Resolve("Calibri", false, false);
        var natural = (face.Ascent + face.Descent + face.LineGap) * 11f;
        var line = Lay(Input("Hello", "<w:spacing w:line=\"360\" w:lineRule=\"auto\"/>"), 200f).Lines[0];

        Assert.Equal(natural - (face.Descent * 11f), line.Baseline, 0.01f);
        Assert.Equal(natural * 0.5f, line.Extra, 0.01f);
        Assert.Equal(0f, Lay(Input("Hello", "<w:spacing w:line=\"240\" w:lineRule=\"auto\"/>"), 200f).Lines[0].Extra, 0.01f);
        Assert.Equal(0f, Lay(Input("Hello", "<w:spacing w:line=\"400\" w:lineRule=\"atLeast\"/>", Run(size: 8f)), 200f).Lines[0].Extra, 0.01f);
    }

    [Fact]
    public void MultipleAtLeastExactSpacing()
    {
        var face = s_fonts.Resolve("Calibri", false, false);
        var natural = (face.Ascent + face.Descent + face.LineGap) * 11f;
        Assert.Equal(natural * 1.5f, Lay(Input("Hello", "<w:spacing w:line=\"360\" w:lineRule=\"auto\"/>"), 200f).Lines[0].Height, 0.01f);

        var small = Run(size: 8f);
        Assert.Equal(20f, Lay(Input("Hello", "<w:spacing w:line=\"400\" w:lineRule=\"atLeast\"/>", small), 200f).Lines[0].Height, 0.01f);

        var big = Run(size: 40f);
        var bigNatural = (face.Ascent + face.Descent + face.LineGap) * 40f;
        Assert.True(bigNatural > 20f);
        Assert.Equal(bigNatural, Lay(Input("Hello", "<w:spacing w:line=\"400\" w:lineRule=\"atLeast\"/>", big), 400f).Lines[0].Height, 0.01f);
        Assert.Equal(14f, Lay(Input("Hello", "<w:spacing w:line=\"280\" w:lineRule=\"exact\"/>", big), 400f).Lines[0].Height, 0.01f);
    }

    [Fact]
    public void FirstLineAndHangingIndent()
    {
        var first = Lay(Input(Sentence, "<w:ind w:firstLine=\"360\"/>"), 150f);
        Assert.True(first.Lines.Count >= 3);
        Assert.Equal(18f, first.Lines[0].X, 0.01f);
        Assert.Equal(0f, first.Lines[1].X, 0.01f);
        Assert.Equal(18f, first.Lines[0].Runs[0].X, 0.01f);

        var hanging = Lay(Input(Sentence, "<w:ind w:left=\"720\" w:hanging=\"360\"/>"), 150f);
        Assert.Equal(18f, hanging.Lines[0].X, 0.01f);
        Assert.Equal(36f, hanging.Lines[1].X, 0.01f);
        foreach (var line in hanging.Lines.Skip(1))
        {
            Assert.True(line.X + line.Width <= 150.01f);
        }
    }

    [Fact]
    public void RightAndCenterAlignment()
    {
        var natural = Natural("Hello");
        var right = Lay(Input("Hello", "<w:jc w:val=\"right\"/>"), 200f).Lines[0];
        Assert.Equal(200f, right.X + right.Width, 0.05f);
        Assert.Equal(200f - natural, right.X, 0.05f);
        Assert.Equal(right.X, right.Runs[0].X, 0.05f);

        var center = Lay(Input("Hello", "<w:jc w:val=\"center\"/>"), 200f).Lines[0];
        Assert.Equal((200f - natural) / 2f, center.X, 0.05f);

        // Trailing spaces do not move the text.
        var spaced = Lay(Input("Hello   ", "<w:jc w:val=\"right\"/>"), 200f).Lines[0];
        Assert.Equal(200f - natural, spaced.X, 0.05f);
    }

    [Fact]
    public void JustifyStretchesAllButTheLastLine()
    {
        var layout = Lay(Input(Sentence, "<w:jc w:val=\"both\"/>"), 150f);
        Assert.Equal(3, layout.Lines.Count);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(150f, layout.Lines[i].Width, 0.05f);
            var line = layout.Lines[i];
            Assert.Single(line.Runs);
            Assert.True(line.Runs[0].X + line.Runs[0].Xs[^1] <= 150.05f);
            Assert.True(line.Runs[0].Xs[^1] > 100f);
        }

        var lastLine = layout.Lines[2];
        Assert.Equal(Natural("far away"), lastLine.Width, 0.05f);
        Assert.Equal(0f, lastLine.X, 0.01f);
        Assert.Equal("far away", layout.TextOf(lastLine));
        Assert.Equal(["The quick brown fox jumps over", "the lazy dog and keeps running", "far away"], Lines(layout));
    }

    [Fact]
    public void TabStopsLeftCenterRightDecimalWithLeaders()
    {
        const string Tabs =
            "<w:tabs><w:tab w:val=\"left\" w:pos=\"1440\"/><w:tab w:val=\"center\" w:pos=\"4320\" w:leader=\"dot\"/>"
            + "<w:tab w:val=\"right\" w:pos=\"7200\" w:leader=\"underscore\"/><w:tab w:val=\"decimal\" w:pos=\"9360\"/></w:tabs>";
        var layout = Lay(Input("A\tB\tC\tD\t12.5", Tabs), 600f);
        Assert.Single(layout.Lines);
        var line = layout.Lines[0];

        Assert.Equal(72f, RunAt(line, 2).X, 0.05f);
        var c = RunAt(line, 4);
        Assert.Equal(216f, c.X + (c.Width / 2f), 0.05f);
        var d = RunAt(line, 6);
        Assert.Equal(360f, d.X + d.Width, 0.05f);
        var number = RunAt(line, 8);
        Assert.Equal(468f, number.X + Natural("12"), 0.1f);

        var leaders = line.Runs.Where(r => r.IsLeader).OrderBy(r => r.X).ToList();
        Assert.Equal(2, leaders.Count);
        var b = RunAt(line, 2);
        Assert.True(leaders[0].X >= b.X + b.Width - 0.01f);
        Assert.True(leaders[0].X + leaders[0].Width <= c.X + 0.01f);
        Assert.True(leaders[0].Glyphs.Length > 10);
        Assert.True(leaders[1].X >= c.X + c.Width - 0.01f);
        Assert.True(leaders[1].X + leaders[1].Width <= d.X + 0.01f);
        Assert.NotEqual(leaders[0].Glyphs[0], leaders[1].Glyphs[0]);
    }

    [Fact]
    public void DefaultTabEvery720Twips()
    {
        var line = Lay(Input("a\tb\tc"), 300f).Lines[0];
        Assert.Equal(36f, RunAt(line, 2).X, 0.01f);
        Assert.Equal(72f, RunAt(line, 4).X, 0.01f);

        // After the last custom stop the grid continues; stops before it are not used.
        var custom = Lay(Input("a\tb\tc", "<w:tabs><w:tab w:val=\"left\" w:pos=\"1000\"/></w:tabs>"), 300f).Lines[0];
        Assert.Equal(50f, RunAt(custom, 2).X, 0.01f);
        Assert.Equal(72f, RunAt(custom, 4).X, 0.01f);
    }

    [Fact]
    public void NumberingLabelTabsToTheHangingIndent()
    {
        const string Numbering =
            "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/>"
            + "<w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        var (paragraph, doc) = Resolve("<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>", LayoutModel.NumberingPart(Numbering));
        var label = new NumberingCounter(doc.Model.Numbering).Next(paragraph);
        Assert.NotNull(label);
        var input = new ParagraphInput(paragraph, [new TextSpan(Sentence, Run())], Run(), label);
        var layout = Lay(input, 200f);

        var line = layout.Lines[0];
        var labelRun = line.Runs.First(r => r.IsLabel);
        Assert.Equal(18f, labelRun.X, 0.01f);
        Assert.Equal(36f, RunAt(line, 0).X, 0.01f);
        Assert.Equal(36f, layout.Lines[1].X, 0.01f);
        Assert.StartsWith("The quick brown", layout.TextOf(line), StringComparison.Ordinal);
        Assert.True(line.X + line.Width <= 200.01f);
    }

    [Fact]
    public void SuperscriptShrinksAndRaises()
    {
        var normal = Run();
        var sup = normal with { VerticalAlignment = RunVerticalAlignment.Superscript };
        var sub = normal with { VerticalAlignment = RunVerticalAlignment.Subscript };
        var layout = Lay(new ParagraphInput(Para(), [new TextSpan("x", normal), new TextSpan("2", sup), new TextSpan("i", sub)], normal), 200f);
        var line = layout.Lines[0];
        Assert.Equal(11f, RunAt(line, 0).Size);
        Assert.True(RunAt(line, 1).Size < 11f);
        Assert.True(RunAt(line, 1).Raise > 0f);
        Assert.True(RunAt(line, 2).Size < 11f);
        Assert.True(RunAt(line, 2).Raise < 0f);
        var plain = Lay(Input("x"), 200f).Lines[0];
        Assert.True(line.Ascent > plain.Ascent);
    }

    [Fact]
    public void SmallCapsAndAllCaps()
    {
        var caps = Lay(Input("abc", run: Run() with { Caps = true }), 200f).Lines[0];
        var upper = Lay(Input("ABC"), 200f).Lines[0];
        Assert.Equal(upper.Runs[0].Glyphs.ToArray(), caps.Runs[0].Glyphs.ToArray());
        Assert.Equal(upper.Width, caps.Width, 0.001f);

        var small = Lay(Input("aB", run: Run() with { SmallCaps = true }), 200f).Lines[0];
        Assert.Equal(2, small.Runs.Count);
        Assert.Equal(11f * 0.8f, small.Runs[0].Size, 0.001f);
        Assert.Equal(11f, small.Runs[1].Size, 0.001f);
        Assert.Equal(Lay(Input("A"), 200f).Lines[0].Runs[0].Glyphs.ToArray(), small.Runs[0].Glyphs.ToArray());
        Assert.True(small.Runs[1].X > small.Runs[0].X);
    }

    [Fact]
    public void RunsOfDifferentFontsShareALine()
    {
        var sans = Run();
        var serif = Run("Cambria");
        var layout = Lay(new ParagraphInput(Para(), [new TextSpan("Hello ", sans), new TextSpan("World", serif)], sans), 300f);
        Assert.Single(layout.Lines);
        var line = layout.Lines[0];
        Assert.Equal(2, line.Runs.Count);
        Assert.Equal("Carlito", line.Runs[0].Face.Family);
        Assert.Equal("Caladea", line.Runs[1].Face.Family);
        Assert.True(line.Runs[1].X > line.Runs[0].X);
        var carlito = s_fonts.Resolve("Calibri", false, false);
        var caladea = s_fonts.Resolve("Cambria", false, false);
        Assert.True(line.Height >= Math.Max((carlito.Ascent + carlito.Descent) * 11f, (caladea.Ascent + caladea.Descent) * 11f));
        Assert.True(line.Ascent >= Math.Max(carlito.Ascent, caladea.Ascent) * 11f - 0.01f);
    }

    [Fact]
    public void InlinePictureRaisesTheLineBox()
    {
        var picture = new InlineBox(40f, 60f, new ImageRef("rId5"));
        var layout = Lay(new ParagraphInput(Para(), [new TextSpan("ab", Run()), picture, new TextSpan("cd", Run())], Run()), 300f);
        var line = layout.Lines[0];
        Assert.Single(line.Boxes);
        Assert.True(line.Ascent >= 60f);
        Assert.True(line.Height >= 60f + (s_fonts.Resolve("Calibri", false, false).Descent * 11f) - 0.01f);
        var placed = line.Boxes[0];
        Assert.Same(picture, placed.Box);
        Assert.Equal(line.Baseline - 60f, placed.Top, 0.01f);
        Assert.Equal(0f, placed.Top, 0.01f);
        Assert.True(placed.X > 0f);
        Assert.True(RunAt(line, 3).X >= placed.X + 40f - 0.01f);
    }

    [Fact]
    public void PageBreakEndsTheParagraphLayout()
    {
        var input = new ParagraphInput(Para(), [new TextSpan("Before", Run()), new BreakItem(BreakKind.Page), new TextSpan("After", Run())], Run());
        var layout = Lay(input, 300f);
        Assert.True(layout.EndsWithPageBreak);
        Assert.Equal(BreakKind.Page, layout.EndBreak);
        Assert.Equal(2, layout.RemainderItem);
        Assert.Equal(["Before"], Lines(layout));

        var line = new ParagraphInput(Para(), [new TextSpan("a", Run()), new BreakItem(BreakKind.Line), new TextSpan("b", Run())], Run());
        var two = Lay(line, 300f);
        Assert.False(two.EndsWithPageBreak);
        Assert.Null(two.EndBreak);
        Assert.Equal(3, two.RemainderItem);
        Assert.Equal(["a", "b"], Lines(two));

        var trailing = Lay(new ParagraphInput(Para(), [new TextSpan("a", Run()), new BreakItem(BreakKind.Line)], Run()), 300f);
        Assert.Equal(2, trailing.Lines.Count);
        Assert.Equal(trailing.Lines[0].Height, trailing.Lines[1].Height, 0.01f);
    }

    [Fact]
    public void UnknownInlineShowsAPlaceholder()
    {
        var run = Run();
        var layout = Lay(new ParagraphInput(Para(), [new TextSpan("a", run), InlineBox.Placeholder(run), new TextSpan("b", run)], run), 300f);
        var line = layout.Lines[0];
        Assert.Single(line.Boxes);
        Assert.True(line.Boxes[0].Box.IsPlaceholder);
        Assert.Equal(11f, line.Boxes[0].Box.Width);
        Assert.Equal(11f, line.Boxes[0].Box.Height);
        Assert.True(RunAt(line, 2).X >= line.Boxes[0].X + 11f - 0.01f);
    }

    [Fact]
    public void UnbreakableWordOverflowsInsteadOfLooping()
    {
        var word = new string('x', 60);
        Assert.True(Natural(word) > 200f);
        var layout = Lay(Input(word), 100f);
        Assert.Single(layout.Lines);
        Assert.True(layout.Lines[0].Width > 100f);

        var after = Lay(Input("ab " + word + " cd"), 100f);
        Assert.Equal(["ab", word, "cd"], Lines(after));
    }

    [Fact]
    public void UndrawableCharactersDoNotThrow()
    {
        foreach (var text in new[]
        {
            "a\U0001F600b", "x\u0001y", "lone\uD800surrogate", "tail\uDC00", "漢字 CJK", "￿​‍ mix", "\uD83D",
        })
        {
            foreach (var font in new[] { "Calibri", "Cambria", "NoSuchFont" })
            {
                var layout = Lay(Input(text, run: Run(font)), 80f);
                Assert.NotEmpty(layout.Lines);
                Assert.True(layout.Height > 0f);
                Assert.True(layout.Lines.Sum(l => l.Runs.Sum(r => r.Glyphs.Length)) > 0);
                foreach (var line in layout.Lines)
                {
                    Assert.False(float.IsNaN(line.Width) || float.IsInfinity(line.Width));
                }
            }
        }
    }

    [Fact]
    public void HugeParagraphLaysOutInBoundedTime()
    {
        var sw = Stopwatch.StartNew();
        var noSpaces = Lay(Input(new string('a', 1_000_000)), 500f);
        sw.Stop();
        Assert.Single(noSpaces.Lines);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"no spaces took {sw.ElapsedMilliseconds} ms");

        var words = string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet, ", 7200));
        Assert.True(words.Length >= 200_000);
        sw.Restart();
        var wrapped = Lay(Input(words), 400f);
        sw.Stop();
        Assert.True(wrapped.Lines.Count > 1000);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"words took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(words.Length, wrapped.Lines[^1].End);
    }

    [Fact]
    public void JustifyStretchesALineEndedByAManualBreak()
    {
        var run = Run();
        var jc = Para("<w:jc w:val=\"both\"/>");
        ParagraphItem[] items = [new TextSpan("The quick brown", run), new BreakItem(BreakKind.Line), new TextSpan("fox jumps", run)];
        var input = new ParagraphInput(jc, items, run);

        var stretched = Lay(input, 200f);
        Assert.Equal(2, stretched.Lines.Count);
        Assert.Equal(200f, stretched.Lines[0].Width, 0.05f);
        Assert.Equal(Natural("fox jumps"), stretched.Lines[1].Width, 0.05f);

        var natural = Lay(input with { ExpandShiftReturn = false }, 200f);
        Assert.Equal(Natural("The quick brown"), natural.Lines[0].Width, 0.05f);

        ParagraphItem[] paged = [new TextSpan("The quick brown", run), new BreakItem(BreakKind.Page), new TextSpan("fox", run)];
        var page = Lay(new ParagraphInput(jc, paged, run), 200f);
        Assert.Equal(Natural("The quick brown"), page.Lines[0].Width, 0.05f);
    }

    [Fact]
    public void GlyphRunsAreMergedPerSourceRunAndLine()
    {
        const string Text = "alpha beta gamma delta";
        var one = Lay(Input(Text), 300f).Lines[0];
        Assert.Single(one.Runs);
        Assert.Equal(Text.Length, one.Runs[0].Glyphs.Length);
        Assert.Equal(one.Width, one.Runs[0].Width, 0.01f);
        Assert.Equal(0f, one.Runs[0].Xs[0]);
        for (var i = 1; i < one.Runs[0].Xs.Length; i++)
        {
            Assert.True(one.Runs[0].Xs[i] > one.Runs[0].Xs[i - 1]);
        }

        var sans = Run();
        var serif = Run("Cambria");
        var two = Lay(new ParagraphInput(Para(), [new TextSpan("alpha beta ", sans), new TextSpan("gamma delta", serif)], sans), 300f).Lines[0];
        Assert.Equal(2, two.Runs.Count);
        Assert.Equal("alpha beta ".Length, two.Runs[0].Glyphs.Length);
        Assert.Equal("gamma delta".Length, two.Runs[1].Glyphs.Length);

        // A justified, merged line keeps the stretched positions.
        var justified = Lay(Input(Sentence, "<w:jc w:val=\"both\"/>"), 150f).Lines[0];
        Assert.Single(justified.Runs);
        Assert.Equal(justified.Runs[0].Glyphs.Length, justified.Runs[0].Xs.Length);
        Assert.Equal(150f, justified.Width, 0.05f);
    }

    [Fact]
    public void TrailingSpacesDoNotCountTowardTheWidth()
    {
        var w = Natural("aaa");
        var layout = Lay(Input("aaa          "), w + 0.5f);
        Assert.Single(layout.Lines);
        Assert.Equal(w, layout.Lines[0].Width, 0.01f);
    }

    [Fact]
    public void AnEmptyParagraphIsOneLineOfTheMarkHeight()
    {
        var mark = Run(size: 20f);
        var layout = Lay(new ParagraphInput(Para(), [], mark), 200f);
        var face = s_fonts.Resolve("Calibri", false, false);
        Assert.Single(layout.Lines);
        Assert.Equal((face.Ascent + face.Descent + face.LineGap) * 20f, layout.Height, 0.01f);
        Assert.Empty(layout.Lines[0].Runs);
    }

    [Fact]
    public void TheWrapSourceIsAskedForEveryLine()
    {
        var wrap = new NarrowingWrap(50f, 10f);
        var layout = Lay(Input(Sentence), 200f, wrap);
        Assert.True(layout.Lines.Count >= 3);
        Assert.Equal(layout.Lines.Count, wrap.Asked.Count);
        Assert.Equal(0f, wrap.Asked[0]);
        Assert.True(wrap.Asked[1] > 0f);
        foreach (var line in layout.Lines)
        {
            Assert.Equal(50f, line.X, 0.01f);
            Assert.True(line.X + line.Width <= 190.01f);
        }

        Assert.True(layout.Lines.Count > Lay(Input(Sentence), 200f).Lines.Count);
    }

    [Fact]
    public void AHyphenatorHookExistsAndIsOptional()
    {
        var layouter = new ParagraphLayouter(s_fonts);
        Assert.Null(layouter.Hyphenator);
    }
}
