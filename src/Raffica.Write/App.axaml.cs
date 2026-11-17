using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Raffica.Core.Hosting;
using Raffica.Hosting;
using Raffica.Shell;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Write.Documents;
using Raffica.Write.Platform.MacOS;
using Raffica.Write.Recovery;
using Raffica.Write.Settings;

namespace Raffica.Write;

internal sealed partial class App : Application
{
    private readonly WriteOptions _options;
    private readonly SingleInstance? _instance;
    private readonly ShellSettings _settings;
    private readonly WriteSettings _appSettings;

    // One for the process: the font catalog is made when the first file is opened and shared by every window.
    private readonly OpenWork _work = new();

    // One for the process: the first window shown offers the recovery copies a crash left behind; later windows do not ask again.
    private readonly RecoveryCoordinator _recovery = new();
    private WindowSettingsSource<WriteSettings>? _settingsSource;
    private WindowHost? _host;

    // True on macOS outside a measurement run: every window carries its own menus (File, Window) in the menu bar.
    private bool _menusOnWindows;

    public App(WriteOptions options, SingleInstance? instance = null)
    {
        _options = options;
        _instance = instance;
        _settings = new SettingsStore(AppFiles.Write.ShellPathFor(options.SettingsFile)).Load();

        // The settings of the app (default view and zoom) are a tiny file, read once here and handed to the window: opening a tab reads nothing.
        _appSettings = new WriteSettingsStore(AppFiles.Write.AppSettingsPathFor(options.SettingsFile)).Load();
    }

    public override void Initialize()
    {
        // The language at start: --lang, then the stored setting, then the UI language of the operating system.
        var culture = Languages.Resolve(_options.Lang ?? _settings.Language, CultureInfo.CurrentUICulture);
        ShellStrings.Culture = culture;
        WriteStrings.Culture = culture;

        AvaloniaXamlLoader.Load(this);
        ShellTheme.Apply(this, _options.ThemeGiven ? _options.Theme : _settings.Theme, AppInk.Write, _options.HighContrast ? true : null);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // macOS keeps the app alive when its last window closes (the Dock icon stays, Finder files and "reopen" make a new window): the host owns the windows.
            // Windows and Linux end with the last window. A measurement run measures one window.
            var hosted = OperatingSystem.IsMacOS() && !_options.IsMeasuring;
            _menusOnWindows = hosted;
            if (hosted)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            var window = CreateWindow(null);
            if (hosted)
            {
                var host = new WindowHost(files =>
                {
                    var next = CreateWindow(files);
                    next.Show();
                    return next;
                }, () => desktop.Shutdown(), Dispatcher.UIThread);
                _host = host;
                host.Adopt(window);
                StartHostedInput(window, host);
                AttachMenu(host);
            }
            else if (_instance is not null)
            {
                window.Listen(_instance);
            }

            desktop.MainWindow = window;
            if (_options.IsMeasuring)
            {
                // Held by the watchdog timer and the window events until the run is over; a normal run never makes one.
                new Measurement(_options, window, Console.Out, code => desktop.Shutdown(code)).Start();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // One window with its services. The first one has the settings read at the start of the process; every later one (macOS) reads them again from their
    // files, because the window before it wrote the placement when it closed.
    private WriteWindow CreateWindow(IReadOnlyList<string>? files)
    {
        var options = files is null ? _options : _options.WithFiles(files);
        _settingsSource ??= new WindowSettingsSource<WriteSettings>(AppFiles.Write, _options.SettingsFile, _settings, _appSettings, file => new WriteSettingsStore(AppFiles.Write.AppSettingsPathFor(file)).Load());
        var (settings, appSettings) = _settingsSource.Next();
        var window = new WriteWindow(options, settings, appSettings, _work, deferRibbon: true) { RecoveryOffer = _recovery };
        if (_menusOnWindows)
        {
            WriteMacMenu.Attach(window, window);
        }

        return window;
    }

    // macOS hands files to the running app through the activation events, not the command line: the bridge subscribes now so nothing is lost before the first
    // window opens, and the pipe of a later launch starts at that window's Opened. Both feed the host, which decides which window gets the files.
    private void StartHostedInput(WriteWindow first, WindowHost host)
    {
        FileActivationBridge? bridge = null;
        if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            var source = new AvaloniaFileActivationSource(activatable);
            bridge = new FileActivationBridge(source);
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

    // The application menu of macOS (the one under the name of the app), bound to the current window of the host. Avalonia takes the menu set on the application as
    // this menu only; File and Window belong to each window (CreateWindow, WriteMacMenu.Attach). "Open" with no window makes one through the host, then runs
    // Open on the new window, so the picker has a window to belong to.
    private void AttachMenu(WindowHost host)
    {
        var ui = Dispatcher.UIThread;
        var menu = new WriteMacMenu(
            () => host.Current as IWriteMenuWindow,
            () => WriteWindow.GuardAsync(() => OpenWindowThenPickerAsync(host, ui)),
            WriteMenuPart.App);
        host.CurrentChanged += menu.Refresh;
        NativeMenu.SetMenu(this, menu.Menu);

        // The first window exists already: the menu follows it (and its states) from now on, without waiting for the system to ask.
        menu.Refresh();
    }

    private static async Task OpenWindowThenPickerAsync(WindowHost host, Dispatcher ui)
    {
        host.Reopen();

        // Reopen posted the window to the dispatcher; this runs behind it.
        await ui.InvokeAsync(() => { });
        if (host.Current is IWriteMenuWindow window)
        {
            window.Open();
        }
    }
}
