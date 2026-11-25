using System.Text;

namespace Raffica.Write.Layout;

/// <summary>Which part of a page the items being added belong to; the finished page lists header, behind-text objects, body, notes and footer in that order.</summary>
internal enum PageRegion
{
    Header,

    /// <summary>Floating objects behind the text of the body: painted after the header and before the body text.</summary>
    Behind,
    Body,
    Notes,
    Footer,
}

/// <summary>
/// Collects the items and text of the page being filled. It is the only thing a flow writes to. Headers, footers and notes
/// hook in through <see cref="IPageChrome"/> and write to their own <see cref="Region"/>.
/// </summary>
internal sealed class PageBuilder
{
    private readonly List<PageItem>[] _items = [[], [], [], [], []];
    private readonly StringBuilder[] _text = [new(), new(), new(), new(), new()];
    private readonly List<BlockSpan> _spans = [];
    private readonly List<TextLine>[] _lines = [[], [], [], [], []];
    private List<PictureBox>? _pictures;
    private List<PictureBox>? _behindPictures;
    private List<TableGrid>? _grids;

    public PageBuilder(int index, int number, string numberText, SectionPlan section, bool isFirstOfSection)
    {
        Index = index;
        Number = number;
        NumberText = numberText;
        Section = section;
        IsFirstOfSection = isFirstOfSection;
        Geometry = section.Geometry;
        BodyTop = Geometry.BodyTop;
        BodyBottom = Geometry.BodyBottom;
    }

    public int Index { get; }

    /// <summary>The page number as a number (restarts and continues per section).</summary>
    public int Number { get; }

    /// <summary>The page number as printed, in the format of the section.</summary>
    public string NumberText { get; }

    /// <summary>The section the page started in; later sections that continue on the page keep their own geometry in their bands.</summary>
    public SectionPlan Section { get; }

    /// <summary>True for the first page made for the section (the page that <c>w:titlePg</c> gives its own header and footer).</summary>
    public bool IsFirstOfSection { get; }

    public PageGeometry Geometry { get; }

    /// <summary>Where body text may start on this page. <see cref="IPageChrome.Begin"/> may move it down (a header taller than the margin).</summary>
    public float BodyTop { get; set; }

    /// <summary>Where body text must end on this page. <see cref="IPageChrome.Begin"/> may move it up (a tall footer). The room the footnotes take comes off this by <see cref="Notes"/>.</summary>
    public float BodyBottom { get; set; }

    /// <summary>The footnotes of this page; null when the page has none to place (the writing view).</summary>
    public PageNotes? Notes { get; set; }

    /// <summary>The height of the header and of the footer block placed on this page (0 for none), and the parts they are made from (null for none): what a chrome-only refresh compares.</summary>
    internal float HeaderHeight { get; set; }

    internal float FooterHeight { get; set; }

    internal string? HeaderPart { get; set; }

    internal string? FooterPart { get; set; }

    /// <summary>The type of the header and footer parts this page used (first page, even page or the default), as the plan decided when it placed them.</summary>
    internal Raffica.Write.Model.HeaderFooterType ChromeType { get; set; }

    /// <summary>True when the header or footer shows a page count (known or not): the page has to be revised when the count changes.</summary>
    internal bool UsesTotals { get; set; }

    /// <summary>True when the header or footer shows a total that was not known yet; the page is made again when the layout is complete.</summary>
    public bool NeedsTotals { get; set; }

    /// <summary>Where <see cref="Add"/> and <see cref="AddLine"/> write.</summary>
    public PageRegion Region { get; set; } = PageRegion.Body;

    /// <summary>The floating objects placed on this page so far: the text of later paragraphs keeps clear of them.</summary>
    public PageFloats Floats { get; } = new();

    private bool _behind;

    public void Add(PageItem item) => _items[(int)(_behind ? PageRegion.Behind : Region)].Add(item);

    /// <summary>Until the returned scope is disposed, <see cref="Add"/> writes to the items painted behind the body text (the text of the page is not affected).</summary>
    public IDisposable PaintBehind() => new BehindScope(this);

    private sealed class BehindScope : IDisposable
    {
        private readonly PageBuilder _page;
        private readonly bool _before;

        public BehindScope(PageBuilder page)
        {
            _page = page;
            _before = page._behind;
            page._behind = true;
        }

        public void Dispose() => _page._behind = _before;
    }

    /// <summary>Records a picture of the body at its place on the page; a picture in the header, footer or notes region is not recorded (those are never edited).</summary>
    internal void AddPicture(PictureAnchor? anchor, float x, float y, float width, float height, bool behindText = false)
    {
        if (anchor is not null && Region == PageRegion.Body)
        {
            (behindText ? _behindPictures ??= [] : _pictures ??= []).Add(new PictureBox(anchor, x, y, width, height, behindText));
        }
    }

