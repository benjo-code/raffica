using System.IO.Compression;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class NotesPartCreationTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static byte[] Blank() => BlankDocument.Create(PaperSize.A4);

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

    private static void AssertSeparators(NoteCollection notes)
    {
        Assert.Equal(2, notes.Notes.Count);
        Assert.Equal(NoteKind.Separator, notes.Find(-1)?.Kind);
        Assert.Equal(NoteKind.ContinuationSeparator, notes.Find(0)?.Kind);
    }

    private static void AssertSettingsName(DocxFile file, string pr, string item)
    {
        var holder = file.Model.Settings!.Properties.Get(W + pr);
        Assert.NotNull(holder);
        Assert.Equal(["-1", "0"], holder.Elements(W + item).Select(e => (string?)e.Attribute(W + "id")).ToArray());
    }

    [Fact]
    public void ABlankDocumentHasNoFootnotesPart()
    {
        using var file = DocxFile.Open(Blank());

        Assert.Null(file.Model.Footnotes);
        Assert.Null(file.Model.Endnotes);
    }

    [Fact]
    public async Task EnsureMakesSeparatorsAndTheSaveWritesAValidPart()
    {
        using var folder = new TempFolder();
        var target = folder.File("notes.docx");
        using (var file = DocxFile.Open(Blank()))
        {
            var footnotes = NotesParts.Ensure(file.Model, NoteType.Footnote);
            var endnotes = NotesParts.Ensure(file.Model, NoteType.Endnote);
            AssertSeparators(footnotes);
            AssertSeparators(endnotes);
            Assert.Same(footnotes, file.Model.Footnotes);
            await file.SaveAsync(target, Cancel);
        }

        using var reopened = DocxFile.Open(target);
        AssertSeparators(reopened.Model.Footnotes!);
        AssertSeparators(reopened.Model.Endnotes!);
        AssertSettingsName(reopened, "footnotePr", "footnote");
        AssertSettingsName(reopened, "endnotePr", "endnote");
        var entries = Entries(target);
        Assert.Contains("word/footnotes.xml", entries.Keys);
        Assert.Contains("word/endnotes.xml", entries.Keys);
        var types = XDocument.Load(new MemoryStream(entries["[Content_Types].xml"])).Root!.Elements().Select(e => (string?)e.Attribute("PartName")).ToList();
        Assert.Contains("/word/footnotes.xml", types);
        Assert.Contains("/word/endnotes.xml", types);
        var rels = System.Text.Encoding.UTF8.GetString(entries["word/_rels/document.xml.rels"]);
        Assert.Contains("relationships/footnotes\"", rels);
        Assert.Contains("relationships/endnotes\"", rels);
    }

    [Fact]
    public async Task EnsureOnADocumentWithSeparatorsOnlyReusesThem()
    {
        using var folder = new TempFolder();
        var first = folder.File("first.docx");
        var second = folder.File("second.docx");
        using (var file = DocxFile.Open(Blank()))
        {
            _ = NotesParts.Ensure(file.Model, NoteType.Footnote);
            await file.SaveAsync(first, Cancel);
        }

        using var again = DocxFile.Open(first);
        var before = again.Model.Footnotes;
        var notes = NotesParts.Ensure(again.Model, NoteType.Footnote);

        Assert.Same(before, notes);
        AssertSeparators(notes);
        Assert.Single(again.Model.Settings!.Properties.Children, c => c.Name == W + "footnotePr");
        Assert.Equal(2, again.Model.Settings.Properties.Get(W + "footnotePr")!.Elements(W + "footnote").Count());
        await again.SaveAsync(second, Cancel);
        using var reopened = DocxFile.Open(second);
        AssertSeparators(reopened.Model.Footnotes!);
    }

    [Fact]
    public async Task TheCreatedPartPassesTheSaveGuardAndTheRecoveryCopy()
    {
        using var folder = new TempFolder();
        var saved = folder.File("saved.docx");
        var copy = folder.File("copy.docx");
        using var file = DocxFile.Open(Blank());
        _ = NotesParts.Ensure(file.Model, NoteType.Footnote);

        await file.SaveAsync(saved, Cancel);
        await file.SaveSnapshotAsync(copy, Cancel);

        foreach (var path in new[] { saved, copy })
        {
            using var reopened = DocxFile.Open(path);
            AssertSeparators(reopened.Model.Footnotes!);
            AssertSettingsName(reopened, "footnotePr", "footnote");
        }

        Assert.Equal(Entries(saved).Keys.Order(StringComparer.Ordinal), Entries(copy).Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task NoOtherPartChangedByteForByte()
    {
        using var folder = new TempFolder();
        var original = folder.File("blank.docx");
        var target = folder.File("notes.docx");
        await File.WriteAllBytesAsync(original, Blank(), Cancel);
        using (var file = DocxFile.Open(original))
        {
            _ = NotesParts.Ensure(file.Model, NoteType.Footnote);
            await file.SaveAsync(target, Cancel);
        }

        var before = Entries(original);
        var after = Entries(target);
        var rewritten = new HashSet<string>(StringComparer.Ordinal) { "word/document.xml", "word/styles.xml", "word/numbering.xml", "word/settings.xml", "word/footnotes.xml", "word/_rels/document.xml.rels", "[Content_Types].xml" };
        foreach (var (name, bytes) in before.Where(p => !rewritten.Contains(p.Key)))
        {
            Assert.Equal(bytes, after[name]);
        }

        Assert.Equal(["word/footnotes.xml"], after.Keys.Except(before.Keys).ToArray());
    }
}
