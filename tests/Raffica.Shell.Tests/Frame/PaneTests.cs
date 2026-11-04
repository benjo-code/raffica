using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class PaneTests
{
    [AvaloniaFact]
    public void HeaderIs38HighAndPaddingIs12()
    {
        var content = new Border { Width = 50, Height = 20, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var pane = new Pane { Title = "Comments", Content = content };
        var window = ControlHarness.Show(pane, ThemeMode.Light);
        ControlHarness.Settle(window);

        var header = ControlHarness.Part<Control>(pane, "PART_Header");
        Assert.Equal(38, header.Bounds.Height, 0.01);
        var title = ControlHarness.Part<TextBlock>(pane, "PART_Title");
        Assert.Equal(12, title.FontSize);
        var body = ControlHarness.Part<Border>(pane, "PART_Body");
        Assert.Equal(new Thickness(12), body.Padding);
        var origin = content.TranslatePoint(default, body)!.Value;
        Assert.Equal(12, origin.X, 0.01);
        Assert.Equal(12, origin.Y, 0.01);
    }

    [AvaloniaFact]
    public void ClosedPaneTakesNoSpace()
    {
        var pane = new Pane { Title = "Comments", Content = new Button { Content = "Inside" } };
        var other = new Button { Content = "Other" };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { pane, other } };
        var window = ControlHarness.Show(row, ThemeMode.Light);
        ControlHarness.Settle(window);
        var openWidth = pane.Bounds.Width;
        var otherX = other.Bounds.X;
        Assert.True(openWidth > 0);

        pane.IsOpen = false;
        ControlHarness.Settle(window);

        Assert.False(pane.IsVisible);
        Assert.Equal(otherX - openWidth, other.Bounds.X, 0.01);
        Assert.False(((IPaneItem)pane).IsAvailable);
    }

    [AvaloniaFact]
    public void ThePaneIsTheBandSurfaceUnlessItIsTheDocument()
    {
        var pane = new Pane { Title = "Format" };
        var doc = new Pane { IsDocument = true };
        var panel = new StackPanel { Children = { pane, doc } };
        var window = ControlHarness.Show(panel, ThemeMode.Light);
        ControlHarness.Settle(window);

        ControlHarness.AssertToken("Raffica.Brush.Surface.Band", ControlHarness.Part<Border>(pane, "PART_Frame").Background);
        Assert.Null(ControlHarness.Part<Border>(doc, "PART_Frame").Background);
        Assert.False(ControlHarness.Part<Control>(doc, "PART_Header").IsVisible);
    }
}
