using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>The question for the password of a file: a masked field (focused), OK (primary) and Cancel; Enter confirms, Esc cancels.</summary>
internal static class PasswordCard
{
    /// <param name="fileName">The name of the file (no path).</param>
    /// <param name="incorrect">True when the last password was wrong: the card says so.</param>
    /// <param name="done">The password, or null when the user canceled.</param>
    public static Dialog Create(string fileName, bool incorrect, Action<string?> done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);

        var message = new TextBlock { Text = PdfStrings.PasswordMessageFormat.Replace("{0}", fileName, StringComparison.Ordinal), TextWrapping = TextWrapping.Wrap };
        var field = new TextBox { Name = "PART_Password", PasswordChar = '•', MinWidth = 280 };
        AutomationProperties.SetName(field, PdfStrings.PasswordLabel);
        var hint = new TextBlock { Name = "PART_Incorrect", Text = PdfStrings.PasswordIncorrect, IsVisible = incorrect, TextWrapping = TextWrapping.Wrap };
        ValidationHint.Mark(hint);

        var ok = CreateButton("PART_Ok", ShellStrings.Ok, primary: true);
        var cancel = CreateButton("PART_Cancel", ShellStrings.Cancel, primary: false);

        var dialog = new Dialog
        {
            Title = PdfStrings.PasswordTitle,
            Content = new StackPanel { Spacing = 12, Children = { message, field, hint } },
            Footer = CardFooter.Build(ok, null, cancel),
        };
        ok.Click += (_, _) => done(field.Text ?? string.Empty);
        cancel.Click += (_, _) => done(null);
        dialog.CancelRequested += (_, _) => done(null);
        field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                done(field.Text ?? string.Empty);
            }
        };
        return dialog;
    }

    private static Button CreateButton(string name, string text, bool primary)
    {
        var button = new Button { Name = name, Content = text };
        button.Classes.Add(primary ? "primary" : "boxed");
        return button;
    }
}
