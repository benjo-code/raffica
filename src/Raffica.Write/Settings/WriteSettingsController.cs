using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Core.Viewing;
using Raffica.Hosting;
using Raffica.Shell;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

namespace Raffica.Write.Settings;

/// <summary>
/// Opens the settings dialog of the shell with the Write additions (the default view and the default zoom, and the recovery-copy row) and applies
/// what the user confirms. The two settings files are written on OK: the shell file (theme, language, compact bar) and the Write file
/// (<see cref="WriteSettings"/>). Theme and compact bar apply at once, the view and zoom to tabs opened later (the controller keeps them in memory, so opening a tab
/// reads no file); a language applies after a restart, which the dialog says. The choices are applied in memory first, then saved; the Write file is written by a
/// background writer (one writer, the newest settings win), so no click waits for the disk for it.
/// </summary>
internal sealed class WriteSettingsController
{
    // The zoom modes a default may have: Free is "stay as it is" and no default (WriteSettings.Normalized repairs it).
    private static readonly ZoomMode[] Zooms = [ZoomMode.ActualSize, ZoomMode.FitWidth, ZoomMode.FitPage];
    private static readonly WriteView[] Views = [WriteView.Page, WriteView.Writing];

    private readonly WriteOptions _options;
    private readonly DialogHost _host;
    private readonly Action<bool> _setCompact;
    private readonly CoalescingWriter _appWriter = new();
    private ShellSettings _shell;

    /// <param name="options">The command line (its <c>--settings-file</c> decides where the files are).</param>
    /// <param name="host">Where the dialog is shown.</param>
    /// <param name="shell">The shell settings read at startup.</param>
    /// <param name="app">The Write settings read once at startup.</param>
    /// <param name="setCompact">Applies the compact command bar to the frame.</param>
    public WriteSettingsController(WriteOptions options, DialogHost host, ShellSettings shell, WriteSettings app, Action<bool> setCompact)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(setCompact);
        _options = options;
        _host = host;
        _shell = shell;
        App = app;
        _setCompact = setCompact;
    }

    /// <summary>The Write settings in force: what the file said at startup, then what the user confirmed. A new tab starts from them.</summary>
    public WriteSettings App { get; private set; }

    /// <summary>The shell settings in force (what the file said at startup, then what the user confirmed and where the window closed).</summary>
    public ShellSettings Shell => _shell;

    /// <summary>
    /// Remembers where the window was when it closed: sets <see cref="ShellSettings.WindowPlacement"/> and writes the shell file once, at once (the window
    /// is closing). Nothing is written when the placement is the one already stored. A file that cannot be written leaves it for this run only; never throws.
    /// </summary>
    public void RememberWindow(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!placement.IsUsable || Equals(_shell.WindowPlacement, placement))
        {
            return;
        }

        _shell = _shell with { WindowPlacement = placement };
        _ = new ShellSettingsStore(AppFiles.Write.ShellPathFor(_options.SettingsFile)).TrySave(_shell);
    }

    /// <summary>Completes when the Write file has been written (tests wait for it instead of sleeping).</summary>
    public Task FlushAsync() => _appWriter.FlushAsync();

    /// <summary>Waits for the Write file to be written, for at most <paramref name="timeout"/> (the window closing); returns whether it was.</summary>
    public bool Flush(TimeSpan timeout) => _appWriter.Flush(timeout);

    /// <summary>Shows the dialog; does nothing while another dialog is open.</summary>
    public void Open()
    {
        if (_host.IsOpen)
        {
            return;
        }

        var view = new SegmentedControl
        {
            Name = "PART_DefaultView",
            ItemsSource = new[] { WriteStrings.ViewPage, WriteStrings.ViewWriting },
            SelectedIndex = Math.Max(0, Array.IndexOf(Views, App.DefaultView)),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(view, WriteStrings.SettingsDefaultView);

        var zoom = new SelectBox
        {
            Name = "PART_DefaultZoom",
            ItemsSource = new[] { WriteStrings.ZoomActualSize, WriteStrings.ZoomFitWidth, WriteStrings.ZoomFitPage },
            SelectedIndex = Math.Max(0, Array.IndexOf(Zooms, App.DefaultZoomMode)),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 200,
        };
        AutomationProperties.SetName(zoom, WriteStrings.SettingsDefaultZoom);

        List<SettingsExtraRow> rows =
        [
            new(WriteStrings.SettingsDefaultView, view),
            new(WriteStrings.SettingsDefaultZoom, zoom),
        ];

        _host.Show(SettingsDialog.Create(
            _shell,
            shell =>
            {
                _host.Close();
                Apply(shell, App with
                {
                    DefaultView = Views[Math.Max(0, view.SelectedIndex)],
                    DefaultZoomMode = Zooms[Math.Max(0, zoom.SelectedIndex)],
                });
            },
            _host.Close,
            new SettingsDialogOptions(ShowAutosave: true, ExtraRows: rows, AutosaveLabel: WriteStrings.SettingsAutosaveLabel)));
    }

    private void Apply(ShellSettings shell, WriteSettings app)
    {
        // In memory first: whatever happens to the files, the choice is in force for this run.
        _shell = shell;
        App = app;
        if (Application.Current is { } current)
        {
            ShellTheme.Apply(current, shell.Theme, AppInk.Write, _options.HighContrast ? true : null);
        }

        _setCompact(shell.CompactCommandBar);

        // Settings are never a reason to fail: a file that cannot be written leaves the choice in force for this run only.
        _ = new ShellSettingsStore(AppFiles.Write.ShellPathFor(_options.SettingsFile)).TrySave(shell);
        var path = AppFiles.Write.AppSettingsPathFor(_options.SettingsFile);
        _appWriter.Post(() => _ = new WriteSettingsStore(path).TrySave(app));
    }
}
