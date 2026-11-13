using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Raffica.Write.Docx;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class DocxFileTests
{
    private static readonly byte[] s_notXml = "this is not xml"u8.ToArray();

    [Fact]
    public void OpenReadsTheThesisModel()
    {
        using var folder = new TempFolder();
        using var file = DocxFile.Open(folder.CopyThesis());
        var model = file.Model;

        Assert.Equal(DocxKind.Docx, file.Kind);
        var topLevel = model.Body.Blocks;
        Assert.Equal(2, topLevel.OfType<Paragraph>().Count(p => p.SectionBreak is not null));
        Assert.NotNull(model.Body.FinalSection);
        Assert.Equal(20, topLevel.OfType<Table>().Count());
        Assert.Equal(100, ModelWalk.RunContents(model.Body).OfType<FootnoteReference>().Count());
        Assert.Equal(102, model.Footnotes!.Notes.Count);
        Assert.Contains(
            ModelWalk.Blocks(topLevel).OfType<Paragraph>().SelectMany(p => FieldScanner.Scan(p.Inlines)),
            f => f.Instruction.TrimStart().StartsWith("TOC", StringComparison.Ordinal));
        Assert.NotNull(model.Styles);
        Assert.NotNull(model.Numbering);
        Assert.NotNull(model.Settings);
        Assert.Null(model.Endnotes);
        Assert.Null(model.Comments);
        Assert.Equal(4, model.HeadersFooters.Count);
    }

    [Fact]
    public void OnlyTheAskedForPartsAreLoaded()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        using var onlyBody = DocxFile.Open(path, DocxParts.Document);
        Assert.Null(onlyBody.Model.Styles);
        Assert.Null(onlyBody.Model.Numbering);
        Assert.Null(onlyBody.Model.Settings);
        Assert.Null(onlyBody.Model.Footnotes);
        Assert.Null(onlyBody.Model.Endnotes);
        Assert.Null(onlyBody.Model.Comments);
        Assert.Empty(onlyBody.Model.HeadersFooters);
        Assert.NotEmpty(onlyBody.Model.Body.Blocks);

        using var notes = DocxFile.Open(path, DocxParts.Notes | DocxParts.Styles);
        Assert.NotNull(notes.Model.Footnotes);
        Assert.Null(notes.Model.Endnotes);
        Assert.NotNull(notes.Model.Styles);
        Assert.Null(notes.Model.Numbering);
        Assert.Empty(notes.Model.HeadersFooters);
    }

    [Fact]
    public void OpenFromMemoryTakesASnapshot()
    {
        var bytes = File.ReadAllBytes(TempFolder.ThesisPath);
        using var file = DocxFile.Open(bytes);
        Array.Clear(bytes);
        Assert.Equal(20, file.Model.Body.Blocks.OfType<Table>().Count());
    }

    [Fact]
    public void ADocumentWithoutCommentsHasNoCommentsModel()
    {
        using var folder = new TempFolder();
        var path = folder.File("simple.docx");
        DocxBuilder.Simple(path);
        using var file = DocxFile.Open(path);
        Assert.Null(file.Model.Comments);
        Assert.Null(file.Model.Footnotes);
        Assert.Equal(2, file.Model.Body.Blocks.Count);
    }

    public static TheoryData<string> MalformedCases() => new() { "truncated", "empty", "random", "no-main-part", "not-xml", "wrong-root", "w-t-with-child" };

    [Theory]
    [MemberData(nameof(MalformedCases))]
    public void MalformedPackageThrowsDocxFormatException(string kind)
    {
        using var folder = new TempFolder();
        var good = folder.File("good.docx");
        DocxBuilder.Simple(good);
        var goodBytes = File.ReadAllBytes(good);
        var path = folder.File("bad.docx");
        switch (kind)
        {
            case "truncated":
                File.WriteAllBytes(path, goodBytes.AsSpan(0, goodBytes.Length / 2).ToArray());
                break;
            case "empty":
                File.WriteAllBytes(path, []);
                break;
            case "random":
                File.WriteAllBytes(path, [.. Enumerable.Range(0, 2000).Select(i => (byte)((i * 31) + 7))]);
                break;
            case "no-main-part":
                DocxBuilder.WithEntry(good, path, "word/document.xml", null);
                break;
            case "not-xml":
                DocxBuilder.WithEntry(good, path, "word/document.xml", s_notXml);
                break;
            case "wrong-root":
                DocxBuilder.WithEntry(good, path, "word/document.xml", "<w:foo xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>"u8.ToArray());
                break;
            case "w-t-with-child":
                DocxBuilder.WithEntry(good, path, "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>a<w:b/>b</w:t></w:r></w:p></w:body></w:document>"u8.ToArray());
                break;
        }

        var before = folder.Names();
        var fromPath = Assert.Throws<DocxFormatException>(() => DocxFile.Open(path));
        Assert.DoesNotContain("this is not xml", fromPath.Message, StringComparison.Ordinal);
        Assert.Throws<DocxFormatException>(() => DocxFile.Open(File.ReadAllBytes(path)));
        Assert.Equal(before, folder.Names());
    }

    [Fact]
    public void AMissingFileIsNotAFormatError()
    {
        using var folder = new TempFolder();
        Assert.Throws<FileNotFoundException>(() => DocxFile.Open(folder.File("missing.docx")));
    }

    [Fact]
    public void AMalformedModelPartFailsTheOpenOnlyWhenItIsLoaded()
    {
        using var folder = new TempFolder();
        var good = folder.File("good.docx");
        DocxBuilder.Simple(good);
        var path = folder.File("bad.docx");
        DocxBuilder.WithEntry(good, path, "word/styles.xml", s_notXml);

        Assert.Throws<DocxFormatException>(() => DocxFile.Open(path));
        Assert.Throws<DocxFormatException>(() => DocxFile.Open(path, DocxParts.Styles));
        using var file = DocxFile.Open(path, DocxParts.Document | DocxParts.Notes);
        Assert.Null(file.Model.Styles);
    }

    [Fact]
    public void HeaderReferencesThatPointNowhereOrToAnotherKindOfPartAreSkipped()
    {
        using var folder = new TempFolder();
        var path = folder.File("broken.docx");
        DocxBuilder.WithBrokenHeaderReferences(path);

        using var file = DocxFile.Open(path);

        Assert.Empty(file.Model.HeadersFooters);
        Assert.Equal(2, file.Model.Body.FinalSection!.HeaderFooterReferences.Count);
    }

    [Fact]
    public async Task DocmKeepsItsMacroPart()
    {
        using var folder = new TempFolder();
        var path = folder.File("macro.docm");
        DocxBuilder.Macro(path);
        var saved = folder.File("saved.docm");

        using (var file = DocxFile.Open(path))
        {
            Assert.Equal(DocxKind.Docm, file.Kind);
            await file.SaveAsync(saved, TestContext.Current.CancellationToken);
        }

        Assert.Equal(DocxBuilder.MacroBytes, PartComparer.ReadPart(saved, "word/vbaProject.bin"));
        var diff = PartComparer.Diff(path, saved);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.DoesNotContain("/word/vbaProject.bin", diff.Changed);
        using var reopened = DocxFile.Open(saved);
        Assert.Equal(DocxKind.Docm, reopened.Kind);
        using var package = WordprocessingDocument.Open(saved, false);
        Assert.NotNull(package.MainDocumentPart!.VbaProjectPart);
        Assert.Equal(WordprocessingDocumentType.MacroEnabledDocument, package.DocumentType);
    }

    [Fact]
    public void AMacroEnabledTemplateIsADocm()
    {
        using var folder = new TempFolder();
        var path = folder.File("template.dotm");
        DocxBuilder.MacroTemplate(path);
        using var file = DocxFile.Open(path);
        Assert.Equal(DocxKind.Docm, file.Kind);
    }

    [Fact]
    public void ADocxIsNotADocm()
    {
        using var folder = new TempFolder();
        var path = folder.File("simple.docx");
        DocxBuilder.Simple(path);
        using var file = DocxFile.Open(path);
        Assert.Equal(DocxKind.Docx, file.Kind);
    }

    [Fact]
    public async Task NothingIsFetchedFromAnExternalRelationship()
    {
        using var folder = new TempFolder();
        var path = folder.File("linked.docx");
        DocxBuilder.WithExternalPicture(path);
        var saved = folder.File("saved.docx");

        using (var file = DocxFile.Open(path))
        {
            var link = ModelWalk.Inlines(file.Model.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines)).OfType<Hyperlink>().Single();
            Assert.Equal("rIdLink", link.RelationshipId);
            await file.SaveAsync(saved, TestContext.Current.CancellationToken);
        }

        string[] Rels(string file) => [.. XDocument.Load(new MemoryStream(PartComparer.ReadPart(file, "word/_rels/document.xml.rels")))
            .Root!.Elements().Select(e => string.Join('|', e.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).Select(a => a.Name + "=" + a.Value))).Order(StringComparer.Ordinal)];
        Assert.Equal(Rels(path), Rels(saved));
        using var package = WordprocessingDocument.Open(saved, false);
        var main = package.MainDocumentPart!;
        Assert.Contains(main.ExternalRelationships, r => r.Id == "rIdPic" && r.Uri.ToString() == DocxBuilder.ExternalAddress);
        Assert.Contains(main.HyperlinkRelationships, r => r.Id == "rIdLink" && r.Uri.ToString() == DocxBuilder.ExternalAddress);
        Assert.Equal(PartComparer.ReadAll(path).Keys, PartComparer.ReadAll(saved).Keys);
    }
}
