using System.Diagnostics;
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
/// <param name="ImageBytes">
/// Gives the bytes of the picture a relationship id stands for (the same function an <see cref="ImageCache"/> takes). The layout reads
/// only the header of each picture, once, to tell a picture Skia can draw from a metafile, corrupt bytes or a missing part (those become
/// a marked box of the stated size) and to size a picture whose extent is missing; it never decodes a bitmap. Null: every picture is
/// assumed drawable. The function may throw for a missing part.
/// </param>
public sealed record LayoutOptions(LayoutMode Mode = LayoutMode.Pages, float FlowTextWidth = 520f, Func<string, ReadOnlyMemory<byte>>? ImageBytes = null);

/// <summary>
/// The pages of one document. <see cref="LayOutFirstPage"/> makes page 1, <see cref="LayOutRestAsync"/> the rest on another thread;
/// every finished page is immutable and published at once, so a reader (the UI) can look at <see cref="Page"/> for every index
/// below <see cref="LaidOutPages"/> while the layout goes on. The layout never throws on what a document contains.
/// </summary>
/// <remarks>
/// Threads: any thread may read <see cref="LaidOutPages"/>, <see cref="Page"/>, <see cref="Locate"/>, <see cref="BlockAt"/>,
/// <see cref="IsComplete"/>, <see cref="FirstPageIsFinal"/> and <see cref="Notes"/> at any time. The methods that lay out take a
/// gate, one at a time; a call from the UI thread waits for the page the background thread is on, not for the whole document. The
/// document model must not change while a layout runs on it; <see cref="ApplyEdit"/> is the one way to follow a change of the model (it
/// can make <see cref="LaidOutPages"/> smaller and raises <see cref="Revision"/>; see there for what readers can rely on).
/// <para>
/// A page whose header or footer shows NUMPAGES or SECTIONPAGES shows "…" there until the layout is complete and has
/// <see cref="LaidOutPage.IsProvisional"/> set; the layout then publishes a new page object in the same place (only the header and
/// footer differ, the body items are shared), raises <see cref="Revision"/> once, and only then sets <see cref="IsComplete"/>. A caller
/// that keeps a page object (or a bitmap of it) must re-read the pages that were provisional when <see cref="Revision"/> changes;
/// the old object stays unchanged.
/// </para>
/// </remarks>
public sealed partial class DocumentLayout
{
    private readonly object _gate = new();
    private readonly PageList _pages = new();
    private readonly ParagraphPageMap _paragraphPages = new();
    private readonly PreeditSlot _preedit = new();
    private readonly FontCatalog _fonts;
    private readonly LayoutOptions _options;
    private List<string> _notes;
    private Engine _engine;
    private Action<PageBuilder>? _pageFinished;
    private int _revision;
    private bool _complete;
    private bool _firstPageFinal;
    private ExceptionDispatchInfo? _fault;

    private DocumentLayout(WordDocument document, FontCatalog fonts, LayoutOptions options)
    {
        _fonts = fonts;
        _options = options;
        _notes = [];
        _engine = Engine.Build(document, fonts, options, _notes, _gate, _preedit);
    }

    /// <summary>Everything that depends on the document model: the filler and its iterator, the block tables and the planners. A replaced model gets a new one.</summary>
    private sealed class Engine
    {
        public required PageFiller Filler { get; init; }

        public required IEnumerator<LaidOutPage> Fill { get; set; }

        public required int[] BlockPage { get; init; }

        public required float[] BlockY { get; init; }

        public required int LastBodyIndex { get; init; }

        public required HeaderFooterPlan? Chrome { get; init; }

        public required WordDocument Document { get; init; }

        /// <summary>The numbering part this engine's counters were made from (the part is created by the first list command of a file that had none, and a counter made from null counts nothing).</summary>
        public NumberingDefinitions? Numbering { get; init; }

        public required BlockPlanner Planner { get; init; }

        public required List<SectionPlan> Sections { get; init; }

        /// <summary>The page count and the pages per section of the finished layout; set when the last page was out (read under the gate by an edit that needs the totals of the old layout).</summary>
        public int FinalPages { get; set; }

        public int[] FinalSectionPages { get; set; } = [];

        /// <summary>The highest body index the filler had looked at when page 1 was made (-1 before that); page 1 is final when the document goes on beyond it.</summary>
        public int FirstTouched { get; set; } = -1;

