namespace Raffica.Write.Layout;

/// <summary>A body block that is ready to be laid out at whatever width the filler asks for. Numbering and style work happen once, when the plan is made.</summary>
internal abstract class BlockPlan
{
    private float _width = float.NaN;
    private IBlockFlow? _flow;

    protected BlockPlan(int bodyIndex) => BodyIndex = bodyIndex;

    public int BodyIndex { get; }

    /// <summary>The flow at this width. The answer for the last width asked is kept, because the filler measures ahead and then places.</summary>
    public IBlockFlow Layout(float width)
    {
        if (_flow is null || _width != width)
        {
            _flow = Make(width);
            _width = width;
        }

        return _flow;
    }

    protected abstract IBlockFlow Make(float width);

    /// <summary>The narrowest this block goes (its longest word) and the width of its content on one line, for an automatic table column.</summary>
    public virtual (float Min, float Max) ContentWidths() => (0f, 0f);
}

/// <summary>A paragraph with its input built; <see cref="Make"/> runs the paragraph layouter.</summary>
internal sealed class ParagraphPlan : BlockPlan
{
    private readonly ParagraphLayouter _layouter;
    private const float MeasureWidth = 20_000f;

    private int[]? _markOffsets;
    private (float Min, float Max)? _widths;

    public ParagraphPlan(int bodyIndex, ParagraphInput input, ParagraphLayouter layouter, bool isContinuation)
        : base(bodyIndex)
    {
        Input = input;
        _layouter = layouter;
        IsContinuation = isContinuation;
    }

    public ParagraphInput Input { get; }

    /// <summary>True for the part of a paragraph after a manual page or column break: no label, no space before.</summary>
    public bool IsContinuation { get; }

    /// <summary>The paragraph after a break: the items from <paramref name="item"/> on, without the list label.</summary>
    public ParagraphPlan After(int item)
    {
        var rest = item >= Input.Items.Count ? [] : Input.Items.Skip(item).ToArray();
        var marks = Input.NoteMarks?.Where(m => m.ItemIndex >= item).Select(m => m with { ItemIndex = m.ItemIndex - item }).ToArray();
        return new ParagraphPlan(BodyIndex, Input with { Items = rest, Label = null, NoteMarks = marks }, _layouter, isContinuation: true);
    }

    /// <summary>The text offset of each note mark of <see cref="ParagraphInput.NoteMarks"/> (counted as the layouter counts text).</summary>
    public int[] MarkOffsets
    {
        get
        {
            if (_markOffsets is { } known)
            {
                return known;
            }

            var marks = Input.NoteMarks ?? [];
            var offsets = new int[marks.Count];
            var items = Input.Items;
            var next = 0;
            var length = 0;
            for (var i = 0; i < items.Count && next < marks.Count; i++)
            {
                for (var k = 0; k < marks.Count; k++)
                {
                    if (marks[k].ItemIndex == i)
                    {
                        offsets[k] = length;
                        next++;
                    }
                }

                length += items[i] switch
                {
                    TextSpan span => span.Run.Hidden ? 0 : span.Text.Length,
                    _ => 1,
                };
            }

            _markOffsets = offsets;
            return offsets;
        }
    }

    protected override IBlockFlow Make(float width) =>
        new ParagraphFlow(this, _layouter.Layout(Input, width), 0, int.MaxValue);

    /// <summary>Laid out at one point and at an unlimited width: the longest word and the whole text on a line.</summary>
    public override (float Min, float Max) ContentWidths()
    {
        if (_widths is { } known)
        {
            return known;
        }

        if (Input.Items.Count == 0)
        {
            return (_widths = (0f, 0f)).Value;
        }

        var probe = Input with { AutoHyphenation = false, ExpandShiftReturn = false };
        var indents = Input.Paragraph.LeftIndent + Input.Paragraph.RightIndent;
        var narrow = Extent(_layouter.Layout(probe, 1f)) + indents;
        var wide = Extent(_layouter.Layout(probe, MeasureWidth)) + indents;
        return (_widths = (TableLimits.Length(narrow), TableLimits.Length(Math.Max(wide, narrow)))).Value;
    }

    // The widest line, measured from where its first thing starts to where its last ends (alignment does not matter).
    private static float Extent(ParagraphLayout layout)
    {
        var widest = 0f;
        foreach (var line in layout.Lines)
        {
            var left = float.MaxValue;
            var right = float.MinValue;
            foreach (var run in line.Runs)
            {
                left = Math.Min(left, run.X);
                right = Math.Max(right, run.X + run.Width);
            }

            foreach (var box in line.Boxes)
            {
                left = Math.Min(left, box.X);
                right = Math.Max(right, box.X + box.Box.Width);
            }

            if (right > left)
            {
                widest = Math.Max(widest, right - left);
            }
        }

        return widest;
    }
}

/// <summary>
/// A range of lines of one laid-out paragraph. Split keeps the rules of Word: widow and orphan control (never one line alone at the
/// bottom or the top of a page, when <c>w:widowControl</c> is on) and "keep lines together".
/// </summary>
internal sealed class ParagraphFlow : IBlockFlow
{
    private readonly ParagraphPlan _plan;
    private readonly ParagraphLayout _layout;
    private readonly int _from;
    private readonly int _to;
    private readonly float _top;
    private IReadOnlyList<NoteRef>? _notes;

