using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class PartComparerTests
{
    [Fact]
    public void AnIdenticalCopyHasNoDifference()
    {
        using var folder = new TempFolder();
        var original = folder.CopyThesis();
        var copy = folder.File("copy.docx");
        File.Copy(original, copy);

        Assert.True(PartComparer.Diff(original, copy).IsEmpty);
    }

    [Fact]
    public void AddedRemovedAndChangedPartsAreListedByTheirDecompressedBytes()
    {
        using var folder = new TempFolder();
        var original = folder.File("a.docx");
        DocxBuilder.Simple(original);
        var withExtra = folder.File("b.docx");
        DocxBuilder.WithEntry(original, withExtra, "word/extra.xml", [1, 2, 3]);
        var changed = folder.File("c.docx");
        DocxBuilder.WithEntry(withExtra, changed, "word/styles.xml", [9]);
        var without = folder.File("d.docx");
        DocxBuilder.WithEntry(original, without, "word/styles.xml", null);

        Assert.Equal(["/word/extra.xml"], PartComparer.Diff(original, withExtra).Added);
        Assert.Equal(["/word/extra.xml"], PartComparer.Diff(withExtra, original).Removed);
        var diff = PartComparer.Diff(original, changed);
        Assert.Equal(["/word/extra.xml"], diff.Added);
        Assert.Equal(["/word/styles.xml"], diff.Changed);
        Assert.Empty(diff.Removed);
        Assert.Equal(["/word/styles.xml"], PartComparer.Diff(original, without).Removed);
    }

    [Fact]
    public void AnotherCompressionOfTheSameBytesIsNoDifference()
    {
        using var folder = new TempFolder();
        var original = folder.File("a.docx");
        DocxBuilder.Simple(original);
        var rewritten = folder.File("b.docx");
        var document = PartComparer.ReadPart(original, "word/document.xml");
        DocxBuilder.WithEntry(original, rewritten, "word/document.xml", document);

        Assert.True(PartComparer.Diff(original, rewritten).IsEmpty);
    }
}
