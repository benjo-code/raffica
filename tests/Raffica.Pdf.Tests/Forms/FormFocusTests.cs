using Avalonia.Headless.XUnit;
using Raffica.Core.Forms;
using Raffica.Core.Text;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Annotating;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FormFocusTests
{
    internal static FormField Widget(string name, int index, FormFieldKind kind = FormFieldKind.Text, FormFieldTraits traits = FormFieldTraits.None, FormLock locked = FormLock.None, FormFieldState? state = null, int page = 0) => new(
        new FormWidgetKey(name, page, index),
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

    // Pages 0 and 5 of an eight-page form are read: a field on each.
    private static async Task<FormSession> SparseAsync(AnnotatedFile file)
    {
        var forms = new FormSession(file.Tab, new QueueContext())
        {
            PageCountSource = () => 8,
            PageReader = (_, page, _) => Task.FromResult<IReadOnlyList<FormField>>(
                page is 0 or 5 ? [Widget("p" + page, 0, page: page)] : []),
        };
        await forms.LoadPageAsync(0);
        await forms.LoadPageAsync(5);
        return forms;
    }

    [AvaloniaFact]
    public async Task MoveNeverJumpsOverAnUnloadedPage()
    {
        using var file = await AnnotatedFile.OpenAsync("forms-basic.pdf");
        using var forms = await SparseAsync(file);
        var focus = new FormFocus();
        focus.Set(new FormWidgetKey("p0", 0, 0));

        Assert.False(focus.Move(forms, forward: true));
        Assert.Equal("p0", focus.Current?.FieldName);
        Assert.Equal(1, focus.PageToLoad(forms, forward: true));
        Assert.Null(focus.PageToLoad(forms, forward: false));

        // Pages 1 to 4 are read and have no field: they are walked over.
        for (var page = 1; page <= 4; page++)
        {
            Assert.Equal(page, focus.PageToLoad(forms, forward: true));
            await forms.LoadPageAsync(page);
        }

        Assert.Null(focus.PageToLoad(forms, forward: true));
        Assert.True(focus.Move(forms, forward: true));
        Assert.Equal("p5", focus.Current?.FieldName);

        // After the last field the next page (6) is not read; once 6 and 7 are, the end is a true end.
        Assert.False(focus.Move(forms, forward: true));
        Assert.Equal(6, focus.PageToLoad(forms, forward: true));
        await forms.LoadPageAsync(6);
        await forms.LoadPageAsync(7);
        Assert.False(focus.Move(forms, forward: true));
        Assert.Null(focus.PageToLoad(forms, forward: true));
        Assert.True(focus.Move(forms, forward: false));
        Assert.Equal("p0", focus.Current?.FieldName);
    }

    [AvaloniaFact]
    public async Task WithoutFocusTheWalkStartsAtTheFirstPageAndTheLastPage()
    {
        using var file = await AnnotatedFile.OpenAsync("forms-basic.pdf");
        using var forms = new FormSession(file.Tab, new QueueContext())
        {
            PageCountSource = () => 3,
            PageReader = (_, page, _) => Task.FromResult<IReadOnlyList<FormField>>([Widget("p" + page, 0, page: page)]),
        };
        var focus = new FormFocus();

        Assert.False(focus.Move(forms, forward: true));
        Assert.Equal(0, focus.PageToLoad(forms, forward: true));
        Assert.Equal(2, focus.PageToLoad(forms, forward: false));

        await forms.LoadPageAsync(0);
        Assert.True(focus.Move(forms, forward: true));
        Assert.Equal("p0", focus.Current?.FieldName);
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
