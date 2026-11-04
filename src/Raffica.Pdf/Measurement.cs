using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf;

/// <summary>
/// The measurement switches of the PDF app, with the protocol of <c>bench/shell-probe/measure.cs</c>. Every run writes <c>first-page</c>
/// when the first sharp tile of page 1 of the file on the command line is painted, in the second animation frame after the page view
/// raised its first sharp page (the tile is in the cache and was drawn once, the frame after it was presented). Then, by switch:
/// <c>--measure-startup</c> ends; <c>--measure-memory</c> writes <c>memory-bytes &lt;n&gt;</c> two seconds later (private bytes on Windows,
/// working set elsewhere); <c>--measure-scroll</c> takes 600 wheel steps, one per animation frame, and writes
/// <c>scroll p99-ms=… max-ms=…</c> from the gaps between the frame callbacks; <c>--measure-second</c> waits for a file that a second
/// launch hands over and writes <c>second-page ms=…</c>, measured in this process from the arrival of the hand-over to the second animation
/// frame after the first sharp tile of the new tab. Every line uses the invariant culture and ASCII keys (they are not localized); a run
/// that does not finish ends with exit code 3 after 30 s. The switches need <c>--settings-file</c> (see <see cref="PdfOptions"/>), so a
/// measurement never reads or writes the settings, the recent files or the instance pipe of the user.
/// </summary>
internal sealed class Measurement : IDisposable
{
    public const int Success = 0;
    public const int TimedOut = 3;

    /// <summary>The number of wheel steps of the scroll run.</summary>
    public const int ScrollSteps = 600;

    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_memoryDelay = TimeSpan.FromSeconds(2);

    private readonly PdfOptions _options;
    private readonly PdfWindow _window;
    private readonly TextWriter _output;
    private readonly Action<int> _finish;
    private readonly TimeSpan _timeout;
    private readonly int _scrollSteps;
    private readonly List<double> _gaps = [];
    private Timer? _watchdog;
    private bool _firstPageSeen;
    private bool _firstPageWritten;
    private bool _finished;
    private long _handoverAt;
    private bool _secondPending;
    private long _lastFrame;
    private int _steps;
    private double _direction = -1;

    /// <param name="options">The command line (the measurement switch).</param>
    /// <param name="window">The window of the run.</param>
    /// <param name="output">Where the result lines go (the standard output of the app; tests give their own).</param>
    /// <param name="finish">Ends the run with the exit code once it is complete.</param>
    /// <param name="timeout">How long the run may take before the process ends with exit code 3; null: 30 s; <see cref="Timeout.InfiniteTimeSpan"/>: no watchdog (tests).</param>
    /// <param name="scrollSteps">The number of wheel steps of the scroll run (tests use fewer).</param>
    public Measurement(PdfOptions options, PdfWindow window, TextWriter output, Action<int> finish, TimeSpan? timeout = null, int scrollSteps = ScrollSteps)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(finish);
        _options = options;
        _window = window;
        _output = output;
        _finish = finish;
        _timeout = timeout ?? s_defaultTimeout;
        _scrollSteps = scrollSteps;
    }

    /// <summary>Starts the watchdog and listens for the first sharp page (and for the hand-over of the second file).</summary>
    public void Start()
    {
        if (_timeout != Timeout.InfiniteTimeSpan)
        {
            // A hanging UI thread cannot post a timeout, so the watchdog lives on a timer thread.
            _watchdog = new Timer(
                _ =>
                {
                    Console.Error.WriteLine("error: no result within 30 s");
                    Environment.Exit(TimedOut);
                },
                null,
                _timeout,
                Timeout.InfiniteTimeSpan);
        }

        _window.FirstSharpPageDrawn += OnFirstSharpPageDrawn;
        _window.HandoverArrived += OnHandoverArrived;
    }

    /// <summary>The line <c>scroll p99-ms=… max-ms=…</c> for the gaps between animation frames in milliseconds (nearest-rank 99th percentile).</summary>
    public static string ScrollLine(IReadOnlyList<double> gaps)
    {
        var sorted = gaps.Order().ToArray();
        var p99 = sorted[(int)Math.Ceiling(0.99 * sorted.Length) - 1];
        return string.Create(CultureInfo.InvariantCulture, $"scroll p99-ms={p99:0.00} max-ms={sorted[^1]:0.00}");
    }

    /// <summary>The line <c>second-page ms=…</c>.</summary>
    public static string SecondLine(double milliseconds) => string.Create(CultureInfo.InvariantCulture, $"second-page ms={milliseconds:0.00}");

    /// <summary>The line <c>memory-bytes &lt;n&gt;</c>.</summary>
    public static string MemoryLine(long bytes) => string.Create(CultureInfo.InvariantCulture, $"memory-bytes {bytes}");

    public void Dispose()
    {
        _watchdog?.Dispose();
        _window.FirstSharpPageDrawn -= OnFirstSharpPageDrawn;
        _window.HandoverArrived -= OnHandoverArrived;
    }

    private void Write(string line)
    {
        _output.WriteLine(line);
        _output.Flush();
    }

    private void OnHandoverArrived()
    {
        if (_options.MeasureSwitch == "--measure-second" && _firstPageWritten && !_secondPending)
        {
            _handoverAt = Stopwatch.GetTimestamp();
            _secondPending = true;
        }
    }

    private void OnFirstSharpPageDrawn(PdfTab tab)
    {
        _ = tab;
        if (!_firstPageSeen)
        {
            _firstPageSeen = true;
            AfterTwoFrames(OnFirstPage);
        }
        else if (_secondPending)
        {
            _secondPending = false;
            var from = _handoverAt;
            AfterTwoFrames(() =>
            {
                Write(SecondLine(Stopwatch.GetElapsedTime(from).TotalMilliseconds));
                Finish(Success);
            });
        }
    }

    private void AfterTwoFrames(Action action) =>
        _window.RequestAnimationFrame(_ => _window.RequestAnimationFrame(_ => action()));

    private void OnFirstPage()
    {
        if (_firstPageWritten)
        {
            return;
        }

        _firstPageWritten = true;
        Write("first-page");
        switch (_options.MeasureSwitch)
        {
            case "--measure-memory":
                DispatcherTimer.RunOnce(WriteMemory, s_memoryDelay);
                break;
            case "--measure-scroll":
                _lastFrame = 0;
                _window.RequestAnimationFrame(_ => ScrollFrame());
                break;
            case "--measure-second":
                // The run stays open for the file of the second launch.
                break;
            default:
                Finish(Success);
                break;
        }
    }

    private void WriteMemory()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        Write(MemoryLine(OperatingSystem.IsWindows() ? process.PrivateMemorySize64 : process.WorkingSet64));
        Finish(Success);
    }

    /// <summary>One animation frame of the scroll run: the gap to the frame before, then one wheel step (down; at the end of the file, up again).</summary>
    private void ScrollFrame()
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastFrame != 0)
        {
            _gaps.Add(Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds);
        }

        _lastFrame = now;
        if (_steps >= _scrollSteps || _window.ActiveView is not { } view)
        {
            Write(_gaps.Count > 0 ? ScrollLine(_gaps) : ScrollLine([0]));
            Finish(Success);
            return;
        }

        if (!view.ScrollByWheel(0, _direction))
        {
            _direction = -_direction;
            view.ScrollByWheel(0, _direction);
        }

        _steps++;
        _window.RequestAnimationFrame(_ => ScrollFrame());
    }

    private void Finish(int code)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Dispose();
        _finish(code);
    }
}
