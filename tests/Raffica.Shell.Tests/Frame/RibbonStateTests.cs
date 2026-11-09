using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

/// <summary>
/// M2.7 Task 10 step 4: a command that is a switch or a choice shows when it is on: a pressed look (the ink fill of the toggle) and the
/// UIA Toggle pattern; the tabs of the ribbon are selection items.
/// </summary>
public sealed class RibbonStateTests
{
    private sealed class Flags
    {
        public bool Dark;
        public bool Single = true;
    }

    private static (Window Window, RibbonView Ribbon, Flags Flags) Show(ThemeMode mode = ThemeMode.Light, bool compact = false)
    {
        var flags = new Flags();
        var tab = new RibbonTabDefinition("View", "V",
        [
            new RibbonGroupDefinition("Page", "1",
            [
                new CommandDefinition("page.dark", "Dark page", "Raffica.Icon.Check", "11", Size: CommandSize.Large, CheckedWhen: () => flags.Dark),
                new CommandDefinition("page.single", "Single page", "Raffica.Icon.Check", "12", CheckedWhen: () => flags.Single),
                new CommandDefinition("page.plain", "Plain", "Raffica.Icon.Check", "13"),
            ]),
        ]);
        var other = new RibbonTabDefinition("Other", "O", [new RibbonGroupDefinition("G", "1", [new CommandDefinition("other.one", "One", "Raffica.Icon.Check", "11")])]);
        var ribbon = new RibbonView { Definition = new RibbonDefinition([tab, other]), IsCompact = compact };
        var frame = new AppFrame { TabRow = ribbon.TabRow, Band = ribbon.Band, IsCompact = compact };
        var window = ControlHarness.Show(frame, mode);
        window.Width = 1400;
        ControlHarness.Settle(window);
        return (window, ribbon, flags);
    }

    private static RibbonButton Button(RibbonView ribbon, string id) => RibbonViewTests.ButtonFor(ribbon, id);

    private static ToggleState StateOf(Control control) => Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(control)).ToggleState;

    [AvaloniaFact]
    public void ACheckedCommandExposesTheTogglePatternOn()
    {
        var (_, ribbon, _) = Show();

        Assert.Equal(ToggleState.On, StateOf(Button(ribbon, "page.single")));
        Assert.Equal(ToggleState.Off, StateOf(Button(ribbon, "page.dark")));
    }

    [AvaloniaFact]
    public void ACommandWithoutACheckedStateHasNoTogglePattern()
    {
        var (_, ribbon, _) = Show();

        var peer = ControlAutomationPeer.CreatePeerForElement(Button(ribbon, "page.plain"));
        Assert.False(peer is IToggleProvider);
        Assert.DoesNotContain("checked", Button(ribbon, "page.plain").Classes);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheStateIsReadAgainWithTheEnabledState(bool compact)
    {
        var (window, ribbon, flags) = Show(compact: compact);
        Assert.Equal(ToggleState.Off, StateOf(Button(ribbon, "page.dark")));
        Assert.Equal(ToggleState.On, StateOf(Button(ribbon, "page.single")));

        flags.Dark = true;
        flags.Single = false;
        ribbon.RefreshCommandStates();
        ControlHarness.Settle(window);

        Assert.Equal(ToggleState.On, StateOf(Button(ribbon, "page.dark")));
        Assert.Equal(ToggleState.Off, StateOf(Button(ribbon, "page.single")));
        Assert.Contains("checked", Button(ribbon, "page.dark").Classes);
        Assert.DoesNotContain("checked", Button(ribbon, "page.single").Classes);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ACheckedButtonIsDrawnPressedInInkAlsoUnderThePointer(ThemeMode mode)
    {
        var (window, ribbon, flags) = Show(mode);
        var button = Button(ribbon, "page.single");
        var plain = Button(ribbon, "page.plain");

        ControlHarness.AssertToken("Raffica.Brush.Ink", ControlHarness.Presenter(button).Background, "checked fill");
        ControlHarness.AssertToken("Raffica.Brush.Text.OnInk", ControlHarness.Presenter(button).Foreground, "checked text");
        Assert.NotEqual(ControlHarness.ColorOf(ControlHarness.Presenter(button).Background), ControlHarness.ColorOf(ControlHarness.Presenter(plain).Background));

        flags.Single = false;
        ribbon.RefreshCommandStates();
        ControlHarness.Settle(window);
        Assert.NotEqual(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(ControlHarness.Presenter(button).Background));
    }

    [AvaloniaFact]
    public void TheTogglePatternTogglesByRunningTheCommand()
    {
        var ran = 0;
        var tab = new RibbonTabDefinition("View", "V", [new RibbonGroupDefinition("G", "1", [new CommandDefinition("a.b", "Ab", "Raffica.Icon.Check", "11", () => ran++, CheckedWhen: () => false)])]);
        var ribbon = new RibbonView { Definition = new RibbonDefinition([tab]) };
        var window = ControlHarness.Show(new AppFrame { TabRow = ribbon.TabRow, Band = ribbon.Band }, ThemeMode.Light);
        ControlHarness.Settle(window);

        var provider = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(RibbonViewTests.ButtonFor(ribbon, "a.b")));
        provider.Toggle();

        Assert.Equal(1, ran);
    }

    [AvaloniaFact]
    public void ARibbonTabIsASelectionItemThatFollowsTheActiveTab()
    {
        var (window, ribbon, _) = Show();
        var tabs = ribbon.TabRow.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ribbon-tab")).ToList();
        Assert.Equal(2, tabs.Count);

        var first = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(tabs[0]));
        var second = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(tabs[1]));
        Assert.Equal(AutomationControlType.TabItem, ControlAutomationPeer.CreatePeerForElement(tabs[0]).GetAutomationControlType());
        Assert.True(first.IsSelected);
        Assert.False(second.IsSelected);

        second.Select();
        ControlHarness.Settle(window);

        Assert.Same(ribbon.Definition!.Tabs[1], ribbon.ActiveTab);
        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
    }
}
