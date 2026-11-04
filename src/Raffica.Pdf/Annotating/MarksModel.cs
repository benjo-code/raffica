using Avalonia.Threading;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The marks of one document as the marks pane lists them. Core has no document-wide listing: the model reads the annotations page by page
/// (<see cref="PdfDocument.GetAnnotationsAsync(int, CancellationToken)"/>, background priority, one page after the other) and only after the
/// page view has drawn its first sharp page, so opening a file asks for nothing. Afterwards only the pages named in
/// <see cref="Raffica.Core.Viewing.ViewerSession.PagesInvalidated"/> are read again; the other pages keep their entries (and so the list its
/// scroll position and selection). Used on the UI thread; the invalidation arrives on any thread and is only posted.
/// </summary>
internal sealed class MarksModel : IDisposable
{
    /// <summary>From this many freehand strokes without a comment on one page, the strokes collapse into one row.</summary>
    public const int GroupThreshold = 5;

    /// <summary>The longest excerpt of a marked text.</summary>
    private const int ExcerptLength = 120;

    private static readonly HashSet<string> s_unlisted = new(StringComparer.Ordinal) { "Link", "Widget", "Popup" };

    private readonly PdfTab _tab;
    private readonly PageView? _view;
    private readonly SortedDictionary<int, IReadOnlyList<MarkEntry>> _pages = [];
    private readonly Dictionary<int, int> _reads = [];
    private readonly HashSet<int> _expanded = [];
    private Task _chain = Task.CompletedTask;
    private bool _started;
    private bool _disposed;

    /// <param name="tab">The document.</param>
    /// <param name="view">
    /// The page view whose first sharp page starts the reading; <see langword="null"/>: start at once (tests, and a view that already
    /// drew its first page).
    /// </param>
    public MarksModel(PdfTab tab, PageView? view = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tab = tab;
        _view = view;
        _tab.Session.PagesInvalidated += OnPagesInvalidated;
        if (view is null || view.FirstSharpPageShown)
        {
            Start();
        }
        else
        {
            view.FirstSharpPage += Start;
        }
    }

    /// <summary>Raised on the UI thread after the entries of some pages changed (not for every page of a long first read: the consumer may collapse bursts).</summary>
    public event Action? Changed;

    /// <summary>The task that ends when every read started so far is done.</summary>
    public Task Idle => _chain;

    /// <summary>How many page reads were made so far (for tests).</summary>
    public int ReadCount
    {
        get
        {
            var count = 0;
            foreach (var reads in _reads.Values)
            {
                count += reads;
            }

            return count;
        }
    }

    /// <summary>Whether the first read of all pages is done (until then "no marks" would be a guess).</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Whether the document has a mark that the list shows (whatever the filter).</summary>
    public bool HasMarks => _pages.Values.Any(p => p.Count > 0);

    /// <summary>How often a page was read (for tests).</summary>
    public int ReadsOf(int page) => _reads.GetValueOrDefault(page);

    /// <summary>
    /// Reads the marks of some pages (all pages when <paramref name="pages"/> is <see langword="null"/>), one after the other at background
    /// priority, after the reads that were asked for before. Before the first sharp page of the view nothing is read: the first read
    /// takes every page then. Never throws.
    /// </summary>
    /// <param name="pages">The 0-based pages, or <see langword="null"/> for all.</param>
    /// <returns>The task that ends when the pages are read.</returns>
    public Task RefreshAsync(IReadOnlySet<int>? pages)
    {
        if (!_started || _disposed)
        {
            return _chain;
        }

        var copy = pages?.ToArray();
        _chain = ReadChainedAsync(_chain, copy);
        return _chain;
    }

    /// <summary>The rows of the list for a filter: sorted by page, top, left and index; many plain strokes of one page are one group row.</summary>
    /// <param name="filter">Which marks.</param>
    /// <returns>The rows, a group followed by its strokes while it is expanded.</returns>
    public IReadOnlyList<MarkRow> Rows(MarkFilter filter)
    {
        var rows = new List<MarkRow>();
        foreach (var (page, entries) in _pages)
        {
            var shown = entries.Where(e => e.Matches(filter)).ToList();
            var strokes = shown.Where(e => e.IsPlainStroke).ToList();
            var grouped = strokes.Count >= GroupThreshold;
            var expanded = grouped && _expanded.Contains(page);
            var groupAdded = false;
            foreach (var entry in shown)
            {
                if (grouped && entry.IsPlainStroke)
                {
                    if (!groupAdded)
                    {
                        rows.Add(new GroupRow(page, strokes, expanded));
                        groupAdded = true;
                    }

                    if (expanded)
                    {
                        rows.Add(new EntryRow(entry, InGroup: true));
                    }

                    continue;
                }

                rows.Add(new EntryRow(entry));
            }
        }

        return rows;
    }

    /// <summary>The marks of a filter counted over all pages, and the pages they are on.</summary>
    /// <param name="filter">Which marks.</param>
    /// <returns>The number of marks and the number of pages that have one.</returns>
    public (int Marks, int Pages, int FirstPage) Count(MarkFilter filter)
    {
        var marks = 0;
        var pages = 0;
        var first = -1;
        foreach (var (page, entries) in _pages)
        {
            var count = entries.Count(e => e.Matches(filter));
            if (count > 0)
            {
                marks += count;
                pages++;
                first = first < 0 ? page : first;
            }
        }

        return (marks, pages, first);
    }

