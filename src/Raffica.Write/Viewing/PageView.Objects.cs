using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write.Viewing;

/// <summary>
/// Pictures and table cells as objects of the page view (milestone 4.5a): a press on a picture selects it and shows its handles, a press on a margin of a table selects a row, a column or the table, Shift and a
/// click or an arrow extends a cell range, Delete with a picture selected hands it to <see cref="DeletePicture"/>. The pointer gesture on an object is the <see cref="ObjectDrag"/> machine.
/// </summary>
/// <remarks>
/// Rules. Objects belong to the pages view and the body: the writing view and a header or footer in edit select none. Nothing exists before the first object is hit: no selection, no machine and no overlay work
/// (the overlay draws nothing without an object selection), and only a press hit-tests (a pointer move reads no model: the session holds the selected drawing, <see cref="EditSession.SelectedDrawing"/>, and the layout's
/// hit test compares drawings by object, never by <see cref="PictureBox.Id"/>; the id is read once, on the click that selects). The overlay (selection tint, ring, handles, the outline of a drag) is drawn
/// over the pages by <see cref="EditOverlay"/> and never enters a <see cref="LaidOutPage"/>; the render pass touches no visual. A drag starts only when <see cref="ObjectDragCommit"/> is set (a task
/// that gives a gesture a meaning sets it) and no save runs; it changes the document once, in that callback, on release. It ends with no change on Escape, a lost capture, a save starting, an edit,
/// another document or layout, a change of the view's size and a mode switch. Esc: a drag in progress first, then a plain object selection, then the other roles of Esc.
/// </remarks>
internal sealed partial class PageView
{
    private ObjectDrag? _objectDrag;
    private double _gestureScale;
    private double _gestureOffsetX;
    private IPointer? _objectPointer;
    private DocumentLayout? _objectLayout;

    /// <summary>
    /// The callback that commits a drag on an object when the pointer is released (kind, start and end in DIPs of the view); true when it changed the document. While it is null a press on an object only selects it.
    /// Called once per gesture and never during it.
    /// </summary>
    internal Func<ObjectDragKind, ObjectDragEnd, bool>? ObjectDragCommit { get; set; }

    /// <summary>
    /// The operation that removes the selected picture; true when it did. The view fills it with <see cref="DeleteSelectedPicture"/> (one history step of the session, like a key); a window or a test may replace it. Delete and Backspace with a
    /// picture selected call it; without one the keys do nothing.
    /// </summary>
    internal Func<PictureSelection, bool>? DeletePicture { get; set; }

    // The registered deleter: the same path as a key (the session is made on first use, a save queues the operation, a stale id is refused by the editor), one history step.
    private bool DeleteSelectedPicture(PictureSelection selection)
    {
        if (BlockReason() is { } reason and not EditBlockedReason.Saving)
        {
            InputRefused?.Invoke(reason);
            return false;
        }

        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        var prefix = IsPrefixDocument;
        return ApplyGuarded(session, ed => ed.DeletePicture(selection.Id, prefix)) is not null;
    }

    /// <summary>The selected object of the tab's session, or null (also before the session exists).</summary>
    internal ObjectSelection? ObjectSelection => _tab.Editor?.ObjectSelection;

    /// <summary>The gesture machine; null until the first press that could start a drag.</summary>
    internal ObjectDrag? ObjectGesture => _objectDrag;

    /// <summary>The pointer that holds the capture for a gesture in progress (tests take the capture away through it).</summary>
    internal IPointer? ObjectPointer => _objectPointer;

    /// <summary>The screen reader text of the selected object (the alt text of a picture, the place of a cell range); null without one.</summary>
    internal string? SelectedObjectAnnouncement => _tab.Editor?.DescribeObjectSelection();

    // Press.

