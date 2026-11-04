using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class ZoomTests
{
    [AvaloniaFact]
    public async Task StepsFollowCoreZoomSteps()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf");
        var view = fixture.View;
        view.SetZoom(1.0);

        var seen = new List<double>();
        for (var i = 0; i < 12; i++)
        {
            view.ZoomIn();
            seen.Add(view.Zoom);
        }

        // Each step is the next one of Core's table, and the top end holds.
        var expected = new List<double>();
        var zoom = 1.0;
        for (var i = 0; i < 12; i++)
        {
            zoom = ZoomSteps.Next(zoom);
            expected.Add(zoom);
        }

        Assert.Equal(expected, seen);
        Assert.Equal(ZoomSteps.Max, view.Zoom);

        for (var i = 0; i < 20; i++)
        {
            var before = view.Zoom;
            view.ZoomOut();
            Assert.Equal(ZoomSteps.Previous(before), view.Zoom);
        }

        Assert.Equal(ZoomSteps.Min, view.Zoom);
    }

    [AvaloniaFact]
    public async Task ZoomIsClampedToCoreMinAndMax()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf");
        var view = fixture.View;

        view.SetZoom(0.01);
        Assert.Equal(0.1, view.Zoom);
        view.SetZoom(100);
        Assert.Equal(8.0, view.Zoom);
        view.SetZoom(double.NaN);
        Assert.Equal(ZoomSteps.Default, view.Zoom);
        view.Zoom = 50;
        Assert.Equal(8.0, view.Zoom);
    }

    [AvaloniaFact]
    public async Task FitWidthFollowsTheWindowWidth()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf", 800, 600);
        var view = fixture.View;

        view.SetZoomMode(ZoomMode.FitWidth);
        ViewFixture.Pump();
        Assert.Equal(ZoomMode.FitWidth, view.ZoomMode);
        var first = view.PageBounds(0)!.Value;
        Assert.Equal(800 - (2 * 32), first.Width, 0);

        fixture.Window.Width = 1000;
        ViewFixture.Pump();
        var wider = view.PageBounds(0)!.Value;
        Assert.Equal(1000 - (2 * 32), wider.Width, 0);
        Assert.Equal(ZoomMode.FitWidth, view.ZoomMode);

        fixture.Window.Width = 600;
        ViewFixture.Pump();
        Assert.Equal(600 - (2 * 32), view.PageBounds(0)!.Value.Width, 0);

        // Fit page: the whole page fits into the window height.
        view.SetZoomMode(ZoomMode.FitPage);
        ViewFixture.Pump();
        Assert.True(view.PageBounds(0)!.Value.Height <= 600 - (2 * 32) + 0.5);
    }

    [AvaloniaFact]
    public async Task ManualZoomLeavesFitMode()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf", 800, 600);
        var view = fixture.View;

        view.SetZoomMode(ZoomMode.FitWidth);
        ViewFixture.Pump();
        view.ZoomIn();
        Assert.Equal(ZoomMode.Free, view.ZoomMode);

        view.SetZoomMode(ZoomMode.FitPage);
        view.SetZoom(2);
        Assert.Equal(ZoomMode.Free, view.ZoomMode);
        Assert.Equal(2, view.Zoom);

        // The window width no longer changes a free zoom.
        fixture.Window.Width = 1000;
        ViewFixture.Pump();
        Assert.Equal(2, view.Zoom);

        // At the top step the zoom cannot grow, but the fit mode still ends.
        view.SetZoom(ZoomSteps.Max);
        view.SetZoomMode(ZoomMode.FitWidth);
        view.ZoomIn();
        Assert.Equal(ZoomMode.Free, view.ZoomMode);

        view.SetZoomMode(ZoomMode.ActualSize);
        ViewFixture.Pump();
        Assert.Equal(1, view.Zoom);
    }

    [AvaloniaFact]
    public async Task CtrlWheelZoomKeepsThePointUnderThePointerStable()
    {
        // A window narrower than the page at every zoom used, so the point can stay put sideways as well as down.
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf", 400, 600);
        var view = fixture.View;
        view.SetZoom(1.0);
        ViewFixture.Pump();
        view.Offset = new Vector(0, 40);

        var pointer = new Point(300, 250);
        var before = FractionsAt(view, pointer);
        Assert.Equal(0, before.Page);

        fixture.Window.MouseWheel(pointer, new Vector(0, 1), RawInputModifiers.Control);
        Assert.Equal(1.25, view.Zoom);
        Assert.Equal(ZoomMode.Free, view.ZoomMode);
        var zoomedIn = FractionsAt(view, pointer);
        Assert.Equal(before.Page, zoomedIn.Page);
        Assert.Equal(before.X, zoomedIn.X, 2);
        Assert.Equal(before.Y, zoomedIn.Y, 2);

        fixture.Window.MouseWheel(pointer, new Vector(0, -1), RawInputModifiers.Control);
        Assert.Equal(1.0, view.Zoom);
        var back = FractionsAt(view, pointer);
        Assert.Equal(before.X, back.X, 2);
        Assert.Equal(before.Y, back.Y, 2);

        // Ctrl + wheel zooms, it does not scroll by itself: a position at the very top stays at the top.
        view.Offset = new Vector(0, 0);
        fixture.Window.MouseWheel(new Point(400, 0), new Vector(0, -1), RawInputModifiers.Control);
        view.CompleteScroll();
        Assert.Equal(0, view.Offset.Y);
    }

    [AvaloniaFact]
    public async Task ZoomOnAnOddPageStaysFinite()
    {
        foreach (var file in new[] { "rotated-page.pdf", "wide-page.pdf", "cropbox.pdf" })
        {
            using var fixture = await ViewFixture.OpenAsync(file, 800, 600);
            var view = fixture.View;
            foreach (var zoom in new[] { 0.1, 8.0 })
            {
                view.SetZoom(zoom);
                ViewFixture.Pump();
                Assert.Equal(zoom, view.Zoom);
                var page = view.PageBounds(0);
                Assert.NotNull(page);
                Assert.True(double.IsFinite(page.Value.X) && double.IsFinite(page.Value.Y), $"{file} at {zoom}: position {page}");
                Assert.True(double.IsFinite(page.Value.Width) && page.Value.Width > 0, $"{file} at {zoom}: width {page}");
                Assert.True(double.IsFinite(page.Value.Height) && page.Value.Height > 0, $"{file} at {zoom}: height {page}");
                Assert.True(double.IsFinite(view.MaxOffsetY) && view.MaxOffsetY >= 0);
                Assert.True(double.IsFinite(view.Offset.X) && double.IsFinite(view.Offset.Y));
            }

            view.SetZoomMode(ZoomMode.FitPage);
            ViewFixture.Pump();
            Assert.True(double.IsFinite(view.Zoom) && view.Zoom > 0);
        }
    }

    [Fact]
    public void WheelZoomCollectsSmallDeltasIntoWholeSteps()
    {
        var remainder = 0.0;
        Assert.Equal(1.0, ViewCommands.ZoomByWheel(1.0, 0.4, ref remainder));
        Assert.Equal(1.0, ViewCommands.ZoomByWheel(1.0, 0.4, ref remainder));
        Assert.Equal(1.25, ViewCommands.ZoomByWheel(1.0, 0.4, ref remainder));
        remainder = 0;
        Assert.Equal(0.75, ViewCommands.ZoomByWheel(1.0, -1, ref remainder));
        Assert.Equal(0.5, ViewCommands.ZoomByWheel(1.0, -2, ref remainder));
        Assert.Equal(1.0, ViewCommands.ZoomByWheel(1.0, double.NaN, ref remainder));
        Assert.Equal(ZoomSteps.Max, ViewCommands.ZoomByWheel(1.0, 1e9, ref remainder));
    }

    // The point under the pointer as fractions of the page it is on: which page, how far across it, how far down it.
    private static (int Page, double X, double Y) FractionsAt(PageView view, Point point)
    {
        for (var page = view.VisiblePages.First; page <= view.VisiblePages.Last; page++)
        {
            var bounds = view.PageBounds(page)!.Value;
            if (bounds.Contains(point))
            {
                return (page, (point.X - bounds.X) / bounds.Width, (point.Y - bounds.Y) / bounds.Height);
            }
        }

        throw new Xunit.Sdk.XunitException($"No page under {point}");
    }
}
