using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// The page chrome: which header and footer part a page shows, their laid-out blocks (made once per part, page number text, section
/// and total, then reused for every page that needs the same), the room they take, and the footnote area at the bottom.
/// </summary>
/// <remarks>
/// NUMPAGES and SECTIONPAGES need a count that exists only when the layout is complete, but pages are published before that. A
/// page whose header or footer shows such a field is first made with <see cref="FieldContext.PendingTotal"/> in its place (the same
/// size of room, so the body is not affected) and made again by <see cref="Revise"/> once the counts are known; only the header and
/// the footer are made again, the body items are shared. The layout publishes the new page in place of the old one before it reports
/// itself complete.
/// </remarks>
internal sealed class HeaderFooterPlan : IPageChrome
{
    private const int MaximumCachedBlocks = 4096;

    private readonly WordDocument _document;
    private readonly Func<BlockPlanner> _planners;
    private readonly NotePlanner _notes;
    private readonly bool _evenAndOdd;
    private readonly string?[][] _references;
    private readonly Dictionary<ChromeKey, ChromeBlock> _blocks = [];
    private readonly HashSet<string> _pageDependent = new(StringComparer.Ordinal);
    private List<NoteGroup> _carry = [];

    public HeaderFooterPlan(WordDocument document, IReadOnlyList<SectionPlan> sections, Func<BlockPlanner> planners, NotePlanner notes)
    {
        _document = document;
        _planners = planners;
        _notes = notes;
        _evenAndOdd = document.Settings?.EvenAndOddHeaders ?? false;
        _references = Resolve(sections);
    }

    /// <summary>How many headers and footers were laid out (not counting reuse); for the tests.</summary>
    public int LayoutCount { get; private set; }

    public bool HasCarry => _carry.Count > 0;

    // The list is replaced, never changed, once a page has been ended with it, so it can be kept and put back as it is.
    public object? CarryState => _carry.Count == 0 ? null : _carry;

    public void RestoreCarry(object? state) => _carry = state as List<NoteGroup> ?? [];

    public IEnumerable<BlockPlan> Trailer() => _notes.EndnotePlans();

    public void Begin(PageBuilder page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var geometry = page.Geometry;
        var (header, footer) = Place(page, null);

        // The body starts below the header and ends above the footer, but keeps a text area of a usable size whatever the parts ask.
        var top = geometry.BodyTop;
        var bottom = geometry.BodyBottom;
        if (header.Height > 0f)
        {
            top = Math.Max(top, Math.Min(geometry.HeaderDistance + header.Height, bottom - PageGeometry.MinimumText));
        }

        if (footer.Height > 0f)
        {
            bottom = Math.Min(bottom, Math.Max(geometry.Height - geometry.FooterDistance - footer.Height, top + PageGeometry.MinimumText));
        }

        page.BodyTop = top;
        page.BodyBottom = bottom;
        page.Notes = new PageNotes(_notes, geometry.TextWidth, bottom - top, top, _carry);
        _carry = [];
    }

    public void End(PageBuilder page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Notes is not { HasContent: true } notes)
        {
            return;
        }

