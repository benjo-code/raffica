using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using Raffica.Write.Documents;

namespace Raffica.Write;

/// <summary>
/// The measurement switches of the Write app, with the protocol of <c>bench/shell-probe/measure.cs</c> (the one of the PDF app). Every run writes
/// <c>first-page</c> when page 1 of the file on the command line is drawn, in the second animation frame after the page view raised its first
/// sharp page (page 1 was drawn once and the frame after it was presented). Then, by switch: <c>--measure-startup</c> ends;
/// <c>--measure-memory</c> writes <c>memory-bytes &lt;n&gt;</c> two seconds later (private bytes on Windows, working set elsewhere);
/// <c>--measure-scroll</c> takes 600 wheel steps, one per animation frame, and writes <c>scroll p99-ms=... max-ms=...</c> from the gaps between
/// the frame callbacks (the background layout of the rest of the file runs meanwhile, as it does for a reader); <c>--measure-second</c> waits for a
/// file that a second launch hands over and writes <c>second-page ms=...</c>, measured in this process from the arrival of the hand-over to the
/// second animation frame after the first sharp page of the new tab. Every line uses the invariant culture and ASCII keys (they are not
/// localized); a run that does not finish ends with exit code 3 after 30 s (the message names the real timeout); a scroll run with no document
/// ends with exit code 1. <c>--measure-typing</c> types at eight places of the document in both views and writes one row per case and view (see <c>Measurement.Typing.cs</c>;
/// its watchdog is 240 s); <c>--measure-typing-pane</c> is the same run with the formatting pane open (the subscribers of the display refresh are in place) and <c>--measure-typing-snapshot</c> is the typing run while a loop writes recovery copies of the tab back to back (also one <c>snapshot n=... lease-median-ms=... total-median-ms=...</c> row); <c>--measure-format</c> formats at the same places (bold on one word, the alignment of one paragraph; rows <c>format case=&lt;kind&gt;/&lt;place&gt; view=...</c>). <c>--measure-structure</c> runs the structure commands of milestone 4.4a (see <c>Measurement.Structure.cs</c>; rows <c>structure case=&lt;case&gt; view=page ...</c>, <c>structure-refresh ...</c>). <c>--measure-sections</c> runs the section commands of milestone 4.4b (see <c>Measurement.Sections.cs</c>; rows <c>sections case=&lt;case&gt; view=page ...</c>, <c>sections-full ...</c>, <c>sections-refresh ...</c>) and <c>--measure-typing-sections-pane</c> is the typing run with the Sections pane open instead of the formatting pane. <c>--measure-objects</c> runs the table, picture and note commands of milestone 4.5a (see <c>Measurement.Objects.cs</c>; rows <c>objects case=&lt;case&gt; view=page ...</c>, <c>objects-refresh ...</c>), <c>--measure-gesture</c> the pointer moves of each drag kind (rows <c>gesture case=&lt;kind&gt; view=page ...</c>) and <c>--measure-typing-objects</c> the typing run in a paragraph that holds a picture and in a table cell. <c>--measure-fields</c> runs the field commands and the field refresh of milestone 4.5b (see <c>Measurement.Fields.cs</c>; rows <c>fields-open ...</c>, <c>fields case=&lt;case&gt; view=page ...</c>, <c>fields-settle ...</c>) and <c>--measure-typing-fields</c> the typing run in a document with a table of contents, with the refresh between the keys (rows <c>typing ...</c>, <c>fields-refresh ...</c>). The switches need <c>--settings-file</c> and a file (see <see cref="WriteOptions"/>), so a measurement never reads or
/// writes the settings, the recent files or the instance pipe of the user. Nothing of this class exists in a normal run: <c>App</c> makes it only
/// when a switch is given.
/// </summary>
internal sealed partial class Measurement : IDisposable
{
    public const int Success = 0;

    /// <summary>The run could not measure what it was asked to (for example, no document to scroll).</summary>
    public const int Failed = 1;
    public const int TimedOut = 3;

    /// <summary>The number of wheel steps of the scroll run.</summary>
    public const int ScrollSteps = 600;

    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_memoryDelay = TimeSpan.FromSeconds(2);

