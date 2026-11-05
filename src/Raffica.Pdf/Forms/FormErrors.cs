using Raffica.Core.Annotations;
using Raffica.Core.Forms;
using Raffica.Core.Pdf;

namespace Raffica.Pdf.Forms;

/// <summary>Why a form value was not written (the card or hint chooses the wording; <see cref="FormEditResult.Refused.Detail"/> names what it needs).</summary>
internal enum FormEditProblem
{
    /// <summary>The text has characters the field cannot show; the detail names them.</summary>
    UnsupportedCharacters,

    /// <summary>The text is longer than the field takes; the detail is the most characters.</summary>
    TooLong,

    /// <summary>A line break in a one-line field.</summary>
    LineBreakNotAllowed,

    /// <summary>A control character; the detail names it.</summary>
    ControlCharacter,

    /// <summary>The form engine did not keep the value; the field is verified to be as before.</summary>
    NotKept,

    /// <summary>The form engine did not keep the value and could not verify the field; its pages are read again, the document may differ from the history.</summary>
    NotKeptNotRestored,

    /// <summary>The field is locked (read-only, a signature or another kind Raffica does not write).</summary>
    Locked,

    /// <summary>The document has no such field any more.</summary>
    NotFound,

    /// <summary>The step does not fit into the history any more; save first.</summary>
    HistoryFull,

    /// <summary>The value is refused for another reason.</summary>
    Invalid,
}

/// <summary>The outcome of <see cref="FormSession.SetValueAsync"/>: <see cref="Applied"/>, <see cref="Unchanged"/> or <see cref="Refused"/>.</summary>
internal abstract record FormEditResult
{
    private FormEditResult()
    {
    }

    /// <summary>The value was written as one undo step.</summary>
    public sealed record Applied : FormEditResult;

    /// <summary>The value is the one the field has: nothing was written and no step was made.</summary>
    public sealed record Unchanged : FormEditResult;

    /// <summary>Nothing was written (or, with <see cref="FormEditProblem.NotKeptNotRestored"/>, not as asked).</summary>
    /// <param name="Problem">Why.</param>
    /// <param name="Detail">What the message names (see <see cref="FormEditProblem"/>); empty when there is nothing to name.</param>
    public sealed record Refused(FormEditProblem Problem, string Detail) : FormEditResult;
}

/// <summary>The mapping of the exceptions of the form write and of the text rules to <see cref="FormEditProblem"/>.</summary>
internal static class FormErrors
{
    public static FormEditResult.Refused FromCheck(FormValueCheck check) => new(
        check.Problem switch
        {
            FormValueProblem.UnsupportedCharacters => FormEditProblem.UnsupportedCharacters,
            FormValueProblem.TooLong => FormEditProblem.TooLong,
            FormValueProblem.LineBreakNotAllowed => FormEditProblem.LineBreakNotAllowed,
            FormValueProblem.ControlCharacter => FormEditProblem.ControlCharacter,
            _ => FormEditProblem.Invalid,
        },
        check.Detail ?? string.Empty);

    /// <summary>Maps an exception of <c>AnnotationEditor.SetFormValueAsync</c>, or <see langword="null"/> when it is not one of the expected ones.</summary>
    public static FormEditResult.Refused? FromException(Exception exception) => exception switch
    {
        AnnotationHistoryFullException => new(FormEditProblem.HistoryFull, string.Empty),
        FormWriteException { Restored: true } => new(FormEditProblem.NotKept, string.Empty),
        FormWriteException => new(FormEditProblem.NotKeptNotRestored, string.Empty),
        FormFieldNotFoundException => new(FormEditProblem.NotFound, string.Empty),
        InvalidOperationException => new(FormEditProblem.Locked, string.Empty),
        ArgumentException => new(FormEditProblem.Invalid, string.Empty),
        _ => null,
    };
}
