using System.Runtime.ExceptionServices;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>Which of the two views the layout serves.</summary>
public enum LayoutMode
{
    /// <summary>Pages as printed: paper size, margins, columns, headers and footers.</summary>
    Pages,

    /// <summary>The writing view: one wide column cut into strips, no page chrome.</summary>
    Flow,
}

/// <summary>Options of a layout. Lengths are points.</summary>
/// <param name="Mode">The view.</param>
/// <param name="FlowTextWidth">The width of the text column in <see cref="LayoutMode.Flow"/> (520 pt is about 85 characters of Carlito 11 pt).</param>
public sealed record LayoutOptions(LayoutMode Mode = LayoutMode.Pages, float FlowTextWidth = 520f);

/// <summary>
/// The pages of one document. <see cref="LayOutFirstPage"/> makes page 1, <see cref="LayOutRestAsync"/> the rest on another thread;
/// every finished page is immutable and published at once, so a reader (the UI) can look at <see cref="Page"/> for every index
/// below <see cref="LaidOutPages"/> while the layout goes on. The layout never throws on what a document contains.
/// </summary>
/// <remarks>
/// Threads: any thread may read <see cref="LaidOutPages"/>, <see cref="Page"/>, <see cref="Locate"/>, <see cref="BlockAt"/>,
/// <see cref="IsComplete"/>, <see cref="FirstPageIsFinal"/> and <see cref="Notes"/> at any time. The methods that lay out take a
/// gate, one at a time; a call from the UI thread waits for the page the background thread is on, not for the whole document. The
/// document model must not change while a layout runs on it.
/// </remarks>
public sealed class DocumentLayout
{
    private readonly object _gate = new();
    private readonly PageList _pages = new();
    private readonly PageFiller _filler;
    private readonly IEnumerator<LaidOutPage> _fill;
    private readonly int[] _blockPage;
    private readonly float[] _blockY;
    private readonly int _lastBodyIndex;
    private readonly List<string> _notes;
    private bool _complete;
    private bool _firstPageFinal;
    private ExceptionDispatchInfo? _fault;

    private DocumentLayout(WordDocument document, FontCatalog fonts, LayoutOptions options)
    {
        var flow = options.Mode == LayoutMode.Flow;
        _notes = [];
        var flat = Flatten(document.Body);
        _lastBodyIndex = document.Body.Blocks.Count - 1;
        _blockPage = new int[document.Body.Blocks.Count];
        _blockY = new float[document.Body.Blocks.Count];
        Array.Fill(_blockPage, -1);

        var sections = flow
            ? [SectionPlan.ForStrips(FlowSegmenter.Geometry(options), flat.Count)]
            : SectionPlan.Build(flat, document.Body.FinalSection, _notes);

        var styles = new StyleResolver(document.Styles, document.Settings, document.Numbering);
        var layouter = new ParagraphLayouter(fonts) { Hyphenator = Hyphenator.Default };
        var planner = new BlockPlanner(styles, new NumberingCounter(document.Numbering), layouter, document.Settings);
        _filler = new PageFiller(sections, flat, planner, flow, _blockPage, _blockY);
        _fill = _filler.Fill().GetEnumerator();
    }

    /// <summary>Starts a layout; nothing is laid out until one of the <c>LayOut</c> methods is called. Page geometry repairs are in <see cref="Notes"/> from the start.</summary>
    public static DocumentLayout Create(WordDocument document, FontCatalog fonts, LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(options);
        return new DocumentLayout(document, fonts, options);
    }

    // Test seam: called before each page is laid out, with the number of pages published so far.
    internal Action<int>? BeforePage { get; set; }

    /// <summary>The number of pages published so far.</summary>
    public int LaidOutPages => _pages.Count;

    /// <summary>
    /// True once the layout has run out of pages. It is set by the call that asks for a page and finds none, so one step after the
    /// last page is published; <see cref="LayOutThrough"/> of the last page alone does not set it, and <see cref="LayOutRestAsync"/>
    /// does.
    /// </summary>
    public bool IsComplete => Volatile.Read(ref _complete);

    /// <summary>
    /// For a model that holds only the start of a document: true when page 1 was decided without ever looking at the model's last
    /// block, so more content after the prefix cannot change it. False before page 1 exists, and when page 1 reaches the last block
    /// (which may be cut short, or be followed by blocks that change how page 1 ends).
    /// </summary>
    public bool FirstPageIsFinal => Volatile.Read(ref _firstPageFinal);

