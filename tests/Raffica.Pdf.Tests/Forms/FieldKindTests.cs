using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Forms;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FieldKindTests
{
    [AvaloniaFact]
    public async Task EveryEditableKindGetsItsElementNamedAfterTheField()
    {
        using var f = await FormEditFixture.OpenAsync();
        var seen = new HashSet<string>();
        foreach (var field in f.Forms.FieldsOn(0).Where(w => w.Lock == FormLock.None && w.Kind is FormFieldKind.Text or FormFieldKind.ComboBox or FormFieldKind.ListBox))
        {
            var editor = new FormFieldEditor(field);
            Assert.Equal(field.DisplayName, AutomationProperties.GetName(editor.Input));
            switch (field.Kind)
            {
                case FormFieldKind.Text:
                    var box = Assert.IsType<TextBox>(editor.Input);
                    Assert.Equal(field.Traits.HasFlag(FormFieldTraits.Multiline), box.AcceptsReturn);
                    Assert.Equal(field.Traits.HasFlag(FormFieldTraits.Password), box.PasswordChar != '\0');

                    // The control must not shorten a pasted text: a text that is too long is refused as a whole by the rules, with a hint.
                    Assert.Equal(0, box.MaxLength);
                    seen.Add(field.Key.FieldName);
                    break;
                case FormFieldKind.ComboBox:
                    var combo = Assert.IsType<ComboBox>(editor.Input);
                    Assert.Equal(field.Traits.HasFlag(FormFieldTraits.Edit), combo.IsEditable);
                    Assert.Equal(field.Options.Count, combo.Items.Count);
                    seen.Add(field.Key.FieldName);
                    break;
                default:
                    var list = Assert.IsType<ListBox>(editor.Input);
                    Assert.Equal(field.Traits.HasFlag(FormFieldTraits.MultiSelect), list.SelectionMode.HasFlag(SelectionMode.Multiple));
                    seen.Add(field.Key.FieldName);
                    break;
            }
        }

        // Single line, multi line, comb, maximum length, password, fixed and editable combo box, single and multi-select list.
        foreach (var name in new[] { "Name", "Bemerkung", "Kamm", "Kurz", "Passwort", "Farbe", "Frei", "Liste", "Mehrfach" })
        {
            Assert.Contains(name, seen);
        }

        // A check box, a radio button and a button have no editor.
        Assert.Throws<ArgumentException>(() => new FormFieldEditor(f.Field("Agb")));
        Assert.Throws<ArgumentException>(() => new FormFieldEditor(f.Field("Zahlung")));
    }

    [AvaloniaFact]
    public async Task HoverNamesWhyAWidgetIsLockedOrRequired()
    {
        using var f = await FormEditFixture.OpenAsync();
        var locked = f.Field("Gesperrt");
        var required = f.Field("Pflicht");
        Assert.True(FormMarkers.IsMarkedRequired(required));

        f.Window.MouseMove(f.PointOf(locked));
        ViewFixture.Pump();
        Assert.Equal(PdfStrings.FormTipReadOnly, ToolTip.GetTip(f.View));

        f.Window.MouseMove(f.PointOf(required));
        ViewFixture.Pump();
        Assert.Equal(PdfStrings.FormTipRequired, ToolTip.GetTip(f.View));

        // A field with neither mark has no hover text, and the text goes when the pointer leaves the widget.
        f.Window.MouseMove(f.PointOf(f.Field("Name")));
        ViewFixture.Pump();
        Assert.Null(ToolTip.GetTip(f.View));

        // The star goes with the value: a filled required field has no reason to name.
        var box = (TextBox)f.Open(required);
        FormEditFixture.SetText(box, "Ausgefuellt");
        f.Leave();
        await f.SettleAsync();
        f.Window.MouseMove(f.PointOf(f.Field("Name")));
        f.Window.MouseMove(f.PointOf(f.Field("Pflicht")));
        ViewFixture.Pump();
        Assert.Null(ToolTip.GetTip(f.View));
    }

    [AvaloniaFact]
    public async Task TouchScrollsThePageAndOpensNoEditor()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");
        var position = f.View.ToView(0, name.Bounds)!.Value.Center;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var press = new PointerPressedEventArgs(f.View, pointer, f.View, position, 0, properties, KeyModifiers.None);

        f.View.RaiseEvent(press);
        ViewFixture.Pump();

        Assert.False(press.Handled);
        Assert.False(f.Interaction.IsEditing);
        Assert.Null(f.Parts.Focus.Current);
    }

    [AvaloniaFact]
    public async Task NothingOpensWhileADialogIsOpen()
    {
        using var f = await FormEditFixture.OpenAsync();
        var queue = new DialogQueue(f.Window.Host);
        Action<bool>? close = null;
        var shown = queue.ShowAsync<bool>(done =>
        {
            close = done;
            return NoticeCard.Create("Test", "Open", () => done(true));
        });
        ViewFixture.Pump();
        Assert.True(f.Window.Host.IsOpen);

        var name = f.Field("Name");
        var position = f.View.ToView(0, name.Bounds)!.Value.Center;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var press = new PointerPressedEventArgs(f.View, pointer, f.View, position, 0, properties, KeyModifiers.None);
        f.View.RaiseEvent(press);
        ViewFixture.Pump();

        Assert.False(press.Handled);
        Assert.False(f.Interaction.IsEditing);

        close!(true);
        await shown.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
