using Avalonia;
using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class PageViewStateTests
{
    [AvaloniaFact]
    public async Task AFitModeOnADocumentWithoutPagesDoesNotThrow()
    {
        // PDFium does not open a file without pages, so the layout of an empty document is handed to the view directly.
        using var fixture = await ViewFixture.OpenAsync("text-basic.pdf", 800, 600);
        var view = fixture.View;
        view.AdoptLayout(new DocumentLayout(0, new PdfPageSize(595, 842)));
        Assert.Equal(0, view.PageCount);

        // Reachable through a remembered view: a fit mode with a page that does not exist.
        view.RestoreView(new ViewState(new PageAnchor(0, 0), 0), 1, ZoomMode.FitWidth, PageLayoutMode.Continuous);
        view.SetZoomMode(ZoomMode.FitPage);
        view.SetLayout(PageLayoutMode.TwoPages);
        fixture.Window.Width = 900;
        ViewFixture.Pump();
        view.ZoomIn();
        Assert.Equal(0, view.CurrentPage);
        Assert.True(double.IsFinite(view.Zoom));
    }

    [AvaloniaFact]
    public async Task ViewChangedIsNotRaisedInsideTheArrangePass()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf", 800, 600);
        var view = fixture.View;
        view.SetZoomMode(ZoomMode.FitWidth);
        ViewFixture.Pump();

        var calls = 0;
        var inArrange = 0;
        view.ViewChanged += () =>
        {
            calls++;
            if (view.IsArranging)
            {
                inArrange++;
            }
        };

        // A resize relayouts from the arrange pass; the event follows after it.
        fixture.Window.Width = 1000;
        ViewFixture.Pump();
        Assert.True(calls > 0, "the resize of a fitted view must raise ViewChanged");
        Assert.Equal(0, inArrange);
    }

    [AvaloniaFact]
    public async Task ASubscriberThatChangesTheZoomDoesNotReenter()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf", 800, 600);
        var view = fixture.View;
        view.SetZoom(1.0);
        ViewFixture.Pump();

        var depth = 0;
        var maxDepth = 0;
        var calls = 0;
        view.ViewChanged += () =>
        {
            depth++;
            maxDepth = Math.Max(maxDepth, depth);
            calls++;
            if (calls == 1)
            {
                view.ZoomIn();
            }

            depth--;
        };

        view.Offset = new Vector(0, 100);
        ViewFixture.Pump();

        Assert.Equal(1, maxDepth);
        Assert.Equal(1.25, view.Zoom);
        Assert.True(calls >= 2, "the zoom made in the handler must be reported as well");
    }

    [AvaloniaFact]
    public async Task ASubscriberThatChangesTheZoomWhileArrangingDoesNotReenter()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf", 800, 600);
        var view = fixture.View;
        view.SetZoomMode(ZoomMode.FitWidth);
        ViewFixture.Pump();

        var depth = 0;
        var maxDepth = 0;
        var changed = false;
        view.ViewChanged += () =>
        {
            depth++;
            maxDepth = Math.Max(maxDepth, depth);
            if (!changed)
            {
                changed = true;
                Assert.False(view.IsArranging);
                view.SetZoom(2);
            }

            depth--;
        };

        fixture.Window.Width = 1000;
        ViewFixture.Pump();

        Assert.True(changed);
        Assert.Equal(1, maxDepth);
        Assert.Equal(2, view.Zoom);
    }
}
