using System.Globalization;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// Pours the body blocks into pages: section after section, column after column, page after page. <see cref="Fill"/> is a lazy
/// sequence that yields each page as it is complete, so the caller decides how far to go and on which thread. The filler knows
/// blocks only as <see cref="IBlockFlow"/>; paragraphs, tables and floats differ only in the flow they make.
/// </summary>
/// <remarks>
/// Extension points: a block kind plugs in through <see cref="BlockPlanner.Plan"/>; headers, footers and notes plug in through
/// <see cref="Chrome"/> (they may shrink the body area of a page before it is filled). One filler serves one layout and is driven by
/// one thread at a time.
/// </remarks>
internal sealed class PageFiller
{
    private const uint Black = 0xFF000000;
    private const int MaximumNotePages = 10_000;

    private readonly IReadOnlyList<SectionPlan> _sections;
    private readonly IReadOnlyList<FlatBlock> _blocks;
    private readonly BlockPlanner _planner;
    private readonly bool _flowMode;
    private readonly int[] _blockPage;
    private readonly float[] _blockY;
    private readonly HashSet<int> _startedSections = [];
    private readonly List<int> _sectionPages = [];
    private readonly List<Revisable> _revisable = [];
    private int _pageCount;
    private int _maxTouched = -1;

    public PageFiller(IReadOnlyList<SectionPlan> sections, IReadOnlyList<FlatBlock> blocks, BlockPlanner planner, bool flowMode, int[] blockPage, float[] blockY)
    {
        _sections = sections;
        _blocks = blocks;
        _planner = planner;
        _flowMode = flowMode;
        _blockPage = blockPage;
        _blockY = blockY;
    }

    /// <summary>Headers, footers and notes; null in the writing view.</summary>
    public IPageChrome? Chrome { get; set; }

    /// <summary>The highest body block index the filler has looked at so far, placed or only measured ahead.</summary>
    public int MaxTouchedBodyIndex => _maxTouched;

    /// <summary>
    /// The state at the start of every page that has begun or is about to (the page after the last one yielded is already in it), by page index;
    /// null for a page that cannot be resumed (the page after an inserted blank page, a page of notes that run on past the last block, a page
    /// made of endnotes). The filler adds to it as it goes; <see cref="DocumentLayout"/> gives a resumed filler a list that holds the pages before the resume point.
    /// </summary>
    public List<FillerState?> Records { get; set; } = [];

    /// <summary>
    /// Asked for each state of a page about to begin (never for the one the filler was resumed from). When it answers true the filler yields
    /// the page it has just finished and ends, with <see cref="StopMatched"/> set: the pages from that state on are the old ones.
    /// </summary>
    public Func<FillerState, bool>? StopProbe { get; set; }

    /// <summary>True when the last run ended because <see cref="StopProbe"/> answered true (and not because the document ended).</summary>
    public bool StopMatched { get; private set; }

    /// <summary>The index of the page whose start made <see cref="StopProbe"/> answer true.</summary>
    public int StopIndex { get; private set; } = -1;

