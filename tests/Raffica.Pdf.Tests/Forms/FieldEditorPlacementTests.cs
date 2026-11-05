using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Raffica.Core.Forms;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FieldEditorPlacementTests
{
    private static void AssertSitsOver(FormEditFixture f, FormField field)
    {
        var rect = f.View.ToView(field.Key.Page, field.Bounds)!.Value;
        var editor = f.Interaction.Editor!;
        var input = editor.Input;
        Assert.Equal(rect, f.View.FieldRect);
        Assert.Equal(rect.X, editor.Bounds.X, 0.5);
        Assert.Equal(rect.Y, editor.Bounds.Y, 0.5);
        Assert.Equal(rect.Width, input.Bounds.Width, 0.5);
        Assert.Equal(rect.Height, input.Bounds.Height, 0.5);
    }

    // Brings the widget into the view at the current zoom: the page of the field is read and scrolled to the field.
    private static async Task ShowAsync(FormEditFixture f, FormField field)
    {
        f.View.GoToPage(field.Key.Page);
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => f.Forms.IsLoaded(field.Key.Page));
        f.View.GoToDestination(field.Key.Page, field.Bounds.Top);
        ViewFixture.Pump();
        var rect = f.View.ToView(field.Key.Page, field.Bounds)!.Value;
        Assert.True(new Rect(f.View.Bounds.Size).Contains(rect), "the field is not in the view: " + rect);
    }

    [AvaloniaFact]
    public async Task EditorSitsOverTheFieldOnRotatedAndCropBoxPages()
    {
        // Page 1 has /Rotate 90 and a CropBox that is offset inside the MediaBox; page 2 has /Rotate 270.
        using var f = await FormEditFixture.OpenAsync("forms-rotated.pdf");
        await ViewFixture.WaitAsync(() => f.Forms.IsLoaded(0));
        foreach (var (name, page) in new[] { ("Text1", 0), ("Text2", 1) })
        {
            foreach (var zoom in new[] { 0.75, 1.5 })
            {
                f.View.SetZoom(zoom);
                ViewFixture.Pump();
                await f.Forms.LoadPageAsync(page);
                var field = f.Field(name, 0, page);
                await ShowAsync(f, field);

                f.Click(field);
                Assert.True(f.Interaction.IsEditing, name + " at " + zoom);
                AssertSitsOver(f, field);

                // The text stays upright and grows with the zoom (the page rotation never turns the editor).
                var size = ((TextBox)f.Interaction.Editor!.Input).FontSize;
                Assert.True(size > 0);
                f.Interaction.CommitOrPark();
                Assert.False(f.Interaction.IsEditing);
            }
        }

        // The size of the text follows the zoom.
        var probe = f.Field("Text1");
        f.View.SetZoom(0.75);
        await ShowAsync(f, probe);
        f.Click(probe);
        var small = ((TextBox)f.Interaction.Editor!.Input).FontSize;
        f.Interaction.CommitOrPark();
        f.View.SetZoom(1.5);
        await ShowAsync(f, probe);
        f.Click(probe);
        var large = ((TextBox)f.Interaction.Editor!.Input).FontSize;
        Assert.Equal(2, large / small, 0.05);
    }

    [AvaloniaFact]
    public async Task EditorFollowsScrollAndIsHiddenWhenTheFieldLeavesTheView()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.SetZoom(1.5);
        ViewFixture.Pump();
        var name = f.Field("Name");
        await ShowAsync(f, name);
        var box = (TextBox)f.Open(name);
        FormEditFixture.SetText(box, "Gescrollt");
        var before = f.View.FieldRect;

        // A small scroll: the editor moves with the field.
        f.View.Offset += new Vector(0, 20);
        ViewFixture.Pump();
        Assert.True(f.Interaction.IsEditing);
        AssertSitsOver(f, name);
        Assert.Equal(before.Y - 20, f.View.FieldRect.Y, 0.5);

        // Scrolled out of sight: a valid text is written and no editor is on screen.
        f.View.Offset = new Vector(f.View.Offset.X, f.View.MaxOffsetY);
        ViewFixture.Pump();
        await f.SettleAsync();
        Assert.False(f.Interaction.IsEditing);
        Assert.Null(f.View.Field);
        Assert.Equal("Gescrollt", (await f.FromDocumentAsync("Name")).State.Text);
    }

    [AvaloniaFact]
    public async Task ARefusedTextStaysHiddenWhileItsFieldIsOutOfSightAndComesBack()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.SetZoom(1.5);
        ViewFixture.Pump();
        var name = f.Field("Name");
        await ShowAsync(f, name);
        var box = (TextBox)f.Open(name);
        FormEditFixture.SetText(box, "中");

        f.View.Offset = new Vector(f.View.Offset.X, f.View.MaxOffsetY);
        ViewFixture.Pump();
        Assert.True(f.Interaction.IsEditing);
        Assert.False(f.Interaction.Editor!.IsVisible);
        Assert.True(f.Tab.IsDirty);

        await ShowAsync(f, name);
        Assert.True(f.Interaction.Editor.IsVisible);
        AssertSitsOver(f, name);
    }

    [AvaloniaFact]
    public async Task DarkPageKeepsTheEditorReadable()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.PageTheme = PageTheme.Dark;
        ViewFixture.Pump();

        foreach (var (name, check) in new (string, Func<Control, IBrush?>)[]
        {
            ("Name", c => ((TextBox)c).Background),
            ("Farbe", c => ((ComboBox)c).Background),
            ("Liste", c => ((ListBox)c).Background),
        })
        {
            var input = f.Open(f.Field(name));
            Assert.Equal(Colors.White, ((ISolidColorBrush)check(input)!).Color);
            var foreground = ((ISolidColorBrush)((TemplatedControl)input).Foreground!).Color;
            Assert.Equal(Colors.Black, foreground);
            f.Interaction.CommitOrPark();
            f.Leave();
            await f.SettleAsync();
        }
    }
}
