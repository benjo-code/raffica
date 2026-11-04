using Avalonia.Headless.XUnit;
using Raffica.Core.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class LayoutTests
{
    [AvaloniaFact]
    public async Task SwitchingToTwoPagesKeepsTheCurrentPage()
    {
        using var fixture = await ViewFixture.OpenAsync("pages-marked.pdf", 1000, 700);
        var view = fixture.View;
        Assert.True(view.PageCount >= 4);

        view.GoToPage(3);
        Assert.Equal(3, view.CurrentPage);
        view.SetLayout(PageLayoutMode.TwoPages);
        ViewFixture.Pump();
        Assert.Equal(PageLayoutMode.TwoPages, view.Layout);
        Assert.Equal(3, view.CurrentPage);

        // The row of pages 3 and 4 stands at the top, one margin below the edge.
        Assert.InRange(view.PageBounds(3)!.Value.Y, 0, 40);

        view.SetLayout(PageLayoutMode.Continuous);
        ViewFixture.Pump();
        Assert.Equal(PageLayoutMode.Continuous, view.Layout);
        Assert.Equal(3, view.CurrentPage);

        view.SetLayout(PageLayoutMode.SinglePage);
        ViewFixture.Pump();
        Assert.Equal(3, view.CurrentPage);
    }

    [AvaloniaFact]
    public async Task SinglePageShowsOneRow()
    {
        using var fixture = await ViewFixture.OpenAsync("pages-marked.pdf", 800, 600);
        var view = fixture.View;
        Assert.True(view.PageCount >= 3);

        view.SetLayout(PageLayoutMode.SinglePage);
        ViewFixture.Pump();
        var (first, last) = view.VisiblePages;
        Assert.Equal(first, last);
        Assert.NotNull(view.PageBounds(first));
        Assert.Null(view.PageBounds(first + 1));

        view.GoToPage(2);
        ViewFixture.Pump();
        Assert.Equal(2, view.CurrentPage);
        Assert.NotNull(view.PageBounds(2));
        Assert.Null(view.PageBounds(0));
        Assert.Equal((2, 2), view.VisiblePages);
    }

    [AvaloniaFact]
    public async Task CoverLayoutPutsPageOneAlone()
    {
        using var fixture = await ViewFixture.OpenAsync("pages-marked.pdf", 1200, 800);
        var view = fixture.View;
        Assert.True(view.PageCount >= 3);

        view.SetLayout(PageLayoutMode.TwoPagesWithCover);
        ViewFixture.Pump();
        var cover = view.PageBounds(0)!.Value;
        var left = view.PageBounds(1)!.Value;
        var right = view.PageBounds(2)!.Value;

        // The cover has a row of its own; pages 2 and 3 share the next one.
        Assert.True(left.Y > cover.Y);
        Assert.Equal(left.Y, right.Y, 3);
        Assert.True(right.X > left.X);

        // The cover stands on the right half, next to where the spread's right page goes.
        Assert.True(cover.X >= left.X + left.Width - 1);

        // Without the cover, pages 1 and 2 share a row.
        view.SetLayout(PageLayoutMode.TwoPages);
        ViewFixture.Pump();
        Assert.Equal(view.PageBounds(0)!.Value.Y, view.PageBounds(1)!.Value.Y, 3);
        Assert.True(view.PageBounds(2)!.Value.Y > view.PageBounds(0)!.Value.Y);
    }

    [AvaloniaFact]
    public async Task LayoutIsKeptPerTab()
    {
        using var windows = WindowFixture.Create();
        var first = await windows.OpenAsync("pages-marked.pdf");
        var second = await windows.OpenAsync("text-basic.pdf");
        var firstView = windows.Window.ViewOf(first)!;
        var secondView = windows.Window.ViewOf(second)!;

        firstView.SetLayout(PageLayoutMode.TwoPages);
        secondView.SetLayout(PageLayoutMode.SinglePage);
        ViewFixture.Pump();

        windows.Window.Workspace.Select(first);
        ViewFixture.Pump();
        Assert.Equal(PageLayoutMode.TwoPages, firstView.Layout);
        Assert.Equal(PageLayoutMode.SinglePage, secondView.Layout);

        windows.Window.Workspace.Select(second);
        ViewFixture.Pump();
        Assert.Equal(PageLayoutMode.TwoPages, firstView.Layout);
        Assert.Equal(PageLayoutMode.SinglePage, secondView.Layout);
    }
}
