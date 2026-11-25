namespace Raffica.Write.Editing;

/// <summary>A row height to write on the rows <paramref name="FirstRow"/> to <paramref name="LastRow"/> (indexes of <c>Table.Rows</c>, both included): <paramref name="Twips"/> zero removes the height, <paramref name="Exact"/> is the rule "exactly" instead of "at least".</summary>
public sealed record TableRowHeight(int FirstRow, int LastRow, int Twips, bool Exact);

/// <summary>
/// What the Table properties card changes, written as ONE history step by <see cref="TextEditor.SetTableProperties"/>: the widths of the columns (one per grid column) or, when only the width of the table
/// changed, the table width (the columns keeping their proportions); a row height for a range of rows; the repeated header row. A part that is null is left as it is; when both
/// <paramref name="ColumnTwips"/> and <paramref name="TableWidthTwips"/> are given the columns win (their sum is the table width).
/// </summary>
public sealed record TableProperties(int? TableWidthTwips = null, IReadOnlyList<int>? ColumnTwips = null, TableRowHeight? RowHeight = null, bool? HeaderRow = null)
{
    /// <summary>True when no part is set (there is nothing to write).</summary>
    public bool IsEmpty => TableWidthTwips is null && ColumnTwips is null && RowHeight is null && HeaderRow is null;
}

/// <summary>
/// What the Table properties card shows of a table as the model holds it: the width of each grid column (<c>w:gridCol</c>), the table width in twips (null when it is automatic or a percentage), whether the first row
/// is a repeated header row, and the height of one row.
/// </summary>
public sealed record TableValues(IReadOnlyList<int> GridTwips, int? TableWidthTwips, bool HeaderRow, int RowHeightTwips, bool RowHeightExact);
