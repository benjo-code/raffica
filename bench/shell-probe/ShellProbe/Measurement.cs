using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;

namespace ShellProbe;

/// <summary>The window the probe shows.</summary>
internal enum ProbeMode
{
    Empty,
    Frame,
}

/// <summary>The command line of the probe; see bench/shell-probe/README.md.</summary>
internal sealed class ProbeOptions
{
    public ProbeMode Mode { get; private set; } = ProbeMode.Frame;

    public string Language { get; private set; } = "en";

    public bool MeasureStartup { get; private set; }

    public bool MeasureInput { get; private set; }

    public bool MeasureMemory { get; private set; }

    public string? PrintString { get; private set; }

    public static ProbeOptions? Parse(string[] args, out string error)
    {
        var o = new ProbeOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            switch (name)
            {
                case "--measure-startup": o.MeasureStartup = true; break;
                case "--measure-input": o.MeasureInput = true; break;
                case "--measure-memory": o.MeasureMemory = true; break;
                case "--mode" or "--lang" or "--print-string":
                    if (i + 1 >= args.Length)
                    {
                        error = $"error: {name} needs a value";
                        return null;
                    }

                    var value = args[++i];
                    if (name == "--mode" && value is "empty" or "frame")
                    {
                        o.Mode = value == "empty" ? ProbeMode.Empty : ProbeMode.Frame;
                    }
                    else if (name == "--lang" && value is "en" or "de")
                    {
                        o.Language = value;
                    }
                    else if (name == "--print-string")
                    {
                        o.PrintString = value;
                    }
                    else
                    {
                        error = $"error: invalid value '{value}' for {name}";
                        return null;
                    }

                    break;
                default:
                    error = $"error: unknown switch {name}";
                    return null;
            }
        }

        if (o.MeasureInput && o.Mode != ProbeMode.Frame)
        {
            error = "error: --measure-input needs --mode frame";
            return null;
        }

        error = string.Empty;
        return o;
    }
}

/// <summary>
/// The measurement switches. "first-frame" is written in the second animation frame that follows the window's
/// <c>Opened</c>; every result line uses the invariant culture; a run that does not finish ends with exit code 3.
/// </summary>
internal sealed class Measurement : IDisposable
{
    public const int Success = 0;
    public const int TimedOut = 3;

    private const int Presses = 100;

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_memoryDelay = TimeSpan.FromSeconds(2);

    private readonly ProbeOptions _options;
    private readonly Window _window;
    private readonly TextBox? _input;
    private readonly List<double> _presses = [];
    private Timer? _watchdog;

    public Measurement(ProbeOptions options, Window window, TextBox? input)
    {
        _options = options;
        _window = window;
        _input = input;
    }

    /// <summary>Whether a measurement switch is set; without one the probe is just a window.</summary>
    public static bool IsActive(ProbeOptions options) =>
        options.MeasureStartup || options.MeasureInput || options.MeasureMemory;

    public void Start()
    {
        // A hanging UI thread cannot post a timeout, so the watchdog lives on a timer thread.
        _watchdog = new Timer(
            _ =>
            {
                Console.Error.WriteLine("error: no result within 30 s");
                Environment.Exit(TimedOut);
            },
            null,
            s_timeout,
            Timeout.InfiniteTimeSpan);
        _window.Opened += (_, _) => _window.RequestAnimationFrame(_ => _window.RequestAnimationFrame(_ => OnFirstFrame()));
    }

    /// <summary>The line <c>input median-ms=… p99-ms=… max-ms=…</c> for the press times in milliseconds.</summary>
    public static string InputLine(IReadOnlyList<double> milliseconds)
    {
        var sorted = milliseconds.Order().ToArray();
        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        var p99 = sorted[(int)Math.Ceiling(0.99 * sorted.Length) - 1];
        return string.Create(CultureInfo.InvariantCulture, $"input median-ms={median:0.00} p99-ms={p99:0.00} max-ms={sorted[^1]:0.00}");
    }

    private static void Write(string line)
    {
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }

    private void OnFirstFrame()
    {
        Write("first-frame");
        if (_options.MeasureMemory)
        {
            DispatcherTimer.RunOnce(WriteMemory, s_memoryDelay);
        }
        else if (_options.MeasureInput)
        {
            PressNext();
        }
        else
        {
            Finish(Success);
        }
    }

    private void WriteMemory()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var bytes = OperatingSystem.IsWindows() ? process.PrivateMemorySize64 : process.WorkingSet64;
        Write(string.Create(CultureInfo.InvariantCulture, $"memory-bytes {bytes}"));
        Finish(Success);
    }

    /// <summary>One key press: focus, timestamp, KeyDown and TextInput on the text box, two frames later the elapsed time.</summary>
    private void PressNext()
    {
        if (_presses.Count == Presses)
        {
            Write(InputLine(_presses));
            Finish(Success);
            return;
        }

        var box = _input!;
        box.Focus();
        var start = Stopwatch.GetTimestamp();
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, Source = box });
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "a", Source = box });
        _window.RequestAnimationFrame(_ => _window.RequestAnimationFrame(_ =>
        {
            _presses.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            // Posted, so that the next press starts outside the frame callback.
            Dispatcher.UIThread.Post(PressNext);
        }));
    }

    public void Dispose() => _watchdog?.Dispose();

    private void Finish(int code)
    {
        Dispose();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown(code);
        }
        else
        {
            Environment.Exit(code);
        }
    }
}
