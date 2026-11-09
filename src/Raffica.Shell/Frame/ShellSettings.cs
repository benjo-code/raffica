using System.Text.Json.Serialization;
using Avalonia;

namespace Raffica.Shell.Frame;

/// <summary>
/// The settings every app of the suite keeps (section 6 of the design spec): the theme, the language (<c>de</c> or <c>en</c>; null
/// follows the operating system and is what a first start has), the compact command bar, autosave (on by default) and the window
/// placement of the last close (null until a window was closed once).
/// </summary>
public sealed record ShellSettings(
    ThemeMode Theme = ThemeMode.System,
    string? Language = null,
    bool CompactCommandBar = false,
    bool Autosave = true,
    WindowPlacement? WindowPlacement = null);

/// <summary>
/// Where a window was when it closed: the restore bounds (the normal state, never the maximized or minimized bounds) and whether it was maximized.
/// <paramref name="X"/> and <paramref name="Y"/> are the screen pixels of the top left corner, <paramref name="Width"/> and <paramref name="Height"/>
/// are device-independent pixels (the unit of <c>Window.Width</c>).
/// </summary>
public sealed record WindowPlacement(double X, double Y, double Width, double Height, bool Maximized)
{
    /// <summary>The space in device-independent pixels that stays free on every side when a window is shrunk to a small work area.</summary>
    public const double Margin = 24;

    /// <summary>Whether every number is real and the size is positive; anything else is not a placement.</summary>
    public bool IsUsable => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;

    /// <summary>
    /// The placement a window gets on a work area: the stored one (its size shrunk to the work area less <see cref="Margin"/> on each side, its position moved
    /// inside), or <paramref name="defaultSize"/> shrunk the same way and centred when nothing usable is stored.
    /// </summary>
    /// <param name="workArea">The usable area of the screen in pixels (without the task bar).</param>
    /// <param name="scaling">Pixels per device-independent pixel of that screen.</param>
    /// <param name="stored">The placement of the last close, or null.</param>
    /// <param name="defaultSize">The size of a window that was never moved, in device-independent pixels.</param>
    public static WindowPlacement FitTo(PixelRect workArea, double scaling, WindowPlacement? stored, Size defaultSize)
    {
        var scale = double.IsFinite(scaling) && scaling > 0 ? scaling : 1;
        var availableWidth = Math.Max(1, (workArea.Width / scale) - (2 * Margin));
        var availableHeight = Math.Max(1, (workArea.Height / scale) - (2 * Margin));
        var usable = stored is { IsUsable: true };
        var width = Math.Min(usable ? stored!.Width : defaultSize.Width, availableWidth);
        var height = Math.Min(usable ? stored!.Height : defaultSize.Height, availableHeight);
        var pixelWidth = width * scale;
        var pixelHeight = height * scale;
        double x;
        double y;
        if (usable)
        {
            x = Math.Clamp(stored!.X, workArea.X, Math.Max(workArea.X, workArea.Right - pixelWidth));
            y = Math.Clamp(stored.Y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - pixelHeight));
        }
        else
        {
            x = workArea.X + ((workArea.Width - pixelWidth) / 2);
            y = workArea.Y + ((workArea.Height - pixelHeight) / 2);
        }

        return new WindowPlacement(Math.Round(x), Math.Round(y), width, height, usable && stored!.Maximized);
    }
}

/// <summary>The source-generated JSON metadata of <see cref="ShellSettings"/> (no reflection, safe for Native AOT); enums are written as names.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true, WriteIndented = true)]
[JsonSerializable(typeof(ShellSettings))]
internal sealed partial class ShellSettingsJsonContext : JsonSerializerContext;
