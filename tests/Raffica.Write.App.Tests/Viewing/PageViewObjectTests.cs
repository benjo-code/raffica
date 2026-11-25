using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>Selecting pictures and cell ranges with the pointer and the keys, and what the view draws and announces for them (milestone 4.5a, Task 5).</summary>
public sealed class PageViewObjectTests : IDisposable
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-objects-").FullName;

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

    internal static async Task<ViewHarness> OpenAsync(string path, WriteView mode = WriteView.Page)
    {
        var harness = new ViewHarness(path, 900, 1000, mode);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        await ViewHarness.WaitAsync(() => harness.View.Runner.PageLayout.IsComplete, 30000);
        return harness;
    }

    internal static double Scale(PageView view) => view.Zoom * 96 / 72;

    internal static Point ViewPoint(PageView view, int page, double x, double y)
    {
        var rect = view.PageBounds(page)!.Value;
        return new Point(rect.X + (x * Scale(view)), rect.Y + (y * Scale(view)));
    }

    internal static void Click(ViewHarness h, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        h.Window.MouseDown(point, MouseButton.Left, modifiers);
        h.Window.MouseUp(point, MouseButton.Left, modifiers);
        ViewHarness.Pump();
    }

    internal static PictureBox ThePicture(ViewHarness h) => Assert.Single(h.View.Runner.PageLayout.Page(0).Objects.Pictures);

    internal static Point Center(PictureBox box, PageView view) => ViewPoint(view, 0, box.X + (box.Width / 2), box.Y + (box.Height / 2));

    internal static TableGrid Grid(ViewHarness h) => Assert.Single(h.View.Runner.PageLayout.Page(0).Objects.Tables);

    internal static Point CellCenter(PageView view, TableGrid grid, int row, int column)
    {
        var cell = grid.Cells.Single(c => c.Row == row && c.Column == column);
        return ViewPoint(view, 0, cell.X + (cell.Width / 2), cell.Y + (cell.Height / 2));
    }

    internal static TableRef TableOf(TableGrid grid) => new(grid.Story, grid.BlockIndex);

    internal static void Stroke(ViewHarness h, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        h.Window.KeyPressQwerty(key, modifiers);
        h.Window.KeyReleaseQwerty(key, modifiers);
        ViewHarness.Pump();
    }

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    [AvaloniaFact]
    public async Task ClickingAPictureSelectsIt()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var box = ThePicture(h);

        Click(h, Center(box, view));

        var session = view.Tab.Editor!;
        var selection = Assert.IsType<PictureSelection>(session.ObjectSelection);
        Assert.Equal(1, selection.Id.Paragraph);
        Assert.Equal(0, selection.Id.Content);
        Assert.NotNull(session.SelectedDrawing);
        Assert.Same(session.SelectedDrawing, ObjectIds.Find(view.Tab.Docx.Model.Body.Blocks, selection.Id)!.Value.Picture);
        Assert.True(box.Shows(session.SelectedDrawing!));
        Assert.Null(view.ObjectGesture);
    }

    [AvaloniaFact]
    public async Task ClickingTextClearsTheObjectSelection()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        Click(h, Center(ThePicture(h), view));
        var session = view.Tab.Editor!;
        Assert.NotNull(session.ObjectSelection);
        var events = 0;
        session.ObjectSelectionChanged += () => events++;

        var before = new TextPosition(StoryRef.Body, P(h, 0), 3);
        var bar = view.CaretBounds(before)!.Value;
        var text = new Point(bar.X + 1, bar.Y + (bar.Height / 2));
        Click(h, text);
        Assert.Null(session.ObjectSelection);
        Assert.Null(session.SelectedDrawing);
        Assert.Equal(1, events);

        // The same again, onto the very place the caret is at: the selection ends too.
        Click(h, Center(ThePicture(h), view));
        Assert.NotNull(session.ObjectSelection);
        session.SetSelection(new TextRange(before, before));
        Assert.Null(session.ObjectSelection);
    }

    [AvaloniaFact]
    public async Task AClickInTheSamePlaceOfTheTextEndsThePictureSelectionToo()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var bar = view.CaretBounds(new TextPosition(StoryRef.Body, P(h, 0), 3))!.Value;
        var text = new Point(bar.X + 1, bar.Y + (bar.Height / 2));
        Click(h, text);
        var session = view.Tab.Editor!;
        var caret = session.Selection;

        // The picture is selected; the caret of the text is where it was. A click on that very place must end the object selection (the text selection does not change).
        Assert.True(session.SetObjectSelection(new PictureSelection(ThePicture(h).Id!)));
        Click(h, text);
        Assert.Null(session.ObjectSelection);
        Assert.Equal(caret!.Value.End.Paragraph, session.Selection!.Value.End.Paragraph);
    }

    [AvaloniaFact]
    public async Task ShiftClickExtendsACellRange()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var grid = Grid(h);
        var session = view.Tab.EnsureEditor();

        // A click in a cell puts the caret there (no object); Shift and a click in another cell of the table selects the cells between.
        Click(h, CellCenter(view, grid, 0, 0));
        Assert.Null(session.ObjectSelection);
        Click(h, CellCenter(view, grid, 1, 2), RawInputModifiers.Shift);
        var selection = Assert.IsType<CellSelection>(session.ObjectSelection);
        Assert.Equal(new CellRange(TableOf(grid), 0, 0, 1, 2), selection.Range);
        Assert.Equal((0, 0), session.CellAnchor);
        Assert.Equal((1, 2), session.CellFocus);

        // Another Shift click keeps the cell where it began.
        Click(h, CellCenter(view, grid, 2, 1), RawInputModifiers.Shift);
        Assert.Equal(new CellRange(TableOf(grid), 0, 0, 2, 1), Assert.IsType<CellSelection>(session.ObjectSelection).Range);

        // A plain click in a cell ends it.
        Click(h, CellCenter(view, grid, 1, 1));
        Assert.Null(session.ObjectSelection);
    }

    [AvaloniaFact]
    public async Task ShiftArrowsExtendACellRange()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var grid = Grid(h);
        var session = view.Tab.EnsureEditor();
        Click(h, CellCenter(view, grid, 0, 0));
        Click(h, CellCenter(view, grid, 0, 1), RawInputModifiers.Shift);
        Assert.Equal(new CellRange(TableOf(grid), 0, 0, 0, 1), Assert.IsType<CellSelection>(session.ObjectSelection).Range);

        Stroke(h, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Assert.Equal(new CellRange(TableOf(grid), 0, 0, 0, 2), Assert.IsType<CellSelection>(session.ObjectSelection).Range);
        Stroke(h, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Stroke(h, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Assert.Equal(new CellRange(TableOf(grid), 0, 0, 2, 2), Assert.IsType<CellSelection>(session.ObjectSelection).Range);

        // At the last row and column nothing moves; Shift and Left or Up shrinks it again; the corner where it began stays.
        Stroke(h, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Stroke(h, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Assert.Equal(new CellRange(TableOf(grid), 0, 0, 2, 2), Assert.IsType<CellSelection>(session.ObjectSelection).Range);
        Stroke(h, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Stroke(h, PhysicalKey.ArrowUp, RawInputModifiers.Shift);
        Assert.Equal(new CellRange(TableOf(grid), 0, 0, 1, 1), Assert.IsType<CellSelection>(session.ObjectSelection).Range);
        Assert.Equal((0, 0), session.CellAnchor);

        // A plain arrow ends the selection and moves the caret.
        Stroke(h, PhysicalKey.ArrowRight);
        Assert.Null(session.ObjectSelection);
    }

    [AvaloniaFact]
    public async Task ClickingTheLeftMarginSelectsARowTheTopEdgeAColumnAndTheCornerTheTable()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var grid = Grid(h);
        var table = TableOf(grid);
        var session = view.Tab.EnsureEditor();
        var reach = 12 / Scale(view);
        var middle = (float)((grid.RowEdges[1] + grid.RowEdges[2]) / 2);

        Click(h, ViewPoint(view, 0, grid.X - reach, middle));
        Assert.Equal(new CellRange(table, 1, 0, 1, 2), Assert.IsType<CellSelection>(session.ObjectSelection).Range);

        var columnMiddle = (grid.ColumnBorders[1] + grid.ColumnBorders[2]) / 2;
        Click(h, ViewPoint(view, 0, columnMiddle, grid.Y - reach));
        Assert.Equal(new CellRange(table, 0, 1, 2, 1), Assert.IsType<CellSelection>(session.ObjectSelection).Range);

        Click(h, ViewPoint(view, 0, grid.X - reach, grid.Y - reach));
        Assert.Equal(new CellRange(table, 0, 0, 2, 2), Assert.IsType<CellSelection>(session.ObjectSelection).Range);

        // The commands of Task 8 call the same methods of the session and get the same selections.
        Assert.True(session.SelectRow(table, 1));
        Assert.Equal(new CellRange(table, 1, 0, 1, 2), Assert.IsType<CellSelection>(session.ObjectSelection).Range);
        Assert.True(session.SelectColumn(table, 1));
        Assert.Equal(new CellRange(table, 0, 1, 2, 1), Assert.IsType<CellSelection>(session.ObjectSelection).Range);
        Assert.True(session.SelectTable(table));
        Assert.Equal(new CellRange(table, 0, 0, 2, 2), Assert.IsType<CellSelection>(session.ObjectSelection).Range);
        Assert.False(session.SelectRow(table, 9));
        Assert.False(session.SelectColumn(table, 3));
        Assert.False(session.SelectRow(new TableRef(StoryRef.Body, 0), 0));
    }

    [AvaloniaFact]
    public async Task DeleteWithAPictureSelectedRemovesIt()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        Click(h, Center(ThePicture(h), view));
        var session = view.Tab.Editor!;
        var selected = session.ObjectSelection;
        var revision = view.Tab.Docx.Model.Edits.Revision;

        // With no operation registered the key does nothing at all: it never falls through to a text delete next to the picture.
        Stroke(h, PhysicalKey.Delete);
        Stroke(h, PhysicalKey.Backspace);
        Assert.Equal(revision, view.Tab.Docx.Model.Edits.Revision);
        Assert.Equal(selected, session.ObjectSelection);

        var deleted = new List<PictureSelection>();
        view.DeletePicture = picture =>
        {
            deleted.Add(picture);
            return true;
        };
        Stroke(h, PhysicalKey.Delete);
        Assert.Equal(selected, Assert.Single(deleted));
        Assert.Equal(revision, view.Tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task AnObjectSelectionIsAnnouncedWithItsAltTextOrTablePosition()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        Click(h, Center(ThePicture(h), view));
        var session = view.Tab.Editor!;

        Assert.Equal("Picture: " + ObjectDocx.Alt, view.SelectedObjectAnnouncement);
        Assert.Contains(ObjectDocx.Alt, AutomationProperties.GetName(view), StringComparison.Ordinal);

        var grid = Grid(h);
        Assert.True(session.SelectCells(TableOf(grid), (1, 2), (1, 2)));
        Assert.Equal("Row 2 of 3, column 3 of 3", view.SelectedObjectAnnouncement);
        Assert.Contains("Row 2 of 3, column 3 of 3", AutomationProperties.GetName(view), StringComparison.Ordinal);
        Assert.True(session.SelectCells(TableOf(grid), (0, 0), (1, 1)));
        Assert.Equal("Rows 1 to 2 of 3, columns 1 to 2 of 3", view.SelectedObjectAnnouncement);

        // The German words are there too.
        var de = CultureInfo.GetCultureInfo("de");
        Assert.Equal("Zeile {0} von {1}, Spalte {2} von {3}", WriteStrings.GetIn("ObjectCell", de));
        Assert.Equal("Bild", WriteStrings.GetIn("ObjectPicture", de));

        // Without alt text it is just a picture.
        session.SetObjectSelection(null);
        Assert.Null(view.SelectedObjectAnnouncement);
        Assert.DoesNotContain("Selected", AutomationProperties.GetName(view), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task APictureWithoutAltTextIsAnnouncedAsAPicture()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder, "plain.docx", alt: string.Empty));
        Click(h, Center(ThePicture(h), h.View));
        Assert.Equal("Picture", h.View.SelectedObjectAnnouncement);
    }

    [AvaloniaFact]
    public async Task NothingIsBuiltBeforeTheFirstObjectIsHit()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var reads = PictureAnchor.IdentityReads;
        var resolves = view.Runner.PageLayout.HitResolves;

        // Text clicks and pointer moves over the page build no selection, no gesture and read no identity.
        var bar = view.CaretBounds(new TextPosition(StoryRef.Body, P(h, 0), 3))!.Value;
        Click(h, new Point(bar.X + 1, bar.Y + (bar.Height / 2)));
        for (var i = 0; i < 20; i++)
        {
            h.Window.MouseMove(new Point(120 + (i * 7), 300 + i));
        }

        ViewHarness.Pump();
        Assert.Null(view.ObjectSelection);
        Assert.Null(view.ObjectGesture);
        Assert.Null(view.SelectedObjectAnnouncement);
        Assert.Equal(reads, PictureAnchor.IdentityReads);
        Assert.Equal(resolves, view.Runner.PageLayout.HitResolves);
    }

    [AvaloniaFact]
    public async Task ThePointerMovingOverASelectedPictureReadsNoModel()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var box = ThePicture(h);
        Click(h, Center(box, view));
        var reads = PictureAnchor.IdentityReads;
        var resolves = view.Runner.PageLayout.HitResolves;
        var centre = Center(box, view);
        for (var i = 0; i < 30; i++)
        {
            h.Window.MouseMove(new Point(centre.X + i, centre.Y + (i / 2)));
        }

        Assert.Equal(reads, PictureAnchor.IdentityReads);
        Assert.Equal(resolves, view.Runner.PageLayout.HitResolves);
    }

    [AvaloniaFact]
    public async Task ARenderPassWithASelectedObjectTouchesNoVisual()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        Click(h, Center(ThePicture(h), view));
        view.ReduceMotion = true;
        h.Capture();
        h.Capture();
        h.Capture();
        var (pages, overlay) = (view.RenderCount, view.OverlayRenderCount);
        Assert.True(overlay > 0);

        // More frames with nothing changed: neither layer is drawn again, so the pass invalidated nothing.
        h.Capture();
        h.Capture();
        h.Capture();
        Assert.Equal(pages, view.RenderCount);
        Assert.Equal(overlay, view.OverlayRenderCount);
        Assert.Equal(overlay, view.OverlayRenderCount);

        // The selected cells draw the same way.
        var session = view.Tab.Editor!;
        Assert.True(session.SelectTable(TableOf(Grid(h))));
        h.Capture();
        h.Capture();
        var settled = view.OverlayRenderCount;
        h.Capture();
        h.Capture();
        Assert.Equal(settled, view.OverlayRenderCount);
    }

    [AvaloniaFact]
    public async Task ASelectedPictureShowsItsHandlesAndTheWritingViewSelectsNoObject()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var box = ThePicture(h);
        view.ReduceMotion = true;
        var before = h.Capture();
        var corner = ViewPoint(view, 0, box.X, box.Y);
        var (cx, cy) = ((int)Math.Round(corner.X), (int)Math.Round(corner.Y));
        var reference = before.Hash(cx - 6, cy - 6, 12, 12);

        Click(h, Center(box, view));
        var after = h.Capture();

        // The handle at the corner changed the pixels around it.
        Assert.NotEqual(reference, after.Hash(cx - 6, cy - 6, 12, 12));

        // Not in the writing view: objects are selected in the pages view only.
        view.Mode = WriteView.Writing;
        Assert.Null(view.Tab.Editor!.ObjectSelection);
    }
}
