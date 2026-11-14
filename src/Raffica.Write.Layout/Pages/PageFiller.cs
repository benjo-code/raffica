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
    private readonly List<(PageBuilder Page, float Height)> _revisable = [];
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

    public IEnumerable<LaidOutPage> Fill()
    {
        PageBuilder? page = null;
        List<Band> bands = [];
        var pageIndex = 0;
        var nextNumber = 1;
        var lastFilled = LastNonEmpty();
        SectionPlan? current = null;

        for (var s = 0; s < _sections.Count; s++)
        {
            var section = _sections[s];
            if (section.IsEmpty)
            {
                continue;
            }

            current = section;

            var geometry = section.Geometry;
            var continuous = page is not null && !_flowMode && section.Start == SectionStart.Continuous && page.Geometry.SamePaper(geometry);
            if (page is not null && !continuous)
            {
                yield return Finish(page, bands);
                page = null;
            }

            if (page is null)
            {
                var number = section.PageNumberStart ?? nextNumber;
                if (!_flowMode && NeedsBlankPage(section.Start, number))
                {
                    yield return Finish(Begin(pageIndex++, number, section), []);
                    number++;
                }

                page = Begin(pageIndex++, number, section);
                nextNumber = number + 1;
                bands = [new Band(geometry, page.BodyTop, page.BodyBottom, false, false, page.Notes)];
            }
            else
            {
                var last = bands[^1];
                Balance(last);
                CountSectionPage(section);

                // The room is what the page has left after header, footer and notes; a section with margins of its own can only narrow it.
                var bottom = Math.Min(geometry.BodyBottom, page.BodyBottom);
                var top = last.MaxY + last.SpaceAfterLast;
                bands.Add(new Band(geometry, Math.Min(top, bottom), bottom, false, true, page.Notes));
            }

            var source = new SectionSource(_planner, _blocks, section.FirstBlock, section.EndBlock, Touch, s == lastFilled ? Chrome?.Trailer() : null);
            IBlockFlow? carry = null;
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
                yield return Finish(page, bands);
                page = Begin(pageIndex++, nextNumber, section);
                nextNumber++;
                bands = [new Band(geometry, page.BodyTop, page.BodyBottom, result.SuppressSpaceBefore, false, page.Notes)];
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
            yield return Finish(Begin(pageIndex++, nextNumber++, current ?? _sections[0]), []);
        }
    }

    /// <summary>The pages of the layout with the header or footer made again now that the page counts are known (call once, when the last page is out).</summary>
    public IReadOnlyList<(int Index, LaidOutPage Page)> Revisions()
    {
        if (Chrome is null || _revisable.Count == 0)
        {
            return [];
        }

        var revised = new List<(int, LaidOutPage)>(_revisable.Count);
        foreach (var (page, height) in _revisable)
        {
            Chrome.Revise(page, _pageCount, _sectionPages);
            revised.Add((page.Index, Build(page, height)));
        }

        _revisable.Clear();
        return revised;
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
        if (page.NeedsTotals)
        {
            _revisable.Add((page, height));
        }

        return Build(page, height);
    }

    private static LaidOutPage Build(PageBuilder page, float height) =>
        new(page.Index, page.NumberText, page.Geometry.Width, height, page.Geometry.IsStrip, page.ItemArray(), page.TextValue(), page.SpanArray());

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
        private readonly List<BlockPlan> _ahead = [];
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

            var plan = _ahead[0];
            _ahead.RemoveAt(0);
            return plan.Layout(width);
        }

        public IBlockFlow? Peek(int ahead, float width)
        {
            Prepare(ahead);
            return ahead < _ahead.Count ? _ahead[ahead].Layout(width) : null;
        }

        private void Prepare(int ahead)
        {
            while (_ahead.Count <= ahead)
            {
                if (_next < _end)
                {
                    var flat = _blocks[_next++];
                    _touch(flat.BodyIndex);
                    if (_planner.Plan(flat) is { } plan)
                    {
                        _ahead.Add(plan);
                    }

                    continue;
                }

                // After the last block of the document come the endnotes (every reference is numbered by now).
                if (_trailer is not null && _trailer.MoveNext())
                {
                    _ahead.Add(_trailer.Current);
                    continue;
                }

                _trailer?.Dispose();
                _trailer = null;
                break;
            }
        }
    }
}
