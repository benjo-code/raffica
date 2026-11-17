using Raffica.Core.Viewing;
using Raffica.Write.Editing;

namespace Raffica.Write.Commands;

/// <summary>
/// When a command may run: Open, Settings and the command search always; the others need a document. The zoom commands also have limits (the range of
/// <see cref="ZoomSteps"/>) and the outline waits for its pane; each disabled command has its reason, so nothing is dimmed silently. The window gives the
/// questions; the rules themselves know nothing of the UI.
/// </summary>
/// <remarks>
/// The edit commands (undo, redo, cut, copy, paste, select all) follow <see cref="EditCommandState"/>. While the document is still opening or editing failed all of them are
/// disabled. While a save runs undo, redo and paste stay enabled (they are operations of the session and are queued like typing, replayed in order when the save ends);
/// cut, copy and select all are disabled because they read the selection now and a queued one could not tell the clipboard what it took. Cut and copy need a selection (a
/// caret is none); undo and redo need a step. An empty clipboard is no disabled state: the clipboard is read asynchronously, so paste is always offered when the document can be
/// edited and does nothing when there is nothing to paste. The formatting commands (<see cref="WriteCommandIds.IsFormatting"/>) are blocked in the same way and queue during a save like typing;
/// beyond that Update style to match needs one defined style and something to move, the paragraph and character style pickers need styles in the file and Clear direct formatting needs
/// formatting to clear. The Formatting pane switch needs a document only.
/// </remarks>
/// <param name="hasDocument">Whether a document tab is open.</param>
/// <param name="zoom">The zoom of the view of the active tab (1 = 100 %).</param>
/// <param name="outlineAvailable">Whether the outline pane exists (the step that builds it says yes).</param>
/// <param name="editState">What the edit commands need to know of the active tab; null reads as a tab that can be edited, with a selection and history.</param>
/// <param name="saveState">What Save and Save as need to know of the active tab; null reads as a document with changes that can be saved.</param>
/// <param name="isClosing">Whether the window is closing (New document is the only command that then says so; null: never).</param>
/// <param name="formatFacts">The facts about the selection that Clear direct formatting, the style pickers and Update style to match need; asked only when one of those commands is (and only while editing is not blocked), so the state of the other commands never pays for them; null keeps the facts of <paramref name="editState"/>.</param>
internal sealed class WriteCommandRules(Func<bool> hasDocument, Func<double> zoom, Func<bool>? outlineAvailable = null, Func<EditCommandState>? editState = null, Func<SaveCommandState>? saveState = null, Func<FormatFacts>? formatFacts = null, Func<bool>? isClosing = null)
{
    private const double Tolerance = 1e-9;

    private EditCommandState State => editState?.Invoke() ?? new EditCommandState(null, true, true, true);

    // The state for a formatting command: the facts are read only for the commands that need them and only while editing is not blocked (a save queues formatting, so the model is not read then).
    private EditCommandState FormatState(string id)
    {
        var state = State;
        if (formatFacts is null || state.Blocked is not null || id is not (WriteCommandIds.ClearDirect or WriteCommandIds.ParagraphStyle or WriteCommandIds.CharacterStyle or WriteCommandIds.UpdateStyle))
        {
            return state;
        }

        var facts = formatFacts();
        return state with { HasStylesPart = facts.HasStylesPart, StyleDefined = facts.StyleDefined, StyleMixed = facts.StyleMixed, HasOverrides = facts.HasOverrides, CanUpdateStyle = facts.CanUpdateStyle };
    }

    private SaveCommandState SaveState => saveState?.Invoke() ?? new SaveCommandState(hasDocument(), false, false, false, false, true);

    /// <summary>Whether the command may run now (not counting whether an action is wired behind it).</summary>
    public bool Allows(string id) => id switch
    {
        WriteCommandIds.New => isClosing?.Invoke() != true,
        WriteCommandIds.Open or WriteCommandIds.Settings or WriteCommandIds.Search => true,
        WriteCommandIds.ZoomIn => hasDocument() && zoom() < ZoomSteps.Max - Tolerance,
        WriteCommandIds.ZoomOut => hasDocument() && zoom() > ZoomSteps.Min + Tolerance,
        WriteCommandIds.Outline => hasDocument() && outlineAvailable?.Invoke() == true,
        WriteCommandIds.Save => SaveState.Refusal(saveAs: false) is null,
        WriteCommandIds.SaveAs => SaveState.Refusal(saveAs: true) is null,
        _ when WriteCommandIds.IsEditing(id) => hasDocument() && EditReason(id, State) is null,
        _ when WriteCommandIds.IsFormatting(id) => hasDocument() && FormatReason(id, FormatState(id)) is null,
        _ => hasDocument(),
    };

