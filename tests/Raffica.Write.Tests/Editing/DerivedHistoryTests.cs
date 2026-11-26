using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// A derived record (a field refresh that changed the paragraph count) rides on the newest history step of any kind: undo reverts it first, redo replays it after the step, so the indices of the older steps
/// stay true (milestone 4.5b). The matrix runs every step kind the editor records; a new kind is one row of <see cref="Kinds"/>.
/// </summary>
public sealed class DerivedHistoryTests
{
    private static string Styles()
    {
        var text = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        for (var i = 1; i <= 9; i++)
        {
            text += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        return LayoutModel.StylesPart(text);
    }

    private static string P(string text, string? style = null) =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}</w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    // H1 A, p, H2 B, p, H1 C, p, p, and the final section.
    private static WordDocument NewDocument() => LayoutModel.Document(
        P("A", "Heading1") + P("a") + P("B", "Heading2") + P("b") + P("C", "Heading1") + P("c") + P("d")
            + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>",
        Styles(),
        LayoutModel.NumberingPart(string.Empty));

    private static TextEditor EditorOf(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document));

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, (Paragraph)document.Body.Blocks[block], offset);

    private static TextRange Range(WordDocument document, int first, int last) =>
        new(At(document, first), At(document, last, TestEdit.TextOf((Paragraph)document.Body.Blocks[last]).Length));

    /// <summary>One row per step kind the editor records: the operation that makes the step.</summary>
    public static TheoryData<string> Kinds() => ["typing", "format", "style-only", "list", "move", "section", "object", "bookmark"];

    private static EditResult? Run(string kind, WordDocument document, TextEditor editor) => kind switch
    {
        "typing" => editor.InsertText(new TextRange(At(document, 3, 1), At(document, 3, 1)), "ab"),
        "format" => editor.ToggleCharacter(Range(document, 1, 1), CharacterToggle.Bold),
        "style-only" => editor.SetHeadingNumbering(true, At(document, 0)),
        "list" => editor.ToggleList(Range(document, 1, 3), ListKind.Bullet),
        "move" => editor.MoveBlocks(0, 2, 7),
        "section" => editor.InsertSectionBreak(At(document, 3, 1), SectionBreakKind.NextPage),
        "object" => editor.InsertNote(At(document, 3, 1), NoteType.Footnote),
        "bookmark" => editor.AddBookmark(Range(document, 1, 3), "Mark"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool Changed(EditResult? result) => result is not null && (result.Changes.Count > 0 || result.Style is not null || result.Sections is not null || result.Move is not null);

    // The body, and the styles for the kind that swaps them (a numbering part and a notes part are never removed by an undo, by design).
    private static string Snap(WordDocument document, string kind)
    {
        var model = ModelSnapshot.Of(document);
        var parts = model.Elements().Where(p => (string?)p.Attribute("name") == "document" || (kind == "style-only" && (string?)p.Attribute("name") == "styles"));
        return string.Concat(parts.Select(p => p.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))) + "|" + document.Body.Blocks.Count;
    }

    private static Paragraph Para(string text)
    {
        var paragraph = new Paragraph();
        var run = new Run();
        run.Content.Add(new TextContent(text));
        paragraph.Inlines.Add(run);
        return paragraph;
    }

    // What a refresh does: two paragraphs go in above everything (every block index of the document shifts by two), as a record of the one block change.
    private static DerivedRecord TwoAbove(WordDocument document, string label = "T")
    {
        document.Body.Blocks.Insert(0, Para(label + "1"));
        document.Body.Blocks.Insert(1, Para(label + "2"));
        return new DerivedRecord([new BlockChange(StoryRef.Body, document.Body.Blocks, 0, 0, 2, [])]);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TheHistoryMatrix(string kind)
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        var history = editor.History;

        // An older step first, so the undo of the newest one has something to be consistent with.
        var original = Snap(document, kind);
        Assert.True(Changed(editor.InsertText(new TextRange(At(document, 5, 1), At(document, 5, 1)), "q")));

        var beforeStep = Snap(document, kind);
        Assert.True(Changed(Run(kind, document, editor)), kind + " changed nothing");
        var afterStep = Snap(document, kind);
        Assert.NotEqual(beforeStep, afterStep);
        var revision = document.Edits.Revision;
        var derivedBefore = document.Edits.DerivedVersion;

        Assert.Equal(AttachOutcome.Attached, history.AttachDerived(TwoAbove(document)));
        var afterBoth = Snap(document, kind);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(derivedBefore, document.Edits.DerivedVersion);

        // Undo: the record is reverted first, then the step: the state before the step, block indices included.
        var undone = history.Undo();
        Assert.NotNull(undone);
        Assert.Equal(beforeStep, Snap(document, kind));
        Assert.NotNull(undone.DerivedChanges);
        Assert.Single(undone.DerivedChanges);
        Assert.Equal(2, undone.DerivedChanges[0].Removed);
        Assert.Equal(0, undone.DerivedChanges[0].Added);
        Assert.True(document.Edits.DerivedVersion > derivedBefore);

        // Redo: the step, then the record (replayed): the state after both.
        var redone = history.Redo();
        Assert.NotNull(redone);
        Assert.Equal(afterBoth, Snap(document, kind));
        Assert.NotNull(redone.DerivedChanges);
        Assert.Single(redone.DerivedChanges);
        Assert.Equal(0, redone.DerivedChanges[0].Removed);
        Assert.Equal(2, redone.DerivedChanges[0].Added);

        // Undo twice in a row: the step and the older one before it, with the indices of the older step intact.
        Assert.NotNull(history.Undo());
        Assert.Equal(beforeStep, Snap(document, kind));
        Assert.NotNull(history.Undo());
        Assert.Equal(original, Snap(document, kind));
        Assert.False(history.CanUndo);

        // A pending redo makes the next attach wait.
        Assert.Equal(AttachOutcome.MustWait, history.AttachDerived(new DerivedRecord([new BlockChange(StoryRef.Body, document.Body.Blocks, 0, 0, 0, [])])));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AnUndoThenAnAttachWaitsBecauseARedoIsPending(string kind)
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        Assert.True(Changed(Run(kind, document, editor)));
        Assert.NotNull(editor.History.Undo());
        Assert.True(editor.History.CanRedo);

        Assert.Equal(AttachOutcome.MustWait, editor.History.AttachDerived(TwoAbove(document)));
    }

    [Fact]
    public void AnEmptyHistoryHasNothingToProtect()
    {
        var document = NewDocument();
        var editor = EditorOf(document);

        Assert.Equal(AttachOutcome.NothingToProtect, editor.History.AttachDerived(TwoAbove(document)));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void ATypingStepWithARecordDoesNotTakeTheNextLetter()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        var original = Snap(document, "typing");
        var caret = editor.InsertText(new TextRange(At(document, 3, 1), At(document, 3, 1)), "a").Caret;
        Assert.Equal(AttachOutcome.Attached, editor.History.AttachDerived(TwoAbove(document)));
        var afterRecord = Snap(document, "typing");

        var next = new TextPosition(StoryRef.Body, caret.Paragraph, caret.Offset);
        Assert.True(Changed(editor.InsertText(new TextRange(next, next), "b")));

        Assert.Equal(2, editor.History.UndoCount);
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(afterRecord, Snap(document, "typing"));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(original, Snap(document, "typing"));
    }

    [Fact]
    public void ARefusedUndoOfAStyleStepPutsTheRevertedRecordsBack()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        Assert.True(Changed(editor.SetHeadingNumbering(true, At(document, 0))));
        Assert.Equal(AttachOutcome.Attached, editor.History.AttachDerived(TwoAbove(document)));
        var stamp = document.Edits.Stamp;
        var untouched = Snap(document, "typing");
        document.Styles = null;

        Assert.Null(editor.History.Undo());

        Assert.Equal(untouched, Snap(document, "typing"));
        Assert.Equal(stamp.Revision, document.Edits.Revision);
        Assert.True(document.Edits.DerivedVersion > stamp.DerivedVersion, "consumers are told that blocks were swapped and swapped back");
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void ARefusedUndoOfAMoveStepPutsTheRevertedRecordsBack()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        Assert.True(Changed(editor.MoveBlocks(0, 2, 7)));
        Assert.Equal(AttachOutcome.Attached, editor.History.AttachDerived(TwoAbove(document)));
        while (document.Body.Blocks.Count > 4)
        {
            document.Body.Blocks.RemoveAt(document.Body.Blocks.Count - 1);
        }

        var untouched = Snap(document, "typing");

        Assert.Null(editor.History.Undo());

        Assert.Equal(untouched, Snap(document, "typing"));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void ARecordThatFailsMidwayLeavesTheDocumentAsItWas()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        Assert.True(Changed(editor.InsertText(new TextRange(At(document, 3, 1), At(document, 3, 1)), "x")));
        Assert.Equal(AttachOutcome.Attached, editor.History.AttachDerived(TwoAbove(document, "T")));
        Assert.Equal(AttachOutcome.Attached, editor.History.AttachDerived(TwoAbove(document, "U")));

        // Three blocks left: the second record (made last, reverted first) fits, the first one then does not.
        while (document.Body.Blocks.Count > 3)
        {
            document.Body.Blocks.RemoveAt(document.Body.Blocks.Count - 1);
        }

        var untouched = Snap(document, "typing");

        Assert.Null(editor.History.Undo());

        Assert.Equal(untouched, Snap(document, "typing"));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void TwoRecordsOnOneStepAreRevertedLastToFirst()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        var before = Snap(document, "typing");
        Assert.True(Changed(editor.InsertText(new TextRange(At(document, 3, 1), At(document, 3, 1)), "x")));
        var step = Snap(document, "typing");

        Assert.Equal(AttachOutcome.Attached, editor.History.AttachDerived(TwoAbove(document, "T")));
        Assert.Equal(AttachOutcome.Attached, editor.History.AttachDerived(TwoAbove(document, "U")));
        var both = Snap(document, "typing");

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Snap(document, "typing"));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(both, Snap(document, "typing"));
        Assert.NotEqual(step, both);
    }

    [Fact]
    public void ARecordIsNotAnEditSoTheHistoryKeepsItsStepsAndEditsBehindTheBackStillClearIt()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        Assert.True(Changed(editor.InsertText(new TextRange(At(document, 3, 1), At(document, 3, 1)), "x")));
        Assert.Equal(AttachOutcome.Attached, editor.History.AttachDerived(TwoAbove(document)));
        Assert.True(editor.History.CanUndo);

        document.Edits.Raise();

        Assert.False(editor.History.CanUndo);
        Assert.Equal(AttachOutcome.NothingToProtect, editor.History.AttachDerived(TwoAbove(document)));
    }
}
