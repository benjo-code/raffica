using System.Text;
using System.Xml.Linq;
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

    /// <summary>A document whose body is the given XML (inside the body element, usual prefixes declared) and no other part.</summary>
    public static void WithBody(string path, string bodyXml)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        WriteMain(package.AddMainDocumentPart(), bodyXml);
    }

    /// <summary>
    /// The generated right-to-left corpus file: short neutral greetings of our own in Hebrew and Arabic (escaped, so the source stays
    /// plain ASCII), <c>w:bidi</c> paragraphs, <c>w:rtl</c> runs with a Latin word inside, and a bidi table.
    /// </summary>
    public static void RightToLeft(string path)
    {
        const string hello = "שלום עולם";
        const string thanks = "תודה רבה";
        const string arabicHello = "مرحبا بالعالم";
        const string arabicThanks = "شكرا جزيلا";
        static string RtlRun(string text, string lang) =>
            "<w:r><w:rPr><w:rtl/><w:lang w:bidi=\"" + lang + "\"/></w:rPr><w:t>" + text + "</w:t></w:r>";
        static string BidiParagraph(params string[] runs) =>
            "<w:p><w:pPr><w:bidi/><w:jc w:val=\"left\"/></w:pPr>" + string.Concat(runs) + "</w:p>";
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        WriteMain(
            package.AddMainDocumentPart(),
            "<w:p><w:r><w:t>Right-to-left text, written for this corpus.</w:t></w:r></w:p>"
            + BidiParagraph(RtlRun(hello, "he-IL"))
            + BidiParagraph(RtlRun(arabicHello, "ar-SA"))
            + BidiParagraph(RtlRun(thanks, "he-IL"), "<w:r><w:t xml:space=\"preserve\"> Raffica </w:t></w:r>", RtlRun(hello, "he-IL"))
            + "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/><w:bidiVisual/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid>"
            + "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr>" + BidiParagraph(RtlRun(arabicThanks, "ar-SA")) + "</w:tc>"
            + "<w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr>" + BidiParagraph(RtlRun(thanks, "he-IL")) + "</w:tc></w:tr></w:tbl>"
            + "<w:p><w:r><w:t>End.</w:t></w:r></w:p>");
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

    /// <summary>A package with a main part and no other part at all (the main part has no relationships part).</summary>
    public static void Bare(string path)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        WriteMain(main, "<w:p><w:r><w:t>Bare</w:t></w:r></w:p>");
    }

    /// <summary>A macro-enabled template (<c>.dotm</c>).</summary>
    public static void MacroTemplate(string path)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.MacroEnabledTemplate);
        var main = package.AddMainDocumentPart();
        WriteMain(main, "<w:p><w:r><w:t>Template</w:t></w:r></w:p>");
    }

    /// <summary>A document with one header part that two relationships (<c>rIdA</c>, <c>rIdB</c>) point to, both used by the section.</summary>
    public static void SharedHeader(string path)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        var header = main.AddNewPart<HeaderPart>("rIdA");
        WritePart(header, "<w:hdr xmlns:w=\"" + XmlSnippets.W + "\"><w:p><w:r><w:t>Shared</w:t></w:r></w:p></w:hdr>");
        WriteMain(
            main,
            "<w:p><w:r><w:t>Text</w:t></w:r></w:p><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdA\"/><w:headerReference w:type=\"first\" r:id=\"rIdB\"/></w:sectPr>");

        package.Dispose();
        EditXml(path, "word/_rels/document.xml.rels", rels =>
        {
            var header = rels.Root!.Elements().Single(e => (string?)e.Attribute("Id") == "rIdA");
            var second = new XElement(header);
            second.SetAttributeValue("Id", "rIdB");
            rels.Root.Add(second);
        });
    }

    public const string ExtendedParaId = "1A2B3C4D";

    /// <summary>A document with one done comment whose extension part (<c>commentsExtended</c>) is related from the comments part, not from the main part.</summary>
    public static void ExtensionOnTheCommentsPart(string path)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        WriteMain(main, "<w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t>Text</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p>");
        var comments = main.AddNewPart<WordprocessingCommentsPart>();
        WritePart(
            comments,
            "<w:comments xmlns:w=\"" + XmlSnippets.W + "\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><w:comment w:id=\"0\" w:author=\"Ann\"><w:p w14:paraId=\"" + ExtendedParaId + "\"><w:r><w:t>Note</w:t></w:r></w:p></w:comment></w:comments>");
        package.Dispose();
        EditXml(path, "[Content_Types].xml", types => types.Root!.Add(new XElement(
            types.Root.Name.Namespace + "Override",
            new XAttribute("PartName", "/word/commentsExtended.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml"))));
        EditEntry(path, "word/commentsExtended.xml", Encoding.UTF8.GetBytes(
            "<w15:commentsEx xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\"><w15:commentEx w15:paraId=\"" + ExtendedParaId + "\" w15:done=\"1\"/></w15:commentsEx>"));
        EditEntry(path, "word/_rels/comments.xml.rels", Encoding.UTF8.GetBytes(
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.microsoft.com/office/2011/relationships/commentsExtended\" Target=\"commentsExtended.xml\"/></Relationships>"));
    }

    private static void EditEntry(string path, string name, byte[] content) => EditInPlace(path, name, content);

    private static void EditXml(string path, string name, Action<XDocument> change)
    {
        XDocument document;
        using (var archive = System.IO.Compression.ZipFile.OpenRead(path))
        using (var stream = archive.GetEntry(name)!.Open())
        {
            document = XDocument.Load(stream);
        }

        change(document);
        EditInPlace(path, name, Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting)));
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
