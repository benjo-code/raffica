using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Text;

/// <summary>
/// The pointer side of the text of one tab: it routes the mouse of the page view to the selection and to the links and sets the cursor
/// (a hand over a link that can be clicked, an I-beam over text, else the default). A click without a drag that starts and ends on the
/// same clickable link follows it; a double or triple click never does, and neither does a press that was dragged away. Press and
/// release on the scroll bars of the view are left to the bars.
/// </summary>
internal sealed class TextInteraction : IDisposable
{
    // Farther than this (in DIPs) from where the button went down, a press is a drag and no click.
    private const double ClickSlop = 4;

    private static Cursor? s_hand;
    private static Cursor? s_ibeam;

    private readonly PageView _view;
    private PageLink? _pressedLink;
    private Point _pressPoint;
    private bool _pressed;
    private bool _moved;
    private bool _disposed;

    public TextInteraction(PdfTab tab, PageView view, DialogQueue dialogs, Action<Uri>? launcher = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
        Selection = new SelectionController(tab, view);
        Links = new LinkController(tab, view, dialogs, launcher, time);
        view.Selection = Selection;
        view.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerExitedEvent, OnExited, RoutingStrategies.Bubble);
    }

    /// <summary>The hand cursor over a link.</summary>
    public static Cursor HandCursor => s_hand ??= new Cursor(StandardCursorType.Hand);

    /// <summary>The I-beam cursor over text.</summary>
    public static Cursor IBeamCursor => s_ibeam ??= new Cursor(StandardCursorType.Ibeam);

    public SelectionController Selection { get; }

    public LinkController Links { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        _view.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _view.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        _view.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        _view.RemoveHandler(InputElement.PointerExitedEvent, OnExited);
        if (ReferenceEquals(_view.Selection, Selection))
        {
            _view.Selection = null;
        }

        _view.Cursor = null;
        Selection.Dispose();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_view);
        if (!point.Properties.IsLeftButtonPressed || _view.IsOverScrollBar(point.Position))
        {
            return;
        }

        _pressed = true;
        _moved = false;
        _pressPoint = point.Position;

        // Only a single click can follow a link: the second click of a double click never does.
        _pressedLink = e.ClickCount == 1 ? Links.LinkAt(point.Position) : null;
        Selection.Press(point.Position, e.ClickCount);
        e.Pointer.Capture(_view);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(_view);
        if (_pressed)
        {
            if (!_moved && Distance(position, _pressPoint) > ClickSlop)
            {
                _moved = true;
            }

            Selection.Drag(position);
            return;
        }

        if (_view.IsOverScrollBar(position))
        {
            SetCursor(null);
            return;
        }

        SetCursor(Links.LinkAt(position) is not null ? HandCursor : Selection.IsOverText(position) ? IBeamCursor : null);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        var position = e.GetPosition(_view);
        var link = _pressedLink;
        var click = !_moved && link is not null && ReferenceEquals(Links.LinkAt(position), link);
        End();
        e.Pointer.Capture(null);
        if (click)
        {
            _ = FollowAsync(link!);
        }
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pressed)
        {
            End();
        }
    }

    private void OnExited(object? sender, PointerEventArgs e)
    {
        if (!_pressed)
        {
            SetCursor(null);
        }
    }

    private void End()
    {
        _pressed = false;
        _pressedLink = null;
        Selection.Release();
    }

    // An exception while following a link must not take the app down (nobody awaits this).
    private async Task FollowAsync(PageLink link)
    {
        try
        {
            await Links.ActivateAsync(link);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The link does nothing.
        }
    }

    private void SetCursor(Cursor? cursor)
    {
        if (!ReferenceEquals(_view.Cursor, cursor))
        {
            _view.Cursor = cursor;
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
