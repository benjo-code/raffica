using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// Connects the Alt field-code mode of a window to the keyboard. It feeds the Alt keys, F10, Esc and the typed letters and digits
/// into a <see cref="FieldCodeSession"/> and shows its state: the badges of the <see cref="RibbonView"/> and of every control that
/// carries a <see cref="FieldCodeBadge.CodeProperty"/> code, and the hint in the <see cref="StatusBar"/>. The session decides what a
/// key means; this class only knows how the keys really arrive:
/// <list type="bullet">
/// <item>AltGr on Windows is Ctrl plus Alt, so Alt with Ctrl held never enters the mode. AltGr on Linux sends no Ctrl; there the
/// typed character (a text input between Alt down and Alt up) counts as another key.</item>
/// <item>A held Alt repeats its key-down; only the first one counts.</item>
/// <item>Both Alt events are marked handled when the session consumes them (so the Windows system menu does not open); Alt with
/// another key is an ordinary shortcut and nothing is consumed.</item>
/// <item>A letter consumed by the session also produces a text input that must not reach a text box; it is swallowed.</item>
/// <item>A pointer press, the window losing the focus, a tab change by pointer, a dialog opening and a key that another handler of
/// the window already took (F6, Ctrl+K) end the mode.</item>
/// </list>
/// </summary>
public sealed class FieldCodeInput : IDisposable
{
    private readonly Window _window;
    private readonly FieldCodeSession _session;
    private readonly RibbonView _ribbon;
    private readonly StatusBar _status;
    private readonly IDisposable _dialogWatch;
    private readonly List<FieldCodeBadge> _badges = [];
    private readonly List<FieldCodeBadge> _menuBadges = [];
    private MenuFlyout? _menu;
    private CommandDefinition? _menuCommand;
    private FlyoutShowMode _menuShowMode;
    private bool _altHeld;
    private bool _altDownConsumed;
    private bool _swallowText;
    private bool _altBlocked;
    private bool _f10Consumed;
    private bool _settingTab;
    private bool _disposed;

    private FieldCodeInput(Window window, FieldCodeSession session, RibbonView ribbon, StatusBar status)
    {
        _window = window;
        _session = session;
        _ribbon = ribbon;
        _status = status;

        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.Deactivated += OnDeactivated;
        ribbon.PropertyChanged += OnRibbonPropertyChanged;
        session.Changed += OnChanged;
        session.TabSelected += OnTabSelected;
        session.CommandRun += OnCommandRun;
        _dialogWatch = DialogHost.ActiveDialogProperty.Changed.Subscribe(new DialogObserver(this));
    }

