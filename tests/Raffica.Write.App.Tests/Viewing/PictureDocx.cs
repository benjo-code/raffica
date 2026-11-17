using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>Writes small DOCX files with real PNG pictures: a page per group, each picture its own relationship and its own media part.</summary>
internal static class PictureDocx
{
    /// <summary>A document with <paramref name="pages"/> pages (a page break before each but the first), <paramref name="perPage"/> pictures of <paramref name="pixels"/> square pixels on each.</summary>
    public static string Write(string folder, int pages, int perPage = 1, int pixels = 8, string name = "pictures.docx")
    {
        var path = Path.Combine(folder, name);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        var rels = new StringBuilder();
        var body = new StringBuilder();
        var count = 0;
        for (var page = 0; page < pages; page++)
        {
            for (var k = 0; k < perPage; k++)
            {
                count++;
                var id = string.Concat("rIdPic", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                rels.Append("<Relationship Id=\"").Append(id).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/p").Append(count).Append(".png\"/>");
                Add(zip, string.Concat("word/media/p", count.ToString(System.Globalization.CultureInfo.InvariantCulture), ".png"), Png(pixels, count));
                body.Append("<w:p>").Append(page > 0 && k == 0 ? "<w:pPr><w:pageBreakBefore/></w:pPr>" : string.Empty)
                    .Append("<w:r><w:drawing><wp:inline><wp:extent cx=\"914400\" cy=\"914400\"/><wp:docPr id=\"").Append(count).Append("\" name=\"P").Append(count).Append("\"/>")
                    .Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:blipFill><a:blip r:embed=\"").Append(id)
                    .Append("\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
            }
        }

        Add(zip, "[Content_Types].xml", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>"));
        Add(zip, "_rels/.rels", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>"));
        Add(zip, "word/_rels/document.xml.rels", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + rels + "</Relationships>"));
        Add(zip, "word/document.xml", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>"
            + body + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>"));
        return path;
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static byte[] Png(int pixels, int seed)
    {
        using var bitmap = new SKBitmap(pixels, pixels);
        bitmap.Erase(new SKColor((byte)(40 + (seed * 37 % 200)), (byte)(90 + (seed * 11 % 150)), 30));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var entry = zip.CreateEntry(name).Open();
        entry.Write(bytes);
    }
}
