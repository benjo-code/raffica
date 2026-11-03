using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

/// <summary>Shows controls in a headless window under a theme and resolves token values for the comparison.</summary>
internal static class ControlHarness
{
    public static Window Show(Control content, ThemeMode mode)
    {
        ShellTheme.Apply(Application.Current!, mode, AppInk.Write, forceHighContrast: false);
        var window = new Window { Width = 480, Height = 320, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    /// <summary>The colour of a brush token of the active theme (or an app resource such as <c>Raffica.Brush.Ink</c>).</summary>
    public static Color Token(string key)
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource(key, app.ActualThemeVariant, out var value), $"token {key} not found");
        return ((ISolidColorBrush)value!).Color;
    }

    public static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;

    public static ContentPresenter Presenter(Control control) =>
        control.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    public static T Part<T>(Control control, string name)
        where T : Control =>
        control.GetVisualDescendants().OfType<T>().First(p => p.Name == name);

    public static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    public static void KeyboardFocus(Window window, Control control)
    {
        control.Focus(NavigationMethod.Tab);
        Settle(window);
    }
}
