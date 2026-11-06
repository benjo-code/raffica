using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Viewing;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Panes;

public sealed class PagesPaneTests
{
    private static Task<PaneFixture<PagesPane>> OpenAsync(string file, bool showView = true) =>
        PaneFixture<PagesPane>.OpenAsync(file, (tab, view) => new PagesPane(tab, view), showView);

    [AvaloniaFact]
    public async Task ClickingAThumbnailGoesToItsPage()
    {
        using var f = await OpenAsync("pages-marked.pdf");
        Assert.True(f.View.PageCount >= 4);
        await f.FirstSharpAsync();
        ViewFixture.Pump();

        // The thumbnails of pages 1 and 2 are on screen in the 700 px window; the click goes through the window like a real one.
        var item = f.Pane.ContainerOf(1);
        Assert.NotNull(item);
        f.Click(item);

        Assert.Equal(1, f.View.CurrentPage);
        Assert.Equal(1, f.Pane.MarkedPage);
        Assert.Equal(1, f.Pane.List.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task EnterGoesToTheSelectedPage()
    {
        using var f = await OpenAsync("pages-marked.pdf");
        await f.FirstSharpAsync();
        f.Pane.List.SelectedIndex = 2;
        Assert.True(f.Pane.ContainerOf(2)!.Focus());
        ViewFixture.Pump();
        f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        ViewFixture.Pump();

        Assert.Equal(2, f.View.CurrentPage);
    }

    [AvaloniaFact]
    public async Task OnlyVisibleThumbnailsAreRequested()
    {
        using var f = await OpenAsync("thousand-pages.pdf");
        await f.FirstSharpAsync();
        await ViewFixture.WaitAsync(() => f.Pane.LastPlan.Count > 0);
        var thumbnails = f.Session.EnsureThumbnails()!;

        // Only what the list shows is planned: a handful of the thousand pages, never the planner's maximum.
        Assert.InRange(f.Pane.LastPlan.Count, 1, 24);
        Assert.All(f.Pane.LastPlan, r => Assert.Contains(r.Key.Page, f.Pane.RealizedPages));
        Assert.InRange(thumbnails.Scheduler.PendingCount, 0, 24);

        // After a jump far down the plan follows the list; nothing near the top stays wanted.
        f.Pane.List.ScrollIntoView(700);
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => f.Pane.LastPlan.Count > 0 && f.Pane.LastPlan.All(r => r.Key.Page > 600));
        Assert.InRange(f.Pane.LastPlan.Count, 1, 24);
        Assert.InRange(thumbnails.Scheduler.PendingCount, 0, 24);
        Assert.Equal(1000, f.Pane.List.ItemCount);
        Assert.InRange(f.Pane.RealizedPages.Count, 1, 40);
    }

    [AvaloniaFact]
    public async Task ThumbnailsStartAfterTheFirstSharpPage()
    {
        // The view of the tab is not shown, so it never draws a sharp page: the pane must request nothing.
        using var f = await OpenAsync("pages-marked.pdf", showView: false);
        for (var i = 0; i < 8; i++)
        {
            await Task.Delay(25);
            ViewFixture.Pump();
        }

        Assert.False(f.View.FirstSharpPageShown);
        Assert.Empty(f.Pane.LastPlan);
        Assert.False(f.Pane.HasStarted);

        // Showing the view draws the first sharp page, and the thumbnails follow.
        var second = new Window { Width = 800, Height = 600, Content = f.View };
        second.Show();
        try
        {
            await f.FirstSharpAsync();
            await ViewFixture.WaitAsync(() => f.Pane.LastPlan.Count > 0);
            Assert.True(f.Pane.HasStarted);
            var thumbnails = f.Session.EnsureThumbnails()!;
            await ViewFixture.WaitAsync(() => thumbnails.Cache.Contains(new TileKey(0, TileKey.PreviewScaleKey, 0, 0)));
        }
        finally
        {
            second.Content = null;
            second.Close();
        }
    }

    [AvaloniaFact]
    public async Task CurrentPageFollowsTheView()
    {
        using var f = await OpenAsync("pages-marked.pdf");
        await f.FirstSharpAsync();
        Assert.Equal(0, f.Pane.MarkedPage);

        f.View.GoToPage(3);
        ViewFixture.Pump();
        Assert.Equal(3, f.Pane.MarkedPage);
        Assert.Equal(3, f.Pane.List.SelectedIndex);
        Assert.Contains("current", f.Pane.ContainerOf(3)!.Classes);
        Assert.DoesNotContain("current", f.Pane.ContainerOf(0)?.Classes ?? []);

        f.View.GoToPage(1);
        ViewFixture.Pump();
        Assert.Equal(1, f.Pane.MarkedPage);
        Assert.Contains("current", f.Pane.ContainerOf(1)!.Classes);
        Assert.DoesNotContain("current", f.Pane.ContainerOf(3)!.Classes);
    }

    [AvaloniaFact]
    public async Task EveryThumbnailHasANameAndTheCurrentOneSaysSo()
    {
        using var f = await OpenAsync("pages-marked.pdf");
        await f.FirstSharpAsync();
        Assert.Equal("Page 2", AutomationProperties.GetName(f.Pane.ContainerOf(1)!));
        Assert.Equal("Page 1, current page", AutomationProperties.GetName(f.Pane.ContainerOf(0)!));
        Assert.Equal("Pages", AutomationProperties.GetName(f.Pane.List));

        f.View.GoToPage(1);
        ViewFixture.Pump();
        Assert.Equal("Page 2, current page", AutomationProperties.GetName(f.Pane.ContainerOf(1)!));
        Assert.Equal("Page 1", AutomationProperties.GetName(f.Pane.ContainerOf(0)!));
    }

    [AvaloniaFact]
    public async Task ThumbnailsAreDrawnAndAPaneThatLeavesTheScreenRequestsNothing()
    {
        using var f = await OpenAsync("pages-marked.pdf");
        await f.FirstSharpAsync();
        var thumbnails = f.Session.EnsureThumbnails()!;
        await ViewFixture.WaitAsync(() => thumbnails.Cache.Contains(new TileKey(0, TileKey.PreviewScaleKey, 0, 0)));
        ViewFixture.Pump();
        f.Window.CaptureRenderedFrame();
        Assert.True(f.Pane.Bitmaps.Count > 0);

        // Out of the tree (the tab is not shown): no wish, no pending work, no converted bitmaps.
        var left = (Raffica.Shell.Frame.Pane)f.Pane.Parent!;
        left.Content = null;
        ViewFixture.Pump();
        Assert.Empty(f.Pane.LastPlan);
        Assert.Equal(0, thumbnails.Scheduler.PendingCount);
        Assert.Equal(0, f.Pane.Bitmaps.Count);
    }

    [AvaloniaFact]
    public async Task ADisposedPaneLetsGoOfTheViewAndTheSession()
    {
        using var f = await OpenAsync("pages-marked.pdf");
        await f.FirstSharpAsync();
        f.Pane.Dispose();
        f.Pane.Dispose();
        f.View.GoToPage(2);
        ViewFixture.Pump();
        Assert.Equal(0, f.Pane.MarkedPage);
        Assert.Empty(f.Pane.LastPlan);
        Assert.Equal(0, f.Pane.Bitmaps.Count);
    }

    [AvaloniaFact]
    public async Task TheNamesAreGermanInGerman()
    {
        PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
        using var f = await OpenAsync("pages-marked.pdf");
        await f.FirstSharpAsync();
        Assert.Equal("Seite 2", AutomationProperties.GetName(f.Pane.ContainerOf(1)!));
        Assert.Equal("Seite 1, aktuelle Seite", AutomationProperties.GetName(f.Pane.ContainerOf(0)!));
    }

    [AvaloniaFact]
    public async Task NavigationListIsUnchangedOutsideTheMode()
    {
        using var f = await OpenAsync("pages-marked.pdf");
        await f.FirstSharpAsync();
        var navigation = f.Pane.List;
        Assert.Same(navigation, f.Pane.Content);
        Assert.Null(f.Pane.Organize);

        // Nothing of the mode exists before it starts; the session made by asking for it does not start it.
        _ = f.Tab.Organize;
        Assert.Same(navigation, f.Pane.Content);
        f.Tab.Organize.Enter();
        ViewFixture.Pump();
        Assert.NotNull(f.Pane.Organize);
        Assert.Same(f.Pane.Organize, f.Pane.Content);
        Assert.Equal(6, f.Pane.Organize.Items.Count);
        await ViewFixture.WaitAsync(() => f.Pane.LastPlan.Count == 0);

        var organize = f.Pane.Organize;
        f.Tab.Organize.End();
        ViewFixture.Pump();
        Assert.Null(f.Pane.Organize);
        Assert.Same(navigation, f.Pane.Content);
        Assert.True(organize.IsDisposed);
        await ViewFixture.WaitAsync(() => f.Pane.LastPlan.Count > 0);
        Assert.Equal(f.View.CurrentPage, f.Pane.List.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task OrganizeListGetsItsThumbnailsAfterTheNavigationListStopped()
    {
        // The navigation list requested first (it shares the tab's one thumbnail source with the organize list); then the mode starts.
        using var f = await OpenAsync("pages-marked.pdf");
        await f.FirstSharpAsync();
        var thumbnails = f.Session.EnsureThumbnails()!;
        await ViewFixture.WaitAsync(() => f.Pane.LastPlan.Count > 0 && thumbnails.Cache.Contains(new TileKey(0, TileKey.PreviewScaleKey, 0, 0)));

        f.Tab.Organize.Enter();
        ViewFixture.Pump();
        var organize = f.Pane.Organize!;
        Assert.Empty(f.Pane.LastPlan);

        // The pictures come back for the organize list, drawn from its own conversion of the shared cache.
        await ViewFixture.WaitAsync(() => organize.Thumbnails.PlanOf(default).Count > 0);
        var first = f.Tab.Organize.Plan.Slots[0];
        await ViewFixture.WaitAsync(() => organize.Thumbnails.BitmapFor(first) is not null);
        Assert.NotEmpty(organize.Thumbnails.PlanOf(default));
        Assert.True(thumbnails.Cache.Contains(new TileKey(0, TileKey.PreviewScaleKey, 0, 0)));

        // And back: ending the mode gives the navigation list its pictures again.
        f.Tab.Organize.End();
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => f.Pane.LastPlan.Count > 0 && thumbnails.Cache.Contains(new TileKey(0, TileKey.PreviewScaleKey, 0, 0)));
        Assert.NotNull(f.Pane.BitmapFor(0));
    }
}
