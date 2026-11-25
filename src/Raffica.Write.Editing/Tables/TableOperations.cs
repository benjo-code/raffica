using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>What a structure operation did to the clone of a table it was given: a refusal, or the paragraph the caret goes to, or the wish to remove the whole table.</summary>
internal readonly record struct TableMutation(TableRefusal? Refusal, Paragraph? Caret = null, bool RemoveTable = false)
{
    public static TableMutation Refuse(TableRefusal refusal) => new(refusal);

    public static TableMutation Done(Paragraph? caret) => new(null, caret);

    public static TableMutation WholeTable() => new(null, null, true);
}

/// <summary>
/// The pure part of the table structure operations of milestone 4.5a: each method changes a table it is given (the editor passes a clone, so the document's table is untouched until the operation is known to
/// work) and keeps the grid, the spans and the merges in agreement. Nothing here knows the history, the layout or the session.
/// </summary>
internal static class TableOperations
{
    /// <summary>The id of the table style a new table gets.</summary>
    public const string GridStyleId = "TableGrid";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // The four border sides and the two inside lines of a table (the order of the schema).
    private static readonly string[] s_borderSides = ["top", "left", "bottom", "right", "insideH", "insideV"];

    // ---- new table, style ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds the table style <c>TableGrid</c> (single 0.5 pt borders, no paragraph spacing, as Word's own) to the style sheet when the document has none of that id, based on <c>TableNormal</c> when the document has it.
    /// Returns the change a step records (old and new are equal clones: the style is made in place), or null when the style exists or the document has no style sheet.
    /// </summary>
    public static StyleChange? EnsureGridStyle(WordDocument document)
    {
        var sheet = document.Styles;
        if (sheet is null || sheet.Find(GridStyleId) is not null)
        {
            return null;
        }

        var style = new Style { Id = GridStyleId, Type = "table", Name = "Table Grid", BasedOn = sheet.Find("TableNormal") is null ? null : "TableNormal" };
        style.ParagraphProperties.Set(W + "spacing", new XElement(W + "spacing", new XAttribute(W + "after", "0"), new XAttribute(W + "line", "240"), new XAttribute(W + "lineRule", "auto")));
        style.TableProperties.Set(W + "tblBorders", Borders());
        NoteOperations.Priority(style);
        sheet.Styles.Add(style);
        return new StyleChange(style.Clone(), style.Clone());
    }

    private static XElement Borders()
    {
        var borders = new XElement(W + "tblBorders");
        foreach (var side in s_borderSides)
        {
            borders.Add(new XElement(W + side, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "space", "0"), new XAttribute(W + "color", "auto")));
        }