    private readonly WriteOptions _options;
    private readonly WriteWindow _window;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Action<int> _finish;
    private readonly Action<int> _exitProcess;
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
    /// <param name="error">Where the error messages go (null: the standard error of the app; tests give their own).</param>
    /// <param name="exitProcess">What the watchdog calls to end the process (null: <see cref="Environment.Exit(int)"/>; tests record the code).</param>
    /// <param name="typingKeys">The keys typed per case and view by the typing run (tests use fewer).</param>
    public Measurement(WriteOptions options, WriteWindow window, TextWriter output, Action<int> finish, TimeSpan? timeout = null, int scrollSteps = ScrollSteps, TextWriter? error = null, Action<int>? exitProcess = null, int typingKeys = TypingKeys)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(finish);
        _options = options;
        _window = window;
        _output = output;
        _error = error ?? Console.Error;
        _finish = finish;
        _exitProcess = exitProcess ?? Environment.Exit;
        _timeout = timeout ?? (options.MeasureSwitch is "--measure-typing" or "--measure-typing-background" or "--measure-typing-pane" or "--measure-format" or "--measure-structure" or "--measure-sections" or "--measure-objects" or "--measure-gesture" or "--measure-typing-objects" or "--measure-typing-sections-pane" or "--measure-typing-snapshot" or "--measure-fields" or "--measure-typing-fields" ? s_typingTimeout : s_defaultTimeout);
        _scrollSteps = scrollSteps;
        _typingKeys = typingKeys;
        _background = options.MeasureSwitch == "--measure-typing-background";
        _formatRun = options.MeasureSwitch == "--measure-format";
        _structureRun = options.MeasureSwitch == "--measure-structure";
        _sectionsRun = options.MeasureSwitch == "--measure-sections";
        _sectionsPaneRun = options.MeasureSwitch == "--measure-typing-sections-pane";
        _objectsRun = options.MeasureSwitch == "--measure-objects";
        _gestureRun = options.MeasureSwitch == "--measure-gesture";
        _typingObjectsRun = options.MeasureSwitch == "--measure-typing-objects";
        _fieldsRun = options.MeasureSwitch == "--measure-fields";
        _typingFieldsRun = options.MeasureSwitch == "--measure-typing-fields";
        _paneRun = options.MeasureSwitch is "--measure-typing-pane" or "--measure-typing-sections-pane";
        _snapshotRun = options.MeasureSwitch == "--measure-typing-snapshot";
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
                    _error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"error: no result within {_timeout.TotalSeconds:0.##} s"));
                    _exitProcess(TimedOut);
                },
                null,
                _timeout,
                Timeout.InfiniteTimeSpan);
        }

        _window.FirstSharpPageDrawn += OnFirstSharpPageDrawn;
        _window.HandoverArrived += OnHandoverArrived;
    }

    /// <summary>The line <c>scroll p99-ms=... max-ms=...</c> for the gaps between animation frames in milliseconds (nearest-rank 99th percentile).</summary>
    public static string ScrollLine(IReadOnlyList<double> gaps)
    {
        var sorted = gaps.Order().ToArray();
        var p99 = sorted[(int)Math.Ceiling(0.99 * sorted.Length) - 1];
        return string.Create(CultureInfo.InvariantCulture, $"scroll p99-ms={p99:0.00} max-ms={sorted[^1]:0.00}");
    }

    /// <summary>The line <c>second-page ms=...</c>.</summary>
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

    private void OnFirstSharpPageDrawn(WriteTab tab)
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
            case "--measure-typing" or "--measure-typing-background" or "--measure-typing-pane" or "--measure-format" or "--measure-structure" or "--measure-sections" or "--measure-objects" or "--measure-gesture" or "--measure-typing-objects" or "--measure-typing-sections-pane" or "--measure-typing-snapshot" or "--measure-fields" or "--measure-typing-fields":
                StartTyping();
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
        if (_window.ActiveView is not { } view)
        {
            // No page view to scroll: a line of zeros would pass for a perfect result.
            _error.WriteLine("error: no document is shown, nothing to scroll");
            Finish(Failed);
            return;
        }

        if (_steps >= _scrollSteps)
        {
            Write(_gaps.Count > 0 ? ScrollLine(_gaps) : ScrollLine([0]));
            Finish(Success);
            return;
        }

        if (!view.ScrollByWheel(_direction))
        {
            _direction = -_direction;
            view.ScrollByWheel(_direction);
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
        _snapshotCancel?.Cancel();
        Dispose();
        _finish(code);
    }
}
