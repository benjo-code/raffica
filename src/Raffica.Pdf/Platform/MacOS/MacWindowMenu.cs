using Avalonia.Controls;

namespace Raffica.Pdf.Platform.MacOS;

/// <summary>
/// Puts the menus of a window (File, Edit, View, Window, Help) into the macOS menu bar. Avalonia takes the menu set on the application as the application
/// menu only (the one under the name of the app); the other menus of the bar come from the menu set on the window that is in front, so every window carries
/// its own, bound to that window.
/// </summary>
internal static class MacWindowMenu
{
    /// <summary>Makes the menus of <paramref name="menuWindow"/> and sets them on <paramref name="window"/> (the same object in the app, two in a test).</summary>
    public static MacMenuBinder Attach(Window window, IMenuWindow menuWindow)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(menuWindow);
        var binder = new MacMenuBinder(() => menuWindow, () => Task.CompletedTask, MenuPart.Window);
        NativeMenu.SetMenu(window, binder.Menu);
        binder.Refresh();
        return binder;
    }
}
