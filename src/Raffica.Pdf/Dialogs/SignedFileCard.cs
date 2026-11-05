using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Saving;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>What the user chose on the card about a signed file.</summary>
internal enum SignedChoice
{
    /// <summary>Do not save (Cancel, Esc, the close button).</summary>
    Cancel,

    /// <summary>Save a copy instead of the signed file.</summary>
    SaveAs,

    /// <summary>Save into the signed file although its signature does not allow it.</summary>
    SaveAnyway,

    /// <summary>The note was read; go on with the save.</summary>
    Continue,
}

/// <summary>
/// The card shown before a signed file is saved. <see cref="SignatureAdvice.WouldInvalidate"/> (DocMDP 1 or 2) warns that programs such as
/// Acrobat will show the signature as invalid afterwards: Cancel is the default, Save as is safe, Save anyway is locked for 400 ms.
/// <see cref="SignatureAdvice.AppendedOnly"/> (DocMDP 3 or none) is only a note with OK; it has its own card rather than <see cref="NoticeCard"/>
/// because Esc must cancel the save there, and <see cref="NoticeCard"/> treats Esc like OK. The card never calls the signature valid. With
/// <c>onlyFormChanges</c> (the editor holds form values only) the warning says so: the level of the signature may allow filling, but the app
/// cannot check whether the signature stays intact.
/// </summary>
internal static class SignedFileCard
{
    public static Dialog Create(string fileName, SignatureAdvice advice, bool onlyFormChanges, Action<SignedChoice> done, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(done);
        if (advice is not (SignatureAdvice.WouldInvalidate or SignatureAdvice.AppendedOnly))
        {
            throw new ArgumentOutOfRangeException(nameof(advice), advice, "Only a signed file gets this card.");
        }

        var finished = false;
        void Finish(SignedChoice choice)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            done(choice);
        }

        var name = new TextBlock { Text = fileName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        if (advice == SignatureAdvice.AppendedOnly)
        {
            var note = new TextBlock { Name = "PART_Message", Text = PdfStrings.SignedNoteText, TextWrapping = TextWrapping.Wrap };
            var ok = new Button { Name = "PART_Ok", Content = ShellStrings.Ok };
            ok.Classes.Add("primary");
            var noteDialog = new Dialog
            {
                Title = PdfStrings.SignedNoteTitle,
                Content = new StackPanel { Spacing = 8, Children = { name, note } },
                Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok } },
            };
            ok.Click += (_, _) => Finish(SignedChoice.Continue);
            noteDialog.CancelRequested += (_, _) => Finish(SignedChoice.Cancel);
            return noteDialog;
        }

        var message = new TextBlock { Name = "PART_Message", Text = onlyFormChanges ? PdfStrings.SignedFormsText : PdfStrings.SignedWarnText, TextWrapping = TextWrapping.Wrap };
        var cancel = new Button { Name = "PART_Cancel", Content = ShellStrings.Cancel };
        cancel.Classes.Add("primary");
        var saveAs = new Button { Name = "PART_SaveAs", Content = PdfStrings.CmdSaveAs };
        saveAs.Classes.Add("boxed");
        var anyway = new Button { Name = "PART_SaveAnyway", Content = PdfStrings.SignedSaveAnyway };
        anyway.Classes.Add("boxed");

        var dialog = new Dialog
        {
            Title = onlyFormChanges ? PdfStrings.SignedFormsTitle : PdfStrings.SignedWarnTitle,
            Content = new StackPanel { Spacing = 8, Children = { name, message } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, saveAs, anyway } },
        };
        var arming = new ArmingLock(time, dialog, anyway);
        cancel.Click += (_, _) => Finish(SignedChoice.Cancel);
        saveAs.Click += (_, _) => Finish(SignedChoice.SaveAs);
        anyway.Click += (_, _) =>
        {
            if (arming.IsArmed)
            {
                Finish(SignedChoice.SaveAnyway);
            }
        };
        dialog.CancelRequested += (_, _) => Finish(SignedChoice.Cancel);
        return dialog;
    }
}
