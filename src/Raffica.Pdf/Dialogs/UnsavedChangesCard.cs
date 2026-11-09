using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Pdf.Documents;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Dialogs;

/// <summary>
/// The question for a document with unsaved changes: Save (primary), Don't save, Cancel. For a tab that cannot be saved (<see cref="UnsavedKind.Locked"/>) the
/// card offers only "Close and discard changes" and Cancel (the default), and answers <see cref="CloseChoice.DontSave"/> or
/// <see cref="CloseChoice.Cancel"/>. For an open page plan (<see cref="UnsavedKind.Plan"/>) the card has its own wording: "Apply…" (answers <see cref="CloseChoice.Save"/>), "Discard"
/// (<see cref="CloseChoice.DontSave"/>) and Cancel. Don't save, Discard and Close and discard changes are disabled for the first 400 ms after the card was made.
/// </summary>
internal enum UnsavedKind
{
    /// <summary>A document with unsaved changes: Save, Don't save, Cancel.</summary>
    Save,

    /// <summary>A tab that cannot be saved: Close and discard changes, Cancel.</summary>
    Locked,

    /// <summary>An open page plan: Apply…, Discard, Cancel.</summary>
    Plan,

    /// <summary>Redactions marked and not applied: Apply…, Discard, Cancel.</summary>
    Redact,

    /// <summary>A text recognition that still runs: Stop recognition, Keep going.</summary>
    Ocr,
}

internal static class UnsavedChangesCard
{
    public static Dialog Create(string fileName, Action<CloseChoice> chosen, UnsavedKind kind = UnsavedKind.Save, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(chosen);

        // The recognition card has a single way forward besides Cancel, like the locked card, and shares its layout.
        var locked = kind is UnsavedKind.Locked or UnsavedKind.Ocr;
        var plan = kind is UnsavedKind.Plan or UnsavedKind.Redact;
        var redact = kind == UnsavedKind.Redact;
        var ocr = kind == UnsavedKind.Ocr;
        var format = ocr ? PdfStrings.UnsavedOcrMessageFormat
            : redact ? PdfStrings.UnsavedRedactMessageFormat
            : plan ? PdfStrings.UnsavedPlanMessageFormat
            : locked ? PdfStrings.UnsavedLockedMessageFormat
            : PdfStrings.UnsavedMessageFormat;
        var message = new TextBlock { Text = format.Replace("{0}", fileName, StringComparison.Ordinal), TextWrapping = TextWrapping.Wrap };
        var cancel = CreateButton("PART_Cancel", ocr ? PdfStrings.UnsavedOcrKeep : ShellStrings.Cancel, primary: locked);

        Dialog dialog;
        Button dangerous;
        if (locked)
        {
            dangerous = CreateButton("PART_Discard", ocr ? PdfStrings.UnsavedOcrStop : PdfStrings.UnsavedDiscard, primary: false);
            dialog = new Dialog
            {
                Title = ocr ? PdfStrings.UnsavedOcrTitle : PdfStrings.UnsavedLockedTitle,
                Content = message,
                Footer = CardFooter.Build(null, [dangerous], cancel),
            };
        }
        else
        {
            var save = CreateButton(plan ? "PART_Apply" : "PART_Save", redact ? PdfStrings.UnsavedRedactApply : plan ? PdfStrings.UnsavedPlanApply : PdfStrings.UnsavedSave, primary: true);
            dangerous = CreateButton(plan ? "PART_Discard" : "PART_DontSave", redact ? PdfStrings.UnsavedRedactDiscard : plan ? PdfStrings.UnsavedPlanDiscard : PdfStrings.UnsavedDontSave, primary: false);
            dialog = new Dialog
            {
                Title = redact ? PdfStrings.UnsavedRedactTitle : plan ? PdfStrings.UnsavedPlanTitle : PdfStrings.UnsavedTitle,
                Content = message,
                Footer = CardFooter.Build(save, [dangerous], cancel),
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
