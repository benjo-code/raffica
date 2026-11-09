using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
}
