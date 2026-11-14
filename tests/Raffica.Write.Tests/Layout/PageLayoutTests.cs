using System.Diagnostics;
using Raffica.Write.Layout;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

public sealed class PageLayoutTests
{
    private const string Lorem = "lorem ipsum dolor sit amet ";
    private const string Continuous = "<w:type w:val=\"continuous\"/>";
    private const string TwoColumns = "<w:cols w:num=\"2\" w:space=\"720\"/>";

    // A paragraph of n lines: the lines are separated by manual line breaks.
    private static string Lines(string prefix, int count, string pPr = "")
    {
        var runs = string.Join("<w:r><w:br/></w:r>", Enumerable.Range(1, count).Select(i => $"<w:r><w:t>{prefix}{i}</w:t></w:r>"));
        return $"<w:p><w:pPr>{pPr}</w:pPr>{runs}</w:p>";
    }

    private static string[] TextLines(LaidOutPage page) => PageTestKit.Lines(page);

    [Fact]
    public void EmptyDocumentHasOneBlankPage()
    {
        foreach (var body in new[] { string.Empty, Sect(), "<w:p/>" + Sect() })
        {
            var layout = Complete(body);
            Assert.Equal(1, layout.LaidOutPages);
            Assert.True(layout.IsComplete);
            var page = layout.Page(0);
            Assert.Equal("1", page.Number);
            Assert.False(page.IsStrip);
            Assert.True(page.Width > 0 && page.Height > 0);
            Assert.Empty(Runs(page));
        }

        var blank = Complete(string.Empty).Page(0);
        Assert.Empty(blank.Items);
        Assert.Equal(string.Empty, blank.Text);
        Assert.Equal(-1, Complete(string.Empty).BlockAt(0, 10f));
    }

    [Fact]
    public void AbsurdPageGeometryIsRepaired()
    {
        // Zero and negative sizes fall back to A4 and say so.
        foreach (var size in new[] { "<w:pgSz w:w=\"0\" w:h=\"0\"/>", "<w:pgSz w:w=\"-100\" w:h=\"-50\"/>" })
        {
            var layout = Complete(P("text") + "<w:sectPr>" + size + Margins(72, 72, 72, 72) + "</w:sectPr>");
            Assert.Equal(595.3f, layout.Page(0).Width, 0.1f);
            Assert.Equal(841.9f, layout.Page(0).Height, 0.1f);
            Assert.Contains(layout.Notes, n => n.Contains("page size", StringComparison.Ordinal));
            Assert.NotEmpty(Runs(layout.Page(0)));
        }

        // Margins larger than the page keep a text area of at least 36 pt.
        var sect = "<w:sectPr><w:pgSz w:w=\"2000\" w:h=\"2000\"/><w:pgMar w:top=\"1500\" w:right=\"1500\" w:bottom=\"1500\" w:left=\"1500\" w:header=\"0\" w:footer=\"0\" w:gutter=\"0\"/></w:sectPr>";
        var absurd = Complete(P(Repeat("word ", 40)) + sect);
        Assert.Contains(absurd.Notes, n => n.Contains("margins", StringComparison.Ordinal));
        Assert.True(absurd.LaidOutPages >= 1);
        foreach (var page in Pages(absurd))
        {
            Assert.Equal(100f, page.Width, 0.01f);
            foreach (var run in Runs(page))
            {
                Assert.True(run.X >= 0f);
                Assert.True(page.Width - run.X >= PageGeometry.MinimumText - 0.01f);
            }
        }

        Assert.Equal(PageGeometry.MinimumText, 100f - (2 * Runs(absurd.Page(0))[0].X), 0.1f);

        // A normal document says nothing.
        Assert.Empty(Complete(P("fine") + Sect()).Notes);
    }

