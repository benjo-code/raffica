using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write.Viewing;

/// <summary>
/// The pointer gestures on a table (milestone 4.5a): an inner column border moves between its two columns, the right edge changes the last column and the table, a row moves by its handle in the left margin, and
/// a drag from one cell into another selects the cells between. Each is a kind of the <see cref="ObjectDrag"/> machine; the guide or the drop line is drawn over the page by the overlay while the pointer is
/// down, and the document changes once, on release, as one history step (or not at all: Escape, a lost capture, a save starting, an edit and a change of the view end the gesture with no change).
/// </summary>
/// <remarks>
/// Rules. A pointer move with no button down reads no model: it asks the layout's hit test of the page under the pointer, and only when that page has a table (a page with no table costs one count compare);
/// while a gesture goes on a move changes the machine and the overlay and nothing else (a cell range changes the selection, which is no edit). The widths a gesture writes are the widths the layout draws
/// now (the column borders of the page piece the gesture began on), so the first write freezes what the reader sees (the width write converts autofit and percentage tables, see <c>SetColumnWidths</c>). The drag
/// of a row asks the editor for the move after the pointer is released and shows a refusal through <see cref="StructureNote"/>. A gesture never starts in a prefix document, during a save or with another
/// button, and a press in a table nested in a cell is the text's (the map holds no grid for it, and a press in its text does not begin a range). The cursor over a border is the horizontal resize arrow, over the handle of a
/// row the hand, over the margin of a column and the corner the arrow, otherwise the text cursor. There are no keys of their own here: the keyboard routes are the table commands, the properties card and Shift with an arrow.
/// </remarks>
internal sealed partial class PageView
{
    private static readonly Dictionary<StandardCursorType, Cursor> s_cursors = [];

    private bool _cellRangeActive;
    private (int Row, int Column)? _cellRangeFocus;

    private static Cursor CursorOf(StandardCursorType type)
    {
        if (!s_cursors.TryGetValue(type, out var cursor))
        {
            s_cursors[type] = cursor = new Cursor(type);
        }

        return cursor;
    }

    // ---- press -------------------------------------------------------------------------------------------------------------------

    private static TableDragKind BorderDragOf(ObjectHit.ColumnBorder border, int page)
    {
        var table = new TableRef(border.Grid.Story, border.Grid.BlockIndex);
        return border.Index == border.Grid.ColumnBorders.Count - 1 ? new TableEdgeDrag(table, border.Grid, page) : new ColumnBorderDrag(table, border.Grid, page, border.Index);
    }

    // A press in a cell of a top-level table: the range starts there, as long as the press is in the cell's own text (a table nested in the cell has no grid and no gesture).
    private void BeginCellRange(DocumentLayout layout, EditSession session, int page, float x, float y, ObjectHit.Cell cell, Point point, int count)
    {
        if (count != 1 || session.IsSaving || IsPrefixDocument || layout.HitTest(page, x, y) is not { } hit || session.Editor.TableOf(hit.Position) is not { } place)
        {
            return;
        }

        var table = new TableRef(cell.Grid.Story, cell.Grid.BlockIndex);
        if (place.Table.Equals(table))
        {
            BeginObjectDrag(session, new CellRangeDrag(table, cell.Grid, page, cell.Rect.Row, cell.Rect.Column), point, layout, count, rightButton: false);
        }
    }

    // ---- move --------------------------------------------------------------------------------------------------------------------

    // The pointer moved with a range started: while it is in the cell it began in the text drag goes on, in another cell the range is selected live. True when the move was taken.
    private bool MoveCellRange(CellRangeDrag range, Point point)
    {
        if (_tab.Editor is not { } session || !TryMapPoint(point, out var page, out var x, out var y) || page != range.Page || TableGestures.CellAt(range.Grid, x, y) is not { } cell)
        {
            return _cellRangeActive;
        }

        var focus = (cell.Row, cell.Column);
        if (!_cellRangeActive && focus == (range.Row, range.Column))
        {
            return false;
        }

        if (!_cellRangeActive || _cellRangeFocus != focus)
        {
            _cellRangeActive = true;
            _cellRangeFocus = focus;
            _ = session.SelectCells(range.Table, (range.Row, range.Column), focus, wholeMergedCells: true);
        }

        return true;
    }

    // ---- release -----------------------------------------------------------------------------------------------------------------

