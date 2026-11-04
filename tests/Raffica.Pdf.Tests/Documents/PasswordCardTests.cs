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

namespace Raffica.Pdf.Tests.Documents;

public sealed class PasswordCardTests
{
    private static Window Show(Dialog dialog)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 600, Height = 400 };
        window.Show();
        host.Show(dialog);
        Pump();
        return window;
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static T Find<T>(Dialog dialog, string name)
        where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void TheCardHasFieldAndOkAndCancelAndEnterConfirms()
    {
        var results = new List<string?>();
        var dialog = PasswordCard.Create("secret.pdf", incorrect: false, results.Add);
        var window = Show(dialog);

        var field = Find<TextBox>(dialog, "PART_Password");
        Assert.Equal('•', field.PasswordChar);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(field)));
        Assert.True(field.IsFocused);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(dialog)));
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("secret.pdf", StringComparison.Ordinal) == true);
        Assert.False(Find<TextBlock>(dialog, "PART_Incorrect").IsVisible);

        var ok = Find<Button>(dialog, "PART_Ok");
        var cancel = Find<Button>(dialog, "PART_Cancel");

        field.Text = "hôtel";
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(["hôtel"], results);

        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal<string?>(["hôtel", "hôtel", null], results);
        window.Close();
    }

    [AvaloniaFact]
    public void TheIncorrectHintShowsOnlyForAWrongPassword()
    {
        var dialog = PasswordCard.Create("secret.pdf", incorrect: true, _ => { });
        var window = Show(dialog);
        var hint = Find<TextBlock>(dialog, "PART_Incorrect");
        Assert.True(hint.IsVisible);
        Assert.Contains("incorrect", hint.Text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void EscapeCancels()
    {
        var results = new List<string?>();
        var dialog = PasswordCard.Create("secret.pdf", incorrect: false, results.Add);
        var window = Show(dialog);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal<string?>([null], results);
        window.Close();
    }
}
