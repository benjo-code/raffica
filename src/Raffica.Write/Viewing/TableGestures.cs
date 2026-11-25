using Raffica.Write.Editing;
using Raffica.Write.Layout;

namespace Raffica.Write.Viewing;

/// <summary>What a pointer gesture on a table does (milestone 4.5a); the grid is the page piece the gesture began on.</summary>
internal abstract record TableDragKind(TableRef Table, TableGrid Grid, int Page) : ObjectDragKind;

/// <summary>An inner column border is dragged: it moves between its two neighbouring columns and the table keeps its width.</summary>
internal sealed record ColumnBorderDrag(TableRef Table, TableGrid Grid, int Page, int Border) : TableDragKind(Table, Grid, Page);

/// <summary>The right outer edge is dragged: the last column and the table get wider or narrower.</summary>
internal sealed record TableEdgeDrag(TableRef Table, TableGrid Grid, int Page) : TableDragKind(Table, Grid, Page);

/// <summary>A row is dragged by its handle in the left margin (<paramref name="Row"/> is the model row).</summary>
internal sealed record RowDrag(TableRef Table, TableGrid Grid, int Page, int Row) : TableDragKind(Table, Grid, Page);

/// <summary>The pointer was pressed in a cell and dragged into another: the cells between are selected (the cell it began in is <paramref name="Row"/> and <paramref name="Column"/>).</summary>
internal sealed record CellRangeDrag(TableRef Table, TableGrid Grid, int Page, int Row, int Column) : TableDragKind(Table, Grid, Page);

/// <summary>
/// The arithmetic of the table gestures, pure and in the units of the layout: the widths are twips, the distances points. A border or the edge never makes a column narrower than
/// <see cref="MinColumnTwips"/> (a column that is narrower than that already is not made narrower still), and the right edge never goes past the edge of the page or
/// <see cref="TableRules.MaxTwips"/>.
/// </summary>
internal static class TableGestures
{
    /// <summary>The narrowest a gesture makes a column: 0.5 cm, 284 twips.</summary>
    public const int MinColumnTwips = 284;

    /// <summary>The widths of the columns as the layout draws them now, in twips (what a first width write freezes).</summary>
    public static int[] DrawnWidths(TableGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var widths = new int[Math.Max(0, grid.ColumnBorders.Count - 1)];
        for (var i = 0; i < widths.Length; i++)
        {
            widths[i] = (int)Math.Round((grid.ColumnBorders[i + 1] - grid.ColumnBorders[i]) * 20.0);
        }

        return widths;
    }

    /// <summary>The distance of the pointer in points, rounded to twips.</summary>
    public static int Twips(double dips, double scale) => (int)Math.Round(dips / scale * 20.0);

    /// <summary>
    /// Moves the inner border <paramref name="border"/> (an index of the grid's column borders, 1 to columns minus 1) by <paramref name="twips"/>: the column left of it grows by that much and the column right of it
    /// shrinks by that much. Returns the widths and the distance that was applied after the minimum was kept; null for an index that is no inner border.
    /// </summary>
    public static (int[] Widths, int Applied)? MoveBorder(IReadOnlyList<int> drawn, int border, int twips)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        if (border < 1 || border >= drawn.Count)
        {
            return null;
        }

        var left = drawn[border - 1];
        var right = drawn[border];
        var applied = Math.Clamp(twips, Math.Min(MinColumnTwips, left) - left, right - Math.Min(MinColumnTwips, right));
        var widths = drawn.ToArray();
        widths[border - 1] = left + applied;
        widths[border] = right - applied;
        return (widths, applied);
    }

    /// <summary>
    /// Moves the right outer edge by <paramref name="twips"/>: only the last column changes and the table with it. The table keeps to <paramref name="maxTotalTwips"/> (the room to the page edge) and to
    /// <see cref="TableRules.MaxTwips"/>. Returns the widths and the distance applied; null for a table with no column.
    /// </summary>
    public static (int[] Widths, int Applied)? MoveEdge(IReadOnlyList<int> drawn, int twips, int maxTotalTwips)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        if (drawn.Count == 0)
        {
            return null;
        }

        var last = drawn[^1];
        var total = drawn.Sum();
        var room = Math.Min(maxTotalTwips, TableRules.MaxTwips) - total;
        var applied = Math.Clamp(twips, Math.Min(MinColumnTwips, last) - last, Math.Max(room, Math.Min(MinColumnTwips, last) - last));
        var widths = drawn.ToArray();
        widths[^1] = last + applied;
        return (widths, applied);
    }

    /// <summary>The room a table has to grow to the right, in twips: from its left edge to the right edge of the page (the layout's map does not say where the text area ends).</summary>
    public static int MaxTotalTwips(TableGrid grid, float pageWidth) => (int)Math.Floor((pageWidth - grid.X) * 20.0);

    /// <summary>The row of the piece (its number among the rows drawn) that the vertical position <paramref name="y"/> falls in; the first or last row above or below the piece.</summary>
    public static int RowOrdinalAt(TableGrid grid, float y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var rows = Math.Min(grid.RowIndexes.Count, grid.RowEdges.Count - 1);
        for (var i = 0; i < rows; i++)
        {
            if (y < grid.RowEdges[i + 1])
            {
                return i;
            }
        }

        return Math.Max(0, rows - 1);
    }

    /// <summary>
    /// Where the drop line of a row drag is, as a y in points: the top of the target row when the row moves up, its bottom when it moves down; null when the row would stay (the pointer is over its own row).
    /// </summary>
    public static float? DropLineY(TableGrid grid, int sourceRow, float y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var target = RowOrdinalAt(grid, y);
        if (target >= grid.RowIndexes.Count || grid.RowIndexes[target] == sourceRow)
        {
            return null;
        }

        return grid.RowIndexes[target] < sourceRow ? grid.RowEdges[target] : grid.RowEdges[target + 1];
    }

    /// <summary>The model row the row drag drops onto: the row of the piece under <paramref name="y"/>; null when the piece has none.</summary>
    public static int? TargetRow(TableGrid grid, float y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var target = RowOrdinalAt(grid, y);
        return target < grid.RowIndexes.Count ? grid.RowIndexes[target] : null;
    }

    /// <summary>The cell of the piece at a point in page space, or null between the cells.</summary>
    public static CellRect? CellAt(TableGrid grid, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        foreach (var cell in grid.Cells)
        {
            if (x >= cell.X && x <= cell.X + cell.Width && y >= cell.Y && y <= cell.Y + cell.Height)
            {
                return cell;
            }
        }

        return null;
    }
}
