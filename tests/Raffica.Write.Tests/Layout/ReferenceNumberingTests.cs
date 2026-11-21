using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The independent count of list numbers (<see cref="ReferenceNumbering"/>, from the model alone) against what the layout draws, over the
/// whole corpus, and the counting rules on small documents with literal expectations.
/// </summary>
public sealed class ReferenceNumberingTests
{
    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static string Corpus(string id) => Path.Combine(Root, id, id + ".docx");

    private static string Level(int level, string format, string text, string extra = "") =>
        $"<w:lvl w:ilvl=\"{level}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"{format}\"/>{extra}<w:lvlText w:val=\"{text}\"/></w:lvl>";

    private static string Item(int numId, int level) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{numId}\"/></w:numPr></w:pPr><w:r><w:t>x</w:t></w:r></w:p>";

    private static List<string> Count(string numbering, params (int NumId, int Level)[] items)
    {
        var document = LayoutModel.Document(
            string.Concat(items.Select(i => Item(i.NumId, i.Level))),
            LayoutModel.StylesPart("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"/>"),
            LayoutModel.NumberingPart(numbering));
        return [.. ReferenceNumbering.Labels(document).Select(l => l.Label)];
    }

    /// <summary>The labels the layout drew, per paragraph: the first line of a body paragraph that has a label run, its label cut from the page text.</summary>
    private static List<(Paragraph Paragraph, string Label)> Drawn(DocumentLayout layout, string id, out int unaligned)
    {
        var drawn = new List<(Paragraph, string)>();
        unaligned = 0;
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            var page = layout.Page(i);
            var lines = page.Text.Length == 0 ? [] : page.Text.Split('\n');
            if (lines.Length != page.TextLines.Length)
            {
                // A line that has no model paragraph (a text box) makes the two lists differ in length; the labels of such a page cannot be read.
                Assert.False(page.TextLines.Any(l => l.Story.Kind == StoryKind.Body && l.Runs.Any(r => r.IsLabel)), $"{id}: page {i} has labels and its text does not line up");
                unaligned++;
                continue;
            }

            for (var l = 0; l < lines.Length; l++)
            {
                var line = page.TextLines[l];
                if (line.Story.Kind == StoryKind.Body && line.Runs.Any(r => r.IsLabel))
                {
                    var text = lines[l];
                    var cut = text.IndexOf(' ', StringComparison.Ordinal);
                    drawn.Add((line.Paragraph, cut < 0 ? text : text[..cut]));
                }
            }
        }

        return drawn;
    }

    // The layout draws the Symbol and Wingdings bullets of Word as plain bullets (the fonts are not shipped).
    private static string Normalized(string label, bool bullet)
    {
        if (!bullet)
        {
            return label;
        }

        return string.Concat(label.Select(c => c is >= '' and <= '' or '▪' ? '•' : c));
    }

    [Fact]
    public async Task TheReferenceMatchesTheLayoutOnEveryCorpusFile()
    {
        var compared = new Dictionary<string, int>();
        var digits = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = DocxFile.Open(Corpus(entry.Id));
            var layout = DocumentLayout.Create(file.Model, PageTestKit.Fonts, new LayoutOptions());
            await layout.LayOutRestAsync(null, TestContext.Current.CancellationToken);
            Assert.True(layout.IsComplete);

            var expected = ReferenceNumbering.Labels(file.Model);
            var drawn = Drawn(layout, entry.Id, out var unaligned);
            Assert.True(unaligned == 0 || expected.Count == 0, $"{entry.Id}: {unaligned} pages could not be read");

            // The same paragraphs in the same order, then the same labels.
            Assert.Equal(expected.Count, drawn.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.Same(expected[i].Paragraph, drawn[i].Paragraph);
                var bullet = expected[i].Label.Length > 0 && expected[i].Label.All(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '%' && c != ')' && c != '(' && c != ' ');
                Assert.Equal(Normalized(expected[i].Label, bullet), Normalized(drawn[i].Label, bullet));
            }

            compared[entry.Id] = expected.Count;
            digits += expected.Count(l => l.Label.Any(char.IsAsciiDigit));
        }

