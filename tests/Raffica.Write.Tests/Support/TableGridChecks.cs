using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Support;

/// <summary>
/// The definition of "the grid equals the cells" of milestone 4.5a, read straight from the XML of the table (not through <see cref="TableGridMap"/>, so the map is checked by it and not the other way round): the table has
/// one <c>w:gridCol</c> per grid column, the cells of every row span exactly all the columns, every vertical-merge continuation has a cell right above it that starts in the same column with the same span and itself
/// takes part in a merge, every row has a cell and every cell holds blocks that end in a paragraph.
/// </summary>
internal static class TableGridChecks
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static int? Int(PropertyBag bag, string name) =>
        int.TryParse((string?)bag.Get(W + name)?.Attribute(W + "val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Fails with <paramref name="label"/> when the grid and the cells of the table disagree. A zero-width <c>w:gridCol</c> is allowed up to <paramref name="zeroWidthColumnsBefore"/> (a command must not make one).</summary>
    public static void AssertGridEqualsCells(Table table, string label, int zeroWidthColumnsBefore = 0)
    {
        var columns = table.GridColumns.Count;
        Assert.True(columns >= 1, label + ": the table has no grid column");
        Assert.True(table.GridColumns.Count(c => c <= 0) <= zeroWidthColumnsBefore, label + ": a command made a grid column of no width");
        List<(int Start, int Span, string? Merge)>? above = null;
        foreach (var row in table.Rows.OfType<TableRow>())
        {
            var cells = new List<(int Start, int Span, string? Merge)>();
            var at = 0;
            foreach (var cell in row.Items.OfType<TableCell>())
            {
                var span = Math.Max(1, Int(cell.Properties, "gridSpan") ?? 1);
                var merge = cell.Properties.Get(W + "vMerge") is { } element ? (string?)element.Attribute(W + "val") ?? "continue" : null;
                cells.Add((at, span, merge));
                at += span;
                Assert.True(cell.Blocks.Count > 0 && cell.Blocks[^1] is Paragraph, label + ": a cell has no closing paragraph");
            }

            Assert.True(cells.Count > 0, label + ": a row has no cell");
            Assert.True(at == columns, $"{label}: the cells of a row span {at} columns, the grid has {columns}");
            foreach (var cell in cells.Where(c => c.Merge == "continue"))
            {
                Assert.True(above is not null && above.Any(a => a.Start == cell.Start && a.Span == cell.Span && a.Merge is not null), $"{label}: a vertical-merge continuation at column {cell.Start} (span {cell.Span}) has no head above it");
            }

            above = cells;
        }
    }

    private static string Signature(PropertyBag properties, string side)
    {
        var element = properties.Get(W + "tcBorders")?.Elements().FirstOrDefault(e => e.Name.Namespace == W && (e.Name.LocalName == side || (side == "left" && e.Name.LocalName == "start") || (side == "right" && e.Name.LocalName == "end")));
        return element is null ? string.Empty : $"{(string?)element.Attribute(W + "val")}/{(string?)element.Attribute(W + "sz")}/{(string?)element.Attribute(W + "color")}";
    }

    /// <summary>
    /// The shared borders of the table that are the same on both cells (a pair whose edge is shared with a merged cell on one side only partly is left out: the merged cell has one border for the whole edge): for every pair of neighbouring grid cells (side by side, or one above the other) the key (row ordinal of the first, its column, 'h' or 'v') is in the set
    /// when the right (or bottom) border of the first and the left (or top) border of the second are written alike (both absent counts).
    /// </summary>
    public static HashSet<(int Row, int Column, char Direction)> SymmetricBorders(Table table)
    {
        var map = TableGridMap.Of(table);
        var result = new HashSet<(int, int, char)>();
        foreach (var cell in map.Cells)
        {
            if (map.Slot(cell.RowOrdinal, cell.LastColumn + 1) is { } right && cell.Head.RowSpan == 1 && right.Head.RowSpan == 1 && Signature(cell.Cell.Properties, "right") == Signature(right.Cell.Properties, "left"))
            {
                result.Add((cell.RowOrdinal, cell.Column, 'h'));
            }

            var last = cell.RowOrdinal == cell.Head.RowOrdinal + cell.Head.RowSpan - 1;
            if (last && map.Slot(cell.RowOrdinal + 1, cell.Column) is { IsContinuation: false } below && below.ColumnSpan == cell.ColumnSpan && Signature(cell.Cell.Properties, "bottom") == Signature(below.Cell.Properties, "top"))
            {
                result.Add((cell.RowOrdinal, cell.Column, 'v'));
            }
        }

        return result;
    }

    /// <summary>
    /// Fails when the widths of a table disagree after a width write: the layout is fixed, <c>w:tblW</c> is in twips and equals the grid, and every cell (continuations included) has a <c>w:tcW</c> in twips that equals the
    /// grid columns it spans.
    /// </summary>
    public static void AssertWidthsAgree(Table table, string label)
    {
        var problem = WidthProblem(table);
        Assert.True(problem is null, $"{label}: {problem}");
    }

    /// <summary>What disagrees in the widths of the table (see <see cref="AssertWidthsAgree"/>), or null when the table is fixed and its table width, grid and cells agree.</summary>
    public static string? WidthProblem(Table table)
    {
        var fixedLayout = (string?)table.Properties.Get(W + "tblLayout")?.Attribute(W + "type");
        if (fixedLayout != "fixed")
        {
            return "the table is not fixed";
        }

        var tblW = table.Properties.Get(W + "tblW");
        if ((string?)tblW?.Attribute(W + "type") != "dxa" || !int.TryParse((string?)tblW.Attribute(W + "w"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) || total != table.GridColumns.Sum())
        {
            return "tblW is not the grid in twips";
        }

        foreach (var cell in TableGridMap.Of(table).Cells)
        {
            var width = cell.Cell.Properties.Get(W + "tcW");
            var expected = Enumerable.Range(cell.Column, cell.ColumnSpan).Sum(c => table.GridColumns[c]);
            if ((string?)width?.Attribute(W + "type") != "dxa" || !int.TryParse((string?)width.Attribute(W + "w"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value != expected)
            {
                return $"tcW of the cell at row {cell.RowOrdinal}, column {cell.Column} is not {expected}";
            }
        }

        return null;
    }
}
