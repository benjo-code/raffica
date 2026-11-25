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
}
