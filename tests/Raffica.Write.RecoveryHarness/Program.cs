using System.Collections.Concurrent;
using System.Globalization;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Recovery;

namespace Raffica.Write.RecoveryHarness;

/// <summary>
/// The child process of the killed-process acceptance test (<c>KilledProcessRecoveryTests</c> in the Write app tests). It makes a blank document, types "line 1" to "line n" through the
/// UI-free <see cref="TextEditor"/> (one paragraph per 5 ms) and runs the real <see cref="AutosaveScheduler"/> and <see cref="RecoveryStore"/> over it. For every finished copy it
/// prints <c>COPY &lt;revision&gt; &lt;paragraphCount&gt;</c> (the revision and the count of the copy as written, read inside the model lease). Afterwards it stays alive until it is
/// killed, and ends by itself only after <see cref="LifeBound"/>. The thread that runs <see cref="Main"/> plays the UI thread: typing, the clock's tick and the scheduler's
/// continuations all run on it, as in the app, and the snapshot's model read runs on a pool thread under a lease that excludes typing.
/// </summary>
internal static class Program
{
    private static readonly TimeSpan s_typingStep = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan s_lifeBound = TimeSpan.FromMinutes(5);

    private static int Main(string[] args)
    {
        if (!TryParse(args, out var folder, out var intervalMs, out var paragraphs))
        {
            Console.Error.WriteLine("usage: --folder <dir> --interval-ms <n> --paragraphs <n>");
            return 2;
        }

        var context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(context);
        var uiThread = Environment.CurrentManagedThreadId;

        using var docx = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var subject = new Subject(docx, uiThread);
        var store = new RecoveryStore(folder);
        using var clock = new ThreadTimerClock(context, TimeSpan.FromMilliseconds(intervalMs));
        using var scheduler = new AutosaveScheduler(store, clock, () => true);
        scheduler.Track(subject);

        var editor = new TextEditor(docx.Model, (_, _) => ProjectionHost.Plain);
        var caret = new TextPosition(StoryRef.Body, docx.Model.Body.Blocks.OfType<Paragraph>().First(), 0);
        var typed = 0;
        var lastPrinted = DateTimeOffset.MinValue;

        // The clock's tick starts the scheduler; this runs after each tick's pass and prints what that pass (or an earlier one) finished.
        clock.AfterTick = () => scheduler.WhenIdleAsync().ContinueWith(_ => PrintNewCopy(), TaskScheduler.FromCurrentSynchronizationContext());

        void PrintNewCopy()
        {
            var entries = store.List().Entries;
            var entry = entries.Count > 0 ? entries[0] : null;
            if (entry is null || entry.Written == lastPrinted)
            {
                return;
            }

            lastPrinted = entry.Written;
            var (revision, count) = subject.LastWritten;
            Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"COPY {revision} {count}"));
            Console.Out.Flush();
        }

        // One key per step, posted to the pump; the timer stops itself after the last paragraph (TypeNext reads it, so it is assigned after the declaration).
        Timer? typing = null;
        typing = new Timer(_ => context.Post(_ => TypeNext(), null), null, s_typingStep, s_typingStep);

        void TypeNext()
        {
            if (typed >= paragraphs)
            {
                typing?.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            typed++;
            var text = typed == 1 ? "line 1" : "\nline " + typed.ToString(CultureInfo.InvariantCulture);
            subject.TypeLock.Wait();
            try
            {
                var result = editor.InsertText(new TextRange(caret, caret), text);
                caret = result.Caret;
            }
            finally
            {
                subject.TypeLock.Release();
            }
        }

        context.Post(_ => { }, null);
        using (typing)
        {
            context.RunUntil(s_lifeBound);
        }

        return 0;
    }

    private static bool TryParse(string[] args, out string folder, out int intervalMs, out int paragraphs)
    {
        folder = string.Empty;
        intervalMs = 0;
        paragraphs = 0;
        string? f = null;
        int? i = null;
        int? p = null;
        for (var n = 0; n + 1 < args.Length; n += 2)
        {
            switch (args[n])
            {
                case "--folder":
                    f = args[n + 1];
                    break;
                case "--interval-ms" when int.TryParse(args[n + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && ms > 0:
                    i = ms;
                    break;
                case "--paragraphs" when int.TryParse(args[n + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0:
                    p = count;
                    break;
                default:
                    return false;
            }
        }

        if (f is null || i is null || p is null)
        {
            return false;
        }

        (folder, intervalMs, paragraphs) = (f, i.Value, p.Value);
        return true;
    }

    /// <summary>A single-threaded loop: what is posted runs on the thread that calls <see cref="RunUntil"/>, in order.</summary>
    private sealed class PumpContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public void RunUntil(TimeSpan bound)
        {
            using var cancel = new CancellationTokenSource(bound);
            try
            {
                foreach (var (callback, state) in _queue.GetConsumingEnumerable(cancel.Token))
                {
                    callback(state);
                }
            }
            catch (OperationCanceledException)
            {
                // Nobody killed the process within the bound: end cleanly.
            }
        }
    }

    /// <summary>The beat of the harness: a thread timer that posts the tick to the pump, so it runs on the "UI thread".</summary>
    private sealed class ThreadTimerClock(PumpContext context, TimeSpan interval) : IRecoveryClock, IDisposable
    {
        private Timer? _timer;

        public Action? AfterTick { get; set; }

        public IDisposable Every(TimeSpan requested, Action tick)
        {
            // The scheduler asks for its 30 s; the harness beats at its own, short interval.
            _timer = new Timer(_ => context.Post(_ => { tick(); AfterTick?.Invoke(); }, null), null, interval, interval);
            return _timer;
        }

        public void Dispose() => _timer?.Dispose();
    }

    /// <summary>The tab as the snapshot writer sees it; the model lease keeps the typing loop out while the model is read.</summary>
    private sealed class Subject(DocxFile docx, int uiThread) : IRecoverySubject
    {
        private readonly object _counts = new();
        private (int Revision, int Paragraphs) _lastWritten;

        public SemaphoreSlim TypeLock { get; } = new(1, 1);

        public string Id { get; } = RecoveryStore.NewId();

        public string Title => "Untitled";

        public string? OriginalPath => null;

        public DocxFile Docx { get; } = docx;

        public bool IsDirty => true;

        public SnapshotSkip? CannotSnapshotNow => null;

        /// <summary>The revision of the model. A read off the "UI thread" is the snapshot's read inside the lease: the revision and the paragraph count of what is written are taken together.</summary>
        public int Revision
        {
            get
            {
                var revision = Docx.Model.Edits.Revision;
                if (Environment.CurrentManagedThreadId != uiThread)
                {
                    var count = Docx.Model.Body.Blocks.OfType<Paragraph>().Count();
                    lock (_counts)
                    {
                        _lastWritten = (revision, count);
                    }
                }

                return revision;
            }
        }

        public (int Revision, int Paragraphs) LastWritten
        {
            get
            {
                lock (_counts)
                {
                    return _lastWritten;
                }
            }
        }

        public IDisposable TakeModelLease()
        {
            TypeLock.Wait();
            return new Release(TypeLock);
        }

        private sealed class Release(SemaphoreSlim gate) : IDisposable
        {
            private int _done;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) == 0)
                {
                    gate.Release();
                }
            }
        }
    }
}