    // A press at a point of a page. True when it was taken by an object (the text press is then skipped); false when it is for the text, which ends an object selection.
    private bool TryPressObject(DocumentLayout layout, EditSession session, int page, float x, float y, Point point, int count, KeyModifiers modifiers, bool rightButton)
    {
        if (_mode != WriteView.Page || _story is not null || _scroller is not { } scroller || !layout.TryGetPage(page, out var laid) || laid is null || laid.Objects.IsEmpty)
        {
            session.SetObjectSelection(null);
            return false;
        }

        var tolerance = ObjectHandles.TolerancePoints((float)scroller.Scale);
        var hit = Find(layout, session, page, x, y, tolerance, includeBehindText: false);
        if (hit is null && laid.Objects.Pictures.Any(p => p.BehindText) && Find(layout, session, page, x, y, tolerance, includeBehindText: true) is ObjectHit.Picture behind && !TextAt(layout, page, x, y))
        {
            // A picture behind the text is selected only where there is no text to put the caret in.
            hit = behind;
        }

        var taken = false;
        switch (hit)
        {
            case ObjectHit.Handle handle:
                taken = true;
                RememberPicturePress(handle.Box, page, x, y);
                BeginObjectDrag(session, new PictureResize(handle.Slot), point, layout, count, rightButton);
                break;
            case ObjectHit.Picture picture:
                if (picture.Box.Id is { } id && session.SetObjectSelection(new PictureSelection(id)))
                {
                    taken = true;

                    // Only a floating picture is dragged. An inline one has no move in this milestone (a picture is not copied or cut, see NotePictureNotCopied), so a press on it only selects it.
                    if (picture.Box.Floating)
                    {
                        RememberPicturePress(picture.Box, page, x, y);
                        BeginObjectDrag(session, new PictureMove(), point, layout, count, rightButton);
                    }
                }

                break;
            case ObjectHit.ColumnBorder border when border.Index > 0:
                // An inner border or the right edge is a gesture; the left outer edge is not (the press is for the text there). A press that starts none (a save, a prefix document, the right button, a double click) is the text's too.
                taken = BeginObjectDrag(session, BorderDragOf(border, page), point, layout, count, rightButton);
                break;
            case ObjectHit.Margin margin:
                var table = new TableRef(margin.Grid.Story, margin.Grid.BlockIndex);
                taken = margin.Kind switch
                {
                    TableMarginKind.Row => session.SelectRow(table, margin.Index),
                    TableMarginKind.Column => session.SelectColumn(table, margin.Index),
                    _ => session.SelectTable(table),
                };
                if (taken && margin.Kind == TableMarginKind.Row)
                {
                    BeginObjectDrag(session, new RowDrag(table, margin.Grid, page, margin.Index), point, layout, count, rightButton);
                }

                break;
            case ObjectHit.Cell cell when !rightButton && modifiers.HasFlag(KeyModifiers.Shift):
                taken = ExtendCells(layout, session, cell);
                break;
            case ObjectHit.Cell cell when !rightButton:
                // The press is the text's (the caret goes into the cell); a drag into another cell turns it into a range of cells.
                BeginCellRange(layout, session, page, x, y, cell, point, count);
                break;
            default:
                break;
        }

        if (!taken)
        {
            session.SetObjectSelection(null);
        }

        return taken;
    }

    private static ObjectHit? Find(DocumentLayout layout, EditSession session, int page, float x, float y, float tolerance, bool includeBehindText) =>
        session.SelectedDrawing is { } drawing
            ? layout.HitObject(page, x, y, tolerance, drawing, includeBehindText)
            : layout.HitObject(page, x, y, tolerance, (ObjectId?)null, includeBehindText);

    // True when a line of text is under the point (not only the nearest line, which the layout's hit test gives for any point of the page).
    private static bool TextAt(DocumentLayout layout, int page, float x, float y)
    {
        if (layout.HitTest(page, x, y) is not { } hit)
        {
            return false;
        }

        var paragraph = TextUnits.ParagraphAt(hit.Position, ProjectionOf(layout, hit.Position));
        foreach (var box in layout.SelectionBoxes(paragraph, page))
        {
            if (x >= box.X && x <= box.X + box.Width && y >= box.Y && y <= box.Y + box.Height)
            {
                return true;
            }
        }

        return false;
    }

