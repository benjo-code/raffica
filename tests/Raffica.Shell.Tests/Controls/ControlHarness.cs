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

    /// <summary>
    /// The colour of a solid brush. Unlike <see cref="ColorOf"/> a missing or non-solid brush fails the test, so an assertion
    /// cannot pass vacuously (a null brush that happens to equal a transparent token).
    /// </summary>
    public static Color Strict(IBrush? brush, string what = "brush")
    {
        Assert.NotNull(brush);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        _ = what;
        return solid.Color;
    }

    /// <summary>Asserts that <paramref name="brush"/> is a solid brush with the value of the token <paramref name="key"/> in the active theme.</summary>
    public static void AssertToken(string key, IBrush? brush, string what = "brush")
    {
        Assert.True(brush is ISolidColorBrush, $"{what}: expected a solid brush for {key}, got {brush?.GetType().Name ?? "null"}");
        Assert.Equal(Token(key), Strict(brush, what));
    }

    public static ContentPresenter Presenter(Control control) =>
        control.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    public static T Part<T>(Control control, string name)
        where T : Control =>
        Descendants(control).OfType<T>().First(p => p.Name == name);

    /// <summary>The visual descendants of <paramref name="root"/>, including the content of popups below it (an open popup lives in its own host).</summary>
    public static IEnumerable<Visual> Descendants(Visual root)
    {
        foreach (var visual in root.GetVisualDescendants())
        {
            yield return visual;
            if (visual is Popup { Child: { } child })
            {
                yield return child;
                foreach (var inner in Descendants(child))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>Whether a border anywhere below <paramref name="root"/> (popup content included) draws a box shadow.</summary>
    public static bool HasShadow(Control root) =>
        Descendants(root).OfType<Border>().Any(b => b.BoxShadow != default);

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

