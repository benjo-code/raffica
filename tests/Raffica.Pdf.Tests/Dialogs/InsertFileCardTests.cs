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

namespace Raffica.Pdf.Tests.Dialogs;

public sealed class InsertFileCardTests
{
    private static Window Show(Dialog dialog)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 600, Height = 500 };
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

    private static void Type(Dialog dialog, string text) => Find<TextBox>(dialog, "PART_Range").Text = text;

    [AvaloniaFact]
    public void RangeFieldValidatesLiveAndKeepsInsertDisabledForABadRange()
    {
        var results = new List<InsertChoice?>();
        var dialog = InsertFileCard.Create("other.pdf", 6, null, results.Add);
        var window = Show(dialog);
        var field = Find<TextBox>(dialog, "PART_Range");
        var insert = Find<Button>(dialog, "PART_Insert");
        var hint = Find<TextBlock>(dialog, "PART_Hint");

        // The default is "all" and valid.
        Assert.Equal("all", field.Text);
        Assert.True(insert.IsEnabled);
        Assert.False(hint.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(field)));
        Assert.Contains("other.pdf", Find<TextBlock>(dialog, "PART_File").Text, StringComparison.Ordinal);

        foreach (var bad in new[] { "", "0", "5-2", "abc", "1-", "999999999", "7" })
        {
            Type(dialog, bad);
            Assert.False(insert.IsEnabled, "[" + bad + "]");
            Assert.True(hint.IsVisible, bad);
            Assert.False(string.IsNullOrWhiteSpace(hint.Text), bad);

            // Enter does nothing with a bad range.
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(results);
        }

        // The reason differs: a syntax problem and a page beyond the file.
        Type(dialog, "abc");
        var syntax = hint.Text;
        Type(dialog, "7");
        Assert.NotEqual(syntax, hint.Text);
        Assert.Contains("6", hint.Text, StringComparison.Ordinal);

        foreach (var good in new[] { "3", "2-5", "1,3,5-6", "ALL", " 2 " })
        {
            Type(dialog, good);
            Assert.True(insert.IsEnabled, good);
            Assert.False(hint.IsVisible, good);
        }

        window.Close();
    }

    [AvaloniaFact]
    public void PositionDefaultsAndTheChoiceIsReturned()
    {
        // No selection: two choices, "at the end" is the default.
        var results = new List<InsertChoice?>();
        var dialog = InsertFileCard.Create("other.pdf", 6, null, results.Add);
        var window = Show(dialog);
        Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<RadioButton>(), r => r.Name == "PART_AfterSelection");
        Assert.True(Find<RadioButton>(dialog, "PART_AtEnd").IsChecked);
        Type(dialog, "2-3");
        Find<Button>(dialog, "PART_Insert").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new InsertChoice("2-3", InsertPosition.AtEnd), results.Single());
        window.Close();

        // A selection: "after the selected pages" is offered and the default; Enter confirms; the start can be chosen.
        results.Clear();
        dialog = InsertFileCard.Create("other.pdf", 6, "After the selected pages (after page 3)", results.Add);
        window = Show(dialog);
        var after = Find<RadioButton>(dialog, "PART_AfterSelection");
        Assert.True(after.IsChecked);
        Assert.Contains("page 3", after.Content as string, StringComparison.Ordinal);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Find<RadioButton>(dialog, "PART_AtStart").IsChecked = true;
        Find<Button>(dialog, "PART_Insert").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal<InsertChoice?>([new InsertChoice("all", InsertPosition.AfterSelection), new InsertChoice("all", InsertPosition.AtStart)], results);
        window.Close();
    }

    [AvaloniaFact]
    public void CancelAndEscapeReturnNothing()
    {
        var results = new List<InsertChoice?>();
        var dialog = InsertFileCard.Create("other.pdf", 6, null, results.Add);
        var window = Show(dialog);
        Find<Button>(dialog, "PART_Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal<InsertChoice?>([null, null], results);
        window.Close();
    }
}
