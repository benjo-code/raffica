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
    private readonly SettingsStore _store;
    private readonly ShellSettings _settings;
    private readonly Raffica.Core.Settings.AppSettings _appSettings;

    public App(PdfOptions options, SingleInstance? instance = null)
    {
        _options = options;
        _instance = instance;
        _store = new SettingsStore(PdfSettingsFiles.ShellPathFor(options.SettingsFile));
        _settings = _store.Load();

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

            var window = new PdfWindow(_options, _settings, _appSettings);
            if (_instance is not null)
            {
                window.Listen(_instance);
            }

            // The system's default app service is made on first use (the offer after the first sharp page, the command); a measurement run has none.
            window.DefaultAppServices = _options.IsMeasuring ? null : Platform.PlatformDefaultAppServices.Create;

            // The update flow is made on first use (the check after the first sharp page, the command); a measurement run has none, so it opens no connection.
            // The installer of the system ends the app through the window (its close question was answered before the installer was called) and the lifetime.
            window.UpdateFlows = Updating.UpdateServices.MeasurementKeepsUpdatesOff(_options)
                ? null
                : enabled => Updating.UpdateServices.Create(_options.SettingsFile, enabled, () => window.EndForUpdate(() => desktop.Shutdown()));

            desktop.MainWindow = window;
            if (_options.IsMeasuring)
            {
                // Held by the watchdog timer and the window events until the run is over.
                new Measurement(_options, window, Console.Out, code => desktop.Shutdown(code)).Start();
            }
        }

        base.OnFrameworkInitializationCompleted();
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
