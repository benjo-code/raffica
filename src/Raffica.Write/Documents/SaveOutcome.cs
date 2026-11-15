using Raffica.Core.Saving;
using Raffica.Write.Commands;
using Raffica.Write.Docx;

namespace Raffica.Write.Documents;

/// <summary>What a Save or Save as ended in. Everything the user has to know was already shown by the flow (a card or a note); the outcome is for the caller that goes on (the close question, the tests).</summary>
internal abstract record SaveOutcome
{
    private protected SaveOutcome()
    {
    }
}

/// <summary>The document was written over its own file and is clean now.</summary>
/// <param name="Path">The full path written.</param>
internal sealed record Saved(string Path) : SaveOutcome;

/// <summary>The document was written under a new name; the tab belongs to that file now and is clean.</summary>
/// <param name="Path">The full path written.</param>
internal sealed record SavedAs(string Path) : SaveOutcome;

/// <summary>The user said no (Cancel at the picker or at the question about a changed file), or the tab closed meanwhile. Nothing was written and the document is as it was.</summary>
internal sealed record SaveCancelled : SaveOutcome;

/// <summary>The save failed and the error card was shown; the document is dirty as before, its history is intact and the file on disk was not replaced.</summary>
internal sealed record SaveFailed(SaveProblem Problem) : SaveOutcome;

/// <summary>Nothing was started because the command is not possible now; no card is shown (the command was disabled with this reason).</summary>
internal sealed record SaveRefused(SaveRefusal Reason) : SaveOutcome;

internal enum SaveProblemKind
{
    /// <summary>A <see cref="SaveFailureException"/>: see <see cref="SaveProblem.Failure"/>.</summary>
    Failure,

    /// <summary>The save stopped at its own check (<see cref="DocxSaveException"/>): see <see cref="SaveProblem.Check"/>.</summary>
    Check,

    /// <summary>The path chosen for Save as is open in another tab.</summary>
    OpenElsewhere,

    /// <summary>Anything else.</summary>
    Unexpected,
}

/// <summary>Why a save failed, in terms the app words itself (the text of an exception or of the document is never shown).</summary>
internal sealed record SaveProblem(SaveProblemKind Kind, SaveFailureReason Failure = SaveFailureReason.WriteFailed, DocxSaveReason Check = DocxSaveReason.PartsChanged)
{
    public static SaveProblem Of(Exception error) => error switch
    {
        SaveFailureException failure => new SaveProblem(SaveProblemKind.Failure, Failure: failure.Reason),
        DocxSaveException check => new SaveProblem(SaveProblemKind.Check, Check: check.Reason),
        _ => new SaveProblem(SaveProblemKind.Unexpected),
    };

    /// <summary>The words of the problem in the UI language.</summary>
    public string Text => Kind switch
    {
        SaveProblemKind.Failure => WriteStrings.ForSaveFailure(Failure),
        SaveProblemKind.Check => WriteStrings.ForSaveCheck(Check),
        SaveProblemKind.OpenElsewhere => WriteStrings.SaveFailOpenElsewhere,
        _ => WriteStrings.SaveFailUnexpected,
    };
}
