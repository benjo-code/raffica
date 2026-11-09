using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Saving;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>Tells the user that a save failed: the file name and a short reason that says the original file is unchanged, one OK button.</summary>
internal static class SaveFailedCard
{
    public static Dialog Create(string fileName, SaveFailureReason reason, Action done) =>
        Create(fileName, PdfStrings.SaveFailedTitle, PdfStrings.SaveFailure(reason), done);

    /// <summary>The same card with another title and message (the page apply words its failures itself).</summary>
    public static Dialog Create(string fileName, string title, string message, Action done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(done);

        var name = new TextBlock { Text = fileName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var text = new TextBlock { Name = "PART_Message", Text = message, TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");

        var dialog = new Dialog
        {
            Title = title,
            Content = new StackPanel { Spacing = 8, Children = { name, text } },
            Footer = CardFooter.Only(ok),
        };
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
