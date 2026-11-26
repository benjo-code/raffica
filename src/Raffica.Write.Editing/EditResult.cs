using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What an editing operation did: where the caret goes and which blocks changed. <paramref name="Changes"/> is empty when the operation
/// found nothing to change (an empty selection, an empty insert); the document revision was then not raised either. <paramref name="Selected"/>
/// is the unit a first Backspace or Delete selected instead of deleting (a field): nothing changed, and deleting that range removes it.
/// <paramref name="Style"/> is the style an operation (or an undo or redo) swapped in the style sheet, with the snapshots before and after; a layout's
/// style snapshot is then stale and its owner must start the layout over. <paramref name="KeepSelection"/> is set by the formatting operations: they change no text, so every offset stays
/// valid and the selection that was there stays as it is (direction and anchor included) instead of collapsing to <paramref name="Caret"/>.
/// <paramref name="Move"/> is set when the operation (or an undo or redo) moved a range of blocks: the one <see cref="BlockChange"/> then spans every block whose place changed, with the same block objects before and after.
/// <paramref name="Sections"/> is the section properties an operation (or an undo or redo) swapped, with the flag of left and right pages; the layout is then started over (<c>DocumentLayout.Resection</c>) instead of being given the block changes.
/// <paramref name="Objects"/> is the notes (later also the pictures) an operation, an undo or a redo added to or removed from the document, together with the block change that holds their references; it needs no attribution (the notes parts are written afresh at every save and copy). The layout is given the block change as usual (<c>DocumentLayout.ApplyEdit</c>), or starts over when the result also carries a style.
/// <paramref name="DerivedChanges"/> are the structural changes of field refreshes that an undo or redo reverted or replayed together with its step (see <see cref="EditHistory.AttachDerived"/>), in the order they were applied;
/// they are already told to the document as derived changes and are null for every result that carries none. A consumer that keeps block indices (the layout) must account for them.
/// </summary>
public sealed record EditResult(TextPosition Caret, IReadOnlyList<BlockChange> Changes, TextRange? Selected = null, StyleChange? Style = null, bool KeepSelection = false, BlockMove? Move = null, SectionChange? Sections = null, ObjectChange? Objects = null, IReadOnlyList<BlockChange>? DerivedChanges = null)
{
    /// <summary>
    /// Tells the document which block ranges this result changed: the one call every raise site of the editor makes after <see cref="DocumentEdits.Raise"/> or <see cref="DocumentEdits.Restore"/>
    /// (an operation, an undo, a redo). It attributes <see cref="Changes"/> and, when <see cref="Sections"/> is set, the carrier paragraphs of the swapped sections (<see cref="SectionChange.CarrierChanges"/>)
    /// in <b>one</b> <see cref="DocumentEdits.Attribute"/> call, because the recovery snapshot's block cache counts one attribution per raise: leaving the carriers out serves stale bytes of a section break
    /// that changed in place, and a second call makes the next copy write the whole body again. Never call <c>document.Edits.Attribute(result.Changes)</c> for a result that may carry sections.
    /// The carrier changes are not put into <see cref="Changes"/> itself: more than one block change makes the history skip the step.
    /// </summary>
    public void Attribute(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (Sections is { } sections && sections.CarrierChanges(document) is { Count: > 0 } carriers)
        {
            document.Edits.Attribute([.. Changes, .. carriers]);
            return;
        }

        document.Edits.Attribute(Changes);
    }
}

/// <summary>How far a <see cref="TextEditor.Backspace"/> or <see cref="TextEditor.DeleteForward"/> reaches.</summary>
public enum DeleteUnit
{
    /// <summary>One grapheme cluster (a base letter with its accents, an emoji with its skin tone), or one whole atomic unit (a picture, a note reference). A field result next to the caret is selected first (<see cref="EditResult.Selected"/>, nothing changes) and deleted by <see cref="TextEditor.Delete"/> of that range; repeating the call at the same caret selects it again.</summary>
    Cluster,

    /// <summary>To the previous or next word boundary; see <see cref="TextEditor.Backspace"/> for the rule.</summary>
    Word,
}
