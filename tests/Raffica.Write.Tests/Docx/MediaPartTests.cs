using System.IO.Compression;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class MediaPartTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string ImageType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    private static readonly string[] s_types = ["image/png", "IMAGE/JPEG", "image/gif", "image/bmp", "image/tiff"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // A valid 1 x 1 PNG, then variants that differ by one byte of the (ignored) trailing data.
    private static byte[] Png(byte variant = 0)
    {
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==").ToList();
        bytes.Add(variant);
        return [.. bytes];
    }

    private static Dictionary<string, byte[]> Entries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            entries[entry.FullName] = memory.ToArray();
        }

        return entries;
    }

    private static Paragraph PictureParagraph(string relationshipId)
    {
        var drawing = new XElement(
            W + "drawing",
            new XElement(
                Wp + "inline",
                new XElement(Wp + "extent", new XAttribute("cx", 9525), new XAttribute("cy", 9525)),
                new XElement(A + "graphic", new XElement(A + "blip", new XAttribute(R + "embed", relationshipId)))));
        var run = new Run();
        run.Content.Add(new Picture(drawing));
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(run);
        return paragraph;
    }

    private static DocxFile Blank() => DocxFile.Open(BlankDocument.Create(PaperSize.A4));

    private static List<(string Id, string Type, string Target, bool External)> Relationships(Dictionary<string, byte[]> entries) =>
        XDocument.Load(new MemoryStream(entries["word/_rels/document.xml.rels"])).Root!.Elements()
            .Select(e => ((string)e.Attribute("Id")!, (string)e.Attribute("Type")!, (string)e.Attribute("Target")!, (string?)e.Attribute("TargetMode") == "External")).ToList();

    private static void AssertNoDangling(string path)
    {
        var entries = Entries(path);
        var rels = Relationships(entries);
        foreach (var (_, _, target, _) in rels.Where(r => r.Type == ImageType && !r.External))
        {
            Assert.Contains(target.StartsWith('/') ? target[1..] : "word/" + target, entries.Keys);
        }

        var main = XDocument.Load(new MemoryStream(entries["word/document.xml"]));
        foreach (var embed in main.Descendants().Attributes(R + "embed"))
        {
            Assert.Contains(rels, r => r.Id == embed.Value && r.Type == ImageType);
        }
    }

    [Fact]
    public async Task ANewPictureIsWrittenAsAMediaPartWithItsRelationship()
    {
        using var folder = new TempFolder();
        var target = folder.File("picture.docx");
        var png = Png();
        using (var file = Blank())
        {
            var media = file.Model.Media.Add(png, "image/png");
            file.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));
            await file.SaveAsync(target, Cancel);
        }

        var entries = Entries(target);
        var part = Assert.Single(entries.Keys, k => k.StartsWith("word/media/", StringComparison.Ordinal));
        Assert.EndsWith(".png", part);
        Assert.Equal(png, entries[part]);
        var rel = Assert.Single(Relationships(entries), r => r.Type == ImageType);
        Assert.Equal(part["word/".Length..], rel.Target);
        var types = XDocument.Load(new MemoryStream(entries["[Content_Types].xml"])).Root!.Elements().ToList();
        Assert.Contains(types, e => (string?)e.Attribute("Extension") == "png" && (string?)e.Attribute("ContentType") == "image/png");
        AssertNoDangling(target);

        using var reopened = DocxFile.Open(target);
        var picture = reopened.Model.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).SelectMany(r => r.Content).OfType<Picture>().Single();
        Assert.Equal(rel.Id, picture.EmbedRelationshipId);
    }

    [Fact]
    public async Task AnUndonePictureLeavesNoMediaPartInTheNextSave()
    {
        using var folder = new TempFolder();
        var first = folder.File("with.docx");
        var second = folder.File("without.docx");
        using var file = Blank();
        var media = file.Model.Media.Add(Png(), "image/png");
        var paragraph = PictureParagraph(media.RelationshipId);
        file.Model.Body.Blocks.Add(paragraph);
        await file.SaveAsync(first, Cancel);

        file.Model.Body.Blocks.Remove(paragraph);
        await file.SaveAsync(second, Cancel);

        Assert.Single(Entries(first).Keys, k => k.StartsWith("word/media/", StringComparison.Ordinal));
        var entries = Entries(second);
        Assert.DoesNotContain(entries.Keys, k => k.StartsWith("word/media/", StringComparison.Ordinal));
        Assert.DoesNotContain(Relationships(entries), r => r.Type == ImageType);
        AssertNoDangling(second);

        // The redo puts the paragraph back: the bytes are still in the store, so the picture comes back with it.
        file.Model.Body.Blocks.Add(paragraph);
        var third = folder.File("again.docx");
        await file.SaveAsync(third, Cancel);
        Assert.Single(Entries(third).Keys, k => k.StartsWith("word/media/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheRelationshipIdIsNeverUsedTwice()
    {
        using var folder = new TempFolder();
        var source = folder.CopyThesis();
        using var file = DocxFile.Open(source);
        var existing = Relationships(Entries(source)).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var a = file.Model.Media.Add(Png(1), "image/png");
        var b = file.Model.Media.Add(Png(2), "image/png");
        Assert.True(file.Model.Media.Remove(b));
        var c = file.Model.Media.Add(Png(3), "image/png");

        Assert.Equal(3, new[] { a, b, c }.Select(m => m.RelationshipId).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(new[] { a, b, c }, m => existing.Contains(m.RelationshipId));

        var target = folder.File("ids.docx");
        file.Model.Body.Blocks.Add(PictureParagraph(a.RelationshipId));
        file.Model.Body.Blocks.Add(PictureParagraph(c.RelationshipId));
        await file.SaveAsync(target, Cancel);
        var rels = Relationships(Entries(target));
        Assert.Equal(rels.Count, rels.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
        AssertNoDangling(target);
    }

    [Fact]
    public async Task ANewPictureNeverReplacesAMediaPartTheFileHas()
    {
        using var folder = new TempFolder();
        var source = folder.File("one.docx");
        var target = folder.File("two.docx");
        using (var file = Blank())
        {
            var media = file.Model.Media.Add(Png(1), "image/png");
            file.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));
            await file.SaveAsync(source, Cancel);
        }

        var before = Entries(source);
        var existingPart = before.Keys.Single(k => k.StartsWith("word/media/", StringComparison.Ordinal));
        using (var again = DocxFile.Open(source))
        {
            var media = again.Model.Media.Add(Png(2), "image/png");
            again.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));
            await again.SaveAsync(target, Cancel);
        }

        var after = Entries(target);
        Assert.Equal(before[existingPart], after[existingPart]);
        Assert.Equal(2, after.Keys.Count(k => k.StartsWith("word/media/", StringComparison.Ordinal)));
        AssertNoDangling(target);
    }

    [Fact]
    public async Task TwoSavesInARowAndASaveAsWriteOnePartPerPicture()
    {
        using var folder = new TempFolder();
        var first = folder.File("a.docx");
        var second = folder.File("b.docx");
        var elsewhere = folder.File("c.docx");
        using var file = Blank();
        var media = file.Model.Media.Add(Png(), "image/png");
        file.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));

        await file.SaveAsync(first, Cancel);
        await file.SaveAsync(second, Cancel);
        await file.SaveAsAsync(elsewhere, replaceExisting: false, Cancel);

        foreach (var path in new[] { first, second, elsewhere })
        {
            Assert.Single(Entries(path).Keys, k => k.StartsWith("word/media/", StringComparison.Ordinal));
            AssertNoDangling(path);
        }

        Assert.Equal(Entries(first)["word/document.xml"], Entries(second)["word/document.xml"]);
    }

    [Fact]
    public async Task TheGuardAllowsTheCreatedPartAndNothingElse()
    {
        using var folder = new TempFolder();
        var target = folder.File("guard.docx");

        foreach (var (name, edit) in new (string Name, Action<string> Edit)[]
        {
            ("/word/extra.bin", t => DocxBuilder.EditInPlace(t, "word/extra.bin", [1])),
            ("/word/media/other.png", t => DocxBuilder.EditInPlace(t, "word/media/other.png", [1, 2])),
        })
        {
            using var file = Blank();
            var media = file.Model.Media.Add(Png(), "image/png");
            file.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));
            file.AfterPackageWritten = edit;
            var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveAsync(target, Cancel));
            Assert.Equal(DocxSaveReason.PartsChanged, error.Reason);
            Assert.Equal(name, error.PartName);
            Assert.False(File.Exists(target));
        }

        // A written picture whose bytes change after the write is not what the model said.
        using (var file = Blank())
        {
            var media = file.Model.Media.Add(Png(), "image/png");
            file.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));
            file.AfterPackageWritten = t => DocxBuilder.EditInPlace(t, "word/media/image1.png", [9, 9]);
            var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveAsync(target, Cancel));
            Assert.Equal(DocxSaveReason.ModelNotReadable, error.Reason);
        }

        // An existing media part changed by the save is refused as well.
        var source = folder.File("source.docx");
        using (var file = Blank())
        {
            var media = file.Model.Media.Add(Png(1), "image/png");
            file.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));
            await file.SaveAsync(source, Cancel);
        }

        using (var file = DocxFile.Open(source))
        {
            var media = file.Model.Media.Add(Png(2), "image/png");
            file.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));
            var existing = Entries(source).Keys.Single(k => k.StartsWith("word/media/", StringComparison.Ordinal));
            file.AfterPackageWritten = t => DocxBuilder.EditInPlace(t, existing, [7]);
            var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveAsync(target, Cancel));
            Assert.Equal(DocxSaveReason.PartsChanged, error.Reason);
            Assert.Equal("/" + existing, error.PartName);
        }

        // The honest save passes.
        using (var file = Blank())
        {
            var media = file.Model.Media.Add(Png(), "image/png");
            file.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));
            await file.SaveAsync(target, Cancel);
        }

        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task TheRecoveryCopyCarriesThePicture()
    {
        using var folder = new TempFolder();
        var copy = folder.File("copy.docx");
        var saved = folder.File("saved.docx");
        var png = Png();
        using var file = Blank();
        var media = file.Model.Media.Add(png, "image/png");
        file.Model.Body.Blocks.Add(PictureParagraph(media.RelationshipId));

        await file.SaveSnapshotAsync(copy, Cancel);
        await file.SaveAsync(saved, Cancel);

        var entries = Entries(copy);
        Assert.Equal(png, entries[Assert.Single(entries.Keys, k => k.StartsWith("word/media/", StringComparison.Ordinal))]);
        Assert.Equal(Entries(saved).Keys.Order(StringComparer.Ordinal), entries.Keys.Order(StringComparer.Ordinal));
        AssertNoDangling(copy);

        // The lighter guard compares the picture byte for byte as well.
        file.AfterPackageWritten = t => DocxBuilder.EditInPlace(t, "word/media/image1.png", [1]);
        var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveSnapshotAsync(folder.File("bad.docx"), Cancel));
        Assert.Equal(DocxSaveReason.ModelNotReadable, error.Reason);
    }

    [Fact]
    public async Task NoDanglingRelationshipAfterAnySave()
    {
        using var folder = new TempFolder();
        using var file = DocxFile.Open(folder.CopyThesis());
        var kept = file.Model.Media.Add(Png(1), "image/png");
        var undone = file.Model.Media.Add(Png(2), "image/jpeg");
        var later = file.Model.Media.Add(Png(3), "image/gif");
        var keptParagraph = PictureParagraph(kept.RelationshipId);
        var undoneParagraph = PictureParagraph(undone.RelationshipId);
        file.Model.Body.Blocks.Add(keptParagraph);
        file.Model.Body.Blocks.Add(undoneParagraph);
        file.Model.Body.Blocks.Remove(undoneParagraph);
        file.Model.Body.Blocks.Add(PictureParagraph(later.RelationshipId));
        file.Model.Body.Blocks.Add(undoneParagraph);

        var one = folder.File("one.docx");
        var two = folder.File("two.docx");
        await file.SaveAsync(one, Cancel);
        file.Model.Body.Blocks.Remove(undoneParagraph);
        await file.SaveAsync(two, Cancel);
        await file.SaveSnapshotAsync(folder.File("three.docx"), Cancel);

        foreach (var path in new[] { one, two, folder.File("three.docx") })
        {
            AssertNoDangling(path);
            using var reopened = DocxFile.Open(path);
            Assert.NotNull(reopened.Model);
        }

        Assert.Equal(3, Entries(one).Keys.Count(k => k.StartsWith("word/media/", StringComparison.Ordinal)));
        Assert.Equal(2, Entries(two).Keys.Count(k => k.StartsWith("word/media/", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheStoreRefusesWhatItCannotHoldWithoutTellingTheBytes()
    {
        var store = new MediaStore();

        Assert.Throws<ArgumentException>(() => store.Add(Png(), "text/plain"));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Add(ReadOnlyMemory<byte>.Empty, "image/png"));
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => store.Add(new byte[MediaStore.MaxEntryBytes + 1], "image/png"));
        Assert.DoesNotContain("PNG", error.Message, StringComparison.Ordinal);
        Assert.Empty(store.Entries);
        Assert.Equal(["png", "jpeg", "gif", "bmp", "tiff"], s_types.Select(MediaStore.ExtensionOf).ToArray());
    }

    [Fact]
    public async Task AReadFromAnotherThreadSeesAnEntryOnceAddReturned()
    {
        var store = new MediaStore();
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        var writer = Task.Run(
            () =>
            {
                for (var i = 0; i < 200; i++)
                {
                    ids.Add(store.Add(Png((byte)i), "image/png").RelationshipId);
                }
            },
            Cancel);
        var reader = Task.Run(
            () =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    foreach (var id in ids.ToArray())
                    {
                        Assert.NotNull(store.TryGet(id));
                    }
                }
            },
            Cancel);

        await Task.WhenAll(writer, reader);
        Assert.Equal(200, store.Entries.Count);
        Assert.Equal(200, store.Entries.Select(e => e.Ref.RelationshipId).Distinct(StringComparer.Ordinal).Count());
    }
}
