using Avalonia.Controls;
using Avalonia.Layout;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The question after a cancel with finished pages ("Keep the finished pages?"): "Discard" is the default (primary, and what Esc and the close button answer),
/// "Keep N pages" writes the finished pages. The message names the document, so a card shown after a tab switch says which file it concerns. The answer is true for Keep.
/// </summary>
internal static class OcrKeepPagesCard
{
    /// <param name="pages">How many pages were finished before the cancel.</param>
    /// <param name="fileName">The title of the document the run belongs to (no path).</param>
    /// <param name="done">Gets true for "Keep N pages", false for Discard, Esc and the close button.</param>
    public static Dialog CreateKeep(int pages, string fileName, Action<bool> done)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);
        var message = new TextBlock { Name = "PART_Message", Text = PdfStrings.Fill(PdfStrings.OcrKeepMessageFormat, pages, fileName), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var discard = new Button { Name = "PART_Discard", Content = PdfStrings.OcrKeepDiscard };
        discard.Classes.Add("primary");
        var keep = new Button { Name = "PART_Keep", Content = PdfStrings.Fill(PdfStrings.OcrKeepKeepFormat, pages) };
        keep.Classes.Add("boxed");
        return Build(PdfStrings.OcrKeepTitle, message, [discard, keep], keep, discard, done);
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
