using Avalonia.Controls;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Documents;

namespace Raffica.Write.Dialogs;

/// <summary>
/// Tells the user that a file could not be opened: the file name and a short reason (one text per <see cref="OpenError"/>), one OK button. For a recovery copy the card says that
/// the copy cannot be read and that a file saved earlier is not affected (the name is the document's, the unreadable file is the copy).
/// </summary>
internal static class OpenErrorCard
{
    public static Dialog Create(string fileName, OpenError error, Action done, bool recoveryCopy = false)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);

        var name = new TextBlock { Text = fileName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var reason = new TextBlock { Text = recoveryCopy ? WriteStrings.RecoveryErrorReason : WriteStrings.ForOpenError(error), TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");

        var dialog = new Dialog
        {
            Title = recoveryCopy ? WriteStrings.RecoveryErrorTitle : WriteStrings.ErrorTitle,
            Content = new StackPanel { Spacing = 8, Children = { name, reason } },
            Footer = CardFooter.Only(ok),
        };
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
