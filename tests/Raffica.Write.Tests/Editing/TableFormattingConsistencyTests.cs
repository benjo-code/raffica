using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The rules of milestone 4.5a that tie the formatting operations to the layout and to the structure operations: the first width write freezes the widths the layout draws (not the model's guess), the caret stays
/// where the user was, and rows and columns added to a formatted table, cells merged and split, keep the widths, the borders and the shading consistent.
/// </summary>
public sealed class TableFormattingConsistencyTests
{
    private static readonly XNamespace W = LayoutModel.W;

    private const string Normal = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";

    private static string Tc(string text, string props = "") =>
        $"<w:tc>{(props.Length == 0 ? string.Empty : $"<w:tcPr>{props}</w:tcPr>")}<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static string Tr(string props, params string[] cells) => $"<w:tr>{(props.Length == 0 ? string.Empty : $"<w:trPr>{props}</w:trPr>")}{string.Concat(cells)}</w:tr>";

    private static string Dxa(int w) => $"<w:tcW w:w=\"{w}\" w:type=\"dxa\"/>";

    private static string Pct(int w) => $"<w:tcW w:w=\"{w}\" w:type=\"pct\"/>";

    private static string Grid(params int[] widths) => $"<w:tblGrid>{string.Concat(widths.Select(w => $"<w:gridCol w:w=\"{w}\"/>"))}</w:tblGrid>";

    private static string After => "<w:p><w:r><w:t>after</w:t></w:r></w:p>";

    // A percentage table of three columns with 30, 30 and 40 percent of the text width.
    private static string Percentage3() =>
        $"<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/></w:tblPr>{Grid(3000, 3000, 3000)}" +
        string.Concat(Enumerable.Range(0, 2).Select(r => Tr(string.Empty, Tc($"{r}0", Pct(1500)), Tc($"{r}1", Pct(1500)), Tc($"{r}2", Pct(2000))))) + "</w:tbl>" + After;

    // An autofit table (no fixed layout, an automatic width): the layout draws the preferred widths of the cells, which are nothing like its grid.
    private static string Autofit3() =>
        $"<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr>{Grid(3000, 3000, 3000)}" +
        Tr(string.Empty, Tc("a", Dxa(1500)), Tc("a rather longer text in the second column that wants room", Dxa(4500)), Tc("mid length text", Dxa(3000))) +
        Tr(string.Empty, Tc("b", Dxa(1500)), Tc("c", Dxa(4500)), Tc("d", Dxa(3000))) + "</w:tbl>" + After;

    private static string Fixed(int rows, int columns = 3, int width = 2000)
    {
        var grid = Enumerable.Repeat(width, columns).ToArray();
        return $"<w:tbl><w:tblPr><w:tblW w:w=\"{width * columns}\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr>{Grid(grid)}" +
            string.Concat(Enumerable.Range(0, rows).Select(r => Tr(string.Empty, [.. Enumerable.Range(0, columns).Select(c => Tc($"{r}{c}", Dxa(width)))])))
            + "</w:tbl>" + After;
    }

    private static WordDocument Document(string body) => LayoutModel.Document(body, LayoutModel.StylesPart(Normal));

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static TableRef Ref(int block = 0) => new(StoryRef.Body, block);

    private static Table TableAt(WordDocument document, int block = 0) => (Table)document.Body.Blocks[block];

    private static TableCell CellAt(WordDocument document, int row, int column) => TableGridMap.Of(TableAt(document)).At(row, column)!.Cell;

    private static string? Attr(XElement? element, string name) => (string?)element?.Attribute(W + name);

    private static XElement? Side(TableCell cell, string side) => cell.Properties.Get(W + "tcBorders")?.Element(W + side);

    private static string Signature(TableCell cell, string side) => Side(cell, side) is { } e ? $"{Attr(e, "val")}/{Attr(e, "sz")}/{Attr(e, "color")}" : string.Empty;

    private static string? Fill(TableCell cell) => Attr(cell.Properties.Get(W + "shd"), "fill");

    // ---- the first width write freezes the drawn widths ----------------------------------------------------------------------------

    private static async Task AssertDrawnEqualsWrittenAsync(WordDocument document, string label)
    {
        var table = TableAt(document);
        var layout = await SectionOracleTests.LaidAsync(document);
        var points = DrawnTableWidths.Points(layout, Ref(), table.GridColumns.Count);
        Assert.NotNull(points);
        for (var c = 0; c < points.Length; c++)
        {
            Assert.True(Math.Abs(points[c] - (table.GridColumns[c] / 20f)) < 0.02f, $"{label}: column {c} is drawn {points[c]:F3} pt and written {table.GridColumns[c] / 20f:F3} pt");
        }
    }

    [Theory]
    [InlineData("percentage")]
    [InlineData("autofit")]
    public async Task DistributeWithTheDrawnWidthsFreezesTheWidthTheReaderSees(string kind)
    {
        var document = Document(kind == "percentage" ? Percentage3() : Autofit3());
        var layout = await SectionOracleTests.LaidAsync(document);
        var drawn = DrawnTableWidths.Of(layout, Ref(), 3)!;
        var drawnPoints = DrawnTableWidths.Points(layout, Ref(), 3)!;
        Assert.NotEqual(drawn[0], drawn[2]);

        Assert.NotNull(Editor(document).DistributeColumns(Ref(), drawn));

        var table = TableAt(document);
        // The table keeps the width it was drawn with: the sum of the drawn widths, shared equally (the remainder to the last column).
        Assert.Equal(drawn.Sum(), table.GridColumns.Sum());
        Assert.Equal(drawn.Sum(), int.Parse(Attr(table.Properties.Get(W + "tblW"), "w")!, CultureInfo.InvariantCulture));
        Assert.Equal("fixed", Attr(table.Properties.Get(W + "tblLayout"), "type"));
        TableGridChecks.AssertWidthsAgree(table, kind);
        Assert.InRange(table.GridColumns[0], (drawn.Sum() / 3) - 1, (drawn.Sum() / 3) + 1);
        await AssertDrawnEqualsWrittenAsync(document, kind + " distribute");
        var after = DrawnTableWidths.Points(await SectionOracleTests.LaidAsync(document), Ref(), 3)!;
        Assert.True(Math.Abs(after.Sum() - drawnPoints.Sum()) < 0.1f, $"the table was drawn {drawnPoints.Sum():F2} pt wide and is {after.Sum():F2} pt wide after the first write");
    }

    [Theory]
    [InlineData("percentage")]
    [InlineData("autofit")]
    public async Task SetTableWidthWithTheDrawnWidthsKeepsTheDrawnProportionsAndHitsTheWidthExactly(string kind)
    {
        var document = Document(kind == "percentage" ? Percentage3() : Autofit3());
        var layout = await SectionOracleTests.LaidAsync(document);
        var drawn = DrawnTableWidths.Of(layout, Ref(), 3)!;
        var requested = drawn.Sum() * 3 / 4;

        Assert.NotNull(Editor(document).SetTableWidth(Ref(), requested, drawn));

        var table = TableAt(document);
        Assert.Equal(requested, table.GridColumns.Sum());
        for (var c = 0; c < 3; c++)
        {
            var expected = (double)requested * drawn[c] / drawn.Sum();
            Assert.True(Math.Abs(table.GridColumns[c] - expected) <= 1.0, $"column {c}: {table.GridColumns[c]} twips, the drawn proportion asks for {expected:F1}");
        }

        TableGridChecks.AssertWidthsAgree(table, kind);
        await AssertDrawnEqualsWrittenAsync(document, kind + " table width");
    }

    [Fact]
    public async Task WithoutTheDrawnWidthsTheProportionsComeFromTheGrid()
    {
        // The fallback: the grid of a percentage table is three equal columns, though the layout draws 30, 30 and 40 percent.
        var document = Document(Percentage3());
        var layout = await SectionOracleTests.LaidAsync(document);
        var drawn = DrawnTableWidths.Of(layout, Ref(), 3)!;
        Assert.True(drawn[2] > drawn[0] + 100, "the third column is drawn wider than the first");

        Assert.NotNull(Editor(document).SetTableWidth(Ref(), 6000));
        Assert.Equal([2000, 2000, 2000], TableAt(document).GridColumns);

        var withDrawn = Document(Percentage3());
        Assert.NotNull(Editor(withDrawn).SetTableWidth(Ref(), 6000, drawn));
        Assert.True(TableAt(withDrawn).GridColumns[2] > TableAt(withDrawn).GridColumns[0] + 100);
    }

    [Fact]
    public async Task AFixedTableIsNotFrozenFromTheDrawnWidths()
    {
        // A table that is already fixed and in twips is read from its own widths; the drawn list is checked and not used.
        var document = Document(Fixed(2));
        var layout = await SectionOracleTests.LaidAsync(document);
        Assert.Equal(6000, DrawnTableWidths.Of(layout, Ref(), 3)!.Sum());

        Assert.NotNull(Editor(document).DistributeColumns(Ref(), [100, 200, 300]));

        Assert.Equal([2000, 2000, 2000], TableAt(document).GridColumns);
    }

    [Fact]
    public void BadDrawnWidthsAreRefused()
    {
        var document = Document(Percentage3());
        var editor = Editor(document);

        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForDistributeColumns(Ref(), [1000, 1000]));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForDistributeColumns(Ref(), [1000, 0, 1000]));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForDistributeColumns(Ref(), [1000, -5, 1000]));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForDistributeColumns(Ref(), [TableRules.MaxTwips, TableRules.MaxTwips, 1]));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetTableWidth(Ref(), 6000, [1000, 1000]));
        Assert.Null(editor.RefusalForSetTableWidth(Ref(), 6000, [1000, 1000, 1000]));
        Assert.Null(editor.RefusalForDistributeColumns(Ref()));
    }

    // ---- the caret stays where the user was ----------------------------------------------------------------------------------------

    private static Paragraph ParagraphAt(WordDocument document, int row, int column) => (Paragraph)CellAt(document, row, column).Blocks[0];

    [Fact]
    public void TheCaretStaysInTheRowItWasInAfterEveryFormattingOperation()
    {
        var document = Document(Fixed(6));
        var editor = Editor(document);
        var range = new CellRange(Ref(), 0, 0, 1, 1);

        var steps = new List<(string Label, Func<TextPosition, EditResult?> Run)>
        {
            ("borders", c => editor.SetBorders(range, BorderSpec.All(), c)),
            ("shading", c => editor.SetShading(range, 0xFFFF0000, c)),
            ("header row", c => editor.SetHeaderRow(Ref(), true, c)),
            ("column widths", c => editor.SetColumnWidths(Ref(), [1000, 2000, 3000], c)),
            ("table width", c => editor.SetTableWidth(Ref(), 7000, null, c)),
            ("row height", c => editor.SetRowHeight(Ref(), 0, 500, false, c)),
            ("distribute", c => editor.DistributeColumns(Ref(), null, c)),
        };

        foreach (var (label, run) in steps)
        {
            var caret = new TextPosition(StoryRef.Body, ParagraphAt(document, 3, 1), 1);
            var result = run(caret);

            Assert.NotNull(result);
            Assert.True(ReferenceEquals(result.Caret.Paragraph, ParagraphAt(document, 3, 1)), label + ": the caret left row 3");
            Assert.Equal(1, result.Caret.Offset);
            Assert.Equal("31", TestEdit.TextOf(result.Caret.Paragraph));
        }
    }

    [Fact]
    public void WithoutACaretInTheTableTheCaretIsInTheFirstCell()
    {
        var document = Document(Fixed(4));
        var editor = Editor(document);
        var outside = new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[1], 2);

        var none = editor.SetShading(new CellRange(Ref(), 2, 0, 2, 0), 0xFF00B050);
        Assert.NotNull(none);
        Assert.True(ReferenceEquals(none.Caret.Paragraph, ParagraphAt(document, 2, 0)), "the first cell of the range");

        var elsewhere = editor.SetHeaderRow(Ref(), true, outside);
        Assert.NotNull(elsewhere);
        Assert.True(ReferenceEquals(elsewhere.Caret.Paragraph, ParagraphAt(document, 0, 0)), "a caret outside the table is not carried over");
        Assert.Equal(0, elsewhere.Caret.Offset);
    }

    [Fact]
    public void UndoAndRedoLandInTheTableAfterAFormattingStepWithACaret()
    {
        var document = Document(Fixed(6));
        var editor = Editor(document);
        var caret = new TextPosition(StoryRef.Body, ParagraphAt(document, 4, 2), 0);

        Assert.NotNull(editor.SetShading(new CellRange(Ref(), 4, 2, 4, 2), 0xFFFFFF00, caret));
        Assert.NotNull(editor.SetRowHeight(Ref(), 4, 700, true, new TextPosition(StoryRef.Body, ParagraphAt(document, 4, 2), 1)));

        for (var i = 0; i < 2; i++)
        {
            var undone = editor.History.Undo();
            Assert.NotNull(undone);
            Assert.True(TableOfParagraph(document, undone.Caret.Paragraph), "undo " + i);
        }

        Assert.Null(Fill(CellAt(document, 4, 2)));
        for (var i = 0; i < 2; i++)
        {
            var redone = editor.History.Redo();
            Assert.NotNull(redone);
            Assert.True(TableOfParagraph(document, redone.Caret.Paragraph), "redo " + i);
        }

        Assert.Equal("FFFF00", Fill(CellAt(document, 4, 2)));
    }

    private static bool TableOfParagraph(WordDocument document, Paragraph paragraph) =>
        document.Body.Blocks.OfType<Table>().Any(t => TableGridMap.Of(t).Cells.Any(c => c.Cell.Blocks.Contains(paragraph)));

    // ---- formatting meets structure ------------------------------------------------------------------------------------------------

    private static (WordDocument Document, TextEditor Editor) FormattedFixed()
    {
        var document = Document(Fixed(3));
        var editor = Editor(document);
        Assert.NotNull(editor.SetColumnWidths(Ref(), [1000, 2500, 2500]));
        Assert.NotNull(editor.SetBorders(new CellRange(Ref(), 0, 0, 2, 2), BorderSpec.All(TableBorderStyle.Solid, 8, 0xFFFF0000)));
        // A line of its own between the second and the third column, and one under the second row: the cells made next to them must take these, not the lines of their other sides.
        Assert.NotNull(editor.SetBorders(new CellRange(Ref(), 0, 1, 2, 1), BorderSpec.Sides(BorderSides.Right, TableBorderStyle.Dotted, 12, 0xFF0070C0)));
        Assert.NotNull(editor.SetBorders(new CellRange(Ref(), 1, 0, 1, 2), BorderSpec.Sides(BorderSides.Bottom, TableBorderStyle.Dotted, 12, 0xFF0070C0)));
        Assert.NotNull(editor.SetShading(new CellRange(Ref(), 1, 1, 1, 1), 0xFFFFFF00));
        return (document, editor);
    }

    private static int AllPairs(Table table)
    {
        var map = TableGridMap.Of(table);
        return (map.RowCount * (map.Columns - 1)) + ((map.RowCount - 1) * map.Columns);
    }

    [Fact]
    public void AddColumnToAFixedFormattedTableKeepsWidthsBordersAndShadingConsistent()
    {
        var (document, editor) = FormattedFixed();

        Assert.NotNull(editor.AddColumn(Ref(), 1, after: true));

        var table = TableAt(document);
        TableGridChecks.AssertGridEqualsCells(table, "add column");
        TableGridChecks.AssertWidthsAgree(table, "add column");
        Assert.Equal(4, table.GridColumns.Count);
        // The new column copies the column it was made next to: its shading (the yellow cell gets a yellow neighbour) and its lines, and the line between the two is one line.
        Assert.Equal("FFFF00", Fill(CellAt(document, 1, 2)));
        Assert.Null(Fill(CellAt(document, 0, 2)));
        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(Signature(CellAt(document, row, 1), "right"), Signature(CellAt(document, row, 2), "left"));
            Assert.Equal("dotted/12/0070C0", Signature(CellAt(document, row, 2), "left"));
        }

        Assert.Equal(AllPairs(table), TableGridChecks.SymmetricBorders(table).Count);
    }

    [Fact]
    public void AddColumnBeforeAndAddRowToAFixedFormattedTableKeepEverythingConsistent()
    {
        var (document, editor) = FormattedFixed();

        Assert.NotNull(editor.AddColumn(Ref(), 0, after: false));
        Assert.NotNull(editor.AddRow(Ref(), 1, after: true));
        Assert.NotNull(editor.AddRow(Ref(), 0, after: false));

        var table = TableAt(document);
        TableGridChecks.AssertGridEqualsCells(table, "structure");
        TableGridChecks.AssertWidthsAgree(table, "structure");
        Assert.Equal(AllPairs(table), TableGridChecks.SymmetricBorders(table).Count);
        Assert.Equal(5, TableGridMap.Of(table).RowCount);
        // The row added below the yellow row copies it, the row added above the first copies the first.
        Assert.Equal("FFFF00", Fill(CellAt(document, 3, 2)));
        // The new row under the second row has the line that was under it as the line above.
        Assert.All(Enumerable.Range(0, 4), column => Assert.Equal("dotted/12/0070C0", Signature(CellAt(document, 3, column), "top")));
        Assert.Null(Fill(CellAt(document, 0, 2)));
    }

    [Fact]
    public void MergeKeepsTheFirstCellsShadingAndTheBordersOfTheOutlineAndSplitRestoresTheCells()
    {
        var document = Document(Fixed(3));
        var editor = Editor(document);
        Assert.NotNull(editor.SetBorders(new CellRange(Ref(), 0, 0, 2, 2), BorderSpec.All()));
        Assert.NotNull(editor.SetShading(new CellRange(Ref(), 0, 0, 0, 0), 0xFFFFFF00));
        Assert.NotNull(editor.SetShading(new CellRange(Ref(), 0, 1, 0, 1), 0xFF0070C0));
        // The line on the right of the second cell is red, on both cells it belongs to.
        Assert.NotNull(editor.SetBorders(new CellRange(Ref(), 0, 1, 0, 1), BorderSpec.Sides(BorderSides.Right, TableBorderStyle.Solid, 12, 0xFFFF0000)));

        Assert.NotNull(editor.MergeCells(new CellRange(Ref(), 0, 0, 0, 1)));

        var table = TableAt(document);
        var merged = CellAt(document, 0, 0);
        Assert.Equal(2, TableGridMap.Of(table).At(0, 0)!.ColumnSpan);
        Assert.Equal("FFFF00", Fill(merged));
        // The outline of the merged cell is the outline of the two: the red line of the second cell is now the right side of the merged one, and the cell beside it has the same line.
        Assert.Equal("single/12/FF0000", Signature(merged, "right"));
        Assert.Equal("single/12/FF0000", Signature(CellAt(document, 0, 2), "left"));
        TableGridChecks.AssertGridEqualsCells(table, "merge");
        TableGridChecks.AssertWidthsAgree(table, "merge");

        Assert.NotNull(editor.SplitCell(Ref(), 0, 0));

        table = TableAt(document);
        TableGridChecks.AssertGridEqualsCells(table, "split");
        TableGridChecks.AssertWidthsAgree(table, "split");
        var map = TableGridMap.Of(table);
        Assert.Equal(3, map.CellsOfRow(0).Count());
        Assert.All(map.CellsOfRow(0), c => Assert.Equal(1, c.ColumnSpan));
        // The cells made by the split copy the merged cell, and the line between them is one line.
        Assert.Equal("FFFF00", Fill(CellAt(document, 0, 1)));
        Assert.Equal(Signature(CellAt(document, 0, 0), "right"), Signature(CellAt(document, 0, 1), "left"));
        Assert.Equal(Signature(CellAt(document, 0, 1), "right"), Signature(CellAt(document, 0, 2), "left"));
    }

    // ---- the repeated header row with stray marks ---------------------------------------------------------------------------------

    [Fact]
    public void TurningTheHeaderRowOnMarksTheFirstRowAndLeavesStrayMarksFurtherDown()
    {
        var document = Document(
            $"<w:tbl><w:tblPr><w:tblW w:w=\"2000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000)}" +
            Tr(string.Empty, Tc("a", Dxa(2000))) + Tr(string.Empty, Tc("b", Dxa(2000))) + Tr("<w:tblHeader/>", Tc("c", Dxa(2000))) + Tr("<w:tblHeader/>", Tc("d", Dxa(2000))) + "</w:tbl>" + After);

        Assert.NotNull(Editor(document).SetHeaderRow(Ref(), true));

        // Only the leading rows repeat, so the header is the first row alone; the marks on rows 2 and 3 stay and have no effect until the rows between them are marked too.
        var rows = TableAt(document).Rows.OfType<TableRow>().ToList();
        Assert.NotNull(rows[0].Properties.Get(W + "tblHeader"));
        Assert.Null(rows[1].Properties.Get(W + "tblHeader"));
        Assert.NotNull(rows[2].Properties.Get(W + "tblHeader"));
        Assert.NotNull(rows[3].Properties.Get(W + "tblHeader"));
    }
}
