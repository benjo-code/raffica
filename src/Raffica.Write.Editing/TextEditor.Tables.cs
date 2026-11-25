using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>A cell of a top-level table the caret is in: the table, the row (an index of <c>Table.Rows</c>) and the grid column the cell starts in.</summary>
public sealed record TableCellPlace(TableRef Table, int Row, int Column);

public sealed partial class TextEditor
{
    // The outcome of a table structure operation: the result of the edit, or the reason for the refusal.
    private readonly record struct TableAttempt(EditResult? Result, TableRefusal? Refusal);

    // The table an operation acts on: where it lives (story, the list of blocks that holds it and its index there) and the live object.
    private sealed record TableTarget(IList<Block> Container, int Index, Table Table);

    // ---- where the caret is ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The cell of a top-level table of the body that holds the paragraph of <paramref name="caret"/>, or null when it is in no table, in another story, or in a table nested in a cell (commands act on top-level tables only).
    /// </summary>
    public TableCellPlace? TableOf(TextPosition caret)
    {
        if (caret.Paragraph is null || caret.Story != StoryRef.Body)
        {
            return null;
        }

        var blocks = _document.Body.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (FindCell(blocks[i], caret.Paragraph, 0) is { } found)
            {
                var map = TableGridMap.Of(found.Table);
                return map.Cells.FirstOrDefault(c => ReferenceEquals(c.Cell, found.Cell)) is { } cell
                    ? new TableCellPlace(new TableRef(StoryRef.Body, i), cell.RowIndex, cell.Column)
                    : null;
            }
        }

