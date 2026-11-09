using Raffica.Core.Pdf;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Text;

namespace Raffica.Pdf.Redacting;

/// <summary>Where the search of the Find panel stands.</summary>
internal enum FindState
{
    /// <summary>No search has run (or the last one was reset).</summary>
    Idle,

    /// <summary>A search is running; its hits arrive in batches.</summary>
    Running,

    /// <summary>The search was canceled: it has no result, and "no matches" must not be said.</summary>
    Canceled,

    /// <summary>The search ran to the end; <see cref="FindSession.Report"/> says what it could not look at.</summary>
    Done,

    /// <summary>The search stopped on an error; nothing is shown.</summary>
    Failed,
}

/// <summary>Why <see cref="FindSession"/> did not start a search.</summary>
internal enum FindRefusal
{
    /// <summary>Nothing was refused.</summary>
    None,

    /// <summary>The term is empty or white space; the panel is reset.</summary>
    EmptyTerm,

    /// <summary>The term is longer than <see cref="RedactionLimits.MaxTermLength"/>; the previous result stays as it was.</summary>
    TermTooLong,
}

/// <summary>
/// One search of the Find panel of a redact session at a time: a term or the patterns, over Core's <see cref="RedactionSearch"/> and
/// <see cref="RedactionPatternSearch"/>. The search runs on a pool thread and posts its hits to the UI thread in batches of
/// <see cref="BatchSize"/>; a new search, <see cref="Cancel"/>, the end of the redact mode and <see cref="Dispose"/> cancel it. A canceled or failed
/// search keeps no hits. The marks are made only from a search that ran to the end, and only when it was complete or the user accepted that it was
/// not (<see cref="Acknowledge"/>, for that one search). Made by the Find panel when it is opened; every member belongs to the UI thread.
/// </summary>
internal sealed class FindSession : IDisposable
{
    /// <summary>How many hits are posted to the UI thread at once.</summary>
    public const int BatchSize = 200;

    private readonly RedactSession _redact;
    private readonly List<RedactionHit> _hits = [];
    private readonly Dictionary<RedactionHit, int> _indexes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<int> _ticked = [];
    private CancellationTokenSource? _cts;
    private int _generation;
    private bool _disposed;

    public FindSession(RedactSession redact)
    {
        ArgumentNullException.ThrowIfNull(redact);
        _redact = redact;
        redact.Ending += OnModeEnding;
    }

    /// <summary>Raised on the UI thread after the state, the hits, the ticks or the acknowledgement changed.</summary>
    public event Action? Changed;

    public FindState State { get; private set; }

    public bool IsRunning => State == FindState.Running;

    /// <summary>Whether the last search was canceled (it has no result then).</summary>
    public bool IsCanceled => State == FindState.Canceled;

    /// <summary>The term of the last search; empty for a pattern search.</summary>
    public string Term { get; private set; } = string.Empty;

    public SearchOptions Options { get; private set; }

    /// <summary>Whether the last search looked for patterns instead of a term.</summary>
    public bool IsPatternSearch { get; private set; }

    /// <summary>The hits so far, in the order the search found them. Empty after a cancel or a failure.</summary>
    public IReadOnlyList<RedactionHit> Hits => _hits;

    /// <summary>What the last search could not look at; null before the first search.</summary>
    public RedactionSearchReport? Report { get; private set; }

    /// <summary>How many pages are pictures of text (the search reads no text from them); known when the search is done.</summary>
    public int PictureCount { get; private set; }

    /// <summary>Whether the user accepted that the current search did not cover everything. Covers this search only.</summary>
    public bool AcknowledgedIncomplete { get; private set; }

    /// <summary>How many marks the last marking added.</summary>
    public int LastAdded { get; private set; }

    /// <summary>How many hits the last marking left out because they cannot be marked.</summary>
    public int LastNotMarkable { get; private set; }

    /// <summary>Whether the last marking went through (even when it added nothing because the hits were marked already); false once anything else happens.</summary>
    public bool HasMarkFeedback { get; private set; }

    /// <summary>Why the last request for a search was refused; <see cref="FindRefusal.None"/> once a search starts.</summary>
    public FindRefusal Refusal { get; private set; }

    /// <summary>Whether the finished search missed pages or stopped at the limit.</summary>
    public bool IsIncomplete => State == FindState.Done && Report is { IsComplete: false };

    /// <summary>The groups of the hits of a pattern search.</summary>
    public IReadOnlyList<PatternGroup> Groups => IsPatternSearch ? PatternGroups.Of(_hits) : [];

