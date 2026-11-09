using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class EndKeyTests
{
    [AvaloniaFact]
    public async Task EndOnAThousandShortLandscapePagesShowsTheLastPageInTheStatusBar()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("thousand-pages.pdf");
        var view = windows.Window.ViewOf(tab)!;
        Assert.Equal(1000, view.PageCount);
        view.Focus();
        Assert.Equal("Page 1 of 1000", windows.Window.Status.Position);

        windows.Window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        ViewFixture.Pump();

        Assert.Equal("Page 1000 of 1000", windows.Window.Status.Position);
        Assert.Equal(999, view.CurrentPage);
    }

    [AvaloniaFact]
    public async Task ScrollingToTheEndWithoutTheEndKeyAlsoMakesTheLastPageCurrent()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("thousand-pages.pdf");
        var view = windows.Window.ViewOf(tab)!;

        // The scroll bar drag and a wheel that reaches the bottom end here as well.
        view.Offset = new Avalonia.Vector(0, view.MaxOffsetY);
        ViewFixture.Pump();

        Assert.Equal(999, view.CurrentPage);
        Assert.Equal("Page 1000 of 1000", windows.Window.Status.Position);

        // One step back up leaves the end: the page is the most visible one again, not forced.
        view.Offset = new Avalonia.Vector(0, view.MaxOffsetY - 300);
        ViewFixture.Pump();
        Assert.True(view.CurrentPage < 999);
    }

    [AvaloniaFact]
    public async Task InTheSinglePageLayoutTheBottomOfAPageIsThatPageNotTheLastOfTheDocument()
    {
        using var fixture = await ViewFixture.OpenAsync("pages-marked.pdf", 800, 400);
        var view = fixture.View;
        Assert.True(view.PageCount >= 4);
        view.SetLayout(PageLayoutMode.SinglePage);
        view.SetZoom(3);
        view.GoToPage(2);
        ViewFixture.Pump();
        Assert.True(view.MaxOffsetY > 0, "the page must be taller than the window");

        view.Offset = new Avalonia.Vector(0, view.MaxOffsetY);
        ViewFixture.Pump();

        Assert.Equal(2, view.CurrentPage);
        Assert.Equal($"Page 3 of {view.PageCount}", Raffica.Pdf.Viewing.ViewCommands.PositionText(view.CurrentPage + 1, view.PageCount));
    }

    [AvaloniaFact]
    public async Task ARestoredSinglePageViewAtTheBottomOfAMiddlePageKeepsThatPage()
    {
        using var fixture = await ViewFixture.OpenAsync("pages-marked.pdf", 800, 400);
        var view = fixture.View;
        view.SetLayout(PageLayoutMode.SinglePage);
        view.SetZoom(3);
        view.GoToPage(2);
        ViewFixture.Pump();
        view.Offset = new Avalonia.Vector(0, view.MaxOffsetY);
        ViewFixture.Pump();
        var kept = view.CaptureRemembered();

        view.RestoreView(kept.View, kept.Zoom, kept.ZoomMode, kept.Layout);
        ViewFixture.Pump();

        Assert.Equal(2, view.CurrentPage);
    }

    [AvaloniaFact]
    public async Task InTheTwoPageLayoutTheEndIsTheLastPageAndAHigherOffsetIsNot()
    {
        using var fixture = await ViewFixture.OpenAsync("pages-marked.pdf", 800, 400);
        var view = fixture.View;
        view.SetLayout(PageLayoutMode.TwoPages);
        view.SetZoom(2);
        ViewFixture.Pump();
        Assert.True(view.MaxOffsetY > 0);

        view.Offset = new Avalonia.Vector(0, view.MaxOffsetY);
        ViewFixture.Pump();
        Assert.Equal(view.PageCount - 1, view.CurrentPage);

        view.Offset = new Avalonia.Vector(0, 0);
        ViewFixture.Pump();
        Assert.True(view.CurrentPage < view.PageCount - 1);
    }
}
