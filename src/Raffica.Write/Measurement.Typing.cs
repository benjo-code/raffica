using System.Diagnostics;
using System.Globalization;
using Avalonia.Input;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write;

/// <summary>
/// The typing run (<c>--measure-typing</c>) of <see cref="Measurement"/>. Once the whole model is there and the layout is complete, it types at eight places
/// of the document (<see cref="TypingPlaces"/>), first in the page view and then in the writing view, one letter per key. Every letter is a
/// <see cref="TextInputEventArgs"/> raised on the page view, so it takes the path of the keyboard: <c>PageView.OnTextInput</c>, <c>EditSession.Apply</c>, the editor and the layout.
/// The key is raised inside an animation frame callback and the next key follows only after the frame that shows this one was drawn, so one key never overlaps the frame of the one before.
/// The frame that shows a key is the first render pass after it, and its pages are the ones that pass handed to the render thread (the page with the caret and any page visible with it);
/// the frame is drawn when the render thread has drawn the last of them. Two times are taken from the key event (a monotonic clock):
/// <list type="bullet">
/// <item><c>frame</c>: to the end of that draw. A key raised at the start of a frame is shown in the next frame, so this number holds the idle wait for the next frame of the display
/// (up to one frame period, 16.7 ms at 60 Hz), which no code of the app can shorten: it is never below about one frame period and is reported for the record only.</item>
/// <item>the judged time (<c>median-ms</c>, <c>p95-ms</c>, <c>max-ms</c>): the work the key causes, that is the time of the key handler (from the raise to the return of the raise: the
/// editor, the layout, the caret and the view) plus the duration of the render pass that shows it (<c>PageView.Render</c> on the UI thread) plus the sum of the durations of the page draws
/// of that frame on the render thread. It leaves out the idle waits (for the next frame, and the render thread waiting for the display), the layout pass of the view and the compositor, which
/// no key-dependent code of the app runs in.</item>
/// </list>
/// Rows: <c>typing case=&lt;case&gt; view=&lt;page|writing&gt; n=&lt;keys&gt; median-ms=… p95-ms=… max-ms=… handler-median-ms=… frame-median-ms=… frame-p95-ms=… render-median-ms=… render-p95-ms=… draw-median-ms=… draw-p95-ms=…</c> for every case and view (render is the UI render pass, draw the render-thread draws of the frame: the split of the judged time, recorded and never judged), and
/// <c>typing-first when=&lt;after-open|after-switch&gt; view=… ms=…</c> (the judged time) for the first key after the open and the first key after the switch to the writing view (no limit).
/// The median of an even count is the mean of the two middle values, the 95th percentile is the nearest rank. A run with no frame within 5 s after a key, or without a document,
/// ends with exit code 1.
/// </summary>
internal sealed partial class Measurement
{
    /// <summary>The keys typed per case and view.</summary>
    public const int TypingKeys = 100;

    /// <summary>The keys typed by <c>--measure-typing-background</c> (when the count is the default one).</summary>
    public const int BackgroundKeys = 50;

    private const int Seed = 4242;
    private static readonly TimeSpan s_typingTimeout = TimeSpan.FromSeconds(240);
    private static readonly TimeSpan s_noFrameAfterKey = TimeSpan.FromSeconds(5);

    private sealed record Job(string Case, WriteView View, string? FirstWhen, TypingPlace Place, int Keys, string? Format = null);

    private static readonly TimeSpan s_notReady = TimeSpan.FromSeconds(60);

    private readonly int _typingKeys;
    private readonly bool _background;
    private readonly bool _formatRun;
    private readonly bool _paneRun;
    private readonly bool _snapshotRun;
    private readonly List<double> _snapshotLease = [];
    private readonly List<double> _snapshotTotal = [];
    private readonly List<double> _snapshotWritten = [];
    private int _snapshotBlocks;
    private int[] _gcAtStart = [];
    private TimeSpan _gcPauseAtStart;
    private CancellationTokenSource? _snapshotCancel;
    private string? _snapshotFailure;
    private readonly TaskCompletionSource _snapshotFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // How long the end of a typing-snapshot run waits for the first copy when none has been written yet (the first copy writes the whole body and can outlast a short run).
    private static readonly TimeSpan s_firstCopyWait = TimeSpan.FromSeconds(60);
    private int _noChange;
    private TextRange? _range;
    private long _waitStarted;
    private readonly List<double> _samples = [];
    private readonly List<double> _handlerSamples = [];
    private readonly List<double> _renderSamples = [];
    private readonly List<double> _drawSamples = [];
    private readonly EditPhases _phases = new();
    private readonly List<double>[] _phaseSamples = Enumerable.Range(0, EditPhases.Names.Length + 1).Select(_ => new List<double>()).ToArray();
    private readonly List<double> _frameSamples = [];
    private readonly List<double> _leaseSamples = [];
    private readonly List<double> _incompleteSamples = [];
    private bool _keyIncomplete;
    private List<Job>? _jobs;
    private Job? _job;
    private int _jobIndex;
    private int _keyInJob;
    private int _keySequence;
    private Random _random = new(Seed);
    private int _placeStage;
    private bool _awaiting;
    private long _keyAt;
    private long _handlerEnd;
    private long _renderStart;

