using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Pdf.Dialogs;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class GoToPageCardTests
{
    private static Window Show(Dialog dialog)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 600, Height = 400 };
        window.Show();
        host.Show(dialog);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        return window;
    }

    private static T Find<T>(Dialog dialog, string name)
        where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void AcceptsOnlyPagesInRange()
    {
        var results = new List<int?>();
        var dialog = GoToPageCard.Create(10, 4, results.Add);
        var window = Show(dialog);
        var field = Find<TextBox>(dialog, "PART_Page");
        var hint = Find<TextBlock>(dialog, "PART_Hint");
        var ok = Find<Button>(dialog, "PART_Ok");

        // The field starts with the current page, selected and focused, and names itself for a screen reader.
        Assert.Equal("4", field.Text);
        Assert.True(field.IsFocused);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(field)));
        Assert.False(hint.IsVisible);

        foreach (var wrong in new[] { "0", "11", "-3", "abc", "", "  ", "3.5", "1e1", "99999999999999999999", "+" })
        {
            field.Text = wrong;
            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(results);
            Assert.True(hint.IsVisible, $"'{wrong}' must show the hint");
            Assert.Contains("10", hint.Text, StringComparison.Ordinal);
        }

        // Enter confirms like the button; surrounding blanks do not matter.
        field.Text = " 7 ";
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal<int?>([7], results);

        field.Text = "1";
        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        field.Text = "10";
        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal<int?>([7, 1, 10], results);

        // Cancel and Esc answer with null.
        Find<Button>(dialog, "PART_Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal<int?>([7, 1, 10, null], results);
        window.Close();
    }

    [AvaloniaFact]
    public void ACurrentPageOutOfRangeStartsWithTheFirstPage()
    {
        var dialog = GoToPageCard.Create(3, 99, _ => { });
        var window = Show(dialog);
        Assert.Equal("1", Find<TextBox>(dialog, "PART_Page").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void TheCardSpeaksGerman()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        var dialog = GoToPageCard.Create(10, 1, _ => { });
        var window = Show(dialog);
        Assert.Equal(PdfStrings.GoToPageTitle, dialog.Title);
        Assert.NotEqual("Go to page", dialog.Title);
        var field = Find<TextBox>(dialog, "PART_Page");
        field.Text = "x";
        Find<Button>(dialog, "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("Seite", Find<TextBlock>(dialog, "PART_Hint").Text, StringComparison.Ordinal);
        window.Close();
    }
}
