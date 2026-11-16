using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What an editing operation did: where the caret goes and which blocks changed. <paramref name="Changes"/> is empty when the operation
/// found nothing to change (an empty selection, an empty insert); the document revision was then not raised either. <paramref name="Selected"/>
/// is the unit a first Backspace or Delete selected instead of deleting (a field): nothing changed, and deleting that range removes it.
/// </summary>
public sealed record EditResult(TextPosition Caret, IReadOnlyList<BlockChange> Changes, TextRange? Selected = null);

/// <summary>How far a <see cref="TextEditor.Backspace"/> or <see cref="TextEditor.DeleteForward"/> reaches.</summary>
public enum DeleteUnit
{
    /// <summary>One grapheme cluster (a base letter with its accents, an emoji with its skin tone), or one whole atomic unit (a picture, a note reference). A field result next to the caret is selected first (<see cref="EditResult.Selected"/>, nothing changes) and deleted by <see cref="TextEditor.Delete"/> of that range; repeating the call at the same caret selects it again.</summary>
    Cluster,

    /// <summary>To the previous or next word boundary; see <see cref="TextEditor.Backspace"/> for the rule.</summary>
    Word,
}
