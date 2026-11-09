using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

/// <summary>
/// The ribbon never clips a command: below the width the full band needs it switches to the compact layout by itself, and when even
/// that does not fit it scrolls with a chevron at each end (M2.7 Task 10). The manual compact setting stays and forces compact.
/// </summary>
public sealed class RibbonOverflowTests
{
    private static (Window Window, AppFrame Frame, RibbonView Ribbon) Show(double width, bool compact = false)
    {
        var ribbon = new RibbonView { Definition = SampleRibbon.Create(), IsCompact = compact };
        var frame = new AppFrame { TabRow = ribbon.TabRow, Band = ribbon.Band, IsCompact = compact };
        var window = ControlHarness.Show(frame, ThemeMode.Light);
        Resize(window, width);
        return (window, frame, ribbon);
    }

    private static void Resize(Window window, double width)
    {
        window.Width = width;
        ControlHarness.Settle(window);
        ControlHarness.Settle(window);
    }

    private static ScrollViewer Scroller(RibbonView ribbon) => ControlHarness.Part<ScrollViewer>(ribbon.Band, "PART_BandScroll");

    private static Button Left(RibbonView ribbon) => ControlHarness.Part<Button>(ribbon.Band, "PART_ScrollLeft");

    private static Button Right(RibbonView ribbon) => ControlHarness.Part<Button>(ribbon.Band, "PART_ScrollRight");

    private static double BandRow(AppFrame frame) => ControlHarness.Part<Border>(frame, "PART_BandRow").Bounds.Height;

    private static bool FullyVisible(Control control, ScrollViewer scroller)
    {
        var left = control.TranslatePoint(default, scroller)!.Value.X;
        return left >= -0.5 && left + control.Bounds.Width <= scroller.Bounds.Width + 0.5;
    }

    private static void Press(Window window, Button chevron)
    {
        chevron.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ControlHarness.Settle(window);
    }

    [AvaloniaFact]
    public void AWideWindowKeepsTheFullBandWithoutChevrons()
    {
        var (_, frame, ribbon) = Show(1400);
        Assert.False(ribbon.IsEffectivelyCompact);
        Assert.Equal(82, BandRow(frame), 0.5);
        Assert.False(Left(ribbon).IsEffectivelyVisible);
        Assert.False(Right(ribbon).IsEffectivelyVisible);
        Assert.Contains(RibbonViewTests.Buttons(ribbon), b => b.Kind == RibbonButtonKind.Tile);
    }

    [AvaloniaFact]
    public void ANarrowWindowSwitchesToTheCompactBandByItself()
    {
        var (_, frame, ribbon) = Show(560);
        Assert.True(ribbon.IsEffectivelyCompact);
        Assert.False(ribbon.IsCompact);
        Assert.Equal(40, BandRow(frame), 0.5);
        Assert.DoesNotContain(RibbonViewTests.Buttons(ribbon), b => b.Kind == RibbonButtonKind.Tile);
    }

    [AvaloniaFact]
    public void WideningTheWindowBringsTheFullBandBackWithoutFlicker()
    {
        var (window, frame, ribbon) = Show(560);
        Assert.True(ribbon.IsEffectivelyCompact);

        Resize(window, 1400);
        Assert.False(ribbon.IsEffectivelyCompact);
        Assert.Equal(82, BandRow(frame), 0.5);

        // Settled at one width, more layout passes change nothing (no rebuild, no loop between the two layouts).
        var builds = ribbon.BandBuildCount;
        for (var i = 0; i < 4; i++)
        {
            ControlHarness.Settle(window);
        }

        Assert.Equal(builds, ribbon.BandBuildCount);

        Resize(window, 560);
        Assert.True(ribbon.IsEffectivelyCompact);
        builds = ribbon.BandBuildCount;
        for (var i = 0; i < 4; i++)
        {
            ControlHarness.Settle(window);
        }

        Assert.Equal(builds, ribbon.BandBuildCount);
    }

    [AvaloniaFact]
    public void TheDecisionDoesNotChangeWithTheLayoutItPicks()
    {
        // Sweep the width in small steps up and down; the switch happens at the same width in both directions (no hysteresis from the layout).
        var (window, _, ribbon) = Show(1400);
        double? downSwitch = null;
        for (var width = 1400; width >= 300; width -= 10)
        {
            Resize(window, width);
            if (ribbon.IsEffectivelyCompact && downSwitch is null)
            {
                downSwitch = width;
            }
        }

        Assert.NotNull(downSwitch);
        double? upSwitch = null;
        for (var width = 300; width <= 1400; width += 10)
        {
            Resize(window, width);
            if (!ribbon.IsEffectivelyCompact && upSwitch is null)
            {
                upSwitch = width;
            }
        }

        Assert.NotNull(upSwitch);
        Assert.Equal(downSwitch!.Value, upSwitch!.Value, 10.01);
    }

    [AvaloniaFact]
    public void TheManualCompactSettingForcesCompactAtAnyWidth()
    {
        var (window, frame, ribbon) = Show(1900, compact: true);
        Assert.True(ribbon.IsEffectivelyCompact);
        Assert.Equal(40, BandRow(frame), 0.5);

        ribbon.IsCompact = false;
        frame.IsCompact = false;
        ControlHarness.Settle(window);
        Assert.False(ribbon.IsEffectivelyCompact);
        Assert.Equal(82, BandRow(frame), 0.5);
    }