    // The probes of the page view: the first render pass after the key (UI thread) and the page draws (render thread).
    private bool _renderSeen;
    private int _expectedDraws;
    private int _drawsAtRender;
    private long _renderStamp;
    private int _draws;
    private long _drawTicks;
    private long _drawTicksAtRender;
    private long _lastDrawStamp;
    private PageView? _probed;

    /// <summary>The line <c>typing case=… view=… n=… median-ms=… p95-ms=… max-ms=…</c> for the times of one case in milliseconds.</summary>
    public static string TypingLine(string name, string view, IReadOnlyList<double> milliseconds, IReadOnlyList<double>? handlerMilliseconds = null, IReadOnlyList<double>? frameMilliseconds = null, string kind = "typing", IReadOnlyList<double>? renderMilliseconds = null, IReadOnlyList<double>? drawMilliseconds = null, IReadOnlyList<IReadOnlyList<double>>? phaseMilliseconds = null)
    {
        var sorted = milliseconds.Order().ToArray();
        var line = string.Create(CultureInfo.InvariantCulture, $"{kind} case={name} view={view} n={sorted.Length} median-ms={Median(sorted):0.00} p95-ms={Percentile95(sorted):0.00} max-ms={sorted[^1]:0.00}");
        if (handlerMilliseconds is not null && frameMilliseconds is not null)
        {
            var handler = handlerMilliseconds.Order().ToArray();
            var frame = frameMilliseconds.Order().ToArray();
            line += string.Create(CultureInfo.InvariantCulture, $" handler-median-ms={Median(handler):0.00} frame-median-ms={Median(frame):0.00} frame-p95-ms={Percentile95(frame):0.00}");
            if (renderMilliseconds is not null && drawMilliseconds is not null)
            {
                var render = renderMilliseconds.Order().ToArray();
                var draw = drawMilliseconds.Order().ToArray();
                line += string.Create(CultureInfo.InvariantCulture, $" render-median-ms={Median(render):0.00} render-p95-ms={Percentile95(render):0.00} draw-median-ms={Median(draw):0.00} draw-p95-ms={Percentile95(draw):0.00}");
                if (phaseMilliseconds is not null)
                {
                    // Where the handler's time went (the phases of EditSession.Apply, then what is left of the handler): recorded, never judged.
                    for (var i = 0; i < phaseMilliseconds.Count; i++)
                    {
                        var phase = phaseMilliseconds[i].Order().ToArray();
                        var phaseName = i < EditPhases.Names.Length ? EditPhases.Names[i] : "rest";
                        line += string.Create(CultureInfo.InvariantCulture, $" ph-{phaseName}-median-ms={Median(phase):0.00} ph-{phaseName}-p95-ms={Percentile95(phase):0.00}");
                    }
                }
            }
        }

        return line;
    }

