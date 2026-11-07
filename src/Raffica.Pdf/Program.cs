using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Raffica.Core.Hosting;
using Raffica.Core.Updates;
using Raffica.Pdf.Hosting;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Settings;

namespace Raffica.Pdf;

internal static class Program
{
    private const int UsageError = 1;
    private static readonly TimeSpan HandOverTimeout = TimeSpan.FromSeconds(1);

    /// <summary>How long a program started by an update waits for the app it replaces to end before it goes on anyway.</summary>
    private static readonly TimeSpan PredecessorTimeout = TimeSpan.FromSeconds(30);

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

        // The installer's registration runs and ends here: no instance lock, no hand-over, no window.
        if (RegistrationCommand.Applies(options))
        {
            return RunRegistration(options);
        }

        // The helper of the Windows updater runs and ends here, before the instance lock: it must not meet the app it replaces, and it never opens the normal window.
        if (options.FinishUpdate is not null)
        {
            return RunUpdateHelper(options);
        }

        // The new program of an update (the AppImage that replaced itself) waits until the old process has ended, so it elects its instance without meeting it.
        if (options.AfterProcess is { } predecessor)
        {
            SystemUpdateProcessStarter.WaitForExit(predecessor, PredecessorTimeout);
        }

        if (options.RestoreSession)
        {
            options = RestoreSession(options);
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

    private static int RunUpdateHelper(PdfOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("error: the update helper is only available on Windows");
            return UsageError;
        }

        try
        {
            return BuildAvaloniaApp(options, instance: null).StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: the window could not be shown: {ex.GetType().Name}: {ex.Message}");
            return UsageError;
        }
    }

    // The tabs of the session the app wrote before the update restarted it, once: the file is read and deleted at once, files that are gone are dropped, and the tabs
    // open in their order (the last one is the active tab).
    internal static PdfOptions RestoreSession(PdfOptions options)
    {
        var store = new SessionStore(UpdateApplierSupport.SessionPath(PdfSettingsFiles.ConfigDirectoryFor(options.SettingsFile)));
        var session = store.Load().KeepFiles(File.Exists);
        store.Delete();
        var files = session.Windows.SelectMany(window => window.Tabs.Select(tab => tab.Path)).Concat(options.Files).ToList();
        return files.Count == 0 ? options : options.WithFiles(files);
    }

    private static int RunRegistration(PdfOptions options)
    {
        var writer = RegistrationCommand.CreateWriter();
        var exe = Environment.ProcessPath;
        if (writer is null || string.IsNullOrEmpty(exe))
        {
            Console.Error.WriteLine("error: the file type registration is only available on Windows");
            return UsageError;
        }

        return RegistrationCommand.Run(options, writer, exe);
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

        // Windows only: software rendering is the default (no Direct3D device, no ANGLE display, no composition; it halves every start case and
        // cuts idle memory to a third, see Docs/DECISIONS.md). RAFFICA_RENDERING=default opts out to Avalonia's own default path.
        if (OperatingSystem.IsWindows()
            && RenderingChoiceParser.Parse(Environment.GetEnvironmentVariable(RenderingChoiceParser.VariableName)) == RenderingChoice.Software)
        {
            builder = builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        }

        return builder;
    }
}
