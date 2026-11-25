using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Rules for ranges of cells that belong to the selection of cells with the pointer (milestone 4.5a).</summary>
public static class CellRanges
{
    /// <summary>
    /// Widens <paramref name="range"/> until it is a union of whole merged cells: a cell that spans columns or rows and touches the range is taken with all of its columns and rows, and what that touches is
    /// taken too, until nothing grows. The rows of the range are model rows (as the range itself) and are returned as such. A table that is not regular (the grid map does not follow it) or a range outside
    /// the table is returned as it is.
    /// </summary>
    public static CellRange Widen(Table table, CellRange range)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(range);
        var map = TableGridMap.Of(table);
        if (!map.IsRegular || range.FirstColumn < 0 || range.LastColumn >= map.Columns)
        {
            return range;
        }

        var (top, bottom) = (-1, -1);
        for (var ordinal = 0; ordinal < map.RowCount; ordinal++)
        {
            var row = map.RowIndexes[ordinal];
            if (row >= range.FirstRow && row <= range.LastRow)
            {
                top = top < 0 ? ordinal : top;
                bottom = ordinal;
            }
        }

        if (top < 0)
        {
            return range;
        }

        var (left, right) = (range.FirstColumn, range.LastColumn);
        bool grew;
        do
        {
            grew = false;
            foreach (var cell in map.Cells)
            {
                if (cell.IsContinuation)
                {
                    continue;
                }

                var (cellTop, cellBottom) = (cell.RowOrdinal, Math.Min(cell.RowOrdinal + cell.RowSpan - 1, map.RowCount - 1));
                if (cellTop > bottom || cellBottom < top || cell.Column > right || cell.LastColumn < left)
                {
                    continue;
                }

                if (cellTop < top || cellBottom > bottom || cell.Column < left || cell.LastColumn > right)
                {
                    (top, bottom, left, right) = (Math.Min(top, cellTop), Math.Max(bottom, cellBottom), Math.Min(left, cell.Column), Math.Max(right, cell.LastColumn));
                    grew = true;
                }
            }
        }
        while (grew);

        return range with { FirstRow = map.RowIndexes[top], LastRow = map.RowIndexes[bottom], FirstColumn = left, LastColumn = right };
    }
}
