using Avalonia;

namespace Raffica.Pdf;

internal static class Program
{
    private const int UsageError = 1;

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

        try
        {
            return BuildAvaloniaApp(options).StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            // No display, no usable backend.
            Console.Error.WriteLine($"error: the window could not be shown: {ex.GetType().Name}: {ex.Message}");
            return UsageError;
        }
    }

    private static AppBuilder BuildAvaloniaApp(PdfOptions options) =>
        AppBuilder.Configure(() => new App(options)).UsePlatformDetect();
}
