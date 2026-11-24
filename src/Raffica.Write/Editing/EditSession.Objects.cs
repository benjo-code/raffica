using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The object selection of the session (milestone 4.5a): a picture or a rectangle of cells selected next to the text selection. It is session state only (never in the model, the history or the revision)
/// and nothing of it exists before the first object is selected. Rules: setting it collapses a text selection (the caret stays where it was and the view draws none while an object is selected); a real
/// change of the text selection, an edit that moves the selection and another document end it; every other edit asks the model again (<see cref="SelectedDrawing"/> is found again with
/// <see cref="ObjectIds.Find"/>, and the selection ends when the place no longer holds the same drawing or the table is gone). The event is raised on the UI thread, once per change, after the lease of an edit ended.
/// </summary>
internal sealed partial class EditSession
{
    private ObjectSelection? _objectSelection;
    private Picture? _drawing;
    private WordDocument? _objectModel;
    private (int Row, int Column)? _cellAnchor;
    private (int Row, int Column)? _cellFocus;
    private bool _objectDirty;

    /// <summary>The selected picture or cell range; null when none is (the usual case).</summary>
    public ObjectSelection? ObjectSelection => _objectSelection;

    /// <summary>The drawing of a <see cref="PictureSelection"/>, found in the model when it was selected and again after every edit; null without a picture selection. The hit test takes it so that a pointer move reads no model.</summary>
    public Picture? SelectedDrawing => _drawing;

    /// <summary>The corner of a cell selection that stays put when it is extended (the cell where it began); null without one.</summary>
    public (int Row, int Column)? CellAnchor => _cellAnchor;

    /// <summary>The corner of a cell selection that moves when it is extended; null without one.</summary>
    public (int Row, int Column)? CellFocus => _cellFocus;

    /// <summary>Raised on the UI thread when <see cref="ObjectSelection"/> became another selection or none.</summary>
    public event Action? ObjectSelectionChanged;

    /// <summary>Raised on the UI thread when the first save handle was opened: a gesture of the view in progress ends (a document that is being written is not a place to drag in).</summary>
    public event Action? SaveStarted;

    /// <summary>
    /// Selects an object (null clears it). A <see cref="PictureSelection"/> whose place holds no picture in the document is refused (false), as is a <see cref="CellSelection"/> of a table that is not there.
    /// A text selection that is a range becomes a caret at its end. Returns true when the selection is as asked (also when it was so already).
    /// </summary>
    public bool SetObjectSelection(ObjectSelection? selection)
    {
        VerifyAccess();
        if (selection is null)
        {
            ClearObjectSelection();
            return true;
        }

        if (Equals(_objectSelection, selection))
        {
            return true;
        }

        var model = _tab.Docx.Model;
        Picture? drawing = null;
        (int Row, int Column)? anchor = null;
        (int Row, int Column)? focus = null;
        switch (selection)
        {
            case PictureSelection picture:
                drawing = ObjectIds.Find(model.Body.Blocks, picture.Id)?.Picture;
                if (drawing is null)
                {
                    return false;
                }

                break;
            case CellSelection cells:
                if (cells.Table.Find(model.Body.Blocks) is null || !cells.Table.Equals(cells.Range.Table))
                {
                    return false;
                }

                anchor = (cells.Range.FirstRow, cells.Range.FirstColumn);
                focus = (cells.Range.LastRow, cells.Range.LastColumn);
                break;
            default:
                return false;
        }

        CollapseTextSelection();
        Commit(selection, drawing, anchor, focus, model);
        return true;
    }

    /// <summary>
    /// Selects the cells between two corner cells of a table (the corners are rows of <c>Table.Rows</c> and grid columns); the first corner becomes <see cref="CellAnchor"/> and the second <see cref="CellFocus"/>.
    /// False when the table is not there or a corner is outside it.
    /// </summary>
    public bool SelectCells(TableRef table, (int Row, int Column) anchor, (int Row, int Column) focus)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(table);
        var model = _tab.Docx.Model;
        if (table.Find(model.Body.Blocks) is not { } found || !IsCell(found, anchor) || !IsCell(found, focus))
        {
            return false;
        }

        var range = CellRange.Between(table, anchor.Row, anchor.Column, focus.Row, focus.Column);
        var selection = new CellSelection(table, range);
        if (Equals(_objectSelection, selection) && _cellAnchor == anchor && _cellFocus == focus)
        {
            return true;
        }

