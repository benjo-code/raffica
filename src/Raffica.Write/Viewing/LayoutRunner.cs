using Avalonia.Threading;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Layout;

namespace Raffica.Write.Viewing;

/// <summary>
/// Owns the background layout of a tab: the layout of the pages (the one the tab opened with, page 1 laid out) and, once the writing view asks for it, the layout of
/// the strips. <see cref="Start"/> goes on after page 1 is on screen: when the tab holds only a prefix of the file the whole file is read on a pool thread and the
/// layout takes the whole model (<see cref="DocumentLayout.ReplaceModel"/>; when the layout refuses it, a new layout over the whole model takes its place and the
/// tab and the view follow), then the remaining pages are laid out on a pool thread. Progress reaches the UI thread through the dispatcher the runner captured when
/// it was made (never read on a pool thread) at most every <see cref="ProgressIntervalMs"/>, plus once at the end. Disposing cancels both layouts at the end of the
/// page they are on and never waits; nothing a layout publishes is read after that. The prefix layout's final section is not the file's: only the layout reads it.
/// </summary>
internal sealed class LayoutRunner : IDisposable
{
    /// <summary>The least time between two <see cref="Changed"/> events caused by progress.</summary>
    public const int ProgressIntervalMs = 100;

    /// <summary>The width of the text column of the writing view, in points (about 85 characters of Carlito 11 pt).</summary>
    public const float FlowTextWidth = 520f;

    private readonly WriteTab _tab;
    private readonly Dispatcher _ui;
    private readonly CancellationTokenSource _cancellation;
    private readonly object _gate = new();
    private readonly Progress _progress;
    private DocumentLayout _page;
    private DocumentLayout? _flow;
    private bool _started;
    private bool _flowRequested;
    private bool _flowStarted;
    private bool _fullReady;
    private int _disposed;
    private int _postPending;
    private int _restarts;
    private long _lastRaise;
    private Exception? _fault;
    private Task _pageTask = Task.CompletedTask;
    private Task _flowTask = Task.CompletedTask;
    private readonly TaskCompletionSource _fullModel = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="tab">The tab; made on the UI thread (the runner keeps that thread's dispatcher).</param>
    public LayoutRunner(WriteTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tab = tab;
        _ui = Dispatcher.UIThread;
        _page = tab.Layout;
        _fullReady = !tab.Docx.IsPrefix;
        if (_fullReady)
        {
            _fullModel.TrySetResult();
        }

        _progress = new Progress(this);
        try
        {
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(tab.Token);
        }
        catch (ObjectDisposedException)
        {
            // The tab is gone already: a runner that is cancelled from the start.
            _cancellation = new CancellationTokenSource();
            _cancellation.Cancel();
        }
    }

    /// <summary>Raised on the UI thread when a layout published pages, was replaced or finished (progress at most every <see cref="ProgressIntervalMs"/>).</summary>
    public event Action? Changed;

    /// <summary>The layout of the pages: the one the tab opened with, or the one that took its place when the prefix layout could not take the whole model.</summary>
    public DocumentLayout PageLayout => Volatile.Read(ref _page);

    /// <summary>The layout of the strips of the writing view; null until <see cref="EnsureFlow"/> was asked for and its first strip is laid out.</summary>
    public DocumentLayout? FlowLayout => Volatile.Read(ref _flow);

    /// <summary>The layout of a mode, or null (the strips before their first one exists).</summary>
    public DocumentLayout? LayoutFor(LayoutMode mode) => mode == LayoutMode.Flow ? FlowLayout : PageLayout;

    /// <summary>True when <see cref="Dispose"/> was called.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>True while the whole model is the one the layouts work on (always for a file opened whole).</summary>
    public bool FullModelReady
    {
        get
        {
            lock (_gate)
            {
                return _fullReady;
            }
        }
    }

    /// <summary>Completes when the layouts work on the whole model (at once for a file opened whole), or when the runner ended without it (a failed full open, a disposed runner). Never faults.</summary>
    public Task FullModelTask => _fullModel.Task;

    /// <summary>How many times the prefix layout refused the whole model and a new layout over it took its place (tests: a navigation must never cause one).</summary>
    internal int RestartCount => Volatile.Read(ref _restarts);

    /// <summary>What ended a layout with an error (a programming error of the layout, never a document); null when nothing did. Cancellation is no error.</summary>
    public Exception? Fault => Volatile.Read(ref _fault);

    /// <summary>The background work of the page layout (ends when it is complete or cancelled; never faults). Completed before <see cref="Start"/>.</summary>
    public Task PageTask => Volatile.Read(ref _pageTask);

    /// <summary>The background work of the strip layout (completed until <see cref="EnsureFlow"/> started it).</summary>
    public Task FlowTask => Volatile.Read(ref _flowTask);

    /// <summary>Raised once on the UI thread when a layout ended with an error (the full open of a body that is damaged after the prefix, or a bug of the layout); never after the runner was disposed. Read <see cref="Fault"/>.</summary>
    public event Action? Faulted;

    /// <summary>True once a layout ended with an error: what is laid out stays, nothing more will come.</summary>
    public bool IsFaulted => Volatile.Read(ref _fault) is not null;

    /// <summary>Test seam: runs just before the whole file is opened; a throw is a failed full open.</summary>
    internal Action? BeforeFullOpen { get; set; }

    /// <summary>Test seam: runs just before the prefix layout takes the whole model; a throw makes it a refused one.</summary>
    internal Action? BeforeReplaceModel { get; set; }

    /// <summary>Starts the full open and the layout of the remaining pages (once; later calls do nothing).</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || IsDisposed)
            {
                return;
            }

            _started = true;
        }

