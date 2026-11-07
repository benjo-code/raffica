using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The confirmation when the file that Save as returns is the open file itself: the redacted file would replace the original, so the same armed choice as
/// "Save in the original file" stands between the user and the write (locked for 400 ms, never the default). "Back" (also Esc) leaves everything as it is.
/// </summary>
internal static class RedactOriginalCard
{
    /// <param name="fileName">The name of the open file.</param>
    /// <param name="done">True when the user confirmed replacing the original.</param>
    /// <param name="time">The clock of the arming delay (tests move it).</param>
    public static Dialog Create(string fileName, Action<bool> done, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);

        var finished = false;
        void Finish(bool replace)
        {
            if (!finished)
            {
                finished = true;
                done(replace);
            }
        }

        var message = new TextBlock { Name = "PART_Message", Text = PdfStrings.Fill(PdfStrings.RedactOriginalMessageFormat, fileName), TextWrapping = TextWrapping.Wrap };
        var original = new Button { Name = "PART_Original", Content = PdfStrings.ApplyOriginal };
        original.Classes.Add("boxed");
        var back = new Button { Name = "PART_Back", Content = PdfStrings.ApplyBack };
        back.Classes.Add("primary");
        var dialog = new Dialog
        {
            Title = PdfStrings.RedactOriginalTitle,
            Content = message,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { back, original } },
        };
        var arming = new ArmingLock(time, dialog, original);
        original.Click += (_, _) =>
        {
            if (arming.IsArmed)
            {
                Finish(true);
            }
        };
        back.Click += (_, _) => Finish(false);
        dialog.CancelRequested += (_, _) => Finish(false);
        return dialog;
    }
}
