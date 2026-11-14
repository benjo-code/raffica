namespace Raffica.Write.Layout;

/// <summary>A piece of a block at its place on the page: the top left corner of its box.</summary>
internal readonly record struct Placed(IBlockFlow Flow, float X, float Y, int Column);

internal enum PlaceStatus
{
    /// <summary>The flow and all of its pieces are placed.</summary>
    Placed,

    /// <summary>The band is full: the page ends, and <see cref="PlaceResult.Carry"/> goes on the next page.</summary>
    NewPage,
}

internal readonly record struct PlaceResult(PlaceStatus Status, IBlockFlow? Carry, bool SuppressSpaceBefore);

/// <summary>Gives the filler a look at the blocks that follow the one being placed, to keep a heading with its text.</summary>
internal interface IFlowLookahead
{
    /// <summary>The flow <paramref name="ahead"/> places after the next one to place (0 is the next one), laid out at <paramref name="width"/>; null past the end of the section.</summary>
    IBlockFlow? Peek(int ahead, float width);
}

/// <summary>
/// The columns of one section on one page, from a top y down to a bottom y: where the next piece goes, how spacing collapses at its
/// top and what moves to the next column. A page has one band per section that starts on it (a continuous section break starts
/// a new band below the last). The same class runs the trial fills that balance columns.
/// </summary>
internal sealed class Band
{
    private const int MaximumChain = 8;
    private const float Epsilon = 0.01f;

    private readonly float _bottom;
    private bool _columnEmpty = true;
    private bool _suppress;
    private float _prevAfter;
    private string? _prevGroup;

    public Band(PageGeometry geometry, float top, float bottom, bool suppressSpaceBefore, bool pageHadContent, PageNotes? notes = null)
    {
        Geometry = geometry;
        Top = top;
        _bottom = bottom;
        Notes = notes;
        Y = top;
        MaxY = top;
        _suppress = suppressSpaceBefore;
        InitialSuppress = suppressSpaceBefore;
        PageHadContent = pageHadContent;
    }

    public PageGeometry Geometry { get; }

    public float Top { get; }

    /// <summary>The footnotes of the page this band is on (shared by every band of the page); null in a trial fill and in the writing view.</summary>
    public PageNotes? Notes { get; }

    /// <summary>Where the body ends now: the bottom of the text area less the room the footnotes of the page have taken so far.</summary>
    public float Bottom => _bottom - (Notes?.Reserved ?? 0f);

    public int Column { get; private set; }

    public float Y { get; private set; }

    /// <summary>The lowest bottom of any piece placed in any column (the end of the band).</summary>
    public float MaxY { get; set; }

    public bool InitialSuppress { get; }

    /// <summary>True when something was already placed on the page before this band (a page break before a block is then a real break).</summary>
    public bool PageHadContent { get; }

    /// <summary>
    /// True when this band starts at the top of the body of a page that has nothing on it yet. Only there must a flow give a head
    /// that does not fit (so content bigger than a page is clipped); anywhere else a flow that does not fit goes to the next
    /// column or page, never into the bottom margin.
    /// </summary>
    public bool AtPageTop => !PageHadContent && Column == 0;

    /// <summary>True in a trial fill: page breaks before blocks are not asked again.</summary>
    public bool IgnorePageBreaks { get; init; }

    public float ColumnWidth => Geometry.Columns.Widths[Column];

    /// <summary>The space after the last piece placed (the next band starts that far below).</summary>
    public float SpaceAfterLast => _prevAfter;

    public List<Placed> Placed { get; set; } = [];

    /// <summary>The flows handed to <see cref="Place"/> on this page, in order: what a trial fill replays.</summary>
    public List<IBlockFlow> Inputs { get; } = [];

    public bool HasContent => PageHadContent || Placed.Count > 0;

