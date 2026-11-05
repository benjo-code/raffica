using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Forms;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

/// <summary>The edges of the field editing: values a document holds that the rules refuse, a cleared combo box, the walk over a page border and the scroll bar.</summary>
public sealed class FieldEditEdgeTests
{
    private const string StoredValue = "/V (Alt)";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // The Name field of forms-basic holds "Alt" as /V; the patch writes a line break into the same bytes, a value that breaks the rules
    // of a single-line field (Raffica never writes it, but a document can hold it). The length stays, so the file stays valid.
    private static void StoreARefusableValue(string path)
    {
        var text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var at = text.IndexOf(StoredValue, StringComparison.Ordinal);
        Assert.True(at >= 0, "the fixture does not hold " + StoredValue);
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes(text.Remove(at, StoredValue.Length).Insert(at, "/V (A\nt)")));
    }

    [AvaloniaFact]
    public async Task AnUntouchedValueThatBreaksTheRulesIsLeftAlone()
    {
        using var f = await FormEditFixture.OpenAsync(prepare: StoreARefusableValue);
        var name = f.Field("Name");
        Assert.False(FormValueRules.CheckText(name, name.State.Text!).Accepted, "the stored value must fail the rules");

        f.Open(name);
        f.Leave();
        await f.SettleAsync();

        // No hint, no draft, no dirty tab: the user did not touch the text.
        Assert.False(f.Interaction.IsEditing);
        Assert.False(f.Tab.Annotations.HasUnresolved);
        Assert.False(f.Tab.IsDirty);
        Assert.False(f.Tab.Annotations.CanUndo);
        Assert.Equal(name.State.Text, (await f.FromDocumentAsync("Name")).State.Text);

        // Clicks on other fields work at once.
        f.Click(f.Field("Agb"));
        await f.SettleAsync();
        Assert.True((await f.FromDocumentAsync("Agb")).State.Checked);

        // Enter on the untouched value only moves on.
        var again = (TextBox)f.Open(name);
        Assert.True(FormEditFixture.Press(again, Key.Enter));
        await f.SettleAsync();
        Assert.False(f.Interaction.IsEditing);
        Assert.False(f.Tab.Annotations.HasUnresolved);

        // A real edit of such a field is still checked: a text that breaks the rules is refused and stays.
        var edit = (TextBox)f.Open(name);
        FormEditFixture.SetText(edit, "x\ny");
        f.Leave();
        Assert.True(f.Interaction.IsEditing);
        Assert.NotEmpty(f.Interaction.Editor!.Hint);
        Assert.True(f.Tab.Annotations.HasUnresolved);
    }

    [AvaloniaFact]
    public async Task ClearingTheTextOfAnEditableComboIsAnEditAndWritesTheEmptyValue()
    {
        using var f = await FormEditFixture.OpenAsync();

        // Untouched and empty: nothing happens.
        f.Open(f.Field("Frei"));
        Assert.True(string.IsNullOrEmpty(f.Field("Frei").State.Text));
        f.Leave();
        await f.SettleAsync();
        Assert.False(f.Tab.IsDirty);

        var combo = Assert.IsType<ComboBox>(f.Open(f.Field("Frei")));
        combo.Text = "Eigener Wert";
        ViewFixture.Pump();
        Assert.True(FormEditFixture.Press(combo, Key.Enter));
        await f.SettleAsync();
        Assert.Equal("Eigener Wert", (await f.FromDocumentAsync("Frei")).State.Text);

        // The field has a value: clearing it is a real edit, written as the empty value, one more step.
        var again = Assert.IsType<ComboBox>(f.Open(f.Field("Frei")));
        Assert.Equal("Eigener Wert", again.Text);
        again.Text = string.Empty;
        ViewFixture.Pump();
        Assert.True(f.Tab.IsDirty);
        f.Leave();
        await f.SettleAsync();

        Assert.False(f.Interaction.IsEditing);
        Assert.True(string.IsNullOrEmpty((await f.FromDocumentAsync("Frei")).State.Text));
        await f.Tab.Annotations.UndoAsync();
        Assert.Equal("Eigener Wert", (await f.FromDocumentAsync("Frei")).State.Text);
    }

    [AvaloniaFact]
    public async Task EnterInTheLastLoadedFieldLoadsTheNextPageAndFocusesItsFirstField()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");

        // A session that has read page 0 only, with "Name" as its last field there, so the next field lies on a page that is not loaded yet
        // (forms-basic has two pages but no field that ends page 0 with a single-line text field: this is the PageReader seam).
        using var forms = new FormSession(f.Tab, SynchronizationContext.Current!)
        {
            PageReader = async (doc, page, token) =>
            {
                var fields = await doc.GetFormFieldsAsync(page, PdfPriority.Background, token);
                return page == 0 ? [.. fields.Where(w => w.Key.FieldName == "Name")] : fields;
            },
        };
        await forms.LoadPageAsync(0);
        Assert.False(forms.IsLoaded(1));
        f.Interaction.Dispose();
        var focus = new FormFocus();
        using var interaction = new FormInteraction(f.Tab, f.View, f.Window.Tools, forms, focus, new DialogQueue(f.Window.Host), () => true);

        f.Click(name);
        Assert.True(interaction.IsEditing);
        var box = (TextBox)interaction.Editor!.Input;
        FormEditFixture.SetText(box, "Seitenwechsel");
        Assert.True(FormEditFixture.Press(box, Key.Enter));
        await ViewFixture.WaitAsync(() => focus.Current is { Page: 1 });
        await interaction.Completion;

        Assert.True(forms.IsLoaded(1));
        Assert.Equal(FormNavigation.Next(forms.FieldsOn(1), null, forward: true), focus.Current);
        var written = (await f.Tab.Session.Document!.GetFormFieldsAsync(0, Cancel)).First(w => w.Key.FieldName == "Name");
        Assert.Equal("Seitenwechsel", written.State.Text);
    }

    [AvaloniaFact]
    public async Task APressOverTheScrollBarOpensNoEditor()
    {
        using var f = await FormEditFixture.OpenAsync();
        var name = f.Field("Name");

        // Zoomed in and scrolled so that the widget reaches under the vertical scroll bar at the right edge of the view.
        f.View.SetZoom(3);
        ViewFixture.Pump();
        f.View.GoToDestination(0, name.Bounds.Top);
        ViewFixture.Pump();
        var width = f.View.Bounds.Width;
        var rect = f.View.ToView(0, name.Bounds)!.Value;
        f.View.Offset += new Vector(rect.Right - (width + 20), 0);
        ViewFixture.Pump();
        rect = f.View.ToView(0, name.Bounds)!.Value;
        var onBar = new Point(width - 4, rect.Center.Y);
        Assert.True(f.View.IsOverScrollBar(onBar), "the point is not over the scroll bar");
        var spot = f.View.PageAt(onBar)!.Value;
        Assert.Equal(name.Key, f.Forms.FieldAt(0, spot.X, spot.Y)?.Key);

        // The press arrives through the window as the mouse sends it: the bar takes it (its own control is the source of the event).
        f.ClickAt(f.View.TranslatePoint(onBar, f.Window)!.Value);
        Assert.False(f.Interaction.IsEditing);
        Assert.Null(f.Parts.Focus.Current);

        // The same press a little to the left, on the widget and off the bar, opens the editor: the guard is the bar and nothing else.
        var onWidget = new Point(width - 60, rect.Center.Y);
        Assert.False(f.View.IsOverScrollBar(onWidget));
        f.ClickAt(f.View.TranslatePoint(onWidget, f.Window)!.Value);
        Assert.True(f.Interaction.IsEditing);
    }
}