        Assert.True(compared["thesis"] > 0, "the thesis has no numbered paragraph");
        Assert.True(digits > 20, $"only {digits} counted numbers were compared");
        Assert.True(compared.Values.Sum() > 100, $"only {compared.Values.Sum()} labels were compared");
    }

    [Fact]
    public void ASharedAbstractDefinitionSharesCounters()
    {
        // Two w:num of one abstract definition count together: 1, 2 (first list), then 3 (second list).
        var numbering = "<w:abstractNum w:abstractNumId=\"0\">" + Level(0, "decimal", "%1.") + "</w:abstractNum>"
            + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"0\"/></w:num>";
        Assert.Equal(["1.", "2.", "3.", "4."], Count(numbering, (1, 0), (1, 0), (2, 0), (1, 0)));

        // Another abstract definition counts for itself.
        var separate = "<w:abstractNum w:abstractNumId=\"0\">" + Level(0, "decimal", "%1.") + "</w:abstractNum>"
            + "<w:abstractNum w:abstractNumId=\"1\">" + Level(0, "upperLetter", "%1)") + "</w:abstractNum>"
            + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>";
        Assert.Equal(["1.", "A)", "2.", "B)"], Count(separate, (1, 0), (2, 0), (1, 0), (2, 0)));
    }

    [Fact]
    public void AStartOverrideRestartsOnce()
    {
        // The second w:num restarts at 10 the first time it is used, and goes on from there.
        var numbering = "<w:abstractNum w:abstractNumId=\"0\">" + Level(0, "decimal", "%1") + "</w:abstractNum>"
            + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>"
            + "<w:num w:numId=\"2\"><w:abstractNumId w:val=\"0\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"10\"/></w:lvlOverride></w:num>";
        Assert.Equal(["1", "2", "10", "11", "12"], Count(numbering, (1, 0), (1, 0), (2, 0), (2, 0), (2, 0)));

        // Using the first list again does not apply the override again, and neither does the second list a second time.
        Assert.Equal(["1", "10", "11", "12", "13"], Count(numbering, (1, 0), (2, 0), (2, 0), (1, 0), (2, 0)));
    }

    [Fact]
    public void ADeeperLevelRestarts()
    {
        var numbering = "<w:abstractNum w:abstractNumId=\"0\">"
            + Level(0, "decimal", "%1") + Level(1, "decimal", "%1.%2") + Level(2, "lowerRoman", "%3.")
            + Level(3, "upperLetter", "%4", "<w:lvlRestart w:val=\"0\"/>") + "</w:abstractNum>"
            + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        Assert.Equal(
            ["1", "1.1", "i.", "ii.", "1.2", "i.", "2", "2.1"],
            Count(numbering, (1, 0), (1, 1), (1, 2), (1, 2), (1, 1), (1, 2), (1, 0), (1, 1)));

        // A level with w:lvlRestart 0 never restarts.
        Assert.Equal(["A", "B", "1", "C"], Count(numbering, (1, 3), (1, 3), (1, 0), (1, 3)));
    }

    [Fact]
    public void TheNumberingOfAStyleChainCounts()
    {
        var styles = LayoutModel.StylesPart(
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"/>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Item\"><w:basedOn w:val=\"Normal\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Sub\"><w:basedOn w:val=\"Item\"/><w:pPr><w:numPr><w:ilvl w:val=\"1\"/></w:numPr></w:pPr></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Plain\"><w:basedOn w:val=\"Item\"/><w:pPr><w:numPr><w:numId w:val=\"0\"/></w:numPr></w:pPr></w:style>");
        var numbering = LayoutModel.NumberingPart(
            "<w:abstractNum w:abstractNumId=\"0\">" + Level(0, "decimal", "%1") + Level(1, "lowerLetter", "%1%2") + "</w:abstractNum>"
            + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>");
        string Styled(string style) => $"<w:p><w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr></w:p>";
        var document = LayoutModel.Document(Styled("Item") + Styled("Sub") + Styled("Plain") + Styled("Normal") + Styled("Item"), styles, numbering);
        Assert.Equal(["1", "1a", "2"], ReferenceNumbering.Labels(document).Select(l => l.Label));
    }

    [Fact]
    public void BulletsKeepTheirTextAndOtherFormatsCount()
    {
        var numbering = "<w:abstractNum w:abstractNumId=\"0\">" + Level(0, "bullet", "o") + Level(1, "upperRoman", "%2", "<w:isLgl/>") + "</w:abstractNum>"
            + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        // isLgl shows the number in decimal, whatever the level's format.
        Assert.Equal(["o", "1", "2"], Count(numbering, (1, 0), (1, 1), (1, 1)));
    }
}
