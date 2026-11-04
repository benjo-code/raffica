using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Settings;
using Raffica.Core.Viewing;
using Raffica.Pdf.Settings;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Xunit;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

namespace Raffica.Pdf.Tests.Settings;

public sealed class PdfSettingsTests
{
    private static T Find<T>(PdfWindow window, string name) where T : Visual =>
        window.Host.ActiveDialog!.GetVisualDescendants().OfType<T>().Single(c => c is Control control && control.Name == name);

    private static void Click(PdfWindow window, string name)
    {
        Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
    }

    private static void Restore() => ShellTheme.Apply(Application.Current!, ThemeMode.System, AppInk.Pdf, null);

    [AvaloniaFact]
    public void DialogHasNoAutosaveRow()
    {
        using var windows = WindowFixture.Create();
        windows.Window.Settings.Open();
        ViewFixture.Pump();

        var dialog = windows.Window.Host.ActiveDialog!;
        var names = dialog.GetVisualDescendants().OfType<Control>().Select(c => c.Name).ToList();
        Assert.DoesNotContain("PART_Autosave", names);
        Assert.Contains("PART_Theme", names);
        Assert.Contains("PART_Language", names);
        Assert.Contains("PART_Compact", names);
        Assert.Contains("PART_PageAppearance", names);
        Assert.Contains("PART_DefaultLayout", names);
        Assert.Contains("PART_DefaultZoom", names);
        Click(windows.Window, "PART_Cancel");
        Assert.False(windows.Window.Host.IsOpen);
    }

    [AvaloniaFact]
    public void CtrlCommaAndTheCommandOpenTheDialogWithoutADocument()
    {
        using var windows = WindowFixture.Create();
        var command = windows.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "file.settings");
        Assert.True(command.IsEnabled);

        command.Run!.Invoke();
        ViewFixture.Pump();
        Assert.True(windows.Window.Host.IsOpen);
        Click(windows.Window, "PART_Cancel");