    [Fact]
    public void AContinuousSectionNeverStartsInTheBottomMargin()
    {
        // Section 1 fills the page to the last line (exactly, and with less than a line left); the continuous section after it
        // starts on the next page, and nothing is drawn below the body.
        var exact = Sect(string.Empty, 612f, 72f + (10 * LineHeight()) + 0.01f, 36f);
        var roomy = TallFor(10);
        foreach (var (first, next) in new[] { (exact, exact.Replace("<w:sectPr>", "<w:sectPr>" + Continuous, StringComparison.Ordinal)), (roomy, TallFor(10, Continuous)) })
        {
            var body = Numbered("one {0}", 9) + PEnd("one 10", first) + P("next") + next;
            var layout = Complete(body);
            Assert.Equal(2, layout.LaidOutPages);
            Assert.Equal(10, TextLines(layout.Page(0)).Length);
            Assert.Equal(["next"], TextLines(layout.Page(1)));
            foreach (var page in Pages(layout))
            {
                Assert.All(Runs(page), r => Assert.True(r.Baseline <= page.Height - 36f, "nothing is drawn in the bottom margin"));
            }
        }

        // A multi-line paragraph that does not fit the room left on a mid-page band moves whole to the next page.
        var tall = TallFor(10);
        var moved = Complete(Numbered("one {0}", 8) + PEnd("one 9", tall) + Lines("L", 3) + TallFor(10, Continuous));
        Assert.Equal(9, TextLines(moved.Page(0)).Length);
        Assert.Equal(["L1", "L2", "L3"], TextLines(moved.Page(1)));
    }

    [Fact]
    public void NonFiniteAndNegativeGeometryIsRepaired()
    {
        const string Cols = "<w:cols w:num=\"2\" w:space=\"{0}\"/>";
        var bad = new[] { "NaN", "Infinity", "-Infinity", "1e300", "1e30", "-1440", "-1e300" };
        var cases = new List<string>();
        foreach (var value in bad)
        {
            foreach (var attribute in new[] { "left", "right", "top", "bottom", "gutter" })
            {
                var margins = new Dictionary<string, string> { ["top"] = "1440", ["right"] = "1440", ["bottom"] = "1440", ["left"] = "1440", ["gutter"] = "0" };
                margins[attribute] = value;
                cases.Add("<w:sectPr>" + Pgsz(612f, 792f) + $"<w:pgMar w:top=\"{margins["top"]}\" w:right=\"{margins["right"]}\" w:bottom=\"{margins["bottom"]}\" w:left=\"{margins["left"]}\" w:header=\"720\" w:footer=\"720\" w:gutter=\"{margins["gutter"]}\"/></w:sectPr>");
            }

            cases.Add("<w:sectPr>" + string.Format(System.Globalization.CultureInfo.InvariantCulture, Cols, value) + Pgsz(612f, 792f) + Margins(72, 72, 72, 72) + "</w:sectPr>");
            cases.Add("<w:sectPr><w:cols w:num=\"2\" w:equalWidth=\"0\"><w:col w:w=\"" + value + "\" w:space=\"720\"/><w:col w:w=\"3000\"/></w:cols>" + Pgsz(612f, 792f) + Margins(72, 72, 72, 72) + "</w:sectPr>");
            cases.Add("<w:sectPr><w:pgSz w:w=\"" + value + "\" w:h=\"" + value + "\"/>" + Margins(72, 72, 72, 72) + "</w:sectPr>");
        }

        foreach (var sect in cases)
        {
            var layout = Complete(P(Repeat("word ", 300)) + sect);
            Assert.NotEmpty(layout.Notes);
            Assert.InRange(layout.LaidOutPages, 1, 400);
            foreach (var page in Pages(layout))
            {
                Assert.True(float.IsFinite(page.Width) && float.IsFinite(page.Height) && page.Width > 0 && page.Height > 0, sect);
                Assert.NotEmpty(Runs(page));
                foreach (var run in Runs(page))
                {
                    Assert.True(float.IsFinite(run.X) && float.IsFinite(run.Baseline) && run.X >= 0f, sect);
                    Assert.True(page.Width - run.X >= PageGeometry.MinimumText - 0.01f, sect);
                    Assert.All(run.Xs.ToArray(), x => Assert.True(float.IsFinite(x), sect));
                }
            }
        }
    }

