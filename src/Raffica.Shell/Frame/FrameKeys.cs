using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Raffica.Shell.Frame;

/// <summary>The key that focuses the command search of the title bar; an app picks one for its frame.</summary>
public enum SearchShortcut
{
    /// <summary>Ctrl+K (Cmd+K on macOS): the PDF app.</summary>
    CtrlK,

    /// <summary>
    /// Alt+Q (the Write app, where Ctrl+K is the key of links). On Windows and Linux it matches only with Alt alone: AltGr+Q types "@" on layouts that have it, and Windows
    /// reports AltGr as Ctrl+Alt, so a chord with Ctrl down never matches. macOS keeps Cmd+K until the Write commands there are settled.
    /// </summary>
    AltQ,
}

/// <summary>The window-wide keys of the app frame.</summary>
public static class FrameKeys
{
    /// <summary>The gesture of a search shortcut on a system; <paramref name="command"/> is the command modifier of the platform (Ctrl, Cmd on macOS).</summary>
    internal static KeyGesture GestureOf(SearchShortcut shortcut, KeyModifiers command, bool macOs) =>
        shortcut == SearchShortcut.AltQ && !macOs ? new KeyGesture(Key.Q, KeyModifiers.Alt) : new KeyGesture(Key.K, command);

    /// <summary>The text of a search shortcut as <see cref="Raffica.Shell.Localization.KeyNames"/> reads it ("Alt+Q", "Ctrl+K", "Meta+K").</summary>
    internal static string TextOf(SearchShortcut shortcut, bool macOs) =>
        shortcut == SearchShortcut.AltQ && !macOs ? "Alt+Q" : macOs ? "Meta+K" : "Ctrl+K";

    /// <summary>
    /// Makes the search shortcut (Ctrl+K by default, Cmd+K on macOS: the command modifier of the platform's hotkey configuration; or Alt+Q, see
    /// <see cref="SearchShortcut"/>) focus the search field and select its text, wherever the focus is in the window. The key is handled on the way
    /// down, so a text box does not see it. While a <see cref="Raffica.Shell.Controls.DialogHost"/> of the window shows a dialog the shortcut does nothing (the dialog is modal).
    /// The field's tooltip names the shortcut.
    /// </summary>
    public static void Attach(Window window, SearchField search, SearchShortcut shortcut = SearchShortcut.CtrlK)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(search);
        var modifiers = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers
            ?? (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        var gesture = GestureOf(shortcut, modifiers, OperatingSystem.IsMacOS());
        search.Shortcut = shortcut;
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
