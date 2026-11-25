using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The table formatting operations of milestone 4.5a (borders, shading, the repeated header row, column and table widths, row height, distribute), one test per rule of the spec: attributes are merged into the
/// elements that are there, a removal restores "before minus that element", a border shared by two cells is written on both, and the first width write makes the table fixed and recomputes every width in it.
/// </summary>
public sealed class TableFormattingTests
{
    private static readonly XNamespace W = LayoutModel.W;

    private const string Normal = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";

    private static string Tc(string text, string props = "") =>
        $"<w:tc>{(props.Length == 0 ? string.Empty : $"<w:tcPr>{props}</w:tcPr>")}<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static string Tr(string props, params string[] cells) => $"<w:tr>{(props.Length == 0 ? string.Empty : $"<w:trPr>{props}</w:trPr>")}{string.Concat(cells)}</w:tr>";

    private static string Dxa(int w) => $"<w:tcW w:w=\"{w}\" w:type=\"dxa\"/>";

    private static string Pct(int w) => $"<w:tcW w:w=\"{w}\" w:type=\"pct\"/>";

    private static string Grid(params int[] widths) => $"<w:tblGrid>{string.Concat(widths.Select(w => $"<w:gridCol w:w=\"{w}\"/>"))}</w:tblGrid>";

    // A 3 by 3 table of fixed dxa cells.
    private static string Plain3(string tblPr = "<w:tblW w:w=\"6000\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>") =>
        $"<w:tbl><w:tblPr>{tblPr}</w:tblPr>{Grid(2000, 2000, 2000)}" +
        string.Concat(Enumerable.Range(0, 3).Select(r => Tr(string.Empty, Tc($"{r}0", Dxa(2000)), Tc($"{r}1", Dxa(2000)), Tc($"{r}2", Dxa(2000))))) +
        "</w:tbl><w:p><w:r><w:t>after</w:t></w:r></w:p>";

    // An autofit table with percentage cells (the widths Word writes for "AutoFit to window").
    private static string Percentage3() =>
        $"<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/></w:tblPr>{Grid(3000, 3000, 3000)}" +
        string.Concat(Enumerable.Range(0, 2).Select(r => Tr(string.Empty, Tc($"{r}0", Pct(1500)), Tc($"{r}1", Pct(1500)), Tc($"{r}2", Pct(2000))))) +
        "</w:tbl><w:p><w:r><w:t>after</w:t></w:r></w:p>";

    private static WordDocument Document(string body) => LayoutModel.Document(body, LayoutModel.StylesPart(Normal));

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static TableRef Ref(int block = 0) => new(StoryRef.Body, block);

    private static Table TableAt(WordDocument document, int block = 0) => (Table)document.Body.Blocks[block];

    private static string BodyXml(WordDocument document) => ModelSnapshot.Of(document).Elements("part").First(e => (string?)e.Attribute("name") == "document").ToString();

    private static TableCell CellAt(WordDocument document, int row, int column) => TableGridMap.Of(TableAt(document)).At(row, column)!.Cell;

    private static XElement? Side(TableCell cell, string side) => cell.Properties.Get(W + "tcBorders")?.Element(W + side);

    private static string? Attr(XElement? element, string name) => (string?)element?.Attribute(W + name);

    private static void AssertLine(TableCell cell, string side, string val = "single", string sz = "4", string color = "auto")
    {
        var element = Side(cell, side);
        Assert.True(element is not null, $"the cell has no {side} border");
        Assert.Equal(val, Attr(element, "val"));
        Assert.Equal(sz, Attr(element, "sz"));
        Assert.Equal(color, Attr(element, "color"));
    }

    private static void AssertOff(TableCell cell, string side)
    {
        var element = Side(cell, side);
        Assert.True(element is not null, $"the cell has no {side} border element (nil expected)");
        Assert.Equal("nil", Attr(element, "val"));
    }

    private static void AssertNoSide(TableCell cell, string side) => Assert.True(Side(cell, side) is null, $"the cell has a {side} border element");

    private static int Twips(XElement? element) => int.Parse(Attr(element, "w")!, CultureInfo.InvariantCulture);

    // ---- borders ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void BoxBordersWriteTheFourOuterSides()
    {
        var document = Document(Plain3());
        var result = Editor(document).SetBorders(new CellRange(Ref(), 0, 0, 1, 1), BorderSpec.Box());

        Assert.NotNull(result);
        AssertLine(CellAt(document, 0, 0), "top");
        AssertLine(CellAt(document, 0, 0), "left");
        AssertLine(CellAt(document, 0, 1), "top");
        AssertLine(CellAt(document, 0, 1), "right");
        AssertLine(CellAt(document, 1, 0), "left");
        AssertLine(CellAt(document, 1, 0), "bottom");
        AssertLine(CellAt(document, 1, 1), "right");
        AssertLine(CellAt(document, 1, 1), "bottom");
        // The lines between the four cells are left alone by a box.
        AssertNoSide(CellAt(document, 0, 0), "right");
        AssertNoSide(CellAt(document, 0, 0), "bottom");
        AssertNoSide(CellAt(document, 1, 1), "left");
        AssertNoSide(CellAt(document, 1, 1), "top");
    }

    [Fact]
    public void InsideBordersAreAppliedByAllNotByBox()
    {
        var all = Document(Plain3());
        Assert.NotNull(Editor(all).SetBorders(new CellRange(Ref(), 0, 0, 1, 1), BorderSpec.All()));
        AssertLine(CellAt(all, 0, 0), "right");
        AssertLine(CellAt(all, 0, 0), "bottom");
        AssertLine(CellAt(all, 1, 1), "left");
        AssertLine(CellAt(all, 1, 1), "top");
        AssertLine(CellAt(all, 0, 1), "left");
        AssertLine(CellAt(all, 1, 0), "top");

        // Outside is an outline and nothing else: the lines between the cells are turned off.
        var outside = Document(Plain3());
        Assert.NotNull(Editor(outside).SetBorders(new CellRange(Ref(), 0, 0, 1, 1), BorderSpec.Outside()));
        AssertLine(CellAt(outside, 0, 0), "top");
        AssertOff(CellAt(outside, 0, 0), "right");
        AssertOff(CellAt(outside, 1, 1), "top");

        var none = Document(Plain3());
        var editor = Editor(none);
        Assert.NotNull(editor.SetBorders(new CellRange(Ref(), 0, 0, 2, 2), BorderSpec.All()));
        Assert.NotNull(editor.SetBorders(new CellRange(Ref(), 0, 0, 2, 2), BorderSpec.None()));
        foreach (var cell in TableGridMap.Of(TableAt(none)).Cells)
        {
            foreach (var side in new[] { "top", "left", "bottom", "right" })
            {
                AssertOff(cell.Cell, side);
            }
        }
    }

    [Fact]
    public void ASharedBorderIsWrittenOnBothCells()
    {
        var document = Document(Plain3());
        var editor = Editor(document);
        Assert.NotNull(editor.SetBorders(new CellRange(Ref(), 1, 1, 1, 1), BorderSpec.Box(TableBorderStyle.DoubleLine, 12, 0xFFFF0000)));

        // The border of the centre cell is also the border of each neighbour on the other side of it.
        AssertLine(CellAt(document, 1, 1), "left", "double", "12", "FF0000");
        AssertLine(CellAt(document, 1, 0), "right", "double", "12", "FF0000");
        AssertLine(CellAt(document, 1, 1), "right", "double", "12", "FF0000");
        AssertLine(CellAt(document, 1, 2), "left", "double", "12", "FF0000");
        AssertLine(CellAt(document, 1, 1), "top", "double", "12", "FF0000");
        AssertLine(CellAt(document, 0, 1), "bottom", "double", "12", "FF0000");
        AssertLine(CellAt(document, 1, 1), "bottom", "double", "12", "FF0000");
        AssertLine(CellAt(document, 2, 1), "top", "double", "12", "FF0000");

        // Two writes that disagree about one border: the later one wins on both cells.
        Assert.NotNull(editor.SetBorders(new CellRange(Ref(), 1, 0, 1, 0), BorderSpec.Off(BorderSides.Right)));
        AssertOff(CellAt(document, 1, 0), "right");
        AssertOff(CellAt(document, 1, 1), "left");
        AssertLine(CellAt(document, 1, 1), "right", "double", "12", "FF0000");
    }

    [Fact]
    public void ABorderMergesIntoTheElementThatIsThere()
    {
        // A start element (the other spelling of left) and a theme colour: the element keeps its name and loses the theme colour the new colour replaces.
        var cell = Tc("a", $"{Dxa(2000)}<w:tcBorders><w:start w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"auto\" w:themeColor=\"accent1\"/><w:tl2br w:val=\"single\"/></w:tcBorders>");
        var document = Document($"<w:tbl><w:tblPr><w:tblW w:w=\"2000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000)}{Tr(string.Empty, cell)}</w:tbl><w:p/>");

        Assert.NotNull(Editor(document).SetBorders(new CellRange(Ref(), 0, 0, 0, 0), BorderSpec.Sides(BorderSides.Left | BorderSides.Top, TableBorderStyle.Dotted, 6, 0xFF00B050)));

        var borders = CellAt(document, 0, 0).Properties.Get(W + "tcBorders")!;
        Assert.Equal(["top", "start", "tl2br"], borders.Elements().Select(e => e.Name.LocalName));
        var start = borders.Element(W + "start")!;
        Assert.Equal("dotted", Attr(start, "val"));
        Assert.Equal("6", Attr(start, "sz"));
        Assert.Equal("00B050", Attr(start, "color"));
        Assert.Null(start.Attribute(W + "themeColor"));
    }

    [Fact]
    public void ABorderSpecThatMeansNothingIsRefused()
    {
        var document = Document(Plain3());
        var editor = Editor(document);
        var before = BodyXml(document);
        var range = new CellRange(Ref(), 0, 0, 0, 0);

        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetBorders(range, new BorderSpec(BorderSides.None)));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetBorders(range, BorderSpec.Box(widthEighths: 1)));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetBorders(range, BorderSpec.Box(widthEighths: 97)));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetBorders(range, new BorderSpec(BorderSides.Top, Cleared: BorderSides.Top)));
        Assert.Equal(TableRefusal.OutOfRange, editor.RefusalForSetBorders(new CellRange(Ref(), 0, 0, 0, 3), BorderSpec.Box()));
        Assert.Equal(TableRefusal.OutOfRange, editor.RefusalForSetBorders(new CellRange(Ref(), 0, 0, 3, 0), BorderSpec.Box()));
        Assert.Equal(TableRefusal.NoTable, editor.RefusalForSetBorders(new CellRange(Ref(5), 0, 0, 0, 0), BorderSpec.Box()));
        Assert.Null(editor.SetBorders(range, new BorderSpec(BorderSides.None)));
        Assert.Equal(before, BodyXml(document));
    }

    [Fact]
    public void BordersOnAMergedCellAreWrittenOnEveryCellOfTheMerge()
    {
        const string Restart = "<w:vMerge w:val=\"restart\"/>";
        const string Continue = "<w:vMerge/>";
        var document = Document(
            $"<w:tbl><w:tblPr><w:tblW w:w=\"4000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000, 2000)}" +
            Tr(string.Empty, Tc("a", Dxa(2000) + Restart), Tc("b", Dxa(2000))) +
            Tr(string.Empty, Tc(string.Empty, Dxa(2000) + Continue), Tc("c", Dxa(2000))) +
            Tr(string.Empty, Tc("d", Dxa(2000)), Tc("e", Dxa(2000))) +
            "</w:tbl><w:p/>");

        // The range names the lower half of the merge only: it is widened to the whole merged cell, and the bottom edge belongs to its last row.
        Assert.NotNull(Editor(document).SetBorders(new CellRange(Ref(), 1, 0, 1, 0), BorderSpec.Box()));

        AssertLine(CellAt(document, 0, 0), "top");
        AssertNoSide(CellAt(document, 1, 0), "top");
        AssertLine(CellAt(document, 0, 0), "left");
        AssertLine(CellAt(document, 1, 0), "left");
        AssertLine(CellAt(document, 0, 0), "right");
        AssertLine(CellAt(document, 1, 0), "right");
        AssertNoSide(CellAt(document, 0, 0), "bottom");
        AssertLine(CellAt(document, 1, 0), "bottom");
        // The cells that share the border get it too.
        AssertLine(CellAt(document, 0, 1), "left");
        AssertLine(CellAt(document, 1, 1), "left");
        AssertLine(CellAt(document, 2, 0), "top");
    }

    // ---- shading -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ShadingUsesTheFixedColourList()
    {
        // The text colours of the fixed list of the Home tab (the app offers exactly these) and one with an alpha: the editor writes the six hex digits of whatever colour it is given, alpha ignored.
        uint[] list = [0xFF000000, 0xFF7F7F7F, 0xFFFFFFFF, 0xFFC00000, 0xFFFF0000, 0xFFFFC000, 0xFFFFFF00, 0xFF92D050, 0xFF00B050, 0xFF00B0F0, 0xFF0070C0, 0xFF002060, 0xFF7030A0, 0x80C0C0C0];
        foreach (var colour in list)
        {
            var document = Document(Plain3());
            Assert.NotNull(Editor(document).SetShading(new CellRange(Ref(), 0, 0, 0, 1), colour));
            foreach (var column in new[] { 0, 1 })
            {
                var shd = CellAt(document, 0, column).Properties.Get(W + "shd");
                Assert.Equal("clear", Attr(shd, "val"));
                Assert.Equal("auto", Attr(shd, "color"));
                Assert.Equal((colour & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture), Attr(shd, "fill"));
            }

            Assert.Null(CellAt(document, 0, 2).Properties.Get(W + "shd"));
            Assert.Null(CellAt(document, 1, 0).Properties.Get(W + "shd"));
        }
    }

    [Fact]
    public void RemovingShadingRestoresTheTableMinusThatOneElement()
    {
        var shaded = Tc("a", $"{Dxa(2000)}<w:shd w:val=\"pct10\" w:color=\"FF0000\" w:fill=\"auto\"/><w:vAlign w:val=\"center\"/>");
        var withShading = Document($"<w:tbl><w:tblPr><w:tblW w:w=\"2000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000)}{Tr(string.Empty, shaded)}</w:tbl><w:p/>");
        var without = Document($"<w:tbl><w:tblPr><w:tblW w:w=\"2000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000)}{Tr(string.Empty, Tc("a", $"{Dxa(2000)}<w:vAlign w:val=\"center\"/>"))}</w:tbl><w:p/>");

        Assert.NotNull(Editor(withShading).SetShading(new CellRange(Ref(), 0, 0, 0, 0), null));

        Assert.Equal(BodyXml(without), BodyXml(withShading));
    }

    [Fact]
    public void ShadingAMergedCellShadesEveryCellOfTheMerge()
    {
        var document = Document(
            $"<w:tbl><w:tblPr><w:tblW w:w=\"4000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000, 2000)}" +
            Tr(string.Empty, Tc("a", Dxa(2000) + "<w:vMerge w:val=\"restart\"/>"), Tc("b", Dxa(2000))) +
            Tr(string.Empty, Tc(string.Empty, Dxa(2000) + "<w:vMerge/>"), Tc("c", Dxa(2000))) +
            "</w:tbl><w:p/>");

        Assert.NotNull(Editor(document).SetShading(new CellRange(Ref(), 0, 0, 0, 0), 0xFFFFFF00));

        Assert.Equal("FFFF00", Attr(CellAt(document, 0, 0).Properties.Get(W + "shd"), "fill"));
        Assert.Equal("FFFF00", Attr(CellAt(document, 1, 0).Properties.Get(W + "shd"), "fill"));
        Assert.Null(CellAt(document, 0, 1).Properties.Get(W + "shd"));
    }

    // ---- header row, row height ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task HeaderRowRepeatsOnEachPageInTheLayout()
    {
        var rows = string.Concat(Enumerable.Range(0, 140).Select(i => Tr(string.Empty, Tc(i == 0 ? "HEADERCELL" : $"row {i}", Dxa(4000)), Tc($"value {i}", Dxa(4000)))));
        var document = Document($"<w:tbl><w:tblPr><w:tblW w:w=\"8000\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr>{Grid(4000, 4000)}{rows}</w:tbl><w:p><w:r><w:t>after</w:t></w:r></w:p>");
        var editor = Editor(document);

        var before = await SectionOracleTests.LaidAsync(document);
        Assert.True(before.LaidOutPages > 2, "the table must span several pages");
        Assert.Equal(1, Enumerable.Range(0, before.LaidOutPages).Count(p => before.Page(p).Text.Contains("HEADERCELL", StringComparison.Ordinal)));

        Assert.NotNull(editor.SetHeaderRow(Ref(), true));
        Assert.NotNull(((TableRow)TableAt(document).Rows[0]).Properties.Get(W + "tblHeader"));
        Assert.Null(((TableRow)TableAt(document).Rows[1]).Properties.Get(W + "tblHeader"));

        var after = await SectionOracleTests.LaidAsync(document);
        var pagesWithTable = Enumerable.Range(0, after.LaidOutPages).Where(p => after.Page(p).Text.Contains("value ", StringComparison.Ordinal)).ToList();
        Assert.True(pagesWithTable.Count > 2);
        foreach (var page in pagesWithTable)
        {
            Assert.Contains("HEADERCELL", after.Page(page).Text, StringComparison.Ordinal);
        }

        // Off again: the header is on one page only.
        Assert.NotNull(editor.SetHeaderRow(Ref(), false));
        Assert.Null(((TableRow)TableAt(document).Rows[0]).Properties.Get(W + "tblHeader"));
        var off = await SectionOracleTests.LaidAsync(document);
        Assert.Equal(1, Enumerable.Range(0, off.LaidOutPages).Count(p => off.Page(p).Text.Contains("HEADERCELL", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheHeaderRowIsTheLeadingRowsOnly()
    {
        var document = Document(
            $"<w:tbl><w:tblPr><w:tblW w:w=\"2000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000)}" +
            Tr("<w:tblHeader/>", Tc("h1", Dxa(2000))) +
            Tr("<w:tblHeader/>", Tc("h2", Dxa(2000))) +
            Tr(string.Empty, Tc("x", Dxa(2000))) +
            Tr("<w:tblHeader/>", Tc("stray", Dxa(2000))) +
            "</w:tbl><w:p/>");
        var editor = Editor(document);

        // Off removes the header rows that count (the leading ones); a stray mark further down has no effect and stays.
        Assert.NotNull(editor.SetHeaderRow(Ref(), false));
        var rows = TableAt(document).Rows.OfType<TableRow>().ToList();
        Assert.Null(rows[0].Properties.Get(W + "tblHeader"));
        Assert.Null(rows[1].Properties.Get(W + "tblHeader"));
        Assert.NotNull(rows[3].Properties.Get(W + "tblHeader"));

        // On marks the first row only.
        Assert.NotNull(editor.SetHeaderRow(Ref(), true));
        rows = [.. TableAt(document).Rows.OfType<TableRow>()];
        Assert.NotNull(rows[0].Properties.Get(W + "tblHeader"));
        Assert.Null(rows[1].Properties.Get(W + "tblHeader"));
    }

    [Fact]
    public void RowHeightWritesAtLeastOrExactAndZeroRemovesIt()
    {
        var document = Document(Plain3());
        var editor = Editor(document);
        var before = BodyXml(document);

        Assert.NotNull(editor.SetRowHeight(Ref(), 1, 600, exact: false));
        var height = ((TableRow)TableAt(document).Rows[1]).Properties.Get(W + "trHeight");
        Assert.Equal("600", Attr(height, "val"));
        Assert.Equal("atLeast", Attr(height, "hRule"));

        Assert.NotNull(editor.SetRowHeight(Ref(), 1, 700, exact: true));
        height = ((TableRow)TableAt(document).Rows[1]).Properties.Get(W + "trHeight");
        Assert.Equal("700", Attr(height, "val"));
        Assert.Equal("exact", Attr(height, "hRule"));

        Assert.NotNull(editor.SetRowHeight(Ref(), 1, 0, exact: false));
        Assert.Equal(before, BodyXml(document));

        Assert.Equal(TableRefusal.OutOfRange, editor.RefusalForSetRowHeight(Ref(), 3, 600, false));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetRowHeight(Ref(), 0, TableRules.MaxTwips + 1, false));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetRowHeight(Ref(), 0, -5, false));
    }

    // ---- widths ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void DistributeGivesEqualColumnsThatSumToTheTableWidth()
    {
        var document = Document($"<w:tbl><w:tblPr><w:tblW w:w=\"9001\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr>{Grid(1000, 2000, 6001)}" +
            Tr(string.Empty, Tc("a", Dxa(1000)), Tc("b", Dxa(2000)), Tc("c", Dxa(6001))) + "</w:tbl><w:p/>");

        Assert.NotNull(Editor(document).DistributeColumns(Ref()));

        var table = TableAt(document);
        // 9001 is not divisible by 3: the remainder goes to the last column, and the sum stays exactly the table width.
        Assert.Equal([3000, 3000, 3001], table.GridColumns);
        Assert.Equal(9001, Twips(table.Properties.Get(W + "tblW")));
        Assert.Equal([3000, 3000, 3001], TableGridMap.Of(table).Cells.Select(c => Twips(c.Cell.Properties.Get(W + "tcW"))));
    }

    [Fact]
    public void SetTableWidthScalesTheColumnsAndTheSumIsExact()
    {
        var document = Document(Plain3());
        var editor = Editor(document);

        Assert.NotNull(editor.SetTableWidth(Ref(), 7001));
        var table = TableAt(document);
        Assert.Equal(7001, table.GridColumns.Sum());
        Assert.Equal(7001, Twips(table.Properties.Get(W + "tblW")));
        Assert.All(table.GridColumns, w => Assert.InRange(w, 2333, 2334));
        TableGridChecks.AssertGridEqualsCells(table, "table");

        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetTableWidth(Ref(), (3 * TableRules.MinColumnTwips) - 1));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetTableWidth(Ref(), TableRules.MaxTwips + 1));
    }

    [Fact]
    public void ColumnWidthsAreOnePerColumnAndMergedCellsTakeTheSumOfTheirColumns()
    {
        var document = Document(
            $"<w:tbl><w:tblPr><w:tblW w:w=\"6000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000, 2000, 2000)}" +
            Tr(string.Empty, Tc("a", Dxa(4000) + "<w:gridSpan w:val=\"2\"/>"), Tc("b", Dxa(2000))) +
            Tr(string.Empty, Tc("c", Dxa(2000)), Tc("d", Dxa(2000)), Tc("e", Dxa(2000))) +
            "</w:tbl><w:p/>");
        var editor = Editor(document);

        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetColumnWidths(Ref(), [1000, 1000]));
        Assert.Equal(TableRefusal.InvalidValue, editor.RefusalForSetColumnWidths(Ref(), [1000, 1000, TableRules.MinColumnTwips - 1]));

        Assert.NotNull(editor.SetColumnWidths(Ref(), [1000, 3000, 2500]));

        var table = TableAt(document);
        Assert.Equal([1000, 3000, 2500], table.GridColumns);
        Assert.Equal(6500, Twips(table.Properties.Get(W + "tblW")));
        var map = TableGridMap.Of(table);
        Assert.Equal(4000, Twips(map.At(0, 0)!.Cell.Properties.Get(W + "tcW")));
        Assert.Equal(2500, Twips(map.At(0, 2)!.Cell.Properties.Get(W + "tcW")));
        Assert.Equal([1000, 3000, 2500], map.CellsOfRow(1).Select(c => Twips(c.Cell.Properties.Get(W + "tcW"))));
    }

    [Fact]
    public void AFirstWidthEditConvertsAnAutofitTableToFixed()
    {
        // Autofit: no fixed layout, an auto table width, auto cell widths.
        var document = Document(
            $"<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr>{Grid(3000, 3000)}" +
            Tr(string.Empty, Tc("a", "<w:tcW w:w=\"0\" w:type=\"auto\"/>"), Tc("b")) +
            Tr(string.Empty, Tc("c", Dxa(1234)), Tc("d", "<w:tcW w:w=\"0\" w:type=\"auto\"/>")) +
            "</w:tbl><w:p/>");

        Assert.NotNull(Editor(document).SetColumnWidths(Ref(), [2500, 3500]));

        var table = TableAt(document);
        Assert.Equal("fixed", Attr(table.Properties.Get(W + "tblLayout"), "type"));
        Assert.Equal("dxa", Attr(table.Properties.Get(W + "tblW"), "type"));
        Assert.Equal(6000, Twips(table.Properties.Get(W + "tblW")));
        Assert.Equal([2500, 3500], table.GridColumns);
        var cells = TableGridMap.Of(table).Cells;
        Assert.All(cells, c => Assert.Equal("dxa", Attr(c.Cell.Properties.Get(W + "tcW"), "type")));
        Assert.Equal([2500, 3500, 2500, 3500], cells.Select(c => Twips(c.Cell.Properties.Get(W + "tcW"))));
    }

    [Fact]
    public void APercentageTableThatWentThroughAddColumnAndMergeIsConsistentAfterTheFirstWidthWrite()
    {
        foreach (var write in new Func<TextEditor, EditResult?>[]
        {
            e => e.SetColumnWidths(Ref(), [1500, 2500, 3000, 2000]),
            e => e.SetTableWidth(Ref(), 8000),
            e => e.DistributeColumns(Ref()),
        })
        {
            var document = Document(Percentage3());
            var editor = Editor(document);
            // Task 6's operations leave the percentage widths of the cells as they were (a cell without a dxa width gets no new width).
            Assert.NotNull(editor.AddColumn(Ref(), 1, after: true));
            Assert.NotNull(editor.MergeCells(new CellRange(Ref(), 0, 0, 0, 1)));

            Assert.NotNull(write(editor));

            var table = TableAt(document);
            TableGridChecks.AssertGridEqualsCells(table, "table");
            Assert.Equal("fixed", Attr(table.Properties.Get(W + "tblLayout"), "type"));
            Assert.Equal("dxa", Attr(table.Properties.Get(W + "tblW"), "type"));
            Assert.Equal(table.GridColumns.Sum(), Twips(table.Properties.Get(W + "tblW")));
            foreach (var cell in TableGridMap.Of(table).Cells)
            {
                Assert.Equal("dxa", Attr(cell.Cell.Properties.Get(W + "tcW"), "type"));
                var expected = Enumerable.Range(cell.Column, cell.ColumnSpan).Sum(c => table.GridColumns[c]);
                Assert.Equal(expected, Twips(cell.Cell.Properties.Get(W + "tcW")));
            }
        }
    }

    [Fact]
    public async Task ReadingNeverConverts()
    {
        var document = Document(Percentage3());
        var editor = Editor(document);
        var before = BodyXml(document);

        // Asking a refusal, finding the table, laying the document out and reading its grid change nothing.
        Assert.Null(editor.RefusalForSetColumnWidths(Ref(), [1000, 2000, 3000]));
        Assert.Null(editor.RefusalForSetTableWidth(Ref(), 5000));
        Assert.Null(editor.RefusalForDistributeColumns(Ref()));
        Assert.Null(editor.RefusalForSetBorders(new CellRange(Ref(), 0, 0, 0, 0), BorderSpec.Box()));
        Assert.NotNull(editor.TableOf(new TextPosition(StoryRef.Body, (Paragraph)CellAt(document, 0, 0).Blocks[0], 0)));
        _ = await SectionOracleTests.LaidAsync(document);

        Assert.Equal(before, BodyXml(document));
        Assert.Null(TableAt(document).Properties.Get(W + "tblLayout"));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void UndoRestoresThePercentageWidths()
    {
        var document = Document(Percentage3());
        var editor = Editor(document);
        var before = BodyXml(document);

        Assert.NotNull(editor.SetColumnWidths(Ref(), [2000, 3000, 4000]));
        var after = BodyXml(document);
        Assert.NotEqual(before, after);

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, BodyXml(document));
        Assert.Equal("pct", Attr(TableAt(document).Properties.Get(W + "tblW"), "type"));

        Assert.NotNull(editor.History.Redo());
        Assert.Equal(after, BodyXml(document));
    }

    [Fact]
    public void EveryOperationIsOneHistoryStepAndOneBlockChange()
    {
        var document = Document(Plain3());
        var editor = Editor(document);
        var range = new CellRange(Ref(), 0, 0, 1, 1);
        var before = BodyXml(document);

        var steps = new List<Func<EditResult?>>
        {
            () => editor.SetBorders(range, BorderSpec.All()),
            () => editor.SetShading(range, 0xFFFF0000),
            () => editor.SetHeaderRow(Ref(), true),
            () => editor.SetColumnWidths(Ref(), [1000, 2000, 3000]),
            () => editor.SetTableWidth(Ref(), 7000),
            () => editor.SetRowHeight(Ref(), 0, 500, exact: true),
            () => editor.DistributeColumns(Ref()),
        };

        // The paragraphs of the table are new objects after every step, so the caret is checked at once.
        foreach (var step in steps)
        {
            var result = step();
            Assert.NotNull(result);
            var change = Assert.Single(result.Changes);
            Assert.Equal(1, change.Removed);
            Assert.Equal(1, change.Added);
            Assert.NotNull(TableHolding(document, result.Caret.Paragraph));
        }

        for (var i = 0; i < steps.Count; i++)
        {
            Assert.NotNull(editor.History.Undo());
        }
        Assert.Equal(before, BodyXml(document));
        Assert.False(editor.History.CanUndo);
    }

    // The table whose cell holds the paragraph (the caret of a result lies in it).
    private static Table? TableHolding(WordDocument document, Paragraph paragraph) =>
        document.Body.Blocks.OfType<Table>().FirstOrDefault(t => TableGridMap.Of(t).Cells.Any(c => c.Cell.Blocks.Contains(paragraph)));

    [Fact]
    public void IrregularAndInconsistentTablesRefuseWithUnsupportedStructure()
    {
        // gridBefore: the table has a row that starts further right.
        var irregular = Document(
            $"<w:tbl><w:tblPr><w:tblW w:w=\"4000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000, 2000)}" +
            Tr("<w:gridBefore w:val=\"1\"/>", Tc("a", Dxa(2000))) + Tr(string.Empty, Tc("b"), Tc("c")) + "</w:tbl><w:p/>");
        // A row shorter than the grid.
        var inconsistent = Document(
            $"<w:tbl><w:tblPr><w:tblW w:w=\"4000\" w:type=\"dxa\"/></w:tblPr>{Grid(2000, 2000)}" +
            Tr(string.Empty, Tc("a")) + "</w:tbl><w:p/>");

        foreach (var document in new[] { irregular, inconsistent })
        {
            var editor = Editor(document);
            var before = BodyXml(document);
            var range = new CellRange(Ref(), 0, 0, 0, 0);
            Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForSetBorders(range, BorderSpec.Box()));
            Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForSetShading(range, 0xFF000000));
            Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForSetHeaderRow(Ref(), true));
            Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForSetColumnWidths(Ref(), [1000, 1000]));
            Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForSetTableWidth(Ref(), 4000));
            Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForSetRowHeight(Ref(), 0, 500, false));
            Assert.Equal(TableRefusal.UnsupportedStructure, editor.RefusalForDistributeColumns(Ref()));
            Assert.Null(editor.SetBorders(range, BorderSpec.Box()));
            Assert.Equal(before, BodyXml(document));
        }
    }

    [Fact]
    public void ThePrefixDocumentRefusesEveryOperation()
    {
        var document = Document(Plain3());
        var editor = Editor(document);
        var range = new CellRange(Ref(), 0, 0, 0, 0);

        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForSetBorders(range, BorderSpec.Box(), prefixDocument: true));
        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForSetShading(range, null, prefixDocument: true));
        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForSetHeaderRow(Ref(), true, prefixDocument: true));
        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForSetColumnWidths(Ref(), [2000, 2000, 2000], prefixDocument: true));
        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForSetTableWidth(Ref(), 6000, prefixDocument: true));
        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForSetRowHeight(Ref(), 0, 500, false, prefixDocument: true));
        Assert.Equal(TableRefusal.PrefixDocument, editor.RefusalForDistributeColumns(Ref(), prefixDocument: true));
    }
}
