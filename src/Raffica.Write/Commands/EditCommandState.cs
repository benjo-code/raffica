using Raffica.Write.Documents;
using Raffica.Write.Editing;

namespace Raffica.Write.Commands;

/// <summary>What the rules of the edit commands need to know about the active tab.</summary>
/// <param name="Blocked">Why editing is not possible now (null when it is).</param>
/// <param name="HasSelection">Whether text is selected (a caret is not a selection).</param>
/// <param name="CanUndo">Whether an undo step is there.</param>
/// <param name="CanRedo">Whether a redo step is there.</param>
internal readonly record struct EditCommandState(EditBlockedReason? Blocked, bool HasSelection, bool CanUndo, bool CanRedo)
{
    /// <summary>
    /// The state of a tab, read without making anything: a tab that has no editing session yet has no selection and no history (the session is made by the first click or key).
    /// </summary>
    public static EditCommandState Of(WriteTab? tab)
    {
        if (tab is null)
        {
            return new EditCommandState(null, false, false, false);
        }

        var session = tab.Editor;
        var blocked = session?.BlockedReason
            ?? (tab.IsDisposed || tab.Runner.IsFaulted ? EditBlockedReason.Faulted : tab.Runner.FullModelReady ? null : EditBlockedReason.StillOpening);
        if (session is null || blocked is EditBlockedReason.Faulted or EditBlockedReason.StillOpening)
        {
            return new EditCommandState(blocked, false, false, false);
        }

        var selection = session.Selection;
        return new EditCommandState(blocked, selection is { IsEmpty: false }, session.CanUndo, session.CanRedo);
    }
}
