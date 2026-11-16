using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>A style edit changes the style sheet, which an engine reads once: <see cref="DocumentLayout.Restyle"/> starts over on a fresh engine.</summary>
public sealed class RestyleTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Thesis => Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx");

    private static void SetHeadingSize(WordDocument model, string halfPoints)
    {
        var style = model.Styles!.Find("Heading1") ?? throw new InvalidOperationException("no Heading1");
        style.RunProperties.Set(W + "sz", new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
    }

    private static async Task<DocumentLayout> LaidAsync(WordDocument model)
    {
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, CancellationToken.None);
        return layout;
    }

    [Fact]
    public async Task ChangingAStyleAndRestylingEqualsAFullLayout()
    {
        using var file = DocxFile.Open(Thesis);
        var layout = await LaidAsync(file.Model);
        SetHeadingSize(file.Model, "72");

        var refill = layout.Restyle(2);

        Assert.True(refill.Restarted);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, file.Model, label: "restyle heading size");
    }

    [Fact]
    public async Task WithoutRestyleTheLayoutDoesNotSeeAStyleChange()
    {
        using var file = DocxFile.Open(Thesis);
        var layout = await LaidAsync(file.Model);
        var before = layout.Page(1);
        SetHeadingSize(file.Model, "72");
        var full = await LaidAsync(file.Model);

        // The engine's resolver snapshotted the styles: the old layout still has the old pages, the fresh one has bigger headings.
        Assert.NotEqual(full.LaidOutPages, layout.LaidOutPages);
        var changed = Enumerable.Range(0, Math.Min(full.LaidOutPages, layout.LaidOutPages))
            .First(i => full.Page(i).Text != layout.Page(i).Text || full.Page(i).Items.Count != layout.Page(i).Items.Count);
        Assert.Same(before, layout.Page(1));
        Assert.True(changed < 40, "a heading of the first chapters differs: " + changed);

        layout.Restyle(changed);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(full.Page(changed).Text, layout.Page(changed).Text);
        Assert.Equal(full.Page(changed).Items.Count, layout.Page(changed).Items.Count);
    }

    [Fact]
    public async Task RestyleCountsARestart()
    {
        using var file = DocxFile.Open(Thesis);
        var layout = await LaidAsync(file.Model);
        SetHeadingSize(file.Model, "72");

        layout.Restyle(0);

        Assert.Equal(new EditStats(1, 1), layout.Stats);
    }

    [Fact]
    public void RestyleAfterAFaultThrows()
    {
        using var file = DocxFile.Open(Thesis);
        var layout = DocumentLayout.Create(file.Model, PageTestKit.Fonts, new LayoutOptions());
        layout.BeforePage = published =>
        {
            if (published == 1)
            {
                throw new InvalidOperationException("injected");
            }
        };
        Assert.Throws<InvalidOperationException>(() => layout.LayOutThrough(3));

        // LayOutThrough and Restyle share the one remembered fault.
        Assert.Throws<InvalidOperationException>(() => layout.Restyle(1));
    }
}