    /// <summary>
    /// Why the command is disabled, in the UI language, when there is something to tell (null otherwise). With a document open every disabled command has a
    /// reason; without one the start view shows no commands, so nothing is told.
    /// </summary>
    public string? ReasonFor(string id)
    {
        if (id == WriteCommandIds.New)
        {
            return Allows(id) ? null : WriteStrings.ReasonClosing;
        }

        if (!hasDocument() || Allows(id))
        {
            return null;
        }

        if (WriteCommandIds.IsEditing(id))
        {
            return EditReason(id, State);
        }

        if (WriteCommandIds.IsFormatting(id))
        {
            return FormatReason(id, FormatState(id));
        }

        return id switch
        {
            WriteCommandIds.Save => SaveReason(SaveState.Refusal(saveAs: false), saveAs: false),
            WriteCommandIds.SaveAs => SaveReason(SaveState.Refusal(saveAs: true), saveAs: true),
            WriteCommandIds.ZoomIn => WriteStrings.ReasonZoomMax,
            WriteCommandIds.ZoomOut => WriteStrings.ReasonZoomMin,
            WriteCommandIds.Outline => WriteStrings.ReasonOutlineSoon,
            _ => null,
        };
    }

    /// <summary>The words for a refusal of Save or Save as in the UI language (null for none and for "no document", which the start view needs no words for).</summary>
    internal static string? SaveReason(SaveRefusal? refusal, bool saveAs) => refusal switch
    {
        SaveRefusal.StillOpening => WriteStrings.ReasonEditOpening,
        SaveRefusal.Running => WriteStrings.ReasonEditSaving,
        SaveRefusal.NothingToSave => WriteStrings.ReasonNothingToSave,
        SaveRefusal.Unavailable => saveAs ? WriteStrings.ReasonSaveAsFaulted : WriteStrings.ReasonSaveFaulted,
        _ => null,
    };

    // The reason a formatting command is disabled, or null when it may run. The block comes first (a save queues formatting like typing, so it never disables it), then the facts of the selection.
    private static string? FormatReason(string id, EditCommandState state)
    {
        switch (state.Blocked)
        {
            case EditBlockedReason.StillOpening:
                return WriteStrings.ReasonEditOpening;
            case EditBlockedReason.Faulted:
                return WriteStrings.EditUnavailable;
            default:
                break;
        }

        return id switch
        {
            WriteCommandIds.ClearDirect when !state.HasOverrides => WriteStrings.ReasonNoFormattingToClear,
            WriteCommandIds.ParagraphStyle or WriteCommandIds.CharacterStyle when !state.HasStylesPart => WriteStrings.ReasonNoStyles,
            WriteCommandIds.UpdateStyle when state.StyleMixed => WriteStrings.ReasonStyleMixed,
            WriteCommandIds.UpdateStyle when !state.HasStylesPart || !state.StyleDefined => WriteStrings.ReasonStyleNotDefined,
            WriteCommandIds.UpdateStyle when !state.CanUpdateStyle => WriteStrings.ReasonNothingToUpdate,
            _ => null,
        };
    }

    // The reason an edit command is disabled, or null when it may run. The block comes first (it holds for every command), then what the command itself needs.
    private static string? EditReason(string id, EditCommandState state)
    {
        var queued = id is WriteCommandIds.Undo or WriteCommandIds.Redo or WriteCommandIds.Paste;
        switch (state.Blocked)
        {
            case EditBlockedReason.StillOpening:
                return WriteStrings.ReasonEditOpening;
            case EditBlockedReason.Faulted:
                return WriteStrings.EditUnavailable;
            case EditBlockedReason.Saving when !queued:
                return WriteStrings.ReasonEditSaving;
            default:
                break;
        }

        return id switch
        {
            WriteCommandIds.Undo when !state.CanUndo => WriteStrings.ReasonNothingToUndo,
            WriteCommandIds.Redo when !state.CanRedo => WriteStrings.ReasonNothingToRedo,
            WriteCommandIds.Cut or WriteCommandIds.Copy when !state.HasSelection => WriteStrings.ReasonNoSelection,
            _ => null,
        };
    }
}
