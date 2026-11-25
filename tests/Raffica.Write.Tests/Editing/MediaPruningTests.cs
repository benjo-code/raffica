using System.IO.Compression;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The picture store follows the history: an entry goes when the history drops the step that added it and nothing refers to it any more (not the document, not a remaining step), and never because of an undo or a redo.
/// </summary>
public sealed class MediaPruningTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly byte[] s_png = TestPictures.Png(6, 6);

    private static WordDocument Doc() => Document(Line("alpha") + Line("beta") + Line("gamma") + Sect());

    private static TextEditor Editor(WordDocument document, HistoryLimits? limits = null) => new(document, (_, _) => ProjectionHost.Plain, null, limits);

    private static TextPosition At(WordDocument document, int block, int offset) => new(StoryRef.Body, (Paragraph)document.Body.Blocks[block], offset);

    private static void Type(TextEditor editor, WordDocument document, int block, string text)
    {
        var caret = At(document, block, 0);
        Assert.NotNull(editor.InsertText(new TextRange(caret, caret), text));
    }

    private static string Id(EditResult? inserted) => Assert.Single(Assert.IsType<ObjectChange>(inserted!.Objects).MediaAdded).RelationshipId;

    private static List<string> MediaParts(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return [.. archive.Entries.Select(e => e.FullName).Where(n => n.StartsWith("word/media/", StringComparison.Ordinal))];
    }

    [Fact]
    public async Task InsertUndoThenTypeDropsTheEntryAndTheNextSaveHasNoMediaPart()
    {
        using var folder = new TempFolder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var document = file.Model;
        var editor = Editor(document);
        var paragraph = document.Body.Blocks.OfType<Paragraph>().First();

        Assert.NotNull(editor.InsertPicture(new TextPosition(StoryRef.Body, paragraph, 0), s_png, null));
        Assert.Equal(s_png.Length, document.Media.TotalBytes);
        Assert.NotNull(editor.History.Undo());

        // Undo keeps the entry (a redo needs it).
        Assert.Single(document.Media.Entries);

        // A new edit cuts the redo stack: the picture of the dropped step is unreachable and goes.
        var caret = new TextPosition(StoryRef.Body, document.Body.Blocks.OfType<Paragraph>().First(), 0);
        Assert.NotNull(editor.InsertText(new TextRange(caret, caret), "x"));
        Assert.Empty(document.Media.Entries);
        Assert.Equal(0, document.Media.TotalBytes);
        var path = folder.File("pruned.docx");
        await file.SaveAsync(path, Cancel);
        Assert.Empty(MediaParts(path));
        Assert.False(editor.History.CanRedo);
    }

    [Fact]
    public void InsertUndoRedoKeepsTheEntry()
    {
        var document = Doc();
        var editor = Editor(document);
        var inserted = editor.InsertPicture(At(document, 0, 2), s_png, null);
        var id = Id(inserted);

        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Redo());
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Redo());

        Assert.Equal(id, Assert.Single(document.Media.Entries).Ref.RelationshipId);
        Assert.NotNull(document.Media.TryGet(id));
    }

    [Fact]
    public void ADeletedPictureIsKeptWhileAnyStepOrTheDocumentCanBringItBack()
    {
        var document = Doc();
        var editor = Editor(document);
        var id = Id(editor.InsertPicture(At(document, 0, 2), s_png, null));
        var picture = editor.PictureAt(At(document, 0, 2))!;

        // Step B deletes the picture; undone, then a new edit drops B from the redo stack: the picture is in the document again and in step A.
        Assert.NotNull(editor.DeletePicture(picture));
        Assert.NotNull(editor.History.Undo());
        Type(editor, document, 2, "x ");
        Assert.NotNull(document.Media.TryGet(id));

        // Delete, then edit on: the insert step A and the delete step B both stay in the history, so the entry stays.
        Assert.NotNull(editor.DeletePicture(editor.PictureAt(At(document, 0, 2))!));
        Type(editor, document, 2, "y ");
        Assert.NotNull(document.Media.TryGet(id));
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(document.Media.TryGet(id));
    }

    [Fact]
    public void AnEvictedStepKeepsAPictureThatTheDocumentStillHolds()
    {
        var document = Doc();
        var editor = Editor(document, new HistoryLimits(2, long.MaxValue));
        var id = Id(editor.InsertPicture(At(document, 0, 2), s_png, null));

        Type(editor, document, 1, "one ");
        Type(editor, document, 2, "two ");
        Type(editor, document, 1, "three ");

        // The insert step is long gone from the history, the picture is still in the paragraph.
        Assert.Equal(2, editor.History.UndoCount);
        Assert.NotNull(document.Media.TryGet(id));
        Assert.Single(((Paragraph)document.Body.Blocks[0]).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>());
    }

    [Fact]
    public void AnEvictedStepDropsAPictureNothingRefersToAnyMore()
    {
        var document = Doc();
        var editor = Editor(document, new HistoryLimits(3, long.MaxValue));
        var id = Id(editor.InsertPicture(At(document, 0, 2), s_png, null));
        Assert.NotNull(editor.DeletePicture(editor.PictureAt(At(document, 0, 2))!));

        // The delete step B keeps a snapshot with the picture while it is in the history; the insert step A is evicted first and the entry stays.
        Type(editor, document, 1, "one ");
        Type(editor, document, 2, "two ");
        Assert.NotNull(document.Media.TryGet(id));

        // B is evicted too: nothing in the document or the history refers to the picture.
        Type(editor, document, 1, "three ");
        Type(editor, document, 2, "four ");
        Assert.Null(document.Media.TryGet(id));
        Assert.Empty(document.Media.Entries);
    }

    [Fact]
    public void OnlyTheUnreachablePictureOfTwoIsRemoved()
    {
        var document = Doc();
        var editor = Editor(document);
        var first = Id(editor.InsertPicture(At(document, 0, 1), s_png, null));
        var second = Id(editor.InsertPicture(At(document, 1, 1), TestPictures.Png(7, 7), null));
        Assert.Equal(2, document.Media.Entries.Count);

        Assert.NotNull(editor.History.Undo());
        Type(editor, document, 2, "x ");

        Assert.Equal(first, Assert.Single(document.Media.Entries).Ref.RelationshipId);
        Assert.Null(document.Media.TryGet(second));

        // A relationship id is never given out twice: the next picture is above both.
        var third = Id(editor.InsertPicture(At(document, 1, 1), TestPictures.Png(8, 8), null));
        Assert.NotEqual(second, third);
        Assert.True(int.Parse(third[3..], System.Globalization.CultureInfo.InvariantCulture) > int.Parse(second[3..], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ManyInsertUndoNewEditCyclesOfALargeFileNeverFillTheStore()
    {
        var document = Doc();
        var editor = Editor(document);
        var big = new byte[40 * 1024 * 1024];
        TestPictures.Png(4, 4).CopyTo(big, 0);

        // Eight times 40 MiB would pass the 256 MiB the store takes (seven do): the unreachable ones leave with each new edit.
        for (var i = 0; i < 8; i++)
        {
            Assert.Null(editor.RefusalForInsertPicture(At(document, 0, 0), big));
            Assert.NotNull(editor.InsertPicture(At(document, 0, 0), big, null));
            Assert.NotNull(editor.History.Undo());
            Type(editor, document, 1, "x ");
            Assert.Equal(0, document.Media.TotalBytes);
        }

        Assert.Empty(document.Media.Entries);
    }

    [Fact]
    public void AClearedHistoryKeepsWhatTheDocumentHoldsAndDropsTheRest()
    {
        var document = Doc();
        var editor = Editor(document);
        var kept = Id(editor.InsertPicture(At(document, 0, 1), s_png, null));
        var undone = Id(editor.InsertPicture(At(document, 1, 1), TestPictures.Png(7, 7), null));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(2, document.Media.Entries.Count);

        // A change behind the history's back clears it at the next look; the picture in the paragraph stays, the undone one has no reference left.
        document.Edits.Raise();
        Assert.False(editor.History.CanUndo);

        Assert.Equal(kept, Assert.Single(document.Media.Entries).Ref.RelationshipId);
        Assert.Null(document.Media.TryGet(undone));
    }

    [Fact]
    public void TypingInADocumentWithoutPicturesNeverMakesAStore()
    {
        var document = Doc();
        var editor = Editor(document, new HistoryLimits(3, long.MaxValue));
        for (var i = 0; i < 10; i++)
        {
            Type(editor, document, i % 3, "w" + i + " ");
        }

        Assert.NotNull(editor.History.Undo());
        Type(editor, document, 0, "z ");
        Assert.False(document.HasMedia);
    }

    [Fact]
    public void ADroppedPastePictureAndAReplacedSelectionAreHandledLikeAnInsert()
    {
        var document = Doc();
        var editor = Editor(document);
        var paragraph = (Paragraph)document.Body.Blocks[0];
        var selection = new TextRange(new TextPosition(StoryRef.Body, paragraph, 1), new TextPosition(StoryRef.Body, paragraph, 4));
        var id = Id(editor.PastePicture(selection, s_png));

        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(document.Media.TryGet(id));
        Type(editor, document, 1, "x ");
        Assert.Null(document.Media.TryGet(id));
    }
}
