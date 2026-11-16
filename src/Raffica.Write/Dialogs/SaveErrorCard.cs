using Avalonia.Controls;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Documents;

namespace Raffica.Write.Dialogs;

/// <summary>Tells the user that a save failed: the file name, what went wrong and what can be done (one text per reason), and that the document is still open and the file on disk was not replaced. One OK button.</summary>
internal static class SaveErrorCard
{
    public static Dialog Create(string fileName, SaveProblem problem, Action done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(done);

        var name = new TextBlock { Text = fileName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var reason = new TextBlock { Name = "PART_Message", Text = problem.Text, TextWrapping = TextWrapping.Wrap };
        var kept = new TextBlock { Name = "PART_Kept", Text = WriteStrings.SaveErrorKept, TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");

        var dialog = new Dialog
        {
            Title = WriteStrings.SaveErrorTitle,
            Content = new StackPanel { Spacing = 8, Children = { name, reason, kept } },
            Footer = CardFooter.Only(ok),
        };
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
