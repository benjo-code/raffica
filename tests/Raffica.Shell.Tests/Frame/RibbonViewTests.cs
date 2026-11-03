using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class RibbonViewTests
{
    internal static (Window Window, AppFrame Frame, RibbonView Ribbon) Show(ThemeMode mode = ThemeMode.Light, bool compact = false)
    {
        var ribbon = new RibbonView { Definition = SampleRibbon.Create(), IsCompact = compact };
        var frame = new AppFrame { TabRow = ribbon.TabRow, Band = ribbon.Band, IsCompact = compact };
        var window = ControlHarness.Show(frame, mode);
        window.Width = 1400;
        ControlHarness.Settle(window);
        return (window, frame, ribbon);
    }

    internal static IEnumerable<RibbonButton> Buttons(RibbonView ribbon) =>
        ControlHarness.Descendants(ribbon.Band).OfType<RibbonButton>();

    internal static RibbonButton ButtonFor(RibbonView ribbon, string id) =>
        Buttons(ribbon).First(b => b.Definition.Id == id);

    private static Button TabButton(RibbonView ribbon, string name) =>
        ribbon.TabRow.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == name);

    private static void Click(Window window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        ControlHarness.Settle(window);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ActiveTabIsShownInInkWithItsRule(ThemeMode mode)
    {
        var (_, _, ribbon) = Show(mode);
        var active = TabButton(ribbon, "Home");
        var other = TabButton(ribbon, "Insert");

        Assert.Equal(33, active.Bounds.Height, 0.01);
        Assert.Equal(FontWeight.SemiBold, active.FontWeight);
        Assert.Equal(new Thickness(1, 1, 1, 0), active.BorderThickness);
        ControlHarness.AssertToken("Raffica.Brush.Ink", active.Foreground, "active tab text");
        ControlHarness.AssertToken("Raffica.Brush.Ink", active.BorderBrush, "active tab border");
        Assert.Equal(Colors.Transparent, ControlHarness.ColorOf(active.Background));
        Assert.NotEqual(FontWeight.SemiBold, other.FontWeight);
        Assert.Equal(Colors.Transparent, ControlHarness.ColorOf(other.BorderBrush));

        var rule = ControlHarness.Part<Border>(ribbon.TabRow, "PART_TabRule");
        Assert.Equal(1, rule.Bounds.Height, 0.01);
        ControlHarness.AssertToken("Raffica.Brush.Ink", rule.Background, "tab row rule");
    }

    [AvaloniaFact]
    public void SwitchingTabsShowsItsGroups()
    {
        var (window, _, ribbon) = Show();
        Assert.Contains(Buttons(ribbon), b => b.Definition.Id == "bold");
        Assert.DoesNotContain(Buttons(ribbon), b => b.Definition.Id == "picture");

        Click(window, TabButton(ribbon, "Insert"));

        Assert.Same(ribbon.Definition!.Tabs[1], ribbon.ActiveTab);
        Assert.Contains(Buttons(ribbon), b => b.Definition.Id == "picture");
        Assert.DoesNotContain(Buttons(ribbon), b => b.Definition.Id == "bold");
        Assert.Equal(33, TabButton(ribbon, "Insert").Bounds.Height, 0.01);
        Assert.NotEqual(33, TabButton(ribbon, "Home").Bounds.Height);
    }

    [AvaloniaFact]
    public void LargeTileIs54HighAndAtLeast48Wide()
    {
        var (window, frame, ribbon) = Show();
        foreach (var id in new[] { "paste", "find", "clearfmt" })
        {
            var tile = ButtonFor(ribbon, id);
            Assert.Equal(54, tile.Bounds.Height, 0.01);
            Assert.True(tile.Bounds.Width >= 48, $"{id} is {tile.Bounds.Width} wide");
        }

        Assert.True(ButtonFor(ribbon, "paste").TranslatePoint(default, window)!.Value.X < ButtonFor(ribbon, "cut").TranslatePoint(default, window)!.Value.X, "the large command comes first");
        Assert.Equal(82, ControlHarness.Part<Border>(frame, "PART_BandRow").Bounds.Height, 0.5);
    }

    [AvaloniaFact]
    public void SmallButtonIs26High()
    {
        var (_, _, ribbon) = Show();
        foreach (var id in new[] { "bold", "cut", "undo" })
        {
            var button = ButtonFor(ribbon, id);
            Assert.Equal(26, button.Bounds.Height, 0.01);
            Assert.True(button.Bounds.Width >= 24);
        }
    }

    [AvaloniaFact]
    public void OnlyTheDestructiveGroupHasARule()
    {
        var (window, _, ribbon) = Show();
        int Rules() => ControlHarness.Descendants(ribbon.Band).OfType<Border>().Count(b => b.Classes.Contains("ribbon-rule"));

        Assert.Equal(1, Rules());
        var rule = ControlHarness.Descendants(ribbon.Band).OfType<Border>().First(b => b.Classes.Contains("ribbon-rule"));
        Assert.Equal(1, rule.Bounds.Width, 0.01);
        var ruleX = rule.TranslatePoint(default, window)!.Value.X;
        var clearX = ButtonFor(ribbon, "clearfmt").TranslatePoint(default, window)!.Value.X;
        Assert.True(ruleX < clearX, "the rule sits before the destructive group");

        Click(window, TabButton(ribbon, "Review"));
        Assert.Equal(0, Rules());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCommandHasAnAutomationName(bool compact)
    {
        var (window, _, ribbon) = Show(compact: compact);
        foreach (var tab in ribbon.Definition!.Tabs)
        {
            ribbon.ActiveTab = tab;
            ControlHarness.Settle(window);
            var buttons = ControlHarness.Descendants(ribbon.Band).OfType<Button>().ToList();
            Assert.NotEmpty(buttons);
            foreach (var button in buttons)
            {
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), $"a button in {tab.Name} has no name");
                if (button is RibbonButton ribbonButton)
                {
                    Assert.Equal(ribbonButton.Definition.Name, AutomationProperties.GetName(button));
                }
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void GroupLabelsAreSentenceCase12pxSecondary(ThemeMode mode)
    {
        var (_, _, ribbon) = Show(mode);
        var labels = ControlHarness.Descendants(ribbon.Band).OfType<TextBlock>().Where(t => t.Classes.Contains("ribbon-group-label")).ToList();

        Assert.Equal(ribbon.ActiveTab!.Groups.Select(g => g.Label), labels.Select(l => l.Text));
        foreach (var label in labels)
        {
            Assert.Equal(12, label.FontSize);
            ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", label.Foreground, "group label");
            Assert.Equal(label.Text![..1].ToUpperInvariant() + label.Text[1..].ToLowerInvariant(), label.Text);
        }
    }

    [AvaloniaFact]
    public void MenuCommandOpensItsItems()
    {
        var (window, _, ribbon) = Show();
        var paste = ButtonFor(ribbon, "paste");
        var flyout = Assert.IsType<MenuFlyout>(paste.Flyout);
        Assert.Equal(["Keep source formatting", "Keep text only"], flyout.Items.OfType<MenuItem>().Select(i => i.Header));

        Click(window, paste);

        Assert.True(flyout.IsOpen);
    }

    [AvaloniaFact]
    public void CommandInvokedRaisesWithTheCommand()
    {
        var (window, _, ribbon) = Show();
        var raised = new List<CommandDefinition>();
        ribbon.CommandInvoked += raised.Add;

        Click(window, ButtonFor(ribbon, "bold"));
        Assert.Equal(["bold"], raised.Select(c => c.Id));

        raised.Clear();
        var item = Assert.IsType<MenuFlyout>(ButtonFor(ribbon, "paste").Flyout).Items.OfType<MenuItem>().First();
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(["paste.keep"], raised.Select(c => c.Id));
    }

    [AvaloniaFact]
    public void KeyboardFocusStaysOnTheTabAfterSwitching()
    {
        var (window, _, ribbon) = Show();
        var insert = TabButton(ribbon, "Insert");
        ControlHarness.KeyboardFocus(window, insert);
        Assert.True(insert.IsFocused);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        ControlHarness.Settle(window);

        Assert.Same(ribbon.Definition!.Tabs[1], ribbon.ActiveTab);
        Assert.Same(insert, TabButton(ribbon, "Insert"));
        Assert.True(insert.IsFocused);
        Assert.True(insert.IsEffectivelyVisible);
        Assert.Contains("active", insert.Classes);

        ribbon.IsCompact = true;
        ControlHarness.Settle(window);
        Assert.Same(insert, TabButton(ribbon, "Insert"));
        Assert.True(insert.IsFocused);
    }

    [AvaloniaFact]
    public void ReplacingTheDefinitionRebuildsOnceAndResetsTheActiveTab()
    {
        var (window, _, ribbon) = Show();
        ribbon.ActiveTab = ribbon.Definition!.Tabs[2];
        ControlHarness.Settle(window);

        var fresh = SampleRibbon.Create();
        ribbon.Definition = fresh;
        ControlHarness.Settle(window);

        Assert.Same(fresh.Tabs[0], ribbon.ActiveTab);
        Assert.Contains(Buttons(ribbon), b => b.Definition.Id == "bold");
        Assert.Equal(1, ribbon.TabRow.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("active")));
    }
}
