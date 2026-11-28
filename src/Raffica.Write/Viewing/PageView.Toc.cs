using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Viewing;

/// <summary>
/// The table of contents commands of the page view (the Table of contents card of the References tab and Remove): the insert and the remove are one <see cref="EditSession.Apply"/> each through <see cref="ApplyGuarded"/> (one history
/// step: undo takes the list, or puts it back, together with the hidden bookmarks of the headings). The list goes at the start of the selection. A command that finds nothing to do says why through <see cref="StructureNote"/>,
/// never silently. A prefix document is refused (the editor cannot see that the body was cut, so the command passes the flag).
/// </summary>
internal sealed partial class PageView
{
    /// <summary>What the Table of contents card needs; read from the model when the card opens, never earlier.</summary>
    internal TocContext TocCardContext()
    {
        var model = _tab.Docx.Model;
        var inside = _tab.Editor is { BlockedReason: null } session && session.Selection is { } range && session.Editor.RefusalForRemoveToc(range.Normalized().Start) is null;
        return new TocContext(CaptionLabels.Known(model), inside);
    }

    /// <summary>Runs the answer of the Table of contents card or the Remove command; true when it was taken (also when it changed nothing), false for a released view or a request that is not well formed.</summary>
    internal bool RunTocCommand(TocRequest request)
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

        if (!_tab.RefreshesFields)
        {
            StructureNote?.Invoke(WriteStrings.NoteStructureFailed);
            return true;
        }

        var host = _tab.EnsureFields().Host;
        var remove = request.Action == TocAction.Remove;

        // Whether the operation reached the editor: a run that did not (no selection) must not word a refusal an earlier call left in it.
        var asked = false;
        var result = ApplyGuarded(session, ed =>
        {
            asked = session.Selection is not null;
            if (session.Selection?.Normalized() is not { } selection)
            {
                return null;
            }

            return remove ? ed.RemoveToc(selection.Start) : ed.InsertToc(selection.Start, request.Options!, host, prefix);
        });

        // The rules said yes but the editor had nothing to do: a visible note with the editor's reason, never silence. A table of contents has said so already (ApplyGuarded); a save queues the operation and says nothing.
        if (result is null && session.BlockedReason is null && !(asked && session.Editor.LastTocRefusal == TocRefusal.InsideToc))
        {
            StructureNote?.Invoke((asked ? RefusalWords(session.Editor.LastTocRefusal) : null) ?? WriteStrings.NoteStructureFailed);
        }

        return true;
    }

    // The words for the editor's refusal of a table of contents command, or null when it left none.
    private static string? RefusalWords(TocRefusal? refusal) => refusal switch
    {
        TocRefusal.Prefix => WriteStrings.ReasonSectionPrefix,
        TocRefusal.NotInBody => WriteStrings.ReasonSectionNotInBody,
        TocRefusal.LevelRange => WriteStrings.TocProblemLevels,
        TocRefusal.LabelNotAllowed => WriteStrings.NoteTocLabel,
        TocRefusal.NothingToRemove => WriteStrings.ReasonTocNone,
        _ => null,
    };
}