        windows.Window.KeyPress(Avalonia.Input.Key.OemComma, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Comma, null);
        windows.Window.KeyRelease(Avalonia.Input.Key.OemComma, Avalonia.Input.RawInputModifiers.Control, Avalonia.Input.PhysicalKey.Comma, null);
        ViewFixture.Pump();
        Assert.True(windows.Window.Host.IsOpen);
    }

    [AvaloniaFact]
    public void ExtraRowsShowTheStoredValues()
    {
        using var windows = WindowFixture.Create(prepare: folder =>
            Assert.True(new Raffica.Core.Settings.SettingsStore(Path.Combine(folder, "pdf-settings.json")).TrySave(
                AppSettings.Default with
                {
                    PageAppearance = PageAppearanceSetting.FollowTheme,
                    DefaultLayout = PageLayoutMode.TwoPages,
                    DefaultZoomMode = ZoomMode.FitPage,
                })));
        windows.Window.Settings.Open();
        ViewFixture.Pump();

        var appearance = Find<SegmentedControl>(windows.Window, "PART_PageAppearance");
        var layout = Find<SelectBox>(windows.Window, "PART_DefaultLayout");
        var zoom = Find<SelectBox>(windows.Window, "PART_DefaultZoom");
        Assert.Equal(1, appearance.SelectedIndex);
        Assert.Equal(2, layout.SelectedIndex);
        Assert.Equal(2, zoom.SelectedIndex);
        Assert.Equal(4, layout.ItemsSource!.Cast<object>().Count());
        Assert.Equal(3, zoom.ItemsSource!.Cast<object>().Count());
        foreach (var editor in new Control[] { appearance, layout, zoom })
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(editor)));
        }

        Click(windows.Window, "PART_Cancel");
    }

    [AvaloniaFact]
    public void OkWritesBothFilesAndAppliesTheTheme()
    {
        using var windows = WindowFixture.Create();
        try
        {
            windows.Window.Settings.Open();
            ViewFixture.Pump();
            Find<SegmentedControl>(windows.Window, "PART_Theme").SelectedIndex = 1;
            Find<CheckBox>(windows.Window, "PART_Compact").IsChecked = true;
            Find<SelectBox>(windows.Window, "PART_DefaultLayout").SelectedIndex = 1;
            Click(windows.Window, "PART_Ok");

            Assert.False(windows.Window.Host.IsOpen);
            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
            Assert.True(windows.Window.Frame.IsCompact);
            Assert.True(windows.Window.Ribbon.IsCompact);

            var shell = new ShellSettingsStore(Path.Combine(windows.Folder, "settings.json")).Load();
            Assert.Equal(ThemeMode.Dark, shell.Theme);
            Assert.True(shell.CompactCommandBar);
            Assert.True(shell.Autosave);
            var app = new Raffica.Core.Settings.SettingsStore(windows.AppSettingsPath).LoadReadOnly();
            Assert.Equal(PageLayoutMode.SinglePage, app.DefaultLayout);
        }
        finally
        {
            Restore();
        }
    }

    [AvaloniaFact]
    public async Task PageAppearanceReachesOpenTabs()
    {
        using var windows = WindowFixture.Create();
        try
        {
            var first = await windows.OpenAsync("text-basic.pdf");
            var second = await windows.OpenAsync("hello_world.pdf");
            var views = new[] { windows.Window.ViewOf(first)!, windows.Window.ViewOf(second)! };

            // Dark theme and "follow theme": both open tabs get dark pages.
            windows.Window.Settings.Open();
            ViewFixture.Pump();
            Find<SegmentedControl>(windows.Window, "PART_Theme").SelectedIndex = 1;
            Find<SegmentedControl>(windows.Window, "PART_PageAppearance").SelectedIndex = 1;
            Click(windows.Window, "PART_Ok");
            Assert.All(views, v => Assert.Equal(PageTheme.Dark, v.PageTheme));
            Assert.All(new[] { first, second }, t => Assert.Equal(PageTheme.Dark, t.Session.PageTheme));

            // The per-tab switch only changes the active tab; the next change of the setting overrides it again.
            windows.Window.ToggleDarkPage();
            Assert.Equal(PageTheme.Light, views[1].PageTheme);
            Assert.Equal(PageTheme.Dark, views[0].PageTheme);
            windows.Window.Settings.Open();
            ViewFixture.Pump();
            Find<SegmentedControl>(windows.Window, "PART_PageAppearance").SelectedIndex = 0;
            Click(windows.Window, "PART_Ok");
            Assert.All(views, v => Assert.Equal(PageTheme.Light, v.PageTheme));

            // A tab opened later starts from the setting.
            windows.Window.Settings.Open();
            ViewFixture.Pump();
            Find<SegmentedControl>(windows.Window, "PART_PageAppearance").SelectedIndex = 1;
            Click(windows.Window, "PART_Ok");
            var third = await windows.OpenAsync("pages-marked.pdf");
            Assert.Equal(PageTheme.Dark, windows.Window.ViewOf(third)!.PageTheme);
        }
        finally
        {
            Restore();
        }
    }

    [AvaloniaFact]
    public async Task DefaultsApplyToTheNextTabOnly()
    {
        using var windows = WindowFixture.Create();
        var first = await windows.OpenAsync("text-basic.pdf");
        var firstView = windows.Window.ViewOf(first)!;
        Assert.Equal(PageLayoutMode.Continuous, firstView.Layout);
        Assert.Equal(ZoomMode.ActualSize, firstView.ZoomMode);

        windows.Window.Settings.Open();
        ViewFixture.Pump();
        Find<SelectBox>(windows.Window, "PART_DefaultLayout").SelectedIndex = 2;
        Find<SelectBox>(windows.Window, "PART_DefaultZoom").SelectedIndex = 1;
        Click(windows.Window, "PART_Ok");

        Assert.Equal(PageLayoutMode.Continuous, firstView.Layout);
        Assert.Equal(ZoomMode.ActualSize, firstView.ZoomMode);
        var second = await windows.OpenAsync("hello_world.pdf");
        var secondView = windows.Window.ViewOf(second)!;
        Assert.Equal(PageLayoutMode.TwoPages, secondView.Layout);
        Assert.Equal(ZoomMode.FitWidth, secondView.ZoomMode);
    }

    [Fact]
    public void CorruptSettingsFilesGiveDefaults()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-corrupt-").FullName;
        try
        {
            var shellPath = Path.Combine(folder, "settings.json");
            File.WriteAllText(shellPath, "{ not json");
            File.WriteAllText(PdfSettingsFiles.PdfSettingsPathFor(shellPath), "\0\0 {{{");

            Assert.Equal(new ShellSettings(), new ShellSettingsStore(PdfSettingsFiles.ShellPathFor(shellPath)).Load());
            Assert.Equal(AppSettings.Default, new Raffica.Core.Settings.SettingsStore(PdfSettingsFiles.PdfSettingsPathFor(shellPath)).LoadReadOnly());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void CorruptFilesShowDefaultsInTheDialog()
    {
        using var windows = WindowFixture.Create(prepare: folder => File.WriteAllText(Path.Combine(folder, "pdf-settings.json"), "garbage"));
        windows.Window.Settings.Open();
        ViewFixture.Pump();
        Assert.Equal(0, Find<SegmentedControl>(windows.Window, "PART_PageAppearance").SelectedIndex);
        Assert.Equal(0, Find<SelectBox>(windows.Window, "PART_DefaultLayout").SelectedIndex);
        Assert.Equal(0, Find<SelectBox>(windows.Window, "PART_DefaultZoom").SelectedIndex);
        Click(windows.Window, "PART_Cancel");
    }

    [AvaloniaFact]
    public void SettingsFileSwitchNeverTouchesTheUsersFolder()
    {
        var userFiles = new[] { PdfSettingsFiles.ShellPath, PdfSettingsFiles.PdfSettingsPath, PdfSettingsFiles.RecentPath };
        var before = userFiles.Select(Stamp).ToList();

        using var windows = WindowFixture.Create();
        try
        {
            var shellPath = Path.Combine(windows.Folder, "settings.json");
            Assert.Equal(shellPath, PdfSettingsFiles.ShellPathFor(shellPath));
            Assert.Equal(windows.AppSettingsPath, PdfSettingsFiles.PdfSettingsPathFor(shellPath));
            Assert.Equal(windows.RecentPath, PdfSettingsFiles.RecentPathFor(shellPath));

            windows.Window.Settings.Open();
            ViewFixture.Pump();
            Find<SegmentedControl>(windows.Window, "PART_Theme").SelectedIndex = 2;
            Click(windows.Window, "PART_Ok");

            Assert.True(File.Exists(shellPath));
            Assert.True(File.Exists(windows.AppSettingsPath));
            Assert.Equal(before, userFiles.Select(Stamp).ToList());
        }
        finally
        {
            Restore();
        }
    }

    private static (bool Exists, DateTime Written) Stamp(string path) => (File.Exists(path), File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default);
}
