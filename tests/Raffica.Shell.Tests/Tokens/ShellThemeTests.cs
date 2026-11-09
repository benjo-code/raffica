using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Raffica.Shell.Tests.Tokens;

public sealed class ShellThemeTests
{
    [AvaloniaFact]
    public void InkFollowsTheChosenAppAndTheActualVariant()
    {
        var app = Application.Current!;

        ShellTheme.Apply(app, ThemeMode.Dark, AppInk.Sheets, forceHighContrast: false);
        Assert.Equal(Color.Parse("#7CCBA2"), Brush(app, "Raffica.Brush.Ink").Color);
        Assert.Equal(Color.Parse("#7CCBA2"), Brush(app, "Raffica.Brush.Focus.Ring").Color);

        ShellTheme.Apply(app, ThemeMode.Light, AppInk.Sheets, forceHighContrast: false);
        Assert.Equal(Color.Parse("#1E6B47"), Brush(app, "Raffica.Brush.Ink").Color);
        Assert.Equal(Color.Parse("#1E6B47"), (Color)Resource(app, "Raffica.Color.Ink"));
    }

    [AvaloniaFact]
    public void InkIsResolvedAgainWhenTheVariantChanges()
    {
        var app = Application.Current!;
        ShellTheme.Apply(app, ThemeMode.Light, AppInk.Pdf, forceHighContrast: false);
        Assert.Equal(Color.Parse("#5E1A3E"), Brush(app, "Raffica.Brush.Ink").Color);

        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        Assert.Equal(Color.Parse("#E5A4C5"), Brush(app, "Raffica.Brush.Ink").Color);
    }

    [AvaloniaFact]
    public void ForcedHighContrastTakesTheTokensFromTheHighContrastSet()
    {
        var app = Application.Current!;

        ShellTheme.Apply(app, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        Assert.Equal(Color.Parse("#FFFFFF"), Brush(app, "Raffica.Brush.Surface.Page").Color);

        ShellTheme.Apply(app, ThemeMode.Light, AppInk.Write, forceHighContrast: true);
        Assert.Equal(Color.Parse("#000000"), Brush(app, "Raffica.Brush.Surface.Page").Color);
        Assert.Equal(Color.Parse("#FFFF00"), Brush(app, "Raffica.Brush.Ink").Color);

        ShellTheme.Apply(app, ThemeMode.Dark, AppInk.Write, forceHighContrast: false);
        Assert.Equal(Color.Parse("#1B1D21"), Brush(app, "Raffica.Brush.Surface.Page").Color);
    }

    [AvaloniaFact]
    public void WithoutAPlatformRequestHighContrastStaysOff()
    {
        var app = Application.Current!;

        ShellTheme.Apply(app, ThemeMode.Light, AppInk.Write);

        Assert.Equal(Color.Parse("#FFFFFF"), Brush(app, "Raffica.Brush.Surface.Page").Color);
    }

    private static SolidColorBrush Brush(Application app, string key) =>
        Assert.IsType<SolidColorBrush>(Resource(app, key));

    private static object Resource(Application app, string key)
    {
        Assert.True(app.TryGetResource(key, app.ActualThemeVariant, out var value), key + " is not defined.");
        return value!;
    }
}
