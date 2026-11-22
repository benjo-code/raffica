using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;

namespace Raffica.Write.Commands;

/// <summary>Why an outline command (move, promote, demote) has nothing to act on or cannot run now (<see cref="None"/>: it can).</summary>
internal enum OutlineReason
{
    None,

    /// <summary>No heading to act on: the caret is before the first heading, outside the body, or the outline row is gone.</summary>
    NoHeading,

    /// <summary>A paragraph of the subtree ends a section: the section break would travel with it.</summary>
    SectionBreakInside,

    /// <summary>A heading of the subtree stands inside a block content control.</summary>
    InsideContentControl,

    /// <summary>The heading is not a body-level paragraph.</summary>
    NotAtTopLevel,

    /// <summary>The chapter is the first one: nothing is above it to move past.</summary>
    AtTop,

    /// <summary>The chapter is the last one: nothing is below it to move past.</summary>
    AtEnd,

    /// <summary>The move would change nothing or is not offered (the place lies inside the chapter, or a table would end the body).</summary>
    CannotMoveThere,

    /// <summary>A level of the range comes from a style that is not Heading 1 to 9, or the style it would move to is missing.</summary>
    NotAHeadingStyle,

    /// <summary>A heading would leave levels 1 to 9.</summary>
    OutOfRange,

    /// <summary>No paragraph of the range has an outline level.</summary>
    NothingToShift,
}

/// <summary>
/// What the rules of the structure commands need to know about the active tab: the facts of the list commands for the selection, the state of the heading numbering switch and, for each outline
/// command, whether it can run on the heading it would act on (the selected outline row when the pane is engaged, else the heading at the caret).
/// </summary>
internal sealed record StructureFacts(ListFacts List, HeadingNumberingState Headings, OutlineReason MoveUp, OutlineReason MoveDown, OutlineReason Promote, OutlineReason Demote)
{
    /// <summary>The facts of a tab that has not been asked: every command is offered (the command itself decides when it runs).</summary>
    public static StructureFacts Permissive { get; } = new(
        new ListFacts(true, null, ListReason.None, true, ListReason.None, true, ListReason.None),
        HeadingNumberingState.Off,
        OutlineReason.None,
        OutlineReason.None,
        OutlineReason.None,
        OutlineReason.None);
}

/// <summary>The words for the reasons of the structure commands (the rules show them as the reason of a disabled command, the page view as the note of a command that did nothing).</summary>
internal static class StructureWording
{
    /// <summary>The words for a refusal of a list command.</summary>
    public static string ListReasonText(ListReason reason) => reason switch
    {
        ListReason.SelectionAcrossContainers => WriteStrings.ReasonListAcrossContainers,
        ListReason.NothingBefore => WriteStrings.ReasonNothingToContinue,
        ListReason.AlreadyContinues => WriteStrings.ReasonAlreadyContinues,
        _ => WriteStrings.ReasonNotInAList,
    };

    /// <summary>The words for a refusal of an outline command (<paramref name="id"/> tells promote from demote for a level out of range).</summary>
    public static string OutlineReasonText(OutlineReason reason, string id) => reason switch
    {
        OutlineReason.NoHeading => WriteStrings.ReasonNoHeadingAtCaret,
        OutlineReason.SectionBreakInside => WriteStrings.ReasonMoveSectionBreak,
        OutlineReason.InsideContentControl => WriteStrings.ReasonMoveInControl,
        OutlineReason.NotAtTopLevel => WriteStrings.ReasonMoveNotTopLevel,
        OutlineReason.AtTop => WriteStrings.ReasonMoveAtTop,
        OutlineReason.AtEnd => WriteStrings.ReasonMoveAtEnd,
        OutlineReason.NotAHeadingStyle => WriteStrings.ReasonShiftNotHeadingStyle,
        OutlineReason.OutOfRange => id == WriteCommandIds.Promote ? WriteStrings.ReasonPromoteOutOfRange : WriteStrings.ReasonDemoteOutOfRange,
        OutlineReason.NothingToShift => WriteStrings.ReasonShiftNothing,
        _ => WriteStrings.ReasonMoveCannot,
    };
}

/// <summary>
/// Reads <see cref="StructureFacts"/> for the rules and the switches of the ribbon. Nothing exists before the first question, and nothing is read for a tab without an editing session (the facts are
/// permissive then). The answer is remembered for the model, its revision, the selection and the outline row in play, so asking again while none of them changed costs nothing; the headings are read
/// again once per revision, never per selection. The asks come from the debounced refresh of the ribbon, never from the key path.
/// </summary>
internal sealed class StructureFactsSource
{
    private ((WordDocument Model, int Revision, TextRange Range, Paragraph? Row) Key, StructureFacts Facts)? _known;
    private ((WordDocument Model, int Revision) Key, IReadOnlyList<OutlineHeading> Headings)? _headings;

    /// <summary>How many times the model was read instead of answering from memory (tests).</summary>
    public int Reads { get; private set; }

    /// <summary>The facts for the tab, or the permissive ones while it has no session, cannot be edited now or has no selection. <paramref name="row"/> is the paragraph of the engaged outline row, or null.</summary>
    public StructureFacts Read(WriteTab? tab, Paragraph? row)
    {
        if (tab?.Editor is not { BlockedReason: null, Selection: { } range } session)
        {
            return StructureFacts.Permissive;
        }

        try
        {
            var model = tab.Docx.Model;
            var key = (model, model.Edits.Revision, range, row);
            if (_known is { } known && known.Key.Equals(key))
            {
                return known.Facts;
            }

            var facts = Compute(session, model, range, row);
            Reads++;
            _known = (key, facts);
            return facts;
        }
        catch (StalePositionException)
        {
            // A selection that does not belong to the model any more has no facts; the next selection or edit reads again.
            return StructureFacts.Permissive;
        }
    }

    private StructureFacts Compute(EditSession session, WordDocument model, TextRange range, Paragraph? row)
    {
        var list = session.ReadListFacts(range);
        var headings = HeadingNumbering.StateOf(model);
        var all = HeadingsOf(model);
        var target = row is not null ? FindRow(all, row) : OutlinePlanner.RowAtCaret(all, model, range.End);
        if (target < 0)
        {
            return new StructureFacts(list, headings, OutlineReason.NoHeading, OutlineReason.NoHeading, OutlineReason.NoHeading, OutlineReason.NoHeading);
        }

        var editor = session.Editor;
        return new StructureFacts(
            list,
            headings,
            OutlinePlanner.Move(all, model, target, up: true).Reason,
            OutlinePlanner.Move(all, model, target, up: false).Reason,
            OutlinePlanner.Shift(all, model, target, -1, editor).Reason,
            OutlinePlanner.Shift(all, model, target, +1, editor).Reason);
    }

    private IReadOnlyList<OutlineHeading> HeadingsOf(WordDocument model)
    {
        var key = (model, model.Edits.Revision);
        if (_headings is { } known && known.Key.Equals(key))
        {
            return known.Headings;
        }

        var headings = OutlineSource.ReadHeadings(model);
        _headings = (key, headings);
        return headings;
    }

    private static int FindRow(IReadOnlyList<OutlineHeading> headings, Paragraph paragraph)
    {
        for (var i = 0; i < headings.Count; i++)
        {
            if (ReferenceEquals(headings[i].Paragraph, paragraph))
            {
                return i;
            }
        }

        return -1;
    }
}
