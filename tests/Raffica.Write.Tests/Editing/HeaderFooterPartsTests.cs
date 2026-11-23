using System.IO.Compression;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class HeaderFooterPartsTests
{
    private static WordDocument Blank() => DocxFile.Open(BlankDocument.Create(PaperSize.A4)).Model;

    [Fact]
    public void CreateGivesAUniqueIdAcrossManyCalls()
    {
        var document = Blank();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 200; i++)
        {
            var id = HeaderFooterParts.Create(document, i % 2 == 0 ? HeaderFooterKind.Footer : HeaderFooterKind.Header, null);
            Assert.StartsWith("rIdWrite", id, StringComparison.Ordinal);
            Assert.True(ids.Add(id));
        }

        Assert.Equal(200, document.HeadersFooters.Count);
    }

    [Fact]
    public void AnIdTheSectionsUseIsNeverTaken()
    {
        var document = Blank();
        document.Body.FinalSection!.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Footer, HeaderFooterType.Default, "rIdWrite1"));

        var id = HeaderFooterParts.Create(document, HeaderFooterKind.Footer, null);

        Assert.NotEqual("rIdWrite1", id);
    }

    [Fact]
    public void ACopyHasNoSharedBlocksWithTheOriginal()
    {
        var document = Blank();
        var first = HeaderFooterParts.Create(document, HeaderFooterKind.Footer, null);
        var original = document.HeadersFooters[first].Content;
        var run = new Run();
        run.Content.Add(new TextContent("keep"));
        ((Paragraph)original.Blocks[0]).Inlines.Add(run);

        var copyId = HeaderFooterParts.Create(document, HeaderFooterKind.Footer, original);

        var copy = document.HeadersFooters[copyId].Content;
        Assert.Equal(original.Blocks.Count, copy.Blocks.Count);
        for (var i = 0; i < copy.Blocks.Count; i++)
        {
            Assert.NotSame(original.Blocks[i], copy.Blocks[i]);
        }

        var copiedRun = Assert.IsType<Run>(((Paragraph)copy.Blocks[0]).Inlines[0]);
        Assert.NotSame(run, copiedRun);
        ((TextContent)copiedRun.Content[0]).Text = "changed";
        Assert.Equal("keep", ((TextContent)run.Content[0]).Text);
    }

    [Fact]
    public void AnEmptyPartHasOneParagraph()
    {
        var document = Blank();

        var header = document.HeadersFooters[HeaderFooterParts.Create(document, HeaderFooterKind.Header, null)];
        var footer = document.HeadersFooters[HeaderFooterParts.Create(document, HeaderFooterKind.Footer, null)];

        Assert.Equal(HeaderFooterKind.Header, header.Kind);
        Assert.Equal(HeaderFooterKind.Footer, footer.Kind);
        Assert.IsType<Paragraph>(Assert.Single(header.Content.Blocks));
        Assert.IsType<Paragraph>(Assert.Single(footer.Content.Blocks));
    }

    [Fact]
    public async Task TheNewPartIsSavedAsAPartAndReopened()
    {
        using var folder = new TempFolder();
        var saved = folder.File("withfooter.docx");
        string id;
        using (var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4)))
        {
            id = HeaderFooterParts.Create(file.Model, HeaderFooterKind.Footer, null);
            var run = new Run();
            run.Content.Add(new TextContent("footer text"));
            ((Paragraph)file.Model.HeadersFooters[id].Content.Blocks[0]).Inlines.Add(run);
            file.Model.Body.FinalSection!.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Footer, HeaderFooterType.Default, id));
            await file.SaveAsync(saved, TestContext.Current.CancellationToken);
        }

        using (var zip = ZipFile.OpenRead(saved))
        {
            var part = Assert.Single(zip.Entries, e => e.FullName.StartsWith("word/footer", StringComparison.Ordinal) && !e.FullName.Contains("_rels", StringComparison.Ordinal));
            using var reader = new StreamReader(part.Open());
            Assert.Contains("footer text", reader.ReadToEnd(), StringComparison.Ordinal);
            using var rels = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
            Assert.Contains($"Id=\"{id}\"", rels.ReadToEnd(), StringComparison.Ordinal);
        }

        using var reopened = DocxFile.Open(saved);
        var footer = Assert.Single(reopened.Model.HeadersFooters.Values);
        Assert.Equal(HeaderFooterKind.Footer, footer.Kind);
        var text = ((TextContent)((Run)((Paragraph)footer.Content.Blocks[0]).Inlines[0]).Content[0]).Text;
        Assert.Equal("footer text", text);
        Assert.Equal(id, Assert.Single(reopened.Model.Body.FinalSection!.HeaderFooterReferences).RelationshipId);
    }

    [Fact]
    public void NoPartIsEverRemoved()
    {
        var document = Blank();
        var ids = Enumerable.Range(0, 5).Select(_ => HeaderFooterParts.Create(document, HeaderFooterKind.Header, null)).ToList();

        _ = HeaderFooterParts.Create(document, HeaderFooterKind.Header, document.HeadersFooters[ids[0]].Content);

        Assert.All(ids, id => Assert.True(document.HeadersFooters.ContainsKey(id)));
        Assert.Equal(6, document.HeadersFooters.Count);
    }
}