    /// <summary>Places a flow, cutting it over columns as needed. Returns when it is all placed or when the band has no column left.</summary>
    public PlaceResult Place(IBlockFlow first, IFlowLookahead? look)
    {
        ArgumentNullException.ThrowIfNull(first);
        Inputs.Add(first);
        var flow = first;
        var strip = Geometry.IsStrip;
        while (true)
        {
            if (!strip && !IgnorePageBreaks && flow.PageBreakBefore && HasContent)
            {
                return new PlaceResult(PlaceStatus.NewPage, flow, false);
            }

            var gap = GapBefore(flow);
            var available = Bottom - Y - gap;
            if (look is not null && !strip && flow.KeepNext && !_columnEmpty && MustMoveWithNext(flow, look, available))
            {
                if (!AdvanceColumn())
                {
                    return new PlaceResult(PlaceStatus.NewPage, flow, true);
                }

                continue;
            }

            var split = SplitWithNotes(flow, Y + gap, available, _columnEmpty && AtPageTop);
            if (split.Head is not { } head)
            {
                if (_columnEmpty && AtPageTop)
                {
                    throw new InvalidOperationException("A flow gave no head at the top of an empty column.");
                }

                if (!AdvanceColumn())
                {
                    return new PlaceResult(PlaceStatus.NewPage, flow, true);
                }

                continue;
            }

            var y = Y + gap;
            Placed.Add(new Placed(head, Geometry.Columns.Lefts[Column], y, Column));
            Y = y + head.Height;
            MaxY = Math.Max(MaxY, Y);
            if (Notes is not null)
            {
                Notes.Add(head.Notes);
                Notes.ContentReaches(Y);
            }

            _columnEmpty = false;
            _suppress = false;
            _prevAfter = head.SpaceAfter;
            _prevGroup = head.ContextualGroup;

            if (split.Tail is { } tail)
            {
                flow = tail;
                if (!AdvanceColumn())
                {
                    return new PlaceResult(PlaceStatus.NewPage, tail, true);
                }

                continue;
            }

            if (head.EndBreak is not { } kind)
            {
                return new PlaceResult(PlaceStatus.Placed, null, false);
            }

            var rest = head.AfterBreak(ColumnWidth);
            if (rest is null)
            {
                return new PlaceResult(PlaceStatus.Placed, null, false);
            }

            if (strip)
            {
                flow = rest;
                continue;
            }

            if (kind == BreakKind.Page)
            {
                return new PlaceResult(PlaceStatus.NewPage, rest, false);
            }

            flow = rest;
            if (!AdvanceColumn())
            {
                return new PlaceResult(PlaceStatus.NewPage, rest, false);
            }

            _suppress = false;
        }
    }

    // Cuts the flow for the room there is, then takes back lines until the notes of the references on the lines that stay fit as well: the
    // room of a note is reserved when its line is placed. Two things must hold: the piece ends above the area the notes would take, and
    // so does everything already on the page (another column). At the top of the page a piece is kept whatever the notes ask (the
    // notes are cut to a share of the page and continue on the next one), which is what keeps the layout moving.
    private FlowSplit SplitWithNotes(IBlockFlow flow, float y, float available, bool mustProgress)
    {
        var split = flow.Split(available, mustProgress);
        if (Notes is null)
        {
            return split;
        }

        while (split.Head is { } head && head.Notes.Count > 0)
        {
            var limit = _bottom - Notes.ReservedWith(head.Notes);
            var fitsBelow = y + head.Height <= limit + Epsilon;
            if (fitsBelow && Notes.ContentBottom <= limit + Epsilon)
            {
                break;
            }

            // Fewer lines: the room that is left, or one line less when the trouble is what other columns hold.
            var room = fitsBelow ? head.Height - Epsilon : Math.Min(limit - y, head.Height - Epsilon);
            var smaller = flow.Split(room, mustProgress);
            if (smaller.Head is { } next && next.Height >= head.Height - (Epsilon / 2f))
            {
                // Nothing smaller exists (a single line at the top of the page): keep it.
                return smaller;
            }

            split = smaller;
        }

        return split;
    }

    // Space between two blocks is the space after the first plus the space before the second (as Word does, not the larger of the two);
    // contextual spacing drops both between blocks of the same style. At the top of a column the space before is dropped when the
    // block got there by overflowing, not when it follows a manual break or starts the section.
    private float GapBefore(IBlockFlow flow)
    {
        if (_columnEmpty)
        {
            return _suppress ? 0f : flow.SpaceBefore;
        }

        if (flow.ContextualGroup is { } group && group == _prevGroup)
        {
            return 0f;
        }

        return _prevAfter + flow.SpaceBefore;
    }

    private bool MustMoveWithNext(IBlockFlow flow, IFlowLookahead look, float available)
    {
        var width = ColumnWidth;
        var need = flow.Height;
        var previous = flow;
        for (var k = 0; k < MaximumChain && previous.KeepNext; k++)
        {
            if (look.Peek(k, width) is not { } next || next.PageBreakBefore)
            {
                break;
            }

            need += previous.SpaceAfter + next.SpaceBefore + (next.KeepNext ? next.Height : next.MinimumHead);
            previous = next;
        }

        // Only a chain that fits an empty column is worth moving; a longer one flows on as it is.
        return need > available && need <= Bottom - Top;
    }

    private bool AdvanceColumn()
    {
        if (Column + 1 >= Geometry.Columns.Count)
        {
            return false;
        }

        Column++;
        Y = Top;
        _columnEmpty = true;
        _suppress = true;
        _prevAfter = 0f;
        _prevGroup = null;
        return true;
    }
}
