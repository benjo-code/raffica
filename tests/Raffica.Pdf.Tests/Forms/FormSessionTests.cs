using Avalonia.Headless.XUnit;
using Raffica.Core.Forms;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FormSessionTests
{
    private const string Basic = "forms-basic.pdf";

    private static async Task<FormField> FieldAsync(FormSession forms, string name)
    {
        await forms.LoadPageAsync(0);
        return forms.FieldsOn(0).Single(f => f.Key.FieldName == name);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            ViewFixture.Pump();
        }

        return condition();
    }

    [AvaloniaFact]
    public async Task NoFormMeansNoWork()
    {
        using var file = await AnnotatedFile.OpenAsync("text-rich.pdf");
        var forms = file.Tab.Forms;
        var changes = 0;
        forms.Changed += () => changes++;

        Assert.False(forms.HasForm);
        var load = forms.LoadPageAsync(0);
        Assert.True(load.IsCompletedSuccessfully);
        await forms.RefreshRequiredAsync();
        await forms.LoadInfoAsync();
        ViewFixture.Pump();

        Assert.Empty(forms.FieldsOn(0));
        Assert.False(forms.IsLoaded(0));
        Assert.Null(forms.Info);
        Assert.Empty(forms.EmptyRequired);
        Assert.Equal(0, changes);
        Assert.Null(file.Tab.AnnotationsIfCreated);
    }

    [AvaloniaFact]
    public async Task FieldsAreLoadedPerPageAndASecondCallJoinsTheFirst()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var forms = file.Tab.Forms;
        Assert.True(forms.HasForm);
        Assert.Empty(forms.FieldsOn(0));

        var first = forms.LoadPageAsync(0);
        var second = forms.LoadPageAsync(0);
        Assert.Same(first, second);
        await first;

        Assert.Contains(forms.FieldsOn(0), f => f.Key.FieldName == "Name");
        Assert.Empty(forms.FieldsOn(1));
        Assert.True(forms.LoadPageAsync(0).IsCompletedSuccessfully);

        await forms.LoadInfoAsync();
        Assert.NotNull(forms.Info);
        Assert.True(forms.Info.HasFields);
    }

    [AvaloniaFact]
    public async Task FieldAtFindsTheTopmostWidgetAndLockedOnesToo()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var forms = file.Tab.Forms;
        var name = await FieldAsync(forms, "Name");
        var locked = forms.FieldsOn(0).Single(f => f.Key.FieldName == "Gesperrt");
        Assert.NotEqual(FormLock.None, locked.Lock);

        Assert.Equal(name.Key, forms.FieldAt(0, (name.Bounds.Left + name.Bounds.Right) / 2, (name.Bounds.Top + name.Bounds.Bottom) / 2)?.Key);
        Assert.Equal(locked.Key, forms.FieldAt(0, (locked.Bounds.Left + locked.Bounds.Right) / 2, (locked.Bounds.Top + locked.Bounds.Bottom) / 2)?.Key);
        Assert.Null(forms.FieldAt(0, -5, -5));
        Assert.Null(forms.FieldAt(7, 10, 10));

        // Where widgets overlap, the one later in the page's annotations is on top.
        foreach (var field in forms.FieldsOn(0))
        {
            var x = (field.Bounds.Left + field.Bounds.Right) / 2;
            var y = (field.Bounds.Top + field.Bounds.Bottom) / 2;
            Assert.Equal(forms.FieldsOn(0).Last(f => f.Bounds.Contains(x, y)).Key, forms.FieldAt(0, x, y)?.Key);
        }
    }

    [AvaloniaFact]
    public async Task SetValueMakesOneUndoStepAndTheTabDirty()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var forms = file.Tab.Forms;
        var name = await FieldAsync(forms, "Name");
        Assert.False(file.Tab.IsDirty);

        var result = await forms.SetValueAsync(name, FormFieldState.OfText("Neu"));
        await file.Tab.Annotations.FlushAsync();
        ViewFixture.Pump();

        Assert.IsType<FormEditResult.Applied>(result);
        Assert.True(file.Tab.IsDirty);
        Assert.True(file.Tab.Annotations.CanUndo);
        Assert.Equal(FormFieldState.OfText("Neu"), forms.FieldsOn(0).Single(f => f.Key.FieldName == "Name").State);

        await file.Tab.Annotations.UndoAsync();
        Assert.False(file.Tab.Annotations.CanUndo);
        Assert.True(await WaitForAsync(() => forms.FieldsOn(0).Single(f => f.Key.FieldName == "Name").State.Equals(FormFieldState.OfText("Alt"))));
    }

    [AvaloniaFact]
    public async Task UnchangedValueIsNotAStep()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var forms = file.Tab.Forms;
        var name = await FieldAsync(forms, "Name");

        var result = await forms.SetValueAsync(name, FormFieldState.OfText("Alt"));
        await file.Tab.Annotations.FlushAsync();
        ViewFixture.Pump();

        Assert.IsType<FormEditResult.Unchanged>(result);
        Assert.False(file.Tab.Annotations.CanUndo);
        Assert.False(file.Tab.IsDirty);
    }

    [AvaloniaTheory]
    [InlineData("Name", "Zeile\nzwei", nameof(FormEditProblem.LineBreakNotAllowed))]
    [InlineData("Kurz", "viel zu lang", nameof(FormEditProblem.TooLong))]
    [InlineData("Name", "中文", nameof(FormEditProblem.UnsupportedCharacters))]
    public async Task RefusedTextReturnsTheProblemAndWritesNothing(string fieldName, string text, string expected)
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var forms = file.Tab.Forms;
        var field = await FieldAsync(forms, fieldName);
        var before = field.State;

        var result = await forms.SetValueAsync(field, FormFieldState.OfText(text));
        await file.Tab.Annotations.FlushAsync();
        ViewFixture.Pump();

        var refused = Assert.IsType<FormEditResult.Refused>(result);
        Assert.Equal(Enum.Parse<FormEditProblem>(expected), refused.Problem);
        Assert.False(file.Tab.Annotations.CanUndo);
        Assert.False(file.Tab.IsDirty);
        Assert.Equal(before, forms.FieldsOn(0).Single(f => f.Key.FieldName == fieldName).State);
    }

    [AvaloniaFact]
    public async Task LockedFieldIsRefused()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var forms = file.Tab.Forms;
        var locked = await FieldAsync(forms, "Gesperrt");

        var result = await forms.SetValueAsync(locked, FormFieldState.OfText("Anders"));

        Assert.Equal(FormEditProblem.Locked, Assert.IsType<FormEditResult.Refused>(result).Problem);
        Assert.False(file.Tab.Annotations.CanUndo);
        Assert.False(file.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task PagesInvalidatedRefreshesTheCachedPage()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var forms = file.Tab.Forms;
        await FieldAsync(forms, "Name");

        // Not through the form session: the editor writes and the viewer session announces the page.
        await file.Tab.Annotations.Editor.SetFormValueAsync("Name", FormFieldState.OfText("Direkt"));

        Assert.True(await WaitForAsync(() => forms.FieldsOn(0).Single(f => f.Key.FieldName == "Name").State.Equals(FormFieldState.OfText("Direkt"))));
    }

    [AvaloniaFact]
    public async Task RequiredListFollowsTheValues()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var forms = file.Tab.Forms;
        var required = await FieldAsync(forms, "Pflicht");

        await forms.RefreshRequiredAsync();
        Assert.Equal(["Pflicht"], forms.EmptyRequired);

        Assert.IsType<FormEditResult.Applied>(await forms.SetValueAsync(required, FormFieldState.OfText("ausgefüllt")));
        Assert.Empty(forms.EmptyRequired);

        await file.Tab.Annotations.UndoAsync();
        Assert.True(await WaitForAsync(() => forms.EmptyRequired.Count == 1));
    }

    [AvaloniaFact]
    public async Task HighlightIsAppliedOnceAndNotForADocumentWithoutAForm()
    {
        using (var file = await AnnotatedFile.OpenAsync(Basic))
        {
            var forms = file.Tab.Forms;
            Assert.False(file.Tab.Session.FormHighlight);
            forms.Highlight = true;
            forms.Highlight = true;
            Assert.True(file.Tab.Session.FormHighlight);
            forms.Highlight = false;
            Assert.False(file.Tab.Session.FormHighlight);
        }

        using var plain = await AnnotatedFile.OpenAsync("text-rich.pdf");
        plain.Tab.Forms.Highlight = true;
        Assert.True(plain.Tab.Forms.Highlight);
        Assert.False(plain.Tab.Session.FormHighlight);
    }

    [AvaloniaFact]
    public async Task ChangedIsMarshalledToTheUiContext()
    {
        using var file = await AnnotatedFile.OpenAsync(Basic);
        var ui = new QueueContext();
        using var forms = new FormSession(file.Tab, ui);
        var changes = 0;
        forms.Changed += () => changes++;

        await forms.LoadPageAsync(0);

        Assert.True(ui.Count > 0);
        Assert.NotEqual(Environment.CurrentManagedThreadId, ui.PostedFromThread);
        Assert.Equal(0, changes);
        Assert.True(ui.Run() > 0);
        Assert.True(changes > 0);
    }
}
