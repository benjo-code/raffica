using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class BlockMoveTests
{
    private static WordDocument NewDocument(int count, string? extra = null) =>
        LayoutModel.Document(string.Concat(Enumerable.Range(0, count).Select(i => $"<w:p><w:r><w:t>p{i}</w:t></w:r></w:p>")) + extra);

    private static TextEditor EditorOf(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static Block[] Blocks(WordDocument document) => document.Body.Blocks.ToArray();

    private static string TextOf(Block block) => TestEdit.TextOf((Paragraph)block);

    private static int[] Order(WordDocument document, Block[] original) => document.Body.Blocks.Select(b => Array.IndexOf(original, b)).ToArray();

    [Fact]
    public void MovingASubtreeDownAndUpKeepsEveryBlockObject()
    {
        var document = NewDocument(8);
        var editor = EditorOf(document);
        var before = Blocks(document);

        var down = editor.MoveBlocks(1, 2, 6);

        Assert.NotNull(down);
        Assert.Equal([0, 3, 4, 5, 1, 2, 6, 7], Order(document, before));
        Assert.Equal(8, document.Body.Blocks.Count);
        var change = Assert.Single(down.Changes);
        Assert.Equal(1, change.First);
        Assert.Equal(5, change.Removed);
        Assert.Equal(5, change.Added);
        Assert.Equal(before.Skip(1).Take(5), change.OldBlocks);
        Assert.Same(before[1], down.Caret.Paragraph);
        Assert.Equal(0, down.Caret.Offset);
        Assert.Equal(new BlockMove(StoryRef.Body, 1, 2, 6), down.Move);

        var up = editor.MoveBlocks(4, 2, 0);

        Assert.NotNull(up);
        Assert.Equal([1, 2, 0, 3, 4, 5, 6, 7], Order(document, before));
        Assert.Same(before[1], up.Caret.Paragraph);
        var upChange = Assert.Single(up.Changes);
        Assert.Equal(0, upChange.First);
        Assert.Equal(6, upChange.Removed);
    }

    [Fact]
    public void AMoveToItsOwnPlaceIsNull()
    {
        var document = NewDocument(5);
        var editor = EditorOf(document);
        var revision = document.Edits.Revision;

        Assert.Null(editor.MoveBlocks(1, 2, 1));
        Assert.Null(editor.MoveBlocks(1, 2, 3));
        Assert.Null(editor.MoveBlocks(0, 5, 5));
        Assert.Null(editor.MoveBlocks(0, 0, 3));
        Assert.Null(editor.MoveBlocks(-1, 1, 3));
        Assert.Null(editor.MoveBlocks(3, 3, 0));
        Assert.Null(editor.MoveBlocks(0, 1, 6));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void ADropInsideTheSubtreeIsNull()
    {
        var document = NewDocument(6);
        var editor = EditorOf(document);
        var before = Blocks(document);

        Assert.Null(editor.MoveBlocks(1, 4, 2));
        Assert.Null(editor.MoveBlocks(1, 4, 4));
        Assert.Equal(before, Blocks(document));
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void UndoRestoresTheOrderAndTheRevision()
    {
        var document = NewDocument(6);
        var editor = EditorOf(document);
        var before = Blocks(document);
        var revision = document.Edits.Revision;

        var moved = editor.MoveBlocks(0, 2, 5);
        Assert.NotNull(moved);
        Assert.True(document.Edits.IsDirty);
        Assert.Equal(1, editor.History.UndoCount);

        var undone = editor.History.Undo();

        Assert.NotNull(undone);
        Assert.Equal(before, Blocks(document));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.False(document.Edits.IsDirty);
        Assert.Same(before[0], undone.Caret.Paragraph);
        var change = Assert.Single(undone.Changes);
        Assert.Equal(0, change.First);
        Assert.Equal(5, change.Removed);
        Assert.Equal(5, change.Added);
        Assert.Null(undone.Style);
        Assert.NotNull(undone.Move);
    }

    [Fact]
    public void RedoMovesAgain()
    {
        var document = NewDocument(6);
        var editor = EditorOf(document);
        var before = Blocks(document);
        editor.MoveBlocks(3, 2, 0);
        var moved = Blocks(document);
        editor.History.Undo();

        var redone = editor.History.Redo();

        Assert.NotNull(redone);
        Assert.Equal(moved, Blocks(document));
        Assert.Same(before[3], redone.Caret.Paragraph);
        Assert.Equal(1, editor.History.UndoCount);
        Assert.False(editor.History.CanRedo);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Blocks(document));
    }

    [Fact]
    public void MovedBlocksKeepTheirParaIdsBookmarksAndCommentAnchors()
    {
        var document = NewDocument(5);
        var editor = EditorOf(document);
        var start = new BookmarkStart(7, "mark");
        var end = new BookmarkEnd(7);
        var comment = new CommentRangeStart(3);
        var first = (Paragraph)document.Body.Blocks[1];
        var last = (Paragraph)document.Body.Blocks[2];
        first.Inlines.Insert(0, start);
        first.Inlines.Insert(1, comment);
        last.Inlines.Add(end);
        var count = first.Inlines.Count;

        editor.MoveBlocks(1, 2, 5);

        Assert.Same(first, document.Body.Blocks[3]);
        Assert.Same(last, document.Body.Blocks[4]);
        Assert.Same(start, first.Inlines[0]);
        Assert.Same(comment, first.Inlines[1]);
        Assert.Same(end, last.Inlines[^1]);
        Assert.Equal(count, first.Inlines.Count);
    }

    [Fact]
    public void TheHistoryStepCostsNoBlockClones()
    {
        var document = NewDocument(400);
        var editor = EditorOf(document);

        Assert.NotNull(editor.MoveBlocks(10, 300, 400));

        Assert.Equal(1, editor.History.UndoCount);
        Assert.InRange(editor.History.UndoBytes, 0, 1024);

        // The step holds the live block objects, not copies: a move of 300 blocks costs what a move of one block costs.
        var small = NewDocument(400);
        var smallEditor = EditorOf(small);
        Assert.NotNull(smallEditor.MoveBlocks(10, 1, 400));
        Assert.Equal(smallEditor.History.UndoBytes, editor.History.UndoBytes);
    }

    [Fact]
    public void TheStepDoesNotCoalesceWithTyping()
    {
        var document = NewDocument(6);
        var editor = EditorOf(document);
        var paragraph = (Paragraph)document.Body.Blocks[3];
        var at = new TextPosition(StoryRef.Body, paragraph, 2);
        editor.InsertText(new TextRange(at, at), "ab");
        Assert.Equal(1, editor.History.UndoCount);

        editor.MoveBlocks(0, 1, 6);
        var next = new TextPosition(StoryRef.Body, paragraph, 4);
        editor.InsertText(new TextRange(next, next), "cd");

        Assert.Equal(3, editor.History.UndoCount);
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal("p0", TextOf(document.Body.Blocks[0]));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal("p3", TextOf(document.Body.Blocks[3]));
    }

    [Fact]
    public void MovingTheFirstChapterToTheEndOfAThousandBlocksIsOneStep()
    {
        var document = NewDocument(1000);
        var editor = EditorOf(document);
        var before = Blocks(document);

        var moved = editor.MoveBlocks(0, 200, 1000);

        Assert.NotNull(moved);
        Assert.Equal(1, editor.History.UndoCount);
        Assert.Same(before[200], document.Body.Blocks[0]);
        Assert.Same(before[0], document.Body.Blocks[800]);
        Assert.Equal(1000, document.Body.Blocks.Count);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Blocks(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Same(before[0], document.Body.Blocks[800]);
    }

    [Fact]
    public void ARangeWithoutAParagraphMovedToTheStartPutsTheCaretInTheParagraphAfterIt()
    {
        var table = "<w:tbl><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>c</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var document = NewDocument(3, table + "<w:p><w:r><w:t>z</w:t></w:r></w:p>");
        var editor = EditorOf(document);
        var first = document.Body.Blocks[0];

        var moved = editor.MoveBlocks(3, 1, 0);

        Assert.NotNull(moved);
        Assert.IsType<Table>(document.Body.Blocks[0]);
        // Nothing lies before the moved table, so the nearest paragraph is the one after it (not the last one of the body).
        Assert.Same(first, moved.Caret.Paragraph);
    }

    [Fact]
    public void ARangeWithoutAParagraphPutsTheCaretInTheNearestParagraph()
    {
        var table = "<w:tbl><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>c</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var document = NewDocument(3, table + "<w:p><w:r><w:t>z</w:t></w:r></w:p>");
        var editor = EditorOf(document);
        var z = document.Body.Blocks[4];

        var moved = editor.MoveBlocks(3, 1, 5);

        Assert.NotNull(moved);
        Assert.IsType<Table>(document.Body.Blocks[4]);
        Assert.Same(z, moved.Caret.Paragraph);
    }

    [Fact]
    public void ASectionBreakParagraphMovesWithItsObject()
    {
        var document = NewDocument(4);
        var editor = EditorOf(document);
        var withBreak = (Paragraph)document.Body.Blocks[1];
        withBreak.SectionBreak = new SectionProperties();

        editor.MoveBlocks(1, 1, 4);

        Assert.Same(withBreak, document.Body.Blocks[3]);
        Assert.NotNull(withBreak.SectionBreak);
    }

    [Fact]
    public void AMoveAcrossASectionBreakKeepsEveryObject()
    {
        var document = NewDocument(7);
        var editor = EditorOf(document);
        var withBreak = (Paragraph)document.Body.Blocks[2];
        var properties = new SectionProperties();
        withBreak.SectionBreak = properties;
        var original = Blocks(document);
        var sections = SectionList.Of(document);
        Assert.Equal(2, sections.Count);

        // The chapter p4 to p5 moves in front of the break paragraph: it joins the first section, the break stays where it ends that section.
        Assert.NotNull(editor.MoveBlocks(4, 2, 2));
        Assert.Equal([0, 1, 4, 5, 2, 3, 6], Order(document, original));
        Assert.All(original, block => Assert.Contains(block, document.Body.Blocks));
        Assert.Same(properties, withBreak.SectionBreak);
        var after = SectionList.Of(document);
        Assert.Equal(2, after.Count);
        Assert.Same(withBreak, after[0].Break);
        Assert.Equal((0, 4), (after[0].FirstBlock, after[0].LastBlock));

        // The range that holds the break moves past blocks of the next section: the break goes with it and the section ranges follow.
        Assert.NotNull(editor.MoveBlocks(3, 3, 7));
        Assert.Contains(withBreak, document.Body.Blocks);
        Assert.Same(properties, withBreak.SectionBreak);
        Assert.Equal(2, SectionList.Of(document).Count);

        // Undo and redo bring back the same objects in the same order.
        var moved = Blocks(document);
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], Order(document, original));
        Assert.Same(properties, withBreak.SectionBreak);
        Assert.NotNull(editor.History.Redo());
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(moved, Blocks(document));
    }
}
