using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Raffica.Gallery.FrameDemo;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Raffica.Shell.Tests.Frame;
using Xunit;

namespace Raffica.Shell.Tests;

/// <summary>M4.3b Task 10: the picker item of the ribbon (font family, size, styles of Write).</summary>
public sealed class RibbonPickerTests
{
    private sealed class State
    {
        public string? Selected = "b";
        public bool Mixed;
        public bool Enabled = true;
        public int ListReads;
        public readonly List<string> Chosen = [];
        public readonly List<string> Invoked = [];
    }

    private static readonly PickerItem[] Items = [new("a", "Alpha"), new("b", "Beta"), new("c", "Gamma")];

    private static readonly PickerItem[] Sizes = [new("10", "10"), new("11", "11"), new("12", "12"), new("14", "14")];

    private static RibbonDefinition Build(State state, bool editable, CompactPlacement placement, PickerItem[]? items = null, int maxLength = 0)
    {
        var list = items ?? Items;
        var picker = new CommandDefinition("font.family", "Font family", "Raffica.Icon.Check", "31",
            Compact: placement,
            EnabledWhen: () => state.Enabled,
            DisabledReason: () => "Open a document first.",
            Picker: new PickerDefinition(
                () =>
                {
                    state.ListReads++;
                    return list;
                },
                id => state.Chosen.Add(id),
                SelectedId: () => state.Selected,
                IsMixed: () => state.Mixed,
                Editable: editable,
                Width: 120,
                MaxLength: maxLength));
        var tab = new RibbonTabDefinition("Home", "H",
        [
            new RibbonGroupDefinition("Font", "1",
            [
                picker,
                new CommandDefinition("bold", "Bold", "Raffica.Icon.Bold", "32", IconOnly: true),
                new CommandDefinition("italic", "Italic", "Raffica.Icon.Italic", "33", Compact: CompactPlacement.Overflow),
                new CommandDefinition("strike", "Strikethrough", "Raffica.Icon.Minus", "34"),
            ]),
        ]);
        var definition = new RibbonDefinition([tab]);
        definition.Validate();
        return definition;
    }

    private static (Window Window, RibbonView Ribbon, State State) Show(bool compact = false, bool editable = false, CompactPlacement placement = CompactPlacement.Inline, bool fieldCodes = false, PickerItem[]? items = null, int maxLength = 0)
    {
        var state = new State();
        var ribbon = new RibbonView { Definition = Build(state, editable, placement, items, maxLength), IsCompact = compact };
        ribbon.CommandInvoked += c => state.Invoked.Add(c.Id);
        var frame = new AppFrame { TabRow = ribbon.TabRow, Band = ribbon.Band, IsCompact = compact };
        var window = ControlHarness.Show(frame, ThemeMode.Light);
        window.Width = 1400;
        ControlHarness.Settle(window);
        if (fieldCodes)
        {
            ribbon.ShowFieldCodes = true;
            ribbon.FieldCodeTabLetter = "H";
            ControlHarness.Settle(window);
        }

        return (window, ribbon, state);
    }

