using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// F6 and Shift+F6 pane navigation of one window. The ring runs through the panes in the order given (navigation, document, then
/// comments or format), skips closed ones, and moves the keyboard focus to the first focusable control of the pane. While it
/// runs the pane is marked (<see cref="Pane.IsPaneFocused"/>) and the status bar shows "Focus: pane". A pointer press anywhere
/// and Esc end the navigation; closing the pane that holds the focus moves it to the document pane. While a
/// <see cref="DialogHost"/> of the window shows a dialog F6 is consumed and does nothing.
/// </summary>
public sealed class PaneFocus
{
    private readonly Window _window;
    private readonly IReadOnlyList<Pane> _panes;
    private readonly StatusBar _status;
    private int? _current;

    public PaneFocus(Window window, IReadOnlyList<Pane> panes, StatusBar status)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(panes);
        ArgumentNullException.ThrowIfNull(status);
        _window = window;
        _panes = panes;
        _status = status;

        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        foreach (var pane in panes)
        {
            pane.PropertyChanged += (_, e) =>
            {
                if (e.Property == Pane.IsOpenProperty)
                {
                    OnPaneOpenChanged(pane);
                }
            };
        }
    }

    /// <summary>Whether F6 navigation is active (a pane is marked and the status bar shows it).</summary>
    public bool IsNavigating => _current is not null;

    /// <summary>
    /// Moves to the next pane (the previous one when <paramref name="backwards"/>) and focuses its first focusable control.
    /// Returns false while a dialog is open or when no pane can take the focus.
    /// </summary>
    public bool Move(bool backwards)
    {
        if (IsDialogOpen())
        {
            return false;
        }

        var items = (IReadOnlyList<IPaneItem>)_panes;
        var from = _current;
        for (var attempts = 0; attempts < _panes.Count; attempts++)
        {
            var next = backwards ? PaneRing.Previous(items, from) : PaneRing.Next(items, from);
            if (next is null)
            {
                return false;
            }

            if (FocusPane(next.Value))
            {
                return true;
            }

            // A pane with nothing to focus is passed over; the walk continues from it.
            from = next;
        }

        return false;
    }

    /// <summary>
    /// Moves the focus into <paramref name="pane"/> (as F6 would) and marks it. Returns false while a dialog is open, for a pane of
    /// another ring, or when the pane has nothing to focus.
    /// </summary>
    public bool Focus(Pane pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        var index = -1;
        for (var i = 0; i < _panes.Count; i++)
        {
            if (ReferenceEquals(_panes[i], pane))
            {
                index = i;
            }
        }

        return index >= 0 && !IsDialogOpen() && FocusPane(index);
    }

    /// <summary>Ends the navigation: no pane is marked and the status cell is empty. The keyboard focus stays where it is.</summary>
    public void End()
    {
        if (_current is { } index)
        {
            _panes[index].IsPaneFocused = false;
        }

        _current = null;
        _status.FocusPane = null;
    }

    private bool FocusPane(int index)
    {
        var pane = _panes[index];
        var target = pane.GetVisualDescendants().OfType<InputElement>()
            .FirstOrDefault(e => e.Focusable && e.IsTabStop && e.IsEffectivelyVisible && e.IsEffectivelyEnabled)
            ?? (pane.Focusable && pane.IsEffectivelyVisible && pane.IsEffectivelyEnabled ? pane : null);
        if (target is null || !target.Focus(NavigationMethod.Tab))
        {
            return false;
        }

        if (_current is { } previous && previous != index)
        {
            _panes[previous].IsPaneFocused = false;
        }

        _current = index;
        pane.IsPaneFocused = true;
        _status.FocusPane = ShellStrings.FocusPane(((IPaneItem)pane).Name);
        return true;
    }

    private bool IsDialogOpen() => FrameDialogs.IsOpen(_window);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F6 && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            e.Handled = true;
            Move((e.KeyModifiers & KeyModifiers.Shift) != 0);
        }
        else if (e.Key == Key.Escape && IsNavigating)
        {
            End();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsNavigating)
        {
            End();
        }
    }

    private void OnPaneOpenChanged(Pane pane)
    {
        if (pane.IsOpen || _current is not { } index || !ReferenceEquals(_panes[index], pane))
        {
            return;
        }

        // The pane that held the focus is gone: hand the focus to the document pane (else to whatever the ring offers).
        pane.IsPaneFocused = false;
        _current = null;
        var document = -1;
        for (var i = 0; i < _panes.Count; i++)
        {
            if (_panes[i].IsDocument && ((IPaneItem)_panes[i]).IsAvailable)
            {
                document = i;
                break;
            }
        }

        if (document >= 0 && FocusPane(document))
        {
            return;
        }

        if (!Move(backwards: false))
        {
            End();
        }
    }
}
