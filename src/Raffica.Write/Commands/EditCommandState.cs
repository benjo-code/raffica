using Raffica.Write.Documents;
using Raffica.Write.Editing;

namespace Raffica.Write.Commands;

/// <summary>What the rules of the edit commands need to know about the active tab.</summary>
/// <param name="Blocked">Why editing is not possible now (null when it is).</param>
/// <param name="HasSelection">Whether text is selected (a caret is not a selection).</param>
/// <param name="CanUndo">Whether an undo step is there.</param>
/// <param name="CanRedo">Whether a redo step is there.</param>
/// <param name="HasStylesPart">Whether the file has a styles part.</param>
/// <param name="StyleDefined">Whether the paragraph style of the selection is defined in the file.</param>
/// <param name="StyleMixed">Whether the selection touches paragraphs of different paragraph styles.</param>
/// <param name="HasOverrides">Whether the selection has direct formatting to clear.</param>
/// <param name="CanUpdateStyle">Whether Update style to match would move something into the style.</param>
internal readonly record struct EditCommandState(EditBlockedReason? Blocked, bool HasSelection, bool CanUndo, bool CanRedo, bool HasStylesPart = true, bool StyleDefined = true, bool StyleMixed = false, bool HasOverrides = true, bool CanUpdateStyle = true)
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

    /// <summary>
    /// The facts of the formatting commands about the selection of a tab, read only when a command that needs one is asked (never with the rest of the state, never before the tab has an
    /// editing session) and remembered by the session until the selection or the document changes. A tab that cannot be edited now (opening, saving, faulted), has no session or has no
    /// selection yet gives the permissive facts: a save queues formatting like typing, so the model is not read then.
    /// </summary>
    public static FormatFacts FactsOf(WriteTab? tab)
    {
        if (tab?.Editor is { BlockedReason: null, Selection: { } range } session)
        {
            try
            {
                return session.ReadFormatFacts(range);
            }
            catch (StalePositionException)
            {
                // A selection that does not belong to the model any more has no facts (this runs inside a timer tick that nothing guards); the next selection or edit reads again.
                return FormatFacts.Unknown;
            }
        }

        return FormatFacts.Unknown;
    }

    /// <summary>
    /// The formatting the ribbon shows for the selection of a tab (the pressed switches, the values of the pickers), or null when there is nothing to show: no editing session yet (nothing is made
    /// for the display), the tab cannot be edited now, or no selection. Cheap after the first read of a selection and revision (<see cref="EditSession.ReadSelectionFormat"/>).
    /// </summary>
    public static SelectionFormat? FormatOf(WriteTab? tab)
    {
        if (tab?.Editor is { BlockedReason: null, Selection: { } range } session)
        {
            try
            {
                return session.ReadSelectionFormat(range);
            }
            catch (StalePositionException)
            {
                // A selection that does not belong to the model any more shows nothing; the next selection or edit shows again.
                return null;
            }
        }

        return null;
    }
}
