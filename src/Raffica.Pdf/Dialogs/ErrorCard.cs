using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Pdf;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>Tells the user that a file could not be opened: the file name and a short reason (one text per <see cref="PdfOpenError"/>), one OK button.</summary>
internal static class ErrorCard
{
    public static Dialog Create(string fileName, PdfOpenError error, Action done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);

        var name = new TextBlock { Text = fileName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var reason = new TextBlock { Text = PdfStrings.OpenError(error), TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");

        var dialog = new Dialog
        {
            Title = PdfStrings.ErrorTitle,
            Content = new StackPanel { Spacing = 8, Children = { name, reason } },
            Footer = CardFooter.Only(ok),
        };
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