    [Fact]
    public void PageSizeMarginsAndOrientation()
    {
        var a4 = Complete(P("x") + "<w:sectPr>" + Pgsz(595.3f, 841.9f) + Margins(56.7f, 70.9f, 56.7f, 70.9f) + "</w:sectPr>").Page(0);
        Assert.Equal(595.3f, a4.Width, 0.05f);
        Assert.Equal(841.9f, a4.Height, 0.05f);
        Assert.Equal(56.7f, Runs(a4)[0].X, 0.05f);

        var letter = Complete(P("x") + Sect()).Page(0);
        Assert.Equal(612f, letter.Width, 0.01f);
        Assert.Equal(792f, letter.Height, 0.01f);
        Assert.Equal(72f, Runs(letter)[0].X, 0.01f);
        Assert.True(Runs(letter)[0].Baseline > 72f);

        // Landscape is a swapped w:pgSz; the page is as the file says.
        var landscape = Complete(P("x") + "<w:sectPr>" + Pgsz(792f, 612f, " w:orient=\"landscape\"") + Margins(72, 72, 72, 72) + "</w:sectPr>").Page(0);
        Assert.Equal(792f, landscape.Width, 0.01f);
        Assert.Equal(612f, landscape.Height, 0.01f);

        // A gutter adds to the left margin.
        var gutter = Complete(P("x") + "<w:sectPr>" + Pgsz(612f, 792f) + "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"360\"/></w:sectPr>").Page(0);
        Assert.Equal(90f, Runs(gutter)[0].X, 0.01f);
    }

    [Fact]
    public void SectionTypes()
    {
        var one = PEnd("one", Sect());

        var nextPage = Complete(one + P("two") + Sect());
        Assert.Equal(2, nextPage.LaidOutPages);
        Assert.Equal(["one"], TextLines(nextPage.Page(0)));
        Assert.Equal(["two"], TextLines(nextPage.Page(1)));

        var continuous = Complete(one + P("two") + Sect(Continuous));
        Assert.Equal(1, continuous.LaidOutPages);
        var baselines = Baselines(continuous.Page(0));
        Assert.Equal(2, baselines.Count);
        Assert.Equal(LineHeight(), baselines[1] - baselines[0], 0.05f);

        // Odd page: section 2 would start on page 2, so a blank page 2 is inserted and it starts on page 3.
        var odd = Complete(one + P("two") + Sect("<w:type w:val=\"oddPage\"/>"));
        Assert.Equal(3, odd.LaidOutPages);
        Assert.Empty(odd.Page(1).Items);
        Assert.Equal("2", odd.Page(1).Number);
        Assert.Equal(["two"], TextLines(odd.Page(2)));
        Assert.Equal("3", odd.Page(2).Number);

        // Even page after one page needs no blank page; after two pages it does.
        var even = Complete(one + P("two") + Sect("<w:type w:val=\"evenPage\"/>"));
        Assert.Equal(2, even.LaidOutPages);
        Assert.Equal(["two"], TextLines(even.Page(1)));

        var twoPages = PEnd("b", Sect(), "<w:pageBreakBefore/>");
        var evenLater = Complete(P("a") + twoPages + P("c") + Sect("<w:type w:val=\"evenPage\"/>"));
        Assert.Equal(4, evenLater.LaidOutPages);
        Assert.Empty(evenLater.Page(2).Items);
        Assert.Equal(["c"], TextLines(evenLater.Page(3)));
    }

