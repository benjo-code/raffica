using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>
/// Tab and Shift+Tab in a table cell (milestone 4.5a, Task 8): the caret moves between the cells, Tab in the last cell adds a row, Shift+Tab in the first does nothing, the list rule of 4.4a decides first only at the
/// start of a list item, the keys are queued during a save and never type a tab character in a cell. The document is <see cref="ObjectDocx"/>: text, a picture, text, then a table of three rows by three columns
/// (block 3) with <c>c00</c> to <c>c22</c>, then a paragraph.
/// </summary>
public sealed class PageViewTableKeysTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-tablekeys-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The system cleans the temp folder.
        }
    }

    private async Task<ViewHarness> OpenAsync()
    {
        var path = ObjectDocx.Write(_folder, Guid.NewGuid().ToString("N") + ".docx");
        var harness = new ViewHarness(path, 900, 1000);
        harness.View.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        await ViewHarness.WaitAsync(() => harness.View.Runner.PageLayout.IsComplete, 30000);
        return harness;
    }

    private static Table TableOf(ViewHarness h) => (Table)h.View.Tab.Docx.Model.Body.Blocks[3];

    private static Paragraph CellParagraph(ViewHarness h, int row, int column) => TableGridMap.Of(TableOf(h)).At(row, column)!.Cell.Blocks.OfType<Paragraph>().First();

    private static string TextOf(ViewHarness h, Paragraph paragraph) =>
        ParagraphProjection.Build(paragraph, h.View.Runner.PageLayout.ProjectionHost(StoryRef.Body, paragraph)).Text;

    private static EditSession CaretIn(ViewHarness h, int row, int column, int offset = 0)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var at = new TextPosition(StoryRef.Body, CellParagraph(h, row, column), offset);
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        return session;
    }

    private static void Stroke(ViewHarness h, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        h.Window.KeyPressQwerty(key, modifiers);
        h.Window.KeyReleaseQwerty(key, modifiers);
        ViewHarness.Pump();
    }

    // The cell the caret is in, as (row, column), or null.
    private static (int Row, int Column)? CellOfCaret(ViewHarness h, EditSession session) =>
        session.Caret is { } caret && session.Editor.TableOf(caret) is { } place ? (place.Row, place.Column) : null;

    [AvaloniaFact]
    public async Task TabMovesToTheNextCellAndAddsARowInTheLast()
    {
        using var h = await OpenAsync();
        var session = CaretIn(h, 0, 0);
        var text = TextOf(h, CellParagraph(h, 0, 0));

        // Along the row, then to the first cell of the next row: the caret is at the start of the cell, no character is typed and no step is recorded.
        var steps = session.Editor.History.UndoCount;
        Stroke(h, PhysicalKey.Tab);
        Assert.Equal((0, 1), CellOfCaret(h, session));
        Stroke(h, PhysicalKey.Tab);
        Stroke(h, PhysicalKey.Tab);
        Assert.Equal((1, 0), CellOfCaret(h, session));
        Assert.Equal(0, session.Caret!.Value.Offset);
        Assert.Equal(text, TextOf(h, CellParagraph(h, 0, 0)));
        Assert.Equal(steps, session.Editor.History.UndoCount);

        // Tab in the last cell adds a row after the last: one step, the caret in the new row.
        CaretIn(h, 2, 2);
        Stroke(h, PhysicalKey.Tab);
        Assert.Equal(4, TableGridMap.Of(TableOf(h)).RowCount);
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        Assert.Equal((3, 0), CellOfCaret(h, session));
        Assert.Equal(string.Empty, TextOf(h, CellParagraph(h, 3, 0)));
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Equal(3, TableGridMap.Of(TableOf(h)).RowCount);
    }

    [AvaloniaFact]
    public async Task ShiftTabMovesBack()
    {
        using var h = await OpenAsync();
        var session = CaretIn(h, 1, 0);
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        Stroke(h, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal((0, 2), CellOfCaret(h, session));
        Stroke(h, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal((0, 1), CellOfCaret(h, session));
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);

        // In the first cell it does nothing (it is claimed: the caret stays and the focus does not leave the page).
        CaretIn(h, 0, 0);
        Stroke(h, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal((0, 0), CellOfCaret(h, session));
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.True(h.View.IsFocused);
    }

    [AvaloniaFact]
    public async Task ASelectionInACellStillMovesToTheNextCellAndNeverTypesATab()
    {
        using var h = await OpenAsync();
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var paragraph = CellParagraph(h, 0, 1);
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, 2)));
        h.View.Focus();
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        Stroke(h, PhysicalKey.Tab);
        Assert.Equal((0, 2), CellOfCaret(h, session));
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.Equal("c01", TextOf(h, CellParagraph(h, 0, 1)));
    }

    [AvaloniaFact]
    public async Task OutsideATableTabIsStillATabCharacterAndTheKeysAreQueuedDuringASave()
    {
        using var h = await OpenAsync();
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var before = (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[0];
        var at = new TextPosition(StoryRef.Body, before, 0);
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        Stroke(h, PhysicalKey.Tab);
        Assert.StartsWith("\t", TextOf(h, (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[0]), StringComparison.Ordinal);
        Assert.NotNull(session.Editor.History.Undo());

        // During a save the key is queued like typing and replayed in order when the save ends: the caret moves then, nothing is lost.
        CaretIn(h, 0, 0);
        var editor = session.Editor;
        var save = session.BeginSave();
        Stroke(h, PhysicalKey.Tab);
        Stroke(h, PhysicalKey.Tab);
        Assert.Equal(2, session.PendingCount);
        var during = editor.TableOf(session.Caret!.Value)!;
        Assert.Equal((0, 0), (during.Row, during.Column));
        save.Dispose();
        ViewHarness.Pump();
        Assert.Equal(0, session.PendingCount);
        Assert.Equal((0, 2), CellOfCaret(h, session));
    }

    // ---- the case table of the order --------------------------------------------------------------------------------------------
    // The ruling (4.4a kept): the list rule decides at offset 0 of a list item, with text or empty; it never falls through (at the edge of the levels ChangeListLevel gives a result with no change, so
    // no tab character, no cell move and no new row). Only a selection that touches no list paragraph falls through to the cell rule. Else in a top-level cell the cell rule; else a tab character (Tab) or nothing (Shift+Tab).

    public enum Item
    {
        None,
        Text,
        Empty,
    }

    public enum Place
    {
        Start,
        Middle,
        End,
    }

    public enum Expect
    {
        LevelUp,
        StayInTheItem,
        NextCell,
        PreviousCell,
        RowAdded,
        StayInTheCell,
        TabCharacter,
        NothingOutside,
    }

    // The cells: the middle one for the plain walk, the last (Tab) or first (Shift+Tab) for the edge of the table; outside is the first paragraph of the body.
    public enum Where
    {
        Outside,
        Middle,
        Edge,
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var where in Enum.GetValues<Where>())
        {
            foreach (var shift in new[] { false, true })
            {
                foreach (var item in Enum.GetValues<Item>())
                {
                    foreach (var place in item == Item.Empty ? [Place.Start] : Enum.GetValues<Place>())
                    {
                        yield return [item, place, where, shift];
                    }
                }
            }
        }
    }

    private static Expect Expected(Item item, Place place, Where where, bool shift)
    {
        if (item != Item.None && place == Place.Start)
        {
            // A fresh list item is at level 0: Tab goes one level deeper, Shift+Tab is the edge and changes nothing, in a cell, in the last or the first cell and outside alike.
            return shift ? Expect.StayInTheItem : Expect.LevelUp;
        }

        return where switch
        {
            Where.Outside => shift ? Expect.NothingOutside : Expect.TabCharacter,
            Where.Middle => shift ? Expect.PreviousCell : Expect.NextCell,
            _ => shift ? Expect.StayInTheCell : Expect.RowAdded,
        };
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task TabInAListInACellFollowsTheListRuleAtTheStartOfAnItem(Item item, Place place, Where where, bool shift)
    {
        using var h = await OpenAsync();
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var (row, column) = where == Where.Edge ? (shift ? (0, 0) : (2, 2)) : (1, 1);
        Paragraph Target() => where == Where.Outside ? (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[0] : CellParagraph(h, row, column);

        var at = new TextPosition(StoryRef.Body, Target(), 0);
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        if (item == Item.Empty)
        {
            var length = TextOf(h, Target()).Length;
            Assert.NotNull(h.View.ApplyOperation(ed => ed.Delete(new TextRange(new TextPosition(StoryRef.Body, Target(), 0), new TextPosition(StoryRef.Body, Target(), length)))));
            Assert.Equal(string.Empty, TextOf(h, Target()));
        }

        if (item != Item.None)
        {
            Assert.True(h.View.RunStructureCommand(WriteCommandIds.Bullets));
            Assert.True(session.ResolveParagraph(StoryRef.Body, Target()).NumberingId > 0);
        }

        var paragraph = Target();
        var text = TextOf(h, paragraph);
        var offset = place switch { Place.Start => 0, Place.Middle => 2, _ => text.Length };
        var put = new TextPosition(StoryRef.Body, paragraph, offset);
        session.SetSelection(new TextRange(put, put));
        var rows = TableGridMap.Of(TableOf(h)).RowCount;
        var level = session.ResolveParagraph(StoryRef.Body, paragraph).NumberingLevel;
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        Stroke(h, PhysicalKey.Tab, shift ? RawInputModifiers.Shift : RawInputModifiers.None);

        var caret = session.Caret!.Value;
        switch (Expected(item, place, where, shift))
        {
            case Expect.LevelUp:
                Assert.Equal(level + 1, session.ResolveParagraph(StoryRef.Body, caret.Paragraph).NumberingLevel);
                Assert.Equal(text, TextOf(h, caret.Paragraph));
                Assert.Equal(rows, TableGridMap.Of(TableOf(h)).RowCount);
                if (where != Where.Outside)
                {
                    Assert.Equal((row, column), CellOfCaret(h, session));
                }

                break;
            case Expect.StayInTheItem:
                Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
                Assert.Same(paragraph, caret.Paragraph);
                Assert.Equal(0, caret.Offset);
                Assert.Equal(rows, TableGridMap.Of(TableOf(h)).RowCount);
                break;
            case Expect.NextCell:
                Assert.Equal((row, column + 1), CellOfCaret(h, session));
                Assert.Equal(0, caret.Offset);
                Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
                break;
            case Expect.PreviousCell:
                Assert.Equal((row, column - 1), CellOfCaret(h, session));
                Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
                break;
            case Expect.RowAdded:
                Assert.Equal(rows + 1, TableGridMap.Of(TableOf(h)).RowCount);
                Assert.Equal((rows, 0), CellOfCaret(h, session));
                break;
            case Expect.StayInTheCell:
                Assert.Equal((row, column), CellOfCaret(h, session));
                Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
                Assert.Equal(rows, TableGridMap.Of(TableOf(h)).RowCount);
                break;
            case Expect.TabCharacter:
                Assert.Equal(text.Insert(offset, "\t"), TextOf(h, caret.Paragraph));
                break;
            default:
                Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
                Assert.Equal(text, TextOf(h, caret.Paragraph));
                break;
        }
    }

    [AvaloniaTheory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task TabAtTheDeepestLevelChangesNothingAndNeverFallsThroughToTheCellRule(int row, int column)
    {
        using var h = await OpenAsync();
        var session = CaretIn(h, row, column);
        Assert.True(h.View.RunStructureCommand(WriteCommandIds.Bullets));
        var paragraph = CellParagraph(h, row, column);
        var at = new TextPosition(StoryRef.Body, paragraph, 0);
        session.SetSelection(new TextRange(at, at));

        for (var i = 0; i < 12; i++)
        {
            Stroke(h, PhysicalKey.Tab);
        }

        Assert.Equal(8, session.ResolveParagraph(StoryRef.Body, CellParagraph(h, row, column)).NumberingLevel);
        Assert.Equal((row, column), CellOfCaret(h, session));
        Assert.Equal(3, TableGridMap.Of(TableOf(h)).RowCount);
        Assert.Equal("c" + row + column, TextOf(h, CellParagraph(h, row, column)));
    }

    [AvaloniaFact]
    public async Task ASelectionThatTouchesAListInACellChangesTheLevelAndOneThatTouchesNoneMovesOn()
    {
        using var h = await OpenAsync();
        var session = CaretIn(h, 1, 1);
        Assert.True(h.View.RunStructureCommand(WriteCommandIds.Bullets));
        var paragraph = CellParagraph(h, 1, 1);
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, paragraph, 1), new TextPosition(StoryRef.Body, paragraph, 3)));

        Stroke(h, PhysicalKey.Tab);
        Assert.Equal(1, session.ResolveParagraph(StoryRef.Body, CellParagraph(h, 1, 1)).NumberingLevel);
        Assert.Equal((1, 1), CellOfCaret(h, session));

        // A selection in a cell with no list falls through the list rule to the cell rule.
        var plain = CellParagraph(h, 1, 2);
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, plain, 0), new TextPosition(StoryRef.Body, plain, 2)));
        Stroke(h, PhysicalKey.Tab);
        Assert.Equal((2, 0), CellOfCaret(h, session));
    }

    [AvaloniaFact]
    public async Task TabInANestedTableIsATabCharacterAsBefore()
    {
        var path = Path.Combine(_folder, "nested.docx");
        WriteNested(path);
        using var h = new ViewHarness(path, 900, 1000);
        h.View.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        h.View.Runner.Start();
        await ViewHarness.WaitAsync(() => h.View.Runner.FullModelReady || h.View.Runner.IsFaulted, 30000);
        await ViewHarness.WaitAsync(() => h.View.Runner.PageLayout.IsComplete, 30000);
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;

        var outer = (Table)h.View.Tab.Docx.Model.Body.Blocks[0];
        var nested = outer.Rows.OfType<TableRow>().First().Items.OfType<TableCell>().First().Blocks.OfType<Table>().Single();
        var inner = nested.Rows.OfType<TableRow>().First().Items.OfType<TableCell>().First().Blocks.OfType<Paragraph>().First();
        var at = new TextPosition(StoryRef.Body, inner, 0);
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        Assert.Null(session.Editor.TableOf(at));
        var rows = outer.Rows.Count;

        Stroke(h, PhysicalKey.Tab);

        // Commands act on the outer table only, so the caret in a nested cell gets the tab character as before; no cell moved and no row was added.
        Assert.Equal("\tn0", TextOf(h, inner));
        Assert.Same(inner, session.Caret!.Value.Paragraph);
        Assert.Equal(rows, ((Table)h.View.Tab.Docx.Model.Body.Blocks[0]).Rows.Count);
    }

    private static void WriteNested(string path)
    {
        const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
        static string Cell(string inner) => "<w:tc><w:tcPr><w:tcW w:w=\"2400\" w:type=\"dxa\"/></w:tcPr>" + inner + "</w:tc>";
        var nested = "<w:tbl><w:tblPr><w:tblW w:w=\"2000\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"1000\"/><w:gridCol w:w=\"1000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>n0</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>n1</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var body = "<w:tbl><w:tblPr><w:tblW w:w=\"4800\" w:type=\"dxa\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2400\"/><w:gridCol w:w=\"2400\"/></w:tblGrid>"
            + "<w:tr>" + Cell(nested + "<w:p/>") + Cell("<w:p><w:r><w:t>b</w:t></w:r></w:p>") + "</w:tr></w:tbl><w:p><w:r><w:t>after</w:t></w:r></w:p>";
        using var stream = File.Create(path);
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create);
        void Add(string name, string text)
        {
            using var entry = zip.CreateEntry(name).Open();
            entry.Write(new System.Text.UTF8Encoding(false).GetBytes(text));
        }

        Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add("word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document " + W + "><w:body>" + body + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
    }
}
