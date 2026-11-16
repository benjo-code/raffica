using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What an editing operation did: where the caret goes and which blocks changed. <paramref name="Changes"/> is empty when the operation
/// found nothing to change (an empty selection, an empty insert); the document revision was then not raised either. <paramref name="Selected"/>
/// is the unit a first Backspace or Delete selected instead of deleting (a field): nothing changed, and deleting that range removes it.
/// <paramref name="Style"/> is the style an operation (or an undo or redo) swapped in the style sheet, with the snapshots before and after; a layout's
/// style snapshot is then stale and its owner must start the layout over. <paramref name="KeepSelection"/> is set by the formatting operations: they change no text, so every offset stays
/// valid and the selection that was there stays as it is (direction and anchor included) instead of collapsing to <paramref name="Caret"/>.
/// </summary>
public sealed record EditResult(TextPosition Caret, IReadOnlyList<BlockChange> Changes, TextRange? Selected = null, StyleChange? Style = null, bool KeepSelection = false);

/// <summary>How far a <see cref="TextEditor.Backspace"/> or <see cref="TextEditor.DeleteForward"/> reaches.</summary>
public enum DeleteUnit
{
    /// <summary>One grapheme cluster (a base letter with its accents, an emoji with its skin tone), or one whole atomic unit (a picture, a note reference). A field result next to the caret is selected first (<see cref="EditResult.Selected"/>, nothing changes) and deleted by <see cref="TextEditor.Delete"/> of that range; repeating the call at the same caret selects it again.</summary>
    Cluster,

    /// <summary>To the previous or next word boundary; see <see cref="TextEditor.Backspace"/> for the rule.</summary>
    Word,
}