    [Fact]
    public void ColumnsFlowAndBalance()
    {
        // Nine lines fit a column: 20 items fill column 1 (9), column 2 (9) and the first column of page 2 (2).
        var sect = TallFor(9, TwoColumns);
        var layout = Complete(Numbered("Item {0:00}", 20) + sect);
        Assert.Equal(2, layout.LaidOutPages);
        var first = layout.Page(0);
        Assert.Equal(18, TextLines(first).Length);
        Assert.Equal(2, TextLines(layout.Page(1)).Length);
        var runs = Runs(first);
        var colWidth = (540f - 36f) / 2f;
        Assert.Equal(36f, runs[0].X, 0.01f);
        Assert.Equal(36f, runs[8].X, 0.01f);
        Assert.Equal(36f + colWidth + 36f, runs[9].X, 0.01f);
        Assert.Equal(runs[0].Baseline, runs[9].Baseline, 0.01f);

        // A continuous section break balances the columns of the section before it.
        var body = Numbered("A{0}", 5) + PEnd("A6", Sect(TwoColumns)) + P("after") + Sect(Continuous);
        var balanced = Complete(body);
        Assert.Equal(1, balanced.LaidOutPages);
        var page = balanced.Page(0);
        var all = Runs(page);
        var left = all.Where(r => r.X < 100f).Select(r => r.Baseline).Distinct().OrderBy(b => b).ToList();
        var right = all.Where(r => r.X > 300f).Select(r => r.Baseline).Distinct().OrderBy(b => b).ToList();
        Assert.Equal(4, left.Count); // three paragraphs and the paragraph after the break
        Assert.Equal(3, right.Count);
        Assert.Equal(left[0], right[0], 0.01f);
        Assert.Equal(LineHeight() * 3f, left[3] - left[0], 0.05f);
    }

    [Fact]
    public void KeepNextKeepLinesPageBreakBefore()
    {
        var sect = TallFor(10);
        var filler = Numbered("filler {0}", 8);
        var body = P(Repeat(Lorem, 30));

        // Without keepNext the heading stays at the bottom of page 1; with it the heading moves with its text.
        var loose = Complete(filler + P("Heading") + body + sect);
        Assert.Equal("Heading", TextLines(loose.Page(0))[^1]);

        var kept = Complete(filler + P("Heading", "<w:keepNext/>") + body + sect);
        Assert.Equal("filler 8", TextLines(kept.Page(0))[^1]);
        Assert.Equal("Heading", TextLines(kept.Page(1))[0]);

        // KeepLines keeps a paragraph whole; without it the paragraph is cut between pages.
        var seven = Numbered("filler {0}", 7);
        var split = Complete(seven + Lines("L", 8) + sect);
        Assert.Equal(2, split.LaidOutPages);
        Assert.Equal(10, TextLines(split.Page(0)).Length);
        var whole = Complete(seven + Lines("L", 8, "<w:keepLines/>") + sect);
        Assert.Equal(7, TextLines(whole.Page(0)).Length);
        Assert.Equal(8, TextLines(whole.Page(1)).Length);

        // pageBreakBefore starts a page; at the very top of the document it adds no blank page.
        var broken = Complete(P("a") + P("b", "<w:pageBreakBefore/>") + sect);
        Assert.Equal(2, broken.LaidOutPages);
        Assert.Equal(["b"], TextLines(broken.Page(1)));
        var first = Complete(P("a", "<w:pageBreakBefore/>") + P("b") + sect);
        Assert.Equal(1, first.LaidOutPages);
    }

