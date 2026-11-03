using System.Globalization;
using System.Resources;
using System.Text;
using Avalonia;

namespace ShellProbe;

/// <summary>
/// The shell probe: a throwaway Native AOT Avalonia app for the speed measurement of milestone 1
/// (Docs/superpowers/specs/2026-11-03-design-system-shell-design.md, section 9). It is not product code.
/// </summary>
internal static class Program
{
    private const int UsageError = 1;

    [STAThread]
    public static int Main(string[] args)
    {
        // Native AOT: AppContext must be initialized before any other thread starts (as in Pagewing).
        _ = AppContext.GetData("System.GC.Server");

        var options = ProbeOptions.Parse(args, out var error);
        if (options is null)
        {
            Console.Error.WriteLine(error);
            return UsageError;
        }

        Strings.Culture = CultureInfo.GetCultureInfo(options.Language);
        if (options.PrintString is { } key)
        {
            return PrintString(key);
        }

        try
        {
            return BuildAvaloniaApp(options).StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            // No display, no usable backend (for example Wayland without XWayland), a font that fails to load.
            Console.Error.WriteLine($"error: the window could not be shown: {ex.GetType().Name}: {ex.Message}");
            return UsageError;
        }
    }

    private static AppBuilder BuildAvaloniaApp(ProbeOptions options) =>
        AppBuilder.Configure(() => new App(options)).UsePlatformDetect();

    /// <summary>The check that the German resource is found under Native AOT: prints the value of a key.</summary>
    private static int PrintString(string key)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.Out.WriteLine(Strings.Get(key));
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MissingManifestResourceException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return UsageError;
        }
    }
}