        public static Engine Build(WordDocument document, FontCatalog fonts, LayoutOptions options, List<string> notes, object gate, PreeditSlot preedit)
        {
            var flow = options.Mode == LayoutMode.Flow;
            var flat = FlatBlocks.From(document.Body.Blocks);
            var blockPage = new int[document.Body.Blocks.Count];
            var blockY = new float[document.Body.Blocks.Count];
            Array.Fill(blockPage, -1);

            var sections = flow
                ? [SectionPlan.ForStrips(FlowSegmenter.Geometry(options), flat.Count)]
                : SectionPlan.Build(flat, document.Body.FinalSection, notes);

            var styles = new StyleResolver(document.Styles, document.Settings, document.Numbering);
            var layouter = new ParagraphLayouter(fonts) { Hyphenator = Hyphenator.Default };
            var probe = new ImageProbe(options.ImageBytes);
            var planner = new BlockPlanner(styles, new NumberingCounter(document.Numbering), layouter, document.Settings, probe, flow) { Gate = gate, Preedit = preedit, BodyBlocks = () => document.Body.Blocks };
            var filler = new PageFiller(sections, flat, planner, flow, blockPage, blockY);
            HeaderFooterPlan? chrome = null;
            if (!flow)
            {
                // Headers, footers and notes make their own planners: list numbers in them never disturb the numbering of the body.
                BlockPlanner Aux() => new(styles, new NumberingCounter(document.Numbering), layouter, document.Settings, probe) { Preedit = preedit };
                var noteSource = new NotePlanner(document, Aux);
                planner.Context = new PlanContext { Notes = noteSource };
                chrome = new HeaderFooterPlan(document, sections, Aux, noteSource);
                filler.Chrome = chrome;
            }

            return new Engine
            {
                Filler = filler,
                Fill = filler.Fill().GetEnumerator(),
                BlockPage = blockPage,
                BlockY = blockY,
                LastBodyIndex = document.Body.Blocks.Count - 1,
                Chrome = chrome,
                Document = document,
                Numbering = document.Numbering,
                Planner = planner,
                Sections = sections,
            };
        }
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

    /// <summary>How many headers and footers were laid out (reuse not counted).</summary>
    /// <summary>Test seam: called with each page builder when its page is made.</summary>
    internal Action<PageBuilder>? PageFinished
    {
        get => _engine.Filler.PageFinished;
        set
        {
            _pageFinished = value;
            _engine.Filler.PageFinished = value;
        }
    }

    /// <summary>
    /// Counts how often published pages were replaced by final ones: it is 0 until the layout completes, then 1 if any page was provisional
    /// (<see cref="LaidOutPage.IsProvisional"/>), and stays 0 for a document without NUMPAGES or SECTIONPAGES. It changes before
    /// <see cref="IsComplete"/> turns true. When it changes, read again the pages that were provisional; every other page object is as it was.
    /// </summary>
    public int Revision => Volatile.Read(ref _revision);

    /// <summary>How many sections the layout's plan of the body has (1 for a document with no section break and for the writing view's strips). It is the count the layout made anyway; asking costs nothing and reads no body.</summary>
    public int SectionCount => Volatile.Read(ref _engine).Sections.Count;

    internal int ChromeLayouts => _engine.Chrome?.LayoutCount ?? 0;

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

    /// <summary>What the layout repaired or could not honour (an absurd page size, margins larger than the page, columns that do not fit), one sentence each. Known from <see cref="Create"/> on; replaced once by <see cref="ReplaceModel"/>.</summary>
    public IReadOnlyList<string> Notes => Volatile.Read(ref _notes);

    /// <summary>
    /// A finished page; <paramref name="index"/> must be below <see cref="LaidOutPages"/>. After <see cref="ApplyEdit"/> the number of pages can
    /// fall, so a reader that read <see cref="LaidOutPages"/> earlier can find the index gone: it throws then (<see cref="TryGetPage"/> says so instead).
    /// </summary>
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
    /// far, and finishes the header and footer refresh an edit of a header or footer part left pending (<see cref="ChromeRefreshPending"/>), in batches, before it lays out more.
    /// When <paramref name="token"/> is cancelled the layout stops at the end of the page or batch it is on and the task ends canceled;
    /// the layout stays valid and a later call carries on from there. It ends when <see cref="IsSettled"/>.
    /// </summary>
    public Task LayOutRestAsync(IProgress<int>? progress, CancellationToken token) =>
        Task.Run(
            () =>
            {
                while (!IsSettled)
                {
                    token.ThrowIfCancellationRequested();

                    // The pages that wait for a new header or footer first (a batch is a few pages: cheap and visible), then the rest of the document.
                    if (ChromeRefreshPending)
                    {
                        if (RefreshChromeBatch(ChromeBatchMilliseconds * Stopwatch.Frequency / 1000))
                        {
                            progress?.Report(LaidOutPages);
                        }
                    }
                    else if (Step())
                    {
                        progress?.Report(LaidOutPages);
                    }
                }
            },
            token);

