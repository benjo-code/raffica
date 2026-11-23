using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;

namespace Raffica.Write.Tests.Editing;

public sealed class PageNumberFieldTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static Paragraph Number(PageNumberLook look = PageNumberLook.Plain, string prefix = "") =>
        PageNumberField.Build(look, PageNumberAlign.Center, prefix, "Page", "of", "1", "9");

    private static IEnumerable<RunContent> Contents(Paragraph paragraph) => paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content);

    [Fact]
    public void APageFieldIsFiveRunsAndTheInstructionReadsPAGE()
    {
        var paragraph = Number();

        Assert.Equal(5, paragraph.Inlines.Count);
        Assert.All(paragraph.Inlines, inline => Assert.IsType<Run>(inline));
        var kinds = Contents(paragraph).OfType<FieldChar>().Select(f => f.Kind).ToList();
        Assert.Equal([FieldCharKind.Begin, FieldCharKind.Separate, FieldCharKind.End], kinds);
        Assert.Equal(" PAGE ", Contents(paragraph).OfType<InstructionText>().Single().Text);
        Assert.Equal("1", Contents(paragraph).OfType<TextContent>().Single().Text);
        Assert.Equal("center", paragraph.Properties.Get(W + "jc")!.Attribute(W + "val")!.Value);
    }

    [Fact]
    public void FieldScannerFindsTheFieldWeBuilt()
    {
        var paragraph = Number(prefix: "A-");

        var field = Assert.Single(FieldScanner.Scan(paragraph.Inlines));
        Assert.Equal("PAGE", field.Instruction);
        Assert.True(field.IsComplete);
        Assert.Equal("A-", ((TextContent)((Run)paragraph.Inlines[0]).Content[0]).Text);
    }

    [Theory]
    [InlineData(PageNumberLook.PageOfTotal, "Page 1 of 9")]
    [InlineData(PageNumberLook.Slash, "1 / 9")]
    public void PageOfTotalHoldsTwoFields(PageNumberLook look, string text)
    {
        var paragraph = Number(look);

        Assert.Equal(["PAGE", "NUMPAGES"], FieldScanner.Scan(paragraph.Inlines).Select(f => f.Instruction).ToArray());
        Assert.Equal(text, string.Concat(Contents(paragraph).OfType<TextContent>().Select(t => t.Text)));
    }

    [Fact]
    public void HoldsPageIsTrueOnlyForAPageField()
    {
        Assert.True(PageNumberField.HoldsPage(Number()));
        Assert.True(PageNumberField.HoldsPage(Number(PageNumberLook.PageOfTotal)));
        Assert.False(PageNumberField.HoldsPage(new Paragraph()));

        var total = new Paragraph();
        var run = new Run();
        run.Content.Add(new FieldChar(FieldCharKind.Begin));
        run.Content.Add(new InstructionText(" NUMPAGES "));
        run.Content.Add(new FieldChar(FieldCharKind.Separate));
        run.Content.Add(new TextContent("3"));
        run.Content.Add(new FieldChar(FieldCharKind.End));
        total.Inlines.Add(run);
        Assert.False(PageNumberField.HoldsPage(total));
    }

    [Fact]
    public async Task TheFieldRoundTripsThroughDocxFile()
    {
        using var folder = new TempFolder();
        var saved = folder.File("numbered.docx");
        using (var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4)))
        {
            var id = HeaderFooterParts.Create(file.Model, HeaderFooterKind.Footer, null);
            file.Model.HeadersFooters[id].Content.Blocks[0] = Number(PageNumberLook.PageOfTotal);
            file.Model.Body.FinalSection!.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Footer, HeaderFooterType.Default, id));
            await file.SaveAsync(saved, TestContext.Current.CancellationToken);
        }

        using var reopened = DocxFile.Open(saved);
        var footer = Assert.Single(reopened.Model.HeadersFooters.Values);
        var paragraph = Assert.IsType<Paragraph>(footer.Content.Blocks[0]);
        Assert.Equal(["PAGE", "NUMPAGES"], FieldScanner.Scan(paragraph.Inlines).Select(f => f.Instruction).ToArray());
        Assert.Equal("Page 1 of 9", string.Concat(Contents(paragraph).OfType<TextContent>().Select(t => t.Text)));
        Assert.True(PageNumberField.HoldsPage(paragraph));
    }

    [Fact]
    public void TheLayoutEvaluatesIt()
    {
        var model = Document(PageTestKit.P("body") + PageTestKit.Sect(Footer("default", "fX")), new Dictionary<string, string> { ["fX"] = FooterPart(Para(string.Empty)) });
        model.HeadersFooters["fX"].Content.Blocks[0] = Number(PageNumberLook.PageOfTotal);

        var layout = Laid(model);

        Assert.Equal(1, layout.LaidOutPages);
        Assert.Equal("Page 1 of 1", LastLine(layout.Page(0)));
    }
}
