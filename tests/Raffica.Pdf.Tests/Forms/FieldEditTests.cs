using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Annotations;
using Raffica.Core.Forms;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FieldEditTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public async Task ClickOnATextFieldOpensTheEditorOverIt()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");

        var input = f.Open(name);

        var box = Assert.IsType<TextBox>(input);
        Assert.Equal("Alt", box.Text);
        Assert.Equal(name.DisplayName, AutomationProperties.GetName(box));
        Assert.Same(f.Interaction.Editor, f.View.Field);
        Assert.Equal(name.Key, f.Parts.Focus.Current);
        var rect = f.View.ToView(0, name.Bounds)!.Value;
        Assert.Equal(rect, f.View.FieldRect);
        Assert.Equal(rect.Width, box.Width, 0.5);
        Assert.Equal(rect.Height, box.Height, 0.5);
        Assert.False(f.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task LeavingTheFieldCommitsOneHistoryStep()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Erika");
        Assert.True(f.Tab.IsDirty);

        f.Leave();
        await f.SettleAsync();

        Assert.False(f.Interaction.IsEditing);
        Assert.Equal("Erika", (await f.FromDocumentAsync("Name")).State.Text);
        Assert.Equal("Erika", f.Field("Name").State.Text);
        Assert.True(f.Tab.Annotations.CanUndo);
        await f.Tab.Annotations.UndoAsync();
        Assert.Equal("Alt", (await f.FromDocumentAsync("Name")).State.Text);
        Assert.False(f.Tab.Annotations.CanUndo);
    }

    [AvaloniaFact]
    public async Task EnterCommitsAndMovesOnInASingleLineField()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");
        var box = (TextBox)f.Open(name);
        FormEditFixture.SetText(box, "Erika");

        Assert.True(FormEditFixture.Press(box, Key.Enter));
        await f.SettleAsync();
        await ViewFixture.WaitAsync(() => f.Parts.Focus.Current != name.Key);

        Assert.False(f.Interaction.IsEditing);
        Assert.Equal("Erika", (await f.FromDocumentAsync("Name")).State.Text);
        Assert.Equal(FormNavigation.Next(f.Forms.FieldsOn(0), name.Key, forward: true), f.Parts.Focus.Current);
    }

    [AvaloniaFact]
    public async Task EnterIsALineBreakAndCtrlEnterCommitsInAMultilineField()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Bemerkung"));

        // A plain Enter is no commit: the text box makes the line break and the editor stays open with nothing written.
        FormEditFixture.Press(box, Key.Enter);
        Assert.True(f.Interaction.IsEditing);
        Assert.False(f.Tab.Annotations.CanUndo);
        Assert.Contains("\n", box.Text, StringComparison.Ordinal);
        FormEditFixture.SetText(box, "Erste Zeile\nZweite Zeile");

        Assert.True(FormEditFixture.Press(box, Key.Enter, KeyModifiers.Control));
        await f.SettleAsync();

        Assert.False(f.Interaction.IsEditing);
        var written = (await f.FromDocumentAsync("Bemerkung")).State.Text;
        Assert.Equal("Erste Zeile\nZweite Zeile", FormFieldEditor.Normalize(written!));
    }

    [AvaloniaFact]
    public async Task EscDiscardsTheDraft()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Verworfen");
        Assert.True(f.Tab.IsDirty);

        Assert.True(FormEditFixture.Press(box, Key.Escape));
        await f.SettleAsync();

        Assert.False(f.Interaction.IsEditing);
        Assert.Null(f.View.Field);
        Assert.False(f.Tab.IsDirty);
        Assert.False(f.Tab.Annotations.CanUndo);
        Assert.Equal("Alt", (await f.FromDocumentAsync("Name")).State.Text);
    }

    [AvaloniaFact]
    public async Task RefusedTextStaysInTheEditorAndKeepsTheTabDirty()
    {
        using var f = await FormEditFixture.OpenAsync();

        // U+4E2D is outside the character set of the form fields; a line break does not fit a single-line field; "Kurz" takes five characters.
        foreach (var (field, text, named) in new[] { ("Name", "中", "中"), ("Name", "zwei\nZeilen", null), ("Kurz", "123456", "5") })
        {
            var box = (TextBox)f.Open(f.Field(field));
            FormEditFixture.SetText(box, text);
            f.Leave();
            await f.SettleAsync();

            Assert.True(f.Interaction.IsEditing, text);
            Assert.NotEmpty(f.Interaction.Editor!.Hint);
            if (named is not null)
            {
                Assert.Contains(named, f.Interaction.Editor.Hint, StringComparison.Ordinal);
            }

            Assert.True(f.Tab.Annotations.HasUnresolved);
            Assert.True(f.Tab.IsDirty);
            Assert.False(f.Tab.Annotations.CanUndo);
            Assert.NotEqual(text, (await f.FromDocumentAsync(field)).State.Text);

            // Esc ends the draft: nothing was written and the tab is clean again.
            Assert.True(FormEditFixture.Press(box, Key.Escape));
            Assert.False(f.Interaction.IsEditing);
            Assert.False(f.Tab.Annotations.HasUnresolved);
            Assert.False(f.Tab.IsDirty);
        }

        // The euro sign is in the set: it is written.
        var euro = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(euro, "5 €");
        f.Leave();
        await f.SettleAsync();
        Assert.False(f.Interaction.IsEditing);
        Assert.Equal("5 €", (await f.FromDocumentAsync("Name")).State.Text);

        // A refused text that is fixed is written and the unresolved handle is gone.
        var again = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(again, "中");
        f.Leave();
        Assert.True(f.Interaction.IsEditing);
        FormEditFixture.SetText(again, "Fixed");
        Assert.Equal(string.Empty, f.Interaction.Editor!.Hint);
        f.Leave();
        await f.SettleAsync();
        Assert.False(f.Interaction.IsEditing);
        Assert.False(f.Tab.Annotations.HasUnresolved);
        Assert.Equal("Fixed", (await f.FromDocumentAsync("Name")).State.Text);
    }

    [AvaloniaFact]
    public async Task CheckBoxAndRadioToggleWithOneStepEach()
    {
        using var f = await FormEditFixture.OpenAsync();
        var agb = f.Field("Agb");

        f.Click(agb);
        await f.SettleAsync();
        Assert.True((await f.FromDocumentAsync("Agb")).State.Checked);
        Assert.False(f.Interaction.IsEditing);
        Assert.Equal(agb.Key, f.Parts.Focus.Current);

        f.Click(agb);
        await f.SettleAsync();
        Assert.False((await f.FromDocumentAsync("Agb")).State.Checked);

        // Two steps: one undo brings the check back, the second the box as it was.
        await f.Tab.Annotations.UndoAsync();
        Assert.True((await f.FromDocumentAsync("Agb")).State.Checked);
        await f.Tab.Annotations.UndoAsync();
        Assert.False((await f.FromDocumentAsync("Agb")).State.Checked);
        Assert.False(f.Tab.Annotations.CanUndo);

        var first = f.Field("Zahlung", 0);
        var second = f.Field("Zahlung", 1);
        f.Click(first);
        await f.SettleAsync();
        Assert.Equal(first.ExportValue, (await f.FromDocumentAsync("Zahlung")).State.Text);
        f.Click(second);
        await f.SettleAsync();
        Assert.Equal(second.ExportValue, (await f.FromDocumentAsync("Zahlung")).State.Text);

        // One step per choice: the undo goes back to the first choice, not to none.
        await f.Tab.Annotations.UndoAsync();
        Assert.Equal(first.ExportValue, (await f.FromDocumentAsync("Zahlung")).State.Text);
    }

    [AvaloniaFact]
    public async Task RadioGroupCannotGoBackToNoChoice()
    {
        using var f = await FormEditFixture.OpenAsync();
        var first = f.Field("Zahlung", 0);
        f.Click(first);
        await f.SettleAsync();
        Assert.Equal(first.ExportValue, (await f.FromDocumentAsync("Zahlung")).State.Text);

        // Core: the engine never undoes a choice, so undoing the first choice fails and the step is forgotten.
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Tab.Annotations.UndoAsync());
        Assert.False(f.Tab.Annotations.CanUndo);
        Assert.Equal(first.ExportValue, (await f.FromDocumentAsync("Zahlung")).State.Text);

        // The window runs the same undo from the keyboard: it swallows the failure, shows no card and the choice stays.
        var second = f.Field("Stufe", 0);
        f.Click(second);
        await f.SettleAsync();
        f.View.Focus();
        f.PressKey(Avalonia.Input.PhysicalKey.Z, Avalonia.Input.RawInputModifiers.Control);
        await f.SettleAsync();
        Assert.False(f.Windows.Window.Host.IsOpen);
        Assert.Equal(second.ExportValue, (await f.FromDocumentAsync("Stufe")).State.Text);
        Assert.False(f.Tab.Annotations.CanUndo);
    }

    // 07-UI-02: the shell's ComboBox theme had no text box, so an editable combo box took no typing. The text goes in as key events.
    [AvaloniaFact]
    public async Task EditableComboTakesTypedKeysCommitsThemAndShowsAnExistingValue()
    {
        using var f = await FormEditFixture.OpenAsync();

        var combo = Assert.IsType<ComboBox>(f.Open(f.Field("Frei")));
        ViewFixture.Pump();
        var box = combo.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_EditableTextBox");
        Assert.True(box.IsVisible);
        Assert.True(box.IsFocused, "focused: " + f.Window.FocusManager?.GetFocusedElement());

        f.Window.KeyTextInput("Other value");
        ViewFixture.Pump();
        Assert.Equal("Other value", box.Text);
        Assert.Equal("Other value", combo.Text);

        f.PressKey(PhysicalKey.Enter, RawInputModifiers.None);
        await f.SettleAsync();
        Assert.Equal("Other value", (await f.FromDocumentAsync("Frei")).State.Text);

        // The editor opens again over the custom value: it is shown in the text box, not lost.
        var again = Assert.IsType<ComboBox>(f.Open(f.Field("Frei")));
        ViewFixture.Pump();
        var againBox = again.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_EditableTextBox");
        Assert.True(againBox.IsVisible);
        Assert.Equal("Other value", againBox.Text);
    }

    [AvaloniaFact]
    public async Task ComboAndListCommitTheChoice()
    {
        using var f = await FormEditFixture.OpenAsync();

        var combo = Assert.IsType<ComboBox>(f.Open(f.Field("Farbe")));
        combo.SelectedIndex = 1;
        ViewFixture.Pump();
        await f.SettleAsync();
        Assert.Equal([1], (await f.FromDocumentAsync("Farbe")).State.SelectedOptions);
        Assert.False(f.Interaction.IsEditing);

        // An editable combo box takes its own text as well; Enter writes it.
        var editable = Assert.IsType<ComboBox>(f.Open(f.Field("Frei")));
        Assert.True(editable.IsEditable);
        editable.Text = "Eigener Wert";
        ViewFixture.Pump();
        Assert.True(FormEditFixture.Press(editable, Key.Enter));
        await f.SettleAsync();
        Assert.Equal("Eigener Wert", (await f.FromDocumentAsync("Frei")).State.Text);

        var list = Assert.IsType<ListBox>(f.Open(f.Field("Liste")));
        list.Selection.Select(2);
        ViewFixture.Pump();
        await f.SettleAsync();
        Assert.Equal([2], (await f.FromDocumentAsync("Liste")).State.SelectedOptions);
    }

    [AvaloniaFact]
    public async Task MultiSelectListKeepsTheSet()
    {
        using var f = await FormEditFixture.OpenAsync();
        var list = Assert.IsType<ListBox>(f.Open(f.Field("Mehrfach")));
        Assert.True(list.SelectionMode.HasFlag(SelectionMode.Multiple));

        list.Selection.Select(0);
        ViewFixture.Pump();
        list.Selection.Select(2);
        ViewFixture.Pump();
        await f.SettleAsync();

        Assert.Equal([0, 2], (await f.FromDocumentAsync("Mehrfach")).State.SelectedOptions.Order());

        // The editor opens again with the set the field has.
        f.Leave();
        await f.SettleAsync();
        var again = Assert.IsType<ListBox>(f.Open(f.Field("Mehrfach")));
        Assert.Equal([0, 2], again.Selection.SelectedIndexes.Order());
    }

    [AvaloniaFact]
    public async Task PasswordFieldIsMasked()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = Assert.IsType<TextBox>(f.Open(f.Field("Passwort")));
        Assert.NotEqual('\0', box.PasswordChar);

        var plain = (TextBox)FormFieldEditorOf(f, "Name").Input;
        Assert.Equal('\0', plain.PasswordChar);
    }

    [AvaloniaFact]
    public async Task LockedFieldDoesNothingOnClick()
    {
        using var f = await FormEditFixture.OpenAsync();
        var locked = f.Field("Gesperrt");
        Assert.NotEqual(FormLock.None, locked.Lock);

        f.Click(locked);
        await f.SettleAsync();

        Assert.False(f.Interaction.IsEditing);
        Assert.Null(f.View.Field);
        Assert.Null(f.Parts.Focus.Current);
        Assert.False(f.Tab.IsDirty);
        Assert.False(f.Tab.Annotations.CanUndo);
        Assert.Equal(locked.State, (await f.FromDocumentAsync("Gesperrt")).State);
    }

    [AvaloniaFact]
    public async Task UndoRestoresTheBeforeValue()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Neu");
        f.Leave();
        await f.SettleAsync();
        Assert.Equal("Neu", (await f.FromDocumentAsync("Name")).State.Text);

        await f.Tab.Annotations.UndoAsync();
        Assert.Equal("Alt", (await f.FromDocumentAsync("Name")).State.Text);

        // The cache follows the undo (the pages are read again after the invalidation).
        await ViewFixture.WaitAsync(() => f.Field("Name").State.Text == "Alt");

        await f.Tab.Annotations.RedoAsync();
        Assert.Equal("Neu", (await f.FromDocumentAsync("Name")).State.Text);
    }

    [AvaloniaFact]
    public async Task ValuesAndAnAnnotationAreOneSave()
    {
        using var f = await FormEditFixture.OpenAsync();
        Assert.NotNull(await AnnotatedFile.HighlightAsync(f.Tab.Annotations));
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Gespeichert");
        f.Leave();
        await f.SettleAsync();
        f.Click(f.Field("Agb"));
        await f.SettleAsync();
        Assert.True(f.Tab.IsDirty);

        f.PressKey(Avalonia.Input.PhysicalKey.S, Avalonia.Input.RawInputModifiers.Control);

        // A mark next to the values does not spare the question about the empty required field; the values and the mark are one save.
        await ViewFixture.WaitAsync(() => f.Window.Host.ActiveDialog is not null);
        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_SaveAnyway").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await ViewFixture.WaitAsync(() => !f.Tab.IsDirty && !f.Window.Saves.IsSaving);
        await f.ReopenAsync();

        Assert.Equal("Gespeichert", f.Field("Name").State.Text);
        Assert.True(f.Field("Agb").State.Checked);
        var marks = await f.Tab.Session.Document!.GetAnnotationsAsync(0, Cancel);
        Assert.Contains(marks, a => a.Kind == AnnotationKind.Highlight);
    }

    [AvaloniaFact]
    public async Task SaveCommitsAnOpenFieldFirst()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Vor dem Speichern");
        Assert.True(f.Tab.IsDirty);

        f.PressKey(Avalonia.Input.PhysicalKey.S, Avalonia.Input.RawInputModifiers.Control);

        // The form has empty required fields: the card asks after the open field was committed (the value is in the file either way).
        await ViewFixture.WaitAsync(() => f.Window.Host.ActiveDialog is not null);
        var card = f.Window.Host.ActiveDialog!;
        card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_SaveAnyway").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await ViewFixture.WaitAsync(() => !f.Tab.IsDirty && !f.Window.Saves.IsSaving);
        Assert.False(f.Interaction.IsEditing);
        await f.ReopenAsync();

        Assert.Equal("Vor dem Speichern", f.Field("Name").State.Text);
    }

    [AvaloniaFact]
    public async Task ToolChangeCommitsAnOpenField()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Werkzeug");

        f.Window.Tools.Choose(AnnotateTool.Select);
        await f.SettleAsync();

        Assert.False(f.Interaction.IsEditing);
        Assert.Equal("Werkzeug", (await f.FromDocumentAsync("Name")).State.Text);
        f.Window.Tools.End();

        // A refused text stays, hidden while the tool is active and shown again when the fields are live again; the tab stays dirty.
        var second = (TextBox)f.Open(f.Field("Kurz"));
        FormEditFixture.SetText(second, "中");
        f.Window.Tools.Choose(AnnotateTool.Rectangle);
        ViewFixture.Pump();
        Assert.True(f.Interaction.IsEditing);
        Assert.False(f.Interaction.Editor!.IsVisible);
        Assert.True(f.Tab.Annotations.HasUnresolved);
        Assert.True(f.Tab.IsDirty);

        f.Window.Tools.End();
        ViewFixture.Pump();
        Assert.True(f.Interaction.Editor.IsVisible);
        Assert.Same(f.Interaction.Editor, f.View.Field);
    }

    [AvaloniaFact]
    public async Task FieldsAreInertWhileAnAnnotateToolIsActive()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.Window.Tools.Choose(AnnotateTool.Select);
        ViewFixture.Pump();

        f.Click(f.Field("Name"));
        f.Click(f.Field("Agb"));
        await f.SettleAsync();

        Assert.False(f.Interaction.IsEditing);
        Assert.Null(f.Parts.Focus.Current);
        Assert.False((await f.FromDocumentAsync("Agb")).State.Checked);
        Assert.False(f.Tab.Annotations.CanUndo);

        f.Window.Tools.End();
        f.Click(f.Field("Agb"));
        await f.SettleAsync();
        Assert.True((await f.FromDocumentAsync("Agb")).State.Checked);
    }

    // The badge hangs over the top middle of the page area (as the annotate badge does): a word under it is not the test's.
    private static bool UnderBadge(FormEditFixture f, Raffica.Core.Text.PageRect box) =>
        f.View.ToView(0, (box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2) is { } spot && f.Window.BadgeHostOf(f.Tab) is { } host && host.IsVisible && host.Bounds.Contains(spot);

    [AvaloniaFact]
    public async Task OutsideAFieldTextSelectionStillWorks()
    {
        using var f = await FormEditFixture.OpenAsync();
        // The badge takes its size from what the form says (the counter appears once the required fields are read): let it settle before a word is picked.
        await ViewFixture.WaitAsync(() => f.Window.BadgeHostOf(f.Tab)?.Forms is not null && f.Forms.Info is not null && f.Forms.RequiredKnown);
        ViewFixture.Pump();
        var text = await f.Tab.Session.EnsurePageTexts()!.GetAsync(0, cancellationToken: Cancel);
        var fields = f.Forms.FieldsOn(0);

        // A word of the page that no widget covers: a double click selects it, as it does in a document without a form (links are the
        // unchanged LinkController of 2.1; no form fixture has a link, so this test does not claim them).
        var letter = text.Chars.Select((c, i) => (c, i)).First(x => text.GetText(x.i, x.i + 1) is { Length: > 0 } glyph && char.IsLetter(glyph[0]) && !fields.Any(w => w.Bounds.Contains((x.c.Box.Left + x.c.Box.Right) / 2, (x.c.Box.Top + x.c.Box.Bottom) / 2)) && !UnderBadge(f, x.c.Box));
        var spot = f.View.ToView(0, (letter.c.Box.Left + letter.c.Box.Right) / 2, (letter.c.Box.Top + letter.c.Box.Bottom) / 2)!.Value;
        var point = f.View.TranslatePoint(spot, f.Window)!.Value;

        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        ViewFixture.Pump();

        Assert.False(f.Interaction.IsEditing);
        Assert.Null(f.Parts.Focus.Current);
        Assert.True(f.Window.TextOf(f.Tab)!.Selection.HasSelection);
    }

    [AvaloniaFact]
    public async Task FillIsRefusedWithAReasonWhenTheDocumentForbidsIt()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");
        var gate = new FormInteraction(f.Tab, f.View, f.Window.Tools, f.Forms, new FormFocus(), new Raffica.Pdf.Dialogs.DialogQueue(f.Window.Host), () => false);
        f.Interaction.Dispose();
        using (gate)
        {
            f.Click(name);

            Assert.False(gate.IsEditing);
            Assert.True(f.Window.Host.IsOpen);
            Assert.Contains(PdfStrings.FormFillForbidden, AllText(f.Window.Host.ActiveDialog), StringComparison.Ordinal);
            Assert.False(f.Tab.IsDirty);
        }
    }

    [AvaloniaFact]
    public async Task AFieldIsLeftAloneWhileAnotherModeLocksTheFields()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");
        var locked = new FormInteraction(f.Tab, f.View, f.Window.Tools, f.Forms, new FormFocus(), new Raffica.Pdf.Dialogs.DialogQueue(f.Window.Host), () => true, () => PdfStrings.ModeRedactReason);
        f.Interaction.Dispose();
        using (locked)
        {
            f.Click(name);

            Assert.False(locked.IsEditing);
            Assert.False(f.Window.Host.IsOpen);
            Assert.False(f.Tab.IsDirty);
        }
    }

    private static string AllText(object? dialog) =>
        dialog is Raffica.Shell.Controls.Dialog { Content: Panel panel } ? string.Join(' ', panel.Children.OfType<TextBlock>().Select(t => t.Text)) : string.Empty;

    private static FormFieldEditor FormFieldEditorOf(FormEditFixture f, string name) => new(f.Field(name));
}
