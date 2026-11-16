using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// Paragraphs inside a block content control (<c>w:sdt</c>) are edited like any other: the control keeps its element, its text is editable, and its content list is a container of
/// its own, like a table cell (a range does not cross its boundary, nothing merges across it, a copy never carries the control).
/// </summary>
public sealed class ContentControlEditingTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static Paragraph P(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(run);
        return paragraph;
    }

    private static ContentControlBlock Control(params Block[] blocks)
    {
        var control = new ContentControlBlock();
        foreach (var block in blocks)
        {
            control.Content.Add(block);
        }

        return control;
    }

    private static Table CellOf(Block block)
    {
        var cell = new TableCell();
        cell.Blocks.Add(block);
        var row = new TableRow();
        row.Items.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        return table;
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

    private static string[] Texts(IEnumerable<Block> blocks) => blocks.OfType<Paragraph>().Select(Text).ToArray();

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain) { FragmentCodec = new DocxFragmentCodec() };

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Caret(Paragraph paragraph, int offset) => new(At(paragraph, offset), At(paragraph, offset));

    private static TextRange Sel(Paragraph paragraph, int start, int end) => new(At(paragraph, start), At(paragraph, end));

    private static TextRange Sel(Paragraph first, int start, Paragraph second, int end) => new(At(first, start), At(second, end));

    private static (WordDocument Document, Paragraph Before, ContentControlBlock Control, Paragraph One, Paragraph Two, Paragraph After) Sample()
    {
        var before = P("Before");
        var one = P("Inside one");
        var two = P("Inside two");
        var control = Control(one, two);
        var after = P("After");
        return (Doc(before, control, after), before, control, one, two, after);
    }

    [Fact]
    public void TypingInAParagraphOfAControlWorksAndKeepsTheControl()
    {
        var (doc, _, control, one, _, _) = Sample();
        var editor = Editor(doc);

        var result = editor.InsertText(Caret(one, 6), "X");

        Assert.Equal("InsideX one", Text(one));
        Assert.Equal(At(one, 7), result.Caret);
        Assert.Same(control, doc.Body.Blocks[1]);
        Assert.Same(control.Content, result.Changes[0].Container);
    }

    [Fact]
    public void EnterInAControlSplitsInsideTheControl()
    {
        var (doc, _, control, one, _, _) = Sample();
        var editor = Editor(doc);

        var result = editor.InsertText(Caret(one, 6), "\n");

        Assert.Equal(3, control.Content.Count);
        Assert.Equal(["Inside", " one", "Inside two"], Texts(control.Content));
        Assert.Equal(3, doc.Body.Blocks.Count);
        Assert.Same(control.Content, result.Changes[0].Container);
    }

    [Fact]
    public void BackspaceAtTheStartOfASecondParagraphMergesInsideTheControlAndTheFirstOneStopsAtTheEdge()
    {
        var (doc, _, control, one, two, _) = Sample();
        var editor = Editor(doc);

        var merged = editor.Backspace(At(two, 0), DeleteUnit.Cluster);

        Assert.NotNull(merged);
        Assert.Equal(["Inside oneInside two"], Texts(control.Content));

        // The first paragraph of the control has no neighbour inside it: the control's edge is a boundary, like the edge of a cell.
        var revision = doc.Edits.Revision;
        Assert.Null(editor.Backspace(At(control.Content.OfType<Paragraph>().First(), 0), DeleteUnit.Cluster));
        Assert.Equal(revision, doc.Edits.Revision);
        Assert.Same(control, doc.Body.Blocks[1]);
        Assert.NotNull(one);
    }

    [Fact]
    public void DeleteForwardAtTheEndOfTheLastParagraphOfAControlStopsAtTheEdge()
    {
        var (doc, _, control, _, two, _) = Sample();
        var editor = Editor(doc);

        var revision = doc.Edits.Revision;
        Assert.Null(editor.DeleteForward(At(two, Text(two).Length), DeleteUnit.Cluster));
        Assert.Equal(revision, doc.Edits.Revision);
        Assert.Equal(["Inside one", "Inside two"], Texts(control.Content));
    }

    [Fact]
    public void ADeleteAcrossTwoParagraphsOfTheControlMergesThemAndAnAnchorOfARemovedMiddleParagraphMoves()
    {
        var one = P("Alpha");
        var middle = P("Middle");
        var three = P("Gamma");
        var control = Control(one, middle, three);
        var doc = Doc(control);
        var editor = Editor(doc);

        var result = editor.Delete(Sel(one, 2, three, 2));

        Assert.NotNull(result.Changes);
        Assert.Equal(["Almma"], Texts(control.Content));
    }

    [Fact]
    public void ARangeThatCrossesTheBoundaryOfTheControlIsRefusedAndNothingChanges()
    {
        var (doc, before, control, one, _, after) = Sample();
        var editor = Editor(doc);
        var revision = doc.Edits.Revision;

        Assert.Empty(editor.Delete(Sel(before, 3, one, 3)).Changes);
        Assert.Empty(editor.Delete(Sel(one, 3, after, 2)).Changes);
        Assert.Empty(editor.InsertText(Sel(before, 3, one, 3), "x").Changes);

        Assert.Equal(revision, doc.Edits.Revision);
        Assert.Equal("Before", Text(before));
        Assert.Equal(["Inside one", "Inside two"], Texts(control.Content));
        Assert.Equal("After", Text(after));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void ARangeAcrossTheWholeControlRemovesTheControlLikeAnyOtherBlock()
    {
        var (doc, before, control, _, _, after) = Sample();
        var editor = Editor(doc);

        // The body-level range runs over the control as a middle block: the whole control element is removed with its text.
        var result = editor.Delete(Sel(before, 3, after, 2));

        Assert.NotNull(result);
        Assert.DoesNotContain(control, doc.Body.Blocks);
        Assert.Equal(["Befter"], Texts(doc.Body.Blocks));

        // Undo brings back a clone of the control with both paragraphs.
        editor.History.Undo();
        Assert.Equal(3, doc.Body.Blocks.Count);
        var restored = Assert.IsType<ContentControlBlock>(doc.Body.Blocks[1]);
        Assert.Equal(["Inside one", "Inside two"], Texts(restored.Content));
    }

    [Fact]
    public void CopyInsideTheControlAndPasteIntoTheBodyNeverCarriesTheControl()
    {
        var (doc, before, control, one, two, _) = Sample();
        var editor = Editor(doc);

        var content = editor.Copy(Sel(one, 7, two, 6));
        Assert.Equal("one\nInside", content.PlainText);

        editor.Paste(Caret(before, 6), content, _ => true);
        var texts = Texts(doc.Body.Blocks);
        Assert.Equal("Beforeone", texts[0]);
        Assert.Equal("Inside", texts[1]);
        Assert.Same(control, Assert.Single(doc.Body.Blocks.OfType<ContentControlBlock>()));
    }

    [Fact]
    public void PasteIntoAParagraphOfTheControlWorks()
    {
        var (doc, before, control, one, _, _) = Sample();
        var editor = Editor(doc);
        var content = editor.Copy(Sel(before, 0, 6));

        editor.Paste(Caret(one, 0), content, _ => true);

        Assert.Equal("BeforeInside one", Text(one));
        Assert.Equal(2, control.Content.Count);
    }

    [Fact]
    public void UndoAndRedoOfEditsInsideTheControlAddressTheRightListAfterAClone()
    {
        var (doc, before, control, one, two, after) = Sample();
        var editor = Editor(doc);

        // Step 1 types in the control, step 2 removes the whole control (undo makes a clone of it), step 3 types in the body.
        editor.InsertText(Caret(two, 10), "!");
        Assert.Equal("Inside two!", Text(two));
        editor.Delete(Sel(before, 3, after, 2));
        editor.InsertText(Caret(doc.Body.Blocks.OfType<Paragraph>().First(), 0), "#");
        Assert.Equal(["#Befter"], Texts(doc.Body.Blocks));

        editor.History.Undo();
        editor.History.Undo();
        var restored = Assert.IsType<ContentControlBlock>(doc.Body.Blocks[1]);
        Assert.NotSame(control, restored);
        Assert.Equal(["Inside one", "Inside two!"], Texts(restored.Content));

        // Step 1 now lives in the clone: its undo must find the new content list.
        var undone = editor.History.Undo()!;
        Assert.Equal(["Inside one", "Inside two"], Texts(restored.Content));
        Assert.Same(restored.Content, undone.Changes[0].Container);

        var redone = editor.History.Redo()!;
        Assert.Equal(["Inside one", "Inside two!"], Texts(restored.Content));
        Assert.Same(restored.Content, redone.Changes[0].Container);
        Assert.NotNull(one);
    }

    [Fact]
    public void AControlInsideACellAndACellInsideAControlAreBothReachable()
    {
        var inner = P("In the cell in the control");
        var table = CellOf(inner);
        var outerControl = Control(table);
        var cellParagraph = P("In the control in the cell");
        var cellControl = Control(cellParagraph);
        var cell = new TableCell();
        cell.Blocks.Add(cellControl);
        var row = new TableRow();
        row.Items.Add(cell);
        var otherTable = new Table();
        otherTable.Rows.Add(row);
        var doc = Doc(outerControl, otherTable);
        var editor = Editor(doc);

        editor.InsertText(Caret(inner, 0), "a");
        editor.InsertText(Caret(cellParagraph, 0), "b");
        Assert.Equal("aIn the cell in the control", Text(inner));
        Assert.Equal("bIn the control in the cell", Text(cellParagraph));

        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(editor.History.Undo());

        // Undo replaces the paragraphs by clones: read them again from the structure.
        Assert.Equal(["In the cell in the control"], Texts(table.Rows.OfType<TableRow>().Single().Items.OfType<TableCell>().Single().Blocks));
        Assert.Equal(["In the control in the cell"], Texts(cellControl.Content));
    }

    [Fact]
    public void TextUnitsFindsTheContainerAndTheIndexOfAParagraphInsideAControl()
    {
        var (doc, before, control, one, two, _) = Sample();
        var blocks = doc.Body.Blocks;

        Assert.Same(control.Content, TextUnits.ContainerOf(blocks, one));
        Assert.Same(control.Content, TextUnits.ContainerOf(blocks, two));
        Assert.Same(blocks, TextUnits.ContainerOf(blocks, before));
        Assert.Equal(1, TextUnits.IndexUnder(blocks, two));
        Assert.Equal(0, TextUnits.IndexUnder(blocks, before));
        Assert.Equal(1, TextUnits.IndexUnder(control.Content, two));
        Assert.Equal(-1, TextUnits.IndexUnder(control.Content, before));
        Assert.Null(TextUnits.ContainerOf(blocks, P("elsewhere")));
    }

    [Fact]
    public void ABookmarkOfAParagraphInsideARemovedControlMovesToTheNearestRemainingPosition()
    {
        var (doc, before, control, _, _, after) = Sample();
        var anchor = new BookmarkStart(7, "inside");
        control.Content.OfType<Paragraph>().First().Inlines.Insert(0, anchor);
        var editor = Editor(doc);

        editor.Delete(Sel(before, 3, after, 2));

        var kept = doc.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<BookmarkStart>()).ToArray();
        Assert.Single(kept);
        Assert.Equal("inside", kept[0].Name);
    }
}
