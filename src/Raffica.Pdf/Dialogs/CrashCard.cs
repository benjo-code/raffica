using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>The answer to an unexpected error: what to do (save under a new name, restart) and where the details are, with a button for the folder of the log.</summary>
internal static class CrashCard
{
    public static Dialog Create(Action openFolder, Action done)
    {
        ArgumentNullException.ThrowIfNull(openFolder);
        ArgumentNullException.ThrowIfNull(done);

        var text = new TextBlock { Name = "PART_Message", Text = PdfStrings.CrashMessage, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var folder = new Button { Name = "PART_OpenFolder", Content = PdfStrings.CrashOpenFolder };
        var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
        ok.Classes.Add("primary");

        var dialog = new Dialog
        {
            Title = PdfStrings.CrashTitle,
            Content = new StackPanel { Spacing = 8, Children = { text } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { folder, ok } },
        };
        folder.Click += (_, _) => openFolder();
        ok.Click += (_, _) => done();
        dialog.CancelRequested += (_, _) => done();
        return dialog;
    }
}
