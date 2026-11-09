using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class RibbonCompactTests
{
    private static List<Button> MoreButtons(RibbonView ribbon) =>
        ControlHarness.Descendants(ribbon.Band).OfType<Button>().Where(b => b.Classes.Contains("ribbon-more")).ToList();

    private static IEnumerable<MenuItem> Flat(IEnumerable<MenuItem> items) =>
        items.SelectMany(i => new[] { i }.Concat(Flat(i.Items.OfType<MenuItem>())));

    [AvaloniaFact]
    public void BandIs40High()
    {
        var (_, frame, ribbon) = RibbonViewTests.Show(compact: true);
        Assert.Equal(40, ControlHarness.Part<Border>(frame, "PART_BandRow").Bounds.Height, 0.5);
        Assert.True(frame.IsCompact);
        Assert.True(ribbon.Band.Bounds.Height <= 40.01);
    }

    [AvaloniaFact]
    public void LabelsAreHiddenButKeptAsNames()
    {
        var (_, _, ribbon) = RibbonViewTests.Show(compact: true);
        Assert.Empty(ControlHarness.Descendants(ribbon.Band).OfType<TextBlock>());
        var paste = RibbonViewTests.ButtonFor(ribbon, "paste");
        Assert.Equal("Paste", AutomationProperties.GetName(paste));
        Assert.NotNull(ToolTip.GetTip(paste));
    }

    [AvaloniaFact]
    public void LargeTilesBecomeSmallButtons()
    {
        var (_, _, ribbon) = RibbonViewTests.Show(compact: true);
        foreach (var id in new[] { "paste", "find", "clearfmt" })
        {
            var button = RibbonViewTests.ButtonFor(ribbon, id);
            Assert.Equal(26, button.Bounds.Height, 0.01);
            Assert.True(button.Bounds.Width >= 24);
        }
    }

    [AvaloniaFact]
    public void EveryCommandIsReachableInCompactMode()
    {
        var (window, _, ribbon) = RibbonViewTests.Show(compact: true);
        foreach (var tab in ribbon.Definition!.Tabs)
        {
            ribbon.ActiveTab = tab;
            ControlHarness.Settle(window);

            var visible = RibbonViewTests.Buttons(ribbon).Where(b => b.IsVisible && b.Bounds.Width > 0).Select(b => b.Definition.Id).ToHashSet();
            var inOverflow = MoreButtons(ribbon)
                .SelectMany(b => Flat(Assert.IsType<MenuFlyout>(b.Flyout).Items.OfType<MenuItem>()))
                .Select(i => ((CommandDefinition)i.Tag!).Id)
                .ToHashSet();

            foreach (var group in tab.Groups)
            {
                foreach (var command in group.Commands)
                {
                    Assert.True(visible.Contains(command.Id) || inOverflow.Contains(command.Id), $"{tab.Name}/{command.Id} is not reachable");
                }
            }
        }
    }

    [AvaloniaFact]
    public void GroupWithoutOverflowHasNoDropdownButton()
    {
        var (_, _, ribbon) = RibbonViewTests.Show(compact: true);
        var tab = ribbon.ActiveTab!;
        var withOverflow = tab.Groups.Count(g => g.Commands.Any(c => c.Compact == CompactPlacement.Overflow));

        Assert.True(withOverflow < tab.Groups.Count, "the sample has a group without overflow");
        Assert.Equal(withOverflow, MoreButtons(ribbon).Count);
        foreach (var more in MoreButtons(ribbon))
        {
            Assert.Equal(24, more.MinWidth, 2);
            Assert.NotEmpty(Assert.IsType<MenuFlyout>(more.Flyout).Items);
        }
    }

    [AvaloniaFact]
    public void NoFrequentCommandIsOnlyInTheOverflow()
    {
        var (window, _, ribbon) = RibbonViewTests.Show(compact: true);
        foreach (var tab in ribbon.Definition!.Tabs)
        {
            ribbon.ActiveTab = tab;
            ControlHarness.Settle(window);
            var visible = RibbonViewTests.Buttons(ribbon).Where(b => b.IsVisible && b.Bounds.Width > 0).Select(b => b.Definition.Id).ToHashSet();
            foreach (var command in tab.Groups.SelectMany(g => g.Commands).Where(c => c.Compact == CompactPlacement.Inline))
            {
                Assert.Contains(command.Id, visible);
            }
        }
    }
}
