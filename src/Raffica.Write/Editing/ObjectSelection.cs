using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What is selected when it is not text: a picture or a rectangle of table cells (milestone 4.5a). It belongs to the editing session next to the text selection, never to the model, the
/// history or the revision: setting it collapses a text selection, and a real text selection change, an edit that moves the caret or another document ends it.
/// </summary>
internal abstract record ObjectSelection
{
    private protected ObjectSelection()
    {
    }
}

/// <summary>
/// A selected picture, by its place in the body (<see cref="ObjectId"/> names a place, not an object): the session keeps the drawing itself next to it (<see cref="EditSession.SelectedDrawing"/>) and
/// asks the model again after every edit; the selection ends when the place no longer holds that same drawing.
/// </summary>
internal sealed record PictureSelection(ObjectId Id) : ObjectSelection;

/// <summary>A selected rectangle of cells of one table; <see cref="CellRange.Table"/> and <paramref name="Table"/> are the same table.</summary>
internal sealed record CellSelection(TableRef Table, CellRange Range) : ObjectSelection;

/// <summary>The words a screen reader gets for an object selection.</summary>
internal static class ObjectAnnouncement
{
    /// <summary>
    /// "Picture: alt text" (just "Picture" without alt text), "Row 2 of 5, column 3 of 4" for one cell, "Rows 2 to 3 of 5, columns 1 to 4 of 4" for a range. <paramref name="drawing"/> is the selected picture's
    /// drawing and <paramref name="table"/> the selected table, as the session resolved them (null when it could not).
    /// </summary>
    public static string For(ObjectSelection selection, Picture? drawing, Table? table)
    {
        ArgumentNullException.ThrowIfNull(selection);
        switch (selection)
        {
            case PictureSelection:
                return drawing?.AltText is { Length: > 0 } alt ? WriteStrings.Fill(WriteStrings.ObjectPictureAlt, alt) : WriteStrings.ObjectPicture;
            case CellSelection cells:
                var range = cells.Range;
                var rows = table is null ? Math.Max(range.LastRow + 1, 1) : RowCount(table);
                var columns = table is null ? Math.Max(range.LastColumn + 1, 1) : Columns(table);
                var first = table is null ? range.FirstRow + 1 : RowNumber(table, range.FirstRow);
                var last = table is null ? range.LastRow + 1 : RowNumber(table, range.LastRow);
                return range.FirstRow == range.LastRow && range.FirstColumn == range.LastColumn
                    ? Format(WriteStrings.ObjectCell, first, rows, range.FirstColumn + 1, columns)
                    : Format(WriteStrings.ObjectCells, first, last, rows, range.FirstColumn + 1, range.LastColumn + 1, columns);
            default:
                return string.Empty;
        }
    }

    private static string Format(string template, params int[] values)
    {
        var text = template;
        for (var i = 0; i < values.Length; i++)
        {
            text = text.Replace("{" + i.ToString(WriteStrings.Culture) + "}", values[i].ToString(WriteStrings.Culture), StringComparison.Ordinal);
        }

        return text;
    }

    internal static int RowCount(Table table) => Math.Max(1, table.Rows.Count(r => r is TableRow));

    internal static int Columns(Table table) =>
        Math.Max(1, Math.Max(table.GridColumns.Count, table.Rows.OfType<TableRow>().Select(r => r.Items.Count(i => i is TableCell)).DefaultIfEmpty(0).Max()));

    // The 1-based number of a model row among the table's rows (rows that are not rows of the table, kept between them, do not count).
    internal static int RowNumber(Table table, int modelRow)
    {
        var number = 0;
        for (var i = 0; i < table.Rows.Count && i <= modelRow; i++)
        {
            if (table.Rows[i] is TableRow)
            {
                number++;
            }
        }

        return Math.Max(1, number);
    }
}
