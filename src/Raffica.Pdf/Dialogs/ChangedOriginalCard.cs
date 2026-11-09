using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

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
/// The question when the file at the path of the document was replaced since it was opened: Save as (the default), Overwrite (locked for
/// 400 ms, never the default) or Cancel.
/// </summary>
internal static class ChangedOriginalCard
{
    public static Dialog Create(string fileName, Action<ChangedChoice> done, TimeProvider? time = null)
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

        var message = new TextBlock { Name = "PART_Message", Text = PdfStrings.ChangedMessageFormat.Replace("{0}", fileName, StringComparison.Ordinal), TextWrapping = TextWrapping.Wrap };
        var saveAs = new Button { Name = "PART_SaveAs", Content = PdfStrings.CmdSaveAs };
        saveAs.Classes.Add("primary");
        var overwrite = new Button { Name = "PART_Overwrite", Content = PdfStrings.ChangedOverwrite };
        overwrite.Classes.Add("boxed");
        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");

        var dialog = new Dialog
        {
            Title = PdfStrings.ChangedTitle,
            Content = message,
            Footer = CardFooter.Build(saveAs, [overwrite], cancel),
        };
        var arming = new ArmingLock(time, dialog, overwrite);
        saveAs.Click += (_, _) => Finish(ChangedChoice.SaveAs);
        overwrite.Click += (_, _) =>
        {
            if (arming.IsArmed)
            {
                Finish(ChangedChoice.Overwrite);
            }
        };
        cancel.Click += (_, _) => Finish(ChangedChoice.Cancel);
        dialog.CancelRequested += (_, _) => Finish(ChangedChoice.Cancel);
        return dialog;
    }
}