        CollapseTextSelection();
        Commit(selection, null, anchor, focus, model);
        return true;
    }

    /// <summary>Selects the whole row (a model row of the table). False when the row is not a row of the table.</summary>
    public bool SelectRow(TableRef table, int row)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(table);
        return table.Find(_tab.Docx.Model.Body.Blocks) is { } found && SelectCells(table, (row, 0), (row, ObjectAnnouncement.Columns(found) - 1));
    }

    /// <summary>Selects the whole column (a grid column of the table, first to last row). False when there is no such column.</summary>
    public bool SelectColumn(TableRef table, int column)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(table);
        return table.Find(_tab.Docx.Model.Body.Blocks) is { } found && Rows(found) is { Count: > 0 } rows && SelectCells(table, (rows[0], column), (rows[^1], column));
    }

    /// <summary>Selects every cell of the table.</summary>
    public bool SelectTable(TableRef table)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(table);
        return table.Find(_tab.Docx.Model.Body.Blocks) is { } found && Rows(found) is { Count: > 0 } rows && SelectCells(table, (rows[0], 0), (rows[^1], ObjectAnnouncement.Columns(found) - 1));
    }

    /// <summary>
    /// Moves the focus corner of the cell selection by whole rows and columns (Shift and an arrow): a row step goes to the next row of the table and stops at the first and last, a column step stops at the first and the last
    /// grid column. False without a cell selection or when the focus does not move.
    /// </summary>
    public bool ExtendCellSelection(int rowStep, int columnStep)
    {
        VerifyAccess();
        if (_objectSelection is not CellSelection cells || _cellAnchor is not { } anchor || _cellFocus is not { } focus || cells.Table.Find(_tab.Docx.Model.Body.Blocks) is not { } table || Rows(table) is not { Count: > 0 } rows)
        {
            return false;
        }

        var at = rows.IndexOf(focus.Row);
        var nextRow = rows[Math.Clamp((at < 0 ? 0 : at) + rowStep, 0, rows.Count - 1)];
        var nextColumn = Math.Clamp(focus.Column + columnStep, 0, ObjectAnnouncement.Columns(table) - 1);
        return (nextRow, nextColumn) != focus && SelectCells(cells.Table, anchor, (nextRow, nextColumn));
    }

    /// <summary>The words for the selected object (see <see cref="ObjectAnnouncement"/>); null without one.</summary>
    public string? DescribeObjectSelection() =>
        _objectSelection is { } selection
            ? ObjectAnnouncement.For(selection, _drawing, selection is CellSelection cells ? cells.Table.Find(_tab.Docx.Model.Body.Blocks) : null)
            : null;

    /// <summary>Ends the object selection when the tab holds another document than the one it was made in (the model object is replaced).</summary>
    public void EndObjectSelectionOfAnotherDocument()
    {
        if (_objectSelection is not null && !ReferenceEquals(_objectModel, _tab.Docx.Model))
        {
            ClearObjectSelection();
        }
    }

    private void CollapseTextSelection()
    {
        if (_selection is { IsEmpty: false } range)
        {
            _selection = new TextRange(range.End, range.End);
            Pending.Clear();
            SelectionChanged?.Invoke();
        }
    }

    private void Commit(ObjectSelection selection, Picture? drawing, (int Row, int Column)? anchor, (int Row, int Column)? focus, WordDocument model)
    {
        _objectSelection = selection;
        _drawing = drawing;
        _cellAnchor = anchor;
        _cellFocus = focus;
        _objectModel = model;
        ObjectSelectionChanged?.Invoke();
    }

    // The object selection ends now (the caller is on the UI thread outside an operation).
    private void ClearObjectSelection()
    {
        if (_objectSelection is null)
        {
            return;
        }

        DropObjectSelection();
        ObjectSelectionChanged?.Invoke();
    }

    private void DropObjectSelection()
    {
        _objectSelection = null;
        _drawing = null;
        _cellAnchor = null;
        _cellFocus = null;
        _objectModel = null;
    }

    // Inside an operation: the selection ends and the event waits for the end of the lease.
    private void DropObjectSelectionInOperation()
    {
        if (_objectSelection is not null)
        {
            DropObjectSelection();
            _objectDirty = true;
        }
    }

    // After an edit that kept the selection: the picture is found again in the model and the selection ends when the place holds another drawing or none, the table is gone or a corner fell outside it.
    private void ReconcileObjectSelection()
    {
        switch (_objectSelection)
        {
            case null:
                return;
            case PictureSelection picture:
                if (!ReferenceEquals(ObjectIds.Find(_tab.Docx.Model.Body.Blocks, picture.Id)?.Picture, _drawing))
                {
                    DropObjectSelectionInOperation();
                }

                return;
            case CellSelection cells:
                if (cells.Table.Find(_tab.Docx.Model.Body.Blocks) is not { } table || !IsCell(table, (cells.Range.FirstRow, cells.Range.FirstColumn)) || !IsCell(table, (cells.Range.LastRow, cells.Range.LastColumn)))
                {
                    DropObjectSelectionInOperation();
                }

                return;
            default:
                return;
        }
    }

    private void RaiseObjectSelectionChanged()
    {
        if (_objectDirty)
        {
            _objectDirty = false;
            ObjectSelectionChanged?.Invoke();
        }
    }

    // The model rows of the table that are rows, in order.
    private static List<int> Rows(Table table)
    {
        var rows = new List<int>(table.Rows.Count);
        for (var i = 0; i < table.Rows.Count; i++)
        {
            if (table.Rows[i] is TableRow)
            {
                rows.Add(i);
            }
        }

        return rows;
    }

    private static bool IsCell(Table table, (int Row, int Column) corner) =>
        corner.Row >= 0 && corner.Row < table.Rows.Count && table.Rows[corner.Row] is TableRow && corner.Column >= 0 && corner.Column < ObjectAnnouncement.Columns(table);
}
