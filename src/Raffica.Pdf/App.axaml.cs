using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Raffica.Shell;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;

namespace Raffica.Pdf;

internal sealed partial class App : Application
{
    private readonly PdfOptions _options;
    private readonly SettingsStore _store;
    private readonly ShellSettings _settings;

    public App(PdfOptions options)
    {
        _options = options;
        _store = new SettingsStore(options.SettingsFile ?? SettingsStore.DefaultPath("Pdf"));
        _settings = _store.Load();
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
            desktop.MainWindow = new PdfWindow(_options, _settings);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
