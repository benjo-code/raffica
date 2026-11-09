using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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

        // The position and count cells size to their text with a minimum (M2.7): a text that fits the minimum moves nothing.
        bar.Position = "Page 123 of 999";
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
    public void PositionAndCountGrowWithTheirTextAndTakeTheRoomFromTheFreeColumn()
    {
        var (window, bar) = Show();
        var before = Cells.ToDictionary(n => n, n => X(bar, n));
        var zoomRight = Part(bar, "PART_Zoom").Bounds.Right;

        bar.Position = "Page 1234567 of 99999999 in a very long section name";
        ControlHarness.Settle(window);

        Assert.True(Part(bar, "PART_Position").Bounds.Width > 128, "the position cell grows past its minimum");
        Assert.Equal(Part(bar, "PART_Position").Bounds.Width, ControlHarness.Part<TextBlock>(bar, "PART_Position").DesiredSize.Width, 0.5);
        Assert.Equal(before["PART_SaveState"], X(bar, "PART_SaveState"), 0.01);
        Assert.Equal(before["PART_Position"], X(bar, "PART_Position"), 0.01);
        Assert.True(X(bar, "PART_Count") > before["PART_Count"], "the cells right of it move over");
        Assert.Equal(zoomRight, Part(bar, "PART_Zoom").Bounds.Right, 0.01);

        bar.Position = "Page 1 of 3";
        ControlHarness.Settle(window);
        Assert.Equal(128, Part(bar, "PART_Position").Bounds.Width, 0.01);
        Assert.Equal(144, Part(bar, "PART_Count").Bounds.Width, 0.01);
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
    public void TheHintWinsWhenFocusAndHintAreBothSet()
    {
        var (window, bar) = Show();
        bar.FocusPane = "Focus: Comments";
        bar.Hint = "Type a field code";
        ControlHarness.Settle(window);
        Assert.True(Part(bar, "PART_HintText").IsVisible);
        Assert.False(Part(bar, "PART_FocusText").IsVisible);

        bar.Hint = null;
        ControlHarness.Settle(window);
        Assert.True(Part(bar, "PART_FocusText").IsVisible);
        Assert.False(Part(bar, "PART_HintText").IsVisible);
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
    public void ZoomChangedReportsMultiplesOfTen()
    {
        var (_, bar) = Show();
        var raised = new List<double>();
        bar.ZoomChanged += (_, z) => raised.Add(z);
        var slider = ControlHarness.Part<Slider>(bar, "PART_ZoomSlider");

        slider.Value = 147;
        slider.Value = 203;
        slider.Value = 12;

        Assert.NotEmpty(raised);
        Assert.All(raised, z => Assert.Equal(0, z % 10));
        Assert.Equal(10, bar.Zoom);
    }

    [AvaloniaFact]
    public void TheZoomRangeReachesEightHundredPercent()
    {
        var (_, bar) = Show();
        var slider = ControlHarness.Part<Slider>(bar, "PART_ZoomSlider");
        var raised = new List<double>();
        bar.ZoomChanged += (_, z) => raised.Add(z);

        bar.Zoom = 800;
        Assert.Equal(800, bar.Zoom);
        Assert.Equal(800, slider.Value);
        Assert.Equal(800, slider.Maximum);

        // The app may set any value of the viewer range, and one above it is limited.
        bar.Zoom = 5000;
        Assert.Equal(800, bar.Zoom);

        // From 800 the plus button cannot go further, and the minus button steps down by ten from the real value.
        ControlHarness.Part<Button>(bar, "PART_ZoomIn").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ControlHarness.Part<Button>(bar, "PART_ZoomOut").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([800d, 790d], raised);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, false)]
    [InlineData(ThemeMode.Dark, false)]
    [InlineData(ThemeMode.Light, true)]
    public void TheZoomSliderUsesNeutralTokensNotTheFluentAccent(ThemeMode mode, bool highContrast)
    {
        var (window, bar) = Show();
        try
        {
            ShellTheme.Apply(Application.Current!, mode, AppInk.Write, highContrast);
            ControlHarness.Settle(window);
            var slider = ControlHarness.Part<Slider>(bar, "PART_ZoomSlider");
            var thumb = ControlHarness.Part<Avalonia.Controls.Shapes.Ellipse>(slider, "PART_ThumbShape");
            ControlHarness.AssertToken("Raffica.Brush.Text.Primary", thumb.Fill, "slider thumb");
            var app = Application.Current!;
            Assert.True(app.TryGetResource("SystemAccentColor", app.ActualThemeVariant, out var accent), "Fluent accent not found");
            Assert.NotEqual((Color)accent!, ControlHarness.Strict(thumb.Fill));

            // The thumb is fully inside the 24 px high slider (no half-clipped edge) and 12 px wide.
            var shape = new Rect(thumb.TranslatePoint(default, slider)!.Value, thumb.Bounds.Size);
            Assert.Equal(12, thumb.Bounds.Width, 0.01);
            Assert.True(shape.Top >= 0 && shape.Bottom <= slider.Bounds.Height, $"thumb {shape} in slider {slider.Bounds}");
        }
        finally
        {
            ShellTheme.Apply(Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        }
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

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheZoomButtonsAndTheSliderHaveTooltipsInTheUiLanguage(string lang)
    {
        var previous = Raffica.Shell.Localization.ShellStrings.Culture;
        try
        {
            Raffica.Shell.Localization.ShellStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo(lang);
            var (_, bar) = Show();

            foreach (var (name, expected) in new[]
            {
                ("PART_ZoomOut", Raffica.Shell.Localization.ShellStrings.ZoomOut),
                ("PART_ZoomIn", Raffica.Shell.Localization.ShellStrings.ZoomIn),
                ("PART_ZoomSlider", Raffica.Shell.Localization.ShellStrings.ZoomLevel),
            })
            {
                var tip = Assert.IsType<TooltipContent>(ToolTip.GetTip(Part(bar, name)));
                Assert.Equal(expected, tip.Title);
                Assert.False(string.IsNullOrWhiteSpace(tip.Title));
            }
        }
        finally
        {
            Raffica.Shell.Localization.ShellStrings.Culture = previous;
        }
    }
}
