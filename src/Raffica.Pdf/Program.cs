using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Raffica.Core.Hosting;
using Raffica.Pdf.Hosting;

namespace Raffica.Pdf;

internal static class Program
{
    private const int UsageError = 1;
    private static readonly TimeSpan HandOverTimeout = TimeSpan.FromSeconds(1);

    [STAThread]
    public static int Main(string[] args)
    {
        // Native AOT: AppContext must be initialized before any other thread starts.
        _ = AppContext.GetData("System.GC.Server");

        PdfOptions options;
        try
        {
            options = PdfOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return UsageError;
        }

        if (options.PrintString is not null)
        {
            return PrintStringCommand.Run(options, Console.Out, Console.Error);
        }

        // The first instance keeps the lock; a later one hands its files over and ends before any Avalonia start. If the first one
        // does not answer within a second, this process starts as an ordinary window.
        var name = InstanceName(options);
        using var instance = SingleInstance.TryBecomePrimary(name);
        if (instance is null && TryHandOver(name, options))
        {
            return 0;
        }

        try
        {
            return BuildAvaloniaApp(options, instance).StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            // No display, no usable backend.
            Console.Error.WriteLine($"error: the window could not be shown: {ex.GetType().Name}: {ex.Message}");
            return UsageError;
        }
    }

    // Whatever goes wrong with the hand-over, this process starts as an ordinary window instead of failing without one.
    private static bool TryHandOver(string name, PdfOptions options)
    {
        try
        {
            return InstanceHandover.TryHandOver(name, options.Files, HandOverTimeout);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>One instance per user; a run with its own settings file (measurements, tests) is an instance of its own and never meets the app of the user.</summary>
    private static string InstanceName(PdfOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SettingsFile))
        {
            return SingleInstance.DefaultName;
        }

        string full;
        try
        {
            full = Path.GetFullPath(options.SettingsFile);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            full = options.SettingsFile;
        }

        return SingleInstance.DefaultName + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0, 6);
    }

    private static AppBuilder BuildAvaloniaApp(PdfOptions options, SingleInstance? instance)
    {
        var builder = AppBuilder.Configure(() => new App(options, instance)).UsePlatformDetect();

        // Developer and measurement override, Windows only: RAFFICA_RENDERING=software takes software rendering as the only mode (no Direct3D
        // device, no ANGLE display, no composition); anything else keeps Avalonia's default.
        if (OperatingSystem.IsWindows()
            && RenderingChoiceParser.Parse(Environment.GetEnvironmentVariable(RenderingChoiceParser.VariableName)) == RenderingChoice.Software)
        {
            builder = builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        }

        return builder;
    }
}
