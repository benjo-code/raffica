using Avalonia.Threading;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
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

    // The edit gate (all under _gate): a lease pauses the layout loops at a page boundary. _segments counts the pool steps that touch a layout or the model
    // (a run of LayOutRestAsync, the making of the first strip); a lease waits for it to be 0. _loopCts ends the running segments and is renewed on release.
    private CancellationTokenSource _loopCts = null!;
    private TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _leased;
    private bool _paused;
    private int _leaseOwner;
    private int _segments;
    private bool _pageAlive;
    private bool _flowAlive;
    private bool _pageStale;
    private int _flowGeneration;

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

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
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
            _pageAlive = true;
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

        // Loops parked behind a lease wake up and end; a thread waiting for a lease or for the loops to stop wakes up and sees the end.
        TaskCompletionSource resume;
        lock (_gate)
        {
            resume = _resume;
            Monitor.PulseAll(_gate);
        }

        resume.TrySetResult();

        // The sources are let go once nothing can use their tokens any more; nobody waits for it here.
        _ = Task.WhenAll(PageTask, FlowTask).ContinueWith(
            _ =>
            {
                lock (_gate)
                {
                    _loopCts.Dispose();
                }

                _cancellation.Dispose();
            },
            TaskScheduler.Default);
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

            await LoopAsync(flow: false, 0).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException && IsDisposed)
        {
            ClearAlive(flow: false, 0);
        }
        catch (OperationCanceledException)
        {
            ClearAlive(flow: false, 0);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ClearAlive(flow: false, 0);
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
        int generation;
        lock (_gate)
        {
            if (_flowStarted || IsDisposed)
            {
                return;
            }

            _flowStarted = true;
            _flowAlive = true;
            generation = _flowGeneration;
        }

        Volatile.Write(ref _flowTask, Task.Run(() => RunFlowAsync(generation)));
    }

    private async Task RunFlowAsync(int generation)
    {
        try
        {
            // Making the first strip reads the model: it is a segment, so a lease keeps it waiting.
            if (await EnterSegmentAsync().ConfigureAwait(false) is null)
            {
                return;
            }

            try
            {
                lock (_gate)
                {
                    if (generation != _flowGeneration)
                    {
                        return;
                    }
                }

                var layout = DocumentLayout.Create(_tab.Docx.Model, _tab.Fonts, Options(LayoutMode.Flow));
                layout.LayOutFirstPage();
                Volatile.Write(ref _flow, layout);
            }
            finally
            {
                ExitSegment();
            }

            PostChanged();
            await LoopAsync(flow: true, generation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException && IsDisposed)
        {
            ClearAlive(flow: true, generation);
        }
        catch (OperationCanceledException)
        {
            ClearAlive(flow: true, generation);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ClearAlive(flow: true, generation);
            Fail(ex);
        }
        finally
        {
            PostChanged();
        }
    }

    /// <summary>
    /// Stops the background layout at the end of the page each loop is on and returns once no pool step touches a layout or the model any more; the caller
    /// (the UI thread) may then change the model and bring the layouts up to date with <see cref="DocumentLayout.ApplyEdit"/>. Disposing the lease lets the
    /// loops go on from where the layouts stand and restarts one that had ended on a layout that an edit made incomplete. The wait is the rest of one page
    /// (a few milliseconds); no loop needs the UI thread to finish it (progress is posted, never invoked).
    /// One lease at a time: a request from another thread waits for the lease to be disposed; a request from the thread that holds it would wait for itself and
    /// throws <see cref="InvalidOperationException"/> instead. The lease does not wait for the full open of a prefix (the model is not ready for edits before
    /// <see cref="FullModelReady"/>, and <see cref="EditSession"/> enforces that); the layout steps that start while it is held wait for it.
    /// </summary>
    /// <exception cref="InvalidOperationException">This thread holds the lease already.</exception>
    /// <exception cref="ObjectDisposedException">The runner is disposed.</exception>
    public EditLease PauseForEdit()
    {
        var me = Environment.CurrentManagedThreadId;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_leased && _leaseOwner == me)
            {
                throw new InvalidOperationException("This thread holds the edit lease already; a second request would wait for itself.");
            }

            var deadline = LeaseWaitTimeout == Timeout.InfiniteTimeSpan ? long.MaxValue : Environment.TickCount64 + (long)LeaseWaitTimeout.TotalMilliseconds;
            while (_leased)
            {
                if (!Monitor.Wait(_gate, Remaining(deadline)))
                {
                    throw new TimeoutException("The edit lease was not free in time.");
                }

                ObjectDisposedException.ThrowIf(IsDisposed, this);
            }

            _leased = true;
            _leaseOwner = me;
            _paused = true;
            try
            {
                _loopCts.Cancel();
            }
            catch (AggregateException)
            {
                // A callback of the token threw; the loops stop all the same.
            }

            while (_segments > 0)
            {
                if (!Monitor.Wait(_gate, Remaining(deadline)))
                {
                    // The loops did not stop: give the gate back as it was and say so.
                    ReleaseLease();
                    throw new TimeoutException("The layout did not stop at a page boundary in time.");
                }
            }
        }

        return new EditLease(this);
    }

    /// <summary>How long <see cref="PauseForEdit"/> waits for another lease and for the loops to stop before it throws <see cref="TimeoutException"/>; infinite by default (tests bound it so a gate that stands still fails).</summary>
    internal TimeSpan LeaseWaitTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>An edit could not be brought into a layout: the runner is faulted like after a failed layout step (<see cref="Fault"/>, <see cref="Faulted"/>), so nothing is edited or laid out any more.</summary>
    internal void FailEdit(Exception ex) => Fail(ex);

    private static int Remaining(long deadline) => deadline == long.MaxValue ? Timeout.Infinite : (int)Math.Max(0, deadline - Environment.TickCount64);

    /// <summary>True while a lease is held (by any thread).</summary>
    internal bool IsLeased
    {
        get
        {
            lock (_gate)
            {
                return _leased;
            }
        }
    }

    internal void ReleaseLease()
    {
        TaskCompletionSource resume;
        lock (_gate)
        {
            if (!_leased)
            {
                return;
            }

            _leased = false;
            _paused = false;
            if (!IsDisposed)
            {
                // No segment runs (the lease waited for that), so nothing holds the old source.
                _loopCts.Dispose();
                _loopCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);

                // A loop that ended because its layout was complete is not alive: an edit may have made the layout incomplete again.
                if (_started && !_pageAlive && _fault is null && !_pageStale && !_page.IsComplete)
                {
                    _pageAlive = true;
                    Volatile.Write(ref _pageTask, Task.Run(() => RestartLoopAsync(flow: false, 0)));
                }

                if (_flow is { IsComplete: false } && !_flowAlive && _fault is null)
                {
                    _flowAlive = true;
                    var generation = _flowGeneration;
                    Volatile.Write(ref _flowTask, Task.Run(() => RestartLoopAsync(flow: true, generation)));
                }
            }

            resume = _resume;
            _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Monitor.PulseAll(_gate);
        }

        resume.TrySetResult();
    }

    /// <summary>
    /// The writing view's layout is dropped (an edit followed the pages layout): it ends its loop and <see cref="FlowLayout"/> is null until
    /// <see cref="EnsureFlow"/> makes it again from the edited model. Needs the lease of the calling thread.
    /// </summary>
    public void DropFlowLayout()
    {
        lock (_gate)
        {
            RequireLease();
            if (_flow is null && !_flowRequested)
            {
                return;
            }

            _flowGeneration++;
            _flowStarted = false;
            _flowRequested = false;
            _flowAlive = false;
            Volatile.Write(ref _flow, null);
            Volatile.Write(ref _flowTask, Task.CompletedTask);
        }
    }

    /// <summary>
    /// The pages layout does not follow an edit made in the writing view (the reverse of <see cref="DropFlowLayout"/>): it is marked stale, its loop ends and
    /// <see cref="PageLayout"/> is a layout of the model before the edit until <see cref="RebuildPageLayoutIfStale"/> made a new one. Needs the lease of the calling thread.
    /// </summary>
    public void DropPageLayout()
    {
        lock (_gate)
        {
            RequireLease();
            _pageStale = true;
        }
    }

    /// <summary>True after <see cref="DropPageLayout"/> until <see cref="RebuildPageLayoutIfStale"/> ran.</summary>
    public bool PageLayoutIsStale => Volatile.Read(ref _pageStale);

    /// <summary>
    /// Makes the pages layout again over the current model (page 1 laid out, the rest in the background) when <see cref="DropPageLayout"/> made it stale; the tab
    /// follows. UI thread; takes the lease itself. Returns whether a new layout was made.
    /// </summary>
    public bool RebuildPageLayoutIfStale()
    {
        if (!PageLayoutIsStale || IsDisposed)
        {
            return false;
        }

        using var lease = PauseForEdit();
        var layout = DocumentLayout.Create(_tab.Docx.Model, _tab.Fonts, Options(LayoutMode.Pages));
        layout.LayOutFirstPage();
        lock (_gate)
        {
            Volatile.Write(ref _page, layout);
            _tab.ReplaceLayout(layout);
            _pageStale = false;
        }

        PostChanged();
        return true;
    }

    private void RequireLease()
    {
        if (!_leased || _leaseOwner != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The layouts change only while this thread holds the edit lease.");
        }
    }

    private void ClearAlive(bool flow, int generation)
    {
        lock (_gate)
        {
            if (flow)
            {
                if (generation == _flowGeneration)
                {
                    _flowAlive = false;
                }
            }
            else
            {
                _pageAlive = false;
            }
        }
    }

    // Waits while a lease is held; null when the runner ended meanwhile. The caller must call ExitSegment.
    private async Task<CancellationToken?> EnterSegmentAsync()
    {
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                if (IsDisposed)
                {
                    return null;
                }

                if (!_paused)
                {
                    _segments++;
                    return _loopCts.Token;
                }

                wait = _resume.Task;
            }

            await wait.ConfigureAwait(false);
        }
    }

    private void ExitSegment()
    {
        lock (_gate)
        {
            _segments--;
            Monitor.PulseAll(_gate);
        }
    }

    private async Task RestartLoopAsync(bool flow, int generation)
    {
        try
        {
            await LoopAsync(flow, generation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException && IsDisposed)
        {
            ClearAlive(flow, generation);
        }
        catch (OperationCanceledException)
        {
            ClearAlive(flow, generation);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ClearAlive(flow, generation);
            Fail(ex);
        }
        finally
        {
            PostChanged();
        }
    }

    // Lays out the rest of a layout in segments: each is a LayOutRestAsync run that a lease ends at a page boundary; the loop then waits for the lease to end and
    // goes on. It ends when the layout is complete, was dropped or went stale, or the runner ended. The decision to end and the alive flag change under one lock,
    // so a lease released later sees a dead loop and restarts it when an edit made the layout incomplete.
    private async Task LoopAsync(bool flow, int generation)
    {
        try
        {
            while (true)
            {
                Task? wait = null;
                DocumentLayout? layout = null;
                CancellationToken token = default;
                lock (_gate)
                {
                    var current = flow ? _flow : _page;
                    var gone = IsDisposed || current is null || (flow ? generation != _flowGeneration : _pageStale);
                    if (gone || (!_paused && current!.IsComplete))
                    {
                        if (!flow)
                        {
                            _pageAlive = false;
                        }
                        else if (generation == _flowGeneration)
                        {
                            _flowAlive = false;
                        }

                        return;
                    }

                    if (_paused)
                    {
                        wait = _resume.Task;
                    }
                    else
                    {
                        _segments++;
                        token = _loopCts.Token;
                        layout = current;
                    }
                }

                if (wait is not null)
                {
                    await wait.ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await layout!.LayOutRestAsync(_progress, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!_cancellation.IsCancellationRequested)
                {
                    // Stopped by a lease at the end of a page; the next round waits for it.
                }
                finally
                {
                    ExitSegment();
                }
            }
        }
        catch
        {
            ClearAlive(flow, generation);
            throw;
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
