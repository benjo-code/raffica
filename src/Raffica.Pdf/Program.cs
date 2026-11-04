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
        if (instance is null && InstanceHandover.TryHandOver(name, options.Files, HandOverTimeout))
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

    private static AppBuilder BuildAvaloniaApp(PdfOptions options, SingleInstance? instance) =>
        AppBuilder.Configure(() => new App(options, instance)).UsePlatformDetect();
}