    public int TickedCount => _ticked.Count;

    /// <summary>The position of a hit in <see cref="Hits"/>, or -1.</summary>
    public int IndexOf(RedactionHit hit) => _indexes.GetValueOrDefault(hit, -1);

    public bool IsTicked(int index) => _ticked.Contains(index);

    /// <summary>Ticks or unticks a hit. A hit that cannot be marked, and one whose checksum failed (it has its own button), cannot be ticked.</summary>
    public bool SetTicked(int index, bool ticked)
    {
        if (index < 0 || index >= _hits.Count)
        {
            return false;
        }

        if (!ticked)
        {
            if (_ticked.Remove(index))
            {
                Changed?.Invoke();
            }

            return true;
        }

        if (!CanTick(_hits[index]))
        {
            return false;
        }

        if (_ticked.Add(index))
        {
            Changed?.Invoke();
        }

        return true;
    }

    /// <summary>The count of hits per kind of the pattern search.</summary>
    public int CountOf(RedactionPatternKind kind) => _hits.Count(h => h.Pattern == kind);

    /// <summary>Searches for a term (letter case and whole words as given). False when the term is empty, white space or too long, or the search did not run to the end.</summary>
    public Task<bool> RunAsync(string term, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(term);
        if (term.Length > RedactionLimits.MaxTermLength)
        {
            // Refused with a reason; the result on screen stays as it was.
            Refusal = FindRefusal.TermTooLong;
            Changed?.Invoke();
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(term))
        {
            Reset();
            Refusal = FindRefusal.EmptyTerm;
            return Task.FromResult(false);
        }

        return StartAsync(term, options, patterns: null, static (doc, search, report, texts, ct) =>
            RedactionSearch.FindAsync(doc, search.Term, search.Options, report, texts, ct));
    }

    /// <summary>Searches for the patterns of the given kinds. False when no kind is given or the search did not run to the end.</summary>
    public Task<bool> RunPatternsAsync(IReadOnlySet<RedactionPatternKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Count == 0)
        {
            Reset();
            return Task.FromResult(false);
        }

