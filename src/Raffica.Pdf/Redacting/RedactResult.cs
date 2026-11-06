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
}

/// <summary>The outcome of an operation: done (then <see cref="MarkId"/> names the mark an add made), or refused with the reason.</summary>
internal readonly record struct RedactResult(RedactProblem? Problem, Guid? MarkId = null)
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
