namespace Raffica.Pdf.Redacting;

/// <summary>Why the session refused an operation; the plan and the history stay as they were.</summary>
internal enum RedactProblem
{
    /// <summary>The session is not active (before Enter, after End, or the mode ended while the text was being read).</summary>
    NotActive,

    /// <summary>Nothing is selected that could be marked or changed.</summary>
    NothingSelected,

    /// <summary>The selected text cannot become a mark; nothing at all is marked (all or nothing).</summary>
    TextNotMarkable,

    /// <summary>The plan would hold more than <see cref="Raffica.Core.Redaction.RedactionLimits.MaxMarks"/> marks, or a mark more areas than allowed.</summary>
    TooManyMarks,

    /// <summary>Only area marks move and resize; text and hit marks are deleted and marked again.</summary>
    NotAnArea,

    /// <summary>The page or the rectangle is out of range or has no size.</summary>
    OutOfRange,

    /// <summary>No mark with this id is in the plan.</summary>
    UnknownMark,

    /// <summary>The text of a page could not be read, so the selection cannot be marked.</summary>
    TextUnavailable,

    /// <summary>One mark would need more lines than <see cref="Raffica.Core.Redaction.RedactionLimits.MaxAreasPerMark"/>; select less text.</summary>
    TooManyLines,

    /// <summary>The previous marking of the selection is still running.</summary>
    Busy,

    /// <summary>The search did not cover everything (a page could not be read, or the limit was reached) and the user has not accepted that yet.</summary>
    SearchIncomplete,

    /// <summary>The search has no hit that could become a mark.</summary>
    NothingToMark,

    /// <summary>A search is still running, so its hits are not final.</summary>
    SearchRunning,

    /// <summary>The fill (or text colour) written in the colour field is not <c>#RRGGBB</c>.</summary>
    FillNotAColour,

    /// <summary>The overlay text is longer than <see cref="Raffica.Core.Redaction.RedactionLimits.MaxOverlayTextLength"/> characters.</summary>
    OverlayTooLong,

    /// <summary>The overlay text has a character the text box font cannot draw; <see cref="RedactResult.CharacterClass"/> says which kind.</summary>
    OverlayCharactersRefused,

    /// <summary>The overlay text of a mark contains a text that is marked, which would put it back on the page.</summary>
    OverlayRepeatsTerm,

    /// <summary>The resolution of the pages turned into images is none of the three offered.</summary>
    DpiNotOffered,

    /// <summary>The plan would turn more pages into images than <see cref="Raffica.Core.Redaction.RedactionLimits.MaxRasterPages"/>.</summary>
    TooManyRasterPages,
}

/// <summary>
/// The outcome of an operation: done (then <see cref="MarkId"/> names the mark an add made), or refused with the reason. For
/// <see cref="RedactProblem.OverlayCharactersRefused"/> the <see cref="CharacterClass"/> names the kind of the first character that cannot be drawn; for
/// <see cref="RedactProblem.OverlayRepeatsTerm"/> the <see cref="Pages"/> (0-based, ascending) are the pages of the marks whose text repeats a marked text.
/// </summary>
internal readonly record struct RedactResult(RedactProblem? Problem, Guid? MarkId = null, OverlayCharacterClass? CharacterClass = null, IReadOnlyList<int>? Pages = null)
{
    public static RedactResult Done => default;

    public bool IsDone => Problem is null;

    public static RedactResult Refused(RedactProblem problem) => new(problem);

    public static RedactResult Added(Guid id) => new(null, id);
}

/// <summary>The marking tool of the redact mode: a drag on the page makes an area, or the text selection stays with the text (and is marked as a whole).</summary>
internal enum RedactTool
{
    Area,
    Text,
}
