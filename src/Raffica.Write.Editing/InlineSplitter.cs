using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Cuts a paragraph's inline tree at a place in the flat list of its run contents: the paragraph keeps everything before the cut and the
/// rest is returned. A container (a link, a content control, a tracked insertion) that the cut passes through is divided in two with the
/// same properties; a run is divided with the same properties.
/// </summary>
internal static class InlineSplitter
{
    /// <param name="paragraph">The paragraph to cut; its inlines become the part before the cut.</param>
    /// <param name="item">The flat index of the first content that goes to the second part (the count of items cuts at the end).</param>
    /// <param name="chars">Characters of that item's text that stay in the first part (0 cuts before the item).</param>
    public static List<Inline> Split(Paragraph paragraph, int item, int chars)
    {
        var state = new State(item, chars);
        return SplitList(paragraph.Inlines, state);
    }

    private static List<Inline> SplitList(IList<Inline> list, State state)
    {
        var head = new List<Inline>(list.Count);
        var tail = new List<Inline>();
        foreach (var inline in list)
        {
            if (state.InTail)
            {
                tail.Add(inline);
                continue;
            }

            if (inline is Run run)
            {
                SplitRun(run, state, head, tail);
                continue;
            }

            if (ParagraphMap.ChildrenOf(inline) is not { } children)
            {
                head.Add(inline);
                continue;
            }

            var rest = SplitList(children, state);
            if (rest.Count == 0)
            {
                head.Add(inline);
            }
            else if (children.Count == 0)
            {
                foreach (var moved in rest)
                {
                    children.Add(moved);
                }

                tail.Add(inline);
            }
            else
            {
                var copy = inline.Clone();
                var copyChildren = ParagraphMap.ChildrenOf(copy)!;
                copyChildren.Clear();
                foreach (var moved in rest)
                {
                    copyChildren.Add(moved);
                }

                head.Add(inline);
                tail.Add(copy);
            }
        }

        list.Clear();
        foreach (var kept in head)
        {
            list.Add(kept);
        }

        return tail;
    }

    private static void SplitRun(Run run, State state, List<Inline> head, List<Inline> tail)
    {
        var first = state.Counter;
        var end = first + run.Content.Count;
        state.Counter = end;
        if (end == first || end <= state.Item)
        {
            head.Add(run);
            return;
        }

        if (first > state.Item || (first == state.Item && state.Chars == 0))
        {
            tail.Add(run);
            state.InTail = true;
            return;
        }

        var local = state.Item - first;
        var moved = run.Clone();
        moved.Content.Clear();
        if (state.Chars > 0)
        {
            var text = (TextContent)run.Content[local];
            var rest = text.Clone();
            rest.Text = text.Text[state.Chars..];
            text.Text = text.Text[..state.Chars];
            moved.Content.Add(rest);
            local++;
        }

        for (var i = local; i < run.Content.Count; i++)
        {
            moved.Content.Add(run.Content[i]);
        }

        for (var i = run.Content.Count - 1; i >= local; i--)
        {
            run.Content.RemoveAt(i);
        }

        head.Add(run);
        tail.Add(moved);
        state.InTail = true;
    }

    private sealed class State(int item, int chars)
    {
        public int Item { get; } = item;

        public int Chars { get; } = chars;

        public int Counter { get; set; }

        public bool InTail { get; set; }
    }
}
