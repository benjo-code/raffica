using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class FieldCodeSessionTests
{
    private readonly List<string> _run = [];
    private readonly List<string> _tabs = [];
    private readonly Dictionary<string, int> _ran = [];
    private readonly FieldCodeSession _session;

    public FieldCodeSessionTests()
    {
        CommandDefinition Leaf(string code) =>
            new("cmd." + code, "n" + code, "icon", code, Run: () => _ran[code] = _ran.GetValueOrDefault(code) + 1);

        var menu = new CommandDefinition("cmd.21", "Menu", "icon", "21", Items: [Leaf("A"), Leaf("B")]);
        var ribbon = new RibbonDefinition(
        [
            new RibbonTabDefinition("Home", "H",
            [
                new RibbonGroupDefinition("G1", "1", [Leaf("11"), Leaf("12"), Leaf("13")]),
                new RibbonGroupDefinition("G2", "2", [menu]),
            ]),
            new RibbonTabDefinition("Edit", "E", [new RibbonGroupDefinition("G1", "1", [Leaf("11")])]),
        ]);
        ribbon.Validate();
        _session = new FieldCodeSession(ribbon);
        _session.CommandRun += c => _run.Add(c.Id);
        _session.TabSelected += t => _tabs.Add(t.Letter);
    }

    private void EnterMode()
    {
        _session.AltDown(false);
        _session.AltUp();
    }

    [Fact]
    public void BareAltPressAndReleaseTurnsTheModeOn()
    {
        Assert.False(_session.AltDown(false));
        Assert.Equal(FieldCodeStage.Off, _session.Stage);
        Assert.True(_session.AltUp());
        Assert.Equal(FieldCodeStage.TabLetters, _session.Stage);
    }

    [Fact]
    public void F10TurnsTheModeOn()
    {
        Assert.True(_session.F10());
        Assert.Equal(FieldCodeStage.TabLetters, _session.Stage);
    }

    [Fact]
    public void AltWithAnotherKeyInBetweenIsAnOrdinaryShortcut()
    {
        _session.AltDown(false);
        Assert.False(_session.OtherKey());
        Assert.False(_session.AltUp());
        Assert.Equal(FieldCodeStage.Off, _session.Stage);
    }

    [Fact]
    public void AltGrNeverEntersTheMode()
    {
        Assert.False(_session.AltDown(ctrlHeld: true));
        Assert.False(_session.AltUp());
        Assert.Equal(FieldCodeStage.Off, _session.Stage);
    }

    [Fact]
    public void TabLetterShowsTheCommandCodes()
    {
        EnterMode();

        Assert.True(_session.Type('H'));

        Assert.Equal(FieldCodeStage.CommandCodes, _session.Stage);
        Assert.Equal("H", _session.ActiveTab?.Letter);
        Assert.Equal(["H"], _tabs);
    }

    [Fact]
    public void TwoCharacterCodeCompletesOnTheSecondKey()
    {
        EnterMode();
        _session.Type('h');

        Assert.True(_session.Type('1'));
        Assert.Equal(FieldCodeStage.CommandCodes, _session.Stage);
        Assert.Equal("1", _session.Typed);
        Assert.Empty(_run);

        Assert.True(_session.Type('3'));
        Assert.Equal(["cmd.13"], _run);
        Assert.Equal(1, _ran["13"]);
        Assert.Equal(FieldCodeStage.Off, _session.Stage);
    }

    [Fact]
    public void MenuCommandShowsItsItemCodesNext()
    {
        EnterMode();
        _session.Type('H');
        _session.Type('2');
        _session.Type('1');

        Assert.Equal(FieldCodeStage.MenuItems, _session.Stage);
        Assert.Equal("cmd.21", _session.OpenMenu?.Id);
        Assert.Empty(_run);

        _session.Type('B');

        Assert.Equal(["cmd.B"], _run);
        Assert.Equal(FieldCodeStage.Off, _session.Stage);
    }

    [Fact]
    public void UnknownCharacterChangesNothing()
    {
        EnterMode();
        var changes = 0;
        _session.Changed += () => changes++;

        Assert.True(_session.Type('Z'));
        Assert.Equal(FieldCodeStage.TabLetters, _session.Stage);

        _session.Type('H');
        changes = 0;
        Assert.True(_session.Type('9'));
        Assert.Equal(FieldCodeStage.CommandCodes, _session.Stage);
        Assert.Equal("", _session.Typed);
        Assert.Equal(0, changes);
        Assert.False(new FieldCodeSession(new RibbonDefinition([])).Type('H'));
    }

    [Fact]
    public void EscapeGoesBackOneStep()
    {
        EnterMode();
        _session.Type('H');
        _session.Type('2');
        _session.Type('1');
        Assert.Equal(FieldCodeStage.MenuItems, _session.Stage);

        _session.Escape();
        Assert.Equal(FieldCodeStage.CommandCodes, _session.Stage);
        Assert.Null(_session.OpenMenu);

        _session.Escape();
        Assert.Equal(FieldCodeStage.TabLetters, _session.Stage);
        Assert.Null(_session.ActiveTab);

        _session.Escape();
        Assert.Equal(FieldCodeStage.Off, _session.Stage);
    }

    [Fact]
    public void AltAgainEndsTheMode()
    {
        EnterMode();

        _session.AltDown(false);
        _session.AltUp();

        Assert.Equal(FieldCodeStage.Off, _session.Stage);
    }

    [Fact]
    public void CancelEndsTheMode()
    {
        EnterMode();
        _session.Type('H');

        _session.Cancel();

        Assert.Equal(FieldCodeStage.Off, _session.Stage);
        Assert.Null(_session.ActiveTab);
    }

    [Fact]
    public void CodesAreLanguageIndependent()
    {
        // The same codes reach the same commands whatever the display names say.
        var ribbon = new RibbonDefinition(
        [
            new RibbonTabDefinition("Start", "H", [new RibbonGroupDefinition("G", "1", [new CommandDefinition("c", "Fett", "icon", "B")])]),
        ]);
        var session = new FieldCodeSession(ribbon);
        var ran = new List<string>();
        session.CommandRun += c => ran.Add(c.Id);

        session.F10();
        session.Type('H');
        session.Type('B');

        Assert.Equal(["c"], ran);
    }

    [Fact]
    public void CancelForgetsASwallowedAltRelease()
    {
        // Alt ends the mode and its release is swallowed; if the release never arrives (Alt+Tab, focus loss) and the
        // window cancels the session, the next bare Alt press and release must enter the mode again.
        EnterMode();
        Assert.True(_session.AltDown(false));
        Assert.Equal(FieldCodeStage.Off, _session.Stage);

        _session.Cancel();
        _session.AltDown(false);
        Assert.True(_session.AltUp());

        Assert.Equal(FieldCodeStage.TabLetters, _session.Stage);
    }

    [Fact]
    public void AnOtherKeyForgetsASwallowedAltRelease()
    {
        EnterMode();
        _session.AltDown(false);
        _session.OtherKey();

        _session.AltDown(false);
        Assert.True(_session.AltUp());
        Assert.Equal(FieldCodeStage.TabLetters, _session.Stage);
    }
}
