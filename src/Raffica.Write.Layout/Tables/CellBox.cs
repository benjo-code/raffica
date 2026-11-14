namespace Raffica.Write.Layout;

/// <summary>
/// The blocks of one cell, laid out at the cell's width and stacked: what a cell shows, or the part of it that is left after a cut.
/// Flows are immutable, so is a stack; <see cref="Split"/> makes two new ones that share the flows (and their line layouts) that
/// were not cut.
/// </summary>
internal sealed class CellStack
{
    private const float Epsilon = 0.01f;

    public static readonly CellStack Empty = new([], continuation: false, trailingSpace: true);

    private readonly IBlockFlow[] _flows;
    private readonly float[] _tops;
    private IReadOnlyList<NoteRef>? _notes;

    private CellStack(IBlockFlow[] flows, bool continuation, bool trailingSpace)
    {
        _flows = flows;
        _tops = new float[flows.Length];
        var y = 0f;
        var after = 0f;
        string? group = null;
        for (var i = 0; i < flows.Length; i++)
        {
            var flow = flows[i];
            var gap = i == 0
                ? (continuation ? 0f : flow.SpaceBefore)
                : flow.ContextualGroup is { } same && same == group ? 0f : after + flow.SpaceBefore;
            _tops[i] = y + gap;
            y = _tops[i] + flow.Height;
            after = flow.SpaceAfter;
            group = flow.ContextualGroup;
        }

        Height = y + (trailingSpace ? after : 0f);
    }

    /// <summary>Everything the stack holds is as high as this, including the space before the first block and after the last.</summary>
    public float Height { get; }

    public bool HasContent => _flows.Length > 0;

    /// <summary>The footnote references drawn by the blocks of this stack.</summary>
    public IReadOnlyList<NoteRef> Notes
    {
        get
        {
            if (_notes is not null)
            {
                return _notes;
            }

            List<NoteRef>? found = null;
            foreach (var flow in _flows)
            {
                var notes = flow.Notes;
                if (notes.Count > 0)
                {
                    (found ??= []).AddRange(notes);
                }
            }

            return _notes = found ?? [];
        }
    }

    /// <summary>The stack of the blocks of a cell at <paramref name="width"/>. A page or column break inside a paragraph does not break the cell: the rest of the paragraph follows as the next block.</summary>
    public static CellStack Build(IReadOnlyList<BlockPlan> plans, float width)
    {
        if (plans.Count == 0)
        {
            return Empty;
        }

        var flows = new List<IBlockFlow>(plans.Count);
        foreach (var plan in plans)
        {
            var flow = plan.Layout(width);
            flows.Add(flow);
            for (var guard = 0; guard < 64 && flow.EndBreak is not null && flow.AfterBreak(width) is { } rest; guard++)
            {
                flows.Add(rest);
                flow = rest;
            }
        }

        return new CellStack([.. flows], continuation: false, trailingSpace: true);
    }

    /// <summary>
    /// Cuts the stack so that the head is at most <paramref name="available"/> points high, at a block boundary or inside a block
    /// where the block allows it. The head is null when nothing stays (only without <paramref name="mustProgress"/>), the tail is
    /// null when everything fits. With <paramref name="mustProgress"/> a head always holds some content.
    /// </summary>
    public (CellStack? Head, CellStack? Tail) Split(float available, bool mustProgress)
    {
        if (_flows.Length == 0 || Height <= available + Epsilon)
        {
            return (this, null);
        }

        for (var i = 0; i < _flows.Length; i++)
        {
            var flow = _flows[i];
            if (_tops[i] + flow.Height <= available + Epsilon)
            {
                continue;
            }

            var cut = flow.Split(available - _tops[i], mustProgress && i == 0);
            if (cut.Head is not null && cut.Tail is null)
            {
                continue;
            }

            var head = new List<IBlockFlow>(i + 1);
            for (var k = 0; k < i; k++)
            {
                head.Add(_flows[k]);
            }

            if (cut.Head is { } part)
            {
                head.Add(part);
            }

            if (head.Count == 0)
            {
                return (null, this);
            }

            var tail = new List<IBlockFlow>(_flows.Length - i);
            if (cut.Tail is { } rest)
            {
                tail.Add(rest);
            }

            for (var k = i + 1; k < _flows.Length; k++)
            {
                tail.Add(_flows[k]);
            }

            return (new CellStack([.. head], continuation: false, trailingSpace: false), tail.Count == 0 ? null : new CellStack([.. tail], continuation: true, trailingSpace: true));
        }

        return (this, null);
    }

