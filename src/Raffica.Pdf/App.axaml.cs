using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Raffica.Core.Hosting;
using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.Windows;
using Raffica.Pdf.Settings;
using Raffica.Shell;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;

namespace Raffica.Pdf;

internal sealed partial class App : Application
{
    private readonly PdfOptions _options;
    private readonly SingleInstance? _instance;
    private readonly ShellSettings _settings;
    private readonly Raffica.Core.Settings.AppSettings _appSettings;
    private IDisposable[]? _crashSubscriptions;

    public App(PdfOptions options, SingleInstance? instance = null)
    {
        _options = options;
        _instance = instance;
        _settings = new SettingsStore(PdfSettingsFiles.ShellPathFor(options.SettingsFile)).Load();

        // The settings of the viewer (default layout and zoom mode) are a tiny file, read once here and handed to the window: opening a tab reads nothing.
        _appSettings = new Raffica.Core.Settings.SettingsStore(PdfSettingsFiles.PdfSettingsPathFor(options.SettingsFile)).LoadReadOnly();
    }

    public override void Initialize()
    {
        // The language at start: --lang, then the stored setting, then the UI language of the operating system.
        var culture = Languages.Resolve(_options.Lang ?? _settings.Language, CultureInfo.CurrentUICulture);
        ShellStrings.Culture = culture;
        PdfStrings.Culture = culture;

        AvaloniaXamlLoader.Load(this);
        ShellTheme.Apply(this, _options.ThemeGiven ? _options.Theme : _settings.Theme, AppInk.Pdf, _options.HighContrast ? true : null);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The helper of the Windows updater (--finish-update): its progress window instead of the normal one, which is never made.
            if (_options.FinishUpdate is { } staging)
            {
                desktop.MainWindow = CreateUpdateHelperWindow(staging, desktop);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // macOS keeps the app alive when its last window closes (the Dock icon stays, Finder files and "reopen" make a new window): the host owns the windows.
            // Windows and Linux end with the last window, as before. A measurement run measures one window.
            var hosted = OperatingSystem.IsMacOS() && !_options.IsMeasuring;
            if (hosted)
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            }

            var window = CreateWindow(null, desktop);
            Hosting.WindowHost? host = null;
            if (hosted)
            {
                host = new Hosting.WindowHost(files =>
                {
                    var next = CreateWindow(files, desktop);
                    next.Show();
                    return next;
                }, () => desktop.Shutdown(), Dispatcher.UIThread);
                host.Adopt(window);
                StartHostedInput(window, host);
            }
            else if (_instance is not null)
            {
                window.Listen(_instance);
            }

            desktop.MainWindow = window;
            if (!_options.IsMeasuring)
            {
                AttachCrashGuard(host is null ? () => window : () => host.Current as PdfWindow, desktop);
            }

            if (_options.IsMeasuring)
            {
                // Held by the watchdog timer and the window events until the run is over.
                new Measurement(_options, window, Console.Out, code => desktop.Shutdown(code)).Start();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Hosting.WindowSettingsSource? _settingsSource;

    // One window with its services. The first one has the settings read at the start of the process; every later one (macOS) reads them again from their
    // files, because the window before it wrote the placement and the compact bar when it closed.
    private PdfWindow CreateWindow(IReadOnlyList<string>? files, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var options = files is null ? _options : _options.WithFiles(files);
        _settingsSource ??= new Hosting.WindowSettingsSource(_options.SettingsFile, _settings, _appSettings);
        var (settings, appSettings) = _settingsSource.Next();
        var window = new PdfWindow(options, settings, appSettings);

        // The system's default app service is made on first use (the offer after the first sharp page, the command); a measurement run has none.
        window.DefaultAppServices = _options.IsMeasuring ? null : Platform.PlatformDefaultAppServices.Create;

        // The update flow is made on first use (the check after the first sharp page, the command); a measurement run has none, so it opens no connection.
        // The installer of the system ends the app through the window (its close question was answered before the installer was called) and the lifetime.
        window.UpdateFlows = Updating.UpdateServices.MeasurementKeepsUpdatesOff(_options)
            ? null
            : enabled => Updating.UpdateServices.Create(_options.SettingsFile, enabled, () => window.EndForUpdate(() => desktop.Shutdown()));
        return window;
    }

    // macOS hands files to the running app through the activation events, not the command line: the bridge subscribes now so nothing is lost before the first
    // window opens, and the pipe of a later launch starts at that window's Opened. Both feed the host, which decides which window gets the files.
    private void StartHostedInput(PdfWindow first, Hosting.WindowHost host)
    {
        Hosting.FileActivationBridge? bridge = null;
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            var source = new Hosting.AvaloniaFileActivationSource(activatable);
            bridge = new Hosting.FileActivationBridge(source);
            source.Reopened += host.Reopen;
        }

        void OnOpened(object? sender, EventArgs e)
        {
            first.Opened -= OnOpened;
            if (_instance is not null)
            {
                try
                {
                    _instance.StartListening(host.OpenFiles);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Without the listener a later launch opens a process of its own.
                }
            }

            bridge?.Attach(host.OpenFiles);
        }

        first.Opened += OnOpened;
    }

    // The last net: an uncaught exception goes to crash.log, a UI one also shows one card. The dispatcher is captured here, on the UI thread, because the
    // callbacks of the other events run on pool threads. Only subscriptions are made now; the file and the card exist after the first exception.
    // With no window (macOS after the last one closed) there is no card to show, so the guard is told it is closed at once.
    private void AttachCrashGuard(Func<PdfWindow?> current, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var ui = Dispatcher.UIThread;
        var folder = PdfSettingsFiles.ConfigDirectoryFor(_options.SettingsFile);
        Hosting.CrashGuard? guard = null;
        guard = new Hosting.CrashGuard(
            folder,
            TimeProvider.System,
            _ => ui.Post(() =>
            {
                if (current() is { } window)
                {
                    window.ShowCrashCard(folder, closed: () => guard!.CardClosed());
                }
                else
                {
                    guard!.CardClosed();
                }
            }),
            () => ui.Post(() => desktop.Shutdown(1)));
        _crashSubscriptions =
        [
            Hosting.CrashGuardHost.AttachUi(guard, ui),
            Hosting.CrashGuardHost.AttachProcess(guard),
        ];
    }

    private Updating.UpdateHelperWindow CreateUpdateHelperWindow(string staging, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var config = PdfSettingsFiles.ConfigDirectoryFor(_options.SettingsFile);
        var helper = new WindowsUpdateHelper(new WindowsUpdateHelperSetup(
            staging,
            config,
            UpdateTrust.Keys,
            Platform.UpdateApplierSupport.RunningVersion(),
            new SystemUpdateProcessStarter(),
            new SystemProcessRunner(),
            () => Dispatcher.UIThread.Post(() => desktop.Shutdown()))
        {
            AfterProcess = _options.AfterProcess,
        });
        return new Updating.UpdateHelperWindow(helper);
    }
}
