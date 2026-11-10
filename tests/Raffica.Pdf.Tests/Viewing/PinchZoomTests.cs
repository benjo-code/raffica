using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class PinchZoomTests
{
    [Fact]
    public void ZoomByMagnifyClampsAtTheZoomLimits()
    {
        Assert.Equal(ZoomSteps.Max, ViewCommands.ZoomByMagnify(7.9, 0.5));
        Assert.Equal(ZoomSteps.Min, ViewCommands.ZoomByMagnify(0.11, -0.9));
        Assert.Equal(1, ViewCommands.ZoomByMagnify(1, double.NaN));
        Assert.Equal(1, ViewCommands.ZoomByMagnify(1, double.PositiveInfinity));
        Assert.Equal(1.5, ViewCommands.ZoomByMagnify(1, 0.5), 10);
    }

    [AvaloniaFact]
    public async Task MagnifyKeepsThePointUnderThePointer()
    {
        // A window narrower than the page, so the point can stay put sideways as well as down.
        using var fixture = await ViewFixture.OpenAsync("pages-marked.pdf", 400, 600);
        var view = fixture.View;
        view.SetZoom(1.0);
        ViewFixture.Pump();
        view.Offset = new Vector(0, 40);

        var point = new Point(300, 250);
        var before = FractionsAt(view, point);

        view.MagnifyAround(0.5, point);

        Assert.Equal(1.5, view.Zoom, 10);
        Assert.Equal(ZoomMode.Free, view.ZoomMode);
        var after = FractionsAt(view, point);
        Assert.Equal(before.Page, after.Page);
        Assert.Equal(before.X, after.X, 2);
        Assert.Equal(before.Y, after.Y, 2);
    }

    [AvaloniaFact]
    public async Task AMagnifyEndsAFitMode()
    {
        using var fixture = await ViewFixture.OpenAsync("pages-marked.pdf", 400, 600);
        var view = fixture.View;
        view.SetZoomMode(ZoomMode.FitPage);
        ViewFixture.Pump();
        var fitted = view.Zoom;

        view.MagnifyAround(0.25, new Point(200, 300));

        Assert.Equal(ZoomMode.Free, view.ZoomMode);
        Assert.Equal(fitted * 1.25, view.Zoom, 10);
    }

    [AvaloniaFact]
    public async Task MagnifyIsRaisedByTheTouchPadEvent()
    {
        using var fixture = await ViewFixture.OpenAsync("pages-marked.pdf", 400, 600);
        var view = fixture.View;
        view.SetZoom(1.0);
        ViewFixture.Pump();

        var args = new PointerDeltaEventArgs(
            InputElement.PointerTouchPadGestureMagnifyEvent,
            view,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true),
            fixture.Window,
            new Point(200, 300),
            0,
            new PointerPointProperties(),
            KeyModifiers.None,
            new Vector(0.5, 0));
        view.RaiseEvent(args);

        Assert.True(args.Handled);
        Assert.Equal(1.5, view.Zoom, 10);
    }

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
