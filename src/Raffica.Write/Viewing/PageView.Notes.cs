using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;

namespace Raffica.Write.Viewing;

/// <summary>
/// The note commands of the page view (<c>note.*</c>: a footnote, an endnote and Back to the text) and the two ways to the same places without a command: a double click on a note reference jumps to the note and a
/// double click on the number of a note, or Esc in a note, goes back to the text. The insert is one <see cref="EditSession.Apply"/> through <see cref="ApplyGuarded"/> (one history step: undo takes the reference and the
/// note away together); the caret then stands in the new note. A command that finds nothing to do says so through <see cref="StructureNote"/>, never silently. The writing view draws no notes, so it refuses a new
/// note with its reason; a prefix document is refused too (the editor cannot see that the body was cut, so the command passes the flag).
/// </summary>
internal sealed partial class PageView
{
    /// <summary>Runs a note command; true when it was taken (also when it changed nothing), false for a released view or an unknown command.</summary>
    internal bool RunNoteCommand(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_released || !WriteCommandIds.IsNote(id))
        {
            return false;
        }

        if (id == WriteCommandIds.NoteBack)
        {
            if (Caret is not { } caret || caret.Story.Kind != StoryKind.Note)
            {
                StructureNote?.Invoke(WriteStrings.ReasonNoteNotInNote);
            }
            else if (!BackToReference())
            {
                StructureNote?.Invoke(WriteStrings.NoteNoteNotFound);
            }

            return true;
        }

        if (_tab.Docx.IsPrefix)
        {
            StructureNote?.Invoke(WriteStrings.ReasonSectionPrefix);
            return true;
        }

        if (_mode == WriteView.Writing)
        {
            StructureNote?.Invoke(WriteStrings.ReasonNoteWritingView);
            return true;
        }

        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        var type = id == WriteCommandIds.NoteEndnote ? NoteType.Endnote : NoteType.Footnote;
        var prefix = _tab.Docx.IsPrefix;
        var result = ApplyGuarded(session, ed => session.Caret is { } caret ? ed.InsertNote(caret, type, prefix) : null);

        // The rules said yes but the editor had nothing to do: a visible note with the editor's reason, never silence. A save queues the operation and says nothing.
        if (result is null && session.BlockedReason is null)
        {
            StructureNote?.Invoke(RefusalWords(session) ?? WriteStrings.NoteStructureFailed);
        }

        return true;
    }

    // The words for the editor's refusal of a note at the caret, or null when it has none (the place is fine, or the position is stale).
    private static string? RefusalWords(EditSession session)
    {
        if (session.Caret is not { } caret)
        {
            return WriteStrings.ReasonSectionNotInBody;
        }

        try
        {
            return session.Editor.RefusalForNote(caret) is { } refusal ? ObjectWording.ReasonText(ObjectWording.BlockOf(refusal)) : null;
        }
        catch (StalePositionException)
        {
            return null;
        }
    }

    /// <summary>
    /// Puts the caret at the start of the text of the note that the reference at <paramref name="reference"/> points to (the place of the reference or the place right behind it). False, and nothing changes,
    /// when the place is no reference, the note is missing from the document or the view cannot take a caret now.
    /// </summary>
    internal bool GoToNote(TextPosition reference)
    {
        if (!CanPlaceCaret || ActiveLayout() is not { } layout || reference.Story.Kind != StoryKind.Body)
        {
            return false;
        }

        var projection = ProjectionOf(layout, reference);
        var target = NoteLinks.ReferenceOf(projection.PieceAt(reference.Offset)) ?? (reference.Offset > 0 ? NoteLinks.ReferenceOf(projection.PieceAt(reference.Offset - 1)) : null);
        return target is { } note && JumpToNote(layout, note);
    }

    private bool JumpToNote(DocumentLayout layout, NoteTarget target)
    {
        if (NoteLinks.FirstParagraph(_tab.Docx.Model, target) is not { } paragraph)
        {
            return false;
        }

        var story = target.Story;
        var noteProjection = ParagraphProjection.Build(paragraph, layout.ProjectionHost(story, paragraph));
        var at = new TextPosition(story, paragraph, NoteLinks.TextStart(noteProjection));
        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        Place(session, new TextRange(at, at), CaretAffinity.Downstream);
        EnsureCaretVisible();
        return true;
    }

    /// <summary>
    /// Puts the caret in the body right behind the reference of the note the caret is in (Back to the text, Esc in a note). False, and nothing changes, when the caret is in no note, the body has no reference
    /// to it or the view cannot take a caret now.
    /// </summary>
    internal bool BackToReference() => Caret is { } caret && BackFrom(caret.Story);

    private bool BackFrom(StoryRef story)
    {
        if (!CanPlaceCaret || ActiveLayout() is not { } layout || NoteLinks.NoteOf(story) is not { } target)
        {
            return false;
        }

        if (NoteLinks.FindReference(_tab.Docx.Model, target, p => ParagraphProjection.Build(p, layout.ProjectionHost(StoryRef.Body, p))) is not { } reference)
        {
            return false;
        }

        var behind = ProjectionOf(layout, reference).PieceAt(reference.Offset) is { } piece ? reference with { Offset = piece.End } : reference;
        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        Place(session, new TextRange(behind, behind), CaretAffinity.Downstream);
        EnsureCaretVisible();
        return true;
    }

    // A double click on the character of a note reference in the body goes to the note, one on the number of a note goes back to the text; any other double click is a word (the word rule would take the digits of
    // a mark together with the letters in front of it, so the character under the pointer is looked at, as the word rule does: the one before the position on the later half of a character). True when it was a note link.
    private bool TryNoteLink(DocumentLayout layout, TextPosition position, bool trailing)
    {
        var projection = ProjectionOf(layout, position);
        var under = trailing && position.Offset > 0 ? position.Offset - 1 : position.Offset;
        if (projection.PieceAt(under) is not { } piece || under < piece.Start || under >= piece.End)
        {
            return false;
        }

        if (position.Story.Kind == StoryKind.Body && NoteLinks.ReferenceOf(piece) is { } target)
        {
            return JumpToNote(layout, target);
        }

        return position.Story.Kind == StoryKind.Note && NoteLinks.IsOwnMark(piece) && BackFrom(position.Story);
    }
}
