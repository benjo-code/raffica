using System.Text;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class PackageVerifierTests
{
    private const string CustomPart = "customXml/item1.xml";

    /// <summary>A package from <see cref="DocxBuilder.Simple"/> with one extra part that no model part is.</summary>
    private static string Original(TempFolder folder)
    {
        var path = folder.File("original.docx");
        DocxBuilder.Simple(path);
        DocxBuilder.EditInPlace(path, CustomPart, Encoding.UTF8.GetBytes("<item>one</item>"));
        return path;
    }

    private static string Edited(TempFolder folder, string original, string entry, byte[]? content)
    {
        var saved = folder.File("saved.docx");
        DocxBuilder.WithEntry(original, saved, entry, content);
        return saved;
    }

    [Fact]
    public void AnIdenticalCopyPasses()
    {
        using var folder = new TempFolder();
        var original = Original(folder);
        var saved = folder.File("copy.docx");
        File.Copy(original, saved);
        Assert.Equal(new PackageVerdict(true, true), PackageVerifier.Compare(original, saved));
    }

    [Fact]
    public void AChangedPartTheModelDoesNotLoadIsNotIdentical()
    {
        using var folder = new TempFolder();
        var original = Original(folder);
        var saved = Edited(folder, original, CustomPart, Encoding.UTF8.GetBytes("<item>two</item>"));
        var verdict = PackageVerifier.Compare(original, saved);
        Assert.False(verdict.UnloadedIdentical);
        Assert.True(verdict.LoadedEquivalent);
    }

    [Fact]
    public void AnAddedPartIsNotIdentical()
    {
        using var folder = new TempFolder();
        var original = Original(folder);
        var saved = Edited(folder, original, "word/media/extra.bin", [1, 2, 3]);
        Assert.False(PackageVerifier.Compare(original, saved).UnloadedIdentical);
    }

    [Fact]
    public void ARemovedPartIsNotIdentical()
    {
        using var folder = new TempFolder();
        var original = Original(folder);
        var saved = Edited(folder, original, CustomPart, null);
        Assert.False(PackageVerifier.Compare(original, saved).UnloadedIdentical);
    }

    [Fact]
    public void ARemovedModelPartIsNotIdentical()
    {
        using var folder = new TempFolder();
        var original = Original(folder);
        var saved = Edited(folder, original, "word/styles.xml", null);
        Assert.False(PackageVerifier.Compare(original, saved).UnloadedIdentical);
    }

    [Fact]
    public void ADifferentModelPartIsNotEquivalent()
    {
        using var folder = new TempFolder();
        var original = Original(folder);
        var text = Encoding.UTF8.GetString(PartComparer.ReadPart(original, "/word/document.xml")).Replace("First paragraph", "First paragraph!", StringComparison.Ordinal);
        var saved = Edited(folder, original, "word/document.xml", Encoding.UTF8.GetBytes(text));
        var verdict = PackageVerifier.Compare(original, saved);
        Assert.True(verdict.UnloadedIdentical);
        Assert.False(verdict.LoadedEquivalent);
    }

    [Fact]
    public void AReformattedModelPartIsEquivalent()
    {
        using var folder = new TempFolder();
        var original = Original(folder);
        var reformatted = XElement.Parse(Encoding.UTF8.GetString(PartComparer.ReadPart(original, "/word/document.xml"))).ToString(SaveOptions.None);
        var saved = Edited(folder, original, "word/document.xml", Encoding.UTF8.GetBytes(reformatted));
        Assert.NotEmpty(PartComparer.Diff(original, saved).Changed);
        Assert.Equal(new PackageVerdict(true, true), PackageVerifier.Compare(original, saved));
    }

    [Fact]
    public void AModelPartThatIsNotXmlIsNotEquivalent()
    {
        using var folder = new TempFolder();
        var original = Original(folder);
        var saved = Edited(folder, original, "word/document.xml", Encoding.UTF8.GetBytes("not xml"));
        Assert.False(PackageVerifier.Compare(original, saved).LoadedEquivalent);
    }

    [Theory]
    [InlineData("/word/document.xml", true)]
    [InlineData("/word/styles.xml", true)]
    [InlineData("/word/numbering.xml", true)]
    [InlineData("/word/settings.xml", true)]
    [InlineData("/word/footnotes.xml", true)]
    [InlineData("/word/endnotes.xml", true)]
    [InlineData("/word/comments.xml", true)]
    [InlineData("/word/commentsExtended.xml", true)]
    [InlineData("/word/header1.xml", true)]
    [InlineData("/word/header.xml", true)]
    [InlineData("/word/footer12.xml", true)]
    [InlineData("/word/commentsIds.xml", false)]
    [InlineData("/word/headerFoo.xml", false)]
    [InlineData("/word/header1.xml.rels", false)]
    [InlineData("/word/media/x.xml", false)]
    [InlineData("/word/media/header1.xml", false)]
    [InlineData("/word/theme/theme1.xml", false)]
    [InlineData("/word/fontTable.xml", false)]
    [InlineData("/word/_rels/document.xml.rels", false)]
    [InlineData("/[Content_Types].xml", false)]
    [InlineData("/customXml/item1.xml", false)]
    [InlineData("/WORD/document.xml", false)]
    [InlineData("word/document.xml", false)]
    public void IsModelPartFollowsTheNameRule(string name, bool expected) => Assert.Equal(expected, PackageVerifier.IsModelPart(name));
}
