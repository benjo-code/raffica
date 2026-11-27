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

    /// <summary>
    /// The number of a chapter heading as <c>STYLEREF 1 \s</c> shows it: the numbers of the paragraph's list label from its first to its last one, each in the format of its level, with the text between two of them
    /// and without the text before the first and after the last (the label <c>Chapter I:</c> gives <c>I</c>, <c>Sec 1 - 2)</c> gives <c>1 - 2</c>); null when the paragraph has no number.
    /// </summary>
    string? ChapterNumber(Paragraph paragraph);

    /// <summary>The page the position is on: its zero-based index and the printed page number (section format and start respected), or null when the layout has no answer.</summary>
    (int PageIndex, string PageText)? PageOf(TextPosition position);

    /// <summary>
    /// A value that changes whenever a page result could: it covers the page of every top-level body block and the printed number of every page, and is known only while <see cref="IsComplete"/>. Null (the default) means "not known": a caller then
    /// never skips a page pass. Equal values at two times mean no body block moved to another page and no page changed its number.
    /// </summary>
    long? PageSignature => null;

    /// <summary>The mark text of the note with that id (the number the reader sees), or null when there is none.</summary>
    string? NoteMarkText(int noteId, NoteType type);
}