        page.Region = PageRegion.Notes;
        try
        {
            _carry = notes.Emit(page, page.Geometry.Columns.Lefts[0], page.BodyBottom - notes.Reserved, page.BodyBottom);
        }
        finally
        {
            page.Region = PageRegion.Body;
        }
    }

    public void Revise(PageBuilder page, int totalPages, IReadOnlyList<int> sectionPages)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(sectionPages);
        var section = page.Section.Index;
        Place(page, new Totals(totalPages, section < sectionPages.Count ? sectionPages[section] : 0));
    }

    private readonly record struct Totals(int Pages, int SectionPages);

    private readonly record struct ChromeKey(string Part, string NumberText, int Section, int Pages, int SectionPages, float Width);

    /// <summary>A header or footer laid out at one width: its flows, where each goes below the top of the block, and its height.</summary>
    private sealed class ChromeBlock
    {
        public static readonly ChromeBlock Empty = new([], [], 0f, false);

        public ChromeBlock(IBlockFlow[] flows, float[] ys, float height, bool needsTotals)
        {
            Flows = flows;
            Ys = ys;
            Height = height;
            NeedsTotals = needsTotals;
        }

        public IBlockFlow[] Flows { get; }

        public float[] Ys { get; }

        public float Height { get; }

        public bool NeedsTotals { get; }
    }

    // Draws the header and the footer of the page into their regions (again, when the page is revised) and says what room they take.
    private (ChromeBlock Header, ChromeBlock Footer) Place(PageBuilder page, Totals? totals)
    {
        var geometry = page.Geometry;
        var x = geometry.Columns.Lefts[0];
        var header = BlockFor(page, HeaderFooterKind.Header, totals);
        var footer = BlockFor(page, HeaderFooterKind.Footer, totals);
        page.NeedsTotals = header.NeedsTotals || footer.NeedsTotals;
        Draw(page, PageRegion.Header, header, x, geometry.HeaderDistance);
        Draw(page, PageRegion.Footer, footer, x, geometry.Height - geometry.FooterDistance - footer.Height);
        return (header, footer);
    }

    private static void Draw(PageBuilder page, PageRegion region, ChromeBlock block, float x, float top)
    {
        page.Clear(region);
        if (block.Flows.Length == 0)
        {
            return;
        }

        page.Region = region;
        try
        {
            for (var i = 0; i < block.Flows.Length; i++)
            {
                block.Flows[i].Emit(page, x, top + block.Ys[i]);
            }
        }
        finally
        {
            page.Region = PageRegion.Body;
        }
    }

    private ChromeBlock BlockFor(PageBuilder page, HeaderFooterKind kind, Totals? totals)
    {
        var type = TypeFor(page);
        var id = _references[page.Section.Index][Slot(kind, type)];
        if (id is null || !_document.HeadersFooters.TryGetValue(id, out var part))
        {
            return ChromeBlock.Empty;
        }

        // The page number text is part of the key only for a part that shows it (found out when the part is first laid out).
        var width = page.Geometry.TextWidth;
        var key = KeyFor(id, page, totals, width);
        if (_blocks.TryGetValue(key, out var known))
        {
            return known;
        }

        if (_blocks.Count >= MaximumCachedBlocks)
        {
            _blocks.Clear();
        }

        var block = Layout(id, part, page, totals, width, out var usesPage);
        if (usesPage && _pageDependent.Add(id))
        {
            key = KeyFor(id, page, totals, width);
        }

        _blocks[key] = block;
        return block;
    }

    private ChromeKey KeyFor(string id, PageBuilder page, Totals? totals, float width) =>
        new(id, _pageDependent.Contains(id) ? page.NumberText : string.Empty, page.Section.Index, totals?.Pages ?? -1, totals?.SectionPages ?? -1, width);

    private ChromeBlock Layout(string id, HeaderFooterPart part, PageBuilder page, Totals? totals, float width, out bool usesPage)
    {
        LayoutCount++;
        var fields = new FieldContext(page.Number, page.NumberText, page.Section.Index + 1, totals?.Pages, totals?.SectionPages);
        var planner = _planners();
        planner.Context = new PlanContext { Fields = fields };
        planner.StoryKey = ImageRef.HeaderFooterStory(id);
        planner.Story = new StoryRef(StoryKind.HeaderFooter, id);
        planner.CanPlaceFloats = false;
        var flows = planner.PlanStory(part.Content.Blocks, width);
        usesPage = fields.UsedPage;
        if (flows.Count == 0)
        {
            return new ChromeBlock([], [], 0f, fields.UsedPendingTotal);
        }

        var ys = new float[flows.Count];
        var y = flows[0].SpaceBefore;
        for (var i = 0; i < flows.Count; i++)
        {
            if (i > 0)
            {
                y += flows[i - 1].SpaceAfter + flows[i].SpaceBefore;
            }

            ys[i] = y;
            y += flows[i].Height;
        }

        y += flows[^1].SpaceAfter;
        return new ChromeBlock([.. flows], ys, y, fields.UsedPendingTotal);
    }

    // The first page of a section with w:titlePg has its own parts; with w:evenAndOddHeaders the even pages (by number) have theirs.
    private HeaderFooterType TypeFor(PageBuilder page)
    {
        if (page.IsFirstOfSection && page.Section.TitlePage)
        {
            return HeaderFooterType.First;
        }

        return _evenAndOdd && page.Number % 2 == 0 ? HeaderFooterType.Even : HeaderFooterType.Default;
    }

    private static int Slot(HeaderFooterKind kind, HeaderFooterType type) => ((int)kind * 3) + (int)type;

    // A section without a reference of its own for a kind and type shows the one of the section before it.
    private static string?[][] Resolve(IReadOnlyList<SectionPlan> sections)
    {
        var result = new string?[sections.Count][];
        var current = new string?[6];
        for (var s = 0; s < sections.Count; s++)
        {
            if (sections[s].Properties is { } properties)
            {
                var own = new HashSet<int>();
                foreach (var reference in properties.HeaderFooterReferences)
                {
                    var slot = Slot(reference.Kind, reference.Type);
                    if (own.Add(slot))
                    {
                        current[slot] = reference.RelationshipId;
                    }
                }
            }

            result[s] = [.. current];
        }

        return result;
    }
}
