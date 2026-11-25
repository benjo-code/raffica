using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>
/// A DOCX for the picture gesture tests: six paragraphs of text (block 0 to 5), a floating picture of one inch anchored to paragraph 1 and wrapped square, a picture of two inches by one inch in line with the text in
/// paragraph 4, then a table of two rows by two columns (block 6, <c>c00</c> to <c>c11</c>) and a last paragraph (block 7).
/// </summary>
internal static class FloatingDocx
{
    /// <summary>The offset of the floating picture from the column, in EMU.</summary>
    public const long FloatOffsetX = 3_000_000;

    /// <summary>The offset of the floating picture from its paragraph, in EMU.</summary>
    public const long FloatOffsetY = 0;

    /// <summary>The description of the floating picture.</summary>
    public const string FloatAlt = "A floating square";

    public static string Write(string folder, string name = "floating.docx", bool aligned = false, string? positionVertical = null)
    {
        var path = Path.Combine(folder, name);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        var body = new StringBuilder();
        for (var i = 0; i < 6; i++)
        {
            body.Append("<w:p><w:pPr><w:spacing w:after=\"600\"/></w:pPr>");
            if (i == 1)
            {
                var horizontal = aligned ? "<wp:positionH relativeFrom=\"column\"><wp:align>center</wp:align></wp:positionH>" : "<wp:positionH relativeFrom=\"column\"><wp:posOffset>" + FloatOffsetX + "</wp:posOffset></wp:positionH>";
                var vertical = positionVertical ?? ("<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>" + FloatOffsetY + "</wp:posOffset></wp:positionV>");
                body.Append("<w:r><w:drawing><wp:anchor distT=\"0\" distB=\"0\" distL=\"114300\" distR=\"114300\" simplePos=\"0\" relativeHeight=\"251659264\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">")
                    .Append("<wp:simplePos x=\"0\" y=\"0\"/>").Append(horizontal).Append(vertical)
                    .Append("<wp:extent cx=\"914400\" cy=\"914400\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/><wp:wrapSquare wrapText=\"bothSides\"/>")
                    .Append("<wp:docPr id=\"1\" name=\"F1\" descr=\"").Append(FloatAlt).Append("\"/>")
                    .Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:blipFill><a:blip r:embed=\"rIdPic1\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>");
            }

            if (i == 4)
            {
                body.Append("<w:r><w:drawing><wp:inline><wp:extent cx=\"1828800\" cy=\"914400\"/><wp:docPr id=\"2\" name=\"I1\" descr=\"An inline picture\"/>")
                    .Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:blipFill><a:blip r:embed=\"rIdPic1\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>");
            }
            else
            {
                body.Append("<w:r><w:t>Paragraph ").Append(i).Append(" has some words in it so that its first line is easy to hit with the pointer.</w:t></w:r>");
            }

            body.Append("</w:p>");
        }

        body.Append("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid>");
        for (var row = 0; row < 2; row++)
        {
            body.Append("<w:tr>");
            for (var column = 0; column < 2; column++)
            {
                body.Append("<w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>c").Append(row).Append(column).Append("</w:t></w:r></w:p></w:tc>");
            }

            body.Append("</w:tr>");
        }

        body.Append("</w:tbl><w:p><w:r><w:t>After the table</w:t></w:r></w:p>");
        Add(zip, "word/media/p1.png", Png());
        Add(zip, "[Content_Types].xml", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>"));
        Add(zip, "_rels/.rels", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>"));
        Add(zip, "word/_rels/document.xml.rels", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdPic1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/p1.png\"/></Relationships>"));
        Add(zip, "word/document.xml", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>"
            + body + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>"));
        return path;
    }

    /// <summary>A small real PNG (for the insert tests).</summary>
    public static byte[] Png(int pixels = 8)
    {
        using var bitmap = new SKBitmap(pixels, pixels);
        bitmap.Erase(new SKColor(40, 160, 30));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var entry = zip.CreateEntry(name).Open();
        entry.Write(bytes);
    }
}
