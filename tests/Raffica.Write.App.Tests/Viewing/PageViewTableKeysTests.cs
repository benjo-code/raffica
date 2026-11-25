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

    // The case table of the order: the list rule of 4.4a at the start of a list item first, else the table rule inside a cell, else a tab character.
    public enum Expect
    {
        ListLevel,
        NextCell,
        TabCharacter,
    }

    [AvaloniaTheory]
    [InlineData(true, true, true, Expect.ListLevel)]
    [InlineData(true, false, true, Expect.NextCell)]
    [InlineData(false, true, true, Expect.NextCell)]
    [InlineData(false, false, true, Expect.NextCell)]
    [InlineData(true, true, false, Expect.ListLevel)]
    [InlineData(true, false, false, Expect.TabCharacter)]
    [InlineData(false, true, false, Expect.TabCharacter)]
    [InlineData(false, false, false, Expect.TabCharacter)]
    public async Task TabInAListInACellFollowsTheListRuleAtTheStartOfAnItem(bool inList, bool atStart, bool inCell, Expect expected)
    {
        using var h = await OpenAsync();
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var paragraph = inCell ? CellParagraph(h, 1, 1) : (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[0];
        var at = new TextPosition(StoryRef.Body, paragraph, 0);
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        if (inList)
        {
            Assert.True(h.View.RunStructureCommand(WriteCommandIds.Bullets));
            paragraph = inCell ? CellParagraph(h, 1, 1) : (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[0];
            Assert.True(session.ResolveParagraph(StoryRef.Body, paragraph).NumberingId > 0);
        }

        var place = new TextPosition(StoryRef.Body, paragraph, atStart ? 0 : 2);
        session.SetSelection(new TextRange(place, place));
        var level = session.ResolveParagraph(StoryRef.Body, paragraph).NumberingLevel;
        var text = TextOf(h, paragraph);

        Stroke(h, PhysicalKey.Tab);

        switch (expected)
        {
            case Expect.ListLevel:
                Assert.Equal(level + 1, session.ResolveParagraph(StoryRef.Body, session.Caret!.Value.Paragraph).NumberingLevel);
                Assert.Equal(text, TextOf(h, session.Caret!.Value.Paragraph));
                if (inCell)
                {
                    Assert.Equal((1, 1), CellOfCaret(h, session));
                }

                break;
            case Expect.NextCell:
                Assert.Equal((1, 2), CellOfCaret(h, session));
                Assert.Equal(text, TextOf(h, CellParagraph(h, 1, 1)));
                break;
            default:
                Assert.Equal(text.Insert(2 * (atStart ? 0 : 1), "\t"), TextOf(h, session.Caret!.Value.Paragraph));
                break;
        }
    }
}
