using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// Revision and dirty state follow the history for every structure operation (milestone 4.4a, review focus 4): after a save an operation makes the document dirty and its undo makes it
/// clean again, typing and operations undo and redo in order, and an edit behind the history's back with a structure step on the stack clears the history instead of undoing blindly.
/// </summary>
public sealed class StructureHistoryTests
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

    // H1 A, p, H2 B, p, H1 C, p, p, and a final section.
    private static WordDocument NewDocument() => LayoutModel.Document(
        P("A", "Heading1") + P("a") + P("B", "Heading2") + P("b") + P("C", "Heading1") + P("c") + P("d")
            + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>",
        Styles(),
        LayoutModel.NumberingPart(string.Empty));

    private static TextEditor EditorOf(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document));

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, (Paragraph)document.Body.Blocks[block], offset);

    private static TextRange Range(WordDocument document, int first, int last) =>
        new(At(document, first), At(document, last, TestEdit.TextOf((Paragraph)document.Body.Blocks[last]).Length));

    /// <summary>The structure operations, each as a delegate over a prepared document; ChangeLevel and Restart need a list, which the preparation makes before the document is saved.</summary>
    public static TheoryData<string> Operations() => ["Move", "Promote", "Demote", "DeleteHeading", "InsertHeading", "HeadingNumbering", "ToggleList", "ChangeLevel", "Restart", "Continue"];

    private static void Prepare(string name, WordDocument document, TextEditor editor)
    {
        if (name is "ChangeLevel" or "Restart" or "Continue")
        {
            Assert.NotEmpty(editor.ToggleList(Range(document, 1, 1), ListKind.Numbered).Changes);
            Assert.NotEmpty(editor.ToggleList(Range(document, 3, 3), ListKind.Numbered).Changes);
            if (name == "Continue")
            {
                Assert.NotNull(editor.RestartNumbering(At(document, 3)));
            }
        }
    }

    private static EditResult? Run(string name, WordDocument document, TextEditor editor) => name switch
    {
        "Move" => editor.MoveBlocks(0, 2, 7),
        "Promote" => editor.ShiftOutlineLevels(2, 2, -1),
        "Demote" => editor.ShiftOutlineLevels(0, 3, 1),
        "DeleteHeading" => editor.Delete(new TextRange(At(document, 2), At(document, 3))),
        "InsertHeading" => editor.SplitParagraph(new TextRange(At(document, 3, 1), At(document, 3, 1))),
        "HeadingNumbering" => editor.SetHeadingNumbering(true, At(document, 0)),
        "ToggleList" => editor.ToggleList(Range(document, 1, 3), ListKind.Bullet),
        "ChangeLevel" => editor.ChangeListLevel(Range(document, 3, 3), 1),
        "Restart" => editor.RestartNumbering(At(document, 3)),
        _ => editor.ContinueNumbering(At(document, 3)),
    };

    private static bool Changed(EditResult? result) => result is not null && (result.Changes.Count > 0 || result.Style is not null);

    [Theory]
    [MemberData(nameof(Operations))]
    public void AfterASaveAnOperationMakesTheDocumentDirtyAndItsUndoMakesItCleanAgain(string name)
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        Prepare(name, document, editor);
        document.Edits.MarkSaved();
        var saved = document.Edits.Revision;
        Assert.False(document.Edits.IsDirty);

        var result = Run(name, document, editor);

        Assert.True(Changed(result), name + " changed nothing");
        Assert.True(document.Edits.IsDirty);
        var after = document.Edits.Revision;
        Assert.NotEqual(saved, after);

        Assert.True(Changed(editor.History.Undo()), name + " undo changed nothing");
        Assert.False(document.Edits.IsDirty);
        Assert.Equal(saved, document.Edits.Revision);

        Assert.True(Changed(editor.History.Redo()), name + " redo changed nothing");
        Assert.True(document.Edits.IsDirty);
        Assert.Equal(after, document.Edits.Revision);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void TypingThenAnOperationThenUndoAndRedoFollowTheHistory(string name)
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        Prepare(name, document, editor);
        var prepared = editor.History.UndoCount;
        document.Edits.MarkSaved();
        var saved = document.Edits.Revision;

        var at = At(document, 1, 1);
        Assert.True(Changed(editor.InsertText(new TextRange(at, at), "xy")));
        var typed = document.Edits.Revision;
        Assert.True(Changed(Run(name, document, editor)), name + " changed nothing");
        var operated = document.Edits.Revision;
        Assert.True(typed != saved && operated != typed);
        Assert.Equal(prepared + 2, editor.History.UndoCount);

        Assert.True(Changed(editor.History.Undo()));
        Assert.Equal(typed, document.Edits.Revision);
        Assert.True(document.Edits.IsDirty);
        Assert.True(Changed(editor.History.Undo()));
        Assert.Equal(saved, document.Edits.Revision);
        Assert.False(document.Edits.IsDirty);

        Assert.True(Changed(editor.History.Redo()));
        Assert.Equal(typed, document.Edits.Revision);
        Assert.True(Changed(editor.History.Redo()));
        Assert.Equal(operated, document.Edits.Revision);
        Assert.True(document.Edits.IsDirty);
    }

    [Fact]
    public void SeveralOperationsUndoAndRedoInOrderThroughTheSavedRevision()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        document.Edits.MarkSaved();
        var saved = document.Edits.Revision;
        var revisions = new List<int> { saved };

        foreach (var name in new[] { "Promote", "HeadingNumbering", "Move", "ToggleList" })
        {
            Assert.True(Changed(Run(name, document, editor)), name + " changed nothing");
            revisions.Add(document.Edits.Revision);
        }

        Assert.Equal(revisions.Count, revisions.Distinct().Count());
        for (var i = revisions.Count - 2; i >= 0; i--)
        {
            Assert.True(Changed(editor.History.Undo()));
            Assert.Equal(revisions[i], document.Edits.Revision);
            Assert.Equal(i != 0, document.Edits.IsDirty);
        }

        for (var i = 1; i < revisions.Count; i++)
        {
            Assert.True(Changed(editor.History.Redo()));
            Assert.Equal(revisions[i], document.Edits.Revision);
        }

        // A new edit after the undos is a revision never seen before: it cannot look like the saved state it replaced.
        for (var i = 0; i < 4; i++)
        {
            editor.History.Undo();
        }

        Assert.True(Changed(Run("InsertHeading", document, editor)));
        Assert.DoesNotContain(document.Edits.Revision, revisions);
        Assert.True(document.Edits.IsDirty);
    }

    [Theory]
    [InlineData("Move")]
    [InlineData("HeadingNumbering")]
    [InlineData("Promote")]
    public void AnEditBehindTheHistorysBackWithAStructureStepOnTheStackClearsTheHistory(string name)
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        Assert.True(Changed(Run(name, document, editor)));
        Assert.True(editor.History.CanUndo);
        var texts = string.Join("|", document.Body.Blocks.OfType<Paragraph>().Select(TestEdit.TextOf));
        var styles = string.Join("|", document.Body.Blocks.OfType<Paragraph>().Select(p => p.Properties.StyleId));
        var heading1 = document.Styles!.Find("Heading1")!.ParagraphProperties.NumberingId;

        document.Edits.Raise();

        Assert.Null(editor.History.Undo());
        Assert.False(editor.History.CanUndo);
        Assert.False(editor.History.CanRedo);
        Assert.Null(editor.History.Redo());

        // Nothing was undone blindly: the document is as the step left it.
        Assert.Equal(texts, string.Join("|", document.Body.Blocks.OfType<Paragraph>().Select(TestEdit.TextOf)));
        Assert.Equal(styles, string.Join("|", document.Body.Blocks.OfType<Paragraph>().Select(p => p.Properties.StyleId)));
        Assert.Equal(heading1, document.Styles.Find("Heading1")!.ParagraphProperties.NumberingId);
    }

    [Fact]
    public void AMoveLeavesTheFinalSectionAlone()
    {
        var document = NewDocument();
        var editor = EditorOf(document);
        var section = document.Body.FinalSection;
        Assert.NotNull(section);

        Assert.True(Changed(editor.MoveBlocks(0, 2, 7)));
        Assert.Same(section, document.Body.FinalSection);

        Assert.True(Changed(editor.History.Undo()));
        Assert.Same(section, document.Body.FinalSection);
        Assert.True(Changed(editor.History.Redo()));
        Assert.Same(section, document.Body.FinalSection);
    }
}
