using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The two questions a text recognition can ask. After a cancel with finished pages ("Keep the finished pages?"): "Discard" is the default (primary, and what Esc
/// and the close button answer), "Keep N pages" writes the finished pages. When the recognized text does not fit into the undo history next to unsaved
/// changes ("Save the changes first"): "Save first" (primary) saves the changes and adds the text, "Back" leaves the file as it is. Both answer through one bool: true is
/// the second button (keep, save first).
/// </summary>
internal static class OcrKeepPagesCard
{
    /// <param name="pages">How many pages were finished before the cancel.</param>
    /// <param name="done">Gets true for "Keep N pages", false for Discard, Esc and the close button.</param>
    public static Dialog CreateKeep(int pages, Action<bool> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var message = new TextBlock { Name = "PART_Message", Text = PdfStrings.Fill(PdfStrings.OcrKeepMessageFormat, pages), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var discard = new Button { Name = "PART_Discard", Content = PdfStrings.OcrKeepDiscard };
        discard.Classes.Add("primary");
        var keep = new Button { Name = "PART_Keep", Content = PdfStrings.Fill(PdfStrings.OcrKeepKeepFormat, pages) };
        keep.Classes.Add("boxed");
        return Build(PdfStrings.OcrKeepTitle, message, [discard, keep], keep, discard, done);
    }

    /// <param name="fileName">The open file.</param>
    /// <param name="done">Gets true for "Save first", false for Back, Esc and the close button.</param>
    public static Dialog CreateHistoryFull(string fileName, Action<bool> done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);
        var message = new TextBlock { Name = "PART_Message", Text = PdfStrings.Fill(PdfStrings.OcrHistoryFullMessageFormat, fileName), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = new Button { Name = "PART_SaveFirst", Content = PdfStrings.SaveFirstSave };
        save.Classes.Add("primary");
        var back = new Button { Name = "PART_Back", Content = PdfStrings.ApplyBack };
        back.Classes.Add("boxed");
        return Build(PdfStrings.OcrHistoryFullTitle, message, [save, back], save, back, done);
    }

    private static Dialog Build(string title, TextBlock message, Button[] buttons, Button yes, Button no, Action<bool> done)
    {
        var dialog = new Dialog
        {
            Title = title,
            Content = message,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { buttons[0], buttons[1] } },
        };
        var finished = false;
        void Finish(bool answer)
        {
            if (!finished)
            {
                finished = true;
                done(answer);
            }
        }

        yes.Click += (_, _) => Finish(true);
        no.Click += (_, _) => Finish(false);
        dialog.CancelRequested += (_, _) => Finish(false);
        return dialog;
    }
}
