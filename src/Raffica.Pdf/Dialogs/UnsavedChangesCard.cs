using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Pdf.Documents;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>The question for a document with unsaved changes: Save (primary), Don't save, Cancel.</summary>
internal static class UnsavedChangesCard
{
    public static Dialog Create(string fileName, Action<CloseChoice> chosen)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(chosen);

        var message = new TextBlock { Text = PdfStrings.UnsavedMessageFormat.Replace("{0}", fileName, StringComparison.Ordinal), TextWrapping = TextWrapping.Wrap };
        var save = CreateButton("PART_Save", PdfStrings.UnsavedSave, primary: true);
        var dontSave = CreateButton("PART_DontSave", PdfStrings.UnsavedDontSave, primary: false);
        var cancel = CreateButton("PART_Cancel", ShellStrings.Cancel, primary: false);

        var dialog = new Dialog
        {
            Title = PdfStrings.UnsavedTitle,
            Content = message,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, dontSave, cancel } },
        };
        save.Click += (_, _) => chosen(CloseChoice.Save);
        dontSave.Click += (_, _) => chosen(CloseChoice.DontSave);
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