    public ParagraphFlow(ParagraphPlan plan, ParagraphLayout layout, int from, int to)
    {
        _plan = plan;
        _layout = layout;
        _from = from;
        _to = Math.Min(to, layout.Lines.Count);
        if (_to > _from)
        {
            _top = layout.Lines[_from].Top;
            var last = layout.Lines[_to - 1];
            Height = last.Top + last.Height - _top;
        }
    }

    private ResolvedParagraph Format => _plan.Input.Paragraph;

    private int LineCount => _to - _from;

    private bool IsLastPiece => _to == _layout.Lines.Count;

    public int BodyIndex => _plan.BodyIndex;

    public bool IsFirstPiece => _from == 0 && !_plan.IsContinuation;

    public float Height { get; }

    public float SpaceBefore => IsFirstPiece ? Format.SpaceBefore : 0f;

    public float SpaceAfter => IsLastPiece ? Format.SpaceAfter : 0f;

    public bool KeepNext => Format.KeepNext;

    public bool PageBreakBefore => IsFirstPiece && Format.PageBreakBefore;

    public string? ContextualGroup => Format.ContextualSpacing ? (Format.StyleId ?? string.Empty) : null;

    public float MinimumHead
    {
        get
        {
            if (LineCount == 0)
            {
                return 0f;
            }

            var count = Format.KeepLines ? LineCount : Math.Min(LineCount, Format.WidowControl ? 2 : 1);
            return HeightOf(_from, _from + count);
        }
    }

    public BreakKind? EndBreak => IsLastPiece ? _layout.EndBreak : null;

    public IReadOnlyList<NoteRef> Notes => _notes ??= FindNotes();

    public IBlockFlow? AfterBreak(float width) =>
        EndBreak is null ? null : _plan.After(_layout.RemainderItem).Layout(width);

    public FlowSplit Split(float available, bool mustProgress)
    {
        if (Height <= available)
        {
            return new FlowSplit(this, null);
        }

        if (LineCount <= 1)
        {
            // One line that does not fit: it stays only when there is nowhere better to put it.
            return mustProgress ? new FlowSplit(this, null) : new FlowSplit(null, this);
        }

        var fits = LinesThatFit(available);
        var count = fits;
        if (Format.KeepLines)
        {
            count = 0;
        }
        else if (Format.WidowControl)
        {
            count = WithWidowAndOrphan(fits);
        }

        if (count == 0 && mustProgress)
        {
            // At the top of an empty column the paragraph has to be cut somewhere. Keep the rules where they allow it, drop them where they do not.
            count = Format.WidowControl ? WithWidowAndOrphan(fits) : fits;
            if (count == 0)
            {
                count = Math.Max(1, fits);
            }
        }

        if (count == 0)
        {
            return new FlowSplit(null, this);
        }

        if (count >= LineCount)
        {
            return new FlowSplit(this, null);
        }

        return new FlowSplit(new ParagraphFlow(_plan, _layout, _from, _from + count), new ParagraphFlow(_plan, _layout, _from + count, _to));
    }

    public void Emit(PageBuilder page, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(page);
        for (var i = _from; i < _to; i++)
        {
            var line = _layout.Lines[i];
            var lineTop = y + (line.Top - _top);
            LineItems.Emit(page, line, x, lineTop);
            var text = _layout.TextOf(line);
            page.AddLine(i == 0 && IsFirstPiece && _plan.Input.Label is { } label ? label.Text + " " + text : text);
        }
    }

    // The note marks whose number is drawn on one of this piece's lines.
    private List<NoteRef> FindNotes()
    {
        var marks = _plan.Input.NoteMarks;
        if (marks is null || marks.Count == 0 || _to <= _from)
        {
            return [];
        }

        var start = _layout.Lines[_from].Start;
        var end = _layout.Lines[_to - 1].End;
        var offsets = _plan.MarkOffsets;
        List<NoteRef>? found = null;
        for (var i = 0; i < marks.Count; i++)
        {
            if (offsets[i] >= start && offsets[i] < end)
            {
                (found ??= []).Add(marks[i].Note);
            }
        }

        return found ?? [];
    }

    // How many lines from the start of this range fit into the room.
    private int LinesThatFit(float available)
    {
        var count = 0;
        while (count < LineCount && HeightOf(_from, _from + count + 1) <= available)
        {
            count++;
        }

        return count;
    }

    // Orphan control: at least two lines stay at the bottom; widow control: at least two lines go on. A paragraph too short for both moves whole.
    private int WithWidowAndOrphan(int fits)
    {
        if (fits >= LineCount)
        {
            return fits;
        }

        var count = fits;
        if (count < 2)
        {
            return 0;
        }

        if (LineCount - count < 2)
        {
            count = LineCount - 2;
            if (count < 2)
            {
                return 0;
            }
        }

        return count;
    }

    private float HeightOf(int from, int to)
    {
        var last = _layout.Lines[to - 1];
        return last.Top + last.Height - _layout.Lines[from].Top;
    }
}
