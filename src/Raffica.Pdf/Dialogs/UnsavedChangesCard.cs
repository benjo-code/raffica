using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Pdf.Documents;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The question for a document with unsaved changes: Save (primary), Don't save, Cancel. For a tab that cannot be saved (<c>locked</c>) the
/// card offers only "Close and discard changes" and Cancel (the default), and answers <see cref="CloseChoice.DontSave"/> or
/// <see cref="CloseChoice.Cancel"/>. Don't save and Close and discard changes are disabled for the first 400 ms after the card was made.
/// </summary>
internal static class UnsavedChangesCard
{
    public static Dialog Create(string fileName, Action<CloseChoice> chosen, bool locked = false, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(chosen);

        var format = locked ? PdfStrings.UnsavedLockedMessageFormat : PdfStrings.UnsavedMessageFormat;
        var message = new TextBlock { Text = format.Replace("{0}", fileName, StringComparison.Ordinal), TextWrapping = TextWrapping.Wrap };
        var cancel = CreateButton("PART_Cancel", ShellStrings.Cancel, primary: locked);

        Dialog dialog;
        Button dangerous;
        if (locked)
        {
            dangerous = CreateButton("PART_Discard", PdfStrings.UnsavedDiscard, primary: false);
            dialog = new Dialog
            {
                Title = PdfStrings.UnsavedLockedTitle,
                Content = message,
                Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, dangerous } },
            };
        }
        else
        {
            var save = CreateButton("PART_Save", PdfStrings.UnsavedSave, primary: true);
            dangerous = CreateButton("PART_DontSave", PdfStrings.UnsavedDontSave, primary: false);
            dialog = new Dialog
            {
                Title = PdfStrings.UnsavedTitle,
                Content = message,
                Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, dangerous, cancel } },
            };
            save.Click += (_, _) => chosen(CloseChoice.Save);
        }

        var arming = new ArmingLock(time, dialog, dangerous);

        // The locked card also checks the clock on the click. Don't save of the normal card relies on its disabled button, which is what a
        // user can reach; the window tests raise its Click directly and have no clock to move.
        dangerous.Click += (_, _) =>
        {
            if (!locked || arming.IsArmed)
            {
                chosen(CloseChoice.DontSave);
            }
        };
        cancel.Click += (_, _) => chosen(CloseChoice.Cancel);
        dialog.CancelRequested += (_, _) => chosen(CloseChoice.Cancel);
        return dialog;
    }

    private static Button CreateButton(string name, string text, bool primary)
    {
        var button = new Button { Name = name, Content = text };
        button.Classes.Add(primary ? "primary" : "boxed");
        return button;
    }
}
