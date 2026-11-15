using Raffica.Write.Layout;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class CaretStopTests
{
    private static readonly FontCatalog s_fonts = FontCatalog.CreateShipped(new NoSystemFonts());

    private sealed class NoSystemFonts : ISystemFonts
    {
        public FontFace? Match(string family, bool bold, bool italic) => null;
    }

    private static ResolvedRun Run(string font = "Calibri") => new() { FontName = font, SizePoints = 11f };

    private static ParagraphLayout Lay(string text, string pPr = "", float width = 100000f, string font = "Calibri")
    {
        var body = $"<w:p><w:pPr>{pPr}</w:pPr><w:r><w:t>x</w:t></w:r></w:p>";
        var document = LayoutModel.Document(body);
        var resolver = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        var paragraph = resolver.ForParagraph(LayoutModel.Para(document, 0));
        var run = Run(font);
        return new ParagraphLayouter(s_fonts).Layout(new ParagraphInput(paragraph, [new TextSpan(text, run)], run), width, null);
    }

    private static GlyphRun TextRun(ParagraphLayout layout, int line = 0) =>
        layout.Lines[line].Runs.First(r => !r.IsLeader && !r.IsLabel && !r.IsHyphen);

    [Fact]
    public void EdgesAreTheRunEdges()
    {
        var run = TextRun(Lay("Hello world"));

        Assert.Equal(run.X, run.CaretX(run.TextStart), 0.001f);
        Assert.Equal(run.X + run.Width, run.CaretX(run.TextEnd), 0.001f);
    }

    [Fact]
    public void LigatureDividesEqually()
    {
        var run = TextRun(Lay("office"));

        // Carlito ligates "ffi": one glyph for three characters, so its two inner stops sit at thirds.
        Assert.True(run.Glyphs.Length < 6, "the face did not ligate ffi");
        var start = run.CaretX(1);
        var end = run.CaretX(4);
        var third = (end - start) / 3f;
        Assert.True(third > 0f);
        Assert.Equal(start + third, run.CaretX(2), 0.01f);
        Assert.Equal(start + (2 * third), run.CaretX(3), 0.01f);
        Assert.Contains(2, run.CaretStops);
        Assert.Contains(3, run.CaretStops);
    }

    [Theory]
    [InlineData("e\u0301", 1)]
    [InlineData("\U0001F44D\U0001F3FD", 1)]
    [InlineData("\U0001F44D\U0001F3FD", 0)]
    public void CombiningMarkAndEmojiAreOneStop(string text, int between)
    {
        var run = TextRun(Lay("a" + text + "b"));

        // "a" is offset 0; the cluster starts at 1, so the offset inside it is 1 + between (for between 0 it is the start itself).
        if (between > 0)
        {
            Assert.DoesNotContain(1 + between, run.CaretStops);
        }
        else
        {
            Assert.Contains(1, run.CaretStops);
        }

        Assert.Contains(1 + text.Length, run.CaretStops);
    }

    [Fact]
    public void StopsAreMonotonicOnAJustifiedLine()
    {
        var text = string.Join(' ', Enumerable.Repeat("office coffee e\u0301 fish affix", 12));
        var layout = Lay(text, "<w:jc w:val=\"both\"/>", 200f);

        Assert.True(layout.Lines.Count > 2);
        foreach (var line in layout.Lines)
        {
            foreach (var run in line.Runs.Where(r => r.CaretStops.Count > 0))
            {
                var previous = float.MinValue;
                foreach (var stop in run.CaretStops)
                {
                    var x = run.CaretX(stop);
                    Assert.True(x >= previous - 0.001f, $"{x} < {previous}");
                    previous = x;
                }
            }
        }
    }

    [Fact]
    public void HyphenRunsHaveNoStops()
    {
        var layout = Lay("internationalization internationalization internationalization", "<w:autoHyphenation/>", 80f);
        foreach (var run in layout.Lines.SelectMany(l => l.Runs).Where(r => r.IsHyphen))
        {
            Assert.Empty(run.CaretStops);
        }
    }
}
