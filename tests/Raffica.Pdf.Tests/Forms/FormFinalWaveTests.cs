using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Forms;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

/// <summary>The final fix wave of milestone 2.3: pasted line breaks, hidden refused drafts, off-screen focus, automation names and small leaks.</summary>
public sealed class FormFinalWaveTests
{
    private static void TypeInto(TextBox box, string text)
    {
        box.Focus();
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        ViewFixture.Pump();
    }

    private static void RunCommand(FormEditFixture f, string id) =>
        f.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id).Run!.Invoke();

    private static void Release(FormEditFixture f, Key key)
    {
        f.View.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = f.View });
        ViewFixture.Pump();
    }

    // Brings a widget into the view at the current zoom.
    private static async Task ShowAsync(FormEditFixture f, FormField field)
    {
        f.View.GoToPage(field.Key.Page);
        ViewFixture.Pump();
        await f.Forms.LoadPageAsync(field.Key.Page);
        f.View.GoToDestination(field.Key.Page, field.Bounds.Top);
        ViewFixture.Pump();
        Assert.True(InView(f, field), "the field is not in the view");
    }

    // In view when it lies inside the view vertically and shows at least its left part horizontally (a zoomed page is wider than the view).
    private static bool InView(FormEditFixture f, FormField field) =>
        f.View.ToView(field.Key.Page, field.Bounds) is { } rect && rect.Top >= 0 && rect.Bottom <= f.View.Bounds.Height
        && rect.Right > 0 && rect.Left < f.View.Bounds.Width;

    // The Name field holds a refused text (an unsupported character) in an editor that is parked, then its page scrolls away: the editor is
    // hidden and the draft stays (unresolved), and a field of the second page is in view.
    private static async Task<FormField> HiddenRefusedDraftAsync(FormEditFixture f)
    {
        f.View.SetZoom(3);
        ViewFixture.Pump();
        var name = f.Field("Name");
        await ShowAsync(f, name);
        var box = (TextBox)f.Open(name);
        FormEditFixture.SetText(box, "中");
        f.Leave();
        Assert.True(f.Tab.Annotations.HasUnresolved);

        var other = await f.OtherPageFieldAsync("Telefon");
        await ShowAsync(f, other);
        Assert.False(f.Interaction.Editor!.IsVisible, "the editor should be hidden while its field is out of sight");
        Assert.False(InView(f, name));
        return other;
    }

    [AvaloniaFact]
    public async Task APastedLineBreakInASingleLineFieldIsRefusedAsAWhole()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, string.Empty);

        // The text arrives as the keyboard or the paste command sends it: through the text input of the control.
        TypeInto(box, "Street 1\n12345 City");

        Assert.Equal("Street 1\n12345 City", box.Text);
        Assert.Equal(PdfStrings.FormOneLineOnly, f.Interaction.Editor!.Hint);
        Assert.True(f.Tab.IsDirty);
        f.Leave();
        await f.SettleAsync();
        Assert.True(f.Interaction.IsEditing);
        Assert.True(f.Tab.Annotations.HasUnresolved);
        Assert.Equal("Alt", (await f.FromDocumentAsync("Name")).State.Text);
    }

    [AvaloniaFact]
    public async Task ATabInPastedTextIsNeitherChangedNorDropped()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, string.Empty);

        TypeInto(box, "a\tb");

        Assert.Equal("a\tb", box.Text);
        Assert.NotEmpty(f.Interaction.Editor!.Hint);
    }

    [AvaloniaFact]
    public async Task ShiftEnterInASingleLineFieldCommitsAndNeverInsertsABreak()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");
        var box = (TextBox)f.Open(name);
        FormEditFixture.SetText(box, "Neu");

        Assert.True(FormEditFixture.Press(box, Key.Enter, KeyModifiers.Shift));
        await f.SettleAsync();

        Assert.False(f.Interaction.IsEditing);
        Assert.Equal("Neu", (await f.FromDocumentAsync("Name")).State.Text);

        // In a multi-line field Shift+Enter is a line break like Enter.
        var multi = (TextBox)f.Open(f.Field("Bemerkung"));
        FormEditFixture.Press(multi, Key.Enter, KeyModifiers.Shift);
        Assert.True(f.Interaction.IsEditing);
    }

    [AvaloniaFact]
    public async Task ClickOnAnotherFieldRevealsAHiddenRefusedDraftInsteadOfSwallowingTheClick()
    {
        using var f = await FormEditFixture.OpenAsync();
        var other = await HiddenRefusedDraftAsync(f);

        f.Click(other);

        var editor = f.Interaction.Editor!;
        Assert.Equal("Name", f.Interaction.EditedField!.Key.FieldName);
        Assert.True(editor.IsVisible);
        Assert.Same(editor, f.View.Field);
        Assert.NotEmpty(editor.Hint);
        Assert.True(InView(f, f.Field("Name")));
        Assert.True(f.Tab.Annotations.HasUnresolved);
    }

    [AvaloniaFact]
    public async Task TabFromTheViewRevealsAHiddenRefusedDraft()
    {
        using var f = await FormEditFixture.OpenAsync();
        await HiddenRefusedDraftAsync(f);
        f.View.Focus();

        FormEditFixture.Press(f.View, Key.Tab);

        var editor = f.Interaction.Editor!;
        Assert.True(f.Parts.Keys.TookLastKey);
        Assert.True(editor.IsVisible);
        Assert.Same(editor, f.View.Field);
        Assert.NotEmpty(editor.Hint);
    }

    [AvaloniaFact]
    public async Task EnterAndSpaceFromTheViewRevealAHiddenRefusedDraft()
    {
        using var f = await FormEditFixture.OpenAsync();
        await HiddenRefusedDraftAsync(f);
        f.View.Focus();

        FormEditFixture.Press(f.View, Key.Space);

        Assert.True(f.Parts.Keys.TookLastKey);
        Assert.True(f.Interaction.Editor!.IsVisible);
    }

    [AvaloniaFact]
    public async Task TheFormCommandsRevealAHiddenRefusedDraftAndDoNotMove()
    {
        using var f = await FormEditFixture.OpenAsync();
        await HiddenRefusedDraftAsync(f);
        var before = f.Parts.Focus.Current;

        RunCommand(f, PdfCommandIds.FormsNext);
        await ViewFixture.WaitAsync(() => f.Interaction.Editor!.IsVisible);
        Assert.Equal(before, f.Parts.Focus.Current);
        Assert.True(f.Tab.Annotations.HasUnresolved);

        // The same for the required walk and for "Go to first empty field" of the card.
        f.Interaction.Editor!.IsVisible = false;
        RunCommand(f, PdfCommandIds.FormsNextRequired);
        await ViewFixture.WaitAsync(() => f.Interaction.Editor!.IsVisible);
        Assert.Equal(before, f.Parts.Focus.Current);

        f.Interaction.Editor!.IsVisible = false;
        f.Window.GoToFirstRequired(f.Tab);
        await ViewFixture.WaitAsync(() => f.Interaction.Editor!.IsVisible);
        Assert.Equal(before, f.Parts.Focus.Current);
    }

    [AvaloniaFact]
    public async Task AHiddenRefusedDraftIsShownAgainWhenTheSlotOfTheViewFreesUp()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");
        var box = (TextBox)f.Open(name);
        FormEditFixture.SetText(box, "中");
        f.Leave();
        var draft = f.Interaction.Editor!;

        // The text box tool takes the slot with a text it refuses itself and keeps its field when the tool ends.
        f.Window.Tools.Choose(AnnotateTool.TextBox);
        ViewFixture.Pump();
        var point = f.View.TranslatePoint(f.View.ToView(0, 30, 620)!.Value, f.Window)!.Value;
        f.ClickAt(point);
        var textBox = f.Window.AnnotateOf(f.Tab)!.TextBox;
        Assert.True(textBox.IsEditing);
        textBox.Editor!.Field.Text = "中";
        ViewFixture.Pump();
        f.Window.Tools.End();
        ViewFixture.Pump();
        Assert.True(textBox.IsEditing, "the text box field should stay with its refused text");
        Assert.False(draft.IsVisible && ReferenceEquals(f.View.Field, draft));

        // Esc in the text box field frees the slot: the form draft comes back.
        FormEditFixture.Press(textBox.Editor!.Field, Key.Escape);
        Assert.False(textBox.IsEditing);
        Assert.True(draft.IsVisible);
        Assert.Same(draft, f.View.Field);

        // It is laid out over its widget (not at zero width in the corner, whatever the order of the text box field's clean-up was) and the input
        // has the keyboard focus once the dispatcher settles.
        ViewFixture.Pump();
        var rect = f.View.ToView(0, name.Bounds)!.Value;
        Assert.Equal(rect, f.View.FieldRect);
        Assert.InRange(draft.Input.Bounds.Width, rect.Width - 1, rect.Width + 1);
        Assert.InRange(draft.Input.Bounds.Height, rect.Height - 1, rect.Height + 1);
        Assert.True(rect.Width > 0 && rect.Height > 0);
        Assert.True(draft.Input.IsKeyboardFocusWithin, "the re-shown editor has no focus");
    }

    private static readonly Raffica.Core.Annotations.AnnotationColor Red = new(220, 38, 38);

    [AvaloniaFact]
    public async Task UndoIsNotSwallowedByAHiddenRefusedDraftWhileAToolIsActive()
    {
        using var f = await FormEditFixture.OpenAsync();
        var mark = await f.Tab.Annotations.Editor.ApplyDrawingAsync(
            0, Raffica.Core.Annotations.DrawingGeometry.Rectangle(new Raffica.Core.Text.PageRect(100, 560, 200, 600), 2), Red, null, null);
        Assert.NotNull(mark);
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "中");
        f.Leave();
        f.Window.Tools.Choose(AnnotateTool.Rectangle);
        ViewFixture.Pump();
        Assert.True(f.Interaction.HasHiddenDraft);
        Assert.True(f.Tab.Annotations.HasUnresolved);
        Assert.True(f.Tab.Annotations.CanUndo);

        // Nothing can be shown while a tool is active, so the draft must not hold the undo: the history step is undone, the draft ends.
        await f.Tab.Annotations.UndoAsync();
        ViewFixture.Pump();

        Assert.False(f.Tab.Annotations.CanUndo);
        Assert.False(f.Interaction.IsEditing);
        Assert.False(f.Tab.Annotations.HasUnresolved);
        var marks = await f.Tab.Session.Document!.GetAnnotationsAsync(0, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(marks, a => a.Kind == Raffica.Core.Annotations.AnnotationKind.Rectangle);
    }

    [AvaloniaFact]
    public async Task TheFormCommandsDoNotScrollForAHiddenDraftWhileAToolIsActive()
    {
        using var f = await FormEditFixture.OpenAsync();
        await HiddenRefusedDraftAsync(f);
        f.Window.Tools.Choose(AnnotateTool.Rectangle);
        ViewFixture.Pump();
        var offset = f.View.Offset;
        var before = f.Parts.Focus.Current;

        RunCommand(f, PdfCommandIds.FormsNext);
        ViewFixture.Pump();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();

        // The move ends without a scroll that shows nothing, and without moving the form focus.
        Assert.Equal(offset, f.View.Offset);
        Assert.Equal(before, f.Parts.Focus.Current);
        Assert.True(f.Interaction.IsEditing);
    }

    [AvaloniaFact]
    public async Task UndoWithAHiddenRefusedDraftRevealsItAndDropsNothing()
    {
        using var f = await FormEditFixture.OpenAsync();
        await HiddenRefusedDraftAsync(f);

        await f.Tab.Annotations.UndoAsync();
        ViewFixture.Pump();

        Assert.True(f.Interaction.IsEditing);
        Assert.True(f.Interaction.Editor!.IsVisible);
        Assert.True(f.Tab.Annotations.HasUnresolved);
        Assert.NotEmpty(f.Interaction.Editor.Hint);

        // A draft the user can see is still dropped by the history command, as before.
        await f.Tab.Annotations.UndoAsync();
        Assert.False(f.Interaction.IsEditing);
    }

    [AvaloniaFact]
    public async Task SpaceOnAnOffScreenFocusedCheckBoxScrollsItIntoViewAndDoesNotToggle()
    {
        using var f = await FormEditFixture.OpenAsync();
        var agb = f.Field("Agb");
        f.View.SetZoom(3);
        ViewFixture.Pump();
        await ShowAsync(f, agb);
        f.Parts.Focus.Set(agb.Key);
        f.View.Focus();

        // The wheel moves the page away from the focused widget; the form focus stays.
        f.View.Offset = new Vector(f.View.Offset.X, 0);
        ViewFixture.Pump();
        Assert.False(InView(f, agb));
        var before = f.View.Offset;

        FormEditFixture.Press(f.View, Key.Space);
        await f.SettleAsync();
        Assert.True(f.Parts.Keys.TookLastKey);
        Assert.NotEqual(before, f.View.Offset);
        Assert.True(InView(f, agb));
        Assert.False((await f.FromDocumentAsync("Agb")).State.Checked);

        Release(f, Key.Space);
        FormEditFixture.Press(f.View, Key.Space);
        await f.SettleAsync();
        Assert.True((await f.FromDocumentAsync("Agb")).State.Checked);
    }

    [AvaloniaFact]
    public async Task EnterOnAnOffScreenFocusedTextFieldScrollsFirstAndOpensNoInvisibleEditor()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");
        f.View.SetZoom(3);
        ViewFixture.Pump();
        await ShowAsync(f, name);
        f.Parts.Focus.Set(name.Key);
        f.View.Focus();
        f.View.Offset = new Vector(f.View.Offset.X, f.View.MaxOffsetY);
        ViewFixture.Pump();
        Assert.False(InView(f, name));

        FormEditFixture.Press(f.View, Key.Enter);

        Assert.False(f.Interaction.IsEditing);
        Assert.True(InView(f, name));
        Release(f, Key.Enter);
        FormEditFixture.Press(f.View, Key.Enter);
        Assert.True(f.Interaction.IsEditing);
    }

    [AvaloniaFact]
    public async Task TheAutomationNameSaysRequiredLockedAndCheckedState()
    {
        using var f = await FormEditFixture.OpenAsync();
        var agb = f.Field("Agb");
        var pflicht = f.Field("Pflicht");
        var locked = f.Field("Gesperrt");
        Assert.True(FormMarkers.IsMarkedRequired(pflicht));

        // A required field that is still empty, a locked one and a check box say so in their name, not by hover alone.
        f.Parts.Focus.Set(pflicht.Key);
        Assert.Equal("Pflicht, " + PdfStrings.FormTipRequired, AutomationProperties.GetName(f.View));
        f.Parts.Focus.Set(locked.Key);
        Assert.Equal("Gesperrt, " + PdfStrings.FormTipReadOnly, AutomationProperties.GetName(f.View));
        f.Parts.Focus.Set(agb.Key);
        Assert.Equal("Agb, " + PdfStrings.FormStateNotChecked, AutomationProperties.GetName(f.View));

        f.Click(agb);
        await f.SettleAsync();
        await ViewFixture.WaitAsync(() => AutomationProperties.GetName(f.View) == "Agb, " + PdfStrings.FormStateChecked);

        // The editor element carries the same name.
        var box = f.Open(pflicht);
        Assert.Equal("Pflicht, " + PdfStrings.FormTipRequired, AutomationProperties.GetName(box));
        FormEditFixture.Press(box, Key.Escape);
        var plain = f.Open(f.Field("Name"));
        Assert.Equal("Name", AutomationProperties.GetName(plain));
    }

    [AvaloniaFact]
    public async Task TabPassingAtTheEndOfTheFormClearsTheFormFocusSoTheNextTabEntersAgain()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.Focus();
        f.Parts.Focus.Set(new FormWidgetKey("Ort", 1, 0));
        await f.Forms.LoadPageAsync(1);

        FormEditFixture.Press(f.View, Key.Tab);
        Assert.False(f.Parts.Keys.TookLastKey);
        Assert.Null(f.Parts.Focus.Current);

        f.View.Focus();
        FormEditFixture.Press(f.View, Key.Tab);
        await ViewFixture.WaitAsync(() => f.Parts.Focus.Current is not null);
        Assert.Equal("Name", f.Parts.Focus.Current!.Value.FieldName);

        // The same at the start with Shift+Tab.
        f.Parts.Focus.Set(f.Forms.FieldsOn(0).First(w => w.Lock == FormLock.None).Key);
        f.View.Focus();
        FormEditFixture.Press(f.View, Key.Tab, KeyModifiers.Shift);
        Assert.False(f.Parts.Keys.TookLastKey);
        Assert.Null(f.Parts.Focus.Current);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowLetsGoOfTheKeysAndTheFormCommands()
    {
        using var f = await FormEditFixture.OpenAsync();
        var view = f.View;
        f.Parts.Focus.Set(f.Field("Agb").Key);
        Assert.NotNull(view.FocusedFieldName);

        f.Window.Close();
        ViewFixture.Pump();

        // The keys were disposed with the rest of the form parts: the view no longer carries the name of a field.
        Assert.Null(view.FocusedFieldName);
    }
}