    /// <summary>What the layout repaired or could not honour (an absurd page size, margins larger than the page, columns that do not fit), one sentence each. Known from <see cref="Create"/> on; never changes.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>A finished page; <paramref name="index"/> must be below <see cref="LaidOutPages"/>.</summary>
    public LaidOutPage Page(int index) => _pages[index];

    /// <summary>Lays out until page 1 is published (or the document is complete).</summary>
    public void LayOutFirstPage() => LayOutThrough(0);

    /// <summary>Lays out until page <paramref name="pageIndex"/> is published (or the document is complete).</summary>
    public void LayOutThrough(int pageIndex)
    {
        while (LaidOutPages <= pageIndex && !IsComplete)
        {
            Step();
        }
    }

    /// <summary>
    /// Lays out every remaining page on a thread of the pool, publishing each as it completes and reporting the number of pages so
    /// far. When <paramref name="token"/> is cancelled the layout stops at the end of the page it is on and the task ends canceled;
    /// the layout stays valid and a later call carries on from there.
    /// </summary>
    public Task LayOutRestAsync(IProgress<int>? progress, CancellationToken token) =>
        Task.Run(
            () =>
            {
                while (!IsComplete)
                {
                    token.ThrowIfCancellationRequested();
                    if (Step())
                    {
                        progress?.Report(LaidOutPages);
                    }
                }
            },
            token);

    /// <summary>The page and y of the start of a top-level body block, or null when that page is not laid out yet (or the block takes no room).</summary>
    public (int Page, float Y)? Locate(int bodyBlockIndex)
    {
        if ((uint)bodyBlockIndex >= (uint)_blockPage.Length)
        {
            return null;
        }

        // Read the count first: an entry for a page below it was written before that page was published.
        var count = LaidOutPages;
        var page = Volatile.Read(ref _blockPage[bodyBlockIndex]);
        return page >= 0 && page < count ? (page, Volatile.Read(ref _blockY[bodyBlockIndex])) : null;
    }

    /// <summary>
    /// The top-level body block that is at <paramref name="y"/> on a laid-out page: the block whose range holds it, otherwise the
    /// nearest block above it, otherwise the first block of the page; -1 for a page with no blocks. With columns, the first block in
    /// reading order whose range holds <paramref name="y"/> wins.
    /// </summary>
    public int BlockAt(int pageIndex, float y)
    {
        var spans = _pages[pageIndex].Spans;
        if (spans.Length == 0)
        {
            return -1;
        }

        var nearest = spans[0];
        foreach (var span in spans)
        {
            if (y >= span.Top && y < span.Bottom)
            {
                return span.BodyIndex;
            }

            if (span.Top <= y && span.Top >= nearest.Top)
            {
                nearest = span;
            }
        }

        return nearest.BodyIndex;
    }

    // Lays out one more page under the gate. Returns true when a page was published.
    private bool Step()
    {
        lock (_gate)
        {
            // A fault (a bug in the layout, never a document) stops the layout for good: the iterator is dead and would otherwise
            // report "no more pages" and hand out a silently truncated document.
            _fault?.Throw();
            if (IsComplete)
            {
                return false;
            }

            bool more;
            try
            {
                BeforePage?.Invoke(_pages.Count);
                more = _fill.MoveNext();
            }
            catch (Exception e)
            {
                _fault = ExceptionDispatchInfo.Capture(e);
                throw;
            }

            if (!more)
            {
                Volatile.Write(ref _complete, true);
                return false;
            }

            // The flag is set before the page is published, so a reader that sees page 1 sees the final answer.
            if (_pages.Count == 0)
            {
                Volatile.Write(ref _firstPageFinal, _filler.MaxTouchedBodyIndex < _lastBodyIndex);
            }

            _pages.Add(_fill.Current);
            return true;
        }
    }

    // The body blocks in reading order with the top-level index each came from; a content control's blocks stand in its place.
    private static List<FlatBlock> Flatten(Story body)
    {
        var flat = new List<FlatBlock>(body.Blocks.Count);
        for (var i = 0; i < body.Blocks.Count; i++)
        {
            Add(flat, body.Blocks[i], i, 0);
        }

        return flat;
    }

    private static void Add(List<FlatBlock> flat, Block block, int bodyIndex, int depth)
    {
        if (block is ContentControlBlock control && depth < 32)
        {
            foreach (var inner in control.Content)
            {
                Add(flat, inner, bodyIndex, depth + 1);
            }

            return;
        }

        flat.Add(new FlatBlock(block, bodyIndex));
    }
}