    /// <summary>
    /// The line <c>typing-background n=... incomplete=... median-ms=... p95-ms=... max-ms=... lease-median-ms=... lease-p95-ms=... lease-max-ms=... incomplete-median-ms=... incomplete-max-ms=...</c>:
    /// the keys typed while the layout of the rest was still running (the judged time of each, which includes the wait for the edit lease), the lease wait alone, and the keys among them that
    /// started with the layout incomplete (<c>incomplete</c> counts them; the last two fields are zero for none). Never judged.
    /// </summary>
    public static string TypingBackgroundLine(IReadOnlyList<double> milliseconds, IReadOnlyList<double> leaseMilliseconds, IReadOnlyList<double> incompleteMilliseconds)
    {
        var all = milliseconds.Order().ToArray();
        var lease = leaseMilliseconds.Order().ToArray();
        var incomplete = incompleteMilliseconds.Order().ToArray();
        var incompleteMedian = incomplete.Length == 0 ? 0 : Median(incomplete);
        var incompleteMax = incomplete.Length == 0 ? 0 : incomplete[^1];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"typing-background n={all.Length} incomplete={incomplete.Length} median-ms={Median(all):0.00} p95-ms={Percentile95(all):0.00} max-ms={all[^1]:0.00} lease-median-ms={Median(lease):0.00} lease-p95-ms={Percentile95(lease):0.00} lease-max-ms={lease[^1]:0.00} incomplete-median-ms={incompleteMedian:0.00} incomplete-max-ms={incompleteMax:0.00}");
    }

    /// <summary>
    /// The line <c>snapshot n=... lease-median-ms=... lease-max-ms=... total-median-ms=... total-max-ms=... blocks=... blocks-written-median=... blocks-written-max=... whole-passes=...</c> of the typing-snapshot run: how long the model-read lease was held and how long the whole
    /// snapshot took, over every copy written during the run, and how many top-level blocks a copy wrote again (<c>whole-passes</c>: the copies that wrote more than half of them). Recorded, never judged.
    /// </summary>
    public static string SnapshotLine(IReadOnlyList<double> leaseMilliseconds, IReadOnlyList<double> totalMilliseconds, IReadOnlyList<double> blocksWritten, int blocks)
    {
        var lease = leaseMilliseconds.Order().ToArray();
        var total = totalMilliseconds.Order().ToArray();
        var written = blocksWritten.Order().ToArray();
        var whole = blocksWritten.Count(w => w * 2 > blocks);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"snapshot n={total.Length} lease-median-ms={Median(lease):0.00} lease-max-ms={lease[^1]:0.00} total-median-ms={Median(total):0.00} total-max-ms={total[^1]:0.00} blocks={blocks} blocks-written-median={Median(written):0} blocks-written-max={written[^1]:0} whole-passes={whole}");
    }

    // The end of a typing-snapshot run: when no copy has finished yet, a copy that is still being written is waited for (on its completion signal, not a sleep), so a short run is not judged
    // "no copy" only because the first copy, which writes the whole body, was slower than the keys.
    private async Task EndSnapshotRunAsync()
    {
        // Started without an await (the typing frame returns at once), so nothing may escape it: a failure ends the run with the type of the exception, like the loop does.
        try
        {
            if (_snapshotFailure is null && _snapshotLease.Count == 0)
            {
                // The wait ends with the first copy (or failure); the delay is stopped then, so no timer outlives the run.
                using var stop = new CancellationTokenSource();
                var delay = Task.Delay(s_firstCopyWait, stop.Token);
                await Task.WhenAny(_snapshotFirst.Task, delay);
                await stop.CancelAsync();
            }

            if (_snapshotFailure is { } failure)
            {
                FailTyping(failure);
                return;
            }

            if (_snapshotLease.Count == 0)
            {
                FailTyping("no recovery snapshot was written during the run");
                return;
            }

            Write(SnapshotLine(_snapshotLease, _snapshotTotal, _snapshotWritten, _snapshotBlocks) + string.Create(CultureInfo.InvariantCulture, $" gc0={GC.CollectionCount(0) - _gcAtStart[0]} gc1={GC.CollectionCount(1) - _gcAtStart[1]} gc2={GC.CollectionCount(2) - _gcAtStart[2]} gc-pause-ms={(GC.GetTotalPauseDuration() - _gcPauseAtStart).TotalMilliseconds:0}"));
            Finish(Success);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Only the type: the message of an exception may name a path or a document value.
            FailTyping("the end of the recovery snapshot run failed: " + ex.GetType().Name);
        }
    }

    // The snapshot loop of the typing-snapshot run: copies of the tab are written back to back into the recovery folder that follows the settings file, as the autosave would at its worst
    // (the window's own timer is off in a measurement run). It runs on the UI thread, the lease is taken by the snapshot on its pool thread, and it ends with the run.
    private void StartSnapshotLoop(WriteTab tab)
    {
        _gcAtStart = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        _gcPauseAtStart = GC.GetTotalPauseDuration();
        _snapshotCancel = new CancellationTokenSource();
        var token = _snapshotCancel.Token;
        var store = new Recovery.RecoveryStore(_window.RecoveryFolder);
        var subject = new Recovery.WriteTabRecoverySubject(tab);
        _ = LoopAsync();

        async Task LoopAsync()
        {
            try
            {
                while (!_finished && !token.IsCancellationRequested)
                {
                    switch (await Recovery.RecoverySnapshot.WriteAsync(subject, store, token))
                    {
                        case Recovery.SnapshotResult.Written written:
                            _snapshotLease.Add(written.LeaseHeld.TotalMilliseconds);
                            _snapshotTotal.Add(written.Total.TotalMilliseconds);
                            _snapshotWritten.Add(tab.Docx.LastSnapshotPass.Written);
                            _snapshotBlocks = tab.Docx.Model.Body.Blocks.Count;
                            _snapshotFirst.TrySetResult();
                            break;
                        case Recovery.SnapshotResult.Failed failed:
                            // Only the type of the error: the message may name a path.
                            _snapshotFailure = "a recovery snapshot failed: " + failed.Error.GetType().Name;
                            _snapshotFirst.TrySetResult();
                            return;
                        default:
                            await Task.Delay(5, token);
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The run ended.
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Only the type: the message of an exception may name a path or a document value.
                _snapshotFailure = "the recovery snapshot loop failed: " + ex.GetType().Name;
                _snapshotFirst.TrySetResult();
            }
        }
    }

    private static double Median(double[] sorted)
    {
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static double Percentile95(double[] sorted) => sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1];

    /// <summary>The line <c>typing-first when=… view=… ms=…</c> for the first key after the open or after the switch.</summary>
    public static string TypingFirstLine(string when, string view, double milliseconds) =>
        string.Create(CultureInfo.InvariantCulture, $"typing-first when={when} view={view} ms={milliseconds:0.00}");

    private static string ViewName(WriteView view) => view == WriteView.Page ? "page" : "writing";

    private void StartTyping() => _window.RequestAnimationFrame(_ => TypingFrame());

    private void NextTypingFrame() => _window.RequestAnimationFrame(_ => TypingFrame());

    private void FailTyping(string message)
    {
        _error.WriteLine("error: " + message);
        Finish(Failed);
    }

    private void Probe(PageView view)
    {
        if (ReferenceEquals(_probed, view))
        {
            return;
        }

        if (_probed is { } old)
        {
            old.RenderProbe = null;
            old.DrawProbe = null;
        }

        _probed = view;
        view.RenderProbe = handed =>
        {
            if (!_awaiting || _renderSeen)
            {
                return;
            }

            if (handed < 0)
            {
                // The start of the first render pass after the key.
                if (_renderStart == 0)
                {
                    _renderStart = Stopwatch.GetTimestamp();
                }

                return;
            }

            _renderSeen = true;
            _expectedDraws = handed;
            _drawsAtRender = Volatile.Read(ref _draws);
            _drawTicksAtRender = Volatile.Read(ref _drawTicks);
            _renderStamp = Stopwatch.GetTimestamp();
        };
        view.DrawProbe = (start, end) =>
        {
            Interlocked.Add(ref _drawTicks, end - start);
            Volatile.Write(ref _lastDrawStamp, end);
            Interlocked.Increment(ref _draws);
        };
    }

    private void ReleaseProbe()
    {
        if (_probed is { } view)
        {
            view.RenderProbe = null;
            view.DrawProbe = null;
            _probed = null;
        }
    }

    // One animation frame of the typing run.
    private void TypingFrame()
    {
        if (_finished)
        {
            return;
        }

        if (_window.ActiveView is not { } view)
        {
            FailTyping("no document is shown, nothing to type into");
            return;
        }

        if (view.Runner.IsFaulted)
        {
            FailTyping("the layout failed");
            return;
        }

        if (_jobs is null)
        {
            // The background run starts as soon as the whole model is there, with the layout of the rest still running; the others wait for the complete layout.
            if (!view.Runner.FullModelReady || (!_background && !view.IsLayoutComplete))
            {
                if (_waitStarted == 0)
                {
                    _waitStarted = Stopwatch.GetTimestamp();
                }

                if (Stopwatch.GetElapsedTime(_waitStarted) > s_notReady)
                {
                    FailTyping("the document was not ready to be edited within 60 s");
                    return;
                }

                NextTypingFrame();
                return;
            }

            _jobs = _background ? PlanBackground(view) : Plan(view);
            if (_jobs.Count == 0)
            {
                FailTyping("the document has no place to type at");
                return;
            }

            Probe(view);
            if (_snapshotRun)
            {
                StartSnapshotLoop(view.Tab);
            }
        }

        if (_awaiting)
        {
            var drawn = Volatile.Read(ref _draws) >= _drawsAtRender + _expectedDraws;
            if (!_renderSeen || !drawn)
            {
                if (Stopwatch.GetElapsedTime(_keyAt) > s_noFrameAfterKey)
                {
                    FailTyping("no frame followed a key within 5 s");
                    return;
                }

                NextTypingFrame();
                return;
            }

            var end = _expectedDraws == 0 ? _renderStamp : Math.Max(_renderStamp, Volatile.Read(ref _lastDrawStamp));
            var handler = Stopwatch.GetElapsedTime(_keyAt, _handlerEnd).TotalMilliseconds;
            _handlerSamples.Add(handler);
            _frameSamples.Add(Stopwatch.GetElapsedTime(_keyAt, end).TotalMilliseconds);
            var drawing = Stopwatch.GetElapsedTime(0, Volatile.Read(ref _drawTicks) - _drawTicksAtRender).TotalMilliseconds;
            var renderPass = Stopwatch.GetElapsedTime(_renderStart, _renderStamp).TotalMilliseconds;
            _renderSamples.Add(renderPass);
            _drawSamples.Add(drawing);
            var inPhases = 0.0;
            for (var i = 0; i < EditPhases.Names.Length; i++)
            {
                var phaseMs = _phases.Milliseconds(i);
                _phaseSamples[i].Add(phaseMs);
                inPhases += phaseMs;
            }

            _phaseSamples[^1].Add(Math.Max(0, handler - inPhases));
            var judged = handler + renderPass + drawing;
            _samples.Add(judged);
            _leaseSamples.Add(view.Runner.LastLeaseWaitMilliseconds);
            if (_keyIncomplete)
            {
                _incompleteSamples.Add(judged);
            }

            _awaiting = false;
            _keyInJob++;
        }

        if (_job is null || _keyInJob >= _job.Keys)
        {
            if (_job is not null)
            {
                Write(_background ? TypingBackgroundLine(_samples, _leaseSamples, _incompleteSamples) : _job.FirstWhen is { } when ? TypingFirstLine(when, ViewName(_job.View), _samples[0]) : TypingLine(_job.Case, ViewName(_job.View), _samples, _handlerSamples, _frameSamples, _formatRun ? "format" : "typing", _renderSamples, _drawSamples, _phaseSamples));
            }

            if (_job is not null && _paneRun)
            {
                WriteRefresh(view);
            }

            if (_jobIndex >= _jobs.Count)
            {
                ReleaseProbe();
                if (_snapshotRun)
                {
                    _ = EndSnapshotRunAsync();
                    return;
                }

                Finish(Success);
                return;
            }

            _job = _jobs[_jobIndex++];
            _keyInJob = 0;
            _samples.Clear();
            _handlerSamples.Clear();
            _renderSamples.Clear();
            _drawSamples.Clear();
            foreach (var list in _phaseSamples)
            {
                list.Clear();
            }
            _frameSamples.Clear();
            _leaseSamples.Clear();
            _incompleteSamples.Clear();
            _placeStage = 0;
        }

        if (view.Mode != _job.View)
        {
            // The switch of the view; the first key after it is its own row.
            view.Mode = _job.View;
            NextTypingFrame();
            return;
        }

        if (!_background && !view.IsLayoutComplete)
        {
            NextTypingFrame();
            return;
        }

        if (_placeStage < 2)
        {
            // The view scrolls to the block of the place (the caret is looked for near the page in view), the caret goes there, and one frame later the view brings the caret itself
            // into view; a frame passes before the first key so that the scroll is drawn.
            if (_placeStage == 0)
            {
                var session = view.Tab.EnsureEditor();
                session.Phases = _phases;
                _ = session.Selection;
                var position = new TextPosition(_job.Place.Story, _job.Place.Paragraph, _job.Place.Offset);
                _range = _job.Format == "format-word" ? FormatRange(view, _job.Place) : null;
                session.SetSelection(_range ?? new TextRange(position, position));
                view.ScrollToBlock(_job.Place.HintBlock);
            }
            else
            {
                view.EnsureCaretVisible();
            }

            _placeStage++;
            NextTypingFrame();
            return;
        }

        if (_job.Format is { } formatKind)
        {
            SendFormat(view, formatKind);
            return;
        }

        var text = TypingPlaces.KeyText(_random, _keySequence++);
        _keyIncomplete = !view.IsLayoutComplete;
        _renderSeen = false;
        _renderStart = 0;
        _awaiting = true;
        _phases.Reset();
        _keyAt = Stopwatch.GetTimestamp();
        view.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = view });
        _handlerEnd = Stopwatch.GetTimestamp();
        NextTypingFrame();
    }

    // The pane run, at the end of a job: the debounced refresh of the display never fires while keys arrive one per frame (every key restarts its 100 ms timer), so it is run now, as the timer would
    // run it, and timed apart from the keys: the refresh of the window (the ribbon states and pickers) and the refresh of the pane (the selection read, the inspector). Recorded, never judged.
    private void WriteRefresh(PageView view)
    {
        var start = Stopwatch.GetTimestamp();
        _window.FormatRefresh?.Flush();
        var mid = Stopwatch.GetTimestamp();
        view.Tab.EnsureEditor();
        _window.FormattingOf(view.Tab)?.Refresh.Flush();
        var end = Stopwatch.GetTimestamp();
        Write(string.Create(
            CultureInfo.InvariantCulture,
            $"typing-refresh case={_job!.Case} view={ViewName(_job.View)} n=1 ms={Stopwatch.GetElapsedTime(start, end).TotalMilliseconds:0.00} window-ms={Stopwatch.GetElapsedTime(start, mid).TotalMilliseconds:0.00} pane-ms={Stopwatch.GetElapsedTime(mid, end).TotalMilliseconds:0.00}"));
    }

    // One formatting command through the page view, as a ribbon button or a key would run it: bold on the word (toggled each time) or the alignment of the paragraph (centered and right in turn). The selection
    // is set again before the key (outside the timing), so the display refresh and the pane hear of it as they do for a user. A command that changed nothing (an alignment the paragraph had already) is not
    // a sample; the next key follows at once.
    private void SendFormat(PageView view, string kind)
    {
        var session = view.Tab.EnsureEditor();
        if (_range is { } range)
        {
            session.SetSelection(range);
        }

        var model = view.Tab.Docx.Model;
        var revision = model.Edits.Revision;
        var id = kind == "format-word" ? WriteCommandIds.Bold : _keySequence++ % 2 == 0 ? WriteCommandIds.AlignCenter : WriteCommandIds.AlignRight;
        _keyIncomplete = false;
        _renderSeen = false;
        _renderStart = 0;
        _awaiting = true;
        session.Phases = _phases;
        _phases.Reset();
        _keyAt = Stopwatch.GetTimestamp();
        view.RunFormatCommand(id);
        _handlerEnd = Stopwatch.GetTimestamp();
        if (model.Edits.Revision == revision)
        {
            _awaiting = false;
            if (++_noChange > 10)
            {
                FailTyping($"a formatting command changed nothing ten times in a row ({_job?.Case}, {_job?.View})");
                return;
            }
        }
        else
        {
            _noChange = 0;
        }

        NextTypingFrame();
    }

    // One job: the first paragraph with text near the start of the document (the layout there exists already), at its end.
    private List<Job> PlanBackground(PageView view)
    {
        var model = view.Tab.Docx.Model;
        var layout = view.Runner.PageLayout;
        for (var i = 0; i < Math.Min(30, model.Body.Blocks.Count); i++)
        {
            if (model.Body.Blocks[i] is Paragraph { SectionBreak: null } paragraph)
            {
                var length = ParagraphProjection.Build(paragraph, layout.ProjectionHost(StoryRef.Body, paragraph)).Text.Length;
                if (length >= 30)
                {
                    return [new Job("background", WriteView.Page, null, new TypingPlace("background", StoryRef.Body, paragraph, length, i), _typingKeys == TypingKeys ? BackgroundKeys : _typingKeys)];
                }
            }
        }

        return [];
    }

    // The word at a place, read from the layout the active view edits with (the text of a note or a header differs between the pages and the strips): the session's host.
    private static TextRange FormatRange(PageView view, TypingPlace place)
    {
        var layout = (view.Mode == WriteView.Writing ? view.Runner.FlowLayout : null) ?? view.Runner.PageLayout;
        var position = new TextPosition(place.Story, place.Paragraph, place.Offset);
        var projection = ParagraphProjection.Build(place.Paragraph, layout.ProjectionHost(place.Story, place.Paragraph));
        return TextUnits.WordAt(position with { Offset = Math.Min(place.Offset, Math.Max(0, projection.Text.Length - 1)) }, projection, trailing: false);
    }

    private List<Job> Plan(PageView view)
    {
        var layout = view.Runner.PageLayout;
        var places = TypingPlaces.Pick(view.Tab.Docx.Model, layout);
        var jobs = new List<Job>();
        if (_paneRun)
        {
            // The formatting pane is open while the keys go in: the display refresh of the Home pickers and the pane's own refresh hear of every selection change, as they do for a user.
            _window.RunCommand(WriteCommandIds.FormattingPane);
        }

        if (_formatRun)
        {
            foreach (var mode in new[] { WriteView.Page, WriteView.Writing })
            {
                foreach (var kind in TypingPlaces.FormatNames)
                {
                    jobs.AddRange(places.Select(place => new Job($"{kind}/{place.Name}", mode, null, place, _typingKeys, kind)));
                }
            }

            return jobs;
        }
        foreach (var (mode, when) in new[] { (WriteView.Page, "after-open"), (WriteView.Writing, "after-switch") })
        {
            if (places.Count == 0)
            {
                break;
            }

            jobs.Add(new Job("first", mode, when, places[0], 1));
            foreach (var place in places)
            {
                jobs.Add(new Job(place.Name, mode, null, place, _typingKeys));
            }
        }

        return jobs;
    }
}