    [Fact]
    public void WidowAndOrphanControl()
    {
        var sect = TallFor(10);

        // Seven lines, then four: three fit. Widow control leaves two for the next page, switching it off leaves one.
        var seven = Numbered("filler {0}", 7);
        var controlled = Complete(seven + Lines("L", 4) + sect);
        Assert.Equal(9, TextLines(controlled.Page(0)).Length);
        Assert.Equal(["L3", "L4"], TextLines(controlled.Page(1)));
        var off = Complete(seven + Lines("L", 4, "<w:widowControl w:val=\"0\"/>") + sect);
        Assert.Equal(10, TextLines(off.Page(0)).Length);
        Assert.Equal(["L4"], TextLines(off.Page(1)));

        // Nine lines, then four: only one fits, which is an orphan: the paragraph moves whole.
        var nine = Numbered("filler {0}", 9);
        var orphan = Complete(nine + Lines("L", 4) + sect);
        Assert.Equal(9, TextLines(orphan.Page(0)).Length);
        Assert.Equal(4, TextLines(orphan.Page(1)).Length);
        var allowed = Complete(nine + Lines("L", 4, "<w:widowControl w:val=\"0\"/>") + sect);
        Assert.Equal(10, TextLines(allowed.Page(0)).Length);

        // The style can turn it off too.
        var styles = LayoutModel.StylesPart("<w:style w:type=\"paragraph\" w:styleId=\"Loose\"><w:name w:val=\"Loose\"/><w:pPr><w:widowControl w:val=\"0\"/></w:pPr></w:style>");
        var byStyle = Complete(seven + Lines("L", 4, "<w:pStyle w:val=\"Loose\"/>") + sect, styles);
        Assert.Equal(10, TextLines(byStyle.Page(0)).Length);
    }

    [Fact]
    public void SpacingCollapsesWithContextualSpacing()
    {
        // Space after plus space before add up between two paragraphs (as Word does).
        const string Spacing = "<w:spacing w:before=\"120\" w:after=\"240\"/>";
        var plain = Complete(P("A", Spacing) + P("B", Spacing) + P("C", Spacing) + Sect());
        var b = Baselines(plain.Page(0));
        Assert.Equal(LineHeight() + 18f, b[1] - b[0], 0.05f);
        Assert.Equal(LineHeight() + 18f, b[2] - b[1], 0.05f);
        Assert.True(b[0] > 72f + 6f, "the first paragraph keeps its space before at the top of the first page");

        // Contextual spacing drops both gaps between paragraphs of the same style, and only those.
        string Style(string id) => $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{id}\"/><w:pPr>{Spacing}<w:contextualSpacing/></w:pPr></w:style>";
        var styles = LayoutModel.StylesPart(Style("Ctx") + Style("Other"));
        string In(string id, string text) => P(text, $"<w:pStyle w:val=\"{id}\"/>");
        var same = Complete(In("Ctx", "A") + In("Ctx", "B") + In("Other", "C") + Sect(), styles);
        var s = Baselines(same.Page(0));
        Assert.Equal(LineHeight(), s[1] - s[0], 0.05f);
        Assert.Equal(LineHeight() + 18f, s[2] - s[1], 0.05f);
    }