        return null;
    }

    // The top-level table (through content controls) whose cell holds the paragraph directly (not in a table nested in the cell).
    private static (Table Table, TableCell Cell)? FindCell(Block block, Paragraph paragraph, int depth)
    {
        if (depth > 32)
        {
            return null;
        }

        switch (block)
        {
            case ContentControlBlock control:
                foreach (var inner in control.Content)
                {
                    if (FindCell(inner, paragraph, depth + 1) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case Table table:
                return FindCellIn(table.Rows, table, paragraph, 0);
            default:
                return null;
        }
    }

    private static (Table Table, TableCell Cell)? FindCellIn(IEnumerable<RowItem> items, Table table, Paragraph paragraph, int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        foreach (var item in items)
        {
            switch (item)
            {
                case TableRow row when FindCellIn(row.Items, table, paragraph, depth + 1) is { } inRow:
                    return inRow;
                case TableCell cell when FindIn(cell.Blocks, paragraph) is { } found:
                    // Directly in the cell (or in a control of it) is a cell of this table; in a table nested in the cell it is not.
                    return IsBodyOrControlList(cell.Blocks, found.Container) ? (table, cell) : null;
                default:
                    break;
            }
        }

        return null;
    }

    /// <summary>True when <paramref name="container"/> is <paramref name="blocks"/> itself or the content of a content control reached from it without passing a table cell.</summary>
    internal static bool IsBodyOrControlList(IList<Block> blocks, IList<Block> container) => ReachesThroughControls(blocks, container, 0);

    private static bool ReachesThroughControls(IList<Block> blocks, IList<Block> target, int depth)
    {
        if (ReferenceEquals(blocks, target))
        {
            return true;
        }

        if (depth > 32)
        {
            return false;
        }

        foreach (var block in blocks)
        {
            if (block is ContentControlBlock control && ReachesThroughControls(control.Content, target, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    // ---- insert ------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The reason a table of <paramref name="columns"/> by <paramref name="rows"/> cannot be inserted at <paramref name="caret"/> (see <see cref="TableRules.Refusal"/>), or null: also a caret in an inline content control of
    /// the kinds text, combo box, drop-down list or date, which only the offset can tell. A stale position throws <see cref="StalePositionException"/>.
    /// </summary>
    public TableRefusal? RefusalForInsertTable(TextPosition caret, int columns, int rows, bool prefixDocument = false)
    {
        if (TableRules.Refusal(_document, caret, columns, rows, prefixDocument) is { } refused)
        {
            return refused;
        }

        var (story, _, _) = Locate(caret);
        var map = Map(story, caret.Paragraph);
        var point = ResolvePoint(map, EditRules.Snap(map, Math.Max(0, caret.Offset)));
        return point.Run is { } run && InRestrictedInlineControl(caret.Paragraph.Inlines, run, 0) ? TableRefusal.RestrictedControl : null;
    }

    /// <summary>
    /// Inserts a table of <paramref name="columns"/> by <paramref name="rows"/> empty cells at the caret as one history step. The table is as wide as the text area of the section it lands in (in a section with several
    /// columns the width of one column), its columns are equal, its layout fixed, and it has the table style <c>TableGrid</c> (single 0.5 pt borders; made as a companion of the step when the document lacks it, see
    /// <see cref="EditResult.Style"/>) or, in a document without a style sheet, the same borders directly. The paragraph the caret is in decides the place: at its start the table goes before it, at its end (or in an empty
    /// paragraph, which then counts as the start) after it, and in the middle the paragraph is split and the table goes between the halves. The table always has a paragraph after it (a body that ends in a table is
    /// repaired by Word) and never touches another table (two tables with nothing between them become one in Word): a paragraph is added where it would. The result's caret is in the first cell. Returns null for a place
    /// that takes no table (<see cref="RefusalForInsertTable"/> says why). A stale position throws <see cref="StalePositionException"/> before anything changes.
    /// </summary>
    public EditResult? InsertTable(TextPosition at, int columns, int rows, bool prefixDocument = false)
    {
        if (RefusalForInsertTable(at, columns, rows, prefixDocument) is not null)
        {
            return null;
        }

        var (story, container, index) = Locate(at);
        var revision = History.BeginOperation();
        var paragraph = at.Paragraph;
        var map = Map(story, paragraph);
        var length = map.Projection.Text.Length;
        var offset = EditRules.Snap(map, Math.Clamp(at.Offset, 0, length));
        var atEnd = length > 0 && offset >= length;
        var atStart = offset <= 0 || length == 0;

        var style = TableOperations.EnsureGridStyle(_document);
        var table = TableOperations.NewTable(columns, rows, TableWidthTwips(container, index, paragraph, columns, atEnd), _document.Styles is not null);
        var cellParagraph = table.Rows.OfType<TableRow>().First().Items.OfType<TableCell>().First().Blocks.OfType<Paragraph>().First();

        var old = new List<Block>();
        var added = new List<Block>();
        int first;
        if (atStart)
        {
            // Before the paragraph; a table right before would join this one in Word.
            first = index;
            if (index > 0 && container[index - 1] is Table)
            {
                added.Add(new Paragraph());
            }

            added.Add(table);
        }
        else if (atEnd)
        {
            first = index + 1;
            added.Add(table);
            if (first >= container.Count || container[first] is not Paragraph)
            {
                added.Add(new Paragraph());
            }
        }
        else
        {
            first = index;
            old.Add(paragraph.Clone());
            var shell = Shell(paragraph);
            var point = ResolvePoint(map, offset);
            var (item, chars) = CutOf(map, point);
            var rest = InlineSplitter.Split(paragraph, item, chars);
            var tail = shell.Clone();
            foreach (var inline in rest)
            {
                tail.Inlines.Add(inline);
            }

            tail.SectionBreak = paragraph.SectionBreak;
            paragraph.SectionBreak = null;
            added.Add(paragraph);
            added.Add(table);
            added.Add(tail);
        }

        if (old.Count == 0)
        {
            InsertRange(container, first, added);
        }
        else
        {
            // The first block of the change is the paragraph itself (already cut), then the table and the tail.
            InsertRange(container, index + 1, added.Skip(1));
        }

        _document.Edits.Raise();
        var change = new BlockChange(story, container, first, old.Count, added.Count, old);
        var result = new EditResult(new TextPosition(story, cellParagraph, 0), [change], Style: style);
        result.Attribute(_document);
        var tableIndex = first + added.IndexOf(table);
        var before = new EditHistory.Point(index, offset);
        History.Record(result, before, revision, null, after: new EditHistory.Point(tableIndex, 0));
        return result;
    }

    // The width of a new table in twips: the text area of the section the table lands in (one column wide in a section with several), at least a little more than a hundred twips a column.
    private int TableWidthTwips(IList<Block> container, int index, Paragraph paragraph, int columns, bool after)
    {
        var sections = SectionList.Of(_document);
        var top = TopLevelIndex(container, index);
        var section = sections.FirstOrDefault(s => top >= s.FirstBlock && top <= s.LastBlock) ?? sections[^1];
        if (after && paragraph.SectionBreak is not null && section.Index + 1 < sections.Count)
        {
            section = sections[section.Index + 1];
        }

        var setup = SectionPageSetup.Read(section.Properties);
        var w = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        // The gutter is part of the left margin for the layout (PageGeometry), so it is taken off the text width as well.
        var gutter = int.TryParse((string?)section.Properties.Get(w + "pgMar")?.Attribute(w + "gutter"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var g) ? Math.Max(0, g) : 0;
        var text = setup.WidthTwips - setup.LeftTwips - setup.RightTwips - gutter;
        var cols = section.Properties.Get(w + "cols");
        var explicitWidths = cols?.Elements(w + "col")
            .Select(c => int.TryParse((string?)c.Attribute(w + "w"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var cw) ? cw : 0)
            .Where(cw => cw > 0).ToList() ?? [];
        var unequal = cols is not null && (string?)cols.Attribute(w + "equalWidth") is "0" or "false" or "off" && explicitWidths.Count > 0;
        if (unequal)
        {
            // Columns of unequal width: the narrowest one, so the table fits whichever column the caret is in.
            text = Math.Min(text, explicitWidths.Min());
        }
        else if (setup.Columns > 1)
        {
            text = (text - ((setup.Columns - 1) * setup.ColumnSpaceTwips)) / setup.Columns;
        }

        return Math.Max(text, columns * 100);
    }

    // The index in the body of the top-level block that holds a block (the block itself, or the content control around it).
    private int TopLevelIndex(IList<Block> container, int index)
    {
        var body = _document.Body.Blocks;
        if (ReferenceEquals(container, body))
        {
            return index;
        }

        for (var i = 0; i < body.Count; i++)
        {
            if (body[i] is ContentControlBlock control && ReachesThroughControls(control.Content, container, 0))
            {
                return i;
            }
        }

        return Math.Max(0, Math.Min(index, body.Count - 1));
    }

    // ---- operations on a table ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds a row before or after the row at <paramref name="index"/> of <c>Table.Rows</c> as one history step. The new row has the cells of that row; where it lands inside a vertical merge the merge grows by it.
    /// Null when the command is refused (<see cref="RefusalForAddRow"/> says why).
    /// </summary>
    public EditResult? AddRow(TableRef table, int index, bool after, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableOperations.AddRow(t, m, index, after), commit: true).Result;

    /// <summary>The reason <see cref="AddRow"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForAddRow(TableRef table, int index, bool after, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableOperations.AddRow(t, m, index, after), commit: false).Refusal;

    /// <summary>
    /// Adds a column left or right of the grid column <paramref name="index"/> as one history step; a cell that spans across the new boundary grows by one column. Null when refused
    /// (<see cref="RefusalForAddColumn"/> says why).
    /// </summary>
    public EditResult? AddColumn(TableRef table, int index, bool after, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableOperations.AddColumn(t, m, index, after), commit: true).Result;

    /// <summary>The reason <see cref="AddColumn"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForAddColumn(TableRef table, int index, bool after, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableOperations.AddColumn(t, m, index, after), commit: false).Refusal;

    /// <summary>
    /// Deletes the rows of the range (the columns of the range are ignored) as one history step, with the rules of a vertical merge (a merge start passes to the next row, a span shrinks). Deleting every row deletes the
    /// table (see <see cref="DeleteTable"/>). Footnotes and endnotes referenced from the deleted cells go with them. Null when refused.
    /// </summary>
    public EditResult? DeleteRows(CellRange range, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableOperations.DeleteRows(t, m, range.FirstRow, range.LastRow), commit: true).Result;

    /// <summary>The reason <see cref="DeleteRows"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForDeleteRows(CellRange range, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableOperations.DeleteRows(t, m, range.FirstRow, range.LastRow), commit: false).Refusal;

    /// <summary>
    /// Deletes the grid columns of the range (the rows of the range are ignored) as one history step: a cell that spans the column shrinks, a cell that spans only deleted columns goes with its content. Deleting every column
    /// deletes the table. Null when refused.
    /// </summary>
    public EditResult? DeleteColumns(CellRange range, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableOperations.DeleteColumns(t, m, range.FirstColumn, range.LastColumn), commit: true).Result;

    /// <summary>The reason <see cref="DeleteColumns"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForDeleteColumns(CellRange range, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableOperations.DeleteColumns(t, m, range.FirstColumn, range.LastColumn), commit: false).Refusal;

    /// <summary>
    /// Merges the cells of the range into one as one history step; a range that cuts through a merged cell is widened to whole merged cells first. The content is concatenated in reading order into the first cell, empty
    /// paragraphs skipped. Null when refused (a range of one cell has nothing to merge).
    /// </summary>
    public EditResult? MergeCells(CellRange range, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableOperations.Merge(t, m, range.FirstRow, range.FirstColumn, range.LastRow, range.LastColumn), commit: true).Result;

    /// <summary>The reason <see cref="MergeCells"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForMerge(CellRange range, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableOperations.Merge(t, m, range.FirstRow, range.FirstColumn, range.LastRow, range.LastColumn), commit: false).Refusal;

    /// <summary>
    /// Splits the merged cell that covers (<paramref name="row"/>, <paramref name="column"/>) back into the cells it was made of as one history step; the content stays in the first cell. A cell that is not merged is
    /// refused with <see cref="TableRefusal.NotMerged"/> (splitting an unmerged cell into several columns is not offered).
    /// </summary>
    public EditResult? SplitCell(TableRef table, int row, int column, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableOperations.Split(t, m, row, column), commit: true).Result;

    /// <summary>The reason <see cref="SplitCell"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForSplit(TableRef table, int row, int column, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableOperations.Split(t, m, row, column), commit: false).Refusal;

    /// <summary>
    /// Moves the row at <paramref name="row"/> (an index of <c>Table.Rows</c>) by <paramref name="delta"/> rows (negative is up) as one history step. Refused when it would cut through a vertical merge or cross the
    /// border between the repeated header rows and the others.
    /// </summary>
    public EditResult? MoveRow(TableRef table, int row, int delta, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableOperations.MoveRow(t, m, row, delta), commit: true).Result;

    /// <summary>The reason <see cref="MoveRow"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForMoveRow(TableRef table, int row, int delta, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableOperations.MoveRow(t, m, row, delta), commit: false).Refusal;

    /// <summary>
    /// Deletes the table as one history step, with the footnotes and endnotes its cells refer to. The container keeps a paragraph where the table was the only block, or where the block before it is a table (so the body
    /// never ends in one); the caret goes to the paragraph that follows the table or, at the end, the one before it. Null when refused.
    /// </summary>
    public EditResult? DeleteTable(TableRef table, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, static (_, _) => TableMutation.WholeTable(), commit: true, needsGrid: false).Result;

    /// <summary>The reason <see cref="DeleteTable"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForDeleteTable(TableRef table, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, static (_, _) => TableMutation.WholeTable(), commit: false, needsGrid: false).Refusal;

    // ---- formatting --------------------------------------------------------------------------------------------------------------------
    // Borders, shading, the header row, widths and row height work on a regular and consistent table (the same rule as the structure operations; any other refuses with UnsupportedStructure). Each is one block
    // change that replaces the table by a changed copy, so the paragraphs of the table are new objects. A formatting operation does not change the structure, so with a caret in the table the result's caret is
    // the same place in the new table (the same paragraph by order, the same offset); without one it is the first cell the command touched.

    /// <summary>
    /// Writes the borders of <paramref name="spec"/> on the cells of the range (widened to whole merged cells) as one history step; see <see cref="TableFormatting.Borders"/> for the sides and for the rule that a border
    /// shared by two cells is written on both. <paramref name="caret"/> is the caret before the command (see above). Null when refused (<see cref="RefusalForSetBorders"/> says why).
    /// </summary>
    public EditResult? SetBorders(CellRange range, BorderSpec spec, TextPosition? caret = null, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableFormatting.Borders(t, m, range, spec), commit: true, keep: caret).Result;

    /// <summary>The reason <see cref="SetBorders"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForSetBorders(CellRange range, BorderSpec spec, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableFormatting.Borders(t, m, range, spec), commit: false).Refusal;

    /// <summary>Shades the cells of the range with a solid colour (0xAARRGGBB, alpha ignored; the caller offers the fixed colour list) or, with null, removes the shading, as one history step. Null when refused.</summary>
    public EditResult? SetShading(CellRange range, uint? color, TextPosition? caret = null, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableFormatting.Shading(t, m, range, color), commit: true, keep: caret).Result;

    /// <summary>The reason <see cref="SetShading"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForSetShading(CellRange range, uint? color, bool prefixDocument = false) =>
        Restructure(range.Table, prefixDocument, (t, m) => TableFormatting.Shading(t, m, range, color), commit: false).Refusal;

    /// <summary>Turns the repeated header row on (the first row) or off (the leading header rows) as one history step. Null when refused.</summary>
    public EditResult? SetHeaderRow(TableRef table, bool on, TextPosition? caret = null, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.HeaderRow(t, m, on), commit: true, keep: caret).Result;

    /// <summary>The reason <see cref="SetHeaderRow"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForSetHeaderRow(TableRef table, bool on, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.HeaderRow(t, m, on), commit: false).Refusal;

    /// <summary>
    /// Sets the width of every column in twips (one per grid column) as one history step. The first width write makes an autofit or percentage table fixed and recomputes the grid, <c>w:tblW</c> and every
    /// <c>w:tcW</c> from the new widths (see <see cref="TableFormatting.ColumnWidths"/>); every width is given, so nothing of the drawn widths is needed. Null when refused.
    /// </summary>
    public EditResult? SetColumnWidths(TableRef table, IReadOnlyList<int> twips, TextPosition? caret = null, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.ColumnWidths(t, m, twips), commit: true, keep: caret).Result;

    /// <summary>The reason <see cref="SetColumnWidths"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForSetColumnWidths(TableRef table, IReadOnlyList<int> twips, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.ColumnWidths(t, m, twips), commit: false).Refusal;

    /// <summary>
    /// Sets the width of the table in twips, the columns keeping their proportions and the sum being exactly that width, as one history step. <paramref name="drawnColumnTwips"/> are the widths the layout draws
    /// for the columns now (one per grid column, from the column borders of the object map, in twips): for a table that is not yet fixed and in twips the proportions come from them, so the conversion freezes what
    /// the reader sees; without them the proportions come from the grid (the fallback). Null when refused.
    /// </summary>
    public EditResult? SetTableWidth(TableRef table, int twips, IReadOnlyList<int>? drawnColumnTwips = null, TextPosition? caret = null, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.TableWidth(t, m, twips, drawnColumnTwips), commit: true, keep: caret).Result;

    /// <summary>The reason <see cref="SetTableWidth"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForSetTableWidth(TableRef table, int twips, IReadOnlyList<int>? drawnColumnTwips = null, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.TableWidth(t, m, twips, drawnColumnTwips), commit: false).Refusal;

    /// <summary>
    /// Sets the height of the row at <paramref name="row"/> (an index of <c>Table.Rows</c>) in twips, at least or exact, as one history step; zero removes the height. Null when refused.
    /// </summary>
    public EditResult? SetRowHeight(TableRef table, int row, int twips, bool exact, TextPosition? caret = null, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.RowHeight(t, m, row, twips, exact), commit: true, keep: caret).Result;

    /// <summary>The reason <see cref="SetRowHeight"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForSetRowHeight(TableRef table, int row, int twips, bool exact, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.RowHeight(t, m, row, twips, exact), commit: false).Refusal;

    /// <summary>
    /// Makes the columns equal, adding up to the current width of the table (the remainder to the last column), as one history step. <paramref name="drawnColumnTwips"/> as for <see cref="SetTableWidth"/>: for a
    /// table that is not yet fixed and in twips the width is their sum; without them it is <c>w:tblW</c> in twips or the sum of the grid (the fallback). Null when refused.
    /// </summary>
    public EditResult? DistributeColumns(TableRef table, IReadOnlyList<int>? drawnColumnTwips = null, TextPosition? caret = null, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.Distribute(t, m, drawnColumnTwips), commit: true, keep: caret).Result;

    /// <summary>The reason <see cref="DistributeColumns"/> refuses, or null. Nothing changes.</summary>
    public TableRefusal? RefusalForDistributeColumns(TableRef table, IReadOnlyList<int>? drawnColumnTwips = null, bool prefixDocument = false) =>
        Restructure(table, prefixDocument, (t, m) => TableFormatting.Distribute(t, m, drawnColumnTwips), commit: false).Refusal;

    // Every paragraph of a table in reading order, nested tables and content controls included: two tables of the same structure give the paragraphs in the same order, which maps a position into a copy.
    private static List<Paragraph> ParagraphsOf(Table table)
    {
        var found = new List<Paragraph>();
        CollectParagraphs(table.Rows, found, 0);
        return found;
    }

    private static void CollectParagraphs(IEnumerable<RowItem> items, List<Paragraph> found, int depth)
    {
        if (depth > 40)
        {
            return;
        }

        foreach (var item in items)
        {
            switch (item)
            {
                case TableRow row:
                    CollectParagraphs(row.Items, found, depth + 1);
                    break;
                case TableCell cell:
                    CollectParagraphs(cell.Blocks, found, depth + 1);
                    break;
                default:
                    break;
            }
        }
    }

    private static void CollectParagraphs(IEnumerable<Block> blocks, List<Paragraph> found, int depth)
    {
        if (depth > 40)
        {
            return;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    found.Add(paragraph);
                    break;
                case ContentControlBlock control:
                    CollectParagraphs(control.Content, found, depth + 1);
                    break;
                case Table nested:
                    CollectParagraphs(nested.Rows, found, depth + 1);
                    break;
                default:
                    break;
            }
        }
    }

    // The table a reference names and where it lives, or the reason it cannot be edited: a prefix document, another story than the body, no table (or two in one control), a content control that holds text only.
    private (TableTarget? Target, TableRefusal? Refusal) FindTable(TableRef reference, bool prefixDocument)
    {
        if (prefixDocument)
        {
            return (null, TableRefusal.PrefixDocument);
        }

        if (reference is null)
        {
            return (null, TableRefusal.NoTable);
        }

        if (TableRules.StoryRefusal(reference.Story) is { } story)
        {
            return (null, story);
        }

        var blocks = _document.Body.Blocks;
        if (reference.Find(blocks) is not { } table || ContainerOf(blocks, table, 0) is not { } place)
        {
            return (null, TableRefusal.NoTable);
        }

        return InSingleParagraphControl(blocks, place.Container)
            ? (null, TableRefusal.RestrictedControl)
            : (new TableTarget(place.Container, place.Index, table), null);
    }

    private static (IList<Block> Container, int Index)? ContainerOf(IList<Block> blocks, Table table, int depth)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], table))
            {
                return (blocks, i);
            }
        }

        if (depth > 32)
        {
            return null;
        }

        foreach (var block in blocks)
        {
            if (block is ContentControlBlock control && ContainerOf(control.Content, table, depth + 1) is { } inside)
            {
                return inside;
            }
        }

        return null;
    }

    // Runs a structure operation on a clone of the table: a refusal changes nothing (and with commit false nothing is ever committed); otherwise the clone replaces the table in the document as one block change.
    private TableAttempt Restructure(TableRef reference, bool prefixDocument, Func<Table, TableGridMap, TableMutation> operate, bool commit, bool needsGrid = true, TextPosition? keep = null)
    {
        var (target, refusal) = FindTable(reference, prefixDocument);
        if (target is null)
        {
            return new TableAttempt(null, refusal);
        }

        if (needsGrid && TableGridMap.Of(target.Table) is not { IsRegular: true, IsConsistent: true })
        {
            return new TableAttempt(null, TableRefusal.UnsupportedStructure);
        }

        var clone = target.Table.Clone();
        var mutation = operate(clone, TableGridMap.Of(clone));
        if (mutation.Refusal is { } refused)
        {
            return new TableAttempt(null, refused);
        }

        if (!commit)
        {
            return new TableAttempt(null, null);
        }

        var (landing, offset) = KeptCaret(target.Table, clone, keep) ?? (mutation.Caret, 0);
        return new TableAttempt(mutation.RemoveTable ? RemoveTableBlock(target) : ReplaceTableBlock(target, clone, landing, offset), null);
    }

    // The caret of a position in the table before a formatting command, in the changed copy: the paragraph at the same place in reading order and the same offset. Null for no position, a position outside the table
    // or one that is stale; the command then puts the caret in the first cell it touched.
    private static (Paragraph? Paragraph, int Offset)? KeptCaret(Table before, Table after, TextPosition? caret)
    {
        if (caret is not { } position || position.Paragraph is null || position.Story != StoryRef.Body)
        {
            return null;
        }

        var index = ParagraphsOf(before).FindIndex(p => ReferenceEquals(p, position.Paragraph));
        var copies = ParagraphsOf(after);
        return index >= 0 && index < copies.Count ? (copies[index], position.Offset) : null;
    }

    private EditResult ReplaceTableBlock(TableTarget target, Table replacement, Paragraph? caret, int caretOffset = 0)
    {
        var revision = History.BeginOperation();
        // Everything that can throw is done before the document changes or its revision rises: a failure after a raise would leave a changed document with no result.
        var paragraph = caret ?? FirstParagraph([replacement]) ?? throw new InvalidOperationException("A table holds no paragraph.");
        var references = NoteOperations.References([target.Table]);
        target.Container[target.Index] = replacement;
        var gone = RemoveUnreferencedNotes(references, [replacement]);
        _document.Edits.Raise();
        var result = new EditResult(new TextPosition(StoryRef.Body, paragraph, caretOffset), [new BlockChange(StoryRef.Body, target.Container, target.Index, 1, 1, [target.Table])], Objects: gone);
        result.Attribute(_document);
        var point = new EditHistory.Point(target.Index, 0);
        History.Record(result, point, revision, null, after: point);
        return result;
    }

    // The notes whose reference was in the table that was replaced by a changed copy and is in none of the blocks that took its place (the copy holds clones of the references, so they are compared by kind and id,
    // never by object). Null when no note went.
    private ObjectChange? RemoveUnreferencedNotes(List<RunContent> before, IEnumerable<Block> after)
    {
        if (before.Count == 0)
        {
            return null;
        }

        var kept = new HashSet<(NoteType Type, int Id)>(NoteOperations.References(after).Select(NoteOperations.Describe));
        var removed = new List<Note>();
        foreach (var reference in before)
        {
            var (type, id) = NoteOperations.Describe(reference);
            if (kept.Contains((type, id)))
            {
                continue;
            }

            var collection = type == NoteType.Endnote ? _document.Endnotes : _document.Footnotes;
            if (collection?.Find(id) is { Kind: NoteKind.Normal } note && !removed.Contains(note))
            {
                collection.Notes.Remove(note);
                removed.Add(note);
            }
        }

        return removed.Count == 0 ? null : new ObjectChange([], removed, [], []);
    }

    private EditResult RemoveTableBlock(TableTarget target)
    {
        var revision = History.BeginOperation();
        var references = NoteOperations.References([target.Table]);
        var container = target.Container;
        var index = target.Index;

        // Where the table was the only block, or the block before it is a table (the end of the list would be one), a paragraph takes its place.
        var last = index == container.Count - 1 ? (index > 0 ? container[index - 1] : null) : container[^1];
        var keep = last is null or Table;
        Paragraph? replacement = keep ? new Paragraph() : null;
        if (replacement is null)
        {
            container.RemoveAt(index);
        }
        else
        {
            container[index] = replacement;
        }

        var added = replacement is null ? 0 : 1;
        var gone = RemoveUnreferencedNotes(references, container.Skip(index).Take(added));
        _document.Edits.Raise();

        Paragraph paragraphAfter;
        var landing = index;
        if (replacement is not null)
        {
            paragraphAfter = replacement;
        }
        else if (container.Skip(index).OfType<Paragraph>().FirstOrDefault() is { } next)
        {
            paragraphAfter = next;
            landing = container.IndexOf(next);
        }
        else if (container.Take(index).OfType<Paragraph>().LastOrDefault() is { } previous)
        {
            paragraphAfter = previous;
            landing = container.IndexOf(previous);
        }
        else
        {
            paragraphAfter = FirstParagraph(container) ?? throw new InvalidOperationException("The list of blocks holds no paragraph after the table was removed.");
        }

        var offset = paragraphAfter == replacement || container.IndexOf(paragraphAfter) >= index ? 0 : Map(StoryRef.Body, paragraphAfter).Projection.Text.Length;
        var result = new EditResult(new TextPosition(StoryRef.Body, paragraphAfter, offset), [new BlockChange(StoryRef.Body, container, index, 1, added, [target.Table])], Objects: gone);
        result.Attribute(_document);
        History.Record(result, new EditHistory.Point(index, 0), revision, null, after: new EditHistory.Point(Math.Max(0, landing), offset));
        return result;
    }
}
