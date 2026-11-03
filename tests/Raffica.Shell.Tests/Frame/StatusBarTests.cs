using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class StatusBarTests
{
    private static (Window Window, StatusBar Bar) Show()
    {
        var bar = new StatusBar { SaveState = "Saved", Position = "Page 1 of 3", Count = "120 words", ZoomText = "100%" };
        var window = ControlHarness.Show(bar, ThemeMode.Light);
        return (window, bar);
    }

    private static Control Part(StatusBar bar, string name) => ControlHarness.Part<Control>(bar, name);

    private static double X(StatusBar bar, string name) =>
        Part(bar, name).TransformToVisual(bar)!.Value.Transform(default).X;

    private static readonly string[] Cells =
        ["PART_SaveState", "PART_Position", "PART_Count", "PART_Focus", "PART_Language", "PART_Zoom"];

    [AvaloniaFact]
    public void CellsChangeInPlace()
    {
        var (window, bar) = Show();
        var before = Cells.ToDictionary(n => n, n => X(bar, n));

        bar.Position = "Page 1234567 of 99999999 in a very long section name";
        bar.Count = "1";
        bar.FocusPane = "Comments, a pane with a rather long name";
        bar.SaveState = "Not saved";
        ControlHarness.Settle(window);

        foreach (var name in Cells)
        {
            Assert.Equal(before[name], X(bar, name), 0.01);
        }
    }

    [AvaloniaFact]
    public void FocusAndHintCellsAreCollapsedAtRest()
    {
        var (window, bar) = Show();
        Assert.False(Part(bar, "PART_FocusText").IsVisible);
        Assert.False(Part(bar, "PART_HintText").IsVisible);
        Assert.False(Part(bar, "PART_LanguageText").IsVisible);

        bar.Hint = "Type a field code";
        ControlHarness.Settle(window);
        Assert.True(Part(bar, "PART_HintText").IsVisible);
        Assert.False(Part(bar, "PART_FocusText").IsVisible);
        Assert.Equal(X(bar, "PART_FocusText"), X(bar, "PART_HintText"), 0.01);

        bar.Hint = null;
        bar.FocusPane = "Navigation";
        bar.Language = "German";
        ControlHarness.Settle(window);
        Assert.True(Part(bar, "PART_FocusText").IsVisible);
        Assert.True(Part(bar, "PART_LanguageText").IsVisible);
    }

    [AvaloniaFact]
    public void ZoomChangedRaisesWhenTheSliderMoves()
    {
        var (_, bar) = Show();
        var raised = new List<double>();
        bar.ZoomChanged += (_, z) => raised.Add(z);

        ControlHarness.Part<Slider>(bar, "PART_ZoomSlider").Value = 150;

        Assert.Equal([150d], raised);
        Assert.Equal(150, bar.Zoom);
    }

    [AvaloniaFact]
    public void NumbersUseTabularFigures()
    {
        var (_, bar) = Show();
        foreach (var name in new[] { "PART_SaveState", "PART_Position", "PART_Count", "PART_FocusText", "PART_HintText", "PART_LanguageText", "PART_Zoom" })
        {
            var text = ControlHarness.Part<TextBlock>(bar, name);
            Assert.Contains("tnum", text.Classes);
        }
    }

    [AvaloniaFact]
    public void CellHeightIs20()
    {
        var (_, bar) = Show();
        foreach (var name in new[] { "PART_SaveState", "PART_Position", "PART_Count", "PART_Zoom" })
        {
            Assert.Equal(20, Part(bar, name).Bounds.Height, 0.01);
        }

        Assert.Equal(30, bar.Bounds.Height, 0.01);
        Assert.Equal(24, Part(bar, "PART_ZoomOut").Bounds.Width, 0.01);
        Assert.Equal(24, Part(bar, "PART_ZoomOut").Bounds.Height, 0.01);
        Assert.Equal(96, Part(bar, "PART_ZoomSlider").Bounds.Width, 0.01);
    }
}
