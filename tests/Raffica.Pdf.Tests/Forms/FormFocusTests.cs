using Avalonia.Headless.XUnit;
using Raffica.Core.Forms;
using Raffica.Core.Text;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Annotating;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FormFocusTests
{
    internal static FormField Widget(string name, int index, FormFieldKind kind = FormFieldKind.Text, FormFieldTraits traits = FormFieldTraits.None, FormLock locked = FormLock.None, FormFieldState? state = null) => new(
        new FormWidgetKey(name, 0, index),
        index,
        kind,
        traits,
        new PageRect(20, 20 + (index * 40), 160, 44 + (index * 40)),
        null,
        null,
        null,
        [],
        state ?? FormFieldState.OfText(string.Empty),
        locked,
        locked == FormLock.None ? null : "locked");

    internal static async Task<FormSession> SessionAsync(AnnotatedFile file, params FormField[] fields)
    {
        var forms = new FormSession(file.Tab, new QueueContext())
        {
            PageReader = (_, _, _) => Task.FromResult<IReadOnlyList<FormField>>(fields),
        };
        await forms.LoadPageAsync(0);
        return forms;
    }

    [AvaloniaFact]
    public async Task MovePassesAtTheEndsInsteadOfTrappingTheFocus()
    {
        using var file = await AnnotatedFile.OpenAsync("forms-basic.pdf");
        using var forms = await SessionAsync(file, Widget("a", 0), Widget("b", 1));
        var focus = new FormFocus();
        var changes = 0;
        focus.Changed += () => changes++;

        Assert.True(focus.Move(forms, forward: true));
        Assert.Equal("a", focus.Current?.FieldName);
        Assert.True(focus.Move(forms, forward: true));
        Assert.Equal("b", focus.Current?.FieldName);
        Assert.False(focus.Move(forms, forward: true));
        Assert.Equal("b", focus.Current?.FieldName);
        Assert.True(focus.Move(forms, forward: false));
        Assert.Equal("a", focus.Current?.FieldName);
        Assert.False(focus.Move(forms, forward: false));
        Assert.Equal("a", focus.Current?.FieldName);
        Assert.Equal(3, changes);
        Assert.Equal("a", focus.CurrentField(forms)?.Key.FieldName);
    }

    [AvaloniaFact]
    public async Task MoveSkipsLockedWidgets()
    {
        using var file = await AnnotatedFile.OpenAsync("forms-basic.pdf");
        using var forms = await SessionAsync(file, Widget("a", 0), Widget("sig", 1, FormFieldKind.Signature, locked: FormLock.Unsupported), Widget("c", 2));
        var focus = new FormFocus();

        focus.Set(new FormWidgetKey("a", 0, 0));
        Assert.True(focus.Move(forms, forward: true));
        Assert.Equal("c", focus.Current?.FieldName);
        Assert.True(focus.Move(forms, forward: false));
        Assert.Equal("a", focus.Current?.FieldName);
    }

    [AvaloniaFact]
    public async Task SetRaisesChangedOnlyForAnotherKey()
    {
        using var file = await AnnotatedFile.OpenAsync("forms-basic.pdf");
        using var forms = await SessionAsync(file, Widget("a", 0));
        var focus = new FormFocus();
        var changes = 0;
        focus.Changed += () => changes++;

        focus.Set(new FormWidgetKey("a", 0, 0));
        focus.Set(new FormWidgetKey("a", 0, 0));
        focus.Set(null);
        Assert.Equal(2, changes);
        Assert.Null(focus.CurrentField(forms));
    }
}