        var copy = new HashSet<RedactionPatternKind>(kinds);
        return StartAsync(string.Empty, default, copy, static (doc, search, report, texts, ct) =>
            RedactionPatternSearch.FindAsync(doc, search.Patterns!, report, texts, ct));
    }

    /// <summary>Stops a running search; it then has no result.</summary>
    public void Cancel()
    {
        if (State != FindState.Running)
        {
            return;
        }

        _generation++;
        _cts?.Cancel();
        ClearResult();
        State = FindState.Canceled;
        Changed?.Invoke();
    }

    /// <summary>Accepts that the current search did not cover everything, so its hits can be marked. Only for this search.</summary>
    public void Acknowledge()
    {
        if (IsIncomplete && !AcknowledgedIncomplete)
        {
            AcknowledgedIncomplete = true;
            Changed?.Invoke();
        }
    }

    /// <summary>Marks every hit that can be marked, ignoring the ticks (not the hits whose checksum failed: <see cref="AddFailed"/> does).</summary>
    public RedactResult MarkAll() => Mark(Enumerable.Range(0, _hits.Count).Where(i => !IsFailed(_hits[i])));

    /// <summary>Marks the ticked hits.</summary>
    public RedactResult MarkTicked() => Mark(_ticked.Order().Where(i => !IsFailed(_hits[i])));

    /// <summary>Adds the hits of the patterns whose checksum failed (a typo in a number is still a number worth hiding).</summary>
    public RedactResult AddFailed() => Mark(Enumerable.Range(0, _hits.Count).Where(i => IsFailed(_hits[i])));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _redact.Ending -= OnModeEnding;
        _generation++;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private static bool IsFailed(RedactionHit hit) => hit.Certainty == PatternCertainty.ChecksumFailed;

    private static bool CanTick(RedactionHit hit) => hit.Markable && !IsFailed(hit);

    private void OnModeEnding()
    {
        Cancel();
        Reset();
    }

    private void Reset()
    {
        _generation++;
        _cts?.Cancel();
        ClearResult();
        Refusal = FindRefusal.None;
        State = FindState.Idle;
        Changed?.Invoke();
    }

    private void ClearResult()
    {
        _hits.Clear();
        _indexes.Clear();
        _ticked.Clear();
        Report = null;
        PictureCount = 0;
        AcknowledgedIncomplete = false;
        LastAdded = 0;
        LastNotMarkable = 0;
        HasMarkFeedback = false;
    }

    private RedactResult Mark(IEnumerable<int> indexes)
    {
        HasMarkFeedback = false;
        if (State == FindState.Running)
        {
            return RedactResult.Refused(RedactProblem.SearchRunning);
        }

        if (State != FindState.Done)
        {
            return RedactResult.Refused(RedactProblem.NothingToMark);
        }

        if (IsIncomplete && !AcknowledgedIncomplete)
        {
            return RedactResult.Refused(RedactProblem.SearchIncomplete);
        }

        var chosen = indexes.Select(i => _hits[i]).ToList();
        var marks = IsPatternSearch
            ? RedactionSearch.ToMarks(chosen, out var notMarkable)
            : RedactionSearch.ToMarks(chosen, Term, everywhere: true, Options, out notMarkable);
        if (marks.Count == 0)
        {
            return RedactResult.Refused(RedactProblem.NothingToMark);
        }

        var result = _redact.AddMarks(marks, out var added);
        LastAdded = result.IsDone ? added : 0;
        LastNotMarkable = result.IsDone ? notMarkable : 0;
        HasMarkFeedback = result.IsDone;
        Changed?.Invoke();
        return result;
    }

    private sealed record Search(string Term, SearchOptions Options, IReadOnlySet<RedactionPatternKind>? Patterns);

    private async Task<bool> StartAsync(
        string term,
        SearchOptions options,
        IReadOnlySet<RedactionPatternKind>? patterns,
        Func<PdfDocument, Search, RedactionSearchReport, PageTextSource?, CancellationToken, IAsyncEnumerable<RedactionHit>> open)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _generation++;
        var generation = _generation;
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = new CancellationTokenSource();
        _cts = cts;

        ClearResult();
        Refusal = FindRefusal.None;
        Term = term;
        Options = options;
        IsPatternSearch = patterns is not null;
        var report = new RedactionSearchReport();
        Report = report;

        var document = _redact.Tab.Session.Document;
        if (document is null || !_redact.IsActive)
        {
            State = FindState.Failed;
            Changed?.Invoke();
            return false;
        }

        State = FindState.Running;
        Changed?.Invoke();

        var texts = PageTexts.SourceOf(_redact.Tab);
        var search = new Search(term, options, patterns);
        var ui = _redact.Ui;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = cts.Token;

        void Post(Action<bool> action) => ui.Post(_ => action(!_disposed && generation == _generation), null);

        _ = Task.Run(async () =>
        {
            try
            {
                var batch = new List<RedactionHit>(BatchSize);
                await foreach (var hit in open(document, search, report, texts, token).WithCancellation(token).ConfigureAwait(false))
                {
                    batch.Add(hit);
                    if (batch.Count >= BatchSize)
                    {
                        var full = batch;
                        batch = new List<RedactionHit>(BatchSize);
                        Post(current => Append(full, current));
                    }
                }

                if (batch.Count > 0)
                {
                    var last = batch;
                    Post(current => Append(last, current));
                }

                var pictures = 0;
                for (var page = 0; page < document.PageCount; page++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (await ScanPageProbe.IsPictureAsync(document, page, token).ConfigureAwait(false))
                        {
                            pictures++;
                        }
                    }
                    catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
                    {
                        // A page the probe cannot read is not named as a picture.
                    }
                }

                Post(current =>
                {
                    if (current)
                    {
                        PictureCount = pictures;
                        State = FindState.Done;
                        Changed?.Invoke();
                    }

                    done.TrySetResult(current);
                });
            }
            catch (OperationCanceledException)
            {
                Post(current =>
                {
                    if (current)
                    {
                        ClearResult();
                        State = FindState.Canceled;
                        Changed?.Invoke();
                    }

                    done.TrySetResult(false);
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Post(current =>
                {
                    if (current)
                    {
                        ClearResult();
                        State = FindState.Failed;
                        Changed?.Invoke();
                    }

                    done.TrySetResult(false);
                });
            }
        }, CancellationToken.None);

        return await done.Task;
    }

    /// <summary>Adds a batch of hits (the UI side of the search; internal for tests that need a hit the fixtures do not have).</summary>
    internal void Append(List<RedactionHit> batch, bool current)
    {
        if (!current)
        {
            return;
        }

        foreach (var hit in batch)
        {
            var index = _hits.Count;
            _hits.Add(hit);
            _indexes[hit] = index;
            if (CanTick(hit))
            {
                _ticked.Add(index);
            }
        }

        Changed?.Invoke();
    }
}