    // Shift and a click on a cell: the range from the cell where it began (the anchor of a cell selection, else the cell of the caret) to this one. A cell of another table than the anchor's is no range.
    private bool ExtendCells(DocumentLayout layout, EditSession session, ObjectHit.Cell cell)
    {
        var table = new TableRef(cell.Grid.Story, cell.Grid.BlockIndex);
        (int Row, int Column)? anchor = null;
        if (session.ObjectSelection is CellSelection existing && existing.Table.Equals(table))
        {
            anchor = session.CellAnchor;
        }
        else if (CellAtCaret(layout, session) is { } caretCell && caretCell.Grid.BlockIndex == cell.Grid.BlockIndex && caretCell.Grid.Story == cell.Grid.Story)
        {
            anchor = (caretCell.Rect.Row, caretCell.Rect.Column);
        }

        return anchor is { } corner && session.SelectCells(table, corner, (cell.Rect.Row, cell.Rect.Column), wholeMergedCells: true);
    }

    private ObjectHit.Cell? CellAtCaret(DocumentLayout layout, EditSession session)
    {
        if (session.Selection is not { } selection || layout.Caret(selection.End, PageHint) is not { } box)
        {
            return null;
        }

        return layout.HitObject(box.Page, box.X + 0.5f, box.Top + (box.Height / 2f), 0f) as ObjectHit.Cell;
    }

    private bool BeginObjectDrag(EditSession session, ObjectDragKind kind, Point point, DocumentLayout layout, int count, bool rightButton)
    {
        // A gesture on a table or a picture has its own commit (the view's); the callback of ObjectDragCommit, when set, takes the picture kinds instead. A prefix document and a save make no gesture.
        if (rightButton || count != 1 || session.IsSaving || (kind is TableDragKind or PictureMove or PictureResize && IsPrefixDocument))
        {
            return false;
        }

        if (_objectDrag is null)
        {
            _objectDrag = new ObjectDrag();
            _objectDrag.Changed += OnObjectDragChanged;
        }

        if (!_objectDrag.Press(kind, point))
        {
            return false;
        }

        _objectLayout = layout;
        (_gestureScale, _gestureOffsetX) = (_scroller?.Scale ?? 0, _offsetX);
        return true;
    }

    private void OnObjectDragChanged() => _overlay.InvalidateVisual();

    // The press that began a gesture takes the pointer so that the move and the release come here even outside the view.
    private void CaptureObjectPointer(IPointer pointer)
    {
        if (_objectDrag is { IsActive: true })
        {
            pointer.Capture(this);
            _objectPointer = pointer;
        }
    }

    // Move, release, cancel.

    private bool MoveObjectDrag(Point point)
    {
        if (_objectDrag is not { IsActive: true } drag)
        {
            return false;
        }

        if (ViewMovedUnder(drag))
        {
            CancelObjectDrag(ObjectDragCancel.ViewChanged);
            return true;
        }

        drag.Move(point);
        if (drag.Kind is PictureMove && drag.Phase == ObjectDragPhase.Dragging)
        {
            UpdatePictureDrop(point);
        }

        return drag.Kind is CellRangeDrag range ? drag.Phase == ObjectDragPhase.Dragging && MoveCellRange(range, point) : true;
    }

    // A width gesture measures the pointer against the scale and the horizontal offset of its press: a zoom or a sideways scroll in the middle of it (Ctrl and the wheel) would give a wrong width, so it ends.
    private bool ViewMovedUnder(ObjectDrag drag) =>
        drag.Kind is ColumnBorderDrag or TableEdgeDrag or PictureResize or PictureMove && (_scroller is not { } scroller || scroller.Scale != _gestureScale || _offsetX != _gestureOffsetX);

    internal bool ReleaseObjectDrag(Point point)
    {
        if (_objectDrag is not { IsActive: true } drag)
        {
            return false;
        }

        if (ViewMovedUnder(drag))
        {
            CancelObjectDrag(ObjectDragCancel.ViewChanged);
            return true;
        }

        var gesture = drag.Kind;
        try
        {
            _ = drag.Release(point, (kind, end) => kind is TableDragKind table ? CommitTableDrag(table, end) : ObjectDragCommit is { } commit ? commit(kind, end) : CommitPictureDrag(kind, end));
        }
        finally
        {
            // The capture goes back also when the commit of a gesture threw.
            EndObjectPointer();
        }

        // The text press that came with a range of cells goes on after it (it ends its own drag).
        return gesture is not CellRangeDrag;
    }

