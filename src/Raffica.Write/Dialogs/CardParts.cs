using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Raffica.Shell.Controls;

namespace Raffica.Write.Dialogs;

/// <summary>The small pieces the Layout cards share: a labelled field, a select, the one-answer rule and the Enter key of a text field.</summary>
internal static class CardParts
{
    /// <summary>A label above a field.</summary>
    public static StackPanel Field(string label, Control field) => new() { Spacing = 4, Children = { new TextBlock { Text = label }, field } };

    /// <summary>A select with a name for a screen reader.</summary>
    public static SelectBox Select(string name, string label, IEnumerable<string> items)
    {
        var box = new SelectBox { Name = name, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = items.ToArray() };
        AutomationProperties.SetName(box, label);
        return box;
    }

    /// <summary>A text field with a name for a screen reader.</summary>
    public static TextBox Text(string name, string label, string? text, double minWidth = 160)
    {
        var box = new TextBox { Name = name, Text = text, MinWidth = minWidth, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(box, label);
        return box;
    }

    /// <summary>A text block for a wrong entry: danger colour, an assertive live region, hidden until it is needed.</summary>
    public static TextBlock Hint()
    {
        var hint = new TextBlock { Name = "PART_Hint", IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        ValidationHint.Mark(hint);
        return hint;
    }

    /// <summary>The primary button and the cancel button of a card.</summary>
    public static (Button Ok, Button Cancel) Buttons(string okText, string cancelText)
    {
        var ok = new Button { Name = "PART_Ok", Content = okText };
        ok.Classes.Add("primary");
        var cancel = new Button { Name = "PART_Cancel", Content = cancelText };
        cancel.Classes.Add("boxed");
        return (ok, cancel);
    }

    /// <summary>Wraps <paramref name="done"/> so that the first answer counts and the rest are ignored.</summary>
    public static Action<T> Once<T>(Action<T> done)
    {
        var finished = false;
        return answer =>
        {
            if (!finished)
            {
                finished = true;
                done(answer);
            }
        };
    }

    /// <summary>Enter in one of the text fields confirms.</summary>
    public static void ConfirmOnEnter(Action confirm, IEnumerable<TextBox> fields)
    {
        void OnKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                confirm();
            }
        }

        foreach (var field in fields)
        {
            field.KeyDown += OnKey;
        }
    }
}
