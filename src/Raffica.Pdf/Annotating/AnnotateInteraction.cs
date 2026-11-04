using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Text;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The pointer side of the annotate mode of one tab. While a tool is active it takes the mouse of the page view (the text interaction yields,
/// see <see cref="TextInteraction"/>) and routes it to the <see cref="IToolController"/> of the tool; it is the overlay of the view and lets
/// the active controller draw. Esc drops what the controller holds (a gesture, a selection) and only then ends the mode. The routing is
/// the one place later tools plug into: one controller per tool family.
/// </summary>
internal sealed class AnnotateInteraction : IPageOverlay, IDisposable
{
    private readonly PageView _view;
    private readonly ToolState _tools;
    private readonly List<IToolController> _controllers;
    private IToolController? _current;
    private IToolController? _pressedBy;
    private bool _disposed;

    public AnnotateInteraction(PdfTab tab, PageView view, ToolState tools, SelectionController selection, DialogQueue dialogs, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(dialogs);
        _view = view;
        _tools = tools;
        Markup = new MarkupController(tab, view, tools, selection, dialogs, time);
        Drawing = new DrawingController(tab, view, tools, dialogs);
        TextBox = new TextBoxController(tab, view, tools, dialogs);
        _controllers = [Markup, Drawing, TextBox];
        foreach (var controller in _controllers)
        {
            controller.Changed += OnControllerChanged;
        }

        _current = Find(tools.Tool);
        tools.Changed += OnToolChanged;
        view.Overlay = this;
        view.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>The controller of the text markup tools.</summary>
    public MarkupController Markup { get; }

    /// <summary>The controller of the drawing tools.</summary>
    public DrawingController Drawing { get; }

    /// <summary>The controller of the text box tool (its editing field also serves the select tool).</summary>
    public TextBoxController TextBox { get; }

    /// <inheritdoc />
    public void Render(DrawingContext context, PageView view) => _current?.Render(context, view);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tools.Changed -= OnToolChanged;
        _view.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        _view.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _view.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        _view.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        _view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        if (ReferenceEquals(_view.Overlay, this))
        {
            _view.Overlay = null;
        }

        foreach (var controller in _controllers)
        {
            controller.Changed -= OnControllerChanged;
            controller.Dispose();
        }
    }

    private IToolController? Find(AnnotateTool tool) => tool == AnnotateTool.None ? null : _controllers.Find(c => c.Handles(tool));

    private void OnControllerChanged() => Changed?.Invoke();

    private void OnToolChanged()
    {
        // Whatever the old tool held (a gesture, a selection) goes with it. A text in the editing field is not lost: it is made first
        // (the field is open for the text box tool and for the select tool alike).
        _pressedBy = null;
        TextBox.CommitOrDrop();
        _current?.Cancel();
        _current = Find(_tools.Tool);
        if (_tools.Tool == AnnotateTool.None)
        {
            _view.Cursor = null;
        }

        Changed?.Invoke();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_tools.Tool == AnnotateTool.None || Find(_tools.Tool) is not { } controller)
        {
            return;
        }

        // A press in the editing field belongs to the field (placing the caret, selecting text).
        if (_view.IsInField(e.Source))
        {
            return;
        }

        // Touch scrolls and zooms the page; only the mouse and the pen work on it (the pen is captured, so it does not scroll).
        if (e.Pointer.Type == PointerType.Touch)
        {
            return;
        }

        var point = e.GetCurrentPoint(_view);
        if (!point.Properties.IsLeftButtonPressed || _view.IsOverScrollBar(point.Position))
        {
            return;
        }

        _current = controller;
        _pressedBy = controller;
        controller.Press(point.Position, e.ClickCount, e.KeyModifiers);
        e.Pointer.Capture(_view);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_tools.Tool == AnnotateTool.None)
        {
            return;
        }

        var position = e.GetPosition(_view);
        if (_pressedBy is { } pressed)
        {
            pressed.Move(position, e.KeyModifiers);
            return;
        }

        var cursor = _view.IsOverScrollBar(position) ? null : _current?.CursorAt(position);
        if (!ReferenceEquals(_view.Cursor, cursor))
        {
            _view.Cursor = cursor;
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedBy is not { } pressed || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        _pressedBy = null;
        pressed.Release(e.GetPosition(_view), e.KeyModifiers);
        e.Pointer.Capture(null);
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pressedBy is { } pressed)
        {
            // The pointer was taken away (a dialog opened, the window lost the mouse): the gesture ends where it stands.
            _pressedBy = null;
            pressed.Cancel();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled)
        {
            HandleKey(e);
        }
    }

    /// <summary>
    /// Takes a key while the mode is on. The active controller sees it first; Esc then drops what the controller holds (a gesture in
    /// progress, a drag, a selection) and only when nothing was held ends the mode. The caller has checked that no dialog is open and that
    /// no text field or the search bar owns the key. Sets <c>Handled</c> when the key was used.
    /// </summary>
    /// <param name="e">The key event.</param>
    /// <returns>Whether the key was used.</returns>
    public bool HandleKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_tools.Tool == AnnotateTool.None)
        {
            return false;
        }

        if (Find(_tools.Tool) is { } controller && controller.Key(e))
        {
            e.Handled = true;
            return true;
        }

        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
        {
            return false;
        }

        _pressedBy = null;
        if (_current is null || !_current.Cancel())
        {
            _tools.End();
        }

        e.Handled = true;
        return true;
    }
}