    /// <summary>Ends a gesture in progress with no change (Escape, a save, an edit, a changed view ...); does nothing when none runs.</summary>
    internal void CancelObjectDrag(ObjectDragCancel reason)
    {
        if (_objectDrag is not { IsActive: true } drag)
        {
            return;
        }

        var range = drag.Kind is CellRangeDrag;
        var selecting = _cellRangeActive;
        drag.Cancel(reason);
        EndObjectPointer();
        if (range)
        {
            // The press began a text drag as well, and the cells selected so far are not kept.
            EndDrag();
            if (selecting)
            {
                _tab.Editor?.SetObjectSelection(null);
            }
        }
    }

    private void EndObjectPointer()
    {
        if (_objectPointer is { } pointer)
        {
            _objectPointer = null;
            if (ReferenceEquals(pointer.Captured, this))
            {
                pointer.Capture(null);
            }
        }

        _objectLayout = null;
        _cellRangeActive = false;
        _cellRangeFocus = null;
        _drop = null;
        _pictureBox = null;

        // The cursor a picture drag set goes with the gesture.
        if (!_released)
        {
            UpdateCursor(_lastPointer);
        }
    }

    private void OnObjectCaptureLost()
    {
        _objectPointer = null;
        CancelObjectDrag(ObjectDragCancel.CaptureLost);
    }

    private void OnSaveStarted() => CancelObjectDrag(ObjectDragCancel.SaveStarted);

    private void OnObjectSelectionChanged()
    {
        if (_released)
        {
            return;
        }

        _overlay.InvalidateVisual();
        UpdateAutomationName();
        PublishSelectionState();
    }

    // The runner changed what it lays out: a gesture on a layout that is gone, or a selection of another document, ends.
    private void CheckObjectState()
    {
        if (_objectDrag is { IsActive: true } && !ReferenceEquals(_objectLayout, _builtLayout))
        {
            CancelObjectDrag(ObjectDragCancel.DocumentChanged);
        }

        _tab.Editor?.EndObjectSelectionOfAnotherDocument();
    }

    // Keys.

    // Esc: a drag in progress first, then a plain object selection. True when it was taken.
    private bool HandleObjectEscape()
    {
        if (_objectDrag is { IsActive: true })
        {
            CancelObjectDrag(ObjectDragCancel.Escape);
            return true;
        }

        if (_tab.Editor is { ObjectSelection: not null } session)
        {
            session.SetObjectSelection(null);
            return true;
        }

        return false;
    }

    // Keys that act on a selected object: Delete and Backspace, and Shift with an arrow for a cell range. True when the key was taken (a key that is not one of them ends the object selection where it edits text).
    private bool HandleObjectKey(KeyEventArgs e, bool repeat, ObjectSelection selection)
    {
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Delete or Key.Back)
        {
            if (!repeat && selection is PictureSelection picture)
            {
                _ = DeletePicture?.Invoke(picture);
            }
            else if (!repeat && selection is CellSelection)
            {
                // Delete with cells selected would edit the text of one cell by surprise, and clearing the cells is no feature of this milestone: the key is claimed and says where the rows and columns go.
                StructureNote?.Invoke(WriteStrings.NoteCellsDeleteKey);
            }

            return true;
        }

        if (selection is PictureSelection selectedPicture && e.Key is Key.Left or Key.Right or Key.Up or Key.Down && NudgeSelectedPicture(selectedPicture, e.Key, e.KeyModifiers, repeat))
        {
            return true;
        }

        if (selection is CellSelection && e.KeyModifiers == KeyModifiers.Shift && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var (rows, columns) = e.Key switch
            {
                Key.Left => (0, -1),
                Key.Right => (0, 1),
                Key.Up => (-1, 0),
                _ => (1, 0),
            };
            _ = _tab.Editor?.ExtendCellSelection(rows, columns);
            return true;
        }

