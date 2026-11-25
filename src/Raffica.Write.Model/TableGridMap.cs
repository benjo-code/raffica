using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>
/// One cell of a <see cref="TableGridMap"/>: the model cell with the place it takes in the grid. <see cref="RowIndex"/> is the index of its row in <c>Table.Rows</c> (the same numbers as the
/// layout's <c>CellRect.Row</c>), <see cref="Column"/> the grid column it starts in. A cell that continues a vertical merge is a <see cref="IsContinuation"/> of its <see cref="Head"/>; the head
/// covers <see cref="RowSpan"/> rows (1 without a merge).
/// </summary>
public sealed class GridCell
{
    internal GridCell(TableRow row, TableCell cell, int rowIndex, int rowOrdinal, int itemIndex, int column, int columnSpan)
    {
        Row = row;
        Cell = cell;
        RowIndex = rowIndex;
        RowOrdinal = rowOrdinal;
        ItemIndex = itemIndex;
        Column = column;
        ColumnSpan = columnSpan;
        Head = this;
    }

    /// <summary>The row element that holds the cell.</summary>
    public TableRow Row { get; }

    /// <summary>The model cell (the live object of the table the map was made from).</summary>
    public TableCell Cell { get; }

    /// <summary>The index of the row in <c>Table.Rows</c>.</summary>
    public int RowIndex { get; }

    /// <summary>The number of the row among the rows of the grid (0 for the first row), whatever sits between the rows in <c>Table.Rows</c>.</summary>
    public int RowOrdinal { get; }

    /// <summary>The index of the cell in <c>TableRow.Items</c>.</summary>
    public int ItemIndex { get; }

    /// <summary>The grid column the cell starts in.</summary>
    public int Column { get; }

    /// <summary>How many grid columns the cell covers (<c>w:gridSpan</c>, at least 1).</summary>
    public int ColumnSpan { get; }

    /// <summary>For a head: how many rows its merge covers (1 without a merge); for a continuation 1.</summary>
    public int RowSpan { get; internal set; } = 1;

    /// <summary>The first cell of the vertical merge this cell belongs to (the cell itself when it has none or starts it).</summary>
    public GridCell Head { get; internal set; }

    /// <summary>True for a cell that continues the one above: its content is not shown.</summary>
    public bool IsContinuation => !ReferenceEquals(Head, this);

    /// <summary>The last grid column the cell covers.</summary>
    public int LastColumn => Column + ColumnSpan - 1;
}

/// <summary>
/// The grid of a table as a read-only view over the model (no storage of its own, computed from the table when asked): which cell covers which grid column in which row, the merged regions and the
/// number of columns. It reads a table the way the layout does (<c>w:gridSpan</c> from the start of the row, a <c>w:vMerge</c> continuation joins the cell above only when that one starts in the same column
/// with the same span), so the operations of the editor and the object map of the layout agree on what a row, a column and a cell are. A table that holds something this view does not follow (a row or cell
/// that is an unknown wrapper, a row with a tracked deletion, <c>w:gridBefore</c> or <c>w:gridAfter</c>) is <see cref="IsRegular"/> false: the map is still made, but an editor must not restructure it.
/// </summary>
public sealed class TableGridMap
{
    private const int MaximumColumns = 63;

    // Elements the model keeps between rows or cells that take no room (the layout's list).
    private static readonly HashSet<string> s_marks = new(StringComparer.Ordinal)
    {
        "tblPrEx", "bookmarkStart", "bookmarkEnd", "commentRangeStart", "commentRangeEnd", "permStart", "permEnd", "proofErr",
        "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd",
    };

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly GridCell?[,] _slots;
    private readonly List<int> _rowIndexes;

    private TableGridMap(GridCell?[,] slots, List<int> rowIndexes, List<GridCell> cells, int columns, bool regular, bool consistent)
    {
        IsConsistent = consistent;
        _slots = slots;
        _rowIndexes = rowIndexes;
        Cells = cells;
        Columns = columns;
        IsRegular = regular;
    }

    /// <summary>The number of grid columns: the grid of the table, widened to the widest row, at least 1 and at most 63 (the layout's own cap).</summary>
    public int Columns { get; }

    /// <summary>The number of rows of the grid.</summary>
    public int RowCount => _rowIndexes.Count;

    /// <summary>The index in <c>Table.Rows</c> of each row of the grid.</summary>
    public IReadOnlyList<int> RowIndexes => _rowIndexes;

    /// <summary>Every cell (heads and continuations) in reading order.</summary>
    public IReadOnlyList<GridCell> Cells { get; }

    /// <summary>False when the table holds something the map does not follow (see the class remarks); a restructuring edit must refuse such a table.</summary>
    public bool IsRegular { get; }

    /// <summary>
    /// True when the grid and the cells agree: the table has as many <c>w:gridCol</c> as grid columns, every row of the grid has cells that span exactly all the columns (no short row), and every <c>w:vMerge</c> continuation joins a head above it
    /// that starts in the same column with the same span (no orphan continuation). A restructuring edit refuses a table that is regular but not consistent (repairing it would be a guess); the map is still made.
    /// </summary>
    public bool IsConsistent { get; }

