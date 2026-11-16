using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

/// <summary>
/// A held band stays an empty strip of its final height until it is released, so an app can put its first page on screen before it lays out the ribbon. The release makes the same choice between
/// the full and the compact band that a band which was never held makes.
/// </summary>
public sealed class RibbonHoldTests
{
    private static (Window Window, AppFrame Frame, RibbonView Ribbon) Show(double width, bool hold = true, double hint = 0)
    {
        var definition = SampleRibbon.Create();
        var ribbon = new RibbonView { Definition = new RibbonDefinition([.. definition.Tabs.Select(t => t with { FullWidthAtLeast = hint })]) };
        if (hold)
        {
            ribbon.HoldBand();
        }

        var frame = new AppFrame { TabRow = ribbon.TabRow, Band = ribbon.Band };
        var window = ControlHarness.Show(frame, ThemeMode.Light);
        window.Width = width;
        ControlHarness.Settle(window);
        ControlHarness.Settle(window);
        return (window, frame, ribbon);
    }

    private static double BandRow(AppFrame frame) => ControlHarness.Part<Border>(frame, "PART_BandRow").Bounds.Height;

    [AvaloniaFact]
    public void AHeldBandIsAnEmptyStripOfItsFinalHeightAndNothingIsLaidOut()
    {
        var (_, frame, ribbon) = Show(1400);

        Assert.True(ribbon.IsBandHeld);
        Assert.Null(((Border)ribbon.Band).Child);
        Assert.Equal(82, BandRow(frame), 0.5);
        Assert.Empty(RibbonViewTests.Buttons(ribbon));
        Assert.Equal(0, ribbon.FullMeasureCount);
    }

    [AvaloniaFact]
    public void TheReleaseShowsTheFullBandInAWideWindow()
    {
        var (window, frame, ribbon) = Show(1400);
        ribbon.ReleaseBand();
        ControlHarness.Settle(window);
        ControlHarness.Settle(window);

        Assert.False(ribbon.IsBandHeld);
        Assert.False(ribbon.IsEffectivelyCompact);
        Assert.Equal(82, BandRow(frame), 0.5);
        Assert.Contains(RibbonViewTests.Buttons(ribbon), b => b.Kind == RibbonButtonKind.Tile);
    }

    [AvaloniaFact]
    public void TheReleaseDecidesForANarrowWindowAsABandThatWasNeverHeldDoes()
    {
        var (window, frame, ribbon) = Show(560);
        // While held nothing is measured, so nothing can be decided yet.
        Assert.False(ribbon.IsEffectivelyCompact);
        Assert.Equal(0, ribbon.FullMeasureCount);

        ribbon.ReleaseBand();
        ControlHarness.Settle(window);
        ControlHarness.Settle(window);

        Assert.True(ribbon.IsEffectivelyCompact);
        Assert.Equal(40, BandRow(frame), 0.5);
        Assert.DoesNotContain(RibbonViewTests.Buttons(ribbon), b => b.Kind == RibbonButtonKind.Tile);
    }

    [AvaloniaFact]
    public void AHintStillDecidesWhileTheBandIsHeld()
    {
        var (_, frame, ribbon) = Show(900, hint: 5000);

        Assert.True(ribbon.IsBandHeld);
        Assert.True(ribbon.IsEffectivelyCompact);
        Assert.Equal(40, BandRow(frame), 0.5);
        Assert.Empty(RibbonViewTests.Buttons(ribbon));
    }

    [AvaloniaFact]
    public void TheFieldCodesReleaseTheBandAndStandOnItsButtons()
    {
        var (window, _, ribbon) = Show(1400);

        ribbon.ShowFieldCodes = true;
        ControlHarness.Settle(window);

        Assert.False(ribbon.IsBandHeld);
        Assert.NotEmpty(RibbonViewTests.Buttons(ribbon));
        Assert.All(RibbonViewTests.Buttons(ribbon), b => Assert.True(b.Bounds.Width > 0, "a button was not laid out when the key tips appeared"));
    }

    [AvaloniaFact]
    public void ReleasingTwiceAndHoldingAfterTheReleaseChangeNothing()
    {
        var (window, _, ribbon) = Show(1400);
        ribbon.ReleaseBand();
        ribbon.ReleaseBand();
        ControlHarness.Settle(window);
        var buttons = RibbonViewTests.Buttons(ribbon).Count();

        ribbon.HoldBand();
        Assert.True(ribbon.IsBandHeld);
        ribbon.ReleaseBand();
        ControlHarness.Settle(window);
        Assert.Equal(buttons, RibbonViewTests.Buttons(ribbon).Count());
    }
}
