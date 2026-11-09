using Raffica.Ocr;

namespace Raffica.Core.Ocr;

/// <summary>Why a whole text recognition run could not go on, as the interface words it (one text per member; the English messages of the exceptions are for logs).</summary>
public enum OcrFailureKind
{
    /// <summary>The native OCR library is not next to the application.</summary>
    LibraryMissing,

    /// <summary>The native library is there but cannot be loaded.</summary>
    LibraryUnusable,

    /// <summary>The choice of languages is not usable.</summary>
    LanguageInvalid,

    /// <summary>A language has no file.</summary>
    LanguageMissing,

    /// <summary>A language file is damaged or made for another version.</summary>
    DataCorrupt,

    /// <summary>Anything else the engine refused.</summary>
    Failed,
}

/// <summary>A failure of the whole run: the kind and, for a language without a file, the names that miss. Lets an app tell the user without depending on the engine library.</summary>
/// <param name="Kind">Why.</param>
/// <param name="Languages">The languages that have no file (only for <see cref="OcrFailureKind.LanguageMissing"/>, and only when known), else empty.</param>
public sealed record OcrFailure(OcrFailureKind Kind, IReadOnlyList<string> Languages)
{
    /// <summary>The failure an exception of the engine or of the language files stands for, or <see langword="null"/> for any other exception.</summary>
    /// <param name="exception">What was thrown while the engines were made or the run went on.</param>
    public static OcrFailure? From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            TessdataMissingException missing => new OcrFailure(OcrFailureKind.LanguageMissing, missing.Missing),
            OcrEngineException engine => new OcrFailure(
                engine.Error switch
                {
                    OcrEngineError.LibraryMissing => OcrFailureKind.LibraryMissing,
                    OcrEngineError.LibraryUnusable => OcrFailureKind.LibraryUnusable,
                    OcrEngineError.LanguageInvalid => OcrFailureKind.LanguageInvalid,
                    OcrEngineError.LanguageMissing => OcrFailureKind.LanguageMissing,
                    OcrEngineError.DataCorrupt => OcrFailureKind.DataCorrupt,
                    _ => OcrFailureKind.Failed,
                },
                []),
            _ => null,
        };
    }
}
