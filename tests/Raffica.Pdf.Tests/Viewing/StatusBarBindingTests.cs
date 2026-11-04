using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class StatusBarBindingTests
{
    [AvaloniaFact]
    public async Task PositionAndZoomFollowTheView()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var view = windows.Window.ViewOf(tab)!;
        var status = windows.Window.Status;
        var count = view.PageCount;
        Assert.True(count >= 4);

        Assert.Equal($"Page 1 of {count}", status.Position);
        Assert.Equal("100%", status.ZoomText);
        Assert.Equal(100, status.Zoom);
        Assert.Contains($"{count} pages", status.Count, StringComparison.Ordinal);
        Assert.Contains(" mm", status.Count, StringComparison.Ordinal);

        view.GoToPage(3);
        ViewFixture.Pump();
        Assert.Equal($"Page 4 of {count}", status.Position);

        view.ZoomIn();
        ViewFixture.Pump();
        Assert.Equal("125%", status.ZoomText);
        Assert.Equal(125, status.Zoom);

        view.SetZoomMode(ZoomMode.FitWidth);
        ViewFixture.Pump();
        Assert.Equal(ViewCommands.ZoomText(view.Zoom), status.ZoomText);

        // Scrolling by the wheel changes the page of the position cell.
        view.Offset = new Vector(0, 0);
        ViewFixture.Pump();
        Assert.Equal($"Page 1 of {count}", status.Position);

        // A resize (fit mode, raised from the arrange pass) reaches the status bar too.
        windows.Window.Width = 1300;
        ViewFixture.Pump();
        Assert.Equal(ViewCommands.ZoomText(view.Zoom), status.ZoomText);
    }

    [AvaloniaFact]
    public async Task TheTextsAreGermanInGerman()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var count = windows.Window.ViewOf(tab)!.PageCount;

        Assert.Equal($"Seite 1 von {count}", windows.Window.Status.Position);
        Assert.Contains("Seiten", windows.Window.Status.Count, StringComparison.Ordinal);
        Assert.DoesNotContain("pages", windows.Window.Status.Count, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SliderChangesTheZoom()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var view = windows.Window.ViewOf(tab)!;
        var status = windows.Window.Status;
        view.SetZoomMode(ZoomMode.FitWidth);
        ViewFixture.Pump();

        var slider = status.GetVisualDescendants().OfType<Slider>().Single();
        slider.Value = 200;
        ViewFixture.Pump();

        Assert.Equal(2.0, view.Zoom);
        Assert.Equal(ZoomMode.Free, view.ZoomMode);
        Assert.Equal("200%", status.ZoomText);

        // The buttons of the cluster step by ten percent.
        var plus = status.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_ZoomIn");
        plus.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Assert.Equal(2.1, view.Zoom, 6);

        // The slider belongs to the tab that is shown: another tab is not changed.
        var other = await windows.OpenAsync("text-basic.pdf");
        Assert.Equal(1.0, windows.Window.ViewOf(other)!.Zoom);
        Assert.Equal("100%", status.ZoomText);
    }

    [AvaloniaFact]
    public async Task ButtonsAndSliderStepFromTheRealZoomAboveFiveHundredPercent()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var view = windows.Window.ViewOf(tab)!;
        var status = windows.Window.Status;
        var plus = status.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_ZoomIn");
        var minus = status.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_ZoomOut");
        var slider = status.GetVisualDescendants().OfType<Slider>().Single();

        view.SetZoom(8);
        ViewFixture.Pump();
        Assert.Equal("800%", status.ZoomText);
        Assert.Equal(800, status.Zoom);
        Assert.Equal(800, slider.Value);

        // Plus at the top is a no-op, not a drop to 500 %.
        plus.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Assert.Equal(8.0, view.Zoom);
        Assert.Equal("800%", status.ZoomText);

        // Minus steps down by ten from 800, not from 500.
        minus.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Assert.Equal(7.9, view.Zoom, 6);
        Assert.Equal("790%", status.ZoomText);
        Assert.Equal(790, slider.Value);

        view.SetZoom(6);
        ViewFixture.Pump();
        plus.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Assert.Equal(6.1, view.Zoom, 6);

        slider.Value = 700;
        ViewFixture.Pump();
        Assert.Equal(7.0, view.Zoom, 6);
        Assert.Equal("700%", status.ZoomText);

        // At the bottom: minus does not jump.
        view.SetZoom(0.1);
        ViewFixture.Pump();
        minus.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Assert.Equal(0.1, view.Zoom, 6);
        Assert.Equal("10%", status.ZoomText);
    }

    [Fact]
    public void TextsFormatPositionCountAndZoom()
    {
        Assert.Equal("Page 3 of 12", ViewCommands.PositionText(3, 12));
        Assert.Equal("1 page · 210 × 297 mm", ViewCommands.CountText(1, new Raffica.Core.Pdf.PdfPageSize(595.28, 841.89)));
        Assert.Equal("12 pages · 216 × 279 mm", ViewCommands.CountText(12, new Raffica.Core.Pdf.PdfPageSize(612, 792)));
        Assert.Equal("12 pages", ViewCommands.CountText(12, null));
        Assert.Equal("33%", ViewCommands.ZoomText(0.333));
        Assert.Equal("800%", ViewCommands.ZoomText(8));
        Assert.Equal("210 × 297 mm", ViewCommands.PageSizeText(new Raffica.Core.Pdf.PdfPageSize(595.28, 841.89), precise: false));
        Assert.Equal("215.9 × 279.4 mm", ViewCommands.PageSizeText(new Raffica.Core.Pdf.PdfPageSize(612, 792), precise: true));
    }
}