/// <summary>One place to type at: a paragraph of a story, an offset in its projected text and the index of the top-level body block on whose page the place is (where the view looks for it first).</summary>
internal sealed record TypingPlace(string Name, StoryRef Story, Paragraph Paragraph, int Offset, int HintBlock);

/// <summary>
/// The places of the typing run, found from the model and the pages layout alone: the middle of a long chapter, the end of a paragraph that ends a page (its growth moves the page break),
/// the paragraph before a section break, the end of a chapter, a table cell, a footnote, a header and a footer with a page number. A near copy of <c>EditCases</c> of <c>bench/write-layout</c> (the bench cannot reference
/// the app and the app must not carry the bench); change both together.
/// </summary>
internal static class TypingPlaces
{
    public static readonly string[] Names = ["middle", "page-break", "section-break", "chapter-end", "table-cell", "footnote", "header", "footer"];

    /// <summary>The cases of <c>--measure-format</c> at the same places: bold on one word, the alignment of one paragraph (<c>EditCases.FormatNames</c> of the bench).</summary>
    public static readonly string[] FormatNames = ["format-word", "format-paragraph"];

    /// <summary>The text of the keystroke <paramref name="index"/>: lower case letters with a space at every sixth one, from a seeded generator.</summary>
    public static string KeyText(Random random, int index) => index % 6 == 5 ? " " : ((char)('a' + random.Next(26))).ToString();

