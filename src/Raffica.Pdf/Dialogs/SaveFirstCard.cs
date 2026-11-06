using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The question when marks or form values are unsaved and the user applies page changes: "Save first" (the default) saves them into the file with
/// the normal incremental save, then the page changes are applied; "Back" leaves everything as it is (the answer is false, also for Esc and the
/// close button). The rewrite takes its pages from the file, so the marks have to be in it. When the user asked for Save as (to keep the open file as
/// it is), the text names the open file and says plainly that it is changed by the save.
/// </summary>
internal static class SaveFirstCard
{
    /// <param name="fileName">The open file.</param>
    /// <param name="done">Gets the answer: true for "Save first".</param>
    /// <param name="saveAs">Whether the apply was asked as Save as (the open file is changed although the pages go to a new file).</param>
    /// <param name="redact">Whether the question is asked before redactions are applied (its words say redactions, not page changes).</param>
    public static Dialog Create(string fileName, Action<bool> done, bool saveAs = false, bool redact = false)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);

        var message = new TextBlock { Name = "PART_Message", Text = PdfStrings.Fill(redact ? (saveAs ? PdfStrings.RedactSaveFirstSaveAsMessage : PdfStrings.RedactSaveFirstMessage) : (saveAs ? PdfStrings.SaveFirstSaveAsMessage : PdfStrings.SaveFirstMessage), fileName), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = new Button { Name = "PART_SaveFirst", Content = PdfStrings.SaveFirstSave };
        save.Classes.Add("primary");
        var back = new Button { Name = "PART_Back", Content = PdfStrings.ApplyBack };
        back.Classes.Add("boxed");

        var dialog = new Dialog
        {
            Title = PdfStrings.SaveFirstTitle,
            Content = message,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, back } },
        };
        var finished = false;
        void Finish(bool answer)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            done(answer);
        }

        save.Click += (_, _) => Finish(true);
        back.Click += (_, _) => Finish(false);
        dialog.CancelRequested += (_, _) => Finish(false);
        return dialog;
    }
}