    /// <summary>Reads the grid of a table.</summary>
    public static TableGridMap Of(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var regular = true;
        var rows = new List<(TableRow Row, int Index)>();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            switch (table.Rows[i])
            {
                case TableRow row when row.Properties.Get(W + "del") is null:
                    rows.Add((row, i));
                    break;
                case TableRow:
                    regular = false;
                    break;
                case UnknownRowItem unknown when Takes(unknown):
                    regular = false;
                    break;
                default:
                    break;
            }
        }

        var consistent = table.GridColumns.Count <= MaximumColumns;
        var columns = Math.Min(table.GridColumns.Count, MaximumColumns);
        var placed = new List<List<(TableCell Cell, int ItemIndex, int Start, int Span)>>(rows.Count);
        foreach (var (row, _) in rows)
        {
            var at = Math.Min(Math.Max(IntValue(row.Properties.Get(W + "gridBefore")) ?? 0, 0), MaximumColumns - 1);
            if (at > 0 || IntValue(row.Properties.Get(W + "gridAfter")) is > 0)
            {
                regular = false;
            }

            var list = new List<(TableCell, int, int, int)>(row.Items.Count);
            for (var c = 0; c < row.Items.Count; c++)
            {
                if (row.Items[c] is TableCell cell)
                {
                    var span = Math.Clamp(IntValue(cell.Properties.Get(W + "gridSpan")) ?? 1, 1, MaximumColumns);
                    var start = Math.Min(at, MaximumColumns - 1);
                    span = Math.Min(span, MaximumColumns - start);
                    list.Add((cell, c, start, span));
                    at = start + span;
                    columns = Math.Max(columns, at);
                }
                else if (row.Items[c] is UnknownRowItem unknown && Takes(unknown))
                {
                    regular = false;
                }
            }

            placed.Add(list);
        }

        columns = Math.Max(columns, 1);
        var slots = new GridCell?[rows.Count, columns];
        var all = new List<GridCell>();
        var open = new GridCell?[columns];
        for (var r = 0; r < rows.Count; r++)
        {
            var seen = new bool[columns];
            foreach (var (cell, itemIndex, start, span) in placed[r])
            {
                var made = new GridCell(rows[r].Row, cell, rows[r].Index, r, itemIndex, start, span);
                all.Add(made);
                for (var k = start; k < start + span && k < columns; k++)
                {
                    slots[r, k] = made;
                }

                var merge = cell.Properties.Get(W + "vMerge");
                var continues = merge is not null && Value(merge) is null or "continue";
                if (continues && open[start] is { } head && head.RowOrdinal + head.RowSpan == r && head.ColumnSpan == span)
                {
                    made.Head = head;
                    head.RowSpan++;
                    seen[start] = true;
                    continue;
                }

                consistent &= !continues;
                open[start] = merge is not null ? made : null;
                seen[start] = true;
            }

            for (var k = 0; k < open.Length; k++)
            {
                if (!seen[k])
                {
                    open[k] = null;
                }
            }
        }

        consistent &= table.GridColumns.Count == columns;
        for (var r = 0; r < rows.Count && consistent; r++)
        {
            consistent = placed[r].Count > 0 && placed[r].Sum(c => c.Span) == columns;
        }

        return new TableGridMap(slots, [.. rows.Select(r => r.Index)], all, columns, regular, consistent);
    }

    /// <summary>The cell (head or continuation) that covers a grid column in a row of <c>Table.Rows</c>; null for a row that is not a row of the grid, a column outside it or a row too short to reach it.</summary>
    public GridCell? At(int modelRow, int column)
    {
        var ordinal = _rowIndexes.IndexOf(modelRow);
        return ordinal < 0 ? null : Slot(ordinal, column);
    }

    /// <summary>The cell (head or continuation) at a row ordinal and a grid column; null outside the grid.</summary>
    public GridCell? Slot(int ordinal, int column) =>
        ordinal >= 0 && ordinal < _rowIndexes.Count && column >= 0 && column < Columns ? _slots[ordinal, column] : null;

    /// <summary>The number of the row among the rows of the grid for an index in <c>Table.Rows</c>, or -1.</summary>
    public int OrdinalOf(int modelRow) => _rowIndexes.IndexOf(modelRow);

    /// <summary>The cells of a row of the grid, left to right (the same cell once).</summary>
    public IEnumerable<GridCell> CellsOfRow(int ordinal) => Cells.Where(c => c.RowOrdinal == ordinal);

    /// <summary>True when a vertical merge crosses the line between a row and the next (some column holds one head in both).</summary>
    public bool MergeCrosses(int ordinal)
    {
        if (ordinal < 0 || ordinal + 1 >= _rowIndexes.Count)
        {
            return false;
        }

        for (var c = 0; c < Columns; c++)
        {
            var above = _slots[ordinal, c];
            var below = _slots[ordinal + 1, c];
            if (above is not null && below is not null && ReferenceEquals(above.Head, below.Head))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Takes(UnknownRowItem item) => !s_marks.Contains(item.Content.Element.Name.LocalName);

    private static string? Value(XElement element) => (string?)element.Attribute(W + "val");

    private static int? IntValue(XElement? element) =>
        element is not null && int.TryParse(Value(element), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
