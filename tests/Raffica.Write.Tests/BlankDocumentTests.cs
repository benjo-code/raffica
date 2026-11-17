using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class BlankDocumentTests
{
    private static readonly System.Xml.Linq.XNamespace W = LayoutModel.W;

    private static DocxFile OpenBlank(PaperSize paper) => DocxFile.Open(BlankDocument.Create(paper));

    /// <summary>The blank styles and numbering applied to a body of one styled paragraph with one run each, so the resolver sees them as a real document would.</summary>
    private static (WordDocument Document, StyleResolver Resolver) Styled(string styleId)
    {
        using var blank = OpenBlank(PaperSize.A4);
        var document = LayoutModel.Document($"<w:p><w:pPr><w:pStyle w:val=\"{styleId}\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        document.Styles = blank.Model.Styles;
        document.Numbering = blank.Model.Numbering;
        document.Settings = blank.Model.Settings;
        return (document, new StyleResolver(document.Styles, document.Settings, document.Numbering));
    }

    private static double Luminance(uint rgb)
    {
        static double Channel(uint v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel((rgb >> 16) & 0xFF)) + (0.7152 * Channel((rgb >> 8) & 0xFF)) + (0.0722 * Channel(rgb & 0xFF));
    }

    private static double ContrastOnWhite(uint rgb) => 1.05 / (Luminance(rgb) + 0.05);

    [Fact]
    public void EveryBuiltInStyleExists()
    {
        using var file = OpenBlank(PaperSize.A4);
        Assert.Equal(24, BlankStyleSet.Ids.Count);
        foreach (var id in BlankStyleSet.Ids)
        {
            Assert.NotNull(file.Model.Styles!.Find(id));
        }
    }

    [Fact]
    public void HeadingsAreOutlineLevelsZeroToEightWithoutNumbering()
    {
        for (var level = 1; level <= 9; level++)
        {
            var (document, resolver) = Styled("Heading" + level.ToString(CultureInfo.InvariantCulture));
            var resolved = resolver.ForParagraph(LayoutModel.Para(document, 0));
            Assert.Equal(level - 1, resolved.OutlineLevel);
            Assert.Equal(0, resolved.NumberingId);
            Assert.Null(document.Styles!.Find("Heading" + level.ToString(CultureInfo.InvariantCulture))!.ParagraphProperties.NumberingId);
        }
    }

    [Fact]
    public void NormalIsCarlito11AndHeadingsAreCaladea()
    {
        var (document, resolver) = Styled("Normal");
        var paragraph = LayoutModel.Para(document, 0);
        var normal = resolver.ForRun(resolver.ForParagraph(paragraph), (Run)paragraph.Inlines[0]);
        Assert.Equal("Carlito", normal.FontName);
        Assert.Equal(11f, normal.SizePoints);

        for (var level = 1; level <= 9; level++)
        {
            var (hDocument, hResolver) = Styled("Heading" + level.ToString(CultureInfo.InvariantCulture));
            var hParagraph = LayoutModel.Para(hDocument, 0);
            var heading = hResolver.ForRun(hResolver.ForParagraph(hParagraph), (Run)hParagraph.Inlines[0]);
            Assert.Equal("Caladea", heading.FontName);
        }
    }

    [Theory]
    [InlineData("US", "Letter")]
    [InlineData("CA", "Letter")]
    [InlineData("DE", "A4")]
    [InlineData("GB", "A4")]
    public void RegionChoosesPaper(string region, string expected)
    {
        var paper = BlankPaper.For(new RegionInfo(region));
        var want = expected == "Letter" ? PaperSize.Letter : PaperSize.A4;
        Assert.Equal(want, paper);

        using var file = OpenBlank(paper);
        var section = file.Model.Body.FinalSection!;
        var size = section.Get(W + "pgSz")!;
        var margins = section.Get(W + "pgMar")!;
        Assert.Equal(want.WidthTwips.ToString(CultureInfo.InvariantCulture), (string?)size.Attribute(W + "w"));
        Assert.Equal(want.HeightTwips.ToString(CultureInfo.InvariantCulture), (string?)size.Attribute(W + "h"));
        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            Assert.Equal(want.MarginTwips.ToString(CultureInfo.InvariantCulture), (string?)margins.Attribute(W + side));
        }
    }

    [Fact]
    public void HeadingColoursReachContrast()
    {
        foreach (var id in BlankStyleSet.Ids.Where(i => i.StartsWith("Heading", StringComparison.Ordinal) || i == "Title"))
        {
            var (document, resolver) = Styled(id);
            var paragraph = LayoutModel.Para(document, 0);
            var run = resolver.ForRun(resolver.ForParagraph(paragraph), (Run)paragraph.Inlines[0]);
            Assert.NotNull(run.Color);
            Assert.True(ContrastOnWhite(run.Color!.Value & 0xFFFFFF) >= 4.5, id);
        }
    }

    [Fact]
    public void TheSameInputGivesTheSameBytes() =>
        Assert.Equal(BlankDocument.Create(PaperSize.A4), BlankDocument.Create(PaperSize.A4));

    [Fact]
    public async Task SaveAndReopenKeepsTheModel()
    {
        var path = Path.Combine(Path.GetTempPath(), "blank-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            using (var file = OpenBlank(PaperSize.Letter))
            {
                await file.SaveAsync(path, TestContext.Current.CancellationToken);
            }

            using var reopened = DocxFile.Open(path);
            foreach (var id in BlankStyleSet.Ids)
            {
                Assert.NotNull(reopened.Model.Styles!.Find(id));
            }

            var block = Assert.Single(reopened.Model.Body.Blocks);
            var paragraph = Assert.IsType<Paragraph>(block);
            Assert.Empty(paragraph.Inlines);
            Assert.Equal("15840", (string?)reopened.Model.Body.FinalSection!.Get(W + "pgSz")!.Attribute(W + "h"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SavedBlankDocumentPassesSchemaValidation()
    {
        var path = Path.Combine(Path.GetTempPath(), "blank-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            using (var file = OpenBlank(PaperSize.A4))
            {
                await file.SaveAsync(path, TestContext.Current.CancellationToken);
            }

            using var package = WordprocessingDocument.Open(path, false);
            var errors = new OpenXmlValidator().Validate(package, TestContext.Current.CancellationToken).Select(e => e.Description).ToList();
            Assert.Empty(errors);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
