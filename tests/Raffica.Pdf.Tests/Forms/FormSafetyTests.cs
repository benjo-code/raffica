using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Forms;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

/// <summary>Hostile, locked, scripted and signed forms: they open, nothing throws, and only what is safe is offered.</summary>
public sealed class FormSafetyTests
{
    [AvaloniaTheory]
    [InlineData("forms-hostile.pdf")]
    [InlineData("forms-xfa.pdf")]
    [InlineData("forms-calc.pdf")]
    [InlineData("forms-signed.pdf")]
    public async Task HostileAndLockedFormsOfferOnlyWhatIsSafe(string file)
    {
        using var f = await FormEditFixture.OpenAsync(file);
        Assert.True(f.Forms.HasForm);
        for (var page = 0; page < f.Forms.PageCount; page++)
        {
            await f.Forms.LoadPageAsync(page);
        }

        var fields = f.Forms.LoadedFields();
        Assert.NotEmpty(fields);

        // The Tab walk never lands on a locked field and runs to a true end.
        f.View.Focus();
        var visited = new List<FormWidgetKey>();
        while (await f.Interaction.Mover.MoveAsync(forward: true))
        {
            visited.Add(f.Parts.Focus.Current!.Value);
            Assert.True(visited.Count <= fields.Count, "the walk does not end");
        }

        Assert.All(visited, key => Assert.Equal(FormLock.None, fields.Single(w => w.Key == key).Lock));
        Assert.Equal(fields.Count(w => w.Lock == FormLock.None), visited.Count);

        foreach (var locked in fields.Where(w => w.Lock != FormLock.None))
        {
            // The reason is named, the widget takes no press, and a write is refused as a whole without touching the document.
            Assert.False(string.IsNullOrWhiteSpace(locked.LockReason), locked.Key.FieldName + " has no reason");
            Assert.False(f.Interaction.Activate(locked));
            Assert.False(f.Interaction.IsEditing);
            var result = await f.Forms.SetValueAsync(locked, FormFieldState.OfText("x"));
            var refused = Assert.IsType<FormEditResult.Refused>(result);
            Assert.Equal(FormEditProblem.Locked, refused.Problem);
            Assert.False(f.Tab.IsDirty);

            // A click on it does nothing (no editor, no card, nothing changed), buttons included.
            if (f.View.ToView(locked.Key.Page, locked.Bounds) is { } rect && rect.Width > 0 && rect.Height > 0 && new Rect(f.View.Bounds.Size).Contains(rect.Center))
            {
                f.Click(locked);
                Assert.False(f.Interaction.IsEditing);
                Assert.False(f.Window.Host.IsOpen);
                Assert.False(f.Tab.IsDirty);
            }
        }

        Assert.False(f.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task AButtonClickDoesNothing()
    {
        using var f = await FormEditFixture.OpenAsync("forms-signed.pdf");
        var button = f.Forms.FieldsOn(0).Single(w => w.Kind == FormFieldKind.PushButton);
        Assert.Equal(FormLock.Unsupported, button.Lock);

        f.Click(button);
        await f.SettleAsync();
        Assert.False(f.Interaction.IsEditing);
        Assert.False(f.Window.Host.IsOpen);
        Assert.False(f.Tab.IsDirty);
        Assert.Null(f.Parts.Focus.Current);
    }

    [AvaloniaFact]
    public async Task AFormatScriptFieldIsWrittenUnchanged()
    {
        using var f = await FormEditFixture.OpenAsync("forms-calc.pdf");

        // The date and the range field carry format and validation scripts; none runs, so the text goes in exactly as typed.
        foreach (var (name, text) in new[] { ("Datum", "1.2.2026"), ("Bereich", "999"), ("Zahl1", "abc") })
        {
            var box = (TextBox)f.Open(f.Field(name));
            FormEditFixture.SetText(box, text);
            f.Leave();
            await f.SettleAsync();
            Assert.Equal(text, (await f.FromDocumentAsync(name)).State.Text);
        }

        // No calculation runs either: the sum is what the file said.
        Assert.Equal("42", (await f.FromDocumentAsync("Summe")).State.Text);
        Assert.True(f.Tab.IsDirty);
    }
}
