namespace Raffica.Write.Model;

/// <summary>The footnotes part (<c>w:footnotes</c>) or the endnotes part (<c>w:endnotes</c>). Children that are not a note with a usable id are kept children in place.</summary>
public sealed class NoteCollection : OrderedElement, IEnumerable<Note>
{
    public NoteCollection(OoxmlRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
    }

    public OoxmlRoot Root { get; }

    /// <summary>The notes are written at the position they were read in; notes added in code go after the last read note.</summary>
    public IList<Note> Notes { get; } = new List<Note>();

    /// <summary>The first note with this id, or null.</summary>
    public Note? Find(int id)
    {
        foreach (var note in Notes)
        {
            if (note.Id == id)
            {
                return note;
            }
        }

        return null;
    }

    public IEnumerator<Note> GetEnumerator() => Notes.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