    /// <summary>The index in the flat block list of a block of the document, or -1.</summary>
    public int FlatIndexOf(Block block)
    {
        for (var i = 0; i < _blocks.Count; i++)
        {
            if (ReferenceEquals(_blocks[i].Block, block))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The pages with a page count in their header or footer, in page order (kept after <see cref="Revisions()"/>, so an edit can revise them again).</summary>
    internal List<Revisable> RevisableList => _revisable;

    /// <summary>The page count and the pages per section of what the filler has begun: the totals once the last page is out.</summary>
    internal (int Pages, int[] SectionPages) Totals() => (_pageCount, [.. _sectionPages]);

    public IEnumerable<LaidOutPage> Fill() => FillFrom(null);

    /// <summary>
    /// Pours the blocks into pages from the start of the document (<paramref name="resume"/> null) or from the start of a page that an
    /// earlier run recorded. The state of the planner, the chrome and the counters are put back first; the page begins as it would have in
    /// a run that had laid out everything before it. Every page of the run that follows is laid out exactly as in a full run.
    /// </summary>
    public IEnumerable<LaidOutPage> FillFrom(FillerState? resume)
    {
        StopMatched = false;
        PageBuilder? page = null;
        List<Band> bands = [];
        var pageIndex = 0;
        var nextNumber = 1;
        var lastFilled = LastNonEmpty();
        SectionPlan? current = null;
        var firstSection = 0;
        var resumeCarry = false;
        _recordedPage = -1;
        if (resume is not null)
        {
            Apply(resume);
            pageIndex = resume.PageIndex;
            nextNumber = resume.NextNumber;
            firstSection = resume.Start.SectionIndex;
            resumeCarry = resume.Kind == PageStartKind.Carry;
            SetRecord(resume.PageIndex, resume);
            _recordedPage = resume.PageIndex;
        }

        for (var s = firstSection; s < _sections.Count; s++)
        {
            var section = _sections[s];
            if (section.IsEmpty)
            {
                continue;
            }

            current = section;

            var geometry = section.Geometry;
            var continuous = page is not null && !_flowMode && section.Start == SectionStart.Continuous && page.Geometry.SamePaper(geometry);
            IBlockFlow? carry = null;
            var firstBlock = section.FirstBlock;
            if (resumeCarry)
            {
                // The page the earlier run was about to begin: the piece it carried goes first, the blocks after its block follow.
                resumeCarry = false;
                page = Begin(pageIndex++, nextNumber, section);
                nextNumber++;
                bands = [new Band(geometry, page.BodyTop, page.BodyBottom, resume!.SuppressSpaceBefore, false, page.Notes, page.Floats)];
                carry = resume.Carry;
                firstBlock = FlatIndexOf(resume.Start.Block!) + 1;
            }
            else
            {
                if (page is not null && !continuous)
                {
                    var built = Finish(page, bands);
                    page = null;
                    var stop = Record(SectionStartState(pageIndex, nextNumber, section));
                    yield return built;
                    if (stop)
                    {
                        yield break;
                    }
                }

                if (page is null)
                {
                    if (_recordedPage != pageIndex)
                    {
                        Record(SectionStartState(pageIndex, nextNumber, section));
                    }

                    var number = section.PageNumberStart ?? nextNumber;
                    if (!_flowMode && NeedsBlankPage(section.Start, number))
                    {
                        // The page after the blank one is not a state a run can start from.
                        var blank = Finish(Begin(pageIndex++, number, section), []);
                        SetRecord(pageIndex, null);
                        yield return blank;
                        number++;
                    }

                    page = Begin(pageIndex++, number, section);
                    nextNumber = number + 1;
                    bands = [new Band(geometry, page.BodyTop, page.BodyBottom, false, false, page.Notes, page.Floats)];
                }
                else
                {
                    var last = bands[^1];
                    Balance(last);
                    CountSectionPage(section);

                    // The room is what the page has left after header, footer and notes; a section with margins of its own can only narrow it.
                    var bottom = Math.Min(geometry.BodyBottom, page.BodyBottom);
                    var top = last.MaxY + last.SpaceAfterLast;
                    bands.Add(new Band(geometry, Math.Min(top, bottom), bottom, false, true, page.Notes, page.Floats));
                }
            }

            // A picture is shrunk to the text area of the section it is in (plans are made lazily, while this section is filled).
            _planner.TextAreaHeight = Math.Max(PageGeometry.MinimumText, geometry.Height - geometry.Top - geometry.Bottom);
            var source = new SectionSource(_planner, _blocks, firstBlock, section.EndBlock, Touch, s == lastFilled ? Chrome?.Trailer() : null);
            while (true)
            {
                var band = bands[^1];
                var flow = carry ?? source.Take(band.ColumnWidth);
                carry = null;
                if (flow is null)
                {
                    break;
                }

                var result = band.Place(flow, source);
                if (result.Status == PlaceStatus.Placed)
                {
                    continue;
                }

                // The page is full: publish it and go on at the top of the next one, in the geometry of the section being filled.
                var full = Finish(page, bands);
                var stopHere = Record(CarryStateOf(result, s, pageIndex, nextNumber, source));
                yield return full;
                if (stopHere)
                {
                    yield break;
                }

                page = Begin(pageIndex++, nextNumber, section);
                nextNumber++;
                bands = [new Band(geometry, page.BodyTop, page.BodyBottom, result.SuppressSpaceBefore, false, page.Notes, page.Floats)];
                carry = result.Carry;
            }
        }

        if (page is not null)
        {
            yield return Finish(page, bands);
        }
        else if (pageIndex == 0)
        {
            // An empty body (or sections with nothing in them) is still one blank page.
            pageIndex = 1;
            nextNumber = 2;
            current = _sections[0];
            yield return Finish(Begin(0, 1, current), []);
        }

        // A note that is longer than the room left continues on pages of its own until it has ended: each page takes at least one line
        // of it, so this ends; the limit only guards against a fault.
        for (var extra = 0; Chrome is { HasCarry: true } && extra < MaximumNotePages; extra++)
        {
            SetRecord(pageIndex, null);
            yield return Finish(Begin(pageIndex++, nextNumber++, current ?? _sections[0]), []);
        }
    }

    private int _recordedPage = -1;

    private void SetRecord(int index, FillerState? state)
    {
        while (Records.Count < index)
        {
            Records.Add(null);
        }

        if (Records.Count == index)
        {
            Records.Add(state);
        }
        else
        {
            Records[index] = state;
        }
    }

    // Keeps a state and asks the probe whether it ends the run.
    private bool Record(FillerState? state)
    {
        if (state is null)
        {
            return false;
        }

        SetRecord(state.PageIndex, state);
        _recordedPage = state.PageIndex;
        if (StopProbe is { } probe && probe(state))
        {
            StopMatched = true;
            StopIndex = state.PageIndex;
            return true;
        }

        return false;
    }

    // The state when a page that starts a section is about to begin: the planner stands before the first block of the section (the sections
    // before it have been planned to their end and nothing is looked at ahead across a section).
    private FillerState SectionStartState(int pageIndex, int nextNumber, SectionPlan section)
    {
        var number = section.PageNumberStart ?? nextNumber;
        var blank = !_flowMode && NeedsBlankPage(section.Start, number);
        var block = blank ? null : _blocks[section.FirstBlock].Block;
        var (numbers, notes) = _planner.CurrentState();
        return Make(PageStartKind.Section, new PageStart(block, 0, section.Index), null, false, pageIndex, nextNumber, numbers, notes);
    }

    // The state when the page after a full one is about to begin with a carried piece; null when the piece cannot be resumed from (it comes from
    // the endnotes at the end of the document, or its block has no plan on record).
    private FillerState? CarryStateOf(PlaceResult result, int sectionIndex, int pageIndex, int nextNumber, SectionSource source)
    {
        // The state after the carried block, as this run left it when it planned that block: the cache entry may stem from an older run.
        var block = source.LastTaken;
        if (block is null || source.LastTakenNumbers is not { } numbers || result.Carry is not { } piece)
        {
            SetRecord(pageIndex, null);
            return null;
        }

        return Make(PageStartKind.Carry, new PageStart(block, piece.StartLine, sectionIndex), piece, result.SuppressSpaceBefore, pageIndex, nextNumber, numbers, source.LastTakenNotes);
    }

    private FillerState Make(PageStartKind kind, PageStart start, IBlockFlow? carry, bool suppress, int pageIndex, int nextNumber, NumberingState numbers, NoteState? notes)
    {
        var started = new int[_startedSections.Count];
        _startedSections.CopyTo(started);
        Array.Sort(started);
        return new FillerState(kind, start, carry, suppress, pageIndex, nextNumber, [.. _sectionPages], started, numbers, notes, Chrome?.CarryState);
    }

    // Puts everything a state holds back: the counters, the planner, the notes that run over, and the pages with a page count from the pages before.
    private void Apply(FillerState state)
    {
        _sectionPages.Clear();
        _sectionPages.AddRange(state.SectionPages);
        _startedSections.Clear();
        foreach (var index in state.StartedSections)
        {
            _startedSections.Add(index);
        }

        _pageCount = state.PageIndex;
        _planner.RestoreState(state.Numbers, state.Notes);
        Chrome?.RestoreCarry(state.ChromeCarry);
        _revisable.RemoveAll(r => r.Published.Index >= state.PageIndex);
    }

    /// <summary>
    /// The pages of the layout with the header or footer made again now that the page counts are known (call when the last page is out). The
    /// entries stay, so the pages can be made again after an edit that changed the counts.
    /// </summary>
    public IReadOnlyList<(int Index, LaidOutPage Page)> Revisions() => Revisions(0, int.MaxValue, _pageCount, _sectionPages);

    /// <summary>The revised pages of the page indexes <c>[from, to)</c> for the given totals.</summary>
    public IReadOnlyList<(int Index, LaidOutPage Page)> Revisions(int from, int to, int totalPages, IReadOnlyList<int> sectionPages)
    {
        if (Chrome is null || _revisable.Count == 0)
        {
            return [];
        }

        var revised = new List<(int, LaidOutPage)>();
        foreach (var r in _revisable)
        {
            var index = r.Published.Index;
            if (index < from || index >= to)
            {
                continue;
            }

            // Header and footer are drawn again into an empty builder; the body and note items and their text are the published page's own.
            var scratch = new PageBuilder(index, r.Number, r.Published.Number, r.Section, r.IsFirstOfSection);
            Chrome.Revise(scratch, totalPages, sectionPages);
            revised.Add((index, r.Rebuild(scratch)));
        }

        return revised;
    }

    /// <summary>
    /// What a page with a provisional page count keeps until the layout is complete: the published page (its item array and text are
    /// shared, not copied) and the sizes of the header and footer parts in them. It holds no <see cref="PageBuilder"/>.
    /// </summary>
    internal sealed record Revisable(LaidOutPage Published, SectionPlan Section, int Number, bool IsFirstOfSection, int HeaderItems, int FooterItems, int HeaderLines, int FooterLines, int HeaderRaw, int FooterRaw, int RawLength, HeaderFooterType Type)
    {
        public LaidOutPage Rebuild(PageBuilder scratch) =>
            new ChromeInfo(Section, Number, IsFirstOfSection, HeaderItems, FooterItems, HeaderLines, FooterLines, HeaderRaw, FooterRaw, RawLength, 0f, 0f, null, null, false, Type).Rebuild(Published, scratch, false);
    }

    private int LastNonEmpty()
    {
        for (var i = _sections.Count - 1; i >= 0; i--)
        {
            if (!_sections[i].IsEmpty)
            {
                return i;
            }
        }

        return -1;
    }

    private void CountSectionPage(SectionPlan section)
    {
        while (_sectionPages.Count <= section.Index)
        {
            _sectionPages.Add(0);
        }

        _sectionPages[section.Index]++;
    }

    private void Touch(int bodyIndex) => _maxTouched = Math.Max(_maxTouched, bodyIndex);

    // An even-page section starts on an even page number, an odd-page section on an odd one; a blank page makes up the difference.
    private static bool NeedsBlankPage(SectionStart start, int number) =>
        (start == SectionStart.EvenPage && number % 2 != 0) || (start == SectionStart.OddPage && number % 2 == 0);

    private PageBuilder Begin(int index, int number, SectionPlan section)
    {
        var text = _flowMode ? (index + 1).ToString(CultureInfo.InvariantCulture) : PageNumbers.Format(number, section.PageNumberFormat);
        var page = new PageBuilder(index, number, text, section, _startedSections.Add(section.Index));
        _pageCount++;
        CountSectionPage(section);
        Chrome?.Begin(page);
        return page;
    }

    private LaidOutPage Finish(PageBuilder page, List<Band> bands)
    {
        var geometry = page.Geometry;
        var end = 0f;
        foreach (var band in bands)
        {
            foreach (var placed in band.Placed)
            {
                var flow = placed.Flow;
                flow.Emit(page, placed.X, placed.Y);
                if (flow.BodyIndex < 0)
                {
                    // An endnote block: not a block of the body.
                    continue;
                }

                page.AddSpan(new BlockSpan(flow.BodyIndex, placed.Y, placed.Y + flow.Height));
                if (flow.IsFirstPiece && _blockPage[flow.BodyIndex] < 0)
                {
                    _blockPage[flow.BodyIndex] = page.Index;
                    _blockY[flow.BodyIndex] = placed.Y;
                }
            }

            end = Math.Max(end, band.MaxY);
            if (band.Geometry.Columns.Separator && band.Placed.Count > 0)
            {
                var columns = band.Geometry.Columns;
                for (var i = 1; i < columns.Count; i++)
                {
                    var x = (columns.Lefts[i - 1] + columns.Widths[i - 1] + columns.Lefts[i]) / 2f;
                    page.Add(new RuleItem(x - 0.25f, band.Top, 0.5f, band.MaxY - band.Top, Black));
                }
            }
        }

        Chrome?.End(page);
        var height = geometry.IsStrip ? Math.Max(1f, end) : geometry.Height;
        var info = ChromeInfo.Of(page);
        var built = new LaidOutPage(page.Index, page.NumberText, page.Geometry.Width, height, page.Geometry.IsStrip, page.ItemArray(), page.TextValue(), page.SpanArray(), isProvisional: page.NeedsTotals, textLines: page.TextLineArray(), chrome: Chrome is null ? null : info);
        if (page.NeedsTotals)
        {
            _revisable.Add(new Revisable(built, info.Section, info.Number, info.IsFirstOfSection, info.HeaderItems, info.FooterItems, info.HeaderLines, info.FooterLines, info.HeaderRaw, info.FooterRaw, info.RawLength, info.Type));
        }

        PageFinished?.Invoke(page);
        return built;
    }

    /// <summary>Test seam: called with each page builder when its page is made.</summary>
    internal Action<PageBuilder>? PageFinished { get; set; }

    // A continuous section break after columns balances them: the least column height at which everything still fits is found by
    // trial fills of the same flows.
    private static void Balance(Band band)
    {
        var columns = band.Geometry.Columns.Count;
        if (columns < 2 || band.Placed.Count == 0)
        {
            return;
        }

        var span = band.MaxY - band.Top;
        var total = 0f;
        for (var c = 0; c < columns; c++)
        {
            var bottom = band.Top;
            foreach (var placed in band.Placed)
            {
                if (placed.Column == c)
                {
                    bottom = Math.Max(bottom, placed.Y + placed.Flow.Height);
                }
            }

            total += bottom - band.Top;
        }

        var low = total / columns;
        var high = span;
        Band? best = null;
        for (var i = 0; i < 20 && high - low > 0.25f; i++)
        {
            var middle = (low + high) / 2f;
            var trial = Trial(band, middle);
            if (trial is null)
            {
                low = middle;
            }
            else
            {
                best = trial;
                high = middle;
            }
        }

        best ??= Trial(band, high);
        if (best is null)
        {
            return;
        }

        band.Placed = best.Placed;
        band.MaxY = best.MaxY;
    }

    private static Band? Trial(Band band, float height)
    {
        var trial = new Band(band.Geometry, band.Top, band.Top + height, band.InitialSuppress, band.PageHadContent) { IgnorePageBreaks = true };
        foreach (var input in band.Inputs)
        {
            if (trial.Place(input, null).Status != PlaceStatus.Placed)
            {
                return null;
            }
        }

        return trial.MaxY <= band.Top + height + 0.01f ? trial : null;
    }

    /// <summary>The blocks of one section, made into plans on demand and in order, with a look ahead for keep-with-next.</summary>
    private sealed class SectionSource : IFlowLookahead
    {
        private readonly BlockPlanner _planner;
        private readonly IReadOnlyList<FlatBlock> _blocks;
        private readonly int _end;
        private readonly Action<int> _touch;
        private readonly List<Ahead> _ahead = [];
        private IEnumerator<BlockPlan>? _trailer;
        private int _next;

        public SectionSource(BlockPlanner planner, IReadOnlyList<FlatBlock> blocks, int first, int end, Action<int> touch, IEnumerable<BlockPlan>? trailer = null)
        {
            _planner = planner;
            _blocks = blocks;
            _next = first;
            _end = end;
            _touch = touch;
            _trailer = trailer?.GetEnumerator();
        }

        public IBlockFlow? Take(float width)
        {
            Prepare(0);
            if (_ahead.Count == 0)
            {
                return null;
            }

            var next = _ahead[0];
            _ahead.RemoveAt(0);
            LastTaken = next.Block;
            LastTakenNumbers = next.Numbers;
            LastTakenNotes = next.Notes;
            return next.Plan.Layout(width);
        }

        /// <summary>The numbering and note state right after the last block taken was planned in this run (what a page that starts after it resumes from).</summary>
        public NumberingState? LastTakenNumbers { get; private set; }

        public NoteState? LastTakenNotes { get; private set; }

        private readonly record struct Ahead(BlockPlan Plan, Block? Block, NumberingState? Numbers, NoteState? Notes);

        /// <summary>The block of the plan last taken (null for an endnote block, or before the first take): every piece that carries over onto the next page belongs to it.</summary>
        public Block? LastTaken { get; private set; }

        public IBlockFlow? Peek(int ahead, float width)
        {
            Prepare(ahead);
            return ahead < _ahead.Count ? _ahead[ahead].Plan.Layout(width) : null;
        }

        private void Prepare(int ahead)
        {
            while (_ahead.Count <= ahead)
            {
                if (_next < _end)
                {
                    var flat = _blocks[_next++];
                    _touch(flat.BodyIndex);
                    if (_planner.PlanFor(flat) is { } plan)
                    {
                        var (numbers, notes) = _planner.CurrentState();
                        _ahead.Add(new Ahead(plan, flat.Block, numbers, notes));
                    }

                    continue;
                }

                // After the last block of the document come the endnotes (every reference is numbered by now).
                if (_trailer is not null && _trailer.MoveNext())
                {
                    _ahead.Add(new Ahead(_trailer.Current, null, null, null));
                    continue;
                }

                _trailer?.Dispose();
                _trailer = null;
                break;
            }
        }
    }
}
