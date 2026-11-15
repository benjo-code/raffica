using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class EditHistoryTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private sealed class FakeTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _ticks;

        public void Advance(int milliseconds) => _ticks += milliseconds;
    }

    private static Run R(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    private static Paragraph P(string text = "")
    {
        var paragraph = new Paragraph();
        if (text.Length > 0)
        {
            paragraph.Inlines.Add(R(text));
        }

        return paragraph;
    }

    private static WordDocument Doc(params Block[] blocks)
    {
        var story = new Story(new OoxmlRoot(W + "document", []));
        foreach (var block in blocks)
        {
            story.Blocks.Add(block);
        }

        return new WordDocument(story);
    }

    private static string Text(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static string[] Texts(IEnumerable<Block> blocks) => blocks.Select(b => b is Paragraph p ? Text(p) : "<" + b.GetType().Name + ">").ToArray();

    private static string[] Texts(WordDocument document) => Texts(document.Body.Blocks);

    private static TextEditor Editor(WordDocument document, TimeProvider? time = null, HistoryLimits? limits = null) =>
        new(document, (_, _) => ProjectionHost.Plain, time, limits);

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Caret(TextPosition position) => new(position, position);

    private static TextPosition Type(TextEditor editor, TextPosition caret, string text) => editor.InsertText(Caret(caret), text).Caret;

    private static bool InDocument(WordDocument document, EditResult result) => document.Body.Blocks.Contains(result.Caret.Paragraph);

    private static (WordDocument Document, FakeTime Time, TextEditor Editor, Paragraph Paragraph) Typing(string text = "")
    {
        var paragraph = P(text);
        var document = Doc(paragraph);
        var time = new FakeTime();
        return (document, time, Editor(document, time), paragraph);
    }

    [Fact]
    public void TypingCoalescesIntoOneStepPerWordOrPause()
    {
        var (document, time, editor, p) = Typing();
        var caret = At(p, 0);
        foreach (var letter in "hello")
        {
            caret = Type(editor, caret, letter.ToString());
            time.Advance(100);
        }

        Assert.Equal(1, editor.History.UndoCount);

        // A blank starts the next step and the letters after it join that step.
        foreach (var letter in " wo")
        {
            caret = Type(editor, caret, letter.ToString());
            time.Advance(100);
        }

        Assert.Equal(2, editor.History.UndoCount);
        Assert.Equal(["hello wo"], Texts(document));

        // A pause of two seconds splits, 1999 ms does not.
        time.Advance(1899);
        caret = Type(editor, caret, "r");
        Assert.Equal(2, editor.History.UndoCount);
        time.Advance(2000);
        caret = Type(editor, caret, "l");
        Assert.Equal(3, editor.History.UndoCount);
        Assert.Equal(["hello worl"], Texts(document));

        var result = editor.History.Undo()!;
        Assert.Equal(["hello wor"], Texts(document));
        result = editor.History.Undo()!;
        Assert.Equal(["hello"], Texts(document));
        result = editor.History.Undo()!;
        Assert.Equal([string.Empty], Texts(document));
        Assert.False(editor.History.CanUndo);
        Assert.Equal(0, result.Caret.Offset);
    }

    [Fact]
    public void OnlyPlainSingleLineInsertsCoalesce()
    {
        var (document, time, editor, p) = Typing("abc");
        var caret = Type(editor, At(p, 3), "d");
        caret = Type(editor, caret, "e");
        Assert.Equal(1, editor.History.UndoCount);

        // A jump of the caret, a replaced selection and a paragraph break each start a step.
        caret = Type(editor, At(document.Body.Blocks.OfType<Paragraph>().First(), 0), "x");
        Assert.Equal(2, editor.History.UndoCount);
        var first = document.Body.Blocks.OfType<Paragraph>().First();
        editor.InsertText(new TextRange(At(first, 0), At(first, 1)), "y");
        Assert.Equal(3, editor.History.UndoCount);
        editor.InsertText(Caret(At(first, 1)), "z");
        Assert.Equal(4, editor.History.UndoCount);
        editor.InsertText(Caret(At(first, 2)), "\n");
        Assert.Equal(5, editor.History.UndoCount);
        Assert.Equal(["yz", "abcde"], Texts(document));
    }

    [Fact]
    public void UndoRestoresTextAndCaretInItsStory()
    {
        var body = P("Body");
        var note = P("Note");
        var document = Doc(body);
        var noteStory = new Story(new OoxmlRoot(W + "footnote", []));
        noteStory.Blocks.Add(note);
        document.Footnotes = new NoteCollection(new OoxmlRoot(W + "footnotes", []));
        document.Footnotes.Notes.Add(new Note(5, NoteKind.Normal, noteStory));
        var editor = Editor(document, new FakeTime());
        var noteRef = new StoryRef(StoryKind.Note, "5");

        editor.InsertText(Caret(At(body, 4)), "!");
        editor.InsertText(Caret(new TextPosition(noteRef, note, 0)), ">");
        Assert.Equal("Body!", Text(body));
        Assert.Equal(">Note", Text(note));

        var undoNote = editor.History.Undo()!;
        Assert.Equal(noteRef, undoNote.Caret.Story);
        Assert.Equal("Note", Text(noteStory.Blocks.OfType<Paragraph>().Single()));
        Assert.Same(noteStory.Blocks[0], undoNote.Caret.Paragraph);
        Assert.Equal(0, undoNote.Caret.Offset);
        Assert.Same(noteStory.Blocks, undoNote.Changes[0].Container);

        var undoBody = editor.History.Undo()!;
        Assert.Equal(StoryRef.Body, undoBody.Caret.Story);
        Assert.Equal(["Body"], Texts(document));
        Assert.Same(document.Body.Blocks[0], undoBody.Caret.Paragraph);
        Assert.Equal(4, undoBody.Caret.Offset);

        var redoBody = editor.History.Redo()!;
        Assert.Equal(["Body!"], Texts(document));
        Assert.Equal(5, redoBody.Caret.Offset);
        var redoNote = editor.History.Redo()!;
        Assert.Equal(">Note", Text((Paragraph)noteStory.Blocks[0]));
        Assert.Equal(noteRef, redoNote.Caret.Story);
        Assert.Equal(1, redoNote.Caret.Offset);
        Assert.False(editor.History.CanRedo);
    }

    [Fact]
    public void RedoIsClearedByANewEdit()
    {
        var (document, _, editor, p) = Typing("ab");
        editor.InsertText(Caret(At(p, 2)), "c");
        Assert.NotNull(editor.History.Undo());
        Assert.True(editor.History.CanRedo);
        editor.InsertText(Caret(At(document.Body.Blocks.OfType<Paragraph>().Single(), 0)), "z");
        Assert.False(editor.History.CanRedo);
        Assert.Null(editor.History.Redo());
        Assert.Equal(["zab"], Texts(document));
    }

    [Fact]
    public void ARefusedOperationRecordsNothing()
    {
        var (document, _, editor, p) = Typing("ab");
        var revision = document.Edits.Revision;
        editor.InsertText(Caret(At(p, 1)), string.Empty);
        editor.Delete(Caret(At(p, 1)));
        Assert.Null(editor.Backspace(At(p, 0), DeleteUnit.Cluster));
        Assert.False(editor.History.CanUndo);
        Assert.Equal(revision, document.Edits.Revision);
    }

    [Fact]
    public void UndoToTheSavedRevisionIsNotDirty()
    {
        var (document, time, editor, p) = Typing("ab");
        document.Edits.MarkSaved();
        var caret = Type(editor, At(p, 2), "c");
        Assert.True(document.Edits.IsDirty);
        editor.History.Undo();
        Assert.False(document.Edits.IsDirty);
        editor.History.Redo();
        Assert.True(document.Edits.IsDirty);
        editor.History.Undo();

        // A new edit after the undo is not the saved state, even though the counter has been there before.
        editor.InsertText(Caret(At(document.Body.Blocks.OfType<Paragraph>().Single(), 0)), "q");
        Assert.True(document.Edits.IsDirty);
        editor.History.Undo();
        Assert.False(document.Edits.IsDirty);
        Assert.Equal(["ab"], Texts(document));
        Assert.NotEqual(0, caret.Offset);
        Assert.Equal(0, time.GetTimestamp());
    }

    [Fact]
    public void TypingAfterASaveDoesNotJoinTheStepBeforeTheSave()
    {
        var (document, _, editor, p) = Typing();
        var caret = Type(editor, At(p, 0), "ab");
        document.Edits.MarkSaved();
        caret = Type(editor, caret, "cd");
        Assert.Equal(2, editor.History.UndoCount);
        editor.History.Undo();
        Assert.False(document.Edits.IsDirty);
        Assert.Equal(["ab"], Texts(document));
    }

    [Fact]
    public void ARevisionThatCanNoLongerBeReachedIsDirty()
    {
        var (document, _, editor, p) = Typing("a");
        document.Edits.MarkSaved();
        var limits = new HistoryLimits(2, long.MaxValue);
        editor = Editor(document, new FakeTime(), limits);
        var caret = At(p, 1);
        foreach (var text in new[] { ".", ",", ";", ":" })
        {
            caret = Type(editor, caret, text);
        }

        Assert.Equal(2, editor.History.UndoCount);
        while (editor.History.CanUndo)
        {
            editor.History.Undo();
        }

        Assert.Equal(["a.,"], Texts(document));
        Assert.True(document.Edits.IsDirty);
    }

    [Fact]
    public void SelectAllDeleteIsOneStepAndUndoRestoresEveryBlock()
    {
        var p1 = P("One");
        var p2 = P("Two");
        var p3 = P("Three");
        var document = Doc(p1, p2, p3);
        var editor = Editor(document, new FakeTime());

        var deleted = editor.Delete(new TextRange(At(p1, 0), At(p3, 5)));
        Assert.Equal([string.Empty], Texts(document));
        Assert.Equal(1, editor.History.UndoCount);

        var undone = editor.History.Undo()!;
        Assert.Equal(["One", "Two", "Three"], Texts(document));
        Assert.True(InDocument(document, undone));
        Assert.Equal(0, undone.Caret.Offset);
        Assert.Same(document.Body.Blocks[0], undone.Caret.Paragraph);
        var change = Assert.Single(undone.Changes);
        Assert.Equal(0, change.First);
        Assert.Equal(1, change.Removed);
        Assert.Equal(3, change.Added);
        Assert.Equal([string.Empty], Texts(change.OldBlocks));
        Assert.NotNull(deleted);

        var redone = editor.History.Redo()!;
        Assert.Equal([string.Empty], Texts(document));
        Assert.True(InDocument(document, redone));
        change = Assert.Single(redone.Changes);
        Assert.Equal(3, change.Removed);
        Assert.Equal(1, change.Added);
        Assert.Equal(["One", "Two", "Three"], Texts(change.OldBlocks));
    }

    [Fact]
    public void SplitAndMergeAreUndoneWithTheCaretOnTheRestoredParagraph()
    {
        var p = P("Hello world");
        var document = Doc(p);
        var editor = Editor(document, new FakeTime());

        var split = editor.SplitParagraph(Caret(At(p, 5)));
        Assert.Equal(["Hello", " world"], Texts(document));
        var merged = editor.Backspace(split.Caret, DeleteUnit.Cluster)!;
        Assert.Equal(["Hello world"], Texts(document));
        Assert.Equal(2, editor.History.UndoCount);

        var undoMerge = editor.History.Undo()!;
        Assert.Equal(["Hello", " world"], Texts(document));
        Assert.Same(document.Body.Blocks[1], undoMerge.Caret.Paragraph);
        Assert.Equal(0, undoMerge.Caret.Offset);

        var undoSplit = editor.History.Undo()!;
        Assert.Equal(["Hello world"], Texts(document));
        Assert.Same(document.Body.Blocks[0], undoSplit.Caret.Paragraph);
        Assert.Equal(5, undoSplit.Caret.Offset);

        editor.History.Redo();
        Assert.Equal(["Hello", " world"], Texts(document));
        var redoMerge = editor.History.Redo()!;
        Assert.Equal(["Hello world"], Texts(document));
        Assert.Same(document.Body.Blocks[0], redoMerge.Caret.Paragraph);
        Assert.Equal(5, redoMerge.Caret.Offset);
        Assert.Equal(5, merged.Caret.Offset);

        // Editing the restored paragraphs and undoing again still works: nothing in the history is shared with the document.
        editor.History.Undo();
        editor.History.Undo();
        editor.InsertText(Caret(new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[0], 0)), "x");
        editor.History.Undo();
        Assert.Equal(["Hello world"], Texts(document));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void LinearUndoAndRedoOfEditsInDifferentParagraphsMatchTheDocumentAtEveryPoint()
    {
        var p1 = P("alpha");
        var p2 = P("beta");
        var p3 = P("gamma");
        var document = Doc(p1, p2, p3);
        var editor = Editor(document, new FakeTime());
        var states = new List<string[]> { Texts(document) };

        editor.InsertText(Caret(At(p1, 5)), "A");
        states.Add(Texts(document));
        editor.InsertText(Caret(At(p3, 0)), "B");
        states.Add(Texts(document));
        editor.Delete(new TextRange(At(p2, 1), At((Paragraph)document.Body.Blocks[2], 1)));
        states.Add(Texts(document));
        Assert.Equal(["alphaA", "bgamma"], states[^1]);

        for (var i = states.Count - 2; i >= 0; i--)
        {
            var result = editor.History.Undo()!;
            Assert.Equal(states[i], Texts(document));
            Assert.True(InDocument(document, result));
        }

        Assert.False(editor.History.CanUndo);
        for (var i = 1; i < states.Count; i++)
        {
            var result = editor.History.Redo()!;
            Assert.Equal(states[i], Texts(document));
            Assert.True(InDocument(document, result));
        }

        Assert.False(editor.History.CanRedo);

        // Undo two, redo one, undo it again: the middle of the history stays intact.
        editor.History.Undo();
        editor.History.Undo();
        editor.History.Redo();
        Assert.Equal(states[2], Texts(document));
        editor.History.Undo();
        editor.History.Undo();
        Assert.Equal(states[0], Texts(document));
    }

    [Fact]
    public void ACellEditSurvivesTheUndoOfAStructuralEditBeforeIt()
    {
        var before = P("Before");
        var cellText = P("cell");
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Blocks.Add(cellText);
        row.Items.Add(cell);
        table.Rows.Add(row);
        var after = P("After");
        var document = Doc(before, table, after);
        var editor = Editor(document, new FakeTime());

        editor.InsertText(Caret(At(cellText, 4)), "!");
        editor.Delete(new TextRange(At(before, 0), At(after, 5)));
        Assert.Equal([string.Empty], Texts(document));

        editor.History.Undo();
        Assert.Equal(3, document.Body.Blocks.Count);
        var restored = Assert.IsType<Table>(document.Body.Blocks[1]);
        Assert.NotSame(table, restored);
        var restoredCell = (TableCell)((TableRow)restored.Rows[0]).Items[0];
        Assert.Equal(["cell!"], Texts(restoredCell.Blocks));

        var undone = editor.History.Undo()!;
        Assert.Equal(["cell"], Texts(restoredCell.Blocks));
        Assert.Same(restoredCell.Blocks, undone.Changes[0].Container);
        Assert.Same(restoredCell.Blocks[0], undone.Caret.Paragraph);

        editor.History.Redo();
        Assert.Equal(["cell!"], Texts(restoredCell.Blocks));
        editor.History.Redo();
        Assert.Equal([string.Empty], Texts(document));
    }

    [Fact]
    public void NewestStepIsKeptEvenOverTheByteLimit()
    {
        var p = P("small");
        var document = Doc(p, P(new string('x', 10_000)));
        var editor = Editor(document, new FakeTime(), new HistoryLimits(100, 1_000));

        editor.Delete(new TextRange(At(p, 0), At((Paragraph)document.Body.Blocks[1], 10_000)));
        Assert.Equal(1, editor.History.UndoCount);
        Assert.True(editor.History.UndoBytes > 1_000);
        Assert.True(editor.History.CanUndo);
        editor.History.Undo();
        Assert.Equal(["small", new string('x', 10_000)], Texts(document));
    }

    [Fact]
    public void OldestStepsGoFirst()
    {
        var (document, _, editor, p) = Typing();
        var steps = Editor(document, new FakeTime(), new HistoryLimits(3, long.MaxValue));
        var caret = At(p, 0);
        foreach (var text in new[] { ".", ",", ";", ":", "!" })
        {
            caret = Type(steps, caret, text);
        }

        Assert.Equal(3, steps.History.UndoCount);
        Assert.Equal([".,;:!"], Texts(document));
        while (steps.History.CanUndo)
        {
            steps.History.Undo();
        }

        Assert.Equal([".,"], Texts(document));
        Assert.False(editor.History.CanUndo);

        // The byte limit drops steps from the old end as well.
        var q = P("seed");
        var other = Doc(q);
        var bytes = Editor(other, new FakeTime(), new HistoryLimits(100, 2_500));
        var position = At(q, 4);
        for (var i = 0; i < 20; i++)
        {
            position = Type(bytes, position, i % 2 == 0 ? "." : ",");
        }

        Assert.True(bytes.History.UndoBytes <= 2_500);
        Assert.InRange(bytes.History.UndoCount, 2, 19);
    }

    [Fact]
    public void StepsShareUnknownContent()
    {
        var unknown = new UnknownContent(new XElement(W + "smartTag", new string('x', 100_000)));
        var p = new Paragraph();
        p.Inlines.Add(R("abc"));
        p.Inlines.Add(new UnknownInline(unknown));
        var document = Doc(p);
        var editor = Editor(document, new FakeTime());

        editor.InsertText(Caret(At(p, 1)), "X");
        Assert.InRange(editor.History.UndoBytes, 1, 5_000);

        editor.History.Undo();
        Assert.Same(unknown, ((Paragraph)document.Body.Blocks[0]).Inlines.OfType<UnknownInline>().Single().Content);
        editor.History.Redo();
        Assert.Same(unknown, ((Paragraph)document.Body.Blocks[0]).Inlines.OfType<UnknownInline>().Single().Content);
    }

    [Fact]
    public void CutIsOneStep()
    {
        var p1 = P("One");
        var p2 = P("Two");
        var p3 = P("Three");
        var document = Doc(p1, p2, p3);
        var editor = Editor(document, new FakeTime());

        editor.Delete(new TextRange(At(p1, 1), At(p3, 2)));
        Assert.Equal(["Oree"], Texts(document));
        Assert.Equal(1, editor.History.UndoCount);
        editor.History.Undo();
        Assert.Equal(["One", "Two", "Three"], Texts(document));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void UndoAndRedoPutTheRevisionBackSoTheLayoutSeesTheChange()
    {
        var (document, _, editor, p) = Typing("ab");
        var start = document.Edits.Revision;
        editor.InsertText(Caret(At(p, 2)), "c");
        var end = document.Edits.Revision;
        Assert.NotEqual(start, end);
        var undone = editor.History.Undo()!;
        Assert.Equal(start, document.Edits.Revision);
        Assert.Equal(["abc"], Texts(undone.Changes[0].OldBlocks));
        editor.History.Redo();
        Assert.Equal(end, document.Edits.Revision);
    }

    [Fact]
    public void AnEditBehindTheHistorysBackEndsTheHistory()
    {
        var (document, _, editor, p) = Typing("ab");
        editor.InsertText(Caret(At(p, 2)), "c");
        Assert.True(editor.History.CanUndo);
        document.Edits.Raise();
        Assert.False(editor.History.CanUndo);
        Assert.Null(editor.History.Undo());
        Assert.Equal(["abc"], Texts(document));
    }
}
