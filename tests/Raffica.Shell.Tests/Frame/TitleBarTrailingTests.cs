using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class TitleBarTrailingTests
{
    private static (Window Window, TitleBar Bar) Show(Action<TitleBar>? configure = null)
    {
        var bar = new TitleBar { VerticalAlignment = VerticalAlignment.Top };
        configure?.Invoke(bar);
        var window = ControlHarness.Show(bar, ThemeMode.Light);
        window.Width = 1200;
        ControlHarness.Settle(window);
        return (window, bar);
    }

    [AvaloniaFact]
    public void TrailingContentSitsLeftOfTheCaptionButtonsAndTheDefaultIsEmpty()
    {
        // The default is empty and takes no room.
        var (_, plain) = Show();
        Assert.Null(plain.Trailing);
        var slot = ControlHarness.Part<Control>(plain, "PART_Trailing");
        Assert.Equal(0, slot.Bounds.Width, 0.01);

        // With content: right of the search field, left of the minimize button, inside the 36 px bar.
        var marker = new Border { Name = "PART_Marker", Width = 60, Height = 20 };
        var (window, bar) = Show(b =>
        {
            b.ShowCaptionButtons = true;
            b.Trailing = marker;
        });
        var minimize = ControlHarness.Part<Button>(bar, "PART_CaptionMinimize");
        var origin = marker.TranslatePoint(new Point(0, 0), bar)!.Value;
        var search = bar.Search.TranslatePoint(new Point(0, 0), bar)!.Value;
        var minimizeLeft = minimize.TranslatePoint(new Point(0, 0), bar)!.Value.X;
        Assert.Equal(60, marker.Bounds.Width, 0.01);
        Assert.True(origin.X >= search.X + bar.Search.Bounds.Width - 0.01, "the content sits right of the search field");
        Assert.True(origin.X + 60 <= minimizeLeft + 0.01, "the content sits left of the caption buttons");
        Assert.Equal(36, bar.Bounds.Height, 0.01);

        // Setting it later works as well, and clearing it gives the room back.
        bar.Trailing = null;
        ControlHarness.Settle(window);
        Assert.Equal(0, ControlHarness.Part<Control>(bar, "PART_Trailing").Bounds.Width, 0.01);
    }
}