    // The commit of a gesture on a table, called once on release (a gesture that began when no save ran was ended by a save starting, so none runs now). True when the document changed.
    private bool CommitTableDrag(TableDragKind kind, ObjectDragEnd end)
    {
        if (_released || kind is CellRangeDrag || IsPrefixDocument || _tab.Editor is not { IsSaving: false } session || _scroller is null)
        {
            return false;
        }

        switch (kind)
        {
            case ColumnBorderDrag or TableEdgeDrag:
                return ResizeOf(kind, end.Start, end.End) is { Applied: not 0 } resize && WriteWidths(session, kind.Table, resize.Widths);
            case RowDrag row:
                return MoveRowTo(session, row, PageY(row.Page, end.End));
            default:
                return false;
        }
    }

    // The widths a width gesture gives, from the widths the page piece draws: null when the gesture changes none.
    private (int[] Widths, int Applied)? ResizeOf(TableDragKind kind, Point start, Point current)
    {
        if (_scroller is not { } scroller)
        {
            return null;
        }

        var drawn = TableGestures.DrawnWidths(kind.Grid);
        var twips = TableGestures.Twips(current.X - start.X, scroller.Scale);
        return kind switch
        {
            ColumnBorderDrag border => TableGestures.MoveBorder(drawn, border.Border, twips),
            TableEdgeDrag => TableGestures.MoveEdge(drawn, twips, MaxTotalTwips(kind)),
            _ => null,
        };
    }

    private int MaxTotalTwips(TableDragKind kind) =>
        _builtLayout is { } layout && layout.TryGetPage(kind.Page, out var page) && page is not null ? TableGestures.MaxTotalTwips(kind.Grid, page.Width) : TableRules.MaxTwips;

    // The vertical position of a point of the view in points of the page it is measured against (the page may be another one than the pointer is over: the rows clamp).
    private float PageY(int page, Point point)
    {
        var scroller = _scroller!;
        var rect = scroller.RectOf(page).Translate(new Vector(LeftShift(scroller), -_offsetY));
        return (float)((point.Y - rect.Y) / scroller.Scale);
    }

    private bool WriteWidths(EditSession session, TableRef table, int[] widths)
    {
        var before = session.ObjectSelection as CellSelection;
        var (anchor, focus) = (session.CellAnchor, session.CellFocus);
        var prefix = IsPrefixDocument;
        TableRefusal? refusal = null;
        var result = ApplyGuarded(session, ed =>
        {
            refusal = ed.RefusalForSetColumnWidths(table, widths, prefix);
            return refusal is null ? ed.SetColumnWidths(table, widths, CaretIn(ed, session, table), prefix) : null;
        });
        return Finish(session, result, refusal, before, anchor, focus, null);
    }

    private bool MoveRowTo(EditSession session, RowDrag drag, float y)
    {
        if (TableGestures.TargetRow(drag.Grid, y) is not { } target || target == drag.Row)
        {
            return false;
        }

        var prefix = IsPrefixDocument;
        TableRefusal? refusal = null;
        var landed = -1;
        var result = ApplyGuarded(session, ed =>
        {
            if (drag.Table.Find(_tab.Docx.Model.Body.Blocks) is not { } table)
            {
                refusal = TableRefusal.NoTable;
                return null;
            }

            // The editor counts rows among the rows of the grid (a row between that is no row of the table has none); the layout's numbers are the rows of the table.
            var map = TableGridMap.Of(table);
            var (from, to) = (map.OrdinalOf(drag.Row), map.OrdinalOf(target));
            if (from < 0 || to < 0)
            {
                refusal = TableRefusal.OutOfRange;
                return null;
            }

            refusal = ed.RefusalForMoveRow(drag.Table, drag.Row, to - from, prefix);
            landed = target;
            return refusal is null ? ed.MoveRow(drag.Table, drag.Row, to - from, prefix) : null;
        });
        return Finish(session, result, refusal, null, null, null, result is not null ? (drag.Table, landed) : null);
    }

