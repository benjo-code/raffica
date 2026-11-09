using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell;
using Raffica.Shell.Theming;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

/// <summary>
/// 07-UI-09: the editors over a page (text box annotation, form fields) sit on white paper whatever the theme is, so caret, edge and
/// selection come from the fixed Paper tokens and reach 3 : 1 on white in Light, Dark and HighContrast.
/// </summary>
public sealed class EditorsOnPaperTests
{
    private static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Lin(c.R)) + (0.7152 * Lin(c.G)) + (0.0722 * Lin(c.B));
    }

    private static double OnWhite(IBrush? brush)
    {
        var color = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
        var a = Luminance(Colors.White);
        var b = Luminance(color);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static void AssertOnPaper(TextBox box, string what)
    {
        Assert.True(OnWhite(box.CaretBrush) >= 3.0, what + " caret " + OnWhite(box.CaretBrush));
        Assert.True(OnWhite(box.SelectionBrush) >= 3.0, what + " selection " + OnWhite(box.SelectionBrush));
        var edge = box.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
        Assert.True(OnWhite(edge.BorderBrush) >= 3.0, what + " edge " + OnWhite(edge.BorderBrush));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, false)]
    [InlineData(ThemeMode.Dark, false)]
    [InlineData(ThemeMode.Dark, true)]
    public async Task CaretEdgeAndSelectionOfEveryEditorReachThreeToOneOnPaper(ThemeMode mode, bool highContrast)
    {
        using var f = await FormEditFixture.OpenAsync();
        try
        {
            ShellTheme.Apply(Application.Current!, mode, AppInk.Pdf, highContrast);
            ViewFixture.Pump();

            var name = Assert.IsType<TextBox>(f.Open(f.Field("Name")));
            ViewFixture.Pump();
            AssertOnPaper(name, "text field");
            f.Interaction.CommitOrPark();
            f.Leave();

            var combo = Assert.IsType<ComboBox>(f.Open(f.Field("Frei")));
            ViewFixture.Pump();
            var inner = combo.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_EditableTextBox");
            Assert.True(OnWhite(inner.CaretBrush) >= 3.0, "combo caret");
            Assert.True(OnWhite(inner.SelectionBrush) >= 3.0, "combo selection");
            var comboEdge = combo.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Background");
            Assert.True(OnWhite(comboEdge.BorderBrush) >= 3.0, "combo edge " + OnWhite(comboEdge.BorderBrush));
            f.Interaction.CommitOrPark();
            f.Leave();

            var editor = new TextBoxEditor();
            var window = new Window { Content = editor };
            window.Show();
            editor.Field.Focus();
            ViewFixture.Pump();
            AssertOnPaper(editor.Field, "text box annotation");
            window.Close();
        }
        finally
        {
            ShellTheme.Apply(Application.Current!, ThemeMode.Light, AppInk.Pdf, forceHighContrast: false);
        }
    }
}
