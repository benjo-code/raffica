using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Raffica.Shell.Theming;

namespace Raffica.Shell;

/// <summary>
/// Applies the theme of the shell to an application: light, dark or the system theme, the Windows high-contrast set,
/// and the ink (accent colour) of the app.
/// </summary>
/// <remarks>
/// The high-contrast tokens are an ordinary theme dictionary under the variant <see cref="HighContrastVariant"/>
/// (inheriting Dark), registered on the first request, so every <c>DynamicResource</c> follows a switch at runtime.
/// The semantic keys <c>Raffica.Color.Ink</c>, <c>Raffica.Brush.Ink</c> and <c>Raffica.Brush.Focus.Ring</c> are plain
/// application resources, set from the ink of the app and the actual variant, and set again whenever the actual variant
/// changes. UI thread only.
/// </remarks>
public static class ShellTheme
{
    /// <summary>The theme variant of Windows high contrast. It inherits Dark, so a resource that is not a token comes from the dark theme.</summary>
    public static readonly ThemeVariant HighContrastVariant = new("HighContrast", ThemeVariant.Dark);

    private static Application? s_app;
    private static ThemeMode s_mode;
    private static AppInk s_ink;
    private static bool? s_forceHighContrast;
    private static IPlatformSettings? s_settings;

    /// <summary>
    /// Sets the theme variant and the ink of <paramref name="app"/>. High contrast is used when
    /// <paramref name="forceHighContrast"/> is <see langword="true"/>, or when it is <see langword="null"/> and Windows
    /// asks for it; other platforms report no request.
    /// </summary>
    public static void Apply(Application app, ThemeMode mode, AppInk ink, bool? forceHighContrast = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        Detach();

        s_app = app;
        s_mode = mode;
        s_ink = ink;
        s_forceHighContrast = forceHighContrast;

        if (forceHighContrast ?? SystemRequestsHighContrast(app))
        {
            EnsureHighContrastTokens(app);
            app.RequestedThemeVariant = HighContrastVariant;
        }
        else
        {
            app.RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }

        ResolveInk(app, ink);
        app.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        if (forceHighContrast is null && OperatingSystem.IsWindows() && app.PlatformSettings is { } settings)
        {
            s_settings = settings;
            settings.ColorValuesChanged += OnColorValuesChanged;
        }
    }

    private static void Detach()
    {
        if (s_app is not null)
        {
            s_app.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }

        if (s_settings is not null)
        {
            s_settings.ColorValuesChanged -= OnColorValuesChanged;
            s_settings = null;
        }
    }

    private static bool SystemRequestsHighContrast(Application app) =>
        OperatingSystem.IsWindows() && app.PlatformSettings?.GetColorValues().ContrastPreference == ColorContrastPreference.High;

    private static void EnsureHighContrastTokens(Application app)
    {
        var themes = app.Resources.ThemeDictionaries;
        if (!themes.ContainsKey(HighContrastVariant))
        {
            themes[HighContrastVariant] = new TokensHighContrast();
        }
    }

    private static void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (s_app is not null)
        {
            ResolveInk(s_app, s_ink);
        }
    }

    private static void OnColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (s_app is not null)
        {
            Apply(s_app, s_mode, s_ink, s_forceHighContrast);
        }
    }

    private static void ResolveInk(Application app, AppInk ink)
    {
        var key = "Raffica.Color.Ink." + ink;
        if (!app.TryGetResource(key, app.ActualThemeVariant, out var value) || value is not Color color)
        {
            throw new InvalidOperationException($"The ink token {key} is missing. Add RafficaStyles to the application styles first.");
        }

        var brush = new SolidColorBrush(color);
        app.Resources["Raffica.Color.Ink"] = color;
        app.Resources["Raffica.Brush.Ink"] = brush;
        app.Resources["Raffica.Brush.Focus.Ring"] = brush;
    }
}
