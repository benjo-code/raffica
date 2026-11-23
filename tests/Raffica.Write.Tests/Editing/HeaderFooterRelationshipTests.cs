using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// A header or footer that gets an own or an even copy keeps what its content points to: the copy is written with the relationships of its source (same ids, the picture and the link target shared),
/// so a picture and a link are the same after the copy, after a save and after a reopen, and no relationship id of a part dangles.
/// </summary>
public sealed class HeaderFooterRelationshipTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rels = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private sealed class StillClock : TimeProvider
    {
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => 0;
    }

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain, new StillClock());

    // A blank document with two one-page sections; the first has a header with a picture and a link ("Link text") and a footer with a link, the second inherits both.
    private static byte[] WithHeader(bool footerToo = true)
    {
        using var input = new MemoryStream(BlankDocument.Create(PaperSize.A4));
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var read = entry.Open();
                using var text = new MemoryStream();
                read.CopyTo(text);
                var content = Encoding.UTF8.GetString(text.ToArray());
                if (entry.FullName == "word/document.xml")
                {
                    var headerRefs = "<w:headerReference w:type=\"default\" r:id=\"rIdH1\"/>" + (footerToo ? "<w:footerReference w:type=\"default\" r:id=\"rIdF1\"/>" : string.Empty);
                    content = content.Replace(
                        "<w:p/>",
                        $"<w:p><w:pPr><w:sectPr>{headerRefs}<w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1417\" w:right=\"1417\" w:bottom=\"1417\" w:left=\"1417\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/></w:sectPr></w:pPr><w:r><w:t>one</w:t></w:r></w:p><w:p><w:r><w:t>two</w:t></w:r></w:p>",
                        StringComparison.Ordinal);
                }
                else if (entry.FullName == "word/_rels/document.xml.rels")
                {
                    content = content.Replace(
                        "</Relationships>",
                        "<Relationship Id=\"rIdH1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>"
                        + (footerToo ? "<Relationship Id=\"rIdF1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>" : string.Empty)
                        + "</Relationships>",
                        StringComparison.Ordinal);
                }
                else if (entry.FullName == "[Content_Types].xml")
                {
                    content = content.Replace(
                        "</Types>",
                        "<Default Extension=\"png\" ContentType=\"image/png\"/>"
                        + "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>"
                        + (footerToo ? "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>" : string.Empty)
                        + "</Types>",
                        StringComparison.Ordinal);
                }

                var created = target.CreateEntry(entry.FullName);
                using var write = created.Open();
                write.Write(Encoding.UTF8.GetBytes(content));
            }

            Add(target, "word/header1.xml", Header);
            Add(target, "word/_rels/header1.xml.rels", HeaderRels);
            Add(target, "word/media/image1.png", Png);
            if (footerToo)
            {
                Add(target, "word/footer1.xml", Footer);
                Add(target, "word/_rels/footer1.xml.rels", FooterRels);
            }
        }

        return output.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string xml) => Add(zip, name, Encoding.UTF8.GetBytes(xml));

    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(bytes);
    }

    private const string Header =
        "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
        + "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
        + "<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"635000\" cy=\"317500\"/><wp:docPr id=\"1\" name=\"logo\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
        + "<pic:pic><pic:blipFill><a:blip r:embed=\"rIdImg\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>"
        + "<w:hyperlink r:id=\"rIdLink\"><w:r><w:t>Link text</w:t></w:r></w:hyperlink></w:p></w:hdr>";

    private const string HeaderRels =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rIdImg\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/>"
        + "<Relationship Id=\"rIdLink\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.org/\" TargetMode=\"External\"/>"
        + "</Relationships>";

    private const string Footer =
        "<w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
        + "<w:p><w:hyperlink r:id=\"rIdFootLink\"><w:r><w:t>Foot link</w:t></w:r></w:hyperlink></w:p></w:ftr>";

    private const string FooterRels =
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
        + "<Relationship Id=\"rIdFootLink\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"https://example.org/footer\" TargetMode=\"External\"/>"
        + "</Relationships>";

    private static DocumentLayout Lay(WordDocument document, List<string>? asked = null) =>
        Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions(ImageBytes: id =>
        {
            asked?.Add(id);
            return Png;
        })));

    // Every picture of every page: where and how big (the id is separate: it differs after a reopen).
    private static List<(float X, float Y, float W, float H)> Pictures(DocumentLayout layout) =>
        [.. Pages(layout).SelectMany(page => page.Items.OfType<ImageItem>()).Select(i => (i.X, i.Y, i.Width, i.Height))];

    private static List<string> PictureIds(DocumentLayout layout) =>
        [.. Pages(layout).SelectMany(page => page.Items.OfType<ImageItem>()).Select(i => i.Image.Id)];

    private static string[] Tops(DocumentLayout layout) => [.. Pages(layout).Select(page => Lines(page)[0])];

    private static XDocument Read(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }

    // The package must not refer to a relationship id that its part lacks, nor to a target that is not in the file.
    private static void AssertNoDanglingIds(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var parts = zip.Entries.Where(e => e.FullName.StartsWith("word/", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal) && e.FullName.Count(c => c == '/') == 1).ToList();
        Assert.NotEmpty(parts);
        foreach (var part in parts)
        {
            var relsName = "word/_rels/" + Path.GetFileName(part.FullName) + ".rels";
            var ids = zip.GetEntry(relsName) is null ? [] : Read(zip, relsName).Root!.Elements(Rels + "Relationship").Select(r => (string)r.Attribute("Id")!).ToHashSet(StringComparer.Ordinal);
            foreach (var attribute in Read(zip, part.FullName).Descendants().SelectMany(e => e.Attributes()).Where(a => a.Name.Namespace == R && a.Name.LocalName is "embed" or "id" or "link"))
            {
                if (part.FullName is "word/document.xml")
                {
                    continue;
                }

                Assert.True(ids.Contains(attribute.Value), $"{part.FullName}: {attribute.Value} has no relationship");
            }

            if (zip.GetEntry(relsName) is not null)
            {
                foreach (var relationship in Read(zip, relsName).Root!.Elements(Rels + "Relationship").Where(r => (string?)r.Attribute("TargetMode") != "External"))
                {
                    var target = new Uri(new Uri("http://p/word/"), (string)relationship.Attribute("Target")!).AbsolutePath.TrimStart('/');
                    Assert.NotNull(zip.GetEntry(Uri.UnescapeDataString(target)));
                }
            }
        }
    }

    private static (string Target, IReadOnlyDictionary<string, (string Target, bool External)> Rels) PartOf(string path, string documentRelationshipId)
    {
        using var zip = ZipFile.OpenRead(path);
        var target = (string)Read(zip, "word/_rels/document.xml.rels").Root!.Elements(Rels + "Relationship").Single(r => (string)r.Attribute("Id")! == documentRelationshipId).Attribute("Target")!;
        target = new Uri(new Uri("http://p/word/"), target).AbsolutePath.TrimStart('/')["word/".Length..];
        var relsName = "word/_rels/" + target + ".rels";
        var rels = zip.GetEntry(relsName) is null
            ? new Dictionary<string, (string, bool)>()
            : Read(zip, relsName).Root!.Elements(Rels + "Relationship").ToDictionary(r => (string)r.Attribute("Id")!, r => ((string)r.Attribute("Target")!, (string?)r.Attribute("TargetMode") == "External"));
        return ("word/" + target, rels);
    }

    [Fact]
    public async Task AnOwnCopyOfAHeaderWithAPictureAndALinkKeepsBothThroughSaveAndReopen()
    {
        using var folder = new TempFolder();
        using var file = DocxFile.Open(WithHeader());
        var document = file.Model;
        var asked = new List<string>();
        var layout = Lay(document, asked);
        var pictures = Pictures(layout);
        var ids = PictureIds(layout);
        var tops = Tops(layout);
        Assert.Equal(2, pictures.Count);
        Assert.Contains("header:rIdH1#rIdImg", ids);

        var result = Editor(document).SetOwnHeaderFooter(1, HeaderFooterKind.Header, HeaderFooterType.Default, true);

        Assert.NotNull(result);
        var copy = SectionList.Of(document)[1].Properties.HeaderFooterReferences.Single(r => r.Kind == HeaderFooterKind.Header).RelationshipId;
        Assert.NotEqual("rIdH1", copy);
        var after = Lay(document);
        Assert.Equal(pictures, Pictures(after));
        Assert.Equal(ids, PictureIds(after));
        Assert.Equal(tops, Tops(after));

        var saved = folder.File("saved.docx");
        await file.SaveAsync(saved, TestContext.Current.CancellationToken);

        AssertNoDanglingIds(saved);
        var (part, rels) = PartOf(saved, copy);
        Assert.NotEqual("word/header1.xml", part);
        Assert.EndsWith("media/image1.png", rels["rIdImg"].Target);
        Assert.Equal(("https://example.org/", true), rels["rIdLink"]);
        using (var zip = ZipFile.OpenRead(saved))
        {
            Assert.Single(zip.Entries, e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        }

        using var reopened = DocxFile.Open(saved);
        var again = Lay(reopened.Model);
        Assert.Equal(pictures, Pictures(again));
        Assert.Equal(tops, Tops(again));
        var hyperlinks = reopened.Model.HeadersFooters[copy].Content.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()).ToList();
        Assert.Equal("rIdLink", Assert.Single(hyperlinks).RelationshipId);
    }

    [Fact]
    public async Task AnEvenCopyOfAHeaderWithAPictureAndALinkKeepsBothThroughSaveAndReopen()
    {
        using var folder = new TempFolder();
        using var file = DocxFile.Open(WithHeader());
        var document = file.Model;
        var layout = Lay(document);
        var pictures = Pictures(layout);
        var tops = Tops(layout);

        Assert.NotNull(Editor(document).SetLeftRightPages(true));

        var even = SectionList.Of(document)[0].Properties.HeaderFooterReferences.Single(r => r.Kind == HeaderFooterKind.Header && r.Type == HeaderFooterType.Even).RelationshipId;
        var after = Lay(document);
        Assert.Equal(pictures, Pictures(after));
        Assert.Equal(tops, Tops(after));

        var saved = folder.File("saved.docx");
        await file.SaveAsync(saved, TestContext.Current.CancellationToken);

        AssertNoDanglingIds(saved);
        var (_, rels) = PartOf(saved, even);
        Assert.EndsWith("media/image1.png", rels["rIdImg"].Target);
        Assert.True(rels["rIdLink"].External);
        using var reopened = DocxFile.Open(saved);
        Assert.True(reopened.Model.Settings!.EvenAndOddHeaders);
        var again = Lay(reopened.Model);
        Assert.Equal(pictures, Pictures(again));
        Assert.Equal(tops, Tops(again));
    }

    [Fact]
    public async Task AnOwnFooterCopyKeepsItsLinkTarget()
    {
        using var folder = new TempFolder();
        using var file = DocxFile.Open(WithHeader());
        var document = file.Model;
        Assert.NotNull(Editor(document).SetOwnHeaderFooter(1, HeaderFooterKind.Footer, HeaderFooterType.Default, true));
        var copy = SectionList.Of(document)[1].Properties.HeaderFooterReferences.Single(r => r.Kind == HeaderFooterKind.Footer).RelationshipId;

        var saved = folder.File("saved.docx");
        await file.SaveAsync(saved, TestContext.Current.CancellationToken);

        AssertNoDanglingIds(saved);
        Assert.Equal(("https://example.org/footer", true), PartOf(saved, copy).Rels["rIdFootLink"]);
    }

    [Fact]
    public async Task ACopyOfACopyAndTheSnapshotWriterKeepTheRelationshipsToo()
    {
        using var folder = new TempFolder();
        using var file = DocxFile.Open(WithHeader());
        var document = file.Model;
        var editor = Editor(document);
        Assert.NotNull(editor.SetOwnHeaderFooter(1, HeaderFooterKind.Header, HeaderFooterType.Default, true));
        Assert.NotNull(editor.SetLeftRightPages(true));
        var evenOfSecond = SectionList.Of(document)[1].Properties.HeaderFooterReferences.Single(r => r.Kind == HeaderFooterKind.Header && r.Type == HeaderFooterType.Even).RelationshipId;

        var copy = folder.File("copy.docx");
        await file.SaveSnapshotAsync(copy, TestContext.Current.CancellationToken);
        var user = folder.File("user.docx");
        await file.SaveAsync(user, TestContext.Current.CancellationToken);

        foreach (var path in new[] { copy, user })
        {
            AssertNoDanglingIds(path);
            Assert.EndsWith("media/image1.png", PartOf(path, evenOfSecond).Rels["rIdImg"].Target);
        }
    }

    [Fact]
    public void TheCopyRecordsWhereItsRelationshipsComeFrom()
    {
        using var file = DocxFile.Open(WithHeader());
        var document = file.Model;
        var original = document.HeadersFooters["rIdH1"];

        Assert.NotNull(Editor(document).SetOwnHeaderFooter(1, HeaderFooterKind.Header, HeaderFooterType.Default, true));

        var copy = SectionList.Of(document)[1].Properties.HeaderFooterReferences.Single(r => r.Kind == HeaderFooterKind.Header).RelationshipId;
        Assert.Same(original, document.HeadersFooters[copy].RelationshipsFrom);
        Assert.Null(original.RelationshipsFrom);
    }
}
