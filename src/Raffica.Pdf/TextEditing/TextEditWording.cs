using System.Globalization;
using Raffica.Core.TextEdit;

namespace Raffica.Pdf.TextEditing;

/// <summary>
/// The words of the text editing in the UI language. Core's English sentences (the notes of a decision, the reasons of a lock or a refusal) are never shown:
/// every <see cref="TextEditLockReason"/>, <see cref="TextEditRejection"/> and <see cref="TextEditOutcome"/> that reaches the user has a text of its own here.
/// The texts never claim that the old text is gone or that the result is right; the characters of a refusal are the user's own typed text and are named
/// where the user needs them (the hint bar), never written to a log or to an automation name.
/// </summary>
internal static class TextEditWording
{
    /// <summary>The most characters a note names; the rest is cut off with an ellipsis.</summary>
    public const int MaxNamedCharacters = 8;

    /// <summary>The note after a line was written.</summary>
    public static string Done => PdfStrings.TextEditNoteDone;

    /// <summary>The note for a text that is the old one.</summary>
    public static string Unchanged => PdfStrings.TextEditNoteUnchanged;

    /// <summary>The note when the page changed after the line was read.</summary>
    public static string Stale => PdfStrings.TextEditNoteStale;

    /// <summary>The note when writing failed and the page may have changed.</summary>
    public static string WriteFailed => PdfStrings.TextEditNoteWriteFailed;

    /// <summary>The note for a history without room.</summary>
    public static string HistoryFull => PdfStrings.TextEditNoteHistoryFull;

    /// <summary>The note for a failure nothing more is known about.</summary>
    public static string Failed => PdfStrings.TextEditNoteFailed;

    /// <summary>The note for a page that cannot be read.</summary>
    public static string PageUnreadable => PdfStrings.TextEditNotePageUnreadable;

    /// <summary>The note about the old text in earlier versions of the file (after the first save of edited text).</summary>
    public static string OldText => PdfStrings.TextEditOldTextNote;

    /// <summary>Why a run cannot be edited.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The reason has no text.</exception>
    public static string Lock(TextEditLockReason reason) => reason switch
    {
        TextEditLockReason.InvisibleText => PdfStrings.TextEditLockInvisibleText,
        TextEditLockReason.Type3Font => PdfStrings.TextEditLockType3Font,
        TextEditLockReason.VerticalWriting => PdfStrings.TextEditLockVerticalWriting,
        TextEditLockReason.UnsupportedRenderMode => PdfStrings.TextEditLockUnsupportedRenderMode,
        TextEditLockReason.FormXObject => PdfStrings.TextEditLockFormXObject,
        TextEditLockReason.MarkedContent => PdfStrings.TextEditLockMarkedContent,
        TextEditLockReason.NotSavable => PdfStrings.TextEditLockNotSavable,
        TextEditLockReason.PageNotRewritable => PdfStrings.TextEditLockPageNotRewritable,
        TextEditLockReason.Ligature => PdfStrings.TextEditLockLigature,
        TextEditLockReason.TooManyRuns => PdfStrings.Fill(PdfStrings.TextEditLockTooManyRunsFormat, TextEditLimits.MaxRunsPerPage),
        TextEditLockReason.RunTooLong => PdfStrings.Fill(PdfStrings.TextEditLockRunTooLongFormat, TextEditLimits.MaxCharsPerRun),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "This lock has no text."),
    };

    /// <summary>
    /// Why a new text is refused as a whole. <paramref name="unsupported"/> are the characters that neither font has (the user's own typed text, named up to
    /// <see cref="MaxNamedCharacters"/>; a control character by its code).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The rejection has no text (<see cref="TextEditRejection.None"/> means the text is not refused).</exception>
    public static string Rejection(TextEditRejection rejection, IReadOnlyList<string>? unsupported = null) => rejection switch
    {
        TextEditRejection.TooLong => PdfStrings.Fill(PdfStrings.TextEditRejectTooLongFormat, TextEditLimits.MaxCharsPerRun),
        TextEditRejection.UnsupportedCharacters => unsupported is { Count: > 0 }
            ? PdfStrings.Fill(PdfStrings.TextEditRejectUnsupportedFormat, Name(unsupported))
            : PdfStrings.TextEditRejectUnsupportedNone,
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "This rejection has no text."),
    };

    /// <summary>The note for the decision about the text typed: the missing characters, the whole line in the replacement font, an empty line, a refusal, what the longer line may run into. <see langword="null"/> when there is nothing to say.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The outcome has no text.</exception>
    public static string? Note(TextEditPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var decision = preview.Decision;
        var main = decision.Outcome switch
        {
            TextEditOutcome.Unchanged => Unchanged,
            TextEditOutcome.KeepFont => null,
            TextEditOutcome.SubstituteMissing => PdfStrings.Fill(PdfStrings.TextEditNoteMissingFormat, Name(decision.Missing)),
            TextEditOutcome.WholeRunInGeist => PdfStrings.TextEditNoteWholeRun,
            TextEditOutcome.Remove => PdfStrings.TextEditNoteEmpty,
            TextEditOutcome.Rejected => Rejection(decision.Rejection == TextEditRejection.None ? TextEditRejection.UnsupportedCharacters : decision.Rejection, decision.Unsupported),
            _ => throw new ArgumentOutOfRangeException(nameof(preview), decision.Outcome, "This outcome has no text."),
        };
        var lines = new List<string>();
        if (main is not null)
        {
            lines.Add(main);
        }

        if (preview.Overflow.HasFlag(TextEditOverflow.OverlapsText))
        {
            lines.Add(PdfStrings.TextEditNoteOverlap);
        }

        if (preview.Overflow.HasFlag(TextEditOverflow.OutsidePage))
        {
            lines.Add(PdfStrings.TextEditNoteOutsidePage);
        }

        return lines.Count == 0 ? null : string.Join(' ', lines);
    }

    /// <summary>The automation name of the outline of a run: the line and the page, never the text of the line.</summary>
    /// <param name="line">The 1-based number of the line on its page.</param>
    /// <param name="page">The 1-based page.</param>
    /// <param name="locked">Whether the run is locked.</param>
    public static string RunName(int line, int page, bool locked) =>
        PdfStrings.Fill(locked ? PdfStrings.TextEditLockedRunNameFormat : PdfStrings.TextEditRunNameFormat, line, page);

    // The characters the note is about (the user's own typed text), at most eight; a control character cannot be seen and is named by its code.
    private static string Name(IReadOnlyList<string> characters)
    {
        var shown = characters.Take(MaxNamedCharacters).Select(Visible);
        var text = string.Join(", ", shown);
        return characters.Count > MaxNamedCharacters ? text + " …" : text;
    }

    private static string Visible(string character) =>
        character.Length > 0 && char.IsControl(character[0]) ? string.Create(CultureInfo.InvariantCulture, $"U+{(int)character[0]:X4}") : character;
}
