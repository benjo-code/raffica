using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The document objects (as opposed to blocks, styles and sections) an operation, an undo or a redo added to or removed from the document: footnotes and endnotes
/// (<paramref name="NotesAdded"/> and <paramref name="NotesRemoved"/> are the live note objects, never copies; a note's kind of part is read from its element name, see <see cref="NoteType"/>)
/// and picture parts (<paramref name="MediaAdded"/> and <paramref name="MediaRemoved"/>, empty until pictures can be inserted). The history keeps the change in its step and applies it, or its
/// <see cref="Inverse"/>, with the block swap of the step, so a reference and its note come and go together.
/// </summary>
public sealed record ObjectChange(IReadOnlyList<Note> NotesAdded, IReadOnlyList<Note> NotesRemoved, IReadOnlyList<MediaRef> MediaAdded, IReadOnlyList<MediaRef> MediaRemoved)
{
    /// <summary>True when nothing was added or removed.</summary>
    public bool IsEmpty => NotesAdded.Count == 0 && NotesRemoved.Count == 0 && MediaAdded.Count == 0 && MediaRemoved.Count == 0;

    /// <summary>What an undo of this change does: what was added is removed and the other way round.</summary>
    public ObjectChange Inverse => new(NotesRemoved, NotesAdded, MediaRemoved, MediaAdded);

    /// <summary>The kind of notes part a note belongs to, from the name of its element (<c>w:endnote</c> is an endnote, anything else a footnote).</summary>
    public static NoteType TypeOf(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return note.Content.Root.Name.LocalName == "endnote" ? NoteType.Endnote : NoteType.Footnote;
    }

    // True when the change can be applied to the document as it is now: every note to remove is in its part (by reference).
    internal bool CanApply(WordDocument document, bool undo)
    {
        var remove = undo ? NotesAdded : NotesRemoved;
        foreach (var note in remove)
        {
            var collection = TypeOf(note) == NoteType.Endnote ? document.Endnotes : document.Footnotes;
            if (collection is null || !collection.Notes.Any(n => ReferenceEquals(n, note)))
            {
                return false;
            }
        }

        return true;
    }

    // Removes the notes the step added (undo) or removed (redo: the ones the operation removed) and puts back the others; a note comes back at the place that keeps the ids in order.
    internal void Apply(WordDocument document, bool undo)
    {
        var remove = undo ? NotesAdded : NotesRemoved;
        var add = undo ? NotesRemoved : NotesAdded;
        foreach (var note in remove)
        {
            var collection = TypeOf(note) == NoteType.Endnote ? document.Endnotes : document.Footnotes;
            for (var i = 0; collection is not null && i < collection.Notes.Count; i++)
            {
                if (ReferenceEquals(collection.Notes[i], note))
                {
                    collection.Notes.RemoveAt(i);
                    break;
                }
            }
        }

        foreach (var note in add)
        {
            var collection = NotesParts.Ensure(document, TypeOf(note));
            var at = 0;
            while (at < collection.Notes.Count && collection.Notes[at].Id <= note.Id)
            {
                at++;
            }

            collection.Notes.Insert(at, note);
        }
    }
}
