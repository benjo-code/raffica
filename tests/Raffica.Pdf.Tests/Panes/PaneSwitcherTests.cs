using Avalonia.Headless.XUnit;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Panes;

public sealed class PaneSwitcherTests
{
    [AvaloniaFact]
    public async Task OnePaneIsShownAtATimeAndClosingBothCollapsesTheSlot()
    {
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        Assert.False(window.NavigationPane.IsOpen);
        var tab = await windows.OpenAsync("outline.pdf");
        var view = window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);

        Assert.Equal(204, window.NavigationPane.Width);
        Assert.Equal(LeftPaneKind.Pages, window.LeftPane);
        Assert.IsType<PagesPane>(window.NavigationPane.Content);
        Assert.Equal("Pages", window.NavigationPane.Title);

        window.ShowOutlinePane();
        ViewFixture.Pump();
        Assert.IsType<OutlinePane>(window.NavigationPane.Content);
        Assert.Equal("Outline", window.NavigationPane.Title);
        Assert.Equal(204, window.NavigationPane.Width);

        window.ShowPagesPane();
        Assert.IsType<PagesPane>(window.NavigationPane.Content);

        window.CloseLeftPane();
        Assert.False(window.NavigationPane.IsOpen);
        Assert.Null(window.NavigationPane.Content);
        window.ShowOutlinePane();
        Assert.True(window.NavigationPane.IsOpen);
        Assert.Equal(LeftPaneKind.Outline, window.LeftPane);
    }

    [AvaloniaFact]
    public async Task EachTabHasItsOwnPanesAndAClosedTabLeavesNothingAlive()
    {
        using var windows = WindowFixture.Create();
        var window = windows.Window;
        var first = await windows.OpenAsync("outline.pdf");
        await ViewFixture.WaitAsync(() => window.ViewOf(first)!.FirstSharpPageShown);
        var firstPane = (PagesPane)window.NavigationPane.Content!;
        await ViewFixture.WaitAsync(() => firstPane.LastPlan.Count > 0);

        var second = await windows.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => window.ViewOf(second)!.FirstSharpPageShown);
        var secondPane = (PagesPane)window.NavigationPane.Content!;
        Assert.NotSame(firstPane, secondPane);

        // The pane of the tab that is not shown requests nothing.
        Assert.Empty(firstPane.LastPlan);
        await ViewFixture.WaitAsync(() => secondPane.LastPlan.Count > 0);

        // The outline is read once per tab, on first show; going back and forth does not read it again.
        window.ShowOutlinePane();
        var outline = (OutlinePane)window.NavigationPane.Content!;
        await outline.LoadTask;
        window.ShowPagesPane();
        window.ShowOutlinePane();
        Assert.Same(outline, window.NavigationPane.Content);
        Assert.Equal(1, outline.ReadCount);

        await window.Workspace.CloseAsync(second);
        ViewFixture.Pump();
        Assert.True(secondPane.IsDisposed);
        Assert.True(outline.IsDisposed);
        Assert.False(firstPane.IsDisposed);
    }
}
