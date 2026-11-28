using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Settings;

namespace Raffica.Write.Viewing;

/// <summary>
/// The bookmark command of the page view (the Bookmarks card of the References tab): Add and Delete are one <see cref="EditSession.Apply"/> through <see cref="ApplyGuarded"/> each (one history step), Go to puts the
/// selection on the range of the bookmark. A command that finds nothing to do says so through <see cref="StructureNote"/>, never silently. A prefix document is refused (the editor cannot see that the body was cut, so the
/// command passes the flag).
/// </summary>
internal sealed partial class PageView
{
    /// <summary>The names the Bookmarks card lists (sorted, hidden names left out) and every name of the document. Read from the model when the card opens, never earlier.</summary>
    internal BookmarkContext BookmarkCardContext()
    {
        var model = _tab.Docx.Model;
        var shown = BookmarkOperations.Visible(model).Select(b => b.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        return new BookmarkContext(shown, BookmarkOperations.AllNames(model));
    }

    /// <summary>Runs the answer of the Bookmarks card; true when it was taken (also when it changed nothing), false for a released view or a request that is not well formed.</summary>
    internal bool RunBookmarkCommand(BookmarkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_released || !request.IsValid)
        {
            return false;
        }

        var prefix = IsPrefixDocument;
        if (prefix)
        {
            StructureNote?.Invoke(WriteStrings.ReasonSectionPrefix);
            return true;
        }

        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        switch (request.Action)
        {
            case BookmarkAction.Add:
                if (session.Selection is not { } selection)
                {
                    StructureNote?.Invoke(WriteStrings.ReasonSectionNotInBody);
                    return true;
                }

                if (ApplyGuarded(session, ed => ed.AddBookmark(selection, request.Name, prefix)) is null && session.BlockedReason is null)
                {
                    StructureNote?.Invoke(WriteStrings.NoteStructureFailed);
                }

                break;
            case BookmarkAction.Delete:
                if (ApplyGuarded(session, ed => ed.DeleteBookmark(request.Name, prefix)) is null && session.BlockedReason is null)
                {
                    StructureNote?.Invoke(WriteStrings.NoteBookmarkMissing);
                }

                break;
            default:
                GoToBookmark(session, request.Name);
                break;
        }

        return true;
    }

    // Puts the selection on the range of the bookmark (the caret at its start for a point) and scrolls to it. A bookmark that is gone (a document that changed behind the card) is a note.
    private void GoToBookmark(EditSession session, string name)
    {
        if (!CanPlaceCaret)
        {
            return;
        }

        TextRange? range;
        try
        {
            range = session.Editor.FindBookmark(name);
        }
        catch (StalePositionException)
        {
            range = null;
        }

        if (range is not { } found)
        {
            StructureNote?.Invoke(WriteStrings.NoteBookmarkMissing);
            return;
        }

        AttachSession(session);
        SyncActiveMode();
        Place(session, found, CaretAffinity.Downstream);
        EnsureCaretVisible();
    }
}