    public static List<TypingPlace> Pick(WordDocument document, DocumentLayout pages)
    {
        var found = new List<TypingPlace>();
        var blocks = document.Body.Blocks;
        var resolver = new StyleResolver(document.Styles, document.Settings, document.Numbering);

        int Length(StoryRef story, Paragraph paragraph) => ParagraphProjection.Build(paragraph, pages.ProjectionHost(story, paragraph)).Text.Length;

        var texts = new List<(int Index, Paragraph Paragraph, int Length)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p && p.SectionBreak is null)
            {
                texts.Add((i, p, Length(StoryRef.Body, p)));
            }
        }

        var middleIndex = blocks.Count / 2;
        Paragraph? middle = null;
        foreach (var t in texts.Where(t => t.Length >= 200).OrderBy(t => Math.Abs(t.Index - middleIndex)))
        {
            middle = t.Paragraph;
            found.Add(new TypingPlace("middle", StoryRef.Body, t.Paragraph, t.Length / 2, t.Index));
            break;
        }

        foreach (var t in texts.Where(t => t.Length >= 1).OrderBy(t => Math.Abs(t.Index - middleIndex)))
        {
            if (t.Index + 1 < blocks.Count
                && !ReferenceEquals(middle, t.Paragraph)
                && pages.Caret(new TextPosition(StoryRef.Body, t.Paragraph, t.Length), 0) is { } end
                && pages.Locate(t.Index + 1) is { } next
                && next.Page == end.Page + 1)
            {
                found.Add(new TypingPlace("page-break", StoryRef.Body, t.Paragraph, t.Length, t.Index));
                break;
            }
        }

        foreach (var i in Enumerable.Range(1, Math.Max(0, blocks.Count - 1)).Where(i => blocks[i] is Paragraph { SectionBreak: not null }).OrderBy(i => Math.Abs(i - middleIndex)))
        {
            if (blocks[i - 1] is Paragraph before && before.SectionBreak is null && Length(StoryRef.Body, before) > 0)
            {
                found.Add(new TypingPlace("section-break", StoryRef.Body, before, Length(StoryRef.Body, before), i - 1));
                break;
            }
        }

        var headings = Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph h && resolver.ForParagraph(h).OutlineLevel == 0).ToList();
        if (headings.Count > 1)
        {
            var heading = headings[Math.Min(headings.Count - 1, headings.Count * 3 / 4)];
            var last = texts.LastOrDefault(t => t.Index < heading && t.Length >= 20);
            if (last.Paragraph is not null && !found.Any(f => ReferenceEquals(f.Paragraph, last.Paragraph)))
            {
                found.Add(new TypingPlace("chapter-end", StoryRef.Body, last.Paragraph, last.Length, last.Index));
            }
        }

        foreach (var i in Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Table).OrderBy(i => Math.Abs(i - middleIndex)))
        {
            var cell = FirstRowParagraphs((Table)blocks[i]).ToList();
            var chosen = cell.FirstOrDefault(p => Length(StoryRef.Body, p) > 0) ?? cell.FirstOrDefault();
            if (chosen is not null)
            {
                found.Add(new TypingPlace("table-cell", StoryRef.Body, chosen, Length(StoryRef.Body, chosen) / 2, i));
                break;
            }
        }

        var citations = new List<(int Index, int Id)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p)
            {
                citations.AddRange(FootnoteIds(p.Inlines).Select(id => (i, id)));
            }
        }

        foreach (var (citedAt, id) in citations.OrderBy(c => Math.Abs(c.Index - middleIndex)))
        {
            var story = new StoryRef(StoryKind.Note, id.ToString(CultureInfo.InvariantCulture));
            var paragraph = document.Footnotes?.Find(id)?.Content.Blocks.OfType<Paragraph>().FirstOrDefault(p => Length(story, p) > 0);
            if (paragraph is not null)
            {
                found.Add(new TypingPlace("footnote", story, paragraph, Length(story, paragraph), citedAt));
                break;
            }
        }

        foreach (var key in document.HeadersFooters.Keys.Order(StringComparer.Ordinal))
        {
            var part = document.HeadersFooters[key];
            if (part.Kind != HeaderFooterKind.Header)
            {
                continue;
            }

            var story = new StoryRef(StoryKind.HeaderFooter, key);
            var paragraph = part.Content.Blocks.OfType<Paragraph>().FirstOrDefault(p => Length(story, p) > 0);
            if (paragraph is not null)
            {
                found.Add(new TypingPlace("header", story, paragraph, Length(story, paragraph), middleIndex));
                break;
            }
        }

        // The first footer (in key order) with a paragraph that shows the page number (a PAGE field), at its end: its block is made again for every page number.
        foreach (var key in document.HeadersFooters.Keys.Order(StringComparer.Ordinal))
        {
            var part = document.HeadersFooters[key];
            if (part.Kind != HeaderFooterKind.Footer)
            {
                continue;
            }

            var story = new StoryRef(StoryKind.HeaderFooter, key);
            var paragraph = part.Content.Blocks.OfType<Paragraph>().FirstOrDefault(p => HasPageField(p.Inlines) && Length(story, p) > 0);
            if (paragraph is not null)
            {
                found.Add(new TypingPlace("footer", story, paragraph, Length(story, paragraph), middleIndex));
                break;
            }
        }

        return [.. Names.SelectMany(name => found.Where(f => f.Name == name))];
    }

    private static IEnumerable<Paragraph> FirstRowParagraphs(Table table)
    {
        foreach (var row in table.Rows)
        {
            if (row is not TableRow tableRow)
            {
                continue;
            }

            foreach (var item in tableRow.Items)
            {
                if (item is TableCell cell)
                {
                    foreach (var block in cell.Blocks)
                    {
                        if (block is Paragraph paragraph)
                        {
                            yield return paragraph;
                        }
                    }
                }
            }

            yield break;
        }
    }

    // True when the inlines hold a PAGE field (a simple field or the instruction text of a complex one), not NUMPAGES or SECTIONPAGES.
    private static bool HasPageField(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case SimpleField field when IsPage(field.Instruction):
                    return true;
                case SimpleField field when HasPageField(field.Inlines):
                    return true;
                case Hyperlink link when HasPageField(link.Inlines):
                    return true;
                case Run run when run.Content.OfType<InstructionText>().Any(i => IsPage(i.Text)):
                    return true;
            }
        }

        return false;
    }

    private static bool IsPage(string instruction) => instruction.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var name, ..] && name.Equals("PAGE", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<int> FootnoteIds(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    foreach (var content in run.Content)
                    {
                        if (content is FootnoteReference reference)
                        {
                            yield return reference.Id;
                        }
                    }

                    break;
                case Hyperlink link:
                    foreach (var id in FootnoteIds(link.Inlines))
                    {
                        yield return id;
                    }

                    break;
            }
        }
    }
}