        return false;
    }

    // Drawing (the overlay only; it reads, it changes no visual).

    private void DrawObjects(DrawingContext context)
    {
        if (_released || _mode != WriteView.Page || _tab.Editor is not { ObjectSelection: { } selection } session || _scroller is not { Count: > 0 } scroller || _builtLayout is not { } layout)
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var ring = BrushOf("Raffica.Brush.Focus.Ring", Brushes.DodgerBlue);
        var face = BrushOf("Raffica.Brush.Paper.OnSelection", Brushes.White);
        var tint = BrushOf("Raffica.Brush.Paper.Selection", Brushes.SteelBlue);
        var pen = new Pen(ring, 2);
        var handlePen = new Pen(ring, 1);
        var (first, last) = scroller.Visible(_offsetY, _viewport.Height);
        var left = LeftShift(scroller);
        for (var index = first; index <= last && index < scroller.Count; index++)
        {
            if (!layout.TryGetPage(index, out var laid) || laid is null || laid.Objects.IsEmpty)
            {
                continue;
            }

            var page = Snap(scroller.RectOf(index).Translate(new Vector(left, -_offsetY)), scaling);
            using var clip = context.PushClip(page);
            if (selection is PictureSelection && session.SelectedDrawing is { } drawing)
            {
                foreach (var box in laid.Objects.Pictures)
                {
                    if (!box.Shows(drawing))
                    {
                        continue;
                    }

                    var rect = new Rect(page.X + (box.X * scroller.Scale), page.Y + (box.Y * scroller.Scale), box.Width * scroller.Scale, box.Height * scroller.Scale);
                    context.DrawRectangle(null, pen, rect);
                    DrawDragOutline(context, rect, ring, scroller.Scale);
                    if (!box.Resizable)
                    {
                        continue;
                    }

                    foreach (var handle in ObjectHandles.Slots(box, (float)scroller.Scale))
                    {
                        context.DrawRectangle(face, handlePen, handle.Square.Translate(new Vector(page.X, page.Y)));
                    }
                }
            }
            else if (selection is CellSelection cells)
            {
                using var opacity = context.PushOpacity(SelectionOpacity);
                foreach (var grid in laid.Objects.Tables)
                {
                    if (grid.Story != cells.Table.Story || grid.BlockIndex != cells.Table.BlockIndex)
                    {
                        continue;
                    }

                    foreach (var cell in grid.Cells)
                    {
                        if (cells.Range.Overlaps(cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan))
                        {
                            context.FillRectangle(tint, new Rect(page.X + (cell.X * scroller.Scale), page.Y + (cell.Y * scroller.Scale), cell.Width * scroller.Scale, cell.Height * scroller.Scale));
                        }
                    }
                }
            }
        }

        DrawPictureDrop(context, scroller, layout, tint);
    }

    // The outline of the picture as the gesture in progress would leave it: dashed, drawn over the page, never part of it.
    private void DrawDragOutline(DrawingContext context, Rect box, IBrush ring, double scale)
    {
        if (_objectDrag is not { Phase: ObjectDragPhase.Dragging } drag)
        {
            return;
        }

        var (dx, dy) = (drag.Current.X - drag.Start.X, drag.Current.Y - drag.Start.Y);
        Rect? outline = drag.Kind switch
        {
            PictureMove => box.Translate(new Vector(dx, dy)),
            PictureResize resize => ResizeOutline(box, resize.Slot, dx, dy, scale),
            _ => null,
        };
        if (outline is { } rect)
        {
            context.DrawRectangle(null, new Pen(ring, 1.5, new DashStyle([4, 3], 0)), rect);
        }
    }

    // The frame of a resize as the commit will write it (the same arithmetic, in points), drawn in DIPs of the view.
    private Rect ResizeOutline(Rect box, HandleSlot slot, double dx, double dy, double scale)
    {
        var frame = PictureGestures.Resize(new Rect(box.X / scale, box.Y / scale, box.Width / scale, box.Height / scale), slot, dx / scale, dy / scale, _pointerModifiers.HasFlag(KeyModifiers.Shift));
        return new Rect(frame.X * scale, frame.Y * scale, frame.Width * scale, frame.Height * scale);
    }
}
