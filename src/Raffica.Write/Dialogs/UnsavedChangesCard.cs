using Avalonia.Controls;
using Avalonia.Media;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;
using Raffica.Write.Documents;

namespace Raffica.Write.Dialogs;

/// <summary>The question for a document with unsaved changes: Save (the default), Don't save, Cancel (Esc). Save answers <see cref="CloseChoice.Save"/>.</summary>
internal static class UnsavedChangesCard
{
    public static Dialog Create(string fileName, Action<CloseChoice> chosen)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(chosen);

        var finished = false;
        void Finish(CloseChoice choice)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            chosen(choice);
        }

        var message = new TextBlock { Name = "PART_Message", Text = WriteStrings.UnsavedMessageFormat.Replace("{0}", fileName, StringComparison.Ordinal), TextWrapping = TextWrapping.Wrap };
        var save = new Button { Name = "PART_Save", Content = WriteStrings.CmdSave };
        save.Classes.Add("primary");
        var dontSave = new Button { Name = "PART_DontSave", Content = WriteStrings.UnsavedDontSave };
        dontSave.Classes.Add("boxed");
        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("boxed");

        var dialog = new Dialog
        {
            Title = WriteStrings.UnsavedTitle,
            Content = message,
            Footer = CardFooter.Build(save, [dontSave], cancel),
        };
        save.Click += (_, _) => Finish(CloseChoice.Save);
        dontSave.Click += (_, _) => Finish(CloseChoice.DontSave);
        cancel.Click += (_, _) => Finish(CloseChoice.Cancel);
        dialog.CancelRequested += (_, _) => Finish(CloseChoice.Cancel);
        return dialog;
    }
}