    public void Emit(PageBuilder page, float x, float y)
    {
        for (var i = 0; i < _flows.Length; i++)
        {
            _flows[i].Emit(page, x, y + _tops[i]);
        }
    }
}

/// <summary>A cell of a laid-out row: its place across the table and the stack of its content (empty for a cell that continues a vertical merge).</summary>
internal sealed class CellBox
{
    public CellBox(CellModel model, float x, float width, CellStack stack)
    {
        Model = model;
        X = x;
        Width = width;
        Stack = stack;
        Need = model.IsContinuation ? 0f : stack.Height + model.MarginTop + model.MarginBottom;
    }

    public CellModel Model { get; }

    /// <summary>From the left edge of the table.</summary>
    public float X { get; }

    public float Width { get; }

    public CellStack Stack { get; }

    /// <summary>The height the content asks for, with the cell margins.</summary>
    public float Need { get; }
}

/// <summary>A row laid out at the table's column widths: its cells and its height.</summary>
internal sealed class RowLayout
{
    private RowFragment? _whole;

    public RowLayout(RowModel model, int index, CellBox[] cells, float height)
    {
        Model = model;
        Index = index;
        Cells = cells;
        Height = height;
    }

    public RowModel Model { get; }

    public int Index { get; }

    public CellBox[] Cells { get; }

    public float Height { get; }

    /// <summary>The row as one fragment (all content, full height).</summary>
    public RowFragment Whole => _whole ??= new RowFragment(this, [.. Cells.Select(c => c.Stack)], Height);
}

/// <summary>A row, or a piece of one that was cut at a page end: what each cell still shows and how high that is.</summary>
internal sealed class RowFragment
{
    public RowFragment(RowLayout row, CellStack?[] stacks, float height)
    {
        Row = row;
        Stacks = stacks;
        Height = height;
    }

    public RowLayout Row { get; }

    /// <summary>One entry per cell of the row; null or empty where the cell has nothing left to show.</summary>
    public CellStack?[] Stacks { get; }

    public float Height { get; }

    /// <summary>
    /// Cuts the row so that the head is at most <paramref name="room"/> points high: each cell is cut inside its content. Null when
    /// that gives nothing useful (no cell has content left over, or no cell has any content that stays).
    /// </summary>
    public (RowFragment Head, RowFragment Tail)? Split(float room, bool forced)
    {
        var count = Stacks.Length;
        var heads = new CellStack?[count];
        var tails = new CellStack?[count];
        float headNeed = 0f, tailNeed = 0f;
        var anyHead = false;
        var anyTail = false;
        for (var k = 0; k < count; k++)
        {
            var stack = Stacks[k];
            var model = Row.Cells[k].Model;
            if (stack is null || !stack.HasContent || model.IsContinuation)
            {
                continue;
            }

            var margins = model.MarginTop + model.MarginBottom;
            var (head, tail) = stack.Split(room - margins, forced);
            if (head is { HasContent: true })
            {
                heads[k] = head;
                headNeed = Math.Max(headNeed, head.Height + margins);
                anyHead = true;
            }

            if (tail is { HasContent: true })
            {
                tails[k] = tail;
                tailNeed = Math.Max(tailNeed, tail.Height + margins);
                anyTail = true;
            }
        }

        if (!anyHead || !anyTail)
        {
            return null;
        }

        var headHeight = Math.Max(headNeed, Math.Min(room, Height));
        var tailHeight = Math.Max(tailNeed, Height - headHeight);
        return (new RowFragment(Row, heads, headHeight), new RowFragment(Row, tails, tailHeight));
    }
}
