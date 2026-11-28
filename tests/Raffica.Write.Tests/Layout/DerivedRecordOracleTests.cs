using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// An undo or a redo of a step that carries a derived record (a structural field refresh) tells the layout the derived changes and the step's own changes in the order <see cref="EditResult.ChangesInApplyOrder"/> gives (the order the session uses),
/// and the layout equals a full layout after each (milestone 4.5b, task 6). Nothing attaches a record yet, so the record is made by hand: two paragraphs that a refresh would have added to a list.
/// </summary>
public sealed class DerivedRecordOracleTests
{
    private static string P(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static (WordDocument Model, TextEditor Editor) Document()
    {
        var model = LayoutModel.Document(P("Intro") + P("alpha") + P("beta") + P("gamma") + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>");
        return (model, new TextEditor(model, (_, _) => ProjectionHost.Plain));
    }

    private static void Tell(DocumentLayout layout, EditResult result)
    {
        foreach (var change in result.ChangesInApplyOrder())
        {
            layout.ApplyEdit(change, 2);
        }
    }

    // A step, then the record: two paragraphs inserted behind the step's block, as a refresh of a table of contents would.
    private static BlockChange AddTwoParagraphs(WordDocument model, TextEditor editor, DocumentLayout layout, out AttachOutcome outcome)
    {
        var extra = LayoutModel.Document(P("entry one") + P("entry two"));
        var first = 2;
        model.Body.Blocks.Insert(first, extra.Body.Blocks[0].Clone());
        model.Body.Blocks.Insert(first + 1, extra.Body.Blocks[1].Clone());
        var change = new BlockChange(StoryRef.Body, model.Body.Blocks, first, 0, 2, []);
        model.Edits.RaiseDerived();
        model.Edits.AttributeDerived([change]);
        layout.ApplyEdit(change, 2);
        outcome = editor.History.AttachDerived(new DerivedRecord([change]));
        return change;
    }

    [Fact]
    public async Task UndoAndRedoOfAStepCarryingARecordEqualAFullLayout()
    {
        var (model, editor) = Document();
        var layout = await SectionOracleTests.LaidAsync(model);
        var paragraph = (Paragraph)model.Body.Blocks[1];
        var caret = new TextPosition(StoryRef.Body, paragraph, 0);
        var typed = editor.InsertText(new TextRange(caret, caret), "typed ");
        Assert.NotNull(typed);
        Tell(layout, typed);
        LayoutOracle.AssertEqualToFull(layout, model, label: "after the step");

        AddTwoParagraphs(model, editor, layout, out var outcome);
        Assert.Equal(AttachOutcome.Attached, outcome);
        LayoutOracle.AssertEqualToFull(layout, model, label: "after the record");
        var revision = model.Edits.Revision;

        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        Assert.NotNull(undone.DerivedChanges);
        Assert.True(undone.DerivedFirst);
        Tell(layout, undone);
        LayoutOracle.AssertEqualToFull(layout, model, label: "after the undo");
        Assert.Equal(4, model.Body.Blocks.Count(b => b is Paragraph));

        var redone = editor.History.Redo();
        Assert.NotNull(redone);
        Assert.NotNull(redone.DerivedChanges);
        Assert.False(redone.DerivedFirst);
        Tell(layout, redone);
        LayoutOracle.AssertEqualToFull(layout, model, label: "after the redo");
        Assert.Equal(6, model.Body.Blocks.Count(b => b is Paragraph));
        Assert.Equal(revision, model.Edits.Revision);

        // Undoing again goes back over the record and the step in one go, and the layout still agrees.
        Tell(layout, editor.History.Undo()!);
        LayoutOracle.AssertEqualToFull(layout, model, label: "after the second undo");
    }

    // H1, three paragraphs and the final section: the heading switch is a style step, whose undo is refused when the style sheet is gone.
    private static string HeadingStyles()
    {
        var text = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        for (var i = 1; i <= 9; i++)
        {
            text += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        return LayoutModel.StylesPart(text);
    }

    [Fact]
    public async Task ARefusedUndoThatRolledARecordBackRaisesTheDerivedVersionAndAFreshLayoutStepEqualsAFullLayout()
    {
        var model = LayoutModel.Document(
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Chapter</w:t></w:r></w:p>" + P("alpha") + P("beta") + P("gamma") + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>",
            HeadingStyles(),
            LayoutModel.NumberingPart(string.Empty));
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(model));
        var layout = await SectionOracleTests.LaidAsync(model);
        var heading = new TextPosition(StoryRef.Body, (Paragraph)model.Body.Blocks[0], 0);
        Assert.NotNull(editor.SetHeadingNumbering(true, heading));
        layout.Restyle(2);
        AddTwoParagraphs(model, editor, layout, out var outcome);
        Assert.Equal(AttachOutcome.Attached, outcome);
        var before = model.Edits.Stamp;
        model.Styles = null;

        var refused = editor.History.Undo();

        // Nothing is undone, the blocks the record had swapped out and back are equal clones now, and only the derived version says so: the session starts the layout over for that.
        Assert.Null(refused);
        Assert.Equal(before.Revision, model.Edits.Revision);
        Assert.True(model.Edits.DerivedVersion > before.DerivedVersion);
        layout.Restyle(2);
        LayoutOracle.AssertEqualToFull(layout, model, label: "after the fresh layout step");
    }
}
