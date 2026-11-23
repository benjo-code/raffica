using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The independent count of page numbers (<see cref="ReferencePageNumbers"/>, from the sections alone) against what the layout draws, over the whole corpus, and the counting rules on
/// small documents with literal expectations.
/// </summary>
public sealed class ReferencePageNumbersTests
{
    private const string Size = "<w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>";

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static string Corpus(string id) => Path.Combine(Root, id, id + ".docx");

    private static string Page(string text) => $"<w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string Break(string numbering, string type = "nextPage") =>
        $"<w:p><w:pPr><w:sectPr><w:type w:val=\"{type}\"/>{Size}{numbering}</w:sectPr></w:pPr></w:p>";

    private static string Final(string numbering = "") => $"<w:sectPr>{Size}{numbering}</w:sectPr>";

    private static List<string> Drawn(string body)
    {
        var document = LayoutModel.Document(body);
        var layout = DocumentLayout.Create(document, PageTestKit.Fonts, new LayoutOptions());
        var drawn = DrawnPageNumbers.Read(layout, document);
        var reference = ReferencePageNumbers.Numbers(document, [.. drawn.Select(d => d.Section)], [.. drawn.Select(d => d.StartsSection)], [.. drawn.Select(d => d.StartedMidPage)]);
        Assert.Equal(reference, drawn.Select(d => d.Number));
        return [.. reference];
    }

    [Fact]
    public async Task TheReferenceMatchesTheLayoutOnEveryCorpusFile()
    {
        var pages = 0;
        var thesis = new List<string>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = DocxFile.Open(Corpus(entry.Id));
            var layout = DocumentLayout.Create(file.Model, PageTestKit.Fonts, new LayoutOptions());
            await layout.LayOutRestAsync(null, TestContext.Current.CancellationToken);
            Assert.True(layout.IsComplete);

            var drawn = DrawnPageNumbers.Read(layout, file.Model);
            var expected = ReferencePageNumbers.Numbers(file.Model, [.. drawn.Select(d => d.Section)], [.. drawn.Select(d => d.StartsSection)], [.. drawn.Select(d => d.StartedMidPage)]);
            Assert.Equal(expected, drawn.Select(d => d.Number));
            pages += expected.Count;
            if (entry.Id == "thesis")
            {
                thesis = [.. expected];
            }
        }

