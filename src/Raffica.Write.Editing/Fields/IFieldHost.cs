using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// What a field refresh needs to know that the model alone cannot say: the number label of a heading or list paragraph, the printed page of a position and the mark of a note. The editor references only the model,
/// so the app implements this over the layout and its numbering counter and a test implements it over its own reference count.
/// </summary>
public interface IFieldHost
{
    /// <summary>True when the layout of the document is complete, so a page answer is final.</summary>
    bool IsComplete { get; }

    /// <summary>The number label of the paragraph as it is shown (for example <c>2.1.</c>), or null when it has none.</summary>
    string? NumberLabel(Paragraph paragraph);

    /// <summary>The page the position is on: its zero-based index and the printed page number (section format and start respected), or null when the layout has no answer.</summary>
    (int PageIndex, string PageText)? PageOf(TextPosition position);

    /// <summary>The mark text of the note with that id (the number the reader sees), or null when there is none.</summary>
    string? NoteMarkText(int noteId, NoteType type);
}
