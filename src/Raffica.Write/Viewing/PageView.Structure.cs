using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;

namespace Raffica.Write.Viewing;

/// <summary>
/// The structure commands of the page view (<c>list.*</c>, <c>structure.*</c> and <c>outline.*</c>): the ribbon, the command search, the menu and the keys (Alt and Shift with the arrows, Tab and
/// Shift+Tab in a list) end here. Every change is one <see cref="EditSession.Apply"/> through <see cref="ApplyGuarded"/> (one history step, the failure rule of a key, the pending format ended like any
/// operation that is not typing).
/// </summary>
/// <remarks>
/// A list command acts like paragraph formatting on the selection. The outline commands act on one heading: the paragraph of the engaged outline row (<see cref="OutlineParagraphSource"/>, set by the window
/// while the user works in the outline pane), else the heading at the caret. The plan (which blocks, where they land) is made inside the operation, from the model as it is when the operation runs,
/// so a stale outline can never move the wrong blocks. A command that finds nothing to do (a heading in a place that cannot move, the level out of range, a refusal of the editor beyond the bounded read) says so
/// through <see cref="StructureNote"/>; it is never a silent no-op. Tab and Shift+Tab decide at run time: Tab at the very start of a list paragraph, or over a selection that touches list paragraphs, changes the
/// level, anywhere else it inserts a tab; Shift+Tab changes the level the same way and does nothing otherwise.
/// </remarks>
internal sealed partial class PageView
{
    /// <summary>Runs a structure command for a key (Alt+Shift+Up ...); the window sets it to check its rules first. Null (the default, and in tests of the view alone): the view runs the command itself.</summary>
    internal Func<string, bool>? StructureKeyHandler { get; set; }

    /// <summary>Which platform's keys the view matches (tests set it to run the other platform's rules; the default is the system the process runs on).</summary>
    internal bool MacOsKeys { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>The paragraph of the outline row the user is working in (the pane is engaged), or null: the outline commands then act on the heading at the caret. The window sets it.</summary>
    internal Func<Paragraph?>? OutlineParagraphSource { get; set; }

    /// <summary>Raised with the words of a command that did nothing and the user must hear of (the window shows them in the status bar).</summary>
    internal event Action<string>? StructureNote;

    /// <summary>Runs a structure command; true when it was taken (also when it changed nothing), false for an unknown command or a released view.</summary>
    internal bool RunStructureCommand(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_released || !WriteCommandIds.IsStructure(id))
        {
            return false;
        }

        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        EditResult? result = null;
        switch (id)
        {
            case WriteCommandIds.Bullets:
                result = ApplyGuarded(session, ed => session.Selection is { } selection ? ed.ToggleList(selection, ListKind.Bullet) : null);
                break;
            case WriteCommandIds.Numbers:
                result = ApplyGuarded(session, ed => session.Selection is { } selection ? ed.ToggleList(selection, ListKind.Numbered) : null);
                break;
            case WriteCommandIds.RestartNumbering:
                result = ApplyGuarded(session, ed => session.Selection is { } selection ? ed.RestartNumbering(selection.Normalized().Start) : null);
                break;
            case WriteCommandIds.ContinueNumbering:
                result = ApplyGuarded(session, ed => session.Selection is { } selection ? ed.ContinueNumbering(selection.Normalized().Start) : null);
                break;
            case WriteCommandIds.NumberHeadings:
                // The state is read when the operation runs: the switch turns the numbers on when they are off and off when they are on.
                result = ApplyGuarded(session, ed => session.Caret is { } caret ? ed.SetHeadingNumbering(HeadingNumbering.StateOf(_tab.Docx.Model) != HeadingNumberingState.On, caret) : null);
                break;
            default:
                RunOutline(session, id);
                return true;
        }

        // The rules said yes but the editor had nothing to do (a refusal, or nothing changed): a visible note, never silence. A save queues the operation and says nothing.
        if (result is null && session.BlockedReason is null)
        {
            StructureNote?.Invoke(WriteStrings.NoteStructureFailed);
        }

        return true;
    }

