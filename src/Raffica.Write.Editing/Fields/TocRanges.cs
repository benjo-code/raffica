using System.Text;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Why an edit near a table of contents was refused: it would change something inside the field, or only part of it.</summary>
public enum FieldRefusalKind
{
    /// <summary>The caret (a typed character, Enter, a paste at a collapsed selection) is in the field, edges included.</summary>
    InsideToc,

    /// <summary>The selection takes part of the field: the field goes whole or not at all.</summary>
    PartOfToc,
}

/// <summary>The refusal of an edit that touches a table of contents (<see cref="TextEditor.LastFieldRefusal"/>); <see cref="Reason"/> is the sentence a view shows.</summary>
public sealed record FieldRefusal(FieldRefusalKind Kind)
{
    /// <summary>The reason a table of contents cannot be edited by hand.</summary>
    public const string TocReason = "This table of contents updates itself. Change the headings, or remove it.";

    public string Reason { get; } = TocReason;
}

/// <summary>A <c>TOC</c> field that spans more than one paragraph of one list of blocks: the begin, the instruction and the separate are in the first paragraph, the end in the last.</summary>
/// <param name="First">The index of the first paragraph (it holds the begin).</param>
/// <param name="Last">The index of the last paragraph (it holds the end).</param>
internal sealed record TocRange(int First, int Last);

/// <summary>
/// Finds the table of contents of a list of blocks. <see cref="FieldScanner"/> reads one paragraph at a time, so a field whose begin and end are in different paragraphs is an unclosed field there; this walk follows the
/// field marks over the paragraphs of one list (the same marks the scanner reads: begin, instruction text, separate, end, in the runs of links, simple fields, controls and tracked changes) and keeps only the fields
/// named <c>TOC</c> that close. A table of contents inside a table cell or a block content control is found when that list is asked for. Costs one pass over the list and allocates only when a field is open.
/// </summary>
internal static class TocRanges
{
    /// <summary>The table of contents of the list, in order; null when it has none.</summary>
    public static List<TocRange>? In(IList<Block> container)
    {
        ArgumentNullException.ThrowIfNull(container);
        List<TocRange>? found = null;
        var start = 0;

        // A field that is never closed (an orphan begin) would swallow every later field: when a pass ends with a field still open, the paragraph that opened it is skipped and the pass starts again after it.
        // The number of restarts is bounded so a list full of orphans costs a bounded time.
        for (var restarts = 0; restarts <= MaxRestarts; restarts++)
        {
            var state = new State();
            for (var i = start; i < container.Count; i++)
            {
                if (container[i] is not Paragraph paragraph)
                {
                    continue;
                }

                state.Index = i;
                Walk(paragraph.Inlines, state);
                if (state.Closed is { } closed)
                {
                    (found ??= []).Add(closed);
                    state.Closed = null;
                }
            }

            if (state.Depth == 0 || state.OutermostBegin < 0)
            {
                break;
            }

            start = state.OutermostBegin + 1;
        }

        return found;
    }

    private const int MaxRestarts = 256;

    /// <summary>The table of contents of the list that holds the paragraph at <paramref name="index"/>, or null.</summary>
    public static TocRange? Around(IList<Block> container, int index) => In(container)?.FirstOrDefault(r => index >= r.First && index <= r.Last);

    private sealed class State
    {
        public int Index { get; set; }

        public int Depth { get; set; }

        public int Candidate { get; set; } = -1;

        // The paragraph of the begin that opened the field that is open now (the outermost one), or -1.
        public int OutermostBegin { get; set; } = -1;

        public int TocFirst { get; set; } = -1;

        public bool Collecting { get; set; }

        public StringBuilder? Instruction { get; set; }

        public TocRange? Closed { get; set; }
    }

    private static void Walk(IList<Inline> inlines, State state)
    {
        for (var i = 0; i < inlines.Count; i++)
        {
            if (inlines[i] is Run run)
            {
                for (var c = 0; c < run.Content.Count; c++)
                {
                    Mark(run.Content[c], state);
                }
            }
            else if (ParagraphMap.ChildrenOf(inlines[i]) is { } children)
            {
                Walk(children, state);
            }
        }
    }

    private static void Mark(RunContent content, State state)
    {
        switch (content)
        {
            case FieldChar { Kind: FieldCharKind.Begin }:
                state.Depth++;
                if (state.Depth == 1)
                {
                    state.Candidate = state.Index;
                    state.OutermostBegin = state.Index;
                    state.Collecting = true;
                    state.Instruction = null;
                }

                break;
            case InstructionText text when state.Depth == 1 && state.Collecting:
                (state.Instruction ??= new StringBuilder()).Append(text.Text);
                break;
            case FieldChar { Kind: FieldCharKind.Separate } when state.Depth == 1 && state.Collecting:
                state.Collecting = false;
                state.TocFirst = state.Instruction is { } instruction && FieldInstruction.Parse(instruction.ToString()).Name == "TOC" ? state.Candidate : -1;
                break;
            case FieldChar { Kind: FieldCharKind.End } when state.Depth > 0:
                state.Depth--;
                if (state.Depth == 0)
                {
                    if (state.TocFirst >= 0 && state.Index > state.TocFirst)
                    {
                        state.Closed = new TocRange(state.TocFirst, state.Index);
                    }

                    state.TocFirst = -1;
                    state.Collecting = false;
                    state.OutermostBegin = -1;
                }

                break;
            default:
                break;
        }
    }
}