    /// <summary>
    /// Swaps the document model of a layout that holds only the start of a document (a prefix model) for the whole document, once
    /// page 1 is final (<see cref="FirstPageIsFinal"/>) and nothing but page 1 is published. The published page 1 stays as it is;
    /// the layout goes on from page 2 over the whole model, as if it had been created with it. The blocks of the prefix must be the
    /// first blocks of <paramref name="full"/> and its final section and parts must be the real ones (the first page then comes out
    /// the same, which this method checks by laying page 1 out once more on the new model and comparing its text).
    /// </summary>
    /// <remarks>
    /// The cost is one more layout of page 1 (a few milliseconds, on the calling thread: call it from the background thread that
    /// lays out the rest). <see cref="Locate"/> and <see cref="BlockAt"/> answer for page 1 before and after, and the block tables grow to the whole document.
    /// Page 1 is published again as the page the new model makes (the same text, the same geometry; <see cref="Page"/> returns a new object whose text
    /// lines name the paragraphs of the new model); <see cref="Revision"/> is not raised for it, a view that reads <c>Page(0)</c> for every frame needs no
    /// notice, and one that keeps the old object keeps an equal page. Positions made on the prefix model's paragraphs are not valid afterwards. <see cref="Notes"/> are those of the new model from then on (they are the only thing that changes); <see cref="Revision"/> is not reset.
    /// The new model must not change while the layout runs on it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Page 1 is not final, other pages are already published, or the first page of the new model differs (the prefix was not the start of it).</exception>
    public void ReplaceModel(WordDocument full)
    {
        ArgumentNullException.ThrowIfNull(full);
        lock (_gate)
        {
            _fault?.Throw();
            if (!FirstPageIsFinal)
            {
                throw new InvalidOperationException("The model can be replaced only after page 1 was laid out and is final.");
            }

            if (_pages.Count != 1)
            {
                throw new InvalidOperationException("The model can be replaced only while page 1 is the only published page.");
            }

            // Page 1 again on the new model, so that the filler, the planners and the numbering stand where they would stand after
            // page 1 of a layout made with the whole document. The page it makes (equal text, checked below) replaces the published one,
            // so page 1 holds the paragraphs of the new model: a position made on them is found by Caret, Move and HitTest.
            var notes = new List<string>();
            _preedit.Current = null;
            var engine = Engine.Build(full, _fonts, _options, notes, _gate, _preedit);
            try
            {
                if (!engine.Fill.MoveNext() || !string.Equals(engine.Fill.Current.Text, _pages[0].Text, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The first page of the new model differs from the published page 1.");
                }
            }
            catch (Exception e) when (e is not InvalidOperationException)
            {
                _fault = ExceptionDispatchInfo.Capture(e);
                throw;
            }

            engine.Filler.PageFinished = _pageFinished;
            engine.FirstTouched = engine.Filler.MaxTouchedBodyIndex;
            _paragraphPages.Register(engine.Fill.Current);
            _pages.Replace(0, engine.Fill.Current);
            ClearStale(0, int.MaxValue);
            Volatile.Write(ref _notes, notes);
            Volatile.Write(ref _engine, engine);
        }
    }

    /// <summary>The page and y of the start of a top-level body block, or null when that page is not laid out yet (or the block takes no room).</summary>
    public (int Page, float Y)? Locate(int bodyBlockIndex)
    {
        var engine = Volatile.Read(ref _engine);
        if ((uint)bodyBlockIndex >= (uint)engine.BlockPage.Length)
        {
            return null;
        }

        // Read the count first: an entry for a page below it was written before that page was published.
        var count = LaidOutPages;
        var page = Volatile.Read(ref engine.BlockPage[bodyBlockIndex]);
        return page >= 0 && page < count ? (page, Volatile.Read(ref engine.BlockY[bodyBlockIndex])) : null;
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
                more = _engine.Fill.MoveNext();
            }
            catch (Exception e)
            {
                _fault = ExceptionDispatchInfo.Capture(e);
                throw;
            }

            if (!more)
            {
                // Pages that show a page count are published again with the count, before the layout calls itself complete.
                try
                {
                    (_engine.FinalPages, _engine.FinalSectionPages) = _engine.Filler.Totals();
                    var revisions = _engine.Filler.Revisions();
                    foreach (var (index, revised) in revisions)
                    {
                        _paragraphPages.Register(revised);
                        _pages.Replace(index, revised);
                    }

                    if (revisions.Count > 0)
                    {
                        Volatile.Write(ref _revision, _revision + 1);
                    }
                }
                catch (Exception e)
                {
                    _fault = ExceptionDispatchInfo.Capture(e);
                    throw;
                }

                Volatile.Write(ref _complete, true);
                return false;
            }

            // The flag is set before the page is published, so a reader that sees page 1 sees the final answer.
            if (_pages.Count == 0)
            {
                _engine.FirstTouched = _engine.Filler.MaxTouchedBodyIndex;
                Volatile.Write(ref _firstPageFinal, _engine.FirstTouched < _engine.LastBodyIndex);
            }

            // The map first: a reader that finds an entry for a page that is not published yet skips it by the count.
            _paragraphPages.Register(_engine.Fill.Current);
            _pages.Add(_engine.Fill.Current);
            return true;
        }
    }
}