    // The end of a gesture that wrote: a refusal is a note (a save that queued the write says nothing), the cells that were selected stay selected, a moved row is the selected row.
    private bool Finish(EditSession session, EditResult? result, TableRefusal? refusal, CellSelection? before, (int Row, int Column)? anchor, (int Row, int Column)? focus, (TableRef Table, int Row)? landed)
    {
        if (result is null)
        {
            if (session.BlockedReason is null)
            {
                StructureNote?.Invoke(refusal is { } reason ? TableWording.ReasonText(reason) : WriteStrings.NoteTableNoChange);
            }

            return false;
        }

        if (before is not null && anchor is { } a && focus is { } f)
        {
            _ = session.SelectCells(before.Table, a, f, wholeMergedCells: true);
        }
        else if (landed is { } moved)
        {
            _ = session.SelectRow(moved.Table, moved.Row);
        }

        return true;
    }

    // The caret of the session when it is in the table (the width write keeps it in the cell it was in), else none: the pointer made no caret move.
    private static TextPosition? CaretIn(TextEditor ed, EditSession session, TableRef table) =>
        session.Selection is { } selection && ed.TableOf(selection.End) is { } place && place.Table.Equals(table) ? selection.End : null;

    // ---- cursor ------------------------------------------------------------------------------------------------------------------

    // The cursor a table object under the pointer asks for, or null for the text cursor. Only a page that has a table is hit-tested, and nothing is read from the model.
    private StandardCursorType? ObjectCursorAt(Point point)
    {
        if (_mode != WriteView.Page || _story is not null || _scroller is not { } scroller || ActiveLayout() is not { } layout || _tab.Editor is { IsSaving: true } || !TryMapPoint(point, out var page, out var x, out var y)
            || !layout.TryGetPage(page, out var laid) || laid is null || IsPrefixDocument)
        {
            return null;
        }

        // The selected picture comes first: its handles and its body ask for their cursors (the id is never read).
        var tolerance = ObjectHandles.TolerancePoints((float)scroller.Scale);
        if (laid.Objects.Pictures.Count > 0 && _tab.Editor is { } session && PictureCursorAt(layout, session, page, x, y, tolerance) is { } picture)
        {
            return picture;
        }

        if (laid.Objects.Tables.Count == 0)
        {
            return null;
        }

        return layout.HitObject(page, x, y, tolerance, (ObjectId?)null, false) switch
        {
            ObjectHit.ColumnBorder border when border.Index > 0 => StandardCursorType.SizeWestEast,
            ObjectHit.Margin { Kind: TableMarginKind.Row } => StandardCursorType.Hand,
            ObjectHit.Margin => StandardCursorType.Arrow,
            _ => null,
        };
    }

    // ---- drawing -----------------------------------------------------------------------------------------------------------------

    // The guide of a width gesture and the drop line of a row gesture, over the page like the handles (the render pass reads and draws, it touches no visual). Nothing is made when no such gesture goes on.
    private void DrawTableGesture(DrawingContext context)
    {
        if (_released || _mode != WriteView.Page || _objectDrag is not { Phase: ObjectDragPhase.Dragging } drag || drag.Kind is not TableDragKind kind || kind is CellRangeDrag || _scroller is not { Count: > 0 } scroller)
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var page = Snap(scroller.RectOf(kind.Page).Translate(new Vector(LeftShift(scroller), -_offsetY)), scaling);
        using var clip = context.PushClip(page);
        var pen = new Pen(BrushOf("Raffica.Brush.Focus.Ring", Brushes.DodgerBlue), 1.5, new DashStyle([4, 3], 0));
        var grid = kind.Grid;
        switch (kind)
        {
            case ColumnBorderDrag or TableEdgeDrag when ResizeOf(kind, drag.Start, drag.Current) is { } resize:
                var border = kind is ColumnBorderDrag column ? column.Border : grid.ColumnBorders.Count - 1;
                var x = page.X + ((grid.ColumnBorders[border] + (resize.Applied / 20.0)) * scroller.Scale);
                context.DrawLine(pen, new Point(x, page.Y + (grid.Y * scroller.Scale)), new Point(x, page.Y + ((grid.Y + grid.Height) * scroller.Scale)));
                break;
            case RowDrag row when TableGestures.DropLineY(grid, row.Row, PageY(row.Page, drag.Current)) is { } line:
                var y = page.Y + (line * scroller.Scale);
                context.DrawLine(pen, new Point(page.X + (grid.X * scroller.Scale), y), new Point(page.X + ((grid.X + grid.Width) * scroller.Scale), y));
                break;
            default:
                break;
        }
    }
}