        return borders;
    }

    /// <summary>
    /// A new table of <paramref name="columns"/> by <paramref name="rows"/> cells, <paramref name="width"/> twips wide in total (the columns equal, the remainder spread over the first ones so the grid adds up exactly), fixed layout,
    /// one empty paragraph in each cell. It has the style <c>TableGrid</c> when <paramref name="styled"/>, otherwise the same borders directly.
    /// </summary>
    public static Table NewTable(int columns, int rows, int width, bool styled)
    {
        var table = new Table();
        var widths = EqualWidths(columns, width);
        if (styled)
        {
            table.Properties.StyleId = GridStyleId;
        }
        else
        {
            table.Properties.Set(W + "tblBorders", Borders());
        }

        table.Properties.Set(W + "tblW", new XElement(W + "tblW", new XAttribute(W + "w", Number(widths.Sum())), new XAttribute(W + "type", "dxa")));
        table.Properties.Set(W + "tblLayout", new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")));
        table.Properties.Set(
            W + "tblLook",
            new XElement(
                W + "tblLook",
                new XAttribute(W + "val", "04A0"),
                new XAttribute(W + "firstRow", "1"),
                new XAttribute(W + "lastRow", "0"),
                new XAttribute(W + "firstColumn", "1"),
                new XAttribute(W + "lastColumn", "0"),
                new XAttribute(W + "noHBand", "0"),
                new XAttribute(W + "noVBand", "1")));
        foreach (var column in widths)
        {
            table.GridColumns.Add(column);
        }

        for (var r = 0; r < rows; r++)
        {
            var row = new TableRow();
            foreach (var column in widths)
            {
                var cell = new TableCell();
                SetWidth(cell, column);
                cell.Blocks.Add(new Paragraph());
                row.Items.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    /// <summary>The widths of <paramref name="columns"/> equal columns that add up to <paramref name="width"/>: the remainder of the division goes to the first columns, one twip each.</summary>
    public static int[] EqualWidths(int columns, int width)
    {
        var each = width / columns;
        var rest = width % columns;
        var widths = new int[columns];
        for (var i = 0; i < columns; i++)
        {
            widths[i] = each + (i < rest ? 1 : 0);
        }

        return widths;
    }

    // ---- small helpers over the model ------------------------------------------------------------------------------------------------

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Sets the preferred width of a cell to <paramref name="twips"/> (<c>w:tcW</c> of type <c>dxa</c>).</summary>
    public static void SetWidth(TableCell cell, int twips) =>
        cell.Properties.Set(W + "tcW", new XElement(W + "tcW", new XAttribute(W + "w", Number(Math.Max(0, twips))), new XAttribute(W + "type", "dxa")));

    private static bool HasDxaWidth(TableCell cell) =>
        cell.Properties.Get(W + "tcW") is { } width && (string?)width.Attribute(W + "type") is "dxa" or null && width.Attribute(W + "w") is not null;

    private static void SetSpan(TableCell cell, int span)
    {
        if (span <= 1)
        {
            cell.Properties.Remove(W + "gridSpan");
        }
        else
        {
            cell.Properties.Set(W + "gridSpan", new XElement(W + "gridSpan", new XAttribute(W + "val", Number(span))));
        }
    }

    /// <summary>Sets <c>w:vMerge</c> to <c>restart</c> or to a bare continuation, or removes it (null).</summary>
    private static void SetMerge(TableCell cell, string? kind)
    {
        switch (kind)
        {
            case null:
                cell.Properties.Remove(W + "vMerge");
                break;
            case "restart":
                cell.Properties.Set(W + "vMerge", new XElement(W + "vMerge", new XAttribute(W + "val", "restart")));
                break;
            default:
                cell.Properties.Set(W + "vMerge", new XElement(W + "vMerge"));
                break;
        }
    }

    // The sum of grid columns from <start> for <span> columns (a column beyond the grid counts 0).
    private static int GridWidth(Table table, int start, int span)
    {
        var sum = 0;
        for (var i = start; i < start + span && i < table.GridColumns.Count; i++)
        {
            sum += table.GridColumns[i];
        }

        return sum;
    }

    /// <summary>Copies the children of a property bag into another (the target is expected to be empty), skipping the named children, and keeps the "was present" mark.</summary>
    private static void CopyProperties(PropertyBag from, PropertyBag to, params XName[] skip)
    {
        foreach (var child in from.Children)
        {
            if (!skip.Contains(child.Name))
            {
                to.AddLoaded(child);
            }
        }

        to.WasPresent = from.WasPresent;
    }

    /// <summary>A new, empty paragraph that takes the properties of <paramref name="like"/> (its style, alignment and the rest; never a list), so a new cell reads like the one it copies.</summary>
    private static Paragraph EmptyLike(TableCell like)
    {
        var paragraph = new Paragraph();
        if (like.Blocks.OfType<Paragraph>().FirstOrDefault() is { } first)
        {
            CopyProperties(first.Properties, paragraph.Properties, W + "numPr");
        }

        return paragraph;
    }

    /// <summary>True for a paragraph that holds nothing a reader sees or that anchors something: no inlines, or only runs with no content or with empty text.</summary>
    public static bool IsEmpty(Block block) =>
        block is Paragraph { SectionBreak: null } paragraph && paragraph.Inlines.All(inline => inline is Run run && run.Content.All(content => content is TextContent { Text.Length: 0 }));

    // A cell must end in a paragraph (Word repairs a file whose cell ends in a table) and hold at least one block.
    private static void EndInParagraph(TableCell cell, TableCell like)
    {
        if (cell.Blocks.Count == 0 || cell.Blocks[^1] is not Paragraph)
        {
            cell.Blocks.Add(EmptyLike(like));
        }
    }

    // The position ints of the table, rows and cells count the children before the property element; a child added or removed before it moves it.
    private static void InsertRow(Table table, int index, TableRow row)
    {
        table.Rows.Insert(index, row);
        if (index < table.PropertiesPosition)
        {
            table.PropertiesPosition++;
        }

        if (index < table.GridPosition)
        {
            table.GridPosition++;
        }
    }

    private static void RemoveRow(Table table, int index)
    {
        table.Rows.RemoveAt(index);
        if (index < table.PropertiesPosition)
        {
            table.PropertiesPosition--;
        }

        if (index < table.GridPosition)
        {
            table.GridPosition--;
        }
    }

    private static void InsertCell(TableRow row, int index, TableCell cell)
    {
        row.Items.Insert(index, cell);
        if (index < row.PropertiesPosition)
        {
            row.PropertiesPosition++;
        }
    }

    private static void RemoveCell(TableRow row, int index)
    {
        row.Items.RemoveAt(index);
        if (index < row.PropertiesPosition)
        {
            row.PropertiesPosition--;
        }
    }

    // Replaces the blocks of a cell; the properties of a cell sit before its blocks in every file Word writes, and a position beyond the new count is the end.
    private static void SetBlocks(TableCell cell, IEnumerable<Block> blocks)
    {
        cell.Blocks.Clear();
        foreach (var block in blocks)
        {
            cell.Blocks.Add(block);
        }

        cell.PropertiesPosition = Math.Min(cell.PropertiesPosition, cell.Blocks.Count);
    }

    /// <summary>The first paragraph inside a cell (looking through content controls and nested tables), or null.</summary>
    public static Paragraph? FirstParagraphOf(TableCell cell) => TextEditor.FirstParagraph(cell.Blocks);

    // ---- rows --------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Inserts a row before or after the row at <paramref name="index"/> of <c>Table.Rows</c>. The new row has the cells of that row (same spans, same widths and properties, an empty paragraph with the old one's paragraph
    /// properties but no list); a cell where the new row falls inside a vertical merge continues the merge (the region grows by this row), any other is a plain cell. The row keeps the row properties of the old one
    /// except the repeated-header mark and tracked changes.
    /// </summary>
    public static TableMutation AddRow(Table table, TableGridMap map, int index, bool after)
    {
        var ordinal = map.OrdinalOf(index);
        if (ordinal < 0)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        if (map.RowCount + 1 > TableRules.MaxRows)
        {
            return TableMutation.Refuse(TableRefusal.TooManyRows);
        }

        var neighbour = after ? ordinal + 1 : ordinal - 1;
        var source = map.CellsOfRow(ordinal).ToList();
        var row = new TableRow();
        CopyProperties(((TableRow)table.Rows[index]).Properties, row.Properties, W + "tblHeader", W + "ins", W + "del", W + "trPrChange");
        foreach (var reference in source)
        {
            var cell = new TableCell();
            CopyProperties(reference.Cell.Properties, cell.Properties, W + "vMerge");
            var other = map.Slot(neighbour, reference.Column);
            if (other is not null && ReferenceEquals(other.Head, reference.Head))
            {
                SetMerge(cell, "continue");
            }

            cell.Blocks.Add(EmptyLike(reference.Cell));
            row.Items.Add(cell);
        }

        InsertRow(table, after ? index + 1 : index, row);
        return TableMutation.Done(row.Items.OfType<TableCell>().Select(FirstParagraphOf).FirstOrDefault(p => p is not null));
    }

    /// <summary>
    /// Deletes the rows <paramref name="firstRow"/> to <paramref name="lastRow"/> (indexes of <c>Table.Rows</c>; anything between rows, such as a bookmark mark, stays). A cell that continues a vertical merge goes with its row and the
    /// region above shrinks; a deleted row that holds the start of a merge passes the start to the cell of the next row that continued it. A merge left with one row is no merge. Deleting every row asks for the table to go.
    /// </summary>
    public static TableMutation DeleteRows(Table table, TableGridMap map, int firstRow, int lastRow)
    {
        if (firstRow > lastRow)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        var ordinals = map.RowIndexes.Select((index, ordinal) => (index, ordinal)).Where(r => r.index >= firstRow && r.index <= lastRow).Select(r => r.ordinal).ToList();
        if (ordinals.Count == 0)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        if (ordinals.Count >= map.RowCount)
        {
            return TableMutation.WholeTable();
        }

        var deleted = new HashSet<int>(ordinals);
        // A merge whose start goes passes it to the first continuation that stays.
        foreach (var head in map.Cells.Where(c => !c.IsContinuation && c.RowSpan > 1 && deleted.Contains(c.RowOrdinal)))
        {
            var heir = map.Cells.Where(c => ReferenceEquals(c.Head, head) && c.IsContinuation && !deleted.Contains(c.RowOrdinal)).MinBy(c => c.RowOrdinal);
            if (heir is not null)
            {
                SetMerge(heir.Cell, "restart");
            }
        }

        var above = ordinals[0] - 1 >= 0 ? map.RowIndexes[ordinals[0] - 1] : -1;
        foreach (var ordinal in ordinals.OrderByDescending(o => o))
        {
            RemoveRow(table, map.RowIndexes[ordinal]);
        }

        // A region that lost every continuation is no merge any more: only the rows next to the cut can be affected.
        var after = TableGridMap.Of(table);
        var touched = new List<int>();
        if (above >= 0)
        {
            touched.Add(after.OrdinalOf(above));
        }

        if (ordinals[0] < after.RowCount)
        {
            touched.Add(ordinals[0]);
        }

        foreach (var cell in touched.Where(o => o >= 0).SelectMany(after.CellsOfRow).Where(c => !c.IsContinuation && c.RowSpan == 1 && c.Cell.Properties.Get(W + "vMerge") is not null))
        {
            SetMerge(cell.Cell, null);
        }

        var landing = Math.Min(ordinals[0], after.RowCount - 1);
        return TableMutation.Done(after.CellsOfRow(landing).Select(c => FirstParagraphOf(c.Cell)).FirstOrDefault(p => p is not null));
    }

    // ---- columns -----------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Inserts a column left or right of the grid column <paramref name="index"/>. A cell that spans across the new boundary grows by one column (the region extends); any other row gets a new plain cell that copies the
    /// properties of the cell next to it. The new grid column takes half the width of the column it is next to (the other half stays), so the table keeps its width.
    /// </summary>
    public static TableMutation AddColumn(Table table, TableGridMap map, int index, bool after)
    {
        if (index < 0 || index >= map.Columns)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        if (map.Columns + 1 > TableRules.MaxColumns)
        {
            return TableMutation.Refuse(TableRefusal.TooManyColumns);
        }

        var boundary = after ? index + 1 : index;
        // The editor refuses a table whose grid and cells disagree, so the grid has a column for every grid column here. A column too narrow to share would make a column of no width.
        var reference = table.GridColumns[index];
        if (reference < 2)
        {
            return TableMutation.Refuse(TableRefusal.WouldBreakTheTable);
        }

        var added = reference / 2;
        table.GridColumns.Insert(boundary, added);
        table.GridColumns[after ? index : index + 1] = reference - added;

        Paragraph? caret = null;
        for (var ordinal = 0; ordinal < map.RowCount; ordinal++)
        {
            var next = map.Slot(ordinal, index);
            if (next is null)
            {
                continue;
            }

            var row = next.Row;
            if (next.Column < boundary && next.LastColumn >= boundary)
            {
                SetSpan(next.Cell, next.ColumnSpan + 1);
                continue;
            }

            var cell = new TableCell();
            CopyProperties(next.Cell.Properties, cell.Properties, W + "gridSpan", W + "vMerge", W + "tcW");
            SetWidth(cell, added);
            cell.Blocks.Add(EmptyLike(next.Cell));
            InsertCell(row, after ? next.ItemIndex + 1 : next.ItemIndex, cell);
            caret ??= FirstParagraphOf(cell);

            // The column next to the new one gave half its width away: a width in twips follows the grid.
            if (HasDxaWidth(next.Cell))
            {
                var start = next.Column + (after ? 0 : 1);
                SetWidth(next.Cell, GridWidth(table, start, next.ColumnSpan));
            }
        }

        return TableMutation.Done(caret);
    }

    /// <summary>
    /// Deletes the grid columns <paramref name="firstColumn"/> to <paramref name="lastColumn"/>: a cell that spans only deleted columns goes with its content, a cell that spans some of them shrinks by that many. Deleting every
    /// column asks for the table to go; a row left without a cell refuses the change.
    /// </summary>
    public static TableMutation DeleteColumns(Table table, TableGridMap map, int firstColumn, int lastColumn)
    {
        if (firstColumn < 0 || lastColumn >= map.Columns || firstColumn > lastColumn)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        if (firstColumn == 0 && lastColumn == map.Columns - 1)
        {
            return TableMutation.WholeTable();
        }

        var oldSum = table.GridColumns.Sum();
        foreach (var group in map.Cells.GroupBy(c => c.Row))
        {
            var kept = 0;
            foreach (var cell in group)
            {
                var overlap = Math.Min(cell.LastColumn, lastColumn) - Math.Max(cell.Column, firstColumn) + 1;
                if (overlap < cell.ColumnSpan)
                {
                    kept++;
                }
            }

            if (kept == 0)
            {
                return TableMutation.Refuse(TableRefusal.WouldBreakTheTable);
            }
        }

        var removed = Math.Min(lastColumn, table.GridColumns.Count - 1) - firstColumn + 1;
        for (var i = 0; i < removed; i++)
        {
            table.GridColumns.RemoveAt(firstColumn);
        }

        foreach (var cell in map.Cells.OrderByDescending(c => c.RowOrdinal).ThenByDescending(c => c.ItemIndex))
        {
            var overlap = Math.Min(cell.LastColumn, lastColumn) - Math.Max(cell.Column, firstColumn) + 1;
            if (overlap <= 0)
            {
                continue;
            }

            if (overlap >= cell.ColumnSpan)
            {
                RemoveCell(cell.Row, cell.ItemIndex);
                continue;
            }

            SetSpan(cell.Cell, cell.ColumnSpan - overlap);
            if (HasDxaWidth(cell.Cell))
            {
                SetWidth(cell.Cell, GridWidth(table, Math.Min(cell.Column, firstColumn), cell.ColumnSpan - overlap));
            }
        }

        // A table width in twips that was the sum of the grid follows it.
        if (table.Properties.Get(W + "tblW") is { } width && (string?)width.Attribute(W + "type") == "dxa" && (string?)width.Attribute(W + "w") == Number(oldSum))
        {
            table.Properties.Set(W + "tblW", new XElement(W + "tblW", new XAttribute(W + "w", Number(table.GridColumns.Sum())), new XAttribute(W + "type", "dxa")));
        }

        var after = TableGridMap.Of(table);
        var landing = Math.Min(firstColumn, after.Columns - 1);
        return TableMutation.Done(after.CellsOfRow(0).Where(c => landing >= c.Column && landing <= c.LastColumn).Select(c => FirstParagraphOf(c.Cell)).FirstOrDefault(p => p is not null));
    }

    // ---- merge and split -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Merges a range of cells into one. A range that cuts through a merged cell is widened to whole merged cells first (so a selection never leaves half of a merge outside). The content of the cells is
    /// concatenated in reading order into the first cell, empty paragraphs skipped (at least one paragraph stays); the first cell spans the columns of the range (<c>w:gridSpan</c>) and, for more than one row, starts
    /// a vertical merge (<c>restart</c>) that one continuation cell per further row carries. A range of one cell has nothing to merge.
    /// </summary>
    public static TableMutation Merge(Table table, TableGridMap map, int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        var first = map.OrdinalOf(firstRow);
        var last = map.OrdinalOf(lastRow);
        if (first < 0 || last < first || firstColumn < 0 || lastColumn >= map.Columns || firstColumn > lastColumn)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        // Widen to whole merged cells until nothing sticks out.
        int r1 = first, r2 = last, c1 = firstColumn, c2 = lastColumn;
        bool grew;
        do
        {
            grew = false;
            for (var r = r1; r <= r2; r++)
            {
                for (var c = c1; c <= c2; c++)
                {
                    if (map.Slot(r, c) is not { } cell)
                    {
                        return TableMutation.Refuse(TableRefusal.WouldBreakTheTable);
                    }

                    var top = cell.Head.RowOrdinal;
                    var bottom = cell.Head.RowOrdinal + cell.Head.RowSpan - 1;
                    var left = cell.Column;
                    var right = cell.LastColumn;
                    if (top < r1 || bottom > r2 || left < c1 || right > c2)
                    {
                        r1 = Math.Min(r1, top);
                        r2 = Math.Max(r2, bottom);
                        c1 = Math.Min(c1, left);
                        c2 = Math.Max(c2, right);
                        grew = true;
                    }
                }
            }
        }
        while (grew);

        var covered = new List<GridCell>();
        for (var r = r1; r <= r2; r++)
        {
            for (var c = c1; c <= c2; c++)
            {
                if (map.Slot(r, c) is not { } cell)
                {
                    return TableMutation.Refuse(TableRefusal.WouldBreakTheTable);
                }

                if (!covered.Contains(cell))
                {
                    covered.Add(cell);
                }
            }
        }

        if (covered.Count(c => !c.IsContinuation) == 1)
        {
            return TableMutation.Refuse(TableRefusal.NothingToMerge);
        }

        covered.Sort((a, b) => a.RowOrdinal != b.RowOrdinal ? a.RowOrdinal.CompareTo(b.RowOrdinal) : a.Column.CompareTo(b.Column));
        var target = covered[0];

        var content = new List<Block>();
        foreach (var cell in covered)
        {
            content.AddRange(cell.Cell.Blocks.Where(b => !IsEmpty(b)));
        }

        if (content.Count == 0)
        {
            content.Add(target.Cell.Blocks.FirstOrDefault() ?? EmptyLike(target.Cell));
        }

        var span = c2 - c1 + 1;
        var width = GridWidth(table, c1, span);
        // The first cell of every row of the range after the first stays as the continuation; the others of the range go.
        var removals = new List<GridCell>();
        for (var r = r1; r <= r2; r++)
        {
            var inRow = covered.Where(c => c.RowOrdinal == r).ToList();
            if (r == r1)
            {
                removals.AddRange(inRow.Skip(1));
                continue;
            }

            var keeper = inRow[0];
            SetSpan(keeper.Cell, span);
            SetMerge(keeper.Cell, "continue");
            SetBlocks(keeper.Cell, [EmptyLike(keeper.Cell)]);
            if (HasDxaWidth(keeper.Cell))
            {
                SetWidth(keeper.Cell, width);
            }

            removals.AddRange(inRow.Skip(1));
        }

        foreach (var cell in removals.OrderByDescending(c => c.ItemIndex))
        {
            RemoveCell(cell.Row, cell.ItemIndex);
        }

        SetSpan(target.Cell, span);
        SetMerge(target.Cell, r2 > r1 ? "restart" : null);
        if (HasDxaWidth(target.Cell))
        {
            SetWidth(target.Cell, width);
        }

        SetBlocks(target.Cell, content);
        EndInParagraph(target.Cell, target.Cell);
        return TableMutation.Done(FirstParagraphOf(target.Cell));
    }

    /// <summary>
    /// Splits the merged cell that covers a row and a column back into the cells it was made of: every row of a vertical merge gets its own cell again and every cell that spans columns becomes one cell per column.
    /// The content stays in the first cell; the new cells are empty and copy its properties. A cell that is not merged is refused (<see cref="TableRefusal.NotMerged"/>).
    /// </summary>
    public static TableMutation Split(Table table, TableGridMap map, int row, int column)
    {
        var at = map.At(row, column);
        if (at is null)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        var head = at.Head;
        if (head.ColumnSpan == 1 && head.RowSpan == 1)
        {
            return TableMutation.Refuse(TableRefusal.NotMerged);
        }

        var members = map.Cells.Where(c => ReferenceEquals(c.Head, head)).OrderBy(c => c.RowOrdinal).ToList();
        foreach (var member in members.OrderByDescending(m => m.RowOrdinal))
        {
            SetMerge(member.Cell, null);
            if (member.ColumnSpan <= 1)
            {
                continue;
            }

            SetSpan(member.Cell, 1);
            if (HasDxaWidth(member.Cell))
            {
                SetWidth(member.Cell, GridWidth(table, member.Column, 1));
            }

            for (var k = member.ColumnSpan - 1; k >= 1; k--)
            {
                var cell = new TableCell();
                CopyProperties(member.Cell.Properties, cell.Properties, W + "gridSpan", W + "vMerge", W + "tcW");
                SetWidth(cell, GridWidth(table, member.Column + k, 1));
                cell.Blocks.Add(EmptyLike(member.Cell));
                InsertCell(member.Row, member.ItemIndex + 1, cell);
            }
        }

        return TableMutation.Done(FirstParagraphOf(head.Cell));
    }

    // ---- move a row --------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves the row at <paramref name="row"/> (an index of <c>Table.Rows</c>) <paramref name="delta"/> rows up (negative) or down: the row keeps its properties, so a repeated-header mark travels with it. Refused when the
    /// row is part of a vertical merge, when the new place is inside one (the move would cut it), when the move would cross the border between the repeated header rows and the others, or when the row cannot move that far.
    /// </summary>
    public static TableMutation MoveRow(Table table, TableGridMap map, int row, int delta)
    {
        var from = map.OrdinalOf(row);
        if (from < 0)
        {
            return TableMutation.Refuse(TableRefusal.OutOfRange);
        }

        var to = from + delta;
        if (delta == 0 || to < 0 || to >= map.RowCount)
        {
            return TableMutation.Refuse(TableRefusal.CannotMove);
        }

        if (map.CellsOfRow(from).Any(c => c.Head.RowSpan > 1 || c.IsContinuation))
        {
            return TableMutation.Refuse(TableRefusal.WouldCutAMerge);
        }

        // The line the row lands on: between the rows that are neighbours there once the row has left its place.
        var line = delta > 0 ? to : to - 1;
        if (map.MergeCrosses(line))
        {
            return TableMutation.Refuse(TableRefusal.WouldCutAMerge);
        }

        var headers = 0;
        while (headers < map.RowCount && IsHeader(table, map.RowIndexes[headers]))
        {
            headers++;
        }

        if (from < headers != to < headers)
        {
            return TableMutation.Refuse(TableRefusal.HeaderRowBoundary);
        }

        var moved = (TableRow)table.Rows[row];
        var target = map.RowIndexes[to];
        RemoveRow(table, row);
        InsertRow(table, target, moved);
        return TableMutation.Done(moved.Items.OfType<TableCell>().Select(FirstParagraphOf).FirstOrDefault(p => p is not null));
    }

    private static bool IsHeader(Table table, int index) =>
        table.Rows[index] is TableRow row && row.Properties.GetFlag(W + "tblHeader");
}