    [AvaloniaFact]
    public void WhenEvenTheCompactBandIsTooWideItScrollsWithChevrons()
    {
        var (window, _, ribbon) = Show(190);
        Assert.True(ribbon.IsEffectivelyCompact);
        var scroller = Scroller(ribbon);
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width, "the compact band must be wider than the window for this test");

        // At the start only the right chevron shows.
        Assert.False(Left(ribbon).IsEffectivelyVisible);
        Assert.True(Right(ribbon).IsEffectivelyVisible);

        Press(window, Right(ribbon));
        Assert.True(scroller.Offset.X > 0);
        Assert.True(Left(ribbon).IsEffectivelyVisible);

        // At the end only the left chevron shows.
        for (var i = 0; i < 40 && Right(ribbon).IsEffectivelyVisible; i++)
        {
            Press(window, Right(ribbon));
        }

        Assert.False(Right(ribbon).IsEffectivelyVisible);
        Assert.True(Left(ribbon).IsEffectivelyVisible);
        Assert.Equal(scroller.Extent.Width - scroller.Viewport.Width, scroller.Offset.X, 1);

        for (var i = 0; i < 40 && Left(ribbon).IsEffectivelyVisible; i++)
        {
            Press(window, Left(ribbon));
        }

        Assert.Equal(0, scroller.Offset.X, 1);
        Assert.False(Left(ribbon).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void EveryCommandIsReachableThroughTheChevronsOnEveryTab()
    {
        var (window, _, ribbon) = Show(190);
        foreach (var tab in ribbon.Definition!.Tabs)
        {
            ribbon.ActiveTab = tab;
            ControlHarness.Settle(window);
            var scroller = Scroller(ribbon);
            foreach (var button in RibbonViewTests.Buttons(ribbon).Cast<Control>().Concat(ControlHarness.Descendants(ribbon.Band).OfType<Button>().Where(b => b.Classes.Contains("ribbon-more"))).ToList())
            {
                for (var i = 0; i < 60 && !FullyVisible(button, scroller); i++)
                {
                    var chevron = button.TranslatePoint(default, scroller)!.Value.X < 0 ? Left(ribbon) : Right(ribbon);
                    Assert.True(chevron.IsEffectivelyVisible, $"{tab.Name}: a button is out of sight and the chevron is hidden");
                    Press(window, chevron);
                }

                Assert.True(FullyVisible(button, scroller), $"{tab.Name}: a button stays clipped");
            }
        }
    }

    [AvaloniaFact]
    public void KeyboardFocusScrollsTheButtonIntoView()
    {
        var (window, _, ribbon) = Show(190);
        var scroller = Scroller(ribbon);
        var last = RibbonViewTests.Buttons(ribbon).Last(b => b.IsEnabled);
        Assert.False(FullyVisible(last, scroller), "the last button starts out of sight");

        ControlHarness.KeyboardFocus(window, last);

        Assert.True(FullyVisible(last, scroller));
        Assert.True(Left(ribbon).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void TheChevronsAreNamedForScreenReadersAndNeverTakeTheTabStop()
    {
        var (_, _, ribbon) = Show(190);
        Assert.Equal(ShellStrings.RibbonScrollLeft, AutomationProperties.GetName(Left(ribbon)));
        Assert.Equal(ShellStrings.RibbonScrollRight, AutomationProperties.GetName(Right(ribbon)));
        Assert.NotNull(ToolTip.GetTip(Left(ribbon)));
        Assert.NotNull(ToolTip.GetTip(Right(ribbon)));
        Assert.False(Left(ribbon).IsTabStop);
        Assert.False(Right(ribbon).IsTabStop);
    }

    [AvaloniaFact]
    public void TheFullMeasureOfATabIsTakenOnceAndKept()
    {
        var (window, _, ribbon) = Show(560);
        foreach (var tab in ribbon.Definition!.Tabs)
        {
            ribbon.ActiveTab = tab;
            ControlHarness.Settle(window);
        }

        var measures = ribbon.FullMeasureCount;
        foreach (var tab in ribbon.Definition.Tabs.Concat(ribbon.Definition.Tabs.Reverse()))
        {
            ribbon.ActiveTab = tab;
            ControlHarness.Settle(window);
        }

        Resize(window, 600);
        Resize(window, 560);
        Assert.Equal(measures, ribbon.FullMeasureCount);
        Assert.True(measures <= ribbon.Definition.Tabs.Count, $"{measures} measures for {ribbon.Definition.Tabs.Count} tabs");
    }

    [AvaloniaFact]
    public void AllWideEnoughNothingIsMeasuredBeyondTheVisibleBand()
    {
        // The full band that is on screen is its own measure: no extra build for it.
        var (_, _, ribbon) = Show(1400);
        Assert.Equal(0, ribbon.FullMeasureCount);
    }

    [AvaloniaFact]
    public void ABandThatIsNotInAFrameStillDecides()
    {
        // The band alone in a window (no AppFrame around): the decision uses its own width.
        var ribbon = new RibbonView { Definition = SampleRibbon.Create() };
        var window = ControlHarness.Show(ribbon.Band, ThemeMode.Light);
        Resize(window, 560);
        Assert.True(ribbon.IsEffectivelyCompact);
        Resize(window, 1400);
        Assert.False(ribbon.IsEffectivelyCompact);
    }
}
