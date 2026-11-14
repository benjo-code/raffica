using System.Text;
using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class DocxPrefixTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Thesis => Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx");

    private static string Paragraphs(int count, string prefix = "w") =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"<{prefix}:p><{prefix}:r><{prefix}:t>Paragraph {i}</{prefix}:t></{prefix}:r></{prefix}:p>"));

    private static byte[] MainPart(string body, string prefix = "w", bool bom = false, string between = "")
    {
        var xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>{between}<{prefix}:document xmlns:{prefix}=\"{W}\">{between}<{prefix}:body>{body}</{prefix}:body>{between}</{prefix}:document>{between}";
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        return bom ? [.. Encoding.UTF8.GetPreamble(), .. bytes] : bytes;
    }

    private static DocxFile PrefixOf(byte[] main, int maxBlocks, TempFolder folder, out DocxFile full)
    {
        var path = folder.File("t-" + Guid.NewGuid().ToString("N") + ".docx");
        DocxBuilder.WithMainBytes(path, main);
        full = DocxFile.Open(path);
        return DocxFile.OpenPrefix(path, maxBlocks);
    }

    /// <summary>The final section as the codec writes it, or null.</summary>
    private static string? FinalSection(WordDocument model) => Xml(model.Body.Root, model.Body.FinalSection);

    private static string? Xml(OoxmlRoot root, SectionProperties? section)
    {
        if (section is null)
        {
            return null;
        }

        var story = new Story(root) { FinalSection = section };
        using var stream = new MemoryStream();
        DocumentXml.Write(story, stream);
        stream.Position = 0;
        return XDocument.Load(stream).Descendants(XName.Get("body", W)).Single().Elements().Single().ToString();
    }

    private static string Body(WordDocument model)
    {
        using var stream = new MemoryStream();
        DocumentXml.Write(model.Body, stream);
        stream.Position = 0;
        return XDocument.Load(stream).Descendants(XName.Get("body", W)).Single().ToString();
    }

    [Fact]
    public void PrefixHasTheFirstBlocksAndTheFinalSection()
    {
        using var full = DocxFile.Open(Thesis);
        using var prefix = DocxFile.OpenPrefix(Thesis, 64);

        Assert.True(prefix.IsPrefix);
        Assert.False(full.IsPrefix);
        Assert.Equal(64, prefix.Model.Body.Blocks.Count);
        Assert.NotNull(prefix.Model.Body.FinalSection);

        // The section of the last block read: the first section break after the prefix, which is not the final section of the file.
        var next = full.Model.Body.Blocks.Skip(64).OfType<Paragraph>().First(p => p.SectionBreak is not null).SectionBreak;
        Assert.Equal(Xml(full.Model.Body.Root, next), FinalSection(prefix.Model));
        Assert.NotEqual(FinalSection(full.Model), FinalSection(prefix.Model));

        var expected = new Story(full.Model.Body.Root);
        foreach (var block in full.Model.Body.Blocks.Take(64))
        {
            expected.Blocks.Add(block);
        }

        expected.FinalSection = prefix.Model.Body.FinalSection;
        var expectedModel = new WordDocument(expected);
        Assert.Equal(Body(expectedModel), Body(prefix.Model));

        // What a first page needs is whole.
        Assert.NotNull(prefix.Model.Styles);
        Assert.NotNull(prefix.Model.Numbering);
        Assert.NotNull(prefix.Model.Settings);
        Assert.NotNull(prefix.Model.Footnotes);
        Assert.InRange(prefix.Model.HeadersFooters.Count, 1, full.Model.HeadersFooters.Count);
        Assert.Null(prefix.Model.Comments);
    }

    [Fact]
    public void PrefixOfAShortFileIsTheWholeFile()
    {
        using var folder = new TempFolder();
        var path = folder.File("simple.docx");
        DocxBuilder.Simple(path);
        using var full = DocxFile.Open(path);

        foreach (var max in new[] { 2, 3, 1000 })
        {
            using var prefix = DocxFile.OpenPrefix(path, max);
            Assert.False(prefix.IsPrefix);
            Assert.Equal(2, prefix.Model.Body.Blocks.Count);
            Assert.Equal(ModelSnapshot.Of(full.Model).ToString(), ModelSnapshot.Of(prefix.Model).ToString());
        }

        using var cut = DocxFile.OpenPrefix(path, 1);
        Assert.True(cut.IsPrefix);
        Assert.Single(cut.Model.Body.Blocks);
    }

    [Fact]
    public async Task PrefixCannotBeSaved()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        using var prefix = DocxFile.OpenPrefix(path, 10);
        var before = File.ReadAllBytes(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => prefix.SaveAsync(path, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => prefix.SaveAsync(folder.File("copy.docx"), overwriteChangedOriginal: true, TestContext.Current.CancellationToken));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(["thesis.docx"], folder.Names());
    }

    [Fact]
    public void PrefixOpenReadsLessThanTheFullOpen()
    {
        var counter = new int[1];
        StoryXml.BlocksCounter.Value = counter;
        using (DocxFile.Open(Thesis))
        {
        }

        var whole = counter[0];

        counter[0] = 0;
        using (DocxFile.OpenPrefix(Thesis, 64))
        {
        }

        var start = counter[0];
        Assert.True(start > 0);
        Assert.True(start * 4 < whole, $"prefix {start} blocks, full {whole}");
    }

    [Fact]
    public void ABodyThatEndsRightAtTheLimitIsNotCut()
    {
        using var folder = new TempFolder();
        using var prefix = PrefixOf(MainPart(Paragraphs(5) + "<w:sectPr><w:pgSz w:w=\"100\" w:h=\"200\"/></w:sectPr>"), 5, folder, out var full);
        using (full)
        {
            Assert.False(prefix.IsPrefix);
            Assert.Equal(5, prefix.Model.Body.Blocks.Count);
            Assert.NotNull(prefix.Model.Body.FinalSection);
            Assert.Equal(FinalSection(full.Model), FinalSection(prefix.Model));
        }
    }

    [Fact]
    public void NoBodyLevelSectionGivesNoFinalSection()
    {
        using var folder = new TempFolder();
        var body = Paragraphs(3) + "<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"100\" w:h=\"200\"/></w:sectPr></w:pPr></w:p>" + Paragraphs(6);
        using var prefix = PrefixOf(MainPart(body), 2, folder, out var full);
        using (full)
        {
            // The prefix ends inside the first section; its properties are those of the paragraph that ends it.
            Assert.True(prefix.IsPrefix);
            Assert.Equal(2, prefix.Model.Body.Blocks.Count);
            Assert.Null(full.Model.Body.FinalSection);
            Assert.Equal(Xml(full.Model.Body.Root, ((Paragraph)full.Model.Body.Blocks[3]).SectionBreak), FinalSection(prefix.Model));
        }
    }

    [Fact]
    public void TheFinalSectionOfAPrefixIsTheNextSectionBreakNotALaterOne()
    {
        using var folder = new TempFolder();
        const string first = "<w:sectPr><w:pgSz w:w=\"100\" w:h=\"200\"/></w:sectPr>";
        const string second = "<w:sectPr><w:pgSz w:w=\"300\" w:h=\"400\"/></w:sectPr>";
        const string last = "<w:sectPr><w:pgSz w:w=\"500\" w:h=\"600\"/></w:sectPr>";
        var body = Paragraphs(20) + $"<w:p><w:pPr>{first}</w:pPr></w:p>" + Paragraphs(100) + $"<w:p><w:pPr>{second}</w:pPr></w:p>" + Paragraphs(10) + last;
        using var prefix = PrefixOf(MainPart(body), 5, folder, out var full);
        using (full)
        {
            Assert.True(prefix.IsPrefix);
            Assert.Equal(5, prefix.Model.Body.Blocks.Count);
            Assert.Equal(Xml(full.Model.Body.Root, ((Paragraph)full.Model.Body.Blocks[20]).SectionBreak), FinalSection(prefix.Model));
            Assert.Contains("100", FinalSection(prefix.Model), StringComparison.Ordinal);
        }

        // A prefix that reaches past the first break ends in the second section.
        using var later = DocxFile.OpenPrefix(folder.File(Directory.GetFiles(folder.Path).Select(Path.GetFileName).First()!), 30);
        Assert.Contains("300", FinalSection(later.Model), StringComparison.Ordinal);
    }

    [Fact]
    public void AMentionThatIsNoSectionBreakIsPassedOver()
    {
        using var folder = new TempFolder();
        const string sect = "<w:sectPr><w:pgSz w:w=\"100\" w:h=\"200\"/></w:sectPr>";
        const string lookalike = "<w:p><w:r><w:x xmlns:w=\"urn:other\"><w:sectPr/></w:x></w:r></w:p>";
        var body = Paragraphs(8) + lookalike + Paragraphs(10) + $"<w:p><w:pPr>{sect}</w:pPr></w:p>" + Paragraphs(10) + "<w:sectPr><w:pgSz w:w=\"9\"/></w:sectPr>";
        using var prefix = PrefixOf(MainPart(body), 3, folder, out var full);
        using (full)
        {
            Assert.Equal(3, prefix.Model.Body.Blocks.Count);
            Assert.Equal(Xml(full.Model.Body.Root, ((Paragraph)full.Model.Body.Blocks[19]).SectionBreak), FinalSection(prefix.Model));
        }
    }

    [Fact]
    public void ASingleSectionBodyIsCutAtTheLimit()
    {
        using var folder = new TempFolder();
        using var prefix = PrefixOf(MainPart(Paragraphs(50) + "<w:sectPr><w:pgSz w:w=\"100\"/></w:sectPr>"), 5, folder, out var full);
        using (full)
        {
            Assert.True(prefix.IsPrefix);
            Assert.Equal(5, prefix.Model.Body.Blocks.Count);
            Assert.Equal(FinalSection(full.Model), FinalSection(prefix.Model));
        }
    }

    [Fact]
    public void ASectionThatIsNotTheLastChildIsNotTheFinalSection()
    {
        using var folder = new TempFolder();
        var body = Paragraphs(6) + "<w:sectPr><w:pgSz w:w=\"100\" w:h=\"200\"/></w:sectPr>" + Paragraphs(2);
        using var prefix = PrefixOf(MainPart(body), 2, folder, out var full);
        using (full)
        {
            Assert.Null(prefix.Model.Body.FinalSection);
            Assert.Null(full.Model.Body.FinalSection);
            Assert.IsType<UnknownBlock>(full.Model.Body.Blocks[6]);
        }
    }

    [Fact]
    public void ASectionWithAChangeRecordHoldingASectionIsFoundWhole()
    {
        using var folder = new TempFolder();
        const string sect = "<w:sectPr><w:pgSz w:w=\"100\" w:h=\"200\"/><w:sectPrChange w:id=\"1\"><w:sectPr><w:pgSz w:w=\"300\" w:h=\"400\"/></w:sectPr></w:sectPrChange></w:sectPr>";
        using var prefix = PrefixOf(MainPart(Paragraphs(8) + sect), 3, folder, out var full);
        using (full)
        {
            Assert.True(prefix.IsPrefix);
            Assert.NotNull(prefix.Model.Body.FinalSection);
            Assert.Equal(FinalSection(full.Model), FinalSection(prefix.Model));
            Assert.Contains("300", FinalSection(prefix.Model), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnEmptyFinalSectionElementIsFound()
    {
        using var folder = new TempFolder();
        using var prefix = PrefixOf(MainPart(Paragraphs(8) + "<w:sectPr/>"), 3, folder, out var full);
        using (full)
        {
            Assert.NotNull(prefix.Model.Body.FinalSection);
            Assert.Equal(FinalSection(full.Model), FinalSection(prefix.Model));
        }
    }

    [Fact]
    public void AnotherNamespacePrefixAndABomAndWhiteSpaceAreFine()
    {
        using var folder = new TempFolder();
        var body = Paragraphs(8, "x") + "<x:sectPr x:rsidR=\"00A\"><x:pgSz x:w=\"100\" x:h=\"200\"/></x:sectPr>";
        foreach (var (bom, between) in new[] { (false, string.Empty), (true, "\r\n  \t") })
        {
            using var prefix = PrefixOf(MainPart(body, "x", bom, between), 3, folder, out var full);
            using (full)
            {
                Assert.True(prefix.IsPrefix);
                Assert.Equal(3, prefix.Model.Body.Blocks.Count);
                Assert.NotNull(prefix.Model.Body.FinalSection);
                Assert.Equal(FinalSection(full.Model), FinalSection(prefix.Model));
            }
        }
    }

    [Fact]
    public void ADefaultNamespaceDocumentIsFine()
    {
        using var folder = new TempFolder();
        var xml = $"<document xmlns=\"{W}\"><body>{Paragraphs(8, string.Empty).Replace("<:", "<", StringComparison.Ordinal).Replace("</:", "</", StringComparison.Ordinal)}<sectPr><pgSz w=\"100\"/></sectPr></body></document>";
        using var prefix = PrefixOf(Encoding.UTF8.GetBytes(xml), 3, folder, out var full);
        using (full)
        {
            Assert.Equal(3, prefix.Model.Body.Blocks.Count);
            Assert.NotNull(full.Model.Body.FinalSection);
            Assert.Equal(FinalSection(full.Model), FinalSection(prefix.Model));
        }
    }

    [Fact]
    public void SectionLookalikesInTextAreIgnored()
    {
        using var folder = new TempFolder();
        var last = "<w:p><w:r><w:t>&lt;/w:sectPr&gt;&lt;w:sectPr&gt;</w:t></w:r></w:p>";
        using var prefix = PrefixOf(MainPart(Paragraphs(8) + last + "<w:sectPr><w:pgSz w:w=\"100\" w:h=\"200\"/></w:sectPr>"), 3, folder, out var full);
        using (full)
        {
            Assert.Equal(FinalSection(full.Model), FinalSection(prefix.Model));
            Assert.Contains("100", FinalSection(prefix.Model), StringComparison.Ordinal);
        }

        // The last child is a paragraph that holds a section break and nothing after it: the file has no final section, the prefix
        // ends in the section that paragraph ends.
        using var paragraphEnd = PrefixOf(MainPart(Paragraphs(8) + "<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"1\"/></w:sectPr></w:pPr></w:p>"), 3, folder, out var full2);
        using (full2)
        {
            Assert.Null(full2.Model.Body.FinalSection);
            Assert.Equal(Xml(full2.Model.Body.Root, ((Paragraph)full2.Model.Body.Blocks[8]).SectionBreak), FinalSection(paragraphEnd.Model));
        }
    }

    [Fact]
    public void ABodyTheEndOfWhichIsNotRecognizableIsReadWhole()
    {
        using var folder = new TempFolder();
        var body = Paragraphs(8) + "<w:sectPr><w:pgSz w:w=\"100\" w:h=\"200\"/></w:sectPr><!-- note -->";
        using var prefix = PrefixOf(MainPart(body), 3, folder, out var full);
        using (full)
        {
            Assert.False(prefix.IsPrefix);
            Assert.Equal(8, prefix.Model.Body.Blocks.Count);
            Assert.NotNull(prefix.Model.Body.FinalSection);
            Assert.Equal(FinalSection(full.Model), FinalSection(prefix.Model));
        }

        // A Utf-16 part cannot be searched byte by byte either.
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(MainPart(Paragraphs(8) + "<w:sectPr/>")).Replace("UTF-8", "UTF-16", StringComparison.Ordinal))).ToArray();
        using var wide = PrefixOf(utf16, 3, folder, out var full3);
        using (full3)
        {
            Assert.False(wide.IsPrefix);
            Assert.Equal(8, wide.Model.Body.Blocks.Count);
        }
    }

    [Fact]
    public void AHugeBodyIsNotParsed()
    {
        using var folder = new TempFolder();
        var main = MainPart(Paragraphs(60_000) + "<w:sectPr><w:pgSz w:w=\"100\" w:h=\"200\"/></w:sectPr>");
        var path = folder.File("huge.docx");
        DocxBuilder.WithMainBytes(path, main);

        var counter = new int[1];
        StoryXml.BlocksCounter.Value = counter;
        using var prefix = DocxFile.OpenPrefix(path, 10);
        Assert.Equal(10, counter[0]);
        Assert.True(prefix.IsPrefix);
        Assert.Equal(10, prefix.Model.Body.Blocks.Count);
        Assert.NotNull(prefix.Model.Body.FinalSection);
    }

    [Fact]
    public void AMalformedBodyEndsAsAFormatError()
    {
        using var folder = new TempFolder();
        var path = folder.File("bad.docx");
        DocxBuilder.WithMainBytes(path, Encoding.UTF8.GetBytes($"<w:document xmlns:w=\"{W}\"><w:body><w:p><w:r><w:t>x</w:t></w:p></w:body></w:document>"));
        Assert.Throws<DocxFormatException>(() => DocxFile.OpenPrefix(path, 1));
        Assert.Throws<DocxFormatException>(() => DocxFile.OpenPrefix(path, 100));
        Assert.Throws<DocxFormatException>(() => DocxFile.Open(path));
    }

    [Fact]
    public void BadArgumentsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocxFile.OpenPrefix(Thesis, 0));
        Assert.Throws<ArgumentNullException>(() => DocxFile.OpenPrefix((string)null!, 1));
        Assert.Throws<FileNotFoundException>(() => DocxFile.OpenPrefix(Path.Combine(TestData.Root, "no-such.docx"), 1));
        Assert.Throws<DocxFormatException>(() => DocxFile.OpenPrefix(new byte[] { 1, 2, 3 }, 1));
    }
}
