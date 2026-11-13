namespace Raffica.Write.Docx;

/// <summary>The file is not a usable DOCX package: not a package, no main document part, or a model part that is not well-formed or has the wrong root. The message names no document text.</summary>
public sealed class DocxFormatException : Exception
{
    public DocxFormatException()
        : this("The file is not a usable DOCX document.")
    {
    }

    public DocxFormatException(string message)
        : base(message)
    {
    }

    public DocxFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Why a save was stopped by its own check.</summary>
public enum DocxSaveReason
{
    /// <summary>A part the save did not write differs from the file that was opened (or a part appeared or vanished).</summary>
    PartsChanged,

    /// <summary>A part the save wrote could not be read back, or is not what the model produced.</summary>
    ModelNotReadable,
}

/// <summary>The check after writing found a difference; the target was not touched. Carries the part name only, never document text.</summary>
public sealed class DocxSaveException : Exception
{
    public DocxSaveException()
        : this(DocxSaveReason.PartsChanged, string.Empty)
    {
    }

    public DocxSaveException(string message)
        : this(DocxSaveReason.PartsChanged, string.Empty, message)
    {
    }

    public DocxSaveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DocxSaveException(DocxSaveReason reason, string partName, string? message = null)
        : base(message ?? Describe(reason, partName))
    {
        Reason = reason;
        PartName = partName;
    }

    public DocxSaveReason Reason { get; }

    /// <summary>The part name (for example <c>/word/styles.xml</c>) the check failed on; empty when none applies.</summary>
    public string PartName { get; } = string.Empty;

    private static string Describe(DocxSaveReason reason, string partName) => reason == DocxSaveReason.PartsChanged
        ? $"The part '{partName}' would have changed; nothing was saved."
        : $"The part '{partName}' was not written as the model produced it; nothing was saved.";
}