    /// <summary>The summary line of the list for a filter ("3 marks on 2 pages"); empty while there is no mark of that kind.</summary>
    /// <param name="filter">Which marks.</param>
    /// <returns>The text in the UI language.</returns>
    public string Summary(MarkFilter filter)
    {
        var (marks, pages, first) = Count(filter);
        return marks switch
        {
            0 => string.Empty,
            1 => PdfStrings.Fill(PdfStrings.MarkSummaryOneFormat, first + 1),
            _ when pages == 1 => PdfStrings.Fill(PdfStrings.MarkSummaryOnePageFormat, marks, first + 1),
            _ => PdfStrings.Fill(PdfStrings.MarkSummaryPagesFormat, marks, pages),
        };
    }

    /// <summary>Whether the freehand strokes of a page are shown one by one.</summary>
    public bool IsExpanded(int page) => _expanded.Contains(page);

    /// <summary>Opens the group of a page, or closes it.</summary>
    public void ToggleGroup(int page)
    {
        if (!_expanded.Remove(page))
        {
            _expanded.Add(page);
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tab.Session.PagesInvalidated -= OnPagesInvalidated;
        if (_view is not null)
        {
            _view.FirstSharpPage -= Start;
        }
    }

    private void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        if (_view is not null)
        {
            _view.FirstSharpPage -= Start;
        }

        _ = RefreshAsync(null);
    }

    // Any thread: only posts. The pages are read on the UI thread, and never waited for here.
    private void OnPagesInvalidated(IReadOnlySet<int> pages)
    {
        var copy = pages.ToHashSet();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                _ = RefreshAsync(copy);
            }
        });
    }

    private async Task ReadChainedAsync(Task previous, int[]? pages)
    {
        await previous;
        if (_disposed || _tab.Session.Document is not { } document)
        {
            return;
        }

        var list = pages ?? Enumerable.Range(0, document.PageCount).ToArray();
        foreach (var page in list)
        {
            if (_disposed)
            {
                return;
            }

            if (page < 0 || page >= document.PageCount)
            {
                if (_pages.Remove(page))
                {
                    Changed?.Invoke();
                }

                continue;
            }

            var entries = await ReadPageAsync(document, page);
            if (entries is null || _disposed)
            {
                continue;
            }

            var had = _pages.TryGetValue(page, out var old) && old.Count > 0;
            _pages[page] = entries;
            if (had || entries.Count > 0)
            {
                Changed?.Invoke();
            }
        }

        if (pages is null && !IsLoaded && !_disposed)
        {
            IsLoaded = true;
            Changed?.Invoke();
        }
    }

    // The entries of a page, or null when the page cannot be read now (the entries of the last read stay).
    private async Task<IReadOnlyList<MarkEntry>?> ReadPageAsync(PdfDocument document, int page)
    {
        IReadOnlyList<PdfAnnotation> read;
        try
        {
            read = await document.GetAnnotationsAsync(page);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The document went away or the page is damaged: the page keeps what it had.
            return null;
        }
        finally
        {
            _reads[page] = _reads.GetValueOrDefault(page) + 1;
        }

        var listed = read.Where(a => !s_unlisted.Contains(a.Subtype)).ToList();
        PageText? text = null;
        if (listed.Any(a => a.Kind is AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.StrikeOut))
        {
            try
            {
                text = await document.GetPageTextAsync(page, PdfPriority.Background);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // No text, no excerpt; the entries are listed all the same.
            }
        }

        return listed
            .OrderBy(a => a.Bounds.Top).ThenBy(a => a.Bounds.Left).ThenBy(a => a.Index)
            .Select(a => new MarkEntry(new AnnotationLocator(a.Page, a.Index, a.Name), a, ExcerptOf(a, text), LockReasonOf(a)))
            .ToList();
    }

    private static string? ExcerptOf(PdfAnnotation annotation, PageText? text)
    {
        if (annotation.TextBox is { } box)
        {
            return Shorten(box.Text);
        }

        if (text is null || annotation.Kind is not (AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.StrikeOut))
        {
            return null;
        }

        var excerpt = AnnotationExcerpt.For(annotation, text, ExcerptLength);
        return excerpt.Length == 0 ? null : excerpt;
    }

    private static string? Shorten(string text)
    {
        var one = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length == 0 ? null : one.Length <= ExcerptLength ? one : one[..(ExcerptLength - 1)] + "…";
    }

    private static string? LockReasonOf(PdfAnnotation a)
    {
        if (a.Kind == AnnotationKind.Other)
        {
            return PdfStrings.MarkOtherReason;
        }

        if (!a.Editable)
        {
            return PdfStrings.MarkRepliesReason;
        }

        if (a.Kind == AnnotationKind.TextBox)
        {
            return a.GeometryEditable ? null : PdfStrings.MarkTextBoxLockedReason;
        }

        return a.Kind is AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.StrikeOut || a.GeometryEditable
            ? null
            : PdfStrings.SelectLockedReason;
    }
}
