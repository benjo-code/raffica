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

        // A sweep over the table and its borders and margins, on the page that has one: the work of the handler (the hit test and the cursor).
        var samples = new List<double>();
        for (var round = 0; round < 6; round++)
        {
            for (var i = 0; i < 60; i++)
            {
                var x = grid.X - 20 + ((grid.Width + 40) * i / 60.0);
                var y = grid.Y + (grid.Height * ((i * 7) % 60) / 60.0);
                var point = ViewPoint(view, page, x, y);
                var timer = Stopwatch.StartNew();
                h.Window.MouseMove(point);
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
        }

        samples.Sort();
        var p95 = samples[(int)(samples.Count * 0.95)];
        TestContext.Current.SendDiagnosticMessage($"pointer moves over a table of the thesis: p95 {p95:F3} ms, median {samples[samples.Count / 2]:F3} ms, max {samples[^1]:F3} ms over {samples.Count} moves");

        // The budget of the plan (16 ms of CPU for the handler) holds as it is: the measured cost is far below it, so no extra room for CI jitter was taken.
        Assert.True(p95 <= 16.0, $"p95 {p95:F3} ms median {samples[samples.Count / 2]:F3} max {samples[^1]:F3}");
        Assert.Equal(revision, Revision(h));
    }
}
