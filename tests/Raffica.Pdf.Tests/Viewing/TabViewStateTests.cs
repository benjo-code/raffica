using Avalonia;
using Avalonia.Headless.XUnit;
using Raffica.Core.Settings;
using Raffica.Core.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class TabViewStateTests
{
    private static RememberedView Remembered(int page, double fraction, double zoom, ZoomMode mode, PageLayoutMode layout) =>
        new(new ViewState(new PageAnchor(page, fraction), 0), zoom, mode, layout);

    private static void WriteRecent(string folder, string pdf, RememberedView view)
    {
        var recent = new RecentFiles();
        recent.Remember(Path.GetFullPath(TestData.Pdf(pdf)), view, DateTimeOffset.UtcNow);
        Assert.True(new RecentFilesStore(Path.Combine(folder, "recent.json")).TrySave(recent));
    }

    [AvaloniaFact]
    public async Task SwitchingTabsRestoresPageZoomAndPosition()
    {
        using var windows = WindowFixture.Create();
        var first = await windows.OpenAsync("pages-marked.pdf");
        var firstView = windows.Window.ViewOf(first)!;
        firstView.GoToPage(2);
        firstView.ZoomIn();
        firstView.Offset = new Vector(firstView.Offset.X, firstView.Offset.Y + 70);
        ViewFixture.Pump();
        var page = firstView.CurrentPage;
        var zoom = firstView.Zoom;
        var offset = firstView.Offset;
        var captured = firstView.CaptureView();
        Assert.Equal(1.25, zoom);

        var second = await windows.OpenAsync("text-basic.pdf");
        var secondView = windows.Window.ViewOf(second)!;
        secondView.ZoomIn();
        secondView.ZoomIn();
        Assert.NotSame(firstView, secondView);
        Assert.Same(secondView, windows.Window.DocumentPane.Content);

        windows.Window.Workspace.Select(first);
        ViewFixture.Pump();
        Assert.Same(firstView, windows.Window.DocumentPane.Content);
        Assert.Equal(page, firstView.CurrentPage);
        Assert.Equal(zoom, firstView.Zoom);
        Assert.Equal(offset, firstView.Offset);
        Assert.Equal(captured.Anchor.Page, firstView.CaptureView().Anchor.Page);
        Assert.Equal(captured.Anchor.Fraction, firstView.CaptureView().Anchor.Fraction, 6);

        // The other tab kept its own zoom, and the status bar follows the tab that is shown.
        Assert.Equal(1.5, secondView.Zoom);
        Assert.Equal("125%", windows.Window.Status.ZoomText);
        windows.Window.Workspace.Select(second);
        ViewFixture.Pump();
        Assert.Equal("150%", windows.Window.Status.ZoomText);
    }

    [AvaloniaFact]
    public async Task NewTabStartsWithTheDefaults()
    {
        // Without a settings file: continuous, 100 %.
        using (var plain = WindowFixture.Create())
        {
            var tab = await plain.OpenAsync("pages-marked.pdf");
            var view = plain.Window.ViewOf(tab)!;
            Assert.Equal(PageLayoutMode.Continuous, view.Layout);
            Assert.Equal(ZoomMode.ActualSize, view.ZoomMode);
            Assert.Equal(1.0, view.Zoom);
            Assert.Equal(0, view.Offset.Y);
        }

        // The defaults of the settings file.
        using var configured = WindowFixture.Create(folder =>
            Assert.True(new SettingsStore(Path.Combine(folder, "pdf-settings.json")).TrySave(
                AppSettings.Default with { DefaultLayout = PageLayoutMode.TwoPages, DefaultZoomMode = ZoomMode.FitWidth })));
        var configuredTab = await configured.OpenAsync("pages-marked.pdf");
        var configuredView = configured.Window.ViewOf(configuredTab)!;
        Assert.Equal(PageLayoutMode.TwoPages, configuredView.Layout);
        Assert.Equal(ZoomMode.FitWidth, configuredView.ZoomMode);
        Assert.NotEqual(1.0, configuredView.Zoom);

        // A corrupt settings file gives the defaults and is left alone.
        using var corrupt = WindowFixture.Create(folder => File.WriteAllText(Path.Combine(folder, "pdf-settings.json"), "{ this is not json"));
        var corruptTab = await corrupt.OpenAsync("pages-marked.pdf");
        var corruptView = corrupt.Window.ViewOf(corruptTab)!;
        Assert.Equal(PageLayoutMode.Continuous, corruptView.Layout);
        Assert.Equal(ZoomMode.ActualSize, corruptView.ZoomMode);
        Assert.Equal("{ this is not json", File.ReadAllText(corrupt.AppSettingsPath));
    }

    [AvaloniaFact]
    public async Task OpeningTheFirstTabReadsNoSettingsFile()
    {
        using var windows = WindowFixture.Create(folder =>
            Assert.True(new SettingsStore(Path.Combine(folder, "pdf-settings.json")).TrySave(
                AppSettings.Default with { DefaultLayout = PageLayoutMode.TwoPages, DefaultZoomMode = ZoomMode.FitWidth })));

        // The settings were read at startup; a file that is changed or gone afterwards does not matter to a tab.
        File.WriteAllText(windows.AppSettingsPath, "{ not json at all");
        var first = await windows.OpenAsync("pages-marked.pdf");
        Assert.Equal(PageLayoutMode.TwoPages, windows.Window.ViewOf(first)!.Layout);
        Assert.Equal(ZoomMode.FitWidth, windows.Window.ViewOf(first)!.ZoomMode);
        File.Delete(windows.AppSettingsPath);
        var second = await windows.OpenAsync("text-basic.pdf");
        Assert.Equal(PageLayoutMode.TwoPages, windows.Window.ViewOf(second)!.Layout);

        // A window that was handed no settings starts from the defaults without looking at any file.
        var folder = Directory.CreateTempSubdirectory("raffica-nosettings-").FullName;
        try
        {
            Assert.True(new SettingsStore(Path.Combine(folder, "pdf-settings.json")).TrySave(
                AppSettings.Default with { DefaultLayout = PageLayoutMode.SinglePage }));
            var options = PdfOptions.Parse(["--settings-file", Path.Combine(folder, "settings.json")]);
            var window = new PdfWindow(options, new Raffica.Shell.Frame.ShellSettings()) { Width = 800, Height = 600 };
            var tab = await window.Flow.OpenAsync(TestData.Pdf("pages-marked.pdf"));
            Assert.Equal(PageLayoutMode.Continuous, window.ViewOf(tab!)!.Layout);
            foreach (var open in window.Workspace.Tabs)
            {
                open.Dispose();
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ARememberedFileOpensAtItsPosition()
    {
        using var windows = WindowFixture.Create(folder =>
            WriteRecent(folder, "pages-marked.pdf", Remembered(2, 0.25, 1.5, ZoomMode.Free, PageLayoutMode.TwoPages)));
        await windows.LoadRecentAsync();
        Assert.NotNull(windows.Window.Recent.ViewOf(Path.GetFullPath(TestData.Pdf("pages-marked.pdf"))));

        var tab = await windows.OpenAsync("pages-marked.pdf");
        var view = windows.Window.ViewOf(tab)!;
        var captured = view.CaptureView();
        Assert.Equal(PageLayoutMode.TwoPages, view.Layout);
        Assert.Equal(ZoomMode.Free, view.ZoomMode);
        Assert.Equal(1.5, view.Zoom);
        Assert.Equal(2, captured.Anchor.Page);
        Assert.Equal(0.25, captured.Anchor.Fraction, 3);

        // A file that is not in the list still starts from the defaults.
        var other = await windows.OpenAsync("text-basic.pdf");
        Assert.Equal(1.0, windows.Window.ViewOf(other)!.Zoom);
    }

    [AvaloniaFact]
    public async Task ARememberedViewBeyondTheEndOfTheFileIsLimited()
    {
        using var windows = WindowFixture.Create(folder =>
            WriteRecent(folder, "text-basic.pdf", Remembered(5000, 7, 2, ZoomMode.Free, PageLayoutMode.SinglePage)));
        await windows.LoadRecentAsync();

        var tab = await windows.OpenAsync("text-basic.pdf");
        var view = windows.Window.ViewOf(tab)!;
        Assert.InRange(view.CurrentPage, 0, view.PageCount - 1);
        Assert.True(double.IsFinite(view.Offset.Y));
        Assert.NotNull(view.PageBounds(view.CurrentPage));
    }

    [AvaloniaFact]
    public async Task AFileOpenedBeforeTheRecentListIsLoadedOpensAtTheDefault()
    {
        // The window is not shown, so nothing has loaded the list yet (the loading belongs after the first sharp page): the open
        // does not wait for it and does not read it.
        using var windows = WindowFixture.Create(
            folder => WriteRecent(folder, "pages-marked.pdf", Remembered(2, 0.25, 1.5, ZoomMode.Free, PageLayoutMode.TwoPages)),
            show: false);
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var view = windows.Window.ViewOf(tab)!;
        Assert.Equal(PageLayoutMode.Continuous, view.Layout);
        Assert.Equal(1.0, view.Zoom);
    }

    [AvaloniaFact]
    public async Task ClosingATabAndTheWindowRemembersTheView()
    {
        var path = Path.GetFullPath(TestData.Pdf("pages-marked.pdf"));
        var otherPath = Path.GetFullPath(TestData.Pdf("text-basic.pdf"));
        using var windows = WindowFixture.Create();
        await windows.LoadRecentAsync();

        // Tab close.
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var view = windows.Window.ViewOf(tab)!;
        view.SetLayout(PageLayoutMode.TwoPages);
        view.GoToPage(2);
        view.ZoomIn();
        var expected = view.CaptureView();
        Assert.True(await windows.Window.Workspace.CloseAsync(tab));
        ViewFixture.Pump();

        var stored = windows.Window.Recent.ViewOf(path);
        Assert.NotNull(stored);
        Assert.Equal(PageLayoutMode.TwoPages, stored.Layout);
        Assert.Equal(1.25, stored.Zoom);
        Assert.Equal(ZoomMode.Free, stored.ZoomMode);
        Assert.Equal(expected.Anchor.Page, stored.View.Anchor.Page);
        Assert.Equal(expected.Anchor.Fraction, stored.View.Anchor.Fraction, 6);
        await windows.Window.Recent.FlushAsync();
        Assert.NotNull(new RecentFilesStore(windows.RecentPath).Load().TryGet(path)?.View);

        // Window close.
        var again = await windows.OpenAsync("text-basic.pdf");
        windows.Window.ViewOf(again)!.SetZoom(3);
        windows.Window.Close();
        ViewFixture.Pump();
        var saved = new RecentFilesStore(windows.RecentPath).Load().TryGet(otherPath)?.View;
        Assert.NotNull(saved);
        Assert.Equal(3, saved.Zoom);
    }
}