        Assert.True(thesis.Count > 0, "the thesis was not compared");
        Assert.Equal("1", thesis[2]);
        Assert.True(pages > 100, $"only {pages} pages were compared");
    }

    [Fact]
    public void AStartRestartsTheCountOnlyOnTheFirstPageOfItsSection()
    {
        // Section 1: two pages. Section 2 starts at 7 and has three pages: 7, 8, 9 (the start does not apply to the later pages).
        var body = Page("a") + Page("b") + Break(string.Empty)
            + Page("c") + Page("d") + Page("e") + Final("<w:pgNumType w:start=\"7\"/>");
        Assert.Equal(["1", "2", "7", "8", "9"], Drawn(body));
    }

    [Fact]
    public void ARomanFormatCountsUp()
    {
        var body = Page("a") + Page("b") + Page("c") + Page("d") + Final("<w:pgNumType w:fmt=\"lowerRoman\"/>");
        Assert.Equal(["i", "ii", "iii", "iv"], Drawn(body));

        var upper = Page("a") + Page("b") + Page("c") + Page("d") + Page("e") + Final("<w:pgNumType w:fmt=\"upperRoman\" w:start=\"7\"/>");
        Assert.Equal(["VII", "VIII", "IX", "X", "XI"], Drawn(upper));

        var letters = Page("a") + Page("b") + Final("<w:pgNumType w:fmt=\"upperLetter\"/>");
        Assert.Equal(["A", "B"], Drawn(letters));
    }

    [Fact]
    public void ANextPageSectionAfterAStartContinuesTheCount()
    {
        // The first section starts at 5; the second has no start and goes on: 5, 6, then 7, 8.
        var body = Page("a") + Page("b") + Break("<w:pgNumType w:start=\"5\"/>")
            + Page("c") + Page("d") + Final();
        Assert.Equal(["5", "6", "7", "8"], Drawn(body));
    }

    [Fact]
    public void LettersRepeatEveryTwentySixPages()
    {
        // Word counts a, b, ... z, aa, bb, ... zz, aaa: 26 is z, 27 aa, 28 bb, 52 zz, 53 aaa.
        var lower = Page("a") + Page("b") + Page("c") + Final("<w:pgNumType w:fmt=\"lowerLetter\" w:start=\"26\"/>");
        Assert.Equal(["z", "aa", "bb"], Drawn(lower));

        var upper = Page("a") + Page("b") + Final("<w:pgNumType w:fmt=\"upperLetter\" w:start=\"52\"/>");
        Assert.Equal(["ZZ", "AAA"], Drawn(upper));

        var plain = Page("a") + Page("b") + Page("c") + Final("<w:pgNumType w:fmt=\"lowerLetter\"/>");
        Assert.Equal(["a", "b", "c"], Drawn(plain));
    }

    [Fact]
    public void AStartOfZeroCountsFromZero()
    {
        Assert.Equal(["0", "1", "2"], Drawn(Page("a") + Page("b") + Page("c") + Final("<w:pgNumType w:start=\"0\"/>")));
    }

    [Fact]
    public void ARomanFormatOnALaterSectionContinuesTheCountInRoman()
    {
        // The first section counts 1, 2 in the default format; the second sets only a format and goes on with iii, iv.
        var body = Page("a") + Page("b") + Break(string.Empty) + Page("c") + Page("d") + Final("<w:pgNumType w:fmt=\"lowerRoman\"/>");
        Assert.Equal(["1", "2", "iii", "iv"], Drawn(body));

        // A start with roman on the later section restarts there: 1, 2, then iv, v.
        var restarted = Page("a") + Page("b") + Break(string.Empty) + Page("c") + Page("d") + Final("<w:pgNumType w:fmt=\"lowerRoman\" w:start=\"4\"/>");
        Assert.Equal(["1", "2", "iv", "v"], Drawn(restarted));
    }

    [Fact]
    public void ASectionWithoutNumberingCountsOn()
    {
        Assert.Equal(["1", "2", "3"], Drawn(Page("a") + Page("b") + Page("c") + Final()));
        Assert.Equal(["1", "2", "3", "4"], Drawn(Page("a") + Page("b") + Break(string.Empty) + Page("c") + Page("d") + Final()));
    }

    // A continuous section that sets a start of 5 and whose first block goes to the top of page 2 begins that page with the start, on the same paper as well as on other paper. Word-checked: the page-top file
    // (continuous-start-page-top) and the other-paper file (continuous-start-other-paper, Docs/write-word-check.md, 2026-11-23). The form here (a page break before the first block, same paper) is extended by
    // reasoning from the page-top file; it was not opened in Word.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AContinuousSectionWhoseFirstBlockStartsAPageRestartsTheCount(bool otherPaper)
    {
        var margins = "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>";
        var first = "<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" + margins + "</w:sectPr></w:pPr><w:r><w:t>one</w:t></w:r></w:p>";
        var size = otherPaper ? "<w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/>" : "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>";
        var last = Page("two") + Page("three") + "<w:sectPr><w:type w:val=\"continuous\"/>" + size + margins + "<w:pgNumType w:start=\"5\"/></w:sectPr>";

        Assert.Equal(["1", "5", "6"], Drawn(first + last));
    }

    // Checked against Word on 2026-11-23 (row continuous-start-same-paper: Word 1 2 6 7): a continuous section on the same paper that sets a start of 5 and begins in the middle of page 2 leaves that
    // page's number alone, and the next page counts from 5 + 1.
    [Fact]
    public void AContinuousSectionThatBeginsInTheMiddleOfAPageLeavesThatPageAndCountsOnFromTheStart()
    {
        var lines = string.Concat(Enumerable.Range(1, 160).Select(i => $"<w:p><w:r><w:t>line {i}</w:t></w:r></w:p>"));
        var first = Page("one") + "<w:p><w:pPr><w:sectPr>" + Size + "</w:sectPr></w:pPr><w:r><w:t>one b</w:t></w:r></w:p>";
        var body = first + Page("two") + "<w:p><w:pPr><w:sectPr>" + Size + "</w:sectPr></w:pPr><w:r><w:t>two b</w:t></w:r></w:p>"
            + lines + "<w:sectPr><w:type w:val=\"continuous\"/>" + Size + "<w:pgNumType w:start=\"5\"/></w:sectPr>";

        var numbers = Drawn(body);

        Assert.Equal("1", numbers[0]);
        Assert.Equal("2", numbers[1]);
        Assert.Equal("6", numbers[2]);
        Assert.Equal("7", numbers[3]);
    }
}
