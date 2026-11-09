using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Raffica.Shell.Frame;

/// <summary>The window-wide keys of the app frame.</summary>
public static class FrameKeys
{
    /// <summary>
    /// Makes Ctrl+K (Cmd+K on macOS: the command modifier of the platform's hotkey configuration) focus the search field and select its
    /// text, wherever the focus is in the window. The key is handled on the way down, so a text box does not see it. While a
    /// <see cref="Raffica.Shell.Controls.DialogHost"/> of the window shows a dialog the shortcut does nothing (the dialog is modal).
    /// </summary>
    public static void Attach(Window window, SearchField search)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(search);
        var modifiers = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers
            ?? (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        var gesture = new KeyGesture(Key.K, modifiers);
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (gesture.Matches(e) && !FrameDialogs.IsOpen(window))
            {
                search.FocusAndSelect();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }
}
