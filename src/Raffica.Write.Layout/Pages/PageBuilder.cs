using System.Text;

namespace Raffica.Write.Layout;

/// <summary>
/// Collects the items and text of the page being filled. It is the only thing a flow writes to. Headers, footers and notes
/// (Task 9) hook in through <see cref="IPageChrome"/>.
/// </summary>
internal sealed class PageBuilder
{
    private readonly List<PageItem> _items = [];
    private readonly StringBuilder _text = new();
    private readonly List<BlockSpan> _spans = [];

    public PageBuilder(int index, int number, string numberText, SectionPlan section)
    {
        Index = index;
        Number = number;
        NumberText = numberText;
        Section = section;
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

    public PageGeometry Geometry { get; }

    /// <summary>Where body text may start on this page. <see cref="IPageChrome.Begin"/> may move it down (a header taller than the margin).</summary>
    public float BodyTop { get; set; }

    /// <summary>Where body text must end on this page. <see cref="IPageChrome.Begin"/> may move it up (a tall footer).</summary>
    public float BodyBottom { get; set; }

    public bool IsBlank => _items.Count == 0 && _text.Length == 0;

    public void Add(PageItem item) => _items.Add(item);

    public void AddLine(string line) => _text.Append(line).Append('\n');

    internal void AddSpan(BlockSpan span) => _spans.Add(span);

    internal PageItem[] ItemArray() => [.. _items];

    internal string TextValue() => _text.Length == 0 ? string.Empty : _text.ToString(0, _text.Length - 1);

    internal BlockSpan[] SpanArray() => [.. _spans];

    internal int ItemCount => _items.Count;

    // Inserts items before the body content (for example the header of a page, which is made after the body).
    internal void Insert(int index, PageItem item) => _items.Insert(index, item);
}

/// <summary>The hook for what belongs to the page and not to the body: headers, footers and notes (Task 9).</summary>
internal interface IPageChrome
{
    /// <summary>Called when a page starts, before any body block is placed; may change <see cref="PageBuilder.BodyTop"/> and <see cref="PageBuilder.BodyBottom"/>.</summary>
    void Begin(PageBuilder page);

    /// <summary>Called when the body of the page is complete, before the page is published; adds header, footer and note items.</summary>
    void End(PageBuilder page);
}
