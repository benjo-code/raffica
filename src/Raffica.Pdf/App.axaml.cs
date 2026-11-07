using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Raffica.Core.Hosting;
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
            var window = new PdfWindow(_options, _settings, _appSettings);
            if (_instance is not null)
            {
                window.Listen(_instance);
            }

            // The system's default app service is made on first use (the offer after the first sharp page, the command); a measurement run has none.
            window.DefaultAppServices = _options.IsMeasuring ? null : Platform.PlatformDefaultAppServices.Create;

            // The update flow is made on first use (the check after the first sharp page, the command); a measurement run has none, so it opens no connection.
            window.UpdateFlows = _options.IsMeasuring ? null : enabled => Updating.UpdateServices.Create(_options.SettingsFile, enabled);

            desktop.MainWindow = window;
            if (_options.IsMeasuring)
            {
                // Held by the watchdog timer and the window events until the run is over.
                new Measurement(_options, window, Console.Out, code => desktop.Shutdown(code)).Start();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
