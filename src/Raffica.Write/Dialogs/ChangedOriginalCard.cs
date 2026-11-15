using Avalonia.Controls;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Write.Dialogs;

/// <summary>What the user chose when another file stands where the opened one was.</summary>
internal enum ChangedChoice
{
    /// <summary>Do not save (Cancel, Esc, the close button).</summary>
    Cancel,

    /// <summary>Save a copy somewhere else.</summary>
    SaveAs,

    /// <summary>Replace the other file with this document.</summary>
    Overwrite,
}

/// <summary>
/// The question when the file of the document was changed or replaced since it was opened: Save as (the default), Overwrite or Cancel. Overwrite is never the default
/// and Esc is Cancel.
/// </summary>
internal static class ChangedOriginalCard
{
    public static Dialog Create(string fileName, Action<ChangedChoice> done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);

        var finished = false;
        void Finish(ChangedChoice choice)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            done(choice);
        }

        var message = new TextBlock { Name = "PART_Message", Text = WriteStrings.ChangedMessageFormat.Replace("{0}", fileName, StringComparison.Ordinal), TextWrapping = TextWrapping.Wrap };
        var saveAs = new Button { Name = "PART_SaveAs", Content = WriteStrings.CmdSaveAs };
        saveAs.Classes.Add("primary");
        var overwrite = new Button { Name = "PART_Overwrite", Content = WriteStrings.ChangedOverwrite };
        overwrite.Classes.Add("boxed");
        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");

        var dialog = new Dialog
        {
            Title = WriteStrings.ChangedTitle,
            Content = message,
            Footer = CardFooter.Build(saveAs, [overwrite], cancel),
        };
        saveAs.Click += (_, _) => Finish(ChangedChoice.SaveAs);
        overwrite.Click += (_, _) => Finish(ChangedChoice.Overwrite);
        cancel.Click += (_, _) => Finish(ChangedChoice.Cancel);
        dialog.CancelRequested += (_, _) => Finish(ChangedChoice.Cancel);
        return dialog;
    }
}
