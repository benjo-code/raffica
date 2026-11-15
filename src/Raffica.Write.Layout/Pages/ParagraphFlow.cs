namespace Raffica.Write.Layout;

/// <summary>A body block that is ready to be laid out at whatever width the filler asks for. Numbering and style work happen once, when the plan is made.</summary>
internal abstract class BlockPlan
{
    private float _width = float.NaN;
    private IBlockFlow? _flow;

    protected BlockPlan(int bodyIndex) => BodyIndex = bodyIndex;

    public int BodyIndex { get; private set; }

    /// <summary>
    /// Moves the plan to another body index (a block was inserted or removed before it). The flow memoised for the last width carries the
    /// old index, so it is dropped and made again on the next <see cref="Layout"/>.
    /// </summary>
    public virtual void Rebase(int bodyIndex)
    {
        if (BodyIndex != bodyIndex)
        {
            BodyIndex = bodyIndex;
            _flow = null;
        }
    }

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

        // A floating object anchored after the break belongs to the part after it.
        var floats = Input.Floats?.Where(f => f.ItemIndex >= item).Select(f => f.WithItemIndex(f.ItemIndex - item)).ToArray();
        var map = (Input.ProjectionMap ?? OffsetMap.Identity).Slice(LayoutLength(Input.Items, item));
        return new ParagraphPlan(BodyIndex, Input with { Items = rest, Label = null, NoteMarks = marks, Floats = floats is { Length: > 0 } ? floats : null, ProjectionMap = map }, _layouter, isContinuation: true);
    }

    /// <summary>
    /// The length of the layout text of the first <paramref name="count"/> items (a hidden span has none, every box and break counts one).
    /// This is the counting rule of <c>ParagraphLayouter</c> (a hidden text span adds 0, everything else adds its text length or 1): the
    /// two must stay equal, because the continuation after a break slices the projection map at this length.
    /// </summary>
    internal static int LayoutLength(IReadOnlyList<ParagraphItem> items, int count) => LayoutLength(items, 0, count);

    /// <summary>The length of the layout text of the items from <paramref name="from"/> up to <paramref name="to"/>.</summary>
    internal static int LayoutLength(IReadOnlyList<ParagraphItem> items, int from, int to)
    {
        var length = 0;
        for (var i = from; i < to && i < items.Count; i++)
        {
            length += items[i] switch
            {
                TextSpan span => span.Run.Hidden ? 0 : span.Text.Length,
                _ => 1,
            };
        }

        return length;
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
        new ParagraphFlow(this, LayOut(width, null), 0, int.MaxValue);

    /// <summary>The paragraph laid out at <paramref name="width"/>, with its lines narrowed by <paramref name="wrap"/> when it is not null. Not cached.</summary>
    internal ParagraphLayout LayOut(float width, IWrapSource? wrap) => _layouter.Layout(Input, width, wrap);

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

        var probe = Input with { AutoHyphenation = false, ExpandShiftReturn = false, ScaleBoxes = false };
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
internal sealed class ParagraphFlow : IBlockFlow, IFloatAware
{
    private readonly ParagraphPlan _plan;
    private readonly ParagraphLayout _layout;
    private readonly int _from;
    private readonly int _to;
    private readonly float _top;
    private readonly bool _placed;
    private readonly IReadOnlyList<FloatBox>? _boxes;
    private IReadOnlyList<NoteRef>? _notes;

    public ParagraphFlow(ParagraphPlan plan, ParagraphLayout layout, int from, int to, bool placed = false, IReadOnlyList<FloatBox>? boxes = null)
    {
        _plan = plan;
        _layout = layout;
        _from = from;
        _to = Math.Min(to, layout.Lines.Count);
        _placed = placed;
        _boxes = boxes;
        if (_to > _from)
        {
            // The first piece starts at the top of the paragraph, so room that wrapping left above its first line is part of it.
            _top = from == 0 ? 0f : layout.Lines[_from].Top;
            var last = layout.Lines[_to - 1];
            Height = last.Top + last.Height - _top;
        }
    }

    private ResolvedParagraph Format => _plan.Input.Paragraph;

    private int LineCount => _to - _from;

    private bool IsLastPiece => _to == _layout.Lines.Count;

    public int BodyIndex => _plan.BodyIndex;

    public bool IsFirstPiece => _from == 0 && !_plan.IsContinuation;

    public int StartLine => _from;

    // The same plan, the same lines of it at the same height. A layout that was not made for a place (no wrapping around objects) is a function
    // of the plan and the width, so two of them are equal even when they are not the same object (a plan that moved to another body index makes
    // its flow again); a layout made for a place is compared by reference.
    public bool SamePieceAs(IBlockFlow? other) =>
        other is ParagraphFlow flow
        && ReferenceEquals(_plan, flow._plan)
        && _from == flow._from
        && _to == flow._to
        && _placed == flow._placed
        && Height == flow.Height
        && _layout.Lines.Count == flow._layout.Lines.Count
        && (ReferenceEquals(_layout, flow._layout) || !_placed);

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

        return new FlowSplit(
            new ParagraphFlow(_plan, _layout, _from, _from + count, _placed, _from == 0 ? _boxes : null),
            new ParagraphFlow(_plan, _layout, _from + count, _to, _placed));
    }

    public IReadOnlyList<FloatBox> Owned => _boxes ?? [];

    /// <summary>
    /// True for a whole paragraph that has not been placed yet and either holds floating objects or sits where an object of the page
    /// reaches: its lines depend on where it lands, so it is laid out again at its place (<see cref="AtPlace"/>). Every other
    /// paragraph keeps the layout it was planned with.
    /// </summary>
    public bool NeedsPlacement(PageFloats page, float y)
    {
        if (_placed || _from != 0 || LineCount == 0)
        {
            return false;
        }

        return _plan.Input.Floats is { Count: > 0 } || page.Reaches(y, y + Height);
    }

    public IBlockFlow AtPlace(FloatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Objects anchored after a manual page or column break belong to the part after it (ParagraphPlan.After hands them on), not to this one.
        var specs = _plan.Input.Floats?.Where(f => _layout.EndBreak is null || f.ItemIndex < _layout.RemainderItem).ToList();
        var boxes = specs is { Count: > 0 } ? FloatPlanner.Place(specs, context) : [];
        var layout = _layout;
        List<FloatBox> obstacles = [.. context.Page.Boxes];
        obstacles.AddRange(boxes.Where(b => b.Wraps));
        var top = context.Y;
        if (obstacles.Any(b => b.ClearTop < top + Height && b.ClearBottom > top))
        {
            layout = _plan.LayOut(context.ColumnWidth, new WrapSource(obstacles, context.ColumnLeft, context.Y, context.Bottom));
        }

        return new ParagraphFlow(_plan, layout, 0, int.MaxValue, true, boxes);
    }

    public void Emit(PageBuilder page, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(page);
        for (var i = _from; i < _to; i++)
        {
            var line = _layout.Lines[i];
            var lineTop = y + (line.Top - _top);
            LineItems.Emit(page, line, x, lineTop);
            if (_plan.Input is { Source: { } source, Projection: { } projection } input)
            {
                page.AddTextLine(new TextLine(source.Story, source.Paragraph, projection, RunsOf(line), StartOf(input, line, i), EndOf(input, line, i), x, lineTop, lineTop + line.Baseline, line.Height, input.ProjectionMap ?? OffsetMap.Identity, source.Cell, line.X, line.X + line.Width, input.Host));
            }

            var text = _layout.TextOf(line);
            page.AddLine(i == 0 && IsFirstPiece && _plan.Input.Label is { } label ? label.Text + " " + text : text);
        }

        if (_boxes is not null)
        {
            // Objects in front of the text are drawn after it; the ones behind it go to their own region.
            foreach (var box in _boxes)
            {
                box.Emit(page);
            }
        }
    }

    private static GlyphRun[] RunsOf(LineBox line) => line.Runs as GlyphRun[] ?? [.. line.Runs];

    // The first line of a paragraph starts at 0 (a leading character the layout gave no room belongs to it); a continuation starts where its map says.
    private int StartOf(ParagraphInput input, LineBox line, int index) =>
        index == 0 && !_plan.IsContinuation ? 0 : (input.ProjectionMap ?? OffsetMap.Identity).Map(line.Start);

    // The last line of a paragraph ends at the end of the projection text; a line that ends in a manual break ends where the continuation starts.
    private int EndOf(ParagraphInput input, LineBox line, int index) =>
        _layout.EndBreak is null && index == _layout.Lines.Count - 1 ? input.Projection!.Text.Length : (input.ProjectionMap ?? OffsetMap.Identity).Map(line.End);

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
        return last.Top + last.Height - (from == _from ? _top : _layout.Lines[from].Top);
    }
}