    /// <summary>
    /// Starts feeding <paramref name="window"/> into <paramref name="session"/>. Disposing the result removes every handler and
    /// badge again (the keys are ordinary keys afterwards).
    /// </summary>
    public static IDisposable Attach(Window window, FieldCodeSession session, RibbonView ribbon, StatusBar status)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ribbon);
        ArgumentNullException.ThrowIfNull(status);
        return new FieldCodeInput(window, session, ribbon, status);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _window.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        _window.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        _window.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _window.Deactivated -= OnDeactivated;
        _ribbon.PropertyChanged -= OnRibbonPropertyChanged;
        _session.Changed -= OnChanged;
        _session.TabSelected -= OnTabSelected;
        _session.CommandRun -= OnCommandRun;
        _dialogWatch.Dispose();
        _session.Cancel();
        CloseMenu();
        FieldCodeBadge.RemoveAll(_badges);
        _ribbon.FieldCodeMenu = null;
        _ribbon.ShowFieldCodes = false;
        _ribbon.FieldCodeTabLetter = null;
        _status.Hint = null;
    }

    private static bool IsAlt(Key key) => key is Key.LeftAlt or Key.RightAlt;

    private static bool IsModifier(Key key) =>
        key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.CapsLock or Key.NumLock or Key.Scroll or Key.System;

    /// <summary>The letter or digit a key types into the session, or null. Keys are read, not text, so any keyboard layout works.</summary>
    private static char? TypedChar(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (char)('A' + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
        _ => null,
    };

    private bool IsDialogOpen() => FrameDialogs.IsOpen(_window);

    /// <summary>The mode only makes sense for a ribbon that is on screen: its tab row sits in the visual tree of this window and is visible (the start view has none).</summary>
    private bool IsRibbonOnScreen() =>
        ReferenceEquals(TopLevel.GetTopLevel(_ribbon.TabRow), _window) && _ribbon.TabRow.IsEffectivelyVisible;

    /// <summary>A key that is not a bare Alt: Alt, if held, was a modifier. A bare Alt that was only blocked (dialog, no ribbon) is no longer consumed on release.</summary>
    private void NoteOtherKey()
    {
        _altBlocked = false;
        _session.OtherKey();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsAlt(e.Key))
        {
            if (_altHeld)
            {
                // The auto-repeat of a held Alt: same answer as the first press, no new decision.
                e.Handled |= _altDownConsumed;
                return;
            }

            _altHeld = true;
            _altBlocked = false;
            if (IsDialogOpen() || !IsRibbonOnScreen())
            {
                // No mode here, but a bare Alt is still consumed on release (else the Windows system menu opens); Alt with a key is not.
                _session.Cancel();
                _altDownConsumed = false;
                _altBlocked = (e.KeyModifiers & KeyModifiers.Control) == 0;
                return;
            }

            _altDownConsumed = _session.AltDown((e.KeyModifiers & KeyModifiers.Control) != 0);
            e.Handled |= _altDownConsumed;
            return;
        }

        if (_session.Stage != FieldCodeStage.Off && !IsRibbonOnScreen())
        {
            // The ribbon went away while the mode was on (the last document closed): the mode ends with the next key.
            _session.Cancel();
        }

        if (e.Handled)
        {
            // Another handler of the window took this key before us (F6, Ctrl+K, a shortcut): it is not a field code. Handlers of one
            // element run in reverse order of registration, so the field codes must not rely on coming first: they listen to handled keys
            // too and end the mode, or - with the mode off - note that Alt was a modifier.
            if (_session.Stage != FieldCodeStage.Off)
            {
                _session.Cancel();
            }
            else
            {
                NoteOtherKey();
            }

            return;
        }

        if (e.Key == Key.F10 && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            if (IsDialogOpen() || !IsRibbonOnScreen())
            {
                // No mode here either; the key is consumed all the same, down and up, so the Windows system menu stays shut.
                e.Handled = true;
                _f10Consumed = true;
                return;
            }

            _f10Consumed = _session.F10();
            e.Handled |= _f10Consumed;
            return;
        }

        if (_session.Stage == FieldCodeStage.Off)
        {
            NoteOtherKey();
            return;
        }

        if (e.Key == Key.Escape)
        {
            _session.Escape();
            e.Handled = true;
        }
        else if (!IsModifier(e.Key))
        {
            if (TypedChar(e.Key) is { } c && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
            {
                _swallowText = true;
                e.Handled |= _session.Type(c);
            }
            else
            {
                // Any other key (Enter, Tab, arrows, shortcuts) is not a field code: the mode ends and the key does its normal job.
                _session.Cancel();
            }
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (IsAlt(e.Key))
        {
            _altHeld = false;
            _altDownConsumed = false;
            var consumed = _session.AltUp() | _altBlocked;
            _altBlocked = false;
            e.Handled |= consumed;
        }
        else if (!IsModifier(e.Key))
        {
            _swallowText = false;
            if (e.Key == Key.F10)
            {
                e.Handled |= _f10Consumed;
                _f10Consumed = false;
            }
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (_swallowText || _session.Stage != FieldCodeStage.Off)
        {
            // The character that went into the session (or a stray one while the mode is on) must not be typed anywhere.
            _swallowText = false;
            e.Handled = true;
            return;
        }

        // A character between Alt down and Alt up (AltGr on Linux, which sends no Ctrl): Alt was a modifier, not a bare press.
        NoteOtherKey();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _altHeld = false;
        _session.Cancel();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // The Alt release never reaches a window that lost the focus (Alt+Tab); forget the held key as well.
        _altHeld = false;
        _altDownConsumed = false;
        _altBlocked = false;
        _f10Consumed = false;
        _swallowText = false;
        _session.Cancel();
    }

    private void OnDialogOpened()
    {
        _altHeld = false;
        _session.Cancel();
    }

    private void OnRibbonPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // A tab change that the session did not make (a click on a tab) ends the mode.
        if (e.Property == RibbonView.ActiveTabProperty && !_settingTab)
        {
            _session.Cancel();
        }
    }

    private void OnTabSelected(RibbonTabDefinition tab)
    {
        _settingTab = true;
        try
        {
            _ribbon.ActiveTab = tab;
        }
        finally
        {
            _settingTab = false;
        }
    }

    private void OnCommandRun(CommandDefinition command) => _ribbon.NotifyInvoked(command);

    private void OnChanged()
    {
        var on = _session.Stage != FieldCodeStage.Off;
        _ribbon.FieldCodeTabLetter = on ? _session.ActiveTab?.Letter : null;
        _ribbon.FieldCodeMenu = _session.Stage == FieldCodeStage.MenuItems ? _session.OpenMenu : null;
        _ribbon.ShowFieldCodes = on;
        _status.Hint = on ? ShellStrings.FieldCodeHint : null;

        FieldCodeBadge.RemoveAll(_badges);
        SyncMenu();
        if (!on)
        {
            return;
        }

        foreach (var control in _window.GetVisualDescendants().OfType<Control>())
        {
            if (FieldCodeBadge.GetCode(control) is { Length: > 0 } code && control.IsEffectivelyVisible
                && FieldCodeBadge.Show(control, code) is { } badge)
            {
                _badges.Add(badge);
            }
        }
    }

    /// <summary>
    /// Keeps the menu of the session in step: in the menu stage the flyout of the command is open and its items carry their codes;
    /// in every other stage it is closed.
    /// </summary>
    private void SyncMenu()
    {
        var wanted = _session.Stage == FieldCodeStage.MenuItems ? _session.OpenMenu : null;
        if (wanted is not null && ReferenceEquals(wanted, _menuCommand))
        {
            return;
        }

        CloseMenu();
        if (wanted is null || _ribbon.FindMenuButton(wanted) is not { Flyout: MenuFlyout flyout } button)
        {
            return;
        }

        _menu = flyout;
        _menuCommand = wanted;
        flyout.Closed += OnMenuClosed;

        // A transient flyout does not take the keyboard focus, so the next key still reaches the window (and this class); a menu that
        // took the focus would swallow the item codes, because key events do not leave a popup. The ordinary mode comes back on close.
        _menuShowMode = flyout.ShowMode;
        flyout.ShowMode = FlyoutShowMode.Transient;
        flyout.ShowAt(button);
        foreach (var item in flyout.Items.OfType<MenuItem>())
        {
            if (item.Tag is CommandDefinition command && FieldCodeBadge.Show(item, command.FieldCode, inside: true) is { } badge)
            {
                _menuBadges.Add(badge);
            }
        }
    }

    private void CloseMenu()
    {
        FieldCodeBadge.RemoveAll(_menuBadges);
        if (_menu is not { } menu)
        {
            return;
        }

        _menu = null;
        _menuCommand = null;
        menu.Closed -= OnMenuClosed;
        menu.Hide();
        menu.ShowMode = _menuShowMode;
    }

    private void OnMenuClosed(object? sender, EventArgs e)
    {
        // The menu closed on its own (a click away, an item chosen with the pointer): the mode is over.
        if (ReferenceEquals(sender, _menu))
        {
            CloseMenu();
            _session.Cancel();
        }
    }

    private sealed class DialogObserver(FieldCodeInput owner) : IObserver<AvaloniaPropertyChangedEventArgs<Dialog?>>
    {
        public void OnNext(AvaloniaPropertyChangedEventArgs<Dialog?> value)
        {
            if (value.NewValue.GetValueOrDefault() is not null
                && value.Sender is DialogHost host && ReferenceEquals(TopLevel.GetTopLevel(host), owner._window))
            {
                owner.OnDialogOpened();
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