    [Fact]
    public void NumberedHeadingsAreLabelled()
    {
        var styles = LayoutModel.StylesPart(
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:pPr><w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>");
        var numbering = LayoutModel.NumberingPart(
            "<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>"
            + "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1\"/><w:pPr><w:ind w:left=\"432\" w:hanging=\"432\"/></w:pPr></w:lvl>"
            + "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.%2\"/><w:pPr><w:ind w:left=\"576\" w:hanging=\"576\"/></w:pPr></w:lvl>"
            + "</w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>");
        string H(int level, string text) => P(text, $"<w:pStyle w:val=\"Heading{level}\"/>");
        var layout = Complete(H(1, "Intro") + H(2, "Aim") + H(2, "Scope") + P("Body") + H(1, "End") + H(2, "Last") + Sect(), styles, numbering);
        Assert.Equal(["1 Intro", "1.1 Aim", "1.2 Scope", "Body", "2 End", "2.1 Last"], TextLines(layout.Page(0)));

        // The label hangs into the margin: the heading text starts at the left indent, the label at the first-line position.
        var runs = Runs(layout.Page(0));
        Assert.Equal(72f, runs[0].X, 0.01f);
        Assert.Equal(72f + 21.6f, runs[1].X, 0.05f);
    }

    [Fact]
    public void FirstPageIsFinalForAPrefix()
    {
        var long_ = Create(Numbered("Paragraph {0}", 200) + Sect());
        Assert.False(long_.FirstPageIsFinal);
        long_.LayOutFirstPage();
        Assert.Equal(1, long_.LaidOutPages);
        Assert.True(long_.FirstPageIsFinal);

        // A prefix that ends on page 1 may be cut short there, so page 1 is not final.
        var short_ = Create(Numbered("Paragraph {0}", 3) + Sect());
        short_.LayOutFirstPage();
        Assert.False(short_.FirstPageIsFinal);

        // Page 1 that had to look at the last block (a heading kept with it) is not final either.
        var chain = Create(Numbered("filler {0}", 5) + P("Heading", "<w:keepNext/>") + P("tail") + TallFor(6));
        chain.LayOutFirstPage();
        Assert.False(chain.FirstPageIsFinal);
    }

    [Fact]
    public void LocateAndBlockAtRoundTrip()
    {
        var layout = Create(Numbered("Paragraph {0}", 150) + Sect());
        Assert.Null(layout.Locate(0));
        Finish(layout);
        Assert.True(layout.LaidOutPages >= 3);

        var lastPage = -1;
        for (var i = 0; i < 150; i++)
        {
            var at = layout.Locate(i);
            Assert.NotNull(at);
            var (page, y) = at.Value;
            Assert.True(page >= lastPage);
            lastPage = page;
            Assert.Equal(i, layout.BlockAt(page, y + 1f));
            Assert.Equal(i, layout.BlockAt(page, y + (LineHeight() / 2f)));
        }

        Assert.Equal(72f, layout.Locate(0)!.Value.Y, 0.01f);
        Assert.Null(layout.Locate(150));
        Assert.Null(layout.Locate(-1));
        Assert.Equal(0, layout.Locate(0)!.Value.Page);

        // Below the last block of a page the last block is answered; above the first, the first.
        var firstOnPage1 = layout.BlockAt(0, 1f);
        Assert.Equal(0, firstOnPage1);
        Assert.True(layout.BlockAt(0, 5000f) > 0);
    }

    [Fact]
    public void FlowModeHasNoPageChrome()
    {
        var header = "<w:headerReference w:type=\"default\" r:id=\"rId9\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/>";
        var body = Numbered("Paragraph {0} " + Repeat("lorem ", 40), 80) + "<w:sectPr>" + header + Pgsz(612f, 792f) + Margins(72, 72, 72, 72) + "</w:sectPr>";
        var flow = Complete(body, options: new LayoutOptions(LayoutMode.Flow, 400f));
        Assert.True(flow.LaidOutPages >= 2);
        var pages = Pages(flow);
        foreach (var page in pages)
        {
            Assert.True(page.IsStrip);
            Assert.Equal(400f, page.Width, 0.01f);
            Assert.InRange(page.Height, 1f, 2000f);
            foreach (var run in Runs(page))
            {
                Assert.InRange(run.X, 0f, 400f);
                Assert.True(run.Baseline <= page.Height + 0.01f);
            }
        }

        // No margins: the first line starts at 0, and nothing is drawn above the first line.
        Assert.Equal(0f, Runs(pages[0])[0].X, 0.01f);
        Assert.True(Runs(pages[0])[0].Baseline < 20f);
        Assert.Empty(flow.Notes);

        // A block is cut between lines, never through one: every line of every paragraph is on exactly one strip, and strips are full.
        var allLines = pages.SelectMany(TextLines).ToList();
        Assert.Equal(allLines.Count, allLines.Count(l => l.Length > 0));
        Assert.StartsWith("Paragraph 1 ", allLines[0], StringComparison.Ordinal);
        for (var i = 0; i < pages.Count - 1; i++)
        {
            Assert.True(pages[i].Height > 2000f - (3 * LineHeight()), "A strip is cut only when it is full.");
        }

        var words = string.Join(" ", allLines).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(80 * 42, words.Length);
        Assert.Equal(Enumerable.Range(1, 80).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), words.Where((w, k) => k % 42 == 1));
    }

