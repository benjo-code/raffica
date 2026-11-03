using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class FieldCodeInputTests
{
    [AvaloniaFact]
    public void BareAltShowsTabLettersAndTheHint()
    {
        var f = FieldCodeFixture.Create();
        Assert.Null(f.Status.Hint);

        f.Alt();

        Assert.Equal(FieldCodeStage.TabLetters, f.Session.Stage);
        Assert.True(f.Ribbon.ShowFieldCodes);
        Assert.Equal(ShellStrings.FieldCodeHint, f.Status.Hint);
        Assert.Equal(["H", "N", "R", "W"], f.BadgeTexts().Where(t => t.Length == 1 && char.IsLetter(t[0]) && t != "Q").Order().ToList());
        Assert.Contains("Q", f.BadgeTexts());
    }

    [AvaloniaFact]
    public void TabLetterThenCodeRunsTheCommand()
    {
        var f = FieldCodeFixture.Create();
        f.Alt();
        f.TypeCode("H");

        Assert.Equal(FieldCodeStage.CommandCodes, f.Session.Stage);
        Assert.Contains("B", f.BadgeTexts());
        Assert.Contains("AL", f.BadgeTexts());
        Assert.Contains("1", f.BadgeTexts());

        f.TypeCode("B");

        Assert.Equal(["bold"], f.Invoked);
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        Assert.False(f.Ribbon.ShowFieldCodes);
        Assert.Null(f.Status.Hint);
        Assert.Empty(f.Badges);
    }

    [AvaloniaFact]
    public void ATabLetterShowsThatTabAndItsCodes()
    {
        var f = FieldCodeFixture.Create();
        f.Alt();
        f.TypeCode("N");

        Assert.Equal("Insert", f.Ribbon.ActiveTab!.Name);
        Assert.Contains("BP", f.BadgeTexts());
        Assert.DoesNotContain("AL", f.BadgeTexts());

        f.TypeCode("PI");
        Assert.Equal(["picture"], f.Invoked);
    }

    [AvaloniaFact]
    public void ACompletedCodeIsNotTypedIntoTheTextBox()
    {
        var f = FieldCodeFixture.Create();
        ControlHarness.KeyboardFocus(f.Window, f.Box);
        f.Alt();
        f.TypeCode("H");
        f.TypeCode("B");

        Assert.Equal(["bold"], f.Invoked);
        Assert.True(string.IsNullOrEmpty(f.Box.Text));

        // The mode is over: ordinary typing reaches the box again.
        f.Type(Key.X, PhysicalKey.X, "x");
        Assert.Equal("x", f.Box.Text);
    }

    [AvaloniaFact]
    public void AltPlusAnotherKeyIsAnOrdinaryShortcut()
    {
        var f = FieldCodeFixture.Create();
        var log = f.WatchKeys();
        var shortcuts = new List<Key>();
        f.Window.KeyDown += (_, e) => shortcuts.Add(e.Key);

        f.AltDown();
        f.Press(Key.F, PhysicalKey.F, RawInputModifiers.Alt);
        f.AltUp();

        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        Assert.False(f.Ribbon.ShowFieldCodes);
        Assert.Contains(Key.F, shortcuts);
        Assert.All(log, entry => Assert.False(entry.Handled, $"{entry.Kind} {entry.Key} must stay unhandled"));
    }

    [AvaloniaFact]
    public void AltGrOnWindowsTypesTheCharacter()
    {
        // German layout: AltGr arrives as LeftCtrl down + RightAlt down, then the character as text input.
        var f = FieldCodeFixture.Create();
        ControlHarness.KeyboardFocus(f.Window, f.Box);
        var log = f.WatchKeys();

        f.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        f.Window.KeyPress(Key.RightAlt, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.AltRight, null);
        f.Window.KeyPress(Key.Q, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.Q, "@");
        f.Window.KeyTextInput("@");
        f.Window.KeyRelease(Key.Q, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.Q, "@");
        f.Window.KeyRelease(Key.RightAlt, RawInputModifiers.Control, PhysicalKey.AltRight, null);
        f.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        ControlHarness.Settle(f.Window);

        Assert.Equal("@", f.Box.Text);
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        Assert.False(f.Ribbon.ShowFieldCodes);
        Assert.Null(f.Status.Hint);
        Assert.All(log, entry => Assert.False(entry.Handled, $"{entry.Kind} {entry.Key} must stay unhandled"));
    }

    [AvaloniaFact]
    public void AltGrPressedAndReleasedAloneDoesNotEnterTheMode()
    {
        var f = FieldCodeFixture.Create();
        var log = f.WatchKeys();

        f.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        f.Window.KeyPress(Key.RightAlt, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.AltRight, null);
        f.Window.KeyRelease(Key.RightAlt, RawInputModifiers.Control, PhysicalKey.AltRight, null);
        f.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        ControlHarness.Settle(f.Window);

        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        Assert.Empty(f.Badges);
        Assert.All(log, entry => Assert.False(entry.Handled));
    }

    [AvaloniaFact]
    public void AltGrOnLinuxSendsNoCtrlButTheCharacterStillCountsAsAnotherKey()
    {
        var f = FieldCodeFixture.Create();
        ControlHarness.KeyboardFocus(f.Window, f.Box);

        f.Window.KeyPress(Key.RightAlt, RawInputModifiers.Alt, PhysicalKey.AltRight, null);
        f.Window.KeyTextInput("€");
        f.Window.KeyRelease(Key.RightAlt, RawInputModifiers.None, PhysicalKey.AltRight, null);
        ControlHarness.Settle(f.Window);

        Assert.Equal("€", f.Box.Text);
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
    }

    [AvaloniaFact]
    public void ARepeatedAltDownAfterTheCharacterDoesNotRearmTheMode()
    {
        var f = FieldCodeFixture.Create();
        ControlHarness.KeyboardFocus(f.Window, f.Box);

        f.Window.KeyPress(Key.RightAlt, RawInputModifiers.Alt, PhysicalKey.AltRight, null);
        f.Window.KeyTextInput("@");
        f.Window.KeyPress(Key.RightAlt, RawInputModifiers.Alt, PhysicalKey.AltRight, null);
        f.Window.KeyRelease(Key.RightAlt, RawInputModifiers.None, PhysicalKey.AltRight, null);
        ControlHarness.Settle(f.Window);

        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
    }

    [AvaloniaFact]
    public void EscStepsBackAndLeavesTheMode()
    {
        var f = FieldCodeFixture.Create();
        var log = f.WatchKeys();
        f.Alt();
        f.TypeCode("H");
        Assert.Equal(FieldCodeStage.CommandCodes, f.Session.Stage);

        f.Press(Key.Escape, PhysicalKey.Escape);
        Assert.Equal(FieldCodeStage.TabLetters, f.Session.Stage);
        Assert.Null(f.Ribbon.FieldCodeTabLetter);
        Assert.True(f.Ribbon.ShowFieldCodes);

        f.Press(Key.Escape, PhysicalKey.Escape);
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        Assert.False(f.Ribbon.ShowFieldCodes);
        Assert.Null(f.Status.Hint);
        Assert.Empty(f.Badges);
        Assert.Contains(log, e => e is { Kind: FieldCodeFixture.RoutedEventKind.Down, Key: Key.Escape, Handled: true });
    }

    [AvaloniaFact]
    public void F10Works()
    {
        var f = FieldCodeFixture.Create();

        f.Press(Key.F10, PhysicalKey.F10);
        Assert.Equal(FieldCodeStage.TabLetters, f.Session.Stage);
        Assert.NotEmpty(f.Badges);

        f.Press(Key.F10, PhysicalKey.F10);
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        Assert.Empty(f.Badges);

        // Shift+F10 is the context-menu key, not a field-code key.
        f.Press(Key.F10, PhysicalKey.F10, RawInputModifiers.Shift);
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
    }

    public enum Ending
    {
        PointerPress,
        Deactivation,
        DialogOpens,
        TabChange,
    }

    [AvaloniaTheory]
    [InlineData(Ending.PointerPress)]
    [InlineData(Ending.Deactivation)]
    [InlineData(Ending.DialogOpens)]
    [InlineData(Ending.TabChange)]
    public void ClickDeactivationAndDialogEndTheMode(Ending ending)
    {
        var f = FieldCodeFixture.Create();
        f.Alt();
        f.TypeCode("H");
        Assert.NotEmpty(f.Badges);

        switch (ending)
        {
            case Ending.PointerPress:
                f.Window.MouseDown(new Avalonia.Point(700, 500), MouseButton.Left);
                break;
            case Ending.Deactivation:
                f.Deactivate();
                break;
            case Ending.DialogOpens:
                f.Host.Show(new Dialog { Title = "Dialog", Content = new Button { Content = "OK" } });
                break;
            case Ending.TabChange:
                f.Ribbon.ActiveTab = f.Definition.Tabs[2];
                break;
        }

        ControlHarness.Settle(f.Window);

        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        Assert.False(f.Ribbon.ShowFieldCodes);
        Assert.Null(f.Status.Hint);
        Assert.Empty(f.Badges);
    }

    [AvaloniaFact]
    public void AltDoesNotEnterTheModeWhileADialogIsOpen()
    {
        var f = FieldCodeFixture.Create();
        f.Host.Show(new Dialog { Title = "Dialog", Content = new Button { Content = "OK" } });
        ControlHarness.Settle(f.Window);

        f.Alt();
        f.Press(Key.F10, PhysicalKey.F10);

        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        Assert.Empty(f.Badges);
    }

    [AvaloniaFact]
    public void AltKeyEventsAreHandled()
    {
        var f = FieldCodeFixture.Create();
        var log = f.WatchKeys();

        // A bare Alt: the press is still undecided and passes; the release enters the mode and is consumed (no system menu).
        f.Alt();
        Assert.Equal([(FieldCodeFixture.RoutedEventKind.Down, Key.LeftAlt, false), (FieldCodeFixture.RoutedEventKind.Up, Key.LeftAlt, true)], log);

        // Alt again ends the mode: now both the press and its release are consumed.
        log.Clear();
        f.Alt();
        Assert.Equal([(FieldCodeFixture.RoutedEventKind.Down, Key.LeftAlt, true), (FieldCodeFixture.RoutedEventKind.Up, Key.LeftAlt, true)], log);
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
    }

    [AvaloniaFact]
    public void ARepeatedAltDownWhileHeldIsTreatedLikeTheFirstOne()
    {
        var f = FieldCodeFixture.Create();
        f.Alt();
        var log = f.WatchKeys();

        f.AltDown();
        f.AltDown();
        f.AltDown();
        f.AltUp();

        Assert.All(log.Where(e => e.Kind == FieldCodeFixture.RoutedEventKind.Down), e => Assert.True(e.Handled));
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        f.Alt();
        Assert.Equal(FieldCodeStage.TabLetters, f.Session.Stage);
    }

    [AvaloniaFact]
    public void TheNextBareAltEntersAgainAfterTheWindowLostTheFocusMidPress()
    {
        var f = FieldCodeFixture.Create();
        f.Alt();
        Assert.Equal(FieldCodeStage.TabLetters, f.Session.Stage);

        // Alt ends the mode and its release is swallowed. The user Alt+Tabs away, so that release never arrives.
        f.AltDown();
        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        f.DeactivateAndReactivate();

        f.Alt();

        Assert.Equal(FieldCodeStage.TabLetters, f.Session.Stage);
        Assert.True(f.Ribbon.ShowFieldCodes);
    }

    [AvaloniaFact]
    public void TheNextBareAltEntersAgainAfterTheWindowLostTheFocusWhileAltWasHeld()
    {
        var f = FieldCodeFixture.Create();

        // Alt down, then Alt+Tab: the release goes to the other window.
        f.AltDown();
        f.DeactivateAndReactivate();

        f.Alt();

        Assert.Equal(FieldCodeStage.TabLetters, f.Session.Stage);
    }

    [AvaloniaFact]
    public void DetachRestoresNormalKeys()
    {
        var f = FieldCodeFixture.Create();
        f.Alt();
        Assert.NotEmpty(f.Badges);
        var log = f.WatchKeys();

        f.Attachment.Dispose();

        Assert.False(f.Ribbon.ShowFieldCodes);
        Assert.Null(f.Status.Hint);
        Assert.Empty(f.Badges);

        f.Alt();
        f.Press(Key.F10, PhysicalKey.F10);
        f.Press(Key.Escape, PhysicalKey.Escape);

        Assert.Equal(FieldCodeStage.Off, f.Session.Stage);
        Assert.False(f.Ribbon.ShowFieldCodes);
        Assert.NotEmpty(log);
        Assert.All(log, e => Assert.False(e.Handled));
    }
}