    /// <summary>Records a table piece of the body on the page (nothing for the header, footer or notes region).</summary>
    internal void AddGrid(TableGrid grid)
    {
        if (Region == PageRegion.Body)
        {
            (_grids ??= []).Add(grid);
        }
    }

    /// <summary>The objects recorded so far as the page's map: the shared empty map when there are none.</summary>
    internal ObjectMap ObjectMapOf()
    {
        if (_pictures is null && _behindPictures is null && _grids is null)
        {
            return ObjectMap.Empty;
        }

        // Paint order: the pictures behind the text first, then the others in the order they were placed.
        var pictures = new PictureBox[(_behindPictures?.Count ?? 0) + (_pictures?.Count ?? 0)];
        _behindPictures?.CopyTo(pictures, 0);
        _pictures?.CopyTo(pictures, _behindPictures?.Count ?? 0);
        return new ObjectMap(pictures, _grids?.ToArray() ?? []);
    }

    public void AddLine(string line) => _text[(int)Region].Append(line).Append('\n');

    internal void AddSpan(BlockSpan span) => _spans.Add(span);

    /// <summary>Adds the record of a line to the current region.</summary>
    internal void AddTextLine(TextLine line) => _lines[(int)Region].Add(line);

    /// <summary>Forgets what a region holds (the header and footer are made again at the end).</summary>
    internal void Clear(PageRegion region)
    {
        _items[(int)region].Clear();
        _text[(int)region].Clear();
        _lines[(int)region].Clear();
    }

    internal int LineCount(PageRegion region) => _lines[(int)region].Count;

    /// <summary>The length of the text of a region as it is added (every line ends with a line feed).</summary>
    internal int RawLengthOf(PageRegion region) => _text[(int)region].Length;

    /// <summary>The length of the text of all regions as it is added.</summary>
    internal int RawLength
    {
        get
        {
            var length = 0;
            foreach (var text in _text)
            {
                length += text.Length;
            }

            return length;
        }
    }

    internal TextLine[] LinesOf(PageRegion region) => [.. _lines[(int)region]];

    internal TextLine[] TextLineArray()
    {
        var count = 0;
        foreach (var list in _lines)
        {
            count += list.Count;
        }

        var all = new TextLine[count];
        var at = 0;
        foreach (var list in _lines)
        {
            list.CopyTo(all, at);
            at += list.Count;
        }

        return all;
    }

    internal int ItemCount(PageRegion region) => _items[(int)region].Count;

    internal PageItem[] ItemsOf(PageRegion region) => [.. _items[(int)region]];

    /// <summary>The text of a region as it is added (every line ends with a line feed).</summary>
    internal string RawTextOf(PageRegion region) => _text[(int)region].ToString();

    internal PageItem[] ItemArray()
    {
        var count = 0;
        foreach (var list in _items)
        {
            count += list.Count;
        }

        var all = new PageItem[count];
        var at = 0;
        foreach (var list in _items)
        {
            list.CopyTo(all, at);
            at += list.Count;
        }

        return all;
    }

    internal string TextValue()
    {
        var all = new StringBuilder();
        foreach (var text in _text)
        {
            all.Append(text);
        }

        return all.Length == 0 ? string.Empty : all.ToString(0, all.Length - 1);
    }

    internal BlockSpan[] SpanArray() => [.. _spans];
}

/// <summary>The hook for what belongs to the page and not to the body: headers, footers and notes.</summary>
internal interface IPageChrome
{
    /// <summary>Called when a page starts, before any body block is placed; sets <see cref="PageBuilder.BodyTop"/>, <see cref="PageBuilder.BodyBottom"/> and <see cref="PageBuilder.Notes"/>.</summary>
    void Begin(PageBuilder page);

    /// <summary>Called when the body of the page is complete, before the page is published; draws the note area.</summary>
    void End(PageBuilder page);

    /// <summary>True when a note continues beyond the last page made: the filler adds pages until it has ended.</summary>
    bool HasCarry { get; }

    /// <summary>The notes that run over from the last page ended onto the next one (the chrome's own object, never changed afterwards), or null when none do.</summary>
    object? CarryState { get; }

    /// <summary>Sets the notes that run over onto the next page to a state <see cref="CarryState"/> returned (the filler goes on from a saved page start).</summary>
    void RestoreCarry(object? state);

    /// <summary>The blocks that end the document (the endnotes); enumerated once the whole body is planned.</summary>
    IEnumerable<BlockPlan> Trailer();

    /// <summary>Makes the header and footer of a page again, now that the page count and the section's page count are known.</summary>
    void Revise(PageBuilder page, int totalPages, IReadOnlyList<int> sectionPages);
}