    private static RibbonPicker PickerOf(RibbonView ribbon) =>
        ControlHarness.Descendants(ribbon.Band).OfType<RibbonPicker>().First();

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        ControlHarness.Settle(window);
    }

    [AvaloniaFact]
    public void ThePickerListsItemsAndReportsTheChosenId()
    {
        var (window, ribbon, state) = Show();
        var picker = PickerOf(ribbon);

        Assert.Equal(0, state.ListReads);
        Assert.Equal("Font family", AutomationProperties.GetName(picker));

        picker.IsDropDownOpen = true;
        ControlHarness.Settle(window);
        Assert.Equal(1, state.ListReads);
        Assert.Equal(["Alpha", "Beta", "Gamma"], picker.Items.Cast<object>().Select(i => i.ToString()));
        Assert.Equal(1, picker.SelectedIndex);

        picker.SelectedIndex = 2;
        ControlHarness.Settle(window);
        Assert.Equal(["c"], state.Chosen);
        Assert.Equal(["font.family"], state.Invoked);

        // The list is asked again on every open, so a changed list is current.
        picker.IsDropDownOpen = false;
        picker.IsDropDownOpen = true;
        ControlHarness.Settle(window);
        Assert.Equal(2, state.ListReads);
    }

    [AvaloniaFact]
    public void MixedShowsNoSelection()
    {
        var (window, ribbon, state) = Show();
        var picker = PickerOf(ribbon);
        state.Mixed = true;
        ribbon.RefreshCommandStates();
        picker.IsDropDownOpen = true;
        ControlHarness.Settle(window);

        Assert.Equal(-1, picker.SelectedIndex);
        Assert.Equal(ShellStrings.PickerMixed, picker.PlaceholderText);
        Assert.Empty(state.Chosen);
        var value = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(picker));
        Assert.Equal(ShellStrings.PickerMixed, value.Value);

        state.Mixed = false;
        state.Selected = "c";
        ribbon.RefreshCommandStates();
        ControlHarness.Settle(window);
        Assert.Equal(2, picker.SelectedIndex);
        Assert.Empty(state.Chosen);
        Assert.Equal("Gamma", Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(picker)).Value);
    }

    [AvaloniaFact]
    public void TheSizeBoxCommitsOnEnterAndRevertsOnEscape()
    {
        var (window, ribbon, state) = Show(editable: true);
        state.Selected = "11";
        ribbon.RefreshCommandStates();
        var picker = PickerOf(ribbon);
        var box = picker.GetVisualDescendants().OfType<TextBox>().First();
        Assert.Equal("11", picker.Text);
        ControlHarness.KeyboardFocus(window, box);

        picker.Text = "13";
        Press(window, PhysicalKey.Enter);
        Assert.Equal(["13"], state.Chosen);

        picker.Text = "99";
        Press(window, PhysicalKey.Escape);
        Assert.Equal("11", picker.Text);
        Assert.Equal(["13"], state.Chosen);

        // Numbers only: a letter never reaches the box, a digit and one decimal mark do.
        box.CaretIndex = box.Text!.Length;
        window.KeyTextInput("x");
        ControlHarness.Settle(window);
        Assert.Equal("11", picker.Text);
        window.KeyTextInput("5");
        ControlHarness.Settle(window);
        Assert.Equal("115", picker.Text);
    }

    [AvaloniaFact]
    public void ADisabledPickerShowsItsReason()
    {
        var (window, ribbon, state) = Show();
        var picker = PickerOf(ribbon);
        Assert.True(picker.IsEnabled);

        state.Enabled = false;
        ribbon.RefreshCommandStates();
        ControlHarness.Settle(window);

        Assert.False(picker.IsEnabled);
        var tip = Assert.IsType<TooltipContent>(ToolTip.GetTip(picker));
        Assert.Equal("Open a document first.", tip.Description);
        Assert.True(ToolTip.GetShowOnDisabled(picker));

        state.Enabled = true;
        ribbon.RefreshCommandStates();
        Assert.True(picker.IsEnabled);
        Assert.NotEqual("Open a document first.", ((TooltipContent)ToolTip.GetTip(picker)!).Description);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheKeyTipFitsTheBadge(bool editable)
    {
        var (window, ribbon, _) = Show(editable: editable, fieldCodes: true);

        Assert.Contains(window.GetVisualDescendants().OfType<FieldCodeBadge>(), b => b.Text == "31");
        var offenders = BadgeFit.Find(window, ribbon.Band);
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders.Distinct()));
    }

    [AvaloniaFact]
    public void TheCompactBarKeepsThePickerReachable()
    {
        // Inline: the picker stays a picker in the compact band.
        var (window, ribbon, state) = Show(compact: true);
        Assert.True(ribbon.IsEffectivelyCompact);
        Assert.NotNull(PickerOf(ribbon));
        Assert.Equal(0, state.ListReads);

        // Overflow: it becomes a menu item with its name in the group's more menu, and its list is read only when the submenu opens.
        var (window2, ribbon2, state2) = Show(compact: true, placement: CompactPlacement.Overflow);
        Assert.Empty(ControlHarness.Descendants(ribbon2.Band).OfType<RibbonPicker>());
        var more = ControlHarness.Descendants(ribbon2.Band).OfType<Button>().First(b => b.Classes.Contains("ribbon-more"));
        var flyout = Assert.IsType<MenuFlyout>(more.Flyout);
        var item = flyout.Items.OfType<MenuItem>().First(i => Equals(i.Header, "Font family"));
        Assert.Equal(0, state2.ListReads);

        item.Open();
        ControlHarness.Settle(window2);
        Assert.Equal(1, state2.ListReads);
        var choices = item.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Alpha", "Beta", "Gamma"], choices.Select(c => c.Header?.ToString()));
        Assert.True(choices[1].IsChecked);

        choices[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        ControlHarness.Settle(window2);
        Assert.Equal(["c"], state2.Chosen);
        Assert.Equal(["font.family"], state2.Invoked);
        Assert.NotNull(window);
    }

    [AvaloniaFact]
    public void APickerCannotBeLargeOrHaveAMenu()
    {
        var picker = new PickerDefinition(() => Items, _ => { });
        var large = new RibbonDefinition([new RibbonTabDefinition("T", "T", [new RibbonGroupDefinition("G", "1", [new CommandDefinition("p", "P", "Raffica.Icon.Check", "11", Size: CommandSize.Large, Picker: picker)])])]);
        Assert.Throws<InvalidOperationException>(large.Validate);

        var both = new RibbonDefinition([new RibbonTabDefinition("T", "T", [new RibbonGroupDefinition("G", "1", [new CommandDefinition("p", "P", "Raffica.Icon.Check", "11", Items: [new CommandDefinition("q", "Q", "Raffica.Icon.Check", "A")], Picker: picker)])])]);
        Assert.Throws<InvalidOperationException>(both.Validate);
    }

    [AvaloniaFact]
    public void TheGalleryShowsThePicker()
    {
        var settings = Path.Combine(Path.GetTempPath(), "raffica-picker-" + Guid.NewGuid().ToString("N") + ".json");
        ShellTheme.Apply(Application.Current!, ThemeMode.Light, AppInk.Write, forceHighContrast: false);
        var window = new FrameDemoWindow(GalleryOptions.Parse(["--view", "frame", "--settings-file", settings])) { Width = 1400, Height = 700 };
        try
        {
            window.Show();
            ControlHarness.Settle(window);

            var picker = ControlHarness.Descendants(window.Ribbon.Band).OfType<RibbonPicker>().Single();
            Assert.Equal(GalleryStrings.CmdFontFamily, AutomationProperties.GetName(picker));
            Assert.True(picker.Bounds.Width > 0);
        }
        finally
        {
            window.Close();
            File.Delete(settings);
        }
    }

    private static TextBox BoxOf(RibbonPicker picker) => picker.GetVisualDescendants().OfType<TextBox>().First();

    [AvaloniaFact]
    public void TypingTheFieldCodeOpensTheInlinePicker()
    {
        var state = new State();
        var f = FieldCodeFixture.Create(definition: Build(state, editable: false, CompactPlacement.Inline));
        var picker = PickerOf(f.Ribbon);
        Assert.Equal(0, state.ListReads);

        f.Alt();
        f.TypeCode("H");
        f.TypeCode("31");

        Assert.True(picker.IsDropDownOpen);
        Assert.Equal(1, state.ListReads);
        Assert.Empty(state.Chosen);
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
    }

    [AvaloniaFact]
    public void TypingTheFieldCodeFocusesTheSizeBoxWithTheTextSelected()
    {
        var state = new State { Selected = "11" };
        var f = FieldCodeFixture.Create(definition: Build(state, editable: true, CompactPlacement.Inline));
        var picker = PickerOf(f.Ribbon);
        picker.RefreshState();

        f.Alt();
        f.TypeCode("H");
        f.TypeCode("31");

        var box = BoxOf(picker);
        Assert.True(box.IsFocused);
        Assert.Equal("11", box.SelectedText);
        Assert.Equal("11", picker.Text);
        Assert.False(picker.IsDropDownOpen);

        // The code's characters were not typed into the box; the next digits overtype the selection.
        f.Window.KeyTextInput("8");
        ControlHarness.Settle(f.Window);
        Assert.Equal("8", picker.Text);
    }

    [AvaloniaFact]
    public void TypingTheFieldCodeOfAnOverflowedPickerOpensItsSubmenu()
    {
        var state = new State();
        var f = FieldCodeFixture.Create(definition: Build(state, editable: false, CompactPlacement.Overflow));
        f.Ribbon.IsCompact = true;
        ControlHarness.Settle(f.Window);
        Assert.Empty(ControlHarness.Descendants(f.Ribbon.Band).OfType<RibbonPicker>());

        f.Alt();
        f.TypeCode("H");
        f.TypeCode("31");
        ControlHarness.Settle(f.Window);

        var more = ControlHarness.Descendants(f.Ribbon.Band).OfType<Button>().First(b => b.Classes.Contains("ribbon-more"));
        var flyout = Assert.IsType<MenuFlyout>(more.Flyout);
        Assert.True(flyout.IsOpen);
        var item = flyout.Items.OfType<MenuItem>().First(i => Equals(i.Header, "Font family"));
        Assert.True(item.IsSubMenuOpen);
        Assert.Equal(1, state.ListReads);
        Assert.Equal(["Alpha", "Beta", "Gamma"], item.Items.OfType<MenuItem>().Select(c => c.Header?.ToString()));
        Assert.Empty(state.Chosen);
    }

    [AvaloniaFact]
    public void ATypedMatchWithTheListLoadedDoesNotCommitBeforeEnter()
    {
        var (window, ribbon, state) = Show(editable: true, items: Sizes);
        state.Selected = "11";
        ribbon.RefreshCommandStates();
        var picker = PickerOf(ribbon);
        picker.IsDropDownOpen = true;
        ControlHarness.Settle(window);
        picker.IsDropDownOpen = false;
        ControlHarness.Settle(window);
        Assert.True(picker.ItemsLoaded);
        ControlHarness.KeyboardFocus(window, BoxOf(picker));

        picker.Text = "";
        window.KeyTextInput("1");
        window.KeyTextInput("2");
        ControlHarness.Settle(window);

        Assert.Equal("12", picker.Text);
        Assert.Empty(state.Chosen);
        Assert.Empty(state.Invoked);

        Press(window, PhysicalKey.Enter);
        Assert.Equal(["12"], state.Chosen);
    }

    [AvaloniaFact]
    public void ChoosingFromTheListOfTheSizeBoxCommits()
    {
        var (window, ribbon, state) = Show(editable: true, items: Sizes);
        var picker = PickerOf(ribbon);
        picker.IsDropDownOpen = true;
        ControlHarness.Settle(window);

        picker.SelectedIndex = 3;
        ControlHarness.Settle(window);

        Assert.Equal(["14"], state.Chosen);
        Assert.NotNull(ribbon);
    }

    [AvaloniaFact]
    public async Task APastedNonNumberNeverStaysInTheBox()
    {
        var (window, ribbon, state) = Show(editable: true, maxLength: 4);
        state.Selected = "11";
        ribbon.RefreshCommandStates();
        var picker = PickerOf(ribbon);
        var box = BoxOf(picker);
        ControlHarness.KeyboardFocus(window, box);

        await window.Clipboard!.SetTextAsync("abc");
        box.SelectAll();
        box.Paste();
        ControlHarness.Settle(window);
        Assert.Equal("11", picker.Text);
        Assert.Equal("11", box.Text);

        await window.Clipboard!.SetTextAsync("99999999999");
        box.SelectAll();
        box.Paste();
        ControlHarness.Settle(window);
        Assert.Equal("11", picker.Text);

        await window.Clipboard!.SetTextAsync("1638");
        box.SelectAll();
        box.Paste();
        ControlHarness.Settle(window);
        Assert.Equal("1638", picker.Text);
        Press(window, PhysicalKey.Enter);
        Assert.Equal(["1638"], state.Chosen);
    }

    [AvaloniaFact]
    public void TheSizeBoxStopsAtTheDigitCap()
    {
        var (window, ribbon, state) = Show(editable: true, maxLength: 4);
        state.Selected = "";
        ribbon.RefreshCommandStates();
        var picker = PickerOf(ribbon);
        ControlHarness.KeyboardFocus(window, BoxOf(picker));

        foreach (var c in "123456789")
        {
            window.KeyTextInput(c.ToString());
        }

        ControlHarness.Settle(window);
        Assert.Equal("1234", picker.Text);
        Press(window, PhysicalKey.Enter);
        Assert.Equal(["1234"], state.Chosen);
    }

    [AvaloniaFact]
    public void EmptyTextAndEnterRevertsWithoutCommitting()
    {
        var (window, ribbon, state) = Show(editable: true);
        state.Selected = "11";
        ribbon.RefreshCommandStates();
        var picker = PickerOf(ribbon);
        ControlHarness.KeyboardFocus(window, BoxOf(picker));

        picker.Text = "";
        Press(window, PhysicalKey.Enter);

        Assert.Equal("11", picker.Text);
        Assert.Empty(state.Chosen);
    }

    [AvaloniaFact]
    public void APendingEditSurvivesARefreshAndLeavingTheBoxPutsTheStateBack()
    {
        var (window, ribbon, state) = Show(editable: true);
        state.Selected = "11";
        ribbon.RefreshCommandStates();
        var picker = PickerOf(ribbon);
        ControlHarness.KeyboardFocus(window, BoxOf(picker));

        picker.Text = "7";
        ribbon.RefreshCommandStates();
        Assert.Equal("7", picker.Text);

        // The state moved on meanwhile; leaving the box shows it.
        state.Selected = "13";
        var other = ControlHarness.Descendants(ribbon.Band).OfType<RibbonButton>().First();
        ControlHarness.KeyboardFocus(window, other);
        Assert.Equal("13", picker.Text);
        Assert.Empty(state.Chosen);
    }

    [AvaloniaFact]
    public void TheAutomationValueSetsTheSizeOnlyWhileEnabled()
    {
        var (window, ribbon, state) = Show(editable: true);
        var picker = PickerOf(ribbon);
        var peer = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(picker));

        state.Enabled = false;
        ribbon.RefreshCommandStates();
        peer.SetValue("14");
        Assert.Empty(state.Chosen);

        state.Enabled = true;
        ribbon.RefreshCommandStates();
        peer.SetValue("14");
        Assert.Equal(["14"], state.Chosen);
        Assert.NotNull(window);
    }
}
