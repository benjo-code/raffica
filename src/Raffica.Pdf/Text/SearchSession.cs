using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Text;

/// <summary>
/// The search of one document tab: the query, the hits found so far and the current one. A run searches the pages with Core's
/// <see cref="DocumentSearch"/> (one request per page, so rendering and other interactive work go on between pages), starting at the
/// current page and wrapping around, and it stops at <see cref="DocumentSearch.MaxHits"/>. The hits arrive in the order the run
/// finds them, so the first hit is the first one at or after the start page. Starting a new run, <see cref="Cancel"/>, closing the tab
/// and <see cref="Dispose"/> all stop the run before anything else happens. All members belong to the UI thread: the run is started
/// there and continues there.
/// </summary>
internal sealed class SearchSession : IDisposable
{
    private static readonly IReadOnlyList<SearchHit> NoHits = [];

    private readonly PdfTab _tab;
    private readonly Func<int> _startPage;
    private readonly List<SearchHit> _hits = [];
    private readonly Dictionary<int, List<SearchHit>> _byPage = [];
    private CancellationTokenSource? _cancel;
    private bool _disposed;

    /// <param name="tab">The tab whose document is searched.</param>
    /// <param name="startPage">The page a run starts at (the page the view shows); null: the first page.</param>
    public SearchSession(PdfTab tab, Func<int>? startPage = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tab = tab;
        _startPage = startPage ?? (() => 0);
        tab.Disposing += Cancel;
    }

    /// <summary>Raised on the UI thread whenever the hits, the current hit, the progress or the options changed.</summary>
    public event Action? Changed;

    /// <summary>Raised when the current hit changed (also when the first hit of a run arrived): the view scrolls to it.</summary>
    public event Action? CurrentChanged;

    /// <summary>The query of the last run (empty after <see cref="Clear"/>).</summary>
    public string Query { get; private set; } = string.Empty;

    /// <summary>The options of the last run.</summary>
    public SearchOptions Options { get; private set; }

    /// <summary>The hits found so far, in the order of the run (the start page first, then on, then from the top of the document).</summary>
    public IReadOnlyList<SearchHit> Hits => _hits;

    /// <summary>The index of the current hit in <see cref="Hits"/>, or -1 without a hit.</summary>
    public int Current { get; private set; } = -1;

    /// <summary>The current hit, or null.</summary>
    public SearchHit? CurrentHit => Current >= 0 && Current < _hits.Count ? _hits[Current] : null;

    /// <summary>Whether a run is still working.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Whether the run stopped at <see cref="DocumentSearch.MaxHits"/>.</summary>
    public bool LimitReached { get; private set; }

    /// <summary>Completes when the run that was started last has ended (also when it was canceled); completed without a run.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>The count for the bar: "12 hits", "No hits", "More than 10,000 hits"; "Searching…" until the first hit; empty without a query.</summary>
    public string Summary
    {
        get
        {
            if (Query.Length == 0)
            {
                return string.Empty;
            }

            if (LimitReached)
            {
                return PdfStrings.Fill(PdfStrings.SearchLimitFormat, DocumentSearch.MaxHits.ToString("N0", PdfStrings.Culture));
            }

            if (_hits.Count == 0)
            {
                return IsRunning ? PdfStrings.SearchSearching : PdfStrings.SearchNoHits;
            }

            return _hits.Count == 1 ? PdfStrings.SearchHitOne : PdfStrings.Fill(PdfStrings.SearchHitsFormat, _hits.Count.ToString("N0", PdfStrings.Culture));
        }
    }

    /// <summary>The hits on a page (never null), for the page view to draw.</summary>
    public IReadOnlyList<SearchHit> HitsOn(int page) => _byPage.TryGetValue(page, out var hits) ? hits : NoHits;

    /// <summary>
    /// Starts a run for the query, after stopping the previous one and dropping its hits. An empty or white-space query only clears.
    /// The query is searched as typed (no pattern characters): the run starts at the page <c>startPage</c> gave at this moment and wraps.
    /// </summary>
    public void Start(string query, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_disposed)
        {
            return;
        }

        StopRun();
        var hadCurrent = Current >= 0;
        _hits.Clear();
        _byPage.Clear();
        Current = -1;
        LimitReached = false;
        Options = options;
        var document = _tab.Session.Document;
        if (string.IsNullOrWhiteSpace(query) || document is null || document.PageCount == 0)
        {
            Query = string.Empty;
            Completion = Task.CompletedTask;
            Changed?.Invoke();
            if (hadCurrent)
            {
                CurrentChanged?.Invoke();
            }

            return;
        }

        Query = query;
        var cancel = _cancel = new CancellationTokenSource();
        IsRunning = true;
        var start = Math.Clamp(_startPage(), 0, document.PageCount - 1);
        Completion = RunAsync(document, query, options, start, cancel);
        Changed?.Invoke();
    }

    /// <summary>Stops the run (the hits found so far stay).</summary>
    public void Cancel()
    {
        if (StopRun())
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Stops the run and forgets the query and the hits (the bar was closed).</summary>
    public void Clear() => Start(string.Empty, Options);

    /// <summary>Makes the next hit the current one; after the last comes the first. Does nothing without a hit.</summary>
    public void Next() => Move(1);

    /// <summary>Makes the previous hit the current one; before the first comes the last. Does nothing without a hit.</summary>
    public void Previous() => Move(-1);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tab.Disposing -= Cancel;
        StopRun();
    }

    private void Move(int step)
    {
        if (_hits.Count == 0)
        {
            return;
        }

        Current = ((Current < 0 ? 0 : Current) + step + _hits.Count) % _hits.Count;
        Changed?.Invoke();
        CurrentChanged?.Invoke();
    }

    // Returns whether a run was working.
    private bool StopRun()
    {
        var was = IsRunning;
        IsRunning = false;
        if (_cancel is { } cancel)
        {
            _cancel = null;
            cancel.Cancel();
        }

        return was;
    }

    private async Task RunAsync(PdfDocument document, string query, SearchOptions options, int start, CancellationTokenSource cancel)
    {
        try
        {
            await foreach (var result in DocumentSearch.SearchAsync(document, query, options, start, cancel.Token))
            {
                if (cancel.IsCancellationRequested)
                {
                    return;
                }

                if (result.Hits.Count > 0)
                {
                    Add(result);
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A canceled run, a closed tab or a document that went away end the run quietly; the hits found so far stay.
        }
        finally
        {
            if (ReferenceEquals(_cancel, cancel))
            {
                _cancel = null;
                IsRunning = false;
                Changed?.Invoke();
            }

            cancel.Dispose();
        }
    }

    private void Add(SearchPageResult result)
    {
        var first = _hits.Count == 0;
        _byPage[result.PageIndex] = [.. result.Hits];
        _hits.AddRange(result.Hits);
        LimitReached = result.LimitReached;
        if (first)
        {
            Current = 0;
        }

        Changed?.Invoke();
        if (first)
        {
            CurrentChanged?.Invoke();
        }
    }
}
