using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace Raffica.Write.Tests.Support;

/// <summary>Small DOCX and DOCM files made through the Open XML SDK (package layer) with the main part written as raw XML.</summary>
internal static class DocxBuilder
{
    public const string ExternalAddress = "http://127.0.0.1:1/x.png";

    public static readonly byte[] MacroBytes = [0x01, 0x02, 0x03, 0xFE, 0x00, 0x7F];

    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    /// <summary>A document with two paragraphs and a styles part and no comments, notes, numbering or settings part.</summary>
    public static void Simple(string path)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        WriteMain(main, "<w:p><w:r><w:t>First paragraph</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t xml:space=\"preserve\"> Second </w:t></w:r></w:p>");
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        WritePart(styles, "<w:styles xmlns:w=\"" + XmlSnippets.W + "\"><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/></w:style></w:styles>");
    }

    /// <summary>A document with a linked picture (external relationship) and a hyperlink to the same address.</summary>
    public static void WithExternalPicture(string path)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        main.AddExternalRelationship(RelBase + "image", new Uri(ExternalAddress), "rIdPic");
        main.AddHyperlinkRelationship(new Uri(ExternalAddress), isExternal: true, "rIdLink");
        WriteMain(
            main,
            "<w:p><w:hyperlink r:id=\"rIdLink\"><w:r><w:t>link</w:t></w:r></w:hyperlink></w:p>"
            + "<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"100\" cy=\"200\"/><wp:docPr id=\"1\" name=\"Linked\" descr=\"alt\"/>"
            + "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:blipFill><a:blip r:link=\"rIdPic\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
    }

    /// <summary>A document whose section refers to a header by an id nothing defines and by the id of the styles part (not a header).</summary>
    public static void WithBrokenHeaderReferences(string path)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        WritePart(styles, "<w:styles xmlns:w=\"" + XmlSnippets.W + "\"/>");
        var stylesId = main.GetIdOfPart(styles);
        WriteMain(
            main,
            "<w:p><w:r><w:t>Text</w:t></w:r></w:p><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdNowhere\"/><w:footerReference w:type=\"default\" r:id=\"" + stylesId + "\"/></w:sectPr>");
    }

    /// <summary>A macro-enabled document with a <c>word/vbaProject.bin</c> part (<see cref="MacroBytes"/>) related to the main part.</summary>
    public static void Macro(string path)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.MacroEnabledDocument);
        var main = package.AddMainDocumentPart();
        WriteMain(main, "<w:p><w:r><w:t>Macro document</w:t></w:r></w:p>");
        var vba = main.AddNewPart<VbaProjectPart>();
        using var stream = vba.GetStream(FileMode.Create, FileAccess.Write);
        stream.Write(MacroBytes);
    }

    /// <summary>A copy of a package with one entry replaced (or added) by <paramref name="content"/>; <see langword="null"/> removes it.</summary>
    public static void WithEntry(string source, string target, string entryName, byte[]? content)
    {
        File.Copy(source, target, overwrite: true);
        EditInPlace(target, entryName, content);
    }

    /// <summary>Replaces (or adds) one entry of the package at <paramref name="path"/>; <see langword="null"/> removes it.</summary>
    public static void EditInPlace(string path, string entryName, byte[]? content)
    {
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update);
        archive.GetEntry(entryName)?.Delete();
        if (content is not null)
        {
            using var stream = archive.CreateEntry(entryName).Open();
            stream.Write(content);
        }
    }

    private static void WriteMain(MainDocumentPart main, string bodyXml)
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"" + XmlSnippets.W
            + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>"
            + bodyXml + "</w:body></w:document>";
        WritePart(main, xml);
    }

    private static void WritePart(OpenXmlPart part, string xml)
    {
        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        stream.Write(new UTF8Encoding(false).GetBytes(xml));
    }
}
