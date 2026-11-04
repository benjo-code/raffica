using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Raffica.Core.Pdf;
using Raffica.Core.Settings;
using Raffica.Core.Viewing;
using Raffica.Shell;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

namespace Raffica.Pdf.Settings;

/// <summary>
/// Opens the settings dialog of the shell with the PDF additions (page appearance, default layout and default zoom; no autosave row in
/// 2.1) and applies what the user confirms. The two settings files are written on OK: the shell file (theme, language, compact bar) and
/// the viewer file (<see cref="AppSettings"/>). Theme and compact bar apply at once, the page appearance to every open tab, the default
/// layout and zoom to tabs opened later (the controller keeps the current <see cref="App"/> in memory, so opening a tab reads no file);
/// a language applies after a restart, which the dialog says.
/// </summary>
internal sealed class PdfSettingsController
{
    // The zoom modes a default may have: Free is "stay as it is" and no default (AppSettings.Normalized repairs it).
    private static readonly ZoomMode[] Zooms = [ZoomMode.ActualSize, ZoomMode.FitWidth, ZoomMode.FitPage];
    private static readonly PageLayoutMode[] Layouts = [PageLayoutMode.Continuous, PageLayoutMode.SinglePage, PageLayoutMode.TwoPages, PageLayoutMode.TwoPagesWithCover];
    private static readonly PageAppearanceSetting[] Appearances = [PageAppearanceSetting.Light, PageAppearanceSetting.FollowTheme];

    private readonly PdfOptions _options;
    private readonly DialogHost _host;
    private readonly Action<bool> _setCompact;
    private readonly Action<PageTheme> _setPageTheme;
    private ShellSettings _shell;

    /// <param name="options">The command line (its <c>--settings-file</c> decides where the files are).</param>
    /// <param name="host">Where the dialog is shown.</param>
    /// <param name="shell">The shell settings read at startup.</param>
    /// <param name="app">The viewer settings read once at startup.</param>
    /// <param name="setCompact">Applies the compact command bar to the frame and the ribbon.</param>
    /// <param name="setPageTheme">Gives the pages of every open tab a theme.</param>
    public PdfSettingsController(PdfOptions options, DialogHost host, ShellSettings shell, AppSettings app, Action<bool> setCompact, Action<PageTheme> setPageTheme)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(setCompact);
        ArgumentNullException.ThrowIfNull(setPageTheme);
        _options = options;
        _host = host;
        _shell = shell;
        App = app;
        _setCompact = setCompact;
        _setPageTheme = setPageTheme;
    }

    /// <summary>The viewer settings in force: what the file said at startup, then what the user confirmed. A new tab starts from them.</summary>
    public AppSettings App { get; private set; }

    /// <summary>The theme a new tab's pages start with: dark only when the setting follows the theme and the theme is dark.</summary>
    public PageTheme PageThemeForNewTab => PageThemeFor(App.PageAppearance);

    /// <summary>
    /// Remembers the printer a job went to: sets <see cref="AppSettings.LastPrinter"/> in memory and writes the viewer file. The other fields are
    /// the ones in force; a file that cannot be written leaves the choice for this run only. Never throws.
    /// </summary>
    public void RememberPrinter(string? printer)
    {
        if (string.IsNullOrWhiteSpace(printer) || printer.Length > AppSettings.MaxPrinterNameLength || string.Equals(App.LastPrinter, printer, StringComparison.Ordinal))
        {
            return;
        }

        App = App with { LastPrinter = printer };
        try
        {
            _ = new Raffica.Core.Settings.SettingsStore(PdfSettingsFiles.PdfSettingsPathFor(_options.SettingsFile)).TrySave(App);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The printer is remembered until the app ends.
        }
    }

    /// <summary>Shows the dialog; does nothing while another dialog is open.</summary>
    public void Open()
    {
        if (_host.IsOpen)
        {
            return;
        }

        var appearance = new SegmentedControl
        {
            Name = "PART_PageAppearance",
            ItemsSource = new[] { PdfStrings.SettingsPageLight, PdfStrings.SettingsPageFollowTheme },
            SelectedIndex = Math.Max(0, Array.IndexOf(Appearances, App.PageAppearance)),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(appearance, PdfStrings.SettingsPageAppearance);

        var layout = new SelectBox
        {
            Name = "PART_DefaultLayout",
            ItemsSource = new[] { PdfStrings.CmdContinuous, PdfStrings.CmdSinglePage, PdfStrings.CmdTwoPages, PdfStrings.CmdTwoPagesCover },
            SelectedIndex = Math.Max(0, Array.IndexOf(Layouts, App.DefaultLayout)),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 200,
        };
        AutomationProperties.SetName(layout, PdfStrings.SettingsDefaultLayout);

        var zoom = new SelectBox
        {
            Name = "PART_DefaultZoom",
            ItemsSource = new[] { PdfStrings.CmdActualSize, PdfStrings.CmdFitWidth, PdfStrings.CmdFitPage },
            SelectedIndex = Math.Max(0, Array.IndexOf(Zooms, App.DefaultZoomMode)),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 200,
        };
        AutomationProperties.SetName(zoom, PdfStrings.SettingsDefaultZoom);

        var options = new SettingsDialogOptions(
            ShowAutosave: false,
            ExtraRows:
            [
                new SettingsExtraRow(PdfStrings.SettingsPageAppearance, appearance),
                new SettingsExtraRow(PdfStrings.SettingsDefaultLayout, layout),
                new SettingsExtraRow(PdfStrings.SettingsDefaultZoom, zoom),
            ]);

        _host.Show(SettingsDialog.Create(
            _shell,
            shell =>
            {
                _host.Close();
                Apply(
                    shell,
                    App with
                    {
                        PageAppearance = Appearances[Math.Max(0, appearance.SelectedIndex)],
                        DefaultLayout = Layouts[Math.Max(0, layout.SelectedIndex)],
                        DefaultZoomMode = Zooms[Math.Max(0, zoom.SelectedIndex)],
                    });
            },
            _host.Close,
            options));
    }

    private void Apply(ShellSettings shell, AppSettings app)
    {
        _shell = shell;
        App = app;
        Save(shell, app);

        // The theme first: "follow the theme" for the pages asks the theme that is in force afterwards.
        if (Application.Current is { } current)
        {
            ShellTheme.Apply(current, shell.Theme, AppInk.Pdf, _options.HighContrast ? true : null);
        }

        _setCompact(shell.CompactCommandBar);

        // A change of the setting overrides the per-tab dark page switch of the open tabs; tabs opened later start from it.
        _setPageTheme(PageThemeFor(app.PageAppearance));
    }

    // Settings are never a reason to fail: a file that cannot be written leaves the choice in force for this run only.
    private void Save(ShellSettings shell, AppSettings app)
    {
        try
        {
            new ShellSettingsStore(PdfSettingsFiles.ShellPathFor(_options.SettingsFile)).Save(shell);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The choice stays in force until the app ends.
        }

        _ = new Raffica.Core.Settings.SettingsStore(PdfSettingsFiles.PdfSettingsPathFor(_options.SettingsFile)).TrySave(app);
    }

    private static PageTheme PageThemeFor(PageAppearanceSetting appearance) =>
        appearance == PageAppearanceSetting.FollowTheme && IsDark(Application.Current?.ActualThemeVariant) ? PageTheme.Dark : PageTheme.Light;

    // The high contrast variant inherits Dark, so it counts as dark here (as it does for every other resource).
    private static bool IsDark(ThemeVariant? variant) => variant == ThemeVariant.Dark || variant?.InheritVariant == ThemeVariant.Dark;
}
