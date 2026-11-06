using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>The keys of the mode, raised on the focused element of the real window: Enter writes, Esc drops, Tab writes and leaves, and no key is taken that another control needs.</summary>
public sealed class TextEditKeysTests
{
    // The way the window delivers a key to the page view: raised on it (tunnel, then bubble, so its own scrolling runs too); returns whether it was handled.
    private static bool PressInView(TextEditRig rig, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = rig.View };
        rig.View.RaiseEvent(e);
        ViewFixture.Pump();
        return e.Handled;
    }

    private static void KeyUp(Avalonia.Interactivity.Interactive source, Key key)
    {
        source.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = source });
        ViewFixture.Pump();
    }

    // Whether the mode takes the key (the keys it leaves are handled by the focus navigation and the scrolling of the page, which a routed press would show as handled).
    private static bool Used(TextEditRig rig, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        rig.Interaction.HandleKey(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = rig.View });

    private static async Task<RunFieldEditor> OpenWithEnterAsync(TextEditRig rig, string line)
    {
        var run = await rig.RunAsync(line);
        rig.View.Focus();
        rig.Session.Focus(run);
        ViewFixture.Pump();
        Assert.True(PressInView(rig, Key.Enter), "Enter on the focused line opens the field");
        await ViewFixture.WaitAsync(() => rig.Interaction.Field is not null && rig.FieldHasFocus);

        // The key goes up while the field that Enter opened has the focus: the page view still sees it (the field is its child), so the next Enter is a new press.
        KeyUp(rig.Interaction.Field!.Field, Key.Enter);
        return rig.Interaction.Field!;
    }

    [AvaloniaFact]
    public async Task EnterCommitsEscDiscardsTabLeavesAndNeverTraps()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();

        // Enter in the field writes the line, the field closes, the page view has the keyboard again, the line keeps its focus.
        var field = await OpenWithEnterAsync(rig, "Quarterly report 2026");
        TextEditRig.Type(field, "Quarterly report 2027");
        rig.Press(PhysicalKey.Enter);
        await rig.Interaction.Completion;
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2027", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.Null(rig.Interaction.Field);
        await ViewFixture.WaitAsync(() => rig.View.IsFocused);
        Assert.NotNull(rig.Session.Focused);

        // Esc in the field drops the draft: nothing is written, the tab is clean again, and the mode stays on.
        await ViewFixture.WaitAsync(() => rig.Session.IsLoaded(0));
        field = await OpenWithEnterAsync(rig, "Dear customer, thank you");
        TextEditRig.Type(field, "Dear customer, thanks");
        Assert.True(rig.Tab.Annotations.HasUnresolved);
        rig.Press(PhysicalKey.Escape);
        Assert.False(rig.Session.HasDraft);
        Assert.Null(rig.Interaction.Field);
        Assert.False(rig.Tab.Annotations.HasUnresolved);
        Assert.Contains("Dear customer, thank you", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.True(rig.Session.IsActive);
        await ViewFixture.WaitAsync(() => rig.View.IsFocused);

        // Tab in the field writes the line and leaves it for the page: the focus is never trapped in the field.
        field = await OpenWithEnterAsync(rig, "Dear customer, thank you");
        TextEditRig.Type(field, "Dear customer, many thanks");
        rig.Press(PhysicalKey.Tab);
        await rig.Interaction.Completion;
        await rig.SettleAsync();
        Assert.Contains("Dear customer, many thanks", await rig.PageTextAsync(), StringComparison.Ordinal);
        await ViewFixture.WaitAsync(() => rig.View.IsFocused);
        Assert.Null(rig.Interaction.Field);

        // A text that is refused: Tab still leaves the field (the draft stays parked with its reason), and Esc on the page ends it.
        await ViewFixture.WaitAsync(() => rig.Session.IsLoaded(0));
        field = await OpenWithEnterAsync(rig, "Quarterly report 2027");
        TextEditRig.Type(field, "Quarterly → 2027");
        rig.Press(PhysicalKey.Tab);
        await rig.Interaction.Completion;
        await ViewFixture.WaitAsync(() => rig.View.IsFocused);
        Assert.True(rig.Session.HasDraft);
        Assert.Equal(TextEditWording.Rejection(Raffica.Core.TextEdit.TextEditRejection.UnsupportedCharacters, ["→"]), rig.Session.Note);
        Assert.True(PressInView(rig, Key.Escape));
        Assert.False(rig.Session.HasDraft);
        Assert.Contains("Quarterly report 2027", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.True(rig.Session.IsActive);

        // With no draft Esc asks to leave the mode, and the mode ends.
        Assert.True(PressInView(rig, Key.Escape));
        await ViewFixture.WaitAsync(() => !rig.Session.IsActive);
        Assert.Null(rig.Window.TextEditOf(rig.Tab));
    }

    [AvaloniaFact]
    public async Task TabAndArrowsWalkTheLinesAndTheWalkEndsWithoutTrapping()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        rig.View.Focus();
        rig.Session.Focus(null);
        var order = rig.Session.RunsOf(0).OrderBy(r => r.Bounds.Top).ToList();
        Assert.Equal(2, order.Count);

        Assert.True(PressInView(rig, Key.Tab));
        Assert.Equal(order[0].ObjectIndex, rig.Session.Focused!.ObjectIndex);
        Assert.True(PressInView(rig, Key.Tab));
        Assert.Equal(order[1].ObjectIndex, rig.Session.Focused!.ObjectIndex);

        // Past the last line the key is not used by the mode, so it goes on to the focus navigation: the focus leaves the page instead of being trapped.
        Assert.False(Used(rig, Key.Tab));
        Assert.Equal(order[1].ObjectIndex, rig.Session.Focused!.ObjectIndex);
        Assert.True(PressInView(rig, Key.Tab, KeyModifiers.Shift));
        Assert.Equal(order[0].ObjectIndex, rig.Session.Focused!.ObjectIndex);
        Assert.False(Used(rig, Key.Tab, KeyModifiers.Shift));

        // The arrows move from line to line too; at the ends they are left to the scrolling of the page.
        Assert.True(PressInView(rig, Key.Down));
        Assert.Equal(order[1].ObjectIndex, rig.Session.Focused!.ObjectIndex);
        Assert.True(PressInView(rig, Key.Up));
        Assert.Equal(order[0].ObjectIndex, rig.Session.Focused!.ObjectIndex);
        Assert.True(PressInView(rig, Key.Right));
        Assert.Equal(order[1].ObjectIndex, rig.Session.Focused!.ObjectIndex);
        Assert.True(PressInView(rig, Key.Left));
        Assert.Equal(order[0].ObjectIndex, rig.Session.Focused!.ObjectIndex);

        // Enter opens the field on the focused line, and a held key opens it once.
        Assert.True(PressInView(rig, Key.Enter));
        Assert.True(PressInView(rig, Key.Enter));
        await ViewFixture.WaitAsync(() => rig.Interaction.Field is not null);
        Assert.Equal(order[0].Text, rig.Session.DraftText);
    }

    [AvaloniaFact]
    public async Task KeysOtherControlsNeedAreNotTaken()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        rig.View.Focus();

        // The window-wide keys, the page keys and the editing keys are not the mode's: the interaction takes none, so they reach their owners.
        bool Taken(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            rig.Interaction.HandleKey(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = rig.View });

        Assert.False(Taken(Key.S, KeyModifiers.Control));
        Assert.False(Taken(Key.F6));
        Assert.False(Taken(Key.K, KeyModifiers.Control));
        Assert.False(Taken(Key.Z, KeyModifiers.Control));
        Assert.False(Taken(Key.A));
        Assert.False(Taken(Key.Delete));
        Assert.False(Taken(Key.PageDown));
        Assert.False(Taken(Key.Enter, KeyModifiers.Control));
        Assert.False(Taken(Key.Enter), "no focused line: Enter is left alone");
        Assert.False(Taken(Key.Right, KeyModifiers.Alt), "Alt combinations stay with their owners");
        Assert.False(Taken(Key.Right, KeyModifiers.Control | KeyModifiers.Alt), "AltGr combinations stay with their owners");
        Assert.False(Taken(Key.Escape, KeyModifiers.Shift));
        Assert.False(Taken(Key.Tab, KeyModifiers.Control));

        // In the field the same holds: the editing keys stay with the TextBox, and only Enter, Esc and Tab are taken.
        var field = await OpenWithEnterAsync2(rig);
        // The TextBox uses these keys itself (undo, select all, the caret, delete); none of them writes, drops or leaves the draft.
        foreach (var (key, modifiers) in new[] { (Key.Z, KeyModifiers.Control), (Key.A, KeyModifiers.Control), (Key.Left, KeyModifiers.None), (Key.Back, KeyModifiers.None), (Key.Q, KeyModifiers.Control | KeyModifiers.Alt) })
        {
            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = field.Field };
            field.Field.RaiseEvent(e);
            ViewFixture.Pump();
            Assert.True(rig.Session.HasDraft, $"{modifiers}+{key} in the field");
            Assert.Same(field, rig.Interaction.Field);
        }

        // An AltGr combination (Ctrl+Alt) is nobody's but the TextBox's: it is not handled before it gets there.
        var altGr = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Q, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt, Source = field.Field };
        field.Field.RaiseEvent(altGr);
        Assert.False(altGr.Handled);
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);
        Assert.True(rig.Session.HasDraft);
    }

    [AvaloniaFact]
    public async Task EnterBelongsToAnOpenImeCompositionAndAnImeProcessedKey()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var field = await OpenWithEnterAsync2(rig);
        TextEditRig.Type(field, "Quarterly report 2027");

        // A key the IME processed is none of ours.
        var processed = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.ImeProcessed, Source = field.Field };
        field.Field.RaiseEvent(processed);
        ViewFixture.Pump();
        Assert.True(rig.Session.HasDraft);
        Assert.False(processed.Handled);

        // While a composition is open (a pre-edit text) Enter commits the composition, not the line.
        var presenter = field.Field.GetVisualDescendants().OfType<TextPresenter>().First();
        presenter.PreeditText = "あ";
        var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = field.Field };
        field.Field.RaiseEvent(enter);
        KeyUp(field.Field, Key.Enter);
        ViewFixture.Pump();
        Assert.False(enter.Handled);
        Assert.True(rig.Session.HasDraft);
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);

        // Without a composition the same Enter writes the line.
        presenter.PreeditText = null;
        var again = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = field.Field };
        field.Field.RaiseEvent(again);
        Assert.True(again.Handled);
        await rig.Interaction.Completion;
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2027", await rig.PageTextAsync(), StringComparison.Ordinal);
    }

    private static async Task<RunFieldEditor> OpenWithEnterAsync2(TextEditRig rig) => await OpenWithEnterAsync(rig, "Quarterly report 2026");
}
