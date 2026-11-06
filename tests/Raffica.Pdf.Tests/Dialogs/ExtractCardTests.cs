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

public sealed class ExtractCardTests
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

    [AvaloniaFact]
    public void RangeStartsAtTheCurrentPageValidatesLiveAndSuggestsTheName()
    {
        var results = new List<string?>();
        var dialog = ExtractCard.Create("a.pdf", 6, 3, "a", results.Add);
        var window = Show(dialog);
        var field = Find<TextBox>(dialog, "PART_Range");
        var extract = Find<Button>(dialog, "PART_Extract");

        Assert.Equal("3", field.Text);
        Assert.True(extract.IsEnabled);
        Assert.Equal("a-pages-3.pdf", Find<TextBlock>(dialog, "PART_Name").Text);

        foreach (var bad in new[] { "", "0", "5-2", "abc", "7", "999999999" })
        {
            field.Text = bad;
            Assert.False(extract.IsEnabled, bad);
            Assert.True(Find<TextBlock>(dialog, "PART_Hint").IsVisible, bad);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            extract.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(results);
        }

        field.Text = "2-4";
        Assert.Equal("a-pages-2-4.pdf", Find<TextBlock>(dialog, "PART_Name").Text);
        extract.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["2-4"], results);
    }

    [AvaloniaFact]
    public void CancelAndEscAnswerNull()
    {
        var results = new List<string?>();
        var dialog = ExtractCard.Create("a.pdf", 6, 1, "a", results.Add);
        Show(dialog);
        Find<Button>(dialog, "PART_Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([null], results);
    }

    [AvaloniaFact]
    public void ConfirmCardListsTheLossesAndAnswersGoOrBack()
    {
        var results = new List<bool>();
        var dialog = ExtractCard.CreateConfirm("1-2", ["Loss one.", "Loss two."], results.Add);
        Show(dialog);
        var lines = dialog.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "PART_Loss").Select(t => t.Text).ToList();
        Assert.Equal(["• Loss one.", "• Loss two."], lines);
        Find<Button>(dialog, "PART_Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<Button>(dialog, "PART_Go").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([false, true], results);
    }
}
