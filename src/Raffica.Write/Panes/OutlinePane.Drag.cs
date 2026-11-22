using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Write.Commands;

namespace Raffica.Write.Panes;

/// <summary>
/// The drag of an outline row. A press on a row that moves past a small distance starts it; the pointer is captured by the list, a line shows the places the chapter may land (only legal ones:
/// <see cref="OutlineTree.DropTarget"/>), and letting go on a place moves the chapter with everything under it, as one history step, through the window (the plan is made again from the live model there).
/// Letting go anywhere else, Esc, or a rebuilt list cancels. Nothing of the drag (the line, the tree it reads) exists before the first drag.
/// </summary>
internal sealed partial class OutlinePane
{
    private const double DragDistance = 6;
    private const int MaxNameText = 300;

    private int _pressRow = -1;
    private Point _pressPoint;
    private bool _dragging;
    private int _dragRow;
    private IReadOnlyList<OutlineHeading>? _dragHeadings;
    private IPointer? _dragPointer;
    private Canvas? _overlay;
    private Border? _dropLine;
    private (int Row, DropPlace Place)? _drop;
    private Control? _dragSource;
    private TopLevel? _keyRoot;

    /// <summary>Whether a row is being dragged.</summary>
    internal bool IsDragging => _dragging;

    /// <summary>The line of the drop place; null until the first drag.</summary>
    internal Border? DropLine => _dropLine;

    /// <summary>Whether the line shows (a legal place is under the pointer).</summary>
    internal bool DropLineVisible => _dropLine is { IsVisible: true };

    /// <summary>The words of the place under the pointer ("Move heading A below B"), or null while there is none; also the name of the line for assistive technology.</summary>
    internal string? DropName { get; private set; }

