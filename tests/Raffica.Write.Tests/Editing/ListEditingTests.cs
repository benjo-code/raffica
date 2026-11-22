using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class ListEditingTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // Abstract 0 is a bullet list (num 1), abstract 1 a numbered one (num 2) with indents per level.
    private const string Definitions =
        "<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"hybridMultilevel\"/>"
        + "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"o\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl>"
        + "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"o\"/><w:pPr><w:ind w:left=\"1440\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>"
        + "<w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"hybridMultilevel\"/>"
        + "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl>"
        + "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.%2.\"/><w:pPr><w:ind w:left=\"1440\" w:hanging=\"360\"/></w:pPr></w:lvl></w:abstractNum>"
        + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>";

    private static (WordDocument Document, TextEditor Editor, LayoutEffectiveFormat Format) Setup(string body, string? numbering = null, bool noNumbering = false, string extraStyles = "")
    {
        var styles = LayoutModel.StylesPart(
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr></w:style>"
            + "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:tblPr/></w:style>" + extraStyles);
        var document = LayoutModel.Document(body, styles, noNumbering ? null : numbering ?? LayoutModel.NumberingPart(Definitions));
        var format = new LayoutEffectiveFormat(document);
        return (document, new TextEditor(document, (_, _) => ProjectionHost.Plain, format: format), format);
    }

    private static string Para(string text, string props = "") =>
        $"<w:p>{(props.Length == 0 ? string.Empty : $"<w:pPr>{props}</w:pPr>")}<w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string InList(int numId, int level = 0) =>
        $"<w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{numId}\"/></w:numPr>";

    private static Paragraph P(WordDocument document, int index) => (Paragraph)document.Body.Blocks[index];

    private static TextPosition At(Paragraph paragraph, int offset = 0) => new(StoryRef.Body, paragraph, offset);

    private static TextRange Sel(Paragraph first, Paragraph last) => new(At(first), At(last, 1));

    private static int NumId(Paragraph paragraph) => paragraph.Properties.NumberingId ?? 0;

    private static int Level(Paragraph paragraph) => paragraph.Properties.NumberingLevel ?? 0;

    private static string[] Labels(WordDocument document) => ReferenceNumbering.Labels(document).Select(l => l.Label).ToArray();

    private static List<string> Snapshot(WordDocument document) =>
        document.Body.Blocks.Select(b => b.ToString() ?? string.Empty).ToList();

    private static string PropsOf(Paragraph paragraph) =>
        string.Concat(paragraph.Properties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting)));

    private static Paragraph[] CellOf(WordDocument document, int tableIndex = 1) =>
        ((TableCell)((TableRow)((Table)document.Body.Blocks[tableIndex]).Rows[0]).Items[0]).Blocks.OfType<Paragraph>().ToArray();

    private static AbstractNumbering AbstractOf(WordDocument document, int numId) =>
        document.Numbering!.FindAbstract(document.Numbering.FindInstance(numId)!.AbstractNumId!.Value)!;

    [Fact]
    public void TogglingBulletsOnPlainParagraphsGivesThemNumPrAndListParagraph()
    {
        var (document, editor, _) = Setup(Para("one") + Para("two") + Para("three"));

        var result = editor.ToggleList(Sel(P(document, 0), P(document, 1)), ListKind.Bullet);

        Assert.Single(result.Changes);
        Assert.True(result.KeepSelection);
        var numId = NumId(P(document, 0));
        Assert.True(numId > 0);
        Assert.Equal(numId, NumId(P(document, 1)));
        Assert.Equal("bullet", AbstractOf(document, numId).Levels.First(l => l.Level == 0).Format);
        Assert.Equal(0, Level(P(document, 0)));
        Assert.Equal("ListParagraph", P(document, 0).Properties.StyleId);
        Assert.Equal("ListParagraph", P(document, 1).Properties.StyleId);
        Assert.Equal(0, NumId(P(document, 2)));
        Assert.Null(P(document, 2).Properties.StyleId);
    }

    [Fact]
    public void TogglingTheSameKindAgainRemovesNumPrAndKeepsTheStyle()
    {
        var (document, editor, _) = Setup(Para("one") + Para("two"));
        var selection = Sel(P(document, 0), P(document, 1));
        editor.ToggleList(selection, ListKind.Bullet);
        var instances = document.Numbering!.Instances.Count;

        var result = editor.ToggleList(selection, ListKind.Bullet);

        Assert.Single(result.Changes);
        Assert.Null(P(document, 0).Properties.Get(W + "numPr"));
        Assert.Null(P(document, 1).Properties.Get(W + "numPr"));
        Assert.Equal("ListParagraph", P(document, 0).Properties.StyleId);
        Assert.Equal(instances, document.Numbering.Instances.Count);

        // Toggling on again takes the unused instance back instead of adding another.
        editor.ToggleList(selection, ListKind.Bullet);
        Assert.Equal(instances, document.Numbering.Instances.Count);
    }

    [Fact]
    public void TogglingTheOtherKindSwitchesTheList()
    {
        var (document, editor, _) = Setup(Para("one") + Para("two"));
        var selection = Sel(P(document, 0), P(document, 1));
        editor.ToggleList(selection, ListKind.Bullet);

        editor.ToggleList(selection, ListKind.Numbered);

        var numId = NumId(P(document, 0));
        Assert.Equal("decimal", AbstractOf(document, numId).Levels.First(l => l.Level == 0).Format);
        Assert.Equal(numId, NumId(P(document, 1)));
        Assert.Equal(["1.", "2."], Labels(document));
        Assert.Equal("ListParagraph", P(document, 0).Properties.StyleId);
    }

    [Fact]
    public void AHeadingKeepsItsStyleWhenItBecomesAListItem()
    {
        var (document, editor, _) = Setup(Para("Title", "<w:pStyle w:val=\"Heading1\"/>"));

        editor.ToggleList(Sel(P(document, 0), P(document, 0)), ListKind.Numbered);

        Assert.Equal("Heading1", P(document, 0).Properties.StyleId);
        Assert.True(NumId(P(document, 0)) > 0);
        Assert.Equal(["1."], Labels(document));
    }

    [Fact]
    public void ARunOfParagraphsGetsOneNewInstanceSoItNumbersFromOne()
    {
        var (document, editor, _) = Setup(Para("a", InList(2)) + Para("b", InList(2)) + Para("x") + Para("c") + Para("d"));
        Assert.Equal(["1.", "2."], Labels(document));
        var instances = document.Numbering!.Instances.Count;

        editor.ToggleList(Sel(P(document, 3), P(document, 4)), ListKind.Numbered);

        Assert.Equal(instances + 1, document.Numbering.Instances.Count);
        var numId = NumId(P(document, 3));
        Assert.NotEqual(2, numId);
        Assert.Equal(numId, NumId(P(document, 4)));
        Assert.Equal(["1.", "2.", "1.", "2."], Labels(document));
    }

    [Fact]
    public void TabLevelChangeFollowsTheLevelIndent()
    {
        var (document, editor, format) = Setup(Para("one") + Para("two"));
        editor.ToggleList(Sel(P(document, 0), P(document, 1)), ListKind.Numbered);
        Assert.Equal(36f, format.ResolveParagraph(StoryRef.Body, P(document, 0)).LeftIndent);

        var result = editor.ChangeListLevel(Sel(P(document, 0), P(document, 0)), 1);

        Assert.NotNull(result);
        Assert.Single(result.Changes);
        Assert.Equal(1, Level(P(document, 0)));
        Assert.Equal(0, Level(P(document, 1)));
        Assert.Equal(72f, new LayoutEffectiveFormat(document).ResolveParagraph(StoryRef.Body, P(document, 0)).LeftIndent);

        editor.ChangeListLevel(Sel(P(document, 0), P(document, 0)), 20);
        Assert.Equal(8, Level(P(document, 0)));
        editor.ChangeListLevel(Sel(P(document, 0), P(document, 0)), -20);
        Assert.Equal(0, Level(P(document, 0)));

        var plain = Setup(Para("plain"));
        Assert.Null(plain.Editor.ChangeListLevel(Sel(P(plain.Document, 0), P(plain.Document, 0)), 1));
    }

    [Fact]
    public void RestartGivesTheRestOfTheListANewInstanceWithStartOverride()
    {
        var (document, editor, _) = Setup(Para("a") + Para("b") + Para("c") + Para("d"));
        editor.ToggleList(Sel(P(document, 0), P(document, 3)), ListKind.Numbered);
        var original = NumId(P(document, 0));
        Assert.Equal(["1.", "2.", "3.", "4."], Labels(document));

        var result = editor.RestartNumbering(At(P(document, 2)));

        Assert.NotNull(result);
        Assert.Equal(original, NumId(P(document, 0)));
        Assert.Equal(original, NumId(P(document, 1)));
        var restarted = NumId(P(document, 2));
        Assert.NotEqual(original, restarted);
        Assert.Equal(restarted, NumId(P(document, 3)));
        var over = Assert.Single(document.Numbering!.FindInstance(restarted)!.Overrides);
        Assert.Equal(0, over.Level);
        Assert.Equal(1, over.StartOverride);
        Assert.Equal(["1.", "2.", "1.", "2."], Labels(document));
    }

    [Fact]
    public void ContinuePointsBackAtThePreviousInstance()
    {
        var (document, editor, _) = Setup(Para("a") + Para("b") + Para("c") + Para("d"));
        editor.ToggleList(Sel(P(document, 0), P(document, 3)), ListKind.Numbered);
        var original = NumId(P(document, 0));
        editor.RestartNumbering(At(P(document, 2)));
        Assert.Equal(["1.", "2.", "1.", "2."], Labels(document));

        var result = editor.ContinueNumbering(At(P(document, 2)));

        Assert.NotNull(result);
        Assert.Equal(original, NumId(P(document, 2)));
        Assert.Equal(original, NumId(P(document, 3)));
        Assert.Equal(["1.", "2.", "3.", "4."], Labels(document));
    }

    [Fact]
    public void EnterOnAnEmptyListParagraphEndsTheList()
    {
        var (document, editor, _) = Setup(Para("a", InList(2)) + "<w:p><w:pPr>" + InList(2) + "</w:pPr></w:p>");
        var empty = P(document, 1);

        var result = editor.SplitParagraph(new TextRange(At(empty), At(empty)));

        Assert.Equal(2, document.Body.Blocks.Count);
        Assert.Same(empty, document.Body.Blocks[1]);
        Assert.Equal(0, NumId(empty));
        Assert.Equal(["1."], Labels(document));
        Assert.Single(result.Changes);
        Assert.Equal(1, editor.History.UndoCount);

        // A paragraph with text splits as before and the new paragraph stays in the list.
        var (other, otherEditor, _) = Setup(Para("ab", InList(2)));
        otherEditor.SplitParagraph(new TextRange(At(P(other, 0), 1), At(P(other, 0), 1)));
        Assert.Equal(2, other.Body.Blocks.Count);
        Assert.Equal(2, NumId(P(other, 1)));
    }

    [Fact]
    public void BackspaceAtTheStartRemovesTheListFirstAndKeepsTheText()
    {
        var (document, editor, _) = Setup(Para("a", InList(2)) + Para("bc", InList(2)));
        var second = P(document, 1);

        var result = editor.Backspace(At(second), DeleteUnit.Cluster);

        Assert.NotNull(result);
        Assert.Equal(2, document.Body.Blocks.Count);
        Assert.Equal(0, NumId(second));
        Assert.Equal("bc", string.Concat(second.Inlines.OfType<Run>().SelectMany(r => r.Content.OfType<TextContent>()).Select(t => t.Text)));
        Assert.Equal(["1."], Labels(document));

        // Now it is a plain paragraph: the next Backspace merges as before.
        editor.Backspace(At(second), DeleteUnit.Cluster);
        Assert.Single(document.Body.Blocks);
    }

    [Fact]
    public void EachOperationIsOneUndoStep()
    {
        var (document, editor, _) = Setup(Para("a") + Para("b") + Para("c"));
        var all = Sel(P(document, 0), P(document, 2));
        var states = new List<List<string>> { Snapshot(document) };

        editor.ToggleList(all, ListKind.Numbered);
        Assert.Equal(1, editor.History.UndoCount);
        states.Add(Snapshot(document));
        editor.ChangeListLevel(Sel(P(document, 1), P(document, 1)), 1);
        Assert.Equal(2, editor.History.UndoCount);
        states.Add(Snapshot(document));
        editor.RestartNumbering(At(P(document, 2)));
        Assert.Equal(3, editor.History.UndoCount);
        states.Add(Snapshot(document));
        editor.ContinueNumbering(At(P(document, 2)));
        Assert.Equal(4, editor.History.UndoCount);
        var done = Labels(document);
        states.Add(Snapshot(document));

        for (var i = states.Count - 2; i >= 0; i--)
        {
            Assert.NotNull(editor.History.Undo());
            Assert.Equal(states[i], Snapshot(document));
        }

        Assert.Empty(Labels(document));
        for (var i = 1; i < states.Count; i++)
        {
            Assert.NotNull(editor.History.Redo());
            Assert.Equal(states[i], Snapshot(document));
        }

        Assert.Equal(done, Labels(document));
        var numbering = document.Numbering!.Instances.Count;
        editor.History.Undo();
        editor.History.Undo();
        Assert.Equal(numbering, document.Numbering.Instances.Count);
    }

    [Fact]
    public void ListToggleAcrossAControlEdgeIsRefused()
    {
        var control = "<w:sdt><w:sdtPr/><w:sdtContent>" + Para("inside") + "</w:sdtContent></w:sdt>";
        var (document, editor, _) = Setup(Para("outside") + control);
        var inside = ((ContentControlBlock)document.Body.Blocks[1]).Content.OfType<Paragraph>().Single();

        var result = editor.ToggleList(new TextRange(At(P(document, 0)), At(inside, 1)), ListKind.Bullet);

        Assert.Empty(result.Changes);
        Assert.Null(P(document, 0).Properties.Get(W + "numPr"));
        Assert.Null(inside.Properties.Get(W + "numPr"));
        Assert.Equal(0, editor.History.UndoCount);

        // Inside the control it works like anywhere else.
        var ok = editor.ToggleList(new TextRange(At(inside), At(inside, 1)), ListKind.Bullet);
        Assert.Single(ok.Changes);
        Assert.True(NumId(inside) > 0);
        Assert.NotNull(editor.History.Undo());
        var restored = ((ContentControlBlock)document.Body.Blocks[1]).Content.OfType<Paragraph>().Single();
        Assert.Null(restored.Properties.Get(W + "numPr"));
    }

    [Fact]
    public void ListsInTableCellsWork()
    {
        var table = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc>" + Para("c1") + Para("c2") + "</w:tc></w:tr></w:tbl>";
        var (document, editor, _) = Setup(Para("before") + table + Para("after"));
        var cell = CellOf(document);

        var result = editor.ToggleList(new TextRange(At(cell[0]), At(cell[1], 1)), ListKind.Numbered);

        Assert.Single(result.Changes);
        Assert.True(NumId(cell[0]) > 0);
        Assert.Equal(NumId(cell[0]), NumId(cell[1]));
        Assert.Equal(0, NumId(P(document, 0)));

        // A restart in the cell moves the paragraph and what follows it, and the history step puts the caret back in the cell.
        Assert.NotNull(editor.RestartNumbering(At(cell[1])));
        Assert.NotEqual(NumId(cell[0]), NumId(cell[1]));
        Assert.Equal(2, editor.History.UndoCount);
        // Undo puts new paragraph objects in; read the cell again.
        Assert.NotNull(editor.History.Undo());
        var again = CellOf(document);
        Assert.Equal(NumId(again[0]), NumId(again[1]));
        Assert.NotEqual(0, NumId(again[0]));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(0, NumId(CellOf(document)[0]));
    }

    [Fact]
    public void ListFactsNameTheReasonOfEachDisabledCase()
    {
        var (document, editor, _) = Setup(Para("plain") + Para("a", InList(2)) + Para("b", InList(2)) + Para("c", InList(1)));

        var plain = editor.ReadListFacts(Sel(P(document, 0), P(document, 0)));
        Assert.False(plain.InList);
        Assert.Null(plain.Kind);
        Assert.False(plain.CanRestart);
        Assert.Equal(ListReason.NotInAList, plain.RestartReason);
        Assert.False(plain.CanContinue);
        Assert.Equal(ListReason.NotInAList, plain.ContinueReason);

        var numbered = editor.ReadListFacts(Sel(P(document, 1), P(document, 2)));
        Assert.True(numbered.InList);
        Assert.Equal(ListKind.Numbered, numbered.Kind);
        Assert.True(numbered.CanRestart);
        Assert.False(numbered.CanContinue);
        Assert.Equal(ListReason.NothingBefore, numbered.ContinueReason);

        var second = editor.ReadListFacts(Sel(P(document, 2), P(document, 2)));
        Assert.False(second.CanContinue);
        Assert.Equal(ListReason.AlreadyContinues, second.ContinueReason);

        var mixed = editor.ReadListFacts(Sel(P(document, 1), P(document, 3)));
        Assert.True(mixed.InList);
        Assert.Null(mixed.Kind);

        editor.RestartNumbering(At(P(document, 2)));
        var afterRestart = editor.ReadListFacts(Sel(P(document, 2), P(document, 2)));
        Assert.True(afterRestart.CanContinue);
        Assert.Equal(ListReason.None, afterRestart.ContinueReason);

        var bullet = editor.ReadListFacts(Sel(P(document, 3), P(document, 3)));
        Assert.Equal(ListKind.Bullet, bullet.Kind);
        Assert.False(bullet.CanContinue);
        Assert.Equal(ListReason.NothingBefore, bullet.ContinueReason);
    }

    [Fact]
    public void ListFactsOfALargeSelectionAreReadFromItsStartOnly()
    {
        var body = string.Concat(Enumerable.Range(0, TextEditor.MaxFactBlocks + 5).Select(i => Para("p" + i.ToString(CultureInfo.InvariantCulture), i == 0 ? InList(2) : string.Empty)));
        var (document, editor, _) = Setup(body);

        var facts = editor.ReadListFacts(Sel(P(document, 0), P(document, TextEditor.MaxFactBlocks + 4)));

        Assert.True(facts.InList);
        Assert.Equal(ListKind.Numbered, facts.Kind);
    }

    [Fact]
    public async Task AToggleOnADocumentWithoutANumberingPartGivesTheLayoutItsBullets()
    {
        var (document, editor, _) = Setup(Para("one") + Para("two") + Para("three"), noNumbering: true);
        Assert.Null(document.Numbering);
        var layout = DocumentLayout.Create(document, PageTestKit.Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, CancellationToken.None);

        var result = editor.ToggleList(Sel(P(document, 0), P(document, 1)), ListKind.Bullet);

        Assert.NotNull(document.Numbering);
        foreach (var change in result.Changes)
        {
            layout.ApplyEdit(change, 0);
        }

        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, document, label: "bullets in a document without numbering");

        // Undo leaves the numbering part in place and the layout equals a full one again; so does redo.
        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        foreach (var change in undone.Changes)
        {
            layout.ApplyEdit(change, 0);
        }

        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, document, label: "undone");
    }

    [Fact]
    public async Task LabelsAfterUndoAndRedoEqualTheReferenceCounter()
    {
        var (document, editor, _) = Setup(Para("a") + Para("b") + Para("c"));
        var layout = DocumentLayout.Create(document, PageTestKit.Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, CancellationToken.None);

        async Task Check(EditResult? result, string label)
        {
            Assert.NotNull(result);
            foreach (var change in result.Changes)
            {
                layout.ApplyEdit(change, 0);
            }

            await layout.LayOutRestAsync(null, CancellationToken.None);
            LayoutOracle.AssertEqualToFull(layout, document, label: label);
        }

        await Check(editor.ToggleList(Sel(P(document, 0), P(document, 2)), ListKind.Numbered), "toggle");
        await Check(editor.RestartNumbering(At(P(document, 1))), "restart");
        Assert.Equal(["1.", "1.", "2."], Labels(document));
        await Check(editor.History.Undo(), "undo restart");
        Assert.Equal(["1.", "2.", "3."], Labels(document));
        await Check(editor.History.Redo(), "redo restart");
        Assert.Equal(["1.", "1.", "2."], Labels(document));
        await Check(editor.ContinueNumbering(At(P(document, 1))), "continue");
        Assert.Equal(["1.", "2.", "3."], Labels(document));
    }

    private const string StyledLists =
        "<w:style w:type=\"paragraph\" w:styleId=\"NumHead\"><w:name w:val=\"Numbered Heading\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"2\"/></w:numPr></w:pPr></w:style>"
        + "<w:style w:type=\"paragraph\" w:styleId=\"ListBullet\"><w:name w:val=\"List Bullet\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>";

    private static string Styled(string style, string text) => Para(text, $"<w:pStyle w:val=\"{style}\"/>");

    private static string Filler(int count) => string.Concat(Enumerable.Range(0, count).Select(i => Para("filler" + i.ToString(CultureInfo.InvariantCulture))));

    private static string CellTable(string cellBody) =>
        "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc>" + cellBody + "</w:tc></w:tr></w:tbl>";

    private static (int NumId, int Level) Direct(Paragraph paragraph) => (paragraph.Properties.NumberingId ?? -1, paragraph.Properties.NumberingLevel ?? -1);

    private static string TextOf(Paragraph paragraph) =>
        string.Concat(paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content.OfType<TextContent>()).Select(t => t.Text));

    // A restart in a deep container stays in that container: a change over the list paragraphs only, the caret in them for the step, undo and redo.
    private static void AssertRestartStaysInItsContainer(WordDocument document, TextEditor editor, Func<Paragraph[]> items, StoryRef story)
    {
        var list = items();
        var result = editor.RestartNumbering(new TextPosition(story, list[1], 1));

        Assert.NotNull(result);
        var change = Assert.Single(result.Changes);
        var container = TextUnits.ContainerOf(TextUnits.BlocksOf(document, story)!, list[1]);
        Assert.Same(container, change.Container);
        Assert.Equal(1, change.First);
        Assert.Equal(2, change.Removed);
        Assert.Equal(2, change.Added);
        Assert.Equal(2, change.OldBlocks.Count);
        Assert.Same(list[1], result.Caret.Paragraph);
        Assert.NotEqual(NumId(list[0]), NumId(list[1]));

        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        Assert.Same(container, Assert.Single(undone.Changes).Container);
        Assert.Same(items()[1], undone.Caret.Paragraph);
        Assert.Equal(NumId(items()[0]), NumId(items()[1]));

        var redone = editor.History.Redo();
        Assert.NotNull(redone);
        Assert.Same(items()[1], redone.Caret.Paragraph);
        Assert.NotEqual(NumId(items()[0]), NumId(items()[1]));
    }

    [Fact]
    public void RestartInACellOfALateTableChangesOnlyTheCellParagraphs()
    {
        var (document, editor, _) = Setup(Filler(50) + CellTable(Para("c0") + Para("c1") + Para("c2")) + Para("after"));
        Paragraph[] Cell() => CellOf(document, 50);
        editor.ToggleList(new TextRange(At(Cell()[0]), At(Cell()[2], 1)), ListKind.Numbered);
        Assert.NotEqual(0, NumId(Cell()[0]));

        AssertRestartStaysInItsContainer(document, editor, Cell, StoryRef.Body);
    }

    [Fact]
    public void ContinueInACellOfALateTableChangesOnlyTheCellParagraphs()
    {
        var (document, editor, _) = Setup(Filler(50) + CellTable(Para("c0") + Para("c1") + Para("c2")) + Para("after"));
        Paragraph[] Cell() => CellOf(document, 50);
        editor.ToggleList(new TextRange(At(Cell()[0]), At(Cell()[2], 1)), ListKind.Numbered);
        editor.RestartNumbering(At(Cell()[1]));
        var restarted = NumId(Cell()[1]);

        var result = editor.ContinueNumbering(At(Cell()[1]));

        Assert.NotNull(result);
        var change = Assert.Single(result.Changes);
        Assert.Equal(1, change.First);
        Assert.Equal(2, change.Added);
        Assert.Same(Cell()[1], result.Caret.Paragraph);
        Assert.NotEqual(restarted, NumId(Cell()[1]));
        Assert.Equal(NumId(Cell()[0]), NumId(Cell()[1]));
        Assert.Equal(["1.", "2.", "3."], Labels(document));
        var undone = editor.History.Undo();
        Assert.Same(Cell()[1], undone!.Caret.Paragraph);
        Assert.Equal(["1.", "1.", "2."], Labels(document));
    }

    [Fact]
    public void RestartInABlockContentControlStaysInTheControl()
    {
        var control = "<w:sdt><w:sdtPr/><w:sdtContent>" + Para("s0") + Para("s1") + Para("s2") + "</w:sdtContent></w:sdt>";
        var (document, editor, _) = Setup(Filler(50) + control + Para("after"));
        Paragraph[] Items() => ((ContentControlBlock)document.Body.Blocks[50]).Content.OfType<Paragraph>().ToArray();
        editor.ToggleList(new TextRange(At(Items()[0]), At(Items()[2], 1)), ListKind.Numbered);

        AssertRestartStaysInItsContainer(document, editor, Items, StoryRef.Body);
    }

    [Fact]
    public void RestartInAHeaderStaysInTheHeader()
    {
        var (document, editor, _) = Setup(Para("body"));
        var story = new Story(new OoxmlRoot(W + "hdr", []));
        for (var i = 0; i < 3; i++)
        {
            var run = new Run();
            run.Content.Add(new TextContent("h" + i.ToString(CultureInfo.InvariantCulture)));
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(run);
            story.Blocks.Add(paragraph);
        }

        document.HeadersFooters["rIdH"] = new HeaderFooterPart(story);
        var head = new StoryRef(StoryKind.HeaderFooter, "rIdH");
        Paragraph[] Items() => story.Blocks.OfType<Paragraph>().ToArray();
        editor.ToggleList(new TextRange(new TextPosition(head, Items()[0], 0), new TextPosition(head, Items()[2], 1)), ListKind.Numbered);

        AssertRestartStaysInItsContainer(document, editor, Items, head);
    }

    [Fact]
    public void ARestartThatCrossesContainersTakesItsHistoryCaretFromATopLevelParagraph()
    {
        var (document, editor, _) = Setup(Filler(3) + CellTable(Para("c0")) + Para("p1") + Para("p2"));
        Paragraph CellParagraph() => CellOf(document, 3)[0];
        editor.ToggleList(new TextRange(At(P(document, 4)), At(P(document, 5), 1)), ListKind.Numbered);
        var numId = NumId(P(document, 4));
        // The list goes on from the cell into the body (set by hand: a selection never crosses the cell edge).
        SetList(CellParagraph(), numId);

        var result = editor.RestartNumbering(At(CellParagraph()));

        Assert.NotNull(result);
        var change = Assert.Single(result.Changes);
        Assert.Equal(3, change.First);
        Assert.Equal(3, change.Added);
        Assert.NotEqual(numId, NumId(P(document, 4)));
        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        Assert.Equal(numId, NumId(P(document, 4)));
        Assert.Same(P(document, 4), undone.Caret.Paragraph);
    }

    private static void SetList(Paragraph paragraph, int numId) =>
        paragraph.Properties.Set(W + "numPr", new XElement(
            W + "numPr",
            new XElement(W + "ilvl", new XAttribute(W + "val", 0)),
            new XElement(W + "numId", new XAttribute(W + "val", numId))));

    [Fact]
    public void TogglingOffAListThatTheStyleCarriesSwitchesItOffOnTheParagraphsOnly()
    {
        var (document, editor, _) = Setup(Styled("NumHead", "a") + Styled("NumHead", "b") + Styled("ListBullet", "c"), extraStyles: StyledLists);
        Assert.Equal(["1.", "2.", "o"], Labels(document));

        editor.ToggleList(Sel(P(document, 0), P(document, 1)), ListKind.Numbered);

        Assert.Equal((0, 0), Direct(P(document, 0)));
        Assert.Equal((0, 0), Direct(P(document, 1)));
        Assert.Equal("NumHead", P(document, 0).Properties.StyleId);
        Assert.Null(P(document, 2).Properties.Get(W + "numPr"));
        Assert.Equal(2, document.Styles!.Find("NumHead")!.ParagraphProperties.NumberingId);
        Assert.Equal(["o"], Labels(document));

        editor.ToggleList(Sel(P(document, 2), P(document, 2)), ListKind.Bullet);
        Assert.Equal((0, 0), Direct(P(document, 2)));
        Assert.Equal("ListBullet", P(document, 2).Properties.StyleId);
        Assert.Empty(Labels(document));
        Assert.Equal(2, editor.History.UndoCount);
    }

    [Fact]
    public void EnterOnAnEmptyNumberedHeadingEndsItsListWithADirectNumIdOfZero()
    {
        var (document, editor, _) = Setup(Styled("NumHead", "a") + "<w:p><w:pPr><w:pStyle w:val=\"NumHead\"/></w:pPr></w:p>", extraStyles: StyledLists);
        var empty = P(document, 1);

        var result = editor.SplitParagraph(new TextRange(At(empty), At(empty)));

        Assert.Single(result.Changes);
        Assert.Equal(2, document.Body.Blocks.Count);
        Assert.Equal((0, 0), Direct(empty));
        Assert.Equal("NumHead", empty.Properties.StyleId);
        Assert.Null(P(document, 0).Properties.Get(W + "numPr"));
        Assert.Equal(["1."], Labels(document));
    }

    [Fact]
    public void BackspaceAtTheStartOfANumberedHeadingSwitchesOffThatParagraphOnly()
    {
        var (document, editor, _) = Setup(Styled("NumHead", "a") + Styled("NumHead", "bc") + Styled("NumHead", "d"), extraStyles: StyledLists);
        var second = P(document, 1);
        Assert.Equal(["1.", "2.", "3."], Labels(document));

        var result = editor.Backspace(At(second), DeleteUnit.Cluster);

        Assert.NotNull(result);
        Assert.Equal(3, document.Body.Blocks.Count);
        var numPr = second.Properties.Get(W + "numPr")!;
        Assert.Equal(["ilvl", "numId"], numPr.Elements().Select(e => e.Name.LocalName).ToArray());
        Assert.Equal(["0", "0"], numPr.Elements().Select(e => (string?)e.Attribute(W + "val")).ToArray());
        Assert.Equal("NumHead", second.Properties.StyleId);
        Assert.Null(P(document, 0).Properties.Get(W + "numPr"));
        Assert.Null(P(document, 2).Properties.Get(W + "numPr"));
        Assert.Equal("bc", TextOf(second));
        Assert.Equal(["1.", "2."], Labels(document));
        Assert.Equal(2, document.Numbering!.Instances.Count);

        // No longer in a list: the next Backspace merges with the paragraph before it.
        editor.Backspace(At(second), DeleteUnit.Cluster);
        Assert.Equal(2, document.Body.Blocks.Count);
    }

    [Fact]
    public void TabOnAListThatTheStyleCarriesWritesADirectNumPrWithTheSameList()
    {
        var (document, editor, _) = Setup(Styled("NumHead", "a") + Styled("NumHead", "b"), extraStyles: StyledLists);

        var result = editor.ChangeListLevel(Sel(P(document, 1), P(document, 1)), 1);

        Assert.NotNull(result);
        Assert.Equal((2, 1), Direct(P(document, 1)));
        Assert.Null(P(document, 0).Properties.Get(W + "numPr"));
        Assert.Equal(["1.", "1.1."], Labels(document));

        Assert.NotNull(editor.ChangeListLevel(Sel(P(document, 1), P(document, 1)), -1));
        Assert.Equal((2, 0), Direct(P(document, 1)));
        Assert.Equal(["1.", "2."], Labels(document));
    }

    [Fact]
    public void EnterOnAnEmptyParagraphInATextControlKeepsItsOldBehaviour()
    {
        var control = "<w:sdt><w:sdtPr><w:text/></w:sdtPr><w:sdtContent><w:p><w:pPr>" + InList(2) + "</w:pPr></w:p></w:sdtContent></w:sdt>";
        var (document, editor, _) = Setup(Para("before") + control);
        var inside = ((ContentControlBlock)document.Body.Blocks[1]).Content.OfType<Paragraph>().Single();

        var result = editor.SplitParagraph(new TextRange(At(inside), At(inside)));

        Assert.Empty(result.Changes);
        Assert.Single(((ContentControlBlock)document.Body.Blocks[1]).Content);
        Assert.Equal(2, NumId(inside));
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public async Task RestartAtALevelOneParagraphRendersTheLevelZeroCounterOfTheNewInstance()
    {
        var (document, editor, _) = Setup(Para("a", InList(2)) + Para("b", InList(2, 1)) + Para("c", InList(2)) + Para("d", InList(2, 1)) + Para("e", InList(2, 1)));
        Assert.Equal(["1.", "1.1.", "2.", "2.1.", "2.2."], Labels(document));
        var layout = DocumentLayout.Create(document, PageTestKit.Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, CancellationToken.None);

        var result = editor.RestartNumbering(At(P(document, 4)));

        Assert.NotNull(result);
        var restarted = NumId(P(document, 4));
        Assert.NotEqual(2, restarted);
        var over = Assert.Single(document.Numbering!.FindInstance(restarted)!.Overrides);
        Assert.Equal(1, over.Level);
        foreach (var change in result.Changes)
        {
            layout.ApplyEdit(change, 0);
        }

        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, document, label: "restart at level 1");
        var labels = Labels(document);
        Assert.Equal(5, labels.Length);
        Assert.Equal(["1.", "1.1.", "2.", "2.1."], labels.Take(4).ToArray());
        Assert.Equal("2.1.", labels[4]);
        foreach (var label in labels)
        {
            Assert.Contains(label, layout.Page(0).Text, StringComparison.Ordinal);
        }
    }
}