        Volatile.Write(ref _pageTask, Task.Run(RunPagesAsync));
    }

    /// <summary>Asks for the strip layout (the writing view); it is made once the whole model is there and kept. Starts the runner when it was not started.</summary>
    public void EnsureFlow()
    {
        bool ready;
        lock (_gate)
        {
            if (_flowRequested || IsDisposed)
            {
                return;
            }

            _flowRequested = true;
            ready = _fullReady;
        }

        Start();
        if (ready)
        {
            StartFlow();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _fullModel.TrySetResult();

        try
        {
            _cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // A callback of the token threw; the runner is going away anyway.
        }

        // The source is let go once nothing can use its token any more; nobody waits for it here.
        _ = Task.WhenAll(PageTask, FlowTask).ContinueWith(_ => _cancellation.Dispose(), TaskScheduler.Default);
    }

    private LayoutOptions Options(LayoutMode mode) => new(mode, FlowTextWidth, _tab.Images.Read);

    private async Task RunPagesAsync()
    {
        try
        {
            var token = _cancellation.Token;
            var layout = PageLayout;
            if (_tab.Docx.IsPrefix)
            {
                token.ThrowIfCancellationRequested();
                BeforeFullOpen?.Invoke();
                var full = DocxFile.Open(_tab.Docx.Package);

                // The tab owns the whole file from here on (and lets go of it if it was closed meanwhile).
                _tab.CompleteOpen(full);
                token.ThrowIfCancellationRequested();
                try
                {
                    BeforeReplaceModel?.Invoke();
                    layout.ReplaceModel(full.Model);
                }
                catch (InvalidOperationException)
                {
                    // Page 1 of the prefix is not page 1 of the whole file: start over on the whole model; the prefix layout is dropped and nothing is told.
                    Interlocked.Increment(ref _restarts);
                    layout = DocumentLayout.Create(full.Model, _tab.Fonts, Options(LayoutMode.Pages));
                    layout.LayOutFirstPage();
                    Volatile.Write(ref _page, layout);
                    _tab.ReplaceLayout(layout);
                    PostChanged();
                }

                bool startFlow;
                lock (_gate)
                {
                    _fullReady = true;
                    startFlow = _flowRequested;
                }

                _fullModel.TrySetResult();

                if (startFlow)
                {
                    StartFlow();
                }
            }

            await layout.LayOutRestAsync(_progress, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException && IsDisposed)
        {
            // The tab closed.
        }
        catch (OperationCanceledException)
        {
            // The tab closed.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(ex);
        }
        finally
        {
            PostChanged();
        }
    }

    // A layout ended with an error: remember the first one and tell the UI thread once (nothing is told for a runner that was disposed).
    private void Fail(Exception ex)
    {
        _fullModel.TrySetResult();
        if (Interlocked.CompareExchange(ref _fault, ex, null) is null && !IsDisposed)
        {
            _ui.Post(RaiseFaulted, DispatcherPriority.Normal);
        }
    }

    private void RaiseFaulted()
    {
        if (!IsDisposed)
        {
            Faulted?.Invoke();
        }
    }

    private void StartFlow()
    {
        lock (_gate)
        {
            if (_flowStarted || IsDisposed)
            {
                return;
            }

            _flowStarted = true;
        }

        Volatile.Write(ref _flowTask, Task.Run(RunFlowAsync));
    }

    private async Task RunFlowAsync()
    {
        try
        {
            var token = _cancellation.Token;
            token.ThrowIfCancellationRequested();
            var layout = DocumentLayout.Create(_tab.Docx.Model, _tab.Fonts, Options(LayoutMode.Flow));
            layout.LayOutFirstPage();
            Volatile.Write(ref _flow, layout);
            PostChanged();
            await layout.LayOutRestAsync(_progress, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException && IsDisposed)
        {
            // The tab closed.
        }
        catch (OperationCanceledException)
        {
            // The tab closed.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(ex);
        }
        finally
        {
            PostChanged();
        }
    }

    // Always reaches the UI thread; used for the events that must not be lost (a layout replaced, the first strip, the end).
    private void PostChanged()
    {
        if (IsDisposed)
        {
            return;
        }

        Interlocked.Exchange(ref _lastRaise, Environment.TickCount64);
        _ui.Post(RaiseChanged, DispatcherPriority.Normal);
    }

    private void RaiseChanged()
    {
        Interlocked.Exchange(ref _postPending, 0);
        if (!IsDisposed)
        {
            Changed?.Invoke();
        }
    }

    // Called on a pool thread by the layout after each page: at most one event per interval, and only one post at a time.
    private void OnProgress()
    {
        if (IsDisposed)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastRaise) < ProgressIntervalMs)
        {
            return;
        }

        if (Interlocked.Exchange(ref _postPending, 1) == 0)
        {
            Interlocked.Exchange(ref _lastRaise, now);
            _ui.Post(RaiseChanged, DispatcherPriority.Normal);
        }
    }

    // Not Progress<T>: that class posts every report to the context it captured, which is what the throttle is there to avoid.
    private sealed class Progress(LayoutRunner owner) : IProgress<int>
    {
        public void Report(int value) => owner.OnProgress();
    }
}

/// <summary>Answers the block questions of <see cref="PageScroller"/> from a layout (only about pages that are laid out).</summary>
internal sealed class LayoutBlockMap(DocumentLayout layout) : IBlockMap
{
    public (int Page, float Y)? Locate(int block) => layout.Locate(block);

    public int BlockAt(int page, float y) => (uint)page < (uint)layout.LaidOutPages ? layout.BlockAt(page, y) : -1;
}
