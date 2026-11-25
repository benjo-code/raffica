using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;
using Xunit;
using static Raffica.WriteApp.Tests.Viewing.PageViewObjectTests;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>
/// The pointer gestures on a table (milestone 4.5a, Task 9): a column border, the right edge, a row by its handle and a cell range. Every gesture changes the document once, on release, and never
/// while it goes on. App tests run one at a time (the assembly turns test parallelism off), so the timing test needs no collection of its own.
/// </summary>
public sealed class TableGestureTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-gestures-").FullName;

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

    private static string Cell(string text, string properties = "") =>
        "<w:tc><w:tcPr><w:tcW w:w=\"2400\" w:type=\"dxa\"/>" + properties + "</w:tcPr><w:p>" + (text.Length == 0 ? string.Empty : "<w:r><w:t>" + text + "</w:t></w:r>") + "</w:p></w:tc>";

    private string WriteBody(string name, string body)
    {
        var path = Path.Combine(_folder, name);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add(zip, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
            + body + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
        return path;
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var entry = zip.CreateEntry(name).Open();
        entry.Write(new UTF8Encoding(false).GetBytes(text));
    }

    // Three rows and three columns, the second column of the first row spans two (a horizontal merge) and the first cell of the second row spans two rows (a vertical merge).
    private string Merged() => WriteBody(
        "merged.docx",
        "<w:p><w:r><w:t>Before</w:t></w:r></w:p><w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2400\"/><w:gridCol w:w=\"2400\"/><w:gridCol w:w=\"2400\"/></w:tblGrid>"
        + "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"4800\" w:type=\"dxa\"/><w:gridSpan w:val=\"2\"/></w:tcPr><w:p><w:r><w:t>m0</w:t></w:r></w:p></w:tc>" + Cell("m1") + "</w:tr>"
        + "<w:tr>" + Cell("m2", "<w:vMerge w:val=\"restart\"/>") + Cell("m3") + Cell("m4") + "</w:tr>"
        + "<w:tr>" + Cell(string.Empty, "<w:vMerge/>") + Cell("m5") + Cell("m6") + "</w:tr></w:tbl><w:p><w:r><w:t>After</w:t></w:r></w:p>");

    // A table whose first cell holds a table of its own.
    private string Nested() => WriteBody(
        "nested.docx",
        "<w:p><w:r><w:t>Before</w:t></w:r></w:p><w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"4800\"/><w:gridCol w:w=\"4800\"/></w:tblGrid>"
        + "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"4800\" w:type=\"dxa\"/></w:tcPr><w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>"
        + "<w:tr>" + Cell("n00") + Cell("n01") + "</w:tr></w:tbl><w:p/></w:tc>" + Cell("o01") + "</w:tr>"
        + "<w:tr>" + Cell("o10") + Cell("o11") + "</w:tr></w:tbl><w:p><w:r><w:t>After</w:t></w:r></w:p>");

    private static float Tolerance(PageView view) => ObjectHandles.TolerancePoints((float)Scale(view));

    private static Point BorderPoint(PageView view, TableGrid grid, int border, int row = 0)
    {
        var cell = grid.Cells.First(c => c.Row == row);
        return ViewPoint(view, 0, grid.ColumnBorders[border], cell.Y + (cell.Height / 2));
    }

    private static Point RowHandlePoint(PageView view, TableGrid grid, int row)
    {
        var cell = grid.Cells.First(c => c.Row == row);
        return ViewPoint(view, 0, grid.X - (2 * Tolerance(view)), cell.Y + (cell.Height / 2));
    }

    private static void Move(ViewHarness h, Point from, Point to, int steps = 6)
    {
        for (var i = 1; i <= steps; i++)
        {
            h.Window.MouseMove(new Point(from.X + ((to.X - from.X) * i / steps), from.Y + ((to.Y - from.Y) * i / steps)));
        }

        ViewHarness.Pump();
    }

    private static void Drag(ViewHarness h, Point from, Point to)
    {
        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, to);
        h.Window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();
    }

    private static int[] Drawn(TableGrid grid) => [.. Enumerable.Range(0, grid.ColumnBorders.Count - 1).Select(c => (int)Math.Round((grid.ColumnBorders[c + 1] - grid.ColumnBorders[c]) * 20.0))];

    private static TableValues Values(ViewHarness h, TableGrid grid) => h.View.Tab.EnsureEditor().Editor.ReadTableValues(TableOf(grid), 0)!;

    private static int Revision(ViewHarness h) => h.View.Tab.Docx.Model.Edits.Revision;

    private static string TextOfCell(ViewHarness h, int block, int row, int column)
    {
        var table = (Table)h.View.Tab.Docx.Model.Body.Blocks[block];
        var cell = (TableCell)((TableRow)table.Rows[row]).Items[column];
        return ParagraphProjection.Build((Paragraph)cell.Blocks[0], ProjectionHost.Plain).Text;
    }

    private static int TableBlock(TableGrid grid) => grid.BlockIndex;

    [AvaloniaFact]
    public async Task DraggingABorderMovesItBetweenTwoColumnsAndKeepsTheTableWidth()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var before = Drawn(grid);
        var from = BorderPoint(h.View, grid, 1);
        var to = new Point(from.X + 40, from.Y);

        h.Window.MouseDown(from, MouseButton.Left);
        var revision = Revision(h);
        Move(h, from, to);
        Assert.Equal("ColumnBorderDrag", h.View.ObjectGesture?.Kind?.GetType().Name);
        Assert.Equal(revision, Revision(h));
        h.Window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();

        Assert.Equal(revision + 1, Revision(h));
        var d = (int)Math.Round(40 / Scale(h.View) * 20);
        var widths = Values(h, grid).GridTwips;
        Assert.InRange(widths[0], before[0] + d - 2, before[0] + d + 2);
        Assert.Equal(before[0] + before[1], widths[0] + widths[1]);
        Assert.Equal(before[2], widths[2]);
        Assert.Equal(before.Sum(), widths.Sum());
    }

    [AvaloniaFact]
    public async Task DraggingTheRightEdgeChangesTheLastColumnAndTheTableWidth()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var before = Drawn(grid);
        var from = BorderPoint(h.View, grid, 3);
        var to = new Point(from.X + 30, from.Y);

        Drag(h, from, to);

        var d = (int)Math.Round(30 / Scale(h.View) * 20);
        var values = Values(h, grid);
        Assert.Equal(before[0], values.GridTwips[0]);
        Assert.Equal(before[1], values.GridTwips[1]);
        Assert.InRange(values.GridTwips[2], before[2] + d - 2, before[2] + d + 2);
        Assert.Equal(values.GridTwips.Sum(), values.TableWidthTwips);
        Assert.True(values.TableWidthTwips > before.Sum());
    }

    [AvaloniaFact]
    public async Task ABorderHasAMinimumColumnWidth()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var before = Drawn(grid);
        var from = BorderPoint(h.View, grid, 1);

        // Far past the column on the left: the column stays 0.5 cm wide and the other one takes the rest.
        Drag(h, from, new Point(from.X - 2000, from.Y));

        var widths = Values(h, grid).GridTwips;
        Assert.Equal(TableGestures.MinColumnTwips, widths[0]);
        Assert.Equal(before[0] + before[1] - TableGestures.MinColumnTwips, widths[1]);
        Assert.Equal(before[2], widths[2]);

        // The edge cannot make the last column narrower than that either.
        var edge = BorderPoint(h.View, Grid(h), 3);
        Drag(h, edge, new Point(edge.X - 3000, edge.Y));
        Assert.Equal(TableGestures.MinColumnTwips, Values(h, grid).GridTwips[2]);
    }

    [AvaloniaFact]
    public async Task DraggingARowMovesItAsOneStepAndUndoes()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var block = TableBlock(grid);
        var from = RowHandlePoint(h.View, grid, 0);
        var to = RowHandlePoint(h.View, grid, 2);

        h.Window.MouseDown(from, MouseButton.Left);
        var revision = Revision(h);
        Move(h, from, to);
        Assert.Equal("RowDrag", h.View.ObjectGesture?.Kind?.GetType().Name);
        Assert.Equal(revision, Revision(h));
        Assert.Equal("c00", TextOfCell(h, block, 0, 0));
        h.Window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();

        Assert.Equal(revision + 1, Revision(h));
        Assert.Equal("c10", TextOfCell(h, block, 0, 0));
        Assert.Equal("c00", TextOfCell(h, block, 2, 0));

        Assert.NotNull(h.View.Tab.Editor!.Apply(ed => ed.History.Undo(), 1));
        Assert.Equal("c00", TextOfCell(h, block, 0, 0));
        Assert.Equal("c20", TextOfCell(h, block, 2, 0));
    }

    [AvaloniaFact]
    public async Task ARefusedRowMoveShowsItsReasonAndChangesNothing()
    {
        using var h = await OpenAsync(Merged());
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var grid = Grid(h);
        var revision = Revision(h);

        // The second row starts a vertical merge: it cannot be moved.
        Drag(h, RowHandlePoint(h.View, grid, 1), RowHandlePoint(h.View, grid, 0));

        Assert.Equal(revision, Revision(h));
        Assert.Single(notes);
    }

    [AvaloniaFact]
    public async Task DraggingAcrossCellsSelectsTheRangeAndShowsNoModelChange()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var revision = Revision(h);
        var from = CellCenter(h.View, grid, 0, 0);
        var to = CellCenter(h.View, grid, 1, 1);

        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, to);

        var live = Assert.IsType<CellSelection>(h.View.Tab.Editor!.ObjectSelection);
        Assert.Equal((0, 0, 1, 1), (live.Range.FirstRow, live.Range.FirstColumn, live.Range.LastRow, live.Range.LastColumn));
        Assert.Equal(revision, Revision(h));
        h.Window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();

        Assert.IsType<CellSelection>(h.View.Tab.Editor.ObjectSelection);
        Assert.Equal(revision, Revision(h));
    }

    [AvaloniaFact]
    public async Task ADragInsideOneCellStaysATextSelection()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var cell = grid.Cells.Single(c => c.Row == 1 && c.Column == 1);
        var from = ViewPoint(h.View, 0, cell.X + 8, cell.Y + (cell.Height / 2));
        var to = ViewPoint(h.View, 0, cell.X + cell.Width - 8, cell.Y + (cell.Height / 2));

        Drag(h, from, to);

        Assert.Null(h.View.Tab.Editor!.ObjectSelection);
        Assert.False(h.View.Tab.Editor.Selection!.Value.IsEmpty);
    }

    [AvaloniaFact]
    public async Task APrefixDocumentMakesNoTableGestureAndOnlySelects()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        h.View.PrefixProbe = () => true;
        var grid = Grid(h);
        var revision = Revision(h);

        var border = BorderPoint(h.View, grid, 1);
        Drag(h, border, new Point(border.X + 40, border.Y));
        Assert.True(h.View.ObjectGesture is null || h.View.ObjectGesture.Phase == ObjectDragPhase.Idle);
        Assert.Equal(revision, Revision(h));

        var handle = RowHandlePoint(h.View, grid, 0);
        h.Window.MouseDown(handle, MouseButton.Left);
        Move(h, handle, RowHandlePoint(h.View, grid, 2));
        h.Window.MouseUp(RowHandlePoint(h.View, grid, 2), MouseButton.Left);
        ViewHarness.Pump();
        Assert.IsType<CellSelection>(h.View.Tab.Editor!.ObjectSelection);
        Assert.Equal(revision, Revision(h));
    }

    [AvaloniaFact]
    public async Task TheCursorChangesOverABorderAndTheHandle()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var grid = Grid(h);

        h.Window.MouseMove(CellCenter(view, grid, 1, 1));
        Assert.Equal(StandardCursorType.Ibeam, view.CursorTypeNow);

        h.Window.MouseMove(BorderPoint(view, grid, 1, 1));
        Assert.Equal(StandardCursorType.SizeWestEast, view.CursorTypeNow);

        h.Window.MouseMove(BorderPoint(view, grid, 3, 1));
        Assert.Equal(StandardCursorType.SizeWestEast, view.CursorTypeNow);

        h.Window.MouseMove(RowHandlePoint(view, grid, 1));
        Assert.Equal(StandardCursorType.Hand, view.CursorTypeNow);

        var top = ViewPoint(view, 0, (grid.ColumnBorders[0] + grid.ColumnBorders[1]) / 2, grid.Y - (2 * Tolerance(view)));
        h.Window.MouseMove(top);
        Assert.Equal(StandardCursorType.Arrow, view.CursorTypeNow);

        h.Window.MouseMove(CellCenter(view, grid, 2, 2));
        Assert.Equal(StandardCursorType.Ibeam, view.CursorTypeNow);
    }

    [AvaloniaFact]
    public async Task NoGestureStartsDuringASaveAndAPressOnlySelects()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var session = view.Tab.EnsureEditor();
        var grid = Grid(h);
        var revision = Revision(h);
        using (session.BeginSave())
        {
            var border = BorderPoint(view, grid, 1);
            h.Window.MouseDown(border, MouseButton.Left);
            Move(h, border, new Point(border.X + 40, border.Y));
            Assert.True(view.ObjectGesture is null || view.ObjectGesture.Phase == ObjectDragPhase.Idle);
            h.Window.MouseUp(new Point(border.X + 40, border.Y), MouseButton.Left);

            var handle = RowHandlePoint(view, grid, 0);
            h.Window.MouseDown(handle, MouseButton.Left);
            ViewHarness.Pump();
            Assert.IsType<CellSelection>(session.ObjectSelection);
            Move(h, handle, RowHandlePoint(view, grid, 2));
            Assert.True(view.ObjectGesture is null || view.ObjectGesture.Phase == ObjectDragPhase.Idle);
            h.Window.MouseUp(RowHandlePoint(view, grid, 2), MouseButton.Left);
            ViewHarness.Pump();
        }

        Assert.Equal(revision, Revision(h));
    }

    // Starts each kind of gesture and moves far enough to be a drag; returns the point of the pointer.
    private static Point StartGesture(ViewHarness h, TableGrid grid, string kind, int first = 0)
    {
        // A second gesture starts on another row: a press on the same point at once would be a double click.
        var view = h.View;
        var (from, to) = kind switch
        {
            "border" => (BorderPoint(view, grid, 1, first), BorderPoint(view, grid, 1, first) + new Vector(30, 0)),
            "edge" => (BorderPoint(view, grid, 3, first), BorderPoint(view, grid, 3, first) + new Vector(30, 0)),
            "row" => (RowHandlePoint(view, grid, first), RowHandlePoint(view, grid, 2)),
            _ => (CellCenter(view, grid, first, 0), CellCenter(view, grid, 1 + first, 1)),
        };
        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, to);
        Assert.Equal(ObjectDragPhase.Dragging, view.ObjectGesture!.Phase);
        return to;
    }

    [AvaloniaTheory]
    [InlineData("border")]
    [InlineData("edge")]
    [InlineData("row")]
    [InlineData("range")]
    public async Task EscapeCancelsEachDragKind(string kind)
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var revision = Revision(h);
        var widths = Values(h, grid).GridTwips.ToArray();
        var end = StartGesture(h, grid, kind);

        Stroke(h, PhysicalKey.Escape);

        Assert.Equal(ObjectDragPhase.Idle, h.View.ObjectGesture!.Phase);
        Assert.Equal(ObjectDragCancel.Escape, h.View.ObjectGesture.LastCancel);
        h.Window.MouseMove(new Point(end.X + 5, end.Y + 5));
        h.Window.MouseUp(end, MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(revision, Revision(h));
        Assert.Equal(widths, Values(h, grid).GridTwips);
        Assert.Equal("c00", TextOfCell(h, TableBlock(grid), 0, 0));
        if (kind == "range")
        {
            Assert.Null(h.View.Tab.Editor!.ObjectSelection);
        }
    }

    [AvaloniaTheory]
    [InlineData("border")]
    [InlineData("edge")]
    [InlineData("row")]
    public async Task CaptureLostCancelsTheTableDragKinds(string kind)
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var revision = Revision(h);
        var end = StartGesture(h, grid, kind);

        h.View.ObjectPointer!.Capture(null);
        ViewHarness.Pump();

        Assert.Equal(ObjectDragCancel.CaptureLost, h.View.ObjectGesture!.LastCancel);
        h.Window.MouseUp(end, MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(revision, Revision(h));
    }

    [AvaloniaTheory]
    [InlineData("border")]
    [InlineData("edge")]
    [InlineData("row")]
    [InlineData("range")]
    public async Task ASaveStartingOrAnUndoMidDragCancelsTheTableDragKinds(string kind)
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var session = h.View.Tab.EnsureEditor();
        var start = new TextPosition(StoryRef.Body, (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[0], 0);
        session.SetSelection(new TextRange(start, start));
        Assert.NotNull(session.Apply(ed => ed.InsertText(session.Selection!.Value, "x"), 1));
        var revision = Revision(h);

        var end = StartGesture(h, grid, kind);
        using (session.BeginSave())
        {
            Assert.Equal(ObjectDragCancel.SaveStarted, h.View.ObjectGesture!.LastCancel);
            h.Window.MouseUp(end, MouseButton.Left);
            ViewHarness.Pump();
        }

        Assert.Equal(revision, Revision(h));

        end = StartGesture(h, Grid(h), kind, first: 1);
        Assert.NotNull(session.Apply(ed => ed.History.Undo(), 1));
        Assert.Equal(ObjectDragCancel.DocumentChanged, h.View.ObjectGesture!.LastCancel);
        var after = Revision(h);
        h.Window.MouseUp(end, MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(after, Revision(h));
    }

    [AvaloniaFact]
    public async Task ADragOnAMergedCellSelectsItsWholeSpan()
    {
        using var h = await OpenAsync(Merged());
        var grid = Grid(h);
        var from = CellCenter(h.View, grid, 0, 2);
        var to = CellCenter(h.View, grid, 1, 1);

        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, to);
        h.Window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();

        // Column 1 of row 0 belongs to a cell that spans the columns 0 and 1, and the first cell of row 1 spans two rows: the range grows to take both whole.
        var selection = Assert.IsType<CellSelection>(h.View.Tab.Editor!.ObjectSelection);
        Assert.Equal((0, 0, 2, 2), (selection.Range.FirstRow, selection.Range.FirstColumn, selection.Range.LastRow, selection.Range.LastColumn));
    }

    [AvaloniaFact]
    public async Task AGestureOnANestedTableIsRefused()
    {
        using var h = await OpenAsync(Nested());
        var view = h.View;
        var grid = Grid(h);
        var outer = (Table)view.Tab.Docx.Model.Body.Blocks[grid.BlockIndex];
        var inner = (Table)((TableCell)((TableRow)outer.Rows[0]).Items[0]).Blocks[0];
        var paragraph = (Paragraph)((TableCell)((TableRow)inner.Rows[0]).Items[0]).Blocks[0];
        var bar = view.CaretBounds(new TextPosition(StoryRef.Body, paragraph, 1))!.Value;
        var from = new Point(bar.X + 1, bar.Y + (bar.Height / 2));
        var to = CellCenter(view, grid, 1, 1);
        var revision = Revision(h);

        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, to);
        h.Window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();

        Assert.Null(view.Tab.Editor!.ObjectSelection);
        Assert.True(view.ObjectGesture is null || view.ObjectGesture.Phase == ObjectDragPhase.Idle);
        Assert.Equal(revision, Revision(h));
        Assert.Single(h.View.Runner.PageLayout.Page(0).Objects.Tables);
    }

    [AvaloniaFact]
    public async Task ThePointerMoveCostsAtMost16msP95OnTheThesis()
    {
        using var h = await OpenAsync(WindowFixture.Thesis);
        var view = h.View;
        var layout = view.Runner.PageLayout;
        var page = Enumerable.Range(0, layout.LaidOutPages).First(i => layout.TryGetPage(i, out var p) && p!.Objects.Tables.Count > 0);
        view.ScrollToPage(page);
        ViewHarness.Pump();
        var grid = layout.Page(page).Objects.Tables[0];
        var revision = Revision(h);

        // A sweep over the table and its borders and margins, on the page that has one, with no button down: the dispatch of the move (the hit test and the cursor). The moves of a drag are timed in ThePointerMoveDuringADragCostsAtMost16msP95OnTheThesis.
        var targets = new List<Point>();
        for (var round = 0; round < 6; round++)
        {
            for (var i = 0; i < 60; i++)
            {
                var x = grid.X - 20 + ((grid.Width + 40) * i / 60.0);
                var y = grid.Y + (grid.Height * ((i * 7) % 60) / 60.0);
                var point = ViewPoint(view, page, x, y);

                // A hover outside the window would measure nothing: the part of the table below the view is left out of the sweep.
                if (new Rect(view.Bounds.Size).Contains(point))
                {
                    targets.Add(point);
                }
            }
        }

        Assert.True(targets.Count >= 120, $"the sweep has {targets.Count} points inside the view");
        AssertP95WithinBudget("pointer moves over a table of the thesis", view, targets, target => h.Window.MouseMove(target));
        Assert.Equal(revision, Revision(h));
    }

    // The plan's 16 ms of CPU per pointer move, as a regression guard that a load spike cannot fail: up to three independent attempts, each with a full warm-up pass over the same targets (so lazy start-up and JIT are never measured), a collection and a drained dispatcher before it; the test fails only when the best attempt's p95 is above the budget (a real regression is slow in every attempt).
    private static void AssertP95WithinBudget(string label, PageView view, List<Point> targets, Action<Point> move)
    {
        const double budgetMs = 16.0;
        const int attempts = 3;
        var bounds = new Rect(view.Bounds.Size);
        Assert.All(targets, t => Assert.True(bounds.Contains(t), $"{label}: the target {t} is inside the view {bounds} (a point near the edge could scroll the view)"));

        var notes = new List<string>();
        var best = double.MaxValue;
        for (var attempt = 1; attempt <= attempts && best > budgetMs; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            ViewHarness.Pump();
            foreach (var target in targets)
            {
                move(target);
            }

            ViewHarness.Pump();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var samples = new List<double>(targets.Count);
            foreach (var target in targets)
            {
                var timer = Stopwatch.StartNew();
                move(target);
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }

            samples.Sort();
            var p95 = samples[(int)(samples.Count * 0.95)];
            best = Math.Min(best, p95);
            notes.Add($"attempt {attempt}: p95 {p95:F3} ms, median {samples[samples.Count / 2]:F3} ms, max {samples[^1]:F3} ms over {samples.Count} moves");
        }

        var note = $"{label}: best p95 {best:F3} ms (budget {budgetMs:F1}); {string.Join("; ", notes)}";
        TestContext.Current.SendDiagnosticMessage(note);
        Assert.True(best <= budgetMs, note);
    }

    // The thesis table with the most cells that has three columns or more on its page, scrolled into view; returns its page and piece.
    private static async Task<(int Page, TableGrid Grid)> BigTableAsync(ViewHarness h)
    {
        var view = h.View;
        var layout = view.Runner.PageLayout;
        TableGrid? best = null;
        var bestPage = -1;
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            if (layout.TryGetPage(i, out var p) && p is not null)
            {
                foreach (var t in p.Objects.Tables.Where(t => t.ColumnBorders.Count >= 4 && t.RowEdges.Count >= 4))
                {
                    if (best is null || t.Cells.Count > best.Cells.Count)
                    {
                        (best, bestPage) = (t, i);
                    }
                }
            }
        }

        Assert.NotNull(best);
        view.ScrollToBlock(best!.BlockIndex);
        await ViewHarness.WaitAsync(() => !view.IsWaitingForAnchor);
        ViewHarness.Pump();
        return (bestPage, layout.Page(bestPage).Objects.Tables.First(t => t.BlockIndex == best.BlockIndex));
    }

    [AvaloniaTheory]
    [InlineData("range")]
    [InlineData("border")]
    [InlineData("edge")]
    [InlineData("row")]
    public async Task ThePointerMoveDuringADragCostsAtMost16msP95OnTheThesis(string kind)
    {
        using var h = await OpenAsync(WindowFixture.Thesis);
        var view = h.View;
        var (page, grid) = await BigTableAsync(h);
        var revision = Revision(h);
        Point At(double x, double y) => ViewPoint(view, page, x, y);
        var cells = grid.Cells.Where(c => c.RowSpan >= 1).ToList();
        var first = cells[0];
        Point from;
        var targets = new List<Point>();
        switch (kind)
        {
            case "range":
                from = At(first.X + 8, first.Y + (first.Height / 2));

                // Every move lands in another cell, so every move changes the focus cell and selects a range.
                for (var round = 0; round < 4; round++)
                {
                    targets.AddRange(cells.Select(c => At(c.X + (c.Width / 2), c.Y + (c.Height / 2))));
                }

                break;
            case "border":
            case "edge":
                var border = kind == "border" ? 1 : grid.ColumnBorders.Count - 1;
                from = At(grid.ColumnBorders[border], first.Y + (first.Height / 2));
                targets.AddRange(Enumerable.Range(0, 240).Select(i => new Point(from.X + (((i % 2) == 0 ? 1 : -1) * (5 + (i % 40))), from.Y)));
                break;
            default:
                from = At(grid.X - (2 * Tolerance(view)), first.Y + (first.Height / 2));
                targets.AddRange(Enumerable.Range(0, 240).Select(i => At(grid.X + 5, grid.Y + (grid.Height * ((i * 13) % 100) / 100.0))));
                break;
        }

        Assert.True(new Rect(view.Bounds.Size).Contains(from), "the table is in view");
        h.Window.MouseDown(from, MouseButton.Left);

        // The work of one move of the pointer: its handler (the machine, the live range) and the render pass the move asks for.
        AssertP95WithinBudget($"{kind} drag over {cells.Count} cells", view, targets, target =>
        {
            h.Window.MouseMove(target);
            ViewHarness.Pump();
        });

        Assert.True(view.ObjectGesture is { Phase: ObjectDragPhase.Dragging }, "a drag is going on");
        Stroke(h, PhysicalKey.Escape);
        h.Window.MouseUp(from, MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(revision, Revision(h));
    }

    [AvaloniaFact]
    public async Task APressOnABorderThatStartsNoGesturePlacesTheCaretLikeAnyPress()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var session = view.Tab.EnsureEditor();
        var grid = Grid(h);
        var revision = Revision(h);
        var border = BorderPoint(view, grid, 1, 1);

        // A prefix document: no gesture, the press goes to the text and ends a stale selection of cells.
        view.PrefixProbe = () => true;
        Assert.True(session.SelectRow(TableOf(grid), 0));
        h.Window.MouseDown(border, MouseButton.Left);
        h.Window.MouseUp(border, MouseButton.Left);
        ViewHarness.Pump();
        Assert.Null(session.ObjectSelection);
        Assert.NotNull(session.Editor.TableOf(session.Selection!.Value.End));
        Assert.True(view.ObjectGesture is null || view.ObjectGesture.Phase == ObjectDragPhase.Idle);
        view.PrefixProbe = null;

        // A save: the same; the caret change is queued like any other and applied when the save ends.
        Assert.True(session.SelectRow(TableOf(grid), 0));
        var before = session.Selection!.Value.End.Paragraph;
        using (session.BeginSave())
        {
            var other = BorderPoint(view, grid, 2, 2);
            h.Window.MouseDown(other, MouseButton.Left);
            h.Window.MouseUp(other, MouseButton.Left);
            ViewHarness.Pump();
            Assert.Null(session.ObjectSelection);
            Assert.True(view.ObjectGesture is null || view.ObjectGesture.Phase == ObjectDragPhase.Idle);
        }

        ViewHarness.Pump();
        Assert.NotNull(session.Editor.TableOf(session.Selection!.Value.End));
        Assert.NotSame(before, session.Selection.Value.End.Paragraph);
        Assert.Equal(revision, Revision(h));
    }

    [AvaloniaFact]
    public async Task ARightClickOrADoubleClickOnABorderIsAPlainOne()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var session = view.Tab.EnsureEditor();
        var grid = Grid(h);
        var border = BorderPoint(view, grid, 1, 0);

        h.Window.MouseDown(border, MouseButton.Right);
        h.Window.MouseUp(border, MouseButton.Right);
        ViewHarness.Pump();
        Assert.True(view.ObjectGesture is null || view.ObjectGesture.Phase == ObjectDragPhase.Idle);
        Assert.NotNull(session.Editor.TableOf(session.Selection!.Value.End));
        Assert.Null(session.ObjectSelection);

        // A double click (the second press on the same point) selects text as everywhere else and starts no gesture.
        var other = BorderPoint(view, grid, 2, 2);
        Click(h, other);
        Click(h, other);
        Assert.True(view.ObjectGesture is null || view.ObjectGesture.Phase == ObjectDragPhase.Idle);
        Assert.Null(session.ObjectSelection);
        Assert.NotNull(session.Editor.TableOf(session.Selection!.Value.End));
    }

    [AvaloniaFact]
    public async Task AZoomInTheMiddleOfAWidthDragEndsItWithNoChange()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var view = h.View;
        var grid = Grid(h);
        var revision = Revision(h);
        var from = BorderPoint(view, grid, 1);
        var to = new Point(from.X + 40, from.Y);

        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, to);
        Assert.Equal(ObjectDragPhase.Dragging, view.ObjectGesture!.Phase);
        view.ZoomAround(view.Zoom * 1.5, to);
        ViewHarness.Pump();
        h.Window.MouseMove(new Point(to.X + 20, to.Y));
        h.Window.MouseUp(new Point(to.X + 20, to.Y), MouseButton.Left);
        ViewHarness.Pump();

        Assert.Equal(ObjectDragCancel.ViewChanged, view.ObjectGesture.LastCancel);
        Assert.Equal(revision, Revision(h));

        // A release with the zoom changed and no move in between: the same.
        grid = Grid(h);
        var again = BorderPoint(view, grid, 1, 1);
        h.Window.MouseDown(again, MouseButton.Left);
        Move(h, again, new Point(again.X + 30, again.Y));
        view.ZoomAround(view.Zoom / 1.5, again);
        ViewHarness.Pump();
        h.Window.MouseUp(new Point(again.X + 30, again.Y), MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal(ObjectDragCancel.ViewChanged, view.ObjectGesture.LastCancel);
        Assert.Equal(revision, Revision(h));
    }

    [AvaloniaFact]
    public async Task AWidenedRangeIsStillTheWidenedRangeAfterABorderDrag()
    {
        using var h = await OpenAsync(Merged());
        var view = h.View;
        var grid = Grid(h);
        var session = view.Tab.EnsureEditor();
        var from = CellCenter(view, grid, 0, 2);
        var to = CellCenter(view, grid, 1, 1);
        h.Window.MouseDown(from, MouseButton.Left);
        Move(h, from, to);
        h.Window.MouseUp(to, MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal((0, 0, 2, 2), Rect(session));
        var revision = Revision(h);

        // A border between the columns 1 and 2 (it runs through the rows of a merged grid), dragged: the width changes and the selection is the widened one again.
        grid = Grid(h);
        var border = BorderPoint(view, grid, 2, 1);
        Drag(h, border, new Point(border.X + 30, border.Y));

        Assert.Equal(revision + 1, Revision(h));
        Assert.Equal((0, 0, 2, 2), Rect(session));
    }

    private static (int, int, int, int) Rect(EditSession session)
    {
        var range = Assert.IsType<CellSelection>(session.ObjectSelection).Range;
        return (range.FirstRow, range.FirstColumn, range.LastRow, range.LastColumn);
    }

    [AvaloniaFact]
    public async Task ShiftArrowsExtendACellRangeOverMergedCellsToWholeCells()
    {
        using var h = await OpenAsync(Merged());
        var view = h.View;
        var grid = Grid(h);
        var session = view.Tab.EnsureEditor();
        Click(h, CellCenter(view, grid, 0, 2));
        Assert.True(session.SelectCells(TableOf(grid), (0, 2), (0, 2), wholeMergedCells: true));

        // The focus steps one grid column to the left, into the merged cell of the first row: the range takes the whole cell.
        Stroke(h, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Assert.Equal((0, 0, 0, 2), Rect(session));
        Assert.Equal((0, 2), session.CellAnchor);
        Assert.Equal((0, 1), session.CellFocus);

        // One row down: the first cell of the second row spans two rows and is taken whole, the range the pointer gives in ADragOnAMergedCellSelectsItsWholeSpan.
        Stroke(h, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Assert.Equal((0, 0, 2, 2), Rect(session));
    }

    [AvaloniaFact]
    public async Task ARowDraggedPastTheBottomOfTheTableLandsInTheLastRow()
    {
        using var h = await OpenAsync(ObjectDocx.Write(_folder));
        var grid = Grid(h);
        var block = TableBlock(grid);
        var from = RowHandlePoint(h.View, grid, 0);

        Drag(h, from, ViewPoint(h.View, 0, grid.X + 5, grid.Y + grid.Height + 60));

        Assert.Equal("c10", TextOfCell(h, block, 0, 0));
        Assert.Equal("c00", TextOfCell(h, block, 2, 0));
    }
}
