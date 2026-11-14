using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests.Documents;

public sealed class PackageImageSourceTests
{
    private const string RelsHead = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">";
    private const string Image = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Rel(string id, string type, string target, bool external = false) =>
        $"<Relationship Id=\"{id}\" Type=\"{type}\" Target=\"{target}\"{(external ? " TargetMode=\"External\"" : string.Empty)}/>";

    private static byte[] Package(string main = "word/document.xml", string? documentRels = null, bool packageRels = true)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] content)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(content);
            }

            if (packageRels)
            {
                Add("_rels/.rels", Bytes(RelsHead + Rel("rId1", Office, "/" + main) + "</Relationships>"));
            }

            Add(main, Bytes("<w:document/>"));
            var folder = main[..(main.LastIndexOf('/') + 1)];
            Add(folder + "_rels/" + main[(main.LastIndexOf('/') + 1)..] + ".rels", Bytes(documentRels ?? (RelsHead
                + Rel("rId1", Image, "media/body.png")
                + Rel("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header", "header1.xml")
                + Rel("rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes", "footnotes.xml")
                + Rel("rId4", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes", "endnotes.xml")
                + Rel("rId5", Image, "https://example.org/remote.png", external: true)
                + Rel("rId6", Image, "media/with%20space.png")
                + Rel("rId7", Image, "../word/media/body.png")
                + "</Relationships>")));
            Add(folder + "header1.xml", Bytes("<w:hdr/>"));
            Add(folder + "_rels/header1.xml.rels", Bytes(RelsHead + Rel("rId1", Image, "media/header.png") + "</Relationships>"));
            Add(folder + "footnotes.xml", Bytes("<w:footnotes/>"));
            Add(folder + "_rels/footnotes.xml.rels", Bytes(RelsHead + Rel("rId1", Image, "media/foot.png") + "</Relationships>"));
            Add(folder + "endnotes.xml", Bytes("<w:endnotes/>"));
            Add(folder + "_rels/endnotes.xml.rels", Bytes(RelsHead + Rel("rId1", Image, "media/end.png") + "</Relationships>"));
            Add(folder + "media/body.png", Bytes("body"));
            Add(folder + "media/header.png", Bytes("header"));
            Add(folder + "media/foot.png", Bytes("foot"));
            Add(folder + "media/end.png", Bytes("end"));
            Add(folder + "media/with space.png", Bytes("spaced"));
        }

        return memory.ToArray();
    }

    private static string Text(ReadOnlyMemory<byte> bytes) => Encoding.UTF8.GetString(bytes.Span);

    [Fact]
    public void EachStoryReadsThePictureOfItsOwnPartAndTheSameRelationshipIdsDoNotCollide()
    {
        using var source = new PackageImageSource(Package());

        Assert.Equal("body", Text(source.Read("document#rId1")));
        Assert.Equal("header", Text(source.Read("header:rId2#rId1")));
        Assert.Equal("foot", Text(source.Read("footnotes#rId1")));
        Assert.Equal("end", Text(source.Read("endnotes#rId1")));
    }

    [Fact]
    public void AnIdWithoutAStoryIsTheBodysAndTargetsAreFoldedAndUnescaped()
    {
        using var source = new PackageImageSource(Package());

        Assert.Equal("body", Text(source.Read("rId1")));
        Assert.Equal("spaced", Text(source.Read("document#rId6")));
        Assert.Equal("body", Text(source.Read("document#rId7")));
    }

    [Fact]
    public void TheMainPartIsFoundByTheOfficeDocumentRelationshipAndAFallbackWhenThereIsNone()
    {
        using (var moved = new PackageImageSource(Package(main: "word2/main.xml")))
        {
            Assert.Equal("body", Text(moved.Read("document#rId1")));
            Assert.Equal("header", Text(moved.Read("header:rId2#rId1")));
        }

        using var fallback = new PackageImageSource(Package(packageRels: false));
        Assert.Equal("body", Text(fallback.Read("document#rId1")));
    }

    [Theory]
    [InlineData("document#rId99")]
    [InlineData("document#rId5")]
    [InlineData("header:rId99#rId1")]
    [InlineData("header:rId2#rId9")]
    [InlineData("footnotes#rId9")]
    public void AMissingOrExternalRelationshipThrowsInsteadOfFetchingAnything(string id)
    {
        using var source = new PackageImageSource(Package());
        Assert.Throws<FileNotFoundException>(() => source.Read(id));
    }

    [Fact]
    public void ARelationshipToAMissingPartThrows()
    {
        var rels = RelsHead + Rel("rId1", Image, "media/nothing.png") + "</Relationships>";
        using var source = new PackageImageSource(Package(documentRels: rels));
        Assert.Throws<FileNotFoundException>(() => source.Read("document#rId1"));
    }

    [Fact]
    public void ADamagedRelationshipsPartAndANonPackageThrowInvalidData()
    {
        using (var damaged = new PackageImageSource(Package(documentRels: "<<< not xml")))
        {
            Assert.Throws<InvalidDataException>(() => damaged.Read("document#rId1"));
        }

        using var notZip = new PackageImageSource(Bytes("not a package"));
        Assert.Throws<InvalidDataException>(() => notZip.Read("document#rId1"));
    }

    [Fact]
    public void ADoctypeInTheRelationshipsIsRefused()
    {
        var rels = "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x \"y\">]>" + RelsHead[RelsHead.IndexOf("<Relationships", StringComparison.Ordinal)..] + Rel("rId1", Image, "media/body.png") + "</Relationships>";
        using var source = new PackageImageSource(Package(documentRels: rels));
        Assert.Throws<InvalidDataException>(() => source.Read("document#rId1"));
    }

    [Fact]
    public void ReadingAfterDisposeThrowsAndDisposeTwiceIsFine()
    {
        var source = new PackageImageSource(Package());
        Assert.Equal("body", Text(source.Read("document#rId1")));
        source.Dispose();
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.Read("document#rId1"));
    }

    [Fact]
    public void ManyThreadsMayAskAtOnce()
    {
        using var source = new PackageImageSource(Package());
        var ids = new[] { "document#rId1", "header:rId2#rId1", "footnotes#rId1", "endnotes#rId1" };
        var expected = new[] { "body", "header", "foot", "end" };

        Parallel.For(0, 400, i => Assert.Equal(expected[i % 4], Text(source.Read(ids[i % 4]))));
    }

    [Fact]
    public void ThePictureComesFromTheSnapshotNotFromTheFile()
    {
        // A real document with pictures: the source holds the bytes the file was read from, so it reads on after the file is gone.
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "thesis.docx");
        File.Copy(RepoPaths.Corpus("thesis"), path);
        var package = File.ReadAllBytes(path);
        string id;
        byte[] expected;
        using (var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relationship = System.Xml.Linq.XDocument.Load(zip.GetEntry("word/_rels/document.xml.rels")!.Open()).Root!.Elements(ns + "Relationship")
                .First(r => ((string?)r.Attribute("Type"))!.EndsWith("/image", StringComparison.Ordinal));
            id = (string)relationship.Attribute("Id")!;
            var target = (string)relationship.Attribute("Target")!;
            using var entry = zip.GetEntry(target.StartsWith('/') ? target[1..] : "word/" + target)!.Open();
            using var copy = new MemoryStream();
            entry.CopyTo(copy);
            expected = copy.ToArray();
        }

        using var source = new PackageImageSource(File.ReadAllBytes(path));
        File.Delete(path);

        Assert.True(expected.AsSpan().SequenceEqual(source.Read("document#" + id).Span));
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("raffica-write-images-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
