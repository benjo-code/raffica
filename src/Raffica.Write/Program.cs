using Avalonia;
using Raffica.Core.Hosting;
using Raffica.Hosting;

namespace Raffica.Write;

internal static class Program
{
    private const int UsageError = 1;
    private static readonly TimeSpan HandOverTimeout = TimeSpan.FromSeconds(1);

    [STAThread]
    public static int Main(string[] args)
    {
        // Native AOT: AppContext must be initialized before any other thread starts.
        _ = AppContext.GetData("System.GC.Server");

        WriteOptions options;
        try
        {
            options = WriteOptions.Parse(args);
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
        // does not answer within a second, this process starts as an ordinary window. Write has its own name, so it never meets the PDF app.
        var name = AppFiles.Write.InstanceName(options.SettingsFile);
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
    private static bool TryHandOver(string name, WriteOptions options)
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

    private static AppBuilder BuildAvaloniaApp(WriteOptions options, SingleInstance? instance)
    {
        var builder = AppBuilder.Configure(() => new App(options, instance)).UsePlatformDetect();

        // The same rendering choice as the PDF app (Docs/DECISIONS.md, "Raffica PDF: software rendering is the Windows default"): software on Windows, the
        // framebuffer on macOS only when RAFFICA_RENDERING=software, untouched on Linux. RAFFICA_RENDERING=default opts out on Windows.
        var choice = RenderingChoiceParser.For(RenderingChoiceParser.CurrentPlatform, Environment.GetEnvironmentVariable(RenderingChoiceParser.VariableName));
        if (choice == RenderingChoice.Software)
        {
            if (OperatingSystem.IsWindows())
            {
                builder = builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
            }
            else if (OperatingSystem.IsMacOS())
            {
                builder = builder.With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] });
            }
        }

        return builder;
    }
}
