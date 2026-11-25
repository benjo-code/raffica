namespace Raffica.Write.Editing;

/// <summary>
/// A rectangle of cells of one table: <paramref name="FirstRow"/> and <paramref name="LastRow"/> are indexes into the table's <c>Rows</c> (the model rows, the same numbers as the layout's
/// <c>CellRect.Row</c>; both ends included, first not after last) and <paramref name="FirstColumn"/> and <paramref name="LastColumn"/> grid columns (both included). A cell
/// that is merged counts by the cell it starts in; widening a range to whole merged cells is the operation's business, not the range's.
/// </summary>
public sealed record CellRange(TableRef Table, int FirstRow, int FirstColumn, int LastRow, int LastColumn)
{
    /// <summary>The range of the rectangle spanned by two corner cells, in any order.</summary>
    public static CellRange Between(TableRef table, int row, int column, int otherRow, int otherColumn) =>
        new(table, Math.Min(row, otherRow), Math.Min(column, otherColumn), Math.Max(row, otherRow), Math.Max(column, otherColumn));

    /// <summary>How many rows the range covers (model rows, so rows between that are not rows of the table count too).</summary>
    public int RowSpan => LastRow - FirstRow + 1;

    /// <summary>How many grid columns the range covers.</summary>
    public int ColumnSpan => LastColumn - FirstColumn + 1;

    /// <summary>True when the cell at a row and a column is inside the range.</summary>
    public bool Contains(int row, int column) => row >= FirstRow && row <= LastRow && column >= FirstColumn && column <= LastColumn;

    /// <summary>True when any of the cells <paramref name="rowSpan"/> rows and <paramref name="columnSpan"/> columns from (row, column) is inside the range.</summary>
    public bool Overlaps(int row, int column, int rowSpan, int columnSpan) =>
        row <= LastRow && row + Math.Max(1, rowSpan) - 1 >= FirstRow && column <= LastColumn && column + Math.Max(1, columnSpan) - 1 >= FirstColumn;
}