    [Fact]
    public void FlowAndPagesShareLineBreaks()
    {
        var text = P(Repeat("The quick brown fox jumps over the lazy dog ", 60));
        var paged = Complete(text + "<w:sectPr>" + Pgsz(664f, 792f) + Margins(72, 72, 72, 72) + "</w:sectPr>");
        var flowed = Complete(text + Sect(), options: new LayoutOptions(LayoutMode.Flow, 520f));
        Assert.Equal(1, paged.LaidOutPages);
        Assert.Equal(1, flowed.LaidOutPages);
        var lines = TextLines(paged.Page(0));
        Assert.True(lines.Length > 10);
        Assert.Equal(lines, TextLines(flowed.Page(0)));
    }

    [Fact]
    public void OversizedContentTerminates()
    {
        // A text area of 36 x 36 pt, a word far wider than it and a paragraph of 200 lines: the page count stays finite and nothing is dropped.
        var sect = "<w:sectPr>" + Pgsz(100f, 100f) + Margins(32, 32, 32, 32) + "</w:sectPr>";
        var timer = Stopwatch.StartNew();
        var layout = Complete(P(Repeat("x", 5000)) + Lines("L", 200) + sect);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10));
        Assert.InRange(layout.LaidOutPages, 2, 1000);
        var all = Pages(layout).SelectMany(TextLines).ToList();
        Assert.Contains(all, l => l.StartsWith("xxxx", StringComparison.Ordinal));
        Assert.Contains("L200", all);
        Assert.Contains("L1", all);

        // A picture taller than the page is placed, not looped over.
        const string Drawing = "<w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><wp:extent cx=\"12700000\" cy=\"25400000\"/><a:graphic><a:graphicData><a:blip r:embed=\"rId5\"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>";
        var picture = Complete("<w:p>" + Drawing + "</w:p>" + P("after") + Sect());
        Assert.InRange(picture.LaidOutPages, 1, 5);
        Assert.Contains(Pages(picture).SelectMany(p => p.Items), i => i is ImageItem);
        Assert.Contains("after", Pages(picture).SelectMany(TextLines));
    }

    [Fact]
    public void SettingsReachTheParagraphs()
    {
        // The default tab stop comes from the settings: "a", a tab, "b" puts b on the next multiple of the interval.
        const string Tabbed = "<w:p><w:r><w:t>a</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>b</w:t></w:r></w:p>";
        var wide = CreateWithSettings(Tabbed + Sect(), "<w:defaultTabStop w:val=\"2000\"/>");
        wide.LayOutFirstPage();
        Assert.Equal(72f + 100f, Runs(wide.Page(0))[^1].X, 0.05f);
        var normal = Complete(Tabbed + Sect());
        Assert.Equal(72f + 36f, Runs(normal.Page(0))[^1].X, 0.05f);

        // Automatic hyphenation comes from the settings: a long word breaks in a narrow column only when it is on.
        var word = "<w:p><w:r><w:rPr><w:lang w:val=\"en-US\"/></w:rPr><w:t>internationalization internationalization</w:t></w:r></w:p>";
        var narrow = new LayoutOptions(LayoutMode.Flow, 80f);
        var off = Finish(CreateWithSettings(word + Sect(), string.Empty, narrow));
        var on = Finish(CreateWithSettings(word + Sect(), "<w:autoHyphenation/>", narrow));
        Assert.Equal(2, TextLines(off.Page(0)).Length);
        Assert.True(TextLines(on.Page(0)).Length > 2);
        Assert.Equal("internationalizationinternationalization", string.Concat(TextLines(on.Page(0))).Replace(" ", string.Empty, StringComparison.Ordinal));
    }
}