    /// <summary>How many times the headings were read for a drag (0 before the first drag).</summary>
    internal int DragReads { get; private set; }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_dragging)
        {
            UpdateDrop(e.GetPosition(_list));
        }
        else if (_pressRow >= 0 && Math.Abs(e.GetPosition(_list).X - _pressPoint.X) + Math.Abs(e.GetPosition(_list).Y - _pressPoint.Y) >= DragDistance)
        {
            BeginDrag(e.Pointer);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressRow = -1;
        if (!_dragging)
        {
            return;
        }

        var drop = _drop;
        var dragged = _rows[_dragRow].Paragraph;
        var onto = drop is { } d ? _rows[d.Row].Paragraph : null;
        EndDrag();
        if (drop is { } place && onto is not null && _actions is { } actions)
        {
            var revision = _tab.Docx.Model.Edits.Revision;
            actions.Drop(dragged, onto, place.Place);

            // A move that happened leaves the dragged row selected and focused once the list has read again; a refused or queued one changes nothing here.
            _restoreFocus = _tab.Docx.Model.Edits.Revision != revision;
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelDrag();

    // Starts the drag of the pressed row: the headings are read once for the drag, and a list that is a moment behind an edit does not start one.
    private void BeginDrag(IPointer pointer)
    {
        var row = _pressRow;
        _pressRow = -1;
        if (row < 0 || row >= _rows.Count || _actions is not { } actions)
        {
            return;
        }

        var model = _tab.Docx.Model;
        IReadOnlyList<OutlineHeading> headings;
        DragReads++;
        try
        {
            headings = Reader(model);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return;
        }

        if (!SameRows(headings))
        {
            return;
        }

        // The chapter of this row cannot travel (a section break, a content control): say why, and do not drag. Its own place is no move, which is not a reason.
        var probe = OutlinePlanner.Drop(headings, model, row, row, DropPlace.Before);
        if (probe.Reason is not (OutlineReason.None or OutlineReason.CannotMoveThere))
        {
            actions.Note(StructureWording.OutlineReasonText(probe.Reason, WriteCommandIds.MoveDown));
            return;
        }

        _dragging = true;
        _dragRow = row;
        _dragHeadings = headings;
        _dragPointer = pointer;
        pointer.Capture(_list);
        EnsureDropLine();
        if (_list.ContainerFromIndex(row) is { } container)
        {
            _dragSource = container;
            container.Classes.Add("dragging");
        }

        if (TopLevel.GetTopLevel(this) is { } root)
        {
            _keyRoot = root;
            root.AddHandler(KeyDownEvent, OnDragKey, RoutingStrategies.Tunnel);
        }
    }

    // True when the headings are the rows of the list (the same paragraphs in the same order).
    private bool SameRows(IReadOnlyList<OutlineHeading> headings)
    {
        if (headings.Count != _rows.Count)
        {
            return false;
        }

        for (var i = 0; i < headings.Count; i++)
        {
            if (!ReferenceEquals(headings[i].Paragraph, _rows[i].Paragraph))
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureDropLine()
    {
        if (_dropLine is not null)
        {
            return;
        }

        _dropLine = new Border { Height = 2, IsVisible = false, IsHitTestVisible = false };
        _dropLine.Classes.Add("outline-drop");
        _overlay = new Canvas { IsHitTestVisible = false };
        _overlay.Children.Add(_dropLine);
        _body.Children.Add(_overlay);
    }

    // The place under the pointer: the row it is over, the upper half is before it and the lower half after it; above the first row and below the last one the ends. The line shows only
    // where the tree says the chapter can land.
    private void UpdateDrop(Point point)
    {
        if (_dragHeadings is not { } headings || _dropLine is null)
        {
            return;
        }

        var found = RowUnder(point.Y, out var top, out var height);
        if (found is not { } row)
        {
            HideDropLine();
            return;
        }

        var place = point.Y < top + (height / 2) ? DropPlace.Before : DropPlace.After;
        if (OutlineTree.DropTarget(headings, _tab.Docx.Model, _dragRow, row, place) < 0)
        {
            HideDropLine();
            return;
        }

        var level = headings[row].Entry.Level;
        var y = place == DropPlace.Before ? top : top + height;
        var indent = 4 + ((level - 1) * IndentPerLevel);
        Canvas.SetLeft(_dropLine, indent);
        Canvas.SetTop(_dropLine, y - 1);
        _dropLine.Width = Math.Max(8, _list.Bounds.Width - indent - 4);
        _dropLine.IsVisible = true;
        _drop = (row, place);
        var dragged = ClipName(headings[_dragRow].Entry.Text);
        var target = ClipName(headings[row].Entry.Text);
        DropName = WriteStrings.Fill(place == DropPlace.Before ? WriteStrings.OutlineDropAboveFormat : WriteStrings.OutlineDropBelowFormat, dragged, target);
        AutomationProperties.SetName(_dropLine, DropName);
    }

    // The words quote at most the 300 characters an entry holds.
    private static string ClipName(string text) => text.Length <= MaxNameText ? text : text[..MaxNameText];

    // The row at a height of the list (in list coordinates) among the rows that are on screen, and its top and height; the ends of the list belong to the first and the last row.
    private int? RowUnder(double y, out double top, out double height)
    {
        top = 0;
        height = 0;
        int? first = null;
        int? last = null;
        (double Top, double Height) firstBox = default;
        (double Top, double Height) lastBox = default;
        foreach (var child in _list.ItemsPanelRoot?.Children ?? [])
        {
            if (child is not ListBoxItem item || _list.IndexFromContainer(item) is not (>= 0 and var index) || item.TranslatePoint(default, _list) is not { } origin)
            {
                continue;
            }

            var box = (origin.Y, item.Bounds.Height);
            if (y >= box.Y - 1 && y <= box.Y + box.Height + 1)
            {
                (top, height) = box;
                return index;
            }

            if (first is null || index < first)
            {
                (first, firstBox) = (index, box);
            }

            if (last is null || index > last)
            {
                (last, lastBox) = (index, box);
            }
        }

        if (first is { } f && y < firstBox.Top && f == 0)
        {
            (top, height) = firstBox;
            return f;
        }

        if (last is { } l && y > lastBox.Top + lastBox.Height && l == _rows.Count - 1)
        {
            (top, height) = lastBox;
            return l;
        }

        return null;
    }

    private void HideDropLine()
    {
        _drop = null;
        DropName = null;
        if (_dropLine is not null)
        {
            _dropLine.IsVisible = false;
        }
    }

    // Esc ends the drag; nothing moves.
    private void OnDragKey(object? sender, KeyEventArgs e)
    {
        if (_dragging && e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelDrag();
        }
    }

    /// <summary>Ends a drag without moving anything (a no-op when none runs).</summary>
    internal void CancelDrag()
    {
        _pressRow = -1;
        if (_dragging)
        {
            EndDrag();
        }
    }

    private void EndDrag()
    {
        _dragging = false;
        _dragHeadings = null;
        HideDropLine();
        _dragSource?.Classes.Remove("dragging");
        _dragSource = null;
        if (_keyRoot is { } root)
        {
            root.RemoveHandler(KeyDownEvent, OnDragKey);
            _keyRoot = null;
        }

        if (_dragPointer is { } pointer)
        {
            _dragPointer = null;
            if (ReferenceEquals(pointer.Captured, _list))
            {
                pointer.Capture(null);
            }
        }
    }
}
