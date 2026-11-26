using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Viewing;

/// <summary>
/// The cross-reference command of the page view (the Cross-reference card of the References tab): the insert is one <see cref="EditSession.Apply"/> through <see cref="ApplyGuarded"/> (one history step: undo takes the field and
/// a bookmark made for it away together). The reference stands at the end of the selection and keeps the selected text. A command that finds nothing to do says why through <see cref="StructureNote"/>, never silently. A prefix
/// document is refused (the editor cannot see that the body was cut, so the command passes the flag).
/// </summary>
internal sealed partial class PageView
{
    /// <summary>What the Cross-reference card needs: the targets of a type are read from the model when the card asks for them (it asks when it opens and when the type changes), never earlier; a tab that follows no fields has none.</summary>
    internal CrossRefContext CrossReferenceCardContext()
    {
        var tab = _tab;
        return new CrossRefContext(
            kind => tab.RefreshesFields ? CrossReferences.Targets(tab.Docx.Model, tab.EnsureFields().Host, kind) : [],
            CrossRefTargetKind.Heading);
    }

    /// <summary>Runs the answer of the Cross-reference card; true when it was taken (also when it changed nothing), false for a released view or a request that is not well formed.</summary>
    internal bool RunCrossReferenceCommand(CrossRefRequest request)
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

        // Whether the operation reached the editor: a run that did not (no selection) must not word a refusal an earlier call left in it.
        var asked = false;
        var result = ApplyGuarded(session, ed =>
        {
            asked = session.Selection is not null;
            return session.Selection?.Normalized() is { } selection ? ed.InsertCrossReference(selection.End, request.Target, request.Kind, host, prefix) : null;
        });

        // The rules said yes but the editor had nothing to do: a visible note with the editor's reason, never silence. A table of contents has said so already (ApplyGuarded); a save queues the operation and says nothing.
        if (result is null && session.BlockedReason is null && !(asked && session.Editor.LastCrossRefRefusal == CrossRefRefusal.InsideToc))
        {
            StructureNote?.Invoke((asked ? RefusalWords(session.Editor.LastCrossRefRefusal) : null) ?? WriteStrings.NoteStructureFailed);
        }

        return true;
    }

    // The words for the editor's refusal of a cross-reference, or null when it left none.
    private static string? RefusalWords(CrossRefRefusal? refusal) => refusal switch
    {
        CrossRefRefusal.Prefix => WriteStrings.ReasonSectionPrefix,
        CrossRefRefusal.NotInBody or CrossRefRefusal.NoPosition => WriteStrings.ReasonSectionNotInBody,
        CrossRefRefusal.RestrictedControl => WriteStrings.ReasonCrossRefRestricted,
        CrossRefRefusal.TargetGone => WriteStrings.NoteCrossRefGone,
        CrossRefRefusal.InsideTarget => WriteStrings.NoteCrossRefInside,
        CrossRefRefusal.NoNumber => WriteStrings.NoteCrossRefNoNumber,
        CrossRefRefusal.SeveralParagraphs => WriteStrings.NoteCrossRefSeveral,
        CrossRefRefusal.KindNotOffered => WriteStrings.NoteCrossRefNotOffered,
        CrossRefRefusal.IdsUsedUp => WriteStrings.NoteCrossRefIds,
        _ => null,
    };
}
