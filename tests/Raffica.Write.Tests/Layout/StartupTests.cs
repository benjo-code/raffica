using System.Globalization;
using System.Text;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using WriteLayoutBench;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class StartupTests
{
    private static string Corpus(string id) => Path.Combine(TestData.Root, "testdata", "write-corpus", id, id + ".docx");

    private static string Thesis => Corpus("thesis");

    private static LayoutOptions Options(byte[] bytes) => new(LayoutMode.Pages, ImageBytes: new PackageImages(bytes).Read);

    /// <summary>Every item of a page with every number it carries, exactly.</summary>
    private static string Describe(LaidOutPage page)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{page.Number} {page.Width:R}x{page.Height:R} strip={page.IsStrip} prov={page.IsProvisional}\n");
        foreach (var item in page.Items)
        {
            switch (item)
            {
                case GlyphRunItem run:
                    text.Append(CultureInfo.InvariantCulture, $"G {run.Face.Family} {run.Face.Bold} {run.Face.Italic} {run.Size:R} {run.Color:X} {run.X:R} {run.Baseline:R} [{string.Join(',', run.Glyphs.ToArray())}] [{string.Join(',', run.Xs.ToArray().Select(x => x.ToString("R", CultureInfo.InvariantCulture)))}]\n");
                    break;
                case RuleItem rule:
                    text.Append(CultureInfo.InvariantCulture, $"R {rule.X:R} {rule.Y:R} {rule.Width:R} {rule.Height:R} {rule.Color:X}\n");
                    break;
                case FillItem fill:
                    text.Append(CultureInfo.InvariantCulture, $"F {fill.X:R} {fill.Y:R} {fill.Width:R} {fill.Height:R} {fill.Color:X}\n");
                    break;
                case ImageItem image:
                    text.Append(CultureInfo.InvariantCulture, $"I {image.Image.Id} {image.X:R} {image.Y:R} {image.Width:R} {image.Height:R}\n");
                    break;
                case PlaceholderItem box:
                    text.Append(CultureInfo.InvariantCulture, $"P {box.X:R} {box.Y:R} {box.Width:R} {box.Height:R}\n");
                    break;
                default:
                    throw new InvalidOperationException(item.GetType().Name);
            }
        }

        text.Append(page.Text);
        return text.ToString();
    }

    private static DocumentLayout Whole(byte[] bytes, FontCatalog fonts)
    {
        var layout = DocumentLayout.Create(DocxFile.Open(bytes).Model, fonts, Options(bytes));
        layout.LayOutFirstPage();
        return layout;
    }

    [Theory]
    [InlineData("thesis", 64)]
    [InlineData("thesis", 2)]
    [InlineData("poi-test-document", 2)]
    [InlineData("poi-table-footnotes", 1)]
    [InlineData("poi-page-specific-head-foot", 1)]
    [InlineData("poi-complex-numbered-lists", 1)]
    [InlineData("poi-endnotes", 1)]
    public void PageOneOfPrefixEqualsPageOneOfFull(string id, int initialBlocks)
    {
        var bytes = File.ReadAllBytes(Corpus(id));
        var fonts = FontCatalog.CreateShipped();
        using var opened = FirstPageOpener.Open(bytes, fonts, Options(bytes), initialBlocks);
        var full = Whole(bytes, fonts);

        Assert.Equal(Describe(full.Page(0)), Describe(opened.Layout.Page(0)));
        if (opened.Docx.IsPrefix)
        {
            Assert.True(opened.Layout.FirstPageIsFinal);
        }
    }

    [Fact]
    public void ThePrefixOfTheThesisIsUsedAndFinal()
    {
        var bytes = File.ReadAllBytes(Thesis);
        using var opened = FirstPageOpener.Open(bytes, FontCatalog.CreateShipped(), Options(bytes));
        Assert.True(opened.Docx.IsPrefix);
        Assert.Equal(FirstPageOpener.DefaultPrefixBlocks, opened.PrefixBlocks);
        Assert.True(opened.Layout.FirstPageIsFinal);
    }

    [Fact]
    public void ABiggerPrefixIsOpenedWhenPageOneReachesTheEnd()
    {
        var bytes = File.ReadAllBytes(Thesis);
        using var opened = FirstPageOpener.Open(bytes, FontCatalog.CreateShipped(), Options(bytes), 1);
        Assert.True(opened.PrefixBlocks > 1);
        Assert.True(opened.Layout.FirstPageIsFinal);
    }

    [Fact]
    public void OnlyTheFacesOfPageOneAreLoaded()
    {
        var bytes = File.ReadAllBytes(Thesis);
        var fonts = FontCatalog.CreateShipped();
        Assert.Equal(0, fonts.LoadedFaces);
        using var opened = FirstPageOpener.Open(bytes, fonts, Options(bytes));
        Assert.InRange(fonts.LoadedFaces, 1, 3);
    }

    [Fact]
    public void FontLoadingDoesNotCopy()
    {
        var assembly = typeof(FontCatalog).Assembly;
        long size;
        using (var stream = assembly.GetManifestResourceStream("Raffica.Write.Layout.Fonts.Carlito-Regular.ttf")!)
        {
            size = stream.Length;
        }

        Assert.True(size > 100_000, "the font is expected to be big enough for a copy to show");
        var fonts = FontCatalog.CreateShipped();

        // Warm the code paths of the catalog and of Skia and HarfBuzz with another face, so the measurement sees the loading of
        // this face and not the first use of the libraries.
        _ = fonts.Resolve("Cambria", false, false);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var face = fonts.Resolve("Calibri", false, false);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal("Carlito", face.Family);
        Assert.True(allocated < size / 8, $"loading allocated {allocated} bytes for a font of {size}");
    }

    [Theory]
    [InlineData("thesis", 64)]
    [InlineData("thesis", 150)]
    public async Task ReplaceModelContinuesAfterPageOne(string id, int blocks)
    {
        var bytes = File.ReadAllBytes(Corpus(id));
        var fonts = FontCatalog.CreateShipped();
        var options = Options(bytes);

        var reference = DocumentLayout.Create(DocxFile.Open(bytes).Model, fonts, options);
        await reference.LayOutRestAsync(null, TestContext.Current.CancellationToken);

        using var prefix = DocxFile.OpenPrefix(bytes, blocks);
        var layout = DocumentLayout.Create(prefix.Model, fonts, options);
        layout.LayOutFirstPage();
        Assert.True(layout.FirstPageIsFinal);
        var first = layout.Page(0);
        var locatedBefore = layout.Locate(0);

        using var full = DocxFile.Open(bytes);
        layout.ReplaceModel(full.Model);
        Assert.Same(first, layout.Page(0));
        Assert.Equal(1, layout.LaidOutPages);
        Assert.Equal(locatedBefore, layout.Locate(0));
        Assert.True(layout.FirstPageIsFinal);
        Assert.False(layout.IsComplete);

        await layout.LayOutRestAsync(null, TestContext.Current.CancellationToken);

        Assert.True(layout.IsComplete);
        Assert.Equal(reference.LaidOutPages, layout.LaidOutPages);
        Assert.Equal(reference.Revision, layout.Revision);
        for (var i = 0; i < reference.LaidOutPages; i++)
        {
            Assert.Equal(reference.Page(i).Number, layout.Page(i).Number);
            Assert.Equal(reference.Page(i).Text, layout.Page(i).Text);
        }

        Assert.Equal(Describe(reference.Page(0)), Describe(layout.Page(0)));
        Assert.Equal(Describe(reference.Page(reference.LaidOutPages / 2)), Describe(layout.Page(reference.LaidOutPages / 2)));
        Assert.Equal(Describe(reference.Page(reference.LaidOutPages - 1)), Describe(layout.Page(reference.LaidOutPages - 1)));
        for (var block = 0; block < full.Model.Body.Blocks.Count; block += 37)
        {
            Assert.Equal(reference.Locate(block), layout.Locate(block));
        }

        Assert.Equal(reference.Notes, layout.Notes);
    }

    private static string Body(int paragraphs, string firstText = "First")
    {
        var text = new StringBuilder($"<w:p><w:r><w:t>{firstText}</w:t></w:r></w:p>");
        for (var i = 1; i < paragraphs; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"<w:p><w:r><w:t>Paragraph number {i}</w:t></w:r></w:p>");
        }

        return text.ToString();
    }

    [Fact]
    public void ReplaceModelRequiresAFinalFirstPage()
    {
        var fonts = PageTestKit.Fonts;
        var small = DocumentLayout.Create(LayoutModel.Document(Body(3)), fonts, new LayoutOptions());

        // Before page 1 exists.
        Assert.Throws<InvalidOperationException>(() => small.ReplaceModel(LayoutModel.Document(Body(3))));

        // Page 1 reaches the last block: not final.
        small.LayOutFirstPage();
        Assert.False(small.FirstPageIsFinal);
        Assert.Throws<InvalidOperationException>(() => small.ReplaceModel(LayoutModel.Document(Body(3))));
        Assert.Throws<ArgumentNullException>(() => small.ReplaceModel(null!));
    }

    [Fact]
    public async Task ReplaceModelRefusesAnotherStartAndLeavesTheLayoutUsable()
    {
        var fonts = PageTestKit.Fonts;
        var layout = DocumentLayout.Create(LayoutModel.Document(Body(120)), fonts, new LayoutOptions());
        layout.LayOutFirstPage();
        Assert.True(layout.FirstPageIsFinal);

        Assert.Throws<InvalidOperationException>(() => layout.ReplaceModel(LayoutModel.Document(Body(200, "Different"))));

        var reference = DocumentLayout.Create(LayoutModel.Document(Body(200)), fonts, new LayoutOptions());
        await reference.LayOutRestAsync(null, TestContext.Current.CancellationToken);
        layout.ReplaceModel(LayoutModel.Document(Body(200)));
        await layout.LayOutRestAsync(null, TestContext.Current.CancellationToken);
        Assert.Equal(reference.LaidOutPages, layout.LaidOutPages);
    }

    [Fact]
    public void ReplaceModelRefusesAfterMorePagesWerePublished()
    {
        var layout = DocumentLayout.Create(LayoutModel.Document(Body(200)), PageTestKit.Fonts, new LayoutOptions());
        layout.LayOutThrough(1);
        Assert.True(layout.FirstPageIsFinal);
        Assert.Throws<InvalidOperationException>(() => layout.ReplaceModel(LayoutModel.Document(Body(200))));
    }
}