    // An outline command on the heading of the engaged row or of the caret. The plan is made from the model as it is when the operation runs.
    private void RunOutline(EditSession session, string id)
    {
        var pane = OutlineParagraphSource?.Invoke();
        var reason = OutlineReason.None;
        var result = ApplyGuarded(session, ed =>
        {
            var model = _tab.Docx.Model;
            var headings = OutlineSource.ReadHeadings(model);
            var row = pane is not null ? OutlinePlanner.RowOf(headings, pane) : OutlinePlanner.RowAtCaret(headings, model, session.Caret);

            var plan = id switch
            {
                WriteCommandIds.MoveUp => OutlinePlanner.Move(headings, model, row, up: true),
                WriteCommandIds.MoveDown => OutlinePlanner.Move(headings, model, row, up: false),
                WriteCommandIds.Promote => OutlinePlanner.Shift(headings, model, row, -1, ed),
                _ => OutlinePlanner.Shift(headings, model, row, +1, ed),
            };
            reason = plan.Reason;
            if (plan.Reason != OutlineReason.None)
            {
                return null;
            }

            return id switch
            {
                WriteCommandIds.MoveUp or WriteCommandIds.MoveDown => ed.MoveBlocks(plan.First, plan.Count, plan.Target),
                WriteCommandIds.Promote => ed.ShiftOutlineLevels(plan.First, plan.Last, -1),
                _ => ed.ShiftOutlineLevels(plan.First, plan.Last, +1),
            };
        });

        if (result is null && session.BlockedReason is null)
        {
            // The plan said no, or the editor refused after a bounded read that was permissive: a visible note, never silence.
            StructureNote?.Invoke(reason != OutlineReason.None ? StructureWording.OutlineReasonText(reason, id) : WriteStrings.NoteStructureFailed);
        }
    }

    /// <summary>
    /// Moves the chapter of the heading paragraph <paramref name="dragged"/> before or after the heading paragraph <paramref name="onto"/> (the drop of a drag in the outline pane). Both are found again in the
    /// model as it is when the operation runs, and the plan is made there, so a stale list can never move the wrong blocks; a heading that is gone or a place that is not offered any more ends as a note.
    /// True when the drop was taken (also when it changed nothing), false for a released view.
    /// </summary>
    internal bool RunOutlineDrop(Paragraph dragged, Paragraph onto, DropPlace place)
    {
        ArgumentNullException.ThrowIfNull(dragged);
        ArgumentNullException.ThrowIfNull(onto);
        if (_released)
        {
            return false;
        }

        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        var reason = OutlineReason.None;
        var result = ApplyGuarded(session, ed =>
        {
            var model = _tab.Docx.Model;
            var headings = OutlineSource.ReadHeadings(model);
            var plan = OutlinePlanner.Drop(headings, model, OutlinePlanner.RowOf(headings, dragged), OutlinePlanner.RowOf(headings, onto), place);
            reason = plan.Reason;
            return plan.Reason == OutlineReason.None ? ed.MoveBlocks(plan.First, plan.Count, plan.Target) : null;
        });

        if (result is null && session.BlockedReason is null)
        {
            StructureNote?.Invoke(reason != OutlineReason.None ? StructureWording.OutlineReasonText(reason, WriteCommandIds.MoveDown) : WriteStrings.NoteStructureFailed);
        }

        return true;
    }

    // Tab and Shift+Tab. Decided when the operation runs (a key typed during a save is queued like typing and reads the document as it is then): at the start of a list paragraph, or over a selection
    // that touches list paragraphs, the level changes (at the edge nothing does, so the key never falls back to a tab character); anywhere else Tab is a tab character and Shift+Tab is nothing.
    private static Func<EditSession, Func<TextEditor, EditResult?>> TabOperation(int delta) => session => ed =>
    {
        if (session.Selection is not { } selection)
        {
            return null;
        }

        var inList = selection.IsEmpty
            ? selection.End.Offset == 0 && session.ResolveParagraph(selection.End.Story, selection.End.Paragraph).NumberingId > 0
            : true;
        if (inList && ed.ChangeListLevel(selection, delta) is { } moved)
        {
            return moved;
        }

        return delta > 0 ? ed.InsertText(selection, "\t") : null;
    };
}
