using System.Diagnostics;
using Raffica.TestSupport;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>Write's own layout of every corpus file, as the corpus tool reads it (<see cref="WriteLayoutFacts"/>).</summary>
public sealed class CorpusLayoutTests
{
    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static string Docx(string id) => Path.Combine(Root, id, id + ".docx");

    [Fact]
    public void EveryCorpusFileLaysOutWithoutAnException()
    {
        var entries = CorpusManifest.Load(Root).Files;
        var clock = Stopwatch.StartNew();
        var counted = 0;
        foreach (var entry in entries)
        {
            var facts = WriteLayoutFacts.Load(Docx(entry.Id));
            Assert.True(facts.PageCount >= 1, entry.Id);
            Assert.Equal(facts.PageCount, facts.PageTexts.Count);
            counted++;
        }

        Assert.Equal(entries.Count, counted);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"the corpus took {clock.Elapsed.TotalSeconds:0.0} s");
    }

    [Fact]
    public void LayoutFactsAreDeterministic()
    {
        foreach (var id in new[] { "thesis", "poi-test-document", "poi-table-footnotes" })
        {
            var first = WriteLayoutFacts.Load(Docx(id));
            var second = WriteLayoutFacts.Load(Docx(id));
            Assert.Equal(first.PageCount, second.PageCount);
            Assert.Equal(first.PageTexts, second.PageTexts);
        }
    }

    [Fact]
    public void ThePageTextsAreNormalised()
    {
        var facts = WriteLayoutFacts.Load(Docx("thesis"));
        Assert.All(facts.PageTexts, text => Assert.Equal(TextNormalizer.Normalize(text), text));
    }

    [Fact]
    public void AcceptViewHasNoDeletedText()
    {
        // poi-tika-792 has no surviving word: its runs are the deleted "s" and "." and an "b" that was moved away, so the
        // accepted-changes view is one page without text.
        var facts = WriteLayoutFacts.Load(Docx("poi-tika-792"));
        Assert.Equal(1, facts.PageCount);
        Assert.Equal("", string.Join(' ', facts.PageTexts));
    }

    [Fact]
    public void AcceptViewShowsInsertedAndHidesDeletedText()
    {
        using var folder = new Raffica.Write.Tests.Support.TempFolder();
        var path = folder.File("revisions.docx");
        Raffica.Write.Tests.Support.DocxBuilder.WithBody(
            path,
            "<w:p><w:r><w:t>kept </w:t></w:r><w:ins w:id=\"1\" w:author=\"A\"><w:r><w:t>inserted </w:t></w:r></w:ins>"
            + "<w:del w:id=\"2\" w:author=\"A\"><w:r><w:delText>removed </w:delText></w:r></w:del><w:r><w:t>tail</w:t></w:r></w:p>");
        var words = string.Join(' ', WriteLayoutFacts.Load(path).PageTexts).Split(' ');
        Assert.Equal(["kept", "inserted", "tail"], words);
    }

    [Fact]
    public async Task AcceptViewMatchesWordAcceptForTrackedChanges()
    {
        var pdf = Path.Combine(Root, "poi-tika-792", CorpusTable.WordAcceptPdf);
        if (!File.Exists(pdf))
        {
            Assert.Skip("word-accept.pdf is absent for poi-tika-792 (the owner's Word PDF, see testdata/write-corpus/README.md)");
        }

        var word = await PdfPageFacts.ReadAsync(pdf, TestContext.Current.CancellationToken);
        var write = WriteLayoutFacts.Load(Docx("poi-tika-792"));
        var result = PageComparer.Compare(word, write);
        Assert.True(result.PageCountEqual);
        Assert.True(result.MeanDice >= 0.9, $"mean dice {result.MeanDice:0.000}");
    }
}
