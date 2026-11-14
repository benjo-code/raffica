using System.Text;

namespace Raffica.Write.Layout;

/// <summary>Which part of a page the items being added belong to; the finished page lists header, body, notes and footer in that order.</summary>
internal enum PageRegion
{
    Header,
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
    private readonly List<PageItem>[] _items = [[], [], [], []];
    private readonly StringBuilder[] _text = [new(), new(), new(), new()];
    private readonly List<BlockSpan> _spans = [];

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

    /// <summary>True when the header or footer shows a total that was not known yet; the page is made again when the layout is complete.</summary>
    public bool NeedsTotals { get; set; }

    /// <summary>Where <see cref="Add"/> and <see cref="AddLine"/> write.</summary>
    public PageRegion Region { get; set; } = PageRegion.Body;

    public void Add(PageItem item) => _items[(int)Region].Add(item);

    public void AddLine(string line) => _text[(int)Region].Append(line).Append('\n');

    internal void AddSpan(BlockSpan span) => _spans.Add(span);

    /// <summary>Forgets what a region holds (the header and footer are made again at the end).</summary>
    internal void Clear(PageRegion region)
    {
        _items[(int)region].Clear();
        _text[(int)region].Clear();
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

    /// <summary>The blocks that end the document (the endnotes); enumerated once the whole body is planned.</summary>
    IEnumerable<BlockPlan> Trailer();

    /// <summary>Makes the header and footer of a page again, now that the page count and the section's page count are known.</summary>
    void Revise(PageBuilder page, int totalPages, IReadOnlyList<int> sectionPages);
}
