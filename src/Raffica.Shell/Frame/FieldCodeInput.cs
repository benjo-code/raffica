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
/// <item>A pointer press, the window losing the focus, a tab change by pointer and a dialog opening end the mode.</item>
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
    private bool _altHeld;
    private bool _altDownConsumed;
    private bool _swallowText;
    private bool _settingTab;
    private bool _disposed;

    private FieldCodeInput(Window window, FieldCodeSession session, RibbonView ribbon, StatusBar status)
    {
        _window = window;
        _session = session;
        _ribbon = ribbon;
        _status = status;

        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
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
        FieldCodeBadge.RemoveAll(_badges);
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

    private bool IsDialogOpen() => _window.GetVisualDescendants().OfType<DialogHost>().Any(host => host.IsOpen);

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
            if (IsDialogOpen())
            {
                _session.Cancel();
                _altDownConsumed = false;
                return;
            }

            _altDownConsumed = _session.AltDown((e.KeyModifiers & KeyModifiers.Control) != 0);
            e.Handled |= _altDownConsumed;
            return;
        }

        if (e.Key == Key.F10 && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            if (!IsDialogOpen())
            {
                e.Handled |= _session.F10();
            }

            return;
        }

        if (_session.Stage == FieldCodeStage.Off)
        {
            _session.OtherKey();
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
            e.Handled |= _session.AltUp();
        }
        else if (!IsModifier(e.Key))
        {
            _swallowText = false;
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
        _session.OtherKey();
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
        _ribbon.ShowFieldCodes = on;
        _status.Hint = on ? ShellStrings.FieldCodeHint : null;

        FieldCodeBadge.RemoveAll(_badges);
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
