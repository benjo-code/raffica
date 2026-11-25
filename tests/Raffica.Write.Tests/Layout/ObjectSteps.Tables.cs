using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The table steps of the object oracle (milestone 4.5a): a table inserted at a random place, rows and columns added and deleted, cells merged and split, a row moved, a table deleted, over the tables of the
/// document (the corpus has merged cells in both directions, autofit and percentage widths). After every step the model's grid (<see cref="TableGridMap"/>) is compared with the grids the layout publishes.
/// </summary>
internal sealed partial class ObjectSteps
{
    /// <summary>How many table pieces were compared with the model grid, summed over the steps.</summary>
    public long GridsCompared { get; private set; }

    private List<TableRef> Tables()
    {
        var found = new List<TableRef>();
        var blocks = _model.Body.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            var reference = new TableRef(StoryRef.Body, i);
            if (reference.Find(blocks) is { } table && TableGridMap.Of(table).IsRegular)
            {
                found.Add(reference);
            }
        }

        return found;
    }

    private (TableRef Reference, TableGridMap Map)? PickTable(Random random)
    {
        var tables = Tables();
        if (tables.Count == 0)
        {
            return null;
        }

        var reference = tables[random.Next(tables.Count)];
        return (reference, TableGridMap.Of(reference.Find(_model.Body.Blocks)!));
    }

    private async Task<bool> InsertTableAsync(Random random, string label)
    {
        var candidates = Paragraphs().Where(p => Editor.RefusalForInsertTable(new TextPosition(StoryRef.Body, p, 0), 2, 2) is null).ToList();
        if (candidates.Count == 0)
        {
            return Refuse();
        }

        var paragraph = candidates[random.Next(candidates.Count)];
        var offset = random.Next(TextLength(paragraph) + 1);
        return await CheckAsync(Editor.InsertTable(new TextPosition(StoryRef.Body, paragraph, offset), random.Next(1, 5), random.Next(1, 4)), label, required: false);
    }

    private async Task<bool> AddRowAsync(Random random, string label)
    {
        if (PickTable(random) is not var (table, map))
        {
            return Refuse();
        }

        return await CheckAsync(Editor.AddRow(table, map.RowIndexes[random.Next(map.RowCount)], random.Next(2) == 0), label, required: false);
    }

    private async Task<bool> AddColumnAsync(Random random, string label)
    {
        if (PickTable(random) is not var (table, map))
        {
            return Refuse();
        }

        return await CheckAsync(Editor.AddColumn(table, random.Next(map.Columns), random.Next(2) == 0), label, required: false);
    }

    private async Task<bool> DeleteRowsAsync(Random random, string label)
    {
        if (PickTable(random) is not var (table, map))
        {
            return Refuse();
        }

        var first = random.Next(map.RowCount);
        var last = Math.Min(map.RowCount - 1, first + random.Next(2));
        return await CheckAsync(Editor.DeleteRows(new CellRange(table, map.RowIndexes[first], 0, map.RowIndexes[last], 0)), label, required: false);
    }

    private async Task<bool> DeleteColumnsAsync(Random random, string label)
    {
        if (PickTable(random) is not var (table, map))
        {
            return Refuse();
        }

        var first = random.Next(map.Columns);
        var last = Math.Min(map.Columns - 1, first + random.Next(2));
        return await CheckAsync(Editor.DeleteColumns(new CellRange(table, 0, first, 0, last)), label, required: false);
    }

    private async Task<bool> MergeAsync(Random random, string label)
    {
        if (PickTable(random) is not var (table, map))
        {
            return Refuse();
        }

        var first = random.Next(map.RowCount);
        var last = Math.Min(map.RowCount - 1, first + random.Next(2));
        var firstColumn = random.Next(map.Columns);
        var lastColumn = Math.Min(map.Columns - 1, firstColumn + random.Next(3));
        return await CheckAsync(Editor.MergeCells(new CellRange(table, map.RowIndexes[first], firstColumn, map.RowIndexes[last], lastColumn)), label, required: false);
    }

    private async Task<bool> SplitAsync(Random random, string label)
    {
        if (PickTable(random) is not var (table, map))
        {
            return Refuse();
        }

        // A merged cell when the table has one (a split of an unmerged cell is a refusal).
        var merged = map.Cells.Where(c => !c.IsContinuation && (c.ColumnSpan > 1 || c.RowSpan > 1)).ToList();
        var pool = merged.Count > 0 ? merged : [.. map.Cells];
        var cell = pool[random.Next(pool.Count)];
        return await CheckAsync(Editor.SplitCell(table, cell.RowIndex, cell.Column), label, required: false);
    }

    private async Task<bool> MoveRowAsync(Random random, string label)
    {
        if (PickTable(random) is not var (table, map))
        {
            return Refuse();
        }

        return await CheckAsync(Editor.MoveRow(table, map.RowIndexes[random.Next(map.RowCount)], random.Next(2) == 0 ? -1 : 1), label, required: false);
    }

    private async Task<bool> DeleteTableAsync(Random random, string label)
    {
        if (PickTable(random) is not var (table, _))
        {
            return Refuse();
        }

        return await CheckAsync(Editor.DeleteTable(table), label, required: false);
    }

    /// <summary>
    /// Fails when a table grid the layout published for a page disagrees with the grid of the model: the number of column borders is one more than the columns, every row the grid names is a row of the table, every cell
    /// rectangle starts where a cell of the model starts and spans as many columns, and every cell of the model that starts a merge (or none) is drawn on some page. Tables the map does not follow (<see cref="TableGridMap.IsRegular"/>)
    /// and a content control that holds several tables are skipped. Returns how many table pieces were compared.
    /// </summary>
    private long AssertTableGrids(string label)
    {
        long compared = 0;
        var drawn = new Dictionary<int, HashSet<(int Row, int Column)>>();
        var checkedTables = new Dictionary<int, TableGridMap>();
        for (var p = 0; p < _layout.LaidOutPages; p++)
        {
            foreach (var grid in _layout.Page(p).Objects.Tables)
            {
                if (new TableRef(StoryRef.Body, grid.BlockIndex).Find(_model.Body.Blocks) is not { } table)
                {
                    continue;
                }

                var map = TableGridMap.Of(table);
                if (!map.IsRegular)
                {
                    continue;
                }

                Assert.True(grid.ColumnBorders.Count == map.Columns + 1, $"{label}: the layout draws {grid.ColumnBorders.Count - 1} columns of table {grid.BlockIndex}, the model grid has {map.Columns}");
                foreach (var row in grid.RowIndexes)
                {
                    Assert.True(map.OrdinalOf(row) >= 0, $"{label}: the layout draws row {row} of table {grid.BlockIndex}, which is no row of the model grid ({string.Join(",", map.RowIndexes)})");
                }

                if (!drawn.TryGetValue(grid.BlockIndex, out var seen))
                {
                    seen = [];
                    drawn[grid.BlockIndex] = seen;
                    checkedTables[grid.BlockIndex] = map;
                }

                foreach (var rect in grid.Cells)
                {
                    var cell = map.At(rect.Row, rect.Column);
                    Assert.True(cell is not null, $"{label}: the layout draws a cell at row {rect.Row}, column {rect.Column} of table {grid.BlockIndex}, the model grid has none");
                    Assert.True(cell.Column == rect.Column && cell.ColumnSpan == rect.ColumnSpan, $"{label}: the layout draws the cell at row {rect.Row}, column {rect.Column} of table {grid.BlockIndex} with span {rect.ColumnSpan}, the model starts it at column {cell.Column} with span {cell.ColumnSpan}");
                    // The rectangle's RowSpan counts the rows of the layout that lie inside the box by height, so a row of zero height inside a merged cell counts too: it is not compared with the merge.
                    Assert.True(rect.RowSpan >= 1, $"{label}: the layout draws the cell at row {rect.Row}, column {rect.Column} of table {grid.BlockIndex} with no rows");
                    seen.Add((rect.Row, rect.Column));
                }

                compared++;
            }
        }

        foreach (var (blockIndex, seen) in drawn)
        {
            var map = checkedTables[blockIndex];
            foreach (var cell in map.Cells.Where(c => !c.IsContinuation))
            {
                Assert.True(seen.Contains((cell.RowIndex, cell.Column)), $"{label}: the cell at row {cell.RowIndex}, column {cell.Column} of table {blockIndex} is in the model grid and drawn on no page");
            }
        }

        return compared;
    }
}
