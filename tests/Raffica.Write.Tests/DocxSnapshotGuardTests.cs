using System.IO.Compression;
using System.Text;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

/// <summary>
/// The guard of a recovery copy (<see cref="DocxFile.SaveSnapshotAsync"/>) is lighter than the one of a user save (chunked byte comparison, the main part read back as well-formed XML, no model),
/// so these tests prove that it still refuses what it must, and that a copy holds the same parts as a save of the same model.
/// </summary>
public sealed class DocxSnapshotGuardTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

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

    [Fact]
    public async Task ACopyWhoseMainPartDiffersByOneByteFromWhatWasWrittenIsRefused()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        var target = folder.File("copy.docx");
        var names = folder.Names();
        using var file = DocxFile.Open(path);

        // The package that was just written is altered behind the writer's back: the same XML with one more space, a different byte count.
        file.AfterPackageWritten = temporary =>
        {
            var main = Entries(temporary)["word/document.xml"];
            DocxBuilder.EditInPlace(temporary, "word/document.xml", [.. main, (byte)' ']);
        };
        var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveSnapshotAsync(target, Cancel));

        Assert.Equal(DocxSaveReason.ModelNotReadable, error.Reason);
        Assert.Equal("/word/document.xml", error.PartName);
        Assert.False(File.Exists(target));
        Assert.Equal(names, folder.Names());
    }

    [Fact]
    public async Task ACopyWhoseMainPartDiffersInOneByteOfTheSameLengthIsRefused()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        var target = folder.File("copy.docx");
        using var file = DocxFile.Open(path);

        file.AfterPackageWritten = temporary =>
        {
            var main = Entries(temporary)["word/document.xml"];
            main[main.Length / 2] ^= 1;
            DocxBuilder.EditInPlace(temporary, "word/document.xml", main);
        };
        var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveSnapshotAsync(target, Cancel));

        Assert.Equal(DocxSaveReason.ModelNotReadable, error.Reason);
        Assert.Equal("/word/document.xml", error.PartName);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task ACopyWhoseSmallerModelPartWasAlteredIsRefusedToo()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        var target = folder.File("copy.docx");
        using var file = DocxFile.Open(path);

        file.AfterPackageWritten = temporary => DocxBuilder.EditInPlace(temporary, "word/footer1.xml", null);
        var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveSnapshotAsync(target, Cancel));

        Assert.Equal(DocxSaveReason.ModelNotReadable, error.Reason);
        Assert.False(File.Exists(target));
    }

    [Theory]
    [InlineData("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p></w:body></w:document>")]
    [InlineData("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">")]
    [InlineData("not xml at all")]
    [InlineData("")]
    [InlineData("<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>")]
    [InlineData("<document xmlns=\"urn:other\"/>")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE w:document [<!ENTITY x \"y\">]><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>")]
    public void TheLightReadBackRefusesAMainPartThatIsNotAWellFormedDocument(string xml)
    {
        var error = Assert.Throws<DocxSaveException>(() => DocxFile.ReadBackWellFormed("/word/document.xml", Encoding.UTF8.GetBytes(xml)));

        Assert.Equal(DocxSaveReason.ModelNotReadable, error.Reason);
        Assert.Equal("/word/document.xml", error.PartName);
    }

    [Fact]
    public void TheLightReadBackAcceptsAWellFormedDocument()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p/></w:body></w:document>";

        DocxFile.ReadBackWellFormed("/word/document.xml", Encoding.UTF8.GetBytes(xml));
    }

    [Fact]
    public async Task ACopyHoldsTheSamePartsAsASaveOfTheSameModelBeforeAndAfterEdits()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        using var file = DocxFile.Open(path);
        var editor = new TextEditor(file.Model, (_, _) => ProjectionHost.Plain);
        var paragraphs = file.Model.Body.Blocks.OfType<Paragraph>().Where(p => p.Inlines.OfType<Run>().Any(r => r.Content.OfType<TextContent>().Any())).Take(3).ToList();

        for (var round = 0; round < 4; round++)
        {
            if (round > 0)
            {
                var paragraph = paragraphs[round % paragraphs.Count];
                var at = new TextPosition(StoryRef.Body, paragraph, 2);
                editor.InsertText(new TextRange(at, at), round % 2 == 0 ? "typed " : "\n");
            }

            var copy = folder.File($"copy{round}.docx");
            var save = folder.File($"save{round}.docx");
            await file.SaveSnapshotAsync(copy, Cancel);
            await file.SaveAsync(save, Cancel);

            var copied = Entries(copy);
            var saved = Entries(save);
            Assert.Equal(saved.Keys.Order(StringComparer.Ordinal), copied.Keys.Order(StringComparer.Ordinal));
            foreach (var (name, bytes) in saved)
            {
                Assert.True(bytes.AsSpan().SequenceEqual(copied[name]), $"round {round}: part {name} differs between the copy and a save");
            }
        }
    }
}
