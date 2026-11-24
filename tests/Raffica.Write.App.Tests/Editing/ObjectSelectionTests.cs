using Avalonia.Headless.XUnit;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;
using static Raffica.WriteApp.Tests.Viewing.PageViewObjectTests;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The object selection of the editing session (milestone 4.5a, Task 5): what sets it, what ends it and that it never reaches the model.</summary>
public sealed class ObjectSelectionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-objsel-").FullName;

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

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    [AvaloniaFact]
    public async Task SettingASelectionCollapsesTheTextRangeAndATextSelectionEndsIt()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var session = h.View.Tab.EnsureEditor();
        var from = new TextPosition(StoryRef.Body, P(h, 0), 1);
        var to = new TextPosition(StoryRef.Body, P(h, 0), 6);
        session.SetSelection(new TextRange(from, to));
        var events = 0;
        session.ObjectSelectionChanged += () => events++;
        var revision = h.View.Tab.Docx.Model.Edits.Revision;

        Assert.True(session.SetObjectSelection(new PictureSelection(ThePicture(h).Id!)));
        Assert.Equal(1, events);
        Assert.True(session.Selection!.Value.IsEmpty);
        Assert.Equal(to.Offset, session.Selection.Value.End.Offset);

        // The same selection again changes nothing and says nothing.
        Assert.True(session.SetObjectSelection(new PictureSelection(ThePicture(h).Id!)));
        Assert.Equal(1, events);

        session.SetSelection(new TextRange(from, to));
        Assert.Null(session.ObjectSelection);
        Assert.Null(session.SelectedDrawing);
        Assert.Equal(2, events);

        // It is never in the model.
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task APlaceThatHoldsNoPictureCannotBeSelected()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var session = h.View.Tab.EnsureEditor();
        Assert.False(session.SetObjectSelection(new PictureSelection(new ObjectId(StoryRef.Body, 0, 0, 0))));
        Assert.False(session.SetObjectSelection(new PictureSelection(new ObjectId(StoryRef.Body, 99, 0, 0))));
        Assert.False(session.SetObjectSelection(new CellSelection(new TableRef(StoryRef.Body, 0), new CellRange(new TableRef(StoryRef.Body, 0), 0, 0, 0, 0))));
        Assert.Null(session.ObjectSelection);
    }

    [AvaloniaFact]
    public async Task AnEditThatMovesTheSelectionEndsItAndUndoIsAnEdit()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var session = h.View.Tab.EnsureEditor();
        var start = new TextPosition(StoryRef.Body, P(h, 0), 0);
        session.SetSelection(new TextRange(start, start));
        Assert.NotNull(session.Apply(ed => ed.InsertText(session.Selection!.Value, "x"), 1));

        Assert.True(session.SetObjectSelection(new PictureSelection(ThePicture(h).Id!)));
        Assert.NotNull(session.Apply(ed => ed.History.Undo(), 1));
        Assert.Null(session.ObjectSelection);
    }

    [AvaloniaFact]
    public async Task ACellSelectionLosesATableThatIsGone()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var session = h.View.Tab.EnsureEditor();
        var table = TableOf(Grid(h));
        Assert.True(session.SelectCells(table, (0, 0), (1, 1)));

        // The paragraph before the table is joined with the one after the picture: the table keeps its place. Deleting everything from the first paragraph to the last takes the table away.
        var first = new TextPosition(StoryRef.Body, P(h, 0), 0);
        var last = h.View.Tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Last();
        var end = new TextPosition(StoryRef.Body, last, 3);
        Assert.NotNull(session.Apply(ed => ed.Delete(new TextRange(first, end)), 1));
        Assert.Null(session.ObjectSelection);
    }

    [Fact]
    public void RangesKnowTheirCells()
    {
        var table = new TableRef(StoryRef.Body, 3);
        var range = CellRange.Between(table, 2, 3, 0, 1);
        Assert.Equal(new CellRange(table, 0, 1, 2, 3), range);
        Assert.Equal(3, range.RowSpan);
        Assert.Equal(3, range.ColumnSpan);
        Assert.True(range.Contains(1, 2));
        Assert.False(range.Contains(3, 2));
        Assert.True(range.Overlaps(2, 0, 1, 2));
        Assert.False(range.Overlaps(3, 0, 2, 4));
    }

    [Fact]
    public void ATableRefFindsTheTableOfItsBlockOrOfItsOnlyControl()
    {
        var table = new Table();
        var control = new ContentControlBlock();
        control.Content.Add(new Paragraph());
        control.Content.Add(table);
        var twoTables = new ContentControlBlock();
        twoTables.Content.Add(new Table());
        twoTables.Content.Add(new Table());
        var body = new List<Block> { new Paragraph(), table, control, twoTables };

        Assert.Same(table, new TableRef(StoryRef.Body, 1).Find(body));
        Assert.Same(table, new TableRef(StoryRef.Body, 2).Find(body));
        Assert.Null(new TableRef(StoryRef.Body, 3).Find(body));
        Assert.Null(new TableRef(StoryRef.Body, 0).Find(body));
        Assert.Null(new TableRef(StoryRef.Body, 9).Find(body));
        Assert.Null(new TableRef(StoryRef.Body, -1).Find(body));
    }
}
