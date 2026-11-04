using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

/// <summary>The enabled state of a command (<see cref="CommandDefinition.EnabledWhen"/>): the session, the ribbon and the search respect it.</summary>
public sealed class CommandEnabledTests
{
    private sealed class Switch
    {
        public bool On { get; set; }

        public int Ran { get; set; }
    }

    private static RibbonDefinition Ribbon(Switch state)
    {
        CommandDefinition Cmd(string id, string name, string code, Func<bool>? enabled = null, Func<string?>? reason = null, IReadOnlyList<CommandDefinition>? items = null) =>
            new(id, name, "Plus", code, () => state.Ran++, EnabledWhen: enabled, DisabledReason: reason, Items: items);

        var ribbon = new RibbonDefinition(
        [
            new RibbonTabDefinition("Home", "H",
            [
                new RibbonGroupDefinition("File", "1",
                [
                    Cmd("always", "Always", "11"),
                    Cmd("maybe", "Maybe", "12", () => state.On, () => "Needs the switch."),
                    Cmd("menu", "Menu", "13", () => state.On, items: [Cmd("menu.a", "Menu item", "A")]),
                ]),
            ]),
        ]);
        ribbon.Validate();
        return ribbon;
    }

    [Fact]
    public void ADefinitionWithoutAConditionIsEnabled()
    {
        var command = new CommandDefinition("x", "X", "Plus", "X1");
        Assert.True(command.IsEnabled);
        Assert.Null(command.CurrentDisabledReason);
    }

    [Fact]
    public void TheConditionAndTheReasonAreAskedWhenNeeded()
    {
        var state = new Switch();
        var maybe = Ribbon(state).Tabs[0].Groups[0].Commands[1];
        Assert.False(maybe.IsEnabled);
        Assert.Equal("Needs the switch.", maybe.CurrentDisabledReason);
        state.On = true;
        Assert.True(maybe.IsEnabled);
        Assert.Null(maybe.CurrentDisabledReason);
    }

    [Fact]
    public void ADisabledCommandIsSkippedByTheFieldCodes()
    {
        var state = new Switch();
        var session = new FieldCodeSession(Ribbon(state));
        var run = new List<string>();
        session.CommandRun += c => run.Add(c.Id);
        session.F10();
        session.Type('H');
        session.Type('1');
        session.Type('2');

        Assert.Empty(run);
        Assert.Equal(0, state.Ran);
        Assert.Equal(FieldCodeStage.CommandCodes, session.Stage);

        // A disabled menu command does not open its menu either.
        session.Type('3');
        Assert.Equal(FieldCodeStage.CommandCodes, session.Stage);
        Assert.Null(session.OpenMenu);

        // The enabled neighbour still runs, and the disabled one runs once it is enabled.
        state.On = true;
        session.Escape();
        session.Type('H');
        session.Type('1');
        session.Type('2');
        Assert.Equal(["maybe"], run);
        Assert.Equal(1, state.Ran);
    }

    [AvaloniaFact]
    public void ADisabledRibbonButtonIsDimmedDoesNotRunAndNamesTheReason()
    {
        var state = new Switch();
        var ribbon = new RibbonView { Definition = Ribbon(state) };
        var frame = new AppFrame { TabRow = ribbon.TabRow, Band = ribbon.Band };
        var window = ControlHarness.Show(frame, ThemeMode.Light);
        ControlHarness.Settle(window);
        var invoked = new List<string>();
        ribbon.CommandInvoked += c => invoked.Add(c.Id);

        var maybe = RibbonViewTests.ButtonFor(ribbon, "maybe");
        Assert.False(maybe.IsEnabled);
        Assert.True(RibbonViewTests.ButtonFor(ribbon, "always").IsEnabled);
        Assert.True(ToolTip.GetShowOnDisabled(maybe));
        var tip = Assert.IsType<TooltipContent>(ToolTip.GetTip(maybe));
        Assert.Equal("Needs the switch.", tip.Description);

        maybe.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(invoked);
        Assert.Equal(0, state.Ran);

        state.On = true;
        ribbon.RefreshCommandStates();
        Assert.True(maybe.IsEnabled);
        Assert.Null(((TooltipContent)ToolTip.GetTip(maybe)!).Description);
        maybe.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["maybe"], invoked);
        Assert.Equal(1, state.Ran);

        state.On = false;
        ribbon.RefreshCommandStates();
        Assert.False(maybe.IsEnabled);
        Assert.False(RibbonViewTests.ButtonFor(ribbon, "menu").IsEnabled);
    }

    [AvaloniaFact]
    public void ADisabledCommandIsShownDimmedInTheSearchAndNeverChosen()
    {
        var state = new Switch();
        var search = new SearchField { Source = Ribbon(state), Width = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var chosen = new List<CommandDefinition>();
        search.CommandChosen += chosen.Add;
        var window = ControlHarness.Show(search, ThemeMode.Light);

        search.FocusAndSelect();
        window.KeyTextInput("may");
        ControlHarness.Settle(window);
        var row = ControlHarness.Descendants(search).OfType<SearchResultRow>().Single();
        Assert.Equal("Maybe", row.Label);
        Assert.False(row.IsEnabled);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        ControlHarness.Settle(window);
        Assert.Empty(chosen);

        state.On = true;
        search.FocusAndSelect();
        window.KeyTextInput("mayb");
        ControlHarness.Settle(window);
        Assert.True(ControlHarness.Descendants(search).OfType<SearchResultRow>().Single().IsEnabled);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        ControlHarness.Settle(window);
        Assert.Equal(["maybe"], chosen.Select(c => c.Id));
    }
}
