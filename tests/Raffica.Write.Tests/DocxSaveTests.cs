using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using Raffica.Core.Saving;
using Raffica.Write.Docx;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class DocxSaveTests
{
    private static readonly XNamespace W = XmlSnippets.W;

    private static readonly string[] s_modelParts =
    [
        "/word/document.xml", "/word/styles.xml", "/word/numbering.xml", "/word/settings.xml", "/word/footnotes.xml", "/word/endnotes.xml",
        "/word/header1.xml", "/word/header2.xml", "/word/footer1.xml", "/word/footer2.xml",
    ];

    private static readonly string[] s_commentChanges = ["/word/document.xml", "/word/styles.xml", "/[Content_Types].xml", "/word/_rels/document.xml.rels"];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static XElement Parse(byte[] bytes) => XElement.Parse(Encoding.UTF8.GetString(bytes));

    private static Story NewStory(string root) => new(new OoxmlRoot(W + root, [new XAttribute(XNamespace.Xmlns + "w", XmlSnippets.W)]));

    private static Paragraph TextParagraph(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(run);
        return paragraph;
    }

    [Fact]
    public async Task SavedWithoutChangesKeepsEveryOtherPartByteIdentical()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        var saved = folder.File("saved.docx");
        using (var file = DocxFile.Open(path))
        {
            await file.SaveAsync(saved, Cancel);
        }

        var diff = PartComparer.Diff(path, saved);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.NotEmpty(diff.Changed);
        foreach (var name in diff.Changed)
        {
            Assert.Contains(name, s_modelParts);
            var before = Parse(PartComparer.ReadPart(path, name));
            var after = Parse(PartComparer.ReadPart(saved, name));
            Assert.True(XmlEquivalence.AreEqual(before, after), $"{name}: {XmlEquivalence.Explain(before, after)}");
        }
    }

    [Fact]
    public async Task PartsNotLoadedStayByteIdentical()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        var saved = folder.File("saved.docx");
        using (var file = DocxFile.Open(path, DocxParts.Document))
        {
            await file.SaveAsync(saved, Cancel);
        }

        var diff = PartComparer.Diff(path, saved);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Equal(["/word/document.xml"], diff.Changed);
    }

    [Fact]
    public async Task ModelChangeRoundTrips()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        var saved = folder.File("saved.docx");
        XElement expected;
        using (var file = DocxFile.Open(path))
        {
            var model = file.Model;
            model.Body.Blocks.Add(TextParagraph("Appended by the test."));
            model.Styles!.Find("Heading2")!.Name = "heading 2 renamed";
            var table = model.Body.Blocks.OfType<Table>().First();
            var rows = table.Rows.Count;
            table.Rows.RemoveAt(1);
            Assert.Equal(rows - 1, table.Rows.Count);
            var note = new Note(101, NoteKind.Normal, NewStory("footnote"));
            note.Content.Blocks.Add(TextParagraph("A new note"));
            model.Footnotes!.Notes.Add(note);
            expected = ModelSnapshot.Of(model);
            await file.SaveAsync(saved, Cancel);
        }

        using var reopened = DocxFile.Open(saved);
        var actual = ModelSnapshot.Of(reopened.Model);
        Assert.True(XmlEquivalence.AreEqual(expected, actual), XmlEquivalence.Explain(expected, actual));
        Assert.Equal("heading 2 renamed", reopened.Model.Styles!.Find("Heading2")!.Name);
        Assert.Equal(103, reopened.Model.Footnotes!.Notes.Count);
        using var package = WordprocessingDocument.Open(saved, false);
        Assert.NotNull(package.MainDocumentPart);
    }

    [Fact]
    public async Task ACommentIsAddedToADocumentWithoutACommentsPart()
    {
        using var folder = new TempFolder();
        var path = folder.File("simple.docx");
        DocxBuilder.Simple(path);
        var saved = folder.File("saved.docx");
        XElement expected;
        using (var file = DocxFile.Open(path))
        {
            var model = file.Model;
            Assert.Null(model.Comments);
            var comments = new CommentCollection(new OoxmlRoot(W + "comments", [new XAttribute(XNamespace.Xmlns + "w", XmlSnippets.W)]));
            var comment = new Comment(0, NewStory("comment")) { Author = "Ann", Initials = "A", DateText = "2026-11-13T10:00:00Z" };
            comment.Content.Blocks.Add(TextParagraph("Please check"));
            comments.Comments.Add(comment);
            model.Comments = comments;
            var target = (Paragraph)model.Body.Blocks[0];
            var reference = new Run();
            reference.Content.Add(new CommentReference(0));
            target.Inlines.Insert(0, new CommentRangeStart(0));
            target.Inlines.Add(new CommentRangeEnd(0));
            target.Inlines.Add(reference);
            expected = ModelSnapshot.Of(model);
            await file.SaveAsync(saved, Cancel);
        }

        var diff = PartComparer.Diff(path, saved);
        Assert.Equal(["/word/comments.xml"], diff.Added);
        Assert.Empty(diff.Removed);
        Assert.All(diff.Changed, name => Assert.Contains(name, s_commentChanges));
        var contentTypes = Encoding.UTF8.GetString(PartComparer.ReadPart(saved, "[Content_Types].xml"));
        Assert.Contains("/word/comments.xml", contentTypes, StringComparison.Ordinal);
        Assert.Contains("wordprocessingml.comments+xml", contentTypes, StringComparison.Ordinal);
        var rels = Encoding.UTF8.GetString(PartComparer.ReadPart(saved, "word/_rels/document.xml.rels"));
        Assert.Contains("relationships/comments", rels, StringComparison.Ordinal);
        using (var package = WordprocessingDocument.Open(saved, false))
        {
            Assert.NotNull(package.MainDocumentPart!.WordprocessingCommentsPart);
        }

        using var reopened = DocxFile.Open(saved);
        Assert.Single(reopened.Model.Comments!.Comments);
        var actual = ModelSnapshot.Of(reopened.Model);
        Assert.True(XmlEquivalence.AreEqual(expected, actual), XmlEquivalence.Explain(expected, actual));
    }

    [Fact]
    public async Task ADoneCommentCreatesTheExtendedPartAndItSurvivesLaterSaves()
    {
        using var folder = new TempFolder();
        var path = folder.File("simple.docx");
        DocxBuilder.Simple(path);
        var first = folder.File("first.docx");
        using (var file = DocxFile.Open(path))
        {
            var comments = new CommentCollection(new OoxmlRoot(W + "comments", [new XAttribute(XNamespace.Xmlns + "w", XmlSnippets.W)]));
            var comment = new Comment(0, NewStory("comment")) { Author = "Ann" };
            comment.Content.Blocks.Add(TextParagraph("Done already"));
            comments.Comments.Add(comment);
            comments.EnsureParaId(comment);
            comment.Done = true;
            file.Model.Comments = comments;
            await file.SaveAsync(first, Cancel);
        }

        Assert.Contains("/word/commentsExtended.xml", PartComparer.Diff(path, first).Added);
        var second = folder.File("second.docx");
        using (var file = DocxFile.Open(first))
        {
            Assert.True(file.Model.Comments!.Comments[0].Done);
            await file.SaveAsync(second, Cancel);
        }

        var diff = PartComparer.Diff(first, second);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        using var reopened = DocxFile.Open(second);
        Assert.True(reopened.Model.Comments!.Comments[0].Done);
    }

    [Fact]
    public async Task AnExtendedPartThatWasReadIsWrittenBackWhenNoCommentHasStateAnyMore()
    {
        using var folder = new TempFolder();
        var path = folder.File("simple.docx");
        DocxBuilder.Simple(path);
        var first = folder.File("first.docx");
        using (var file = DocxFile.Open(path))
        {
            var comments = new CommentCollection(new OoxmlRoot(W + "comments", [new XAttribute(XNamespace.Xmlns + "w", XmlSnippets.W)]));
            var comment = new Comment(0, NewStory("comment")) { Author = "Ann" };
            comment.Content.Blocks.Add(TextParagraph("c"));
            comments.Comments.Add(comment);
            comments.EnsureParaId(comment);
            comment.Done = true;
            file.Model.Comments = comments;
            await file.SaveAsync(first, Cancel);
        }

        var second = folder.File("second.docx");
        using (var file = DocxFile.Open(first))
        {
            file.Model.Comments!.Comments[0].Done = false;
            await file.SaveAsync(second, Cancel);
        }

        using var reopened = DocxFile.Open(second);
        Assert.False(reopened.Model.Comments!.Comments[0].Done);
        Assert.Empty(PartComparer.Diff(first, second).Removed);
    }

    [Fact]
    public async Task ANewHeaderPartIsCreatedForANewRelationshipId()
    {
        using var folder = new TempFolder();
        var path = folder.File("simple.docx");
        DocxBuilder.Simple(path);
        var saved = folder.File("saved.docx");
        using (var file = DocxFile.Open(path))
        {
            var header = NewStory("hdr");
            header.Blocks.Add(TextParagraph("Header text"));
            file.Model.HeadersFooters["rIdNewHeader"] = new HeaderFooterPart(header);
            await file.SaveAsync(saved, Cancel);
        }

        using var reopened = DocxFile.Open(saved);
        Assert.Empty(reopened.Model.HeadersFooters);
        using var package = WordprocessingDocument.Open(saved, false);
        Assert.Single(package.MainDocumentPart!.HeaderParts);
    }

    [Fact]
    public async Task FailedSaveLeavesTargetAndFolderUntouched()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        var target = folder.File("target.docx");
        var targetBytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(target, targetBytes, Cancel);
        var names = folder.Names();

        using (var file = DocxFile.Open(path))
        {
            file.BeforeReplace = () => throw new InvalidOperationException("stop before the replace");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => file.SaveAsync(target, Cancel));
            Assert.Equal("stop before the replace", error.Message);
        }

        Assert.Equal(targetBytes, await File.ReadAllBytesAsync(target, Cancel));
        Assert.Equal(names, folder.Names());

        using (var file = DocxFile.Open(path))
        {
            file.WrapPartWrite = (name, stream) => name == "/word/document.xml" ? new GarbageStream(stream) : stream;
            var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveAsync(target, Cancel));
            Assert.Equal(DocxSaveReason.ModelNotReadable, error.Reason);
            Assert.Equal("/word/document.xml", error.PartName);
        }

        Assert.Equal(targetBytes, await File.ReadAllBytesAsync(target, Cancel));
        Assert.Equal(names, folder.Names());

        foreach (var (edit, partName) in new (Action<string> Edit, string Name)[]
        {
            (t => DocxBuilder.EditInPlace(t, "media/image.png", [1, 2, 3]), "/media/image.png"),
            (t => DocxBuilder.EditInPlace(t, "media/image2.png", null), "/media/image2.png"),
            (t => DocxBuilder.EditInPlace(t, "word/extra.bin", [1]), "/word/extra.bin"),
        })
        {
            using var file = DocxFile.Open(path);
            file.AfterPackageWritten = edit;
            var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveAsync(target, Cancel));
            Assert.Equal(DocxSaveReason.PartsChanged, error.Reason);
            Assert.Equal(partName, error.PartName);
            Assert.Equal(targetBytes, await File.ReadAllBytesAsync(target, Cancel));
            Assert.Equal(names, folder.Names());
        }

        using (var file = DocxFile.Open(path))
        {
            file.AfterPackageWritten = temporary => DocxBuilder.EditInPlace(temporary, "word/footer1.xml", null);
            var error = await Assert.ThrowsAsync<DocxSaveException>(() => file.SaveAsync(target, Cancel));
            Assert.Equal(DocxSaveReason.ModelNotReadable, error.Reason);
        }

        Assert.Equal(targetBytes, await File.ReadAllBytesAsync(target, Cancel));
        Assert.Equal(names, folder.Names());
    }

    [Fact]
    public async Task AReadOnlyTargetEndsInASaveFailureAndLeavesNothingBehind()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        var target = folder.File("target.docx");
        await File.WriteAllBytesAsync(target, [9, 9], Cancel);
        File.SetAttributes(target, FileAttributes.ReadOnly);
        var names = folder.Names();

        using var file = DocxFile.Open(path);
        var error = await Assert.ThrowsAsync<SaveFailureException>(() => file.SaveAsync(target, Cancel));

        Assert.Equal(SaveFailureReason.NotWritable, error.Reason);
        Assert.Equal([9, 9], await File.ReadAllBytesAsync(target, Cancel));
        Assert.Equal(names, folder.Names());
    }

    [Fact]
    public async Task AMissingFolderEndsInASaveFailure()
    {
        using var folder = new TempFolder();
        using var file = DocxFile.Open(folder.CopyThesis());

        await Assert.ThrowsAsync<SaveFailureException>(() => file.SaveAsync(Path.Combine(folder.Path, "no-such-folder", "x.docx"), Cancel));

        Assert.Equal(["thesis.docx"], folder.Names());
    }

    [Fact]
    public async Task ACancelledSaveLeavesTargetAndFolderUntouched()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        var target = folder.File("target.docx");
        await File.WriteAllBytesAsync(target, [7], Cancel);
        var names = folder.Names();
        using var file = DocxFile.Open(path);

        using var source = new CancellationTokenSource();
        file.BeforeReplace = source.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.SaveAsync(target, source.Token));
        Assert.Equal([7], await File.ReadAllBytesAsync(target, Cancel));
        Assert.Equal(names, folder.Names());

        file.BeforeReplace = null;
        using var before = new CancellationTokenSource();
        before.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.SaveAsync(target, before.Token));
        Assert.Equal([7], await File.ReadAllBytesAsync(target, Cancel));
        Assert.Equal(names, folder.Names());
    }

    [Fact]
    public async Task SaveThroughSymlinkWritesTheTarget()
    {
        using var folder = new TempFolder();
        var real = folder.CopyThesis("real.docx");
        var link = folder.File("link.docx");
        try
        {
            File.CreateSymbolicLink(link, real);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"This account cannot create symbolic links here ({ex.GetType().Name}); on Windows that needs developer mode or administrator rights.");
        }

        using (var file = DocxFile.Open(link))
        {
            file.Model.Body.Blocks.Add(TextParagraph("Saved through a link"));
            await file.SaveAsync(link, Cancel);
        }

        Assert.NotNull(new FileInfo(link).LinkTarget);
        using var reopened = DocxFile.Open(real);
        Assert.Contains(reopened.Model.Body.Blocks.OfType<Paragraph>(), p => p.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<TextContent>().Any(t => t.Text == "Saved through a link"));
        Assert.Equal(["link.docx", "real.docx"], folder.Names());
    }

    [Fact]
    public async Task SavingOverTheOpenedFileWorks()
    {
        using var folder = new TempFolder();
        var path = folder.CopyThesis();
        using (var file = DocxFile.Open(path))
        {
            file.Model.Body.Blocks.Add(TextParagraph("In place"));
            await file.SaveAsync(path, Cancel);
        }

        using var reopened = DocxFile.Open(path);
        Assert.Contains(reopened.Model.Body.Blocks.OfType<Paragraph>(), p => p.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<TextContent>().Any(t => t.Text == "In place"));
        Assert.Equal(["thesis.docx"], folder.Names());
    }

    [Fact]
    public async Task ASaveAfterDisposeThrows()
    {
        using var folder = new TempFolder();
        var file = DocxFile.Open(folder.CopyThesis());
        file.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => file.SaveAsync(folder.File("x.docx"), Cancel));
    }

    /// <summary>Passes the bytes through but flips the first one, so the part is no longer what the codec produced.</summary>
    private sealed class GarbageStream(Stream inner) : Stream
    {
        private bool _first = true;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_first && buffer.Length > 0)
            {
                _first = false;
                var copy = buffer.ToArray();
                copy[0] = (byte)'#';
                inner.Write(copy);
                return;
            }

            inner.Write(buffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
