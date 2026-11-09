using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Printing;

/// <summary>A short statement about printing (a refusal, an error, the pages that failed) with one OK button; Enter and Esc close it.</summary>
internal static class PrintMessageCard
{
    public static Dialog Create(string text, Action done)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(done);

        var message = new TextBlock { Name = "PART_Message", Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 };
        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");
        var dialog = new Dialog
        {
            Title = PdfStrings.PrintCardTitle,
            Content = message,
            Footer = CardFooter.Only(ok),
        };
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
