// Measurement script of the shell probe (bench/shell-probe/README.md, spec section 9).
//   dotnet run bench/shell-probe/measure.cs -- run --exe <probe> --mode empty|frame --case cold|warm|input|memory --out <csv> [--machine <name>] [--label <name>]
//   dotnet run bench/shell-probe/measure.cs -- run --target gallery --exe <RafficaGallery> --case cold|warm|input|memory --out <csv> [--machine <name>]
//   dotnet run bench/shell-probe/measure.cs -- run --target pdf --exe <RafficaPdf> --case cold|warm|thousand|second|scroll|memory --out <csv> [--machine <name>] [--testdata <folder>]
//   dotnet run bench/shell-probe/measure.cs -- size --publish-dir <dir> --fonts-dir <dir> --out <csv> [--target probe|gallery|pdf] [--mode frame] [--machine <name>] [--app <bundle dir>] [--dmg <file>]
//   dotnet run bench/shell-probe/measure.cs -- size --check <win-x64|linux-x64|osx-x64|osx-arm64> --publish-dir <dir> --fonts-dir <dir> [--target gallery|pdf] [--app <bundle dir> --dmg <file>: pdf on macOS]
//   dotnet run bench/shell-probe/measure.cs -- check --platform windows|linux|macos --in "<csv glob>" [--target gallery|pdf] [--summary <md>]
//   dotnet run bench/shell-probe/measure.cs -- budgets --in <csv>... --out <md>
//   dotnet run bench/shell-probe/measure.cs -- selftest
// CSV columns: machine,mode,case,run,value,unit. unit is ms or bytes. Rows of the case input carry the statistic
// (median, p99, max) in the column run; rows of the case size carry total, zip, fonts, native, app or dmg (the last two: the macOS bundle and disk image). The gallery
// (--target gallery) writes the mode gallery, the PDF app (--target pdf) the mode pdf; its cases are cold, warm, thousand, second, scroll
// (rows p99 and max per run in the column run) and memory. check --target pdf judges against the recorded limits (see CheckCommand).
//
// Exit codes: 0 ok, 1 a failed run, a value above its limit (or a failed selftest), 2 usage error (or no limits recorded for the binding
// platform of target pdf: a table without limits never passes).

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;

const string Usage =
    "usage: dotnet run bench/shell-probe/measure.cs -- run --exe <probe> --mode empty|frame --case cold|warm|input|memory --out <csv> [--machine <name>]\n" +
    "       dotnet run bench/shell-probe/measure.cs -- size --publish-dir <dir> --fonts-dir <dir> --out <csv> [--mode frame] [--machine <name>]\n" +
    "       dotnet run bench/shell-probe/measure.cs -- budgets --in <csv>... --out <md>\n" +
    "       dotnet run bench/shell-probe/measure.cs -- selftest";

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

if (args[0] == "selftest")
{
    return SelfTest.Run();
}

var options = Options.Parse(args[1..], out var usageError);
if (options is null)
{
    return Fail($"{usageError}\n{Usage}");
}

return args[0] switch
{
    "run" => RunCommand.Execute(options),
    "size" => SizeCommand.Execute(options),
    "budgets" => BudgetsCommand.Execute(options),
    "check" => CheckCommand.Execute(options),
    _ => Fail(Usage),
};

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

/// <summary>Medians, the nearest-rank percentile and the budget rules of spec section 9.4.</summary>
static class Stats
{
    public const double Tolerance = 1.25;
    public const long MiB = 1024 * 1024;

    /// <summary>The median; the mean of the two middle values for an even count.</summary>
    public static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("No values.", nameof(values));
        }

        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>The nearest-rank 99th percentile: the value at rank ceil(0.99 × n).</summary>
    public static double NearestRankP99(IReadOnlyCollection<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Ceiling(0.99 * sorted.Length) - 1];
    }

    /// <summary>The budget of a time: the median rounded up to the next 10 ms.</summary>
    public static double BudgetMs(double median) => Math.Ceiling(median / 10) * 10;

    /// <summary>The budget of memory: the median rounded up to the next 5 MiB.</summary>
    public static long BudgetBytesMemory(double bytes) => (long)Math.Ceiling(bytes / (5 * MiB)) * 5 * MiB;

    /// <summary>The budget of a size: the median rounded up to the next MiB.</summary>
    public static long BudgetBytesSize(double bytes) => (long)Math.Ceiling(bytes / MiB) * MiB;

    /// <summary>The limit of a check: budget × 1.25 (as in the predecessor).</summary>
    public static double Limit(double budget) => budget * Tolerance;
}

/// <summary>CSV text with the invariant culture, and the lines the probe writes.</summary>
static class Lines
{
    public const string CsvHeader = "machine,mode,case,run,value,unit";

    /// <summary>One CSV row. Never culture dependent: a decimal comma would split the value.</summary>
    public static string Csv(string machine, string mode, string @case, string run, double value, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{machine},{mode},{@case},{run},{value:0.##},{unit}");

    /// <summary>Reads <c>input median-ms=… p99-ms=… max-ms=…</c> (invariant culture).</summary>
    public static bool TryParseInput(string line, out double median, out double p99, out double max)
    {
        median = p99 = max = 0;
        var parts = line.Split(' ');
        return parts is ["input", var a, var b, var c]
            && TryValue(a, "median-ms=", out median)
            && TryValue(b, "p99-ms=", out p99)
            && TryValue(c, "max-ms=", out max);
    }

    /// <summary>Reads <c>memory-bytes &lt;n&gt;</c>.</summary>
    public static bool TryParseMemory(string line, out long bytes)
    {
        const string Prefix = "memory-bytes ";
        bytes = 0;
        return line.StartsWith(Prefix, StringComparison.Ordinal)
            && long.TryParse(line.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out bytes);
    }

    /// <summary>Reads <c>scroll p99-ms=… max-ms=…</c> of the PDF app (invariant culture).</summary>
    public static bool TryParseScroll(string line, out double p99, out double max)
    {
        p99 = max = 0;
        var parts = line.Split(' ');
        return parts is ["scroll", var a, var b] && TryValue(a, "p99-ms=", out p99) && TryValue(b, "max-ms=", out max);
    }

    /// <summary>Reads <c>second-page ms=…</c> of the PDF app (invariant culture).</summary>
    public static bool TryParseSecond(string line, out double ms)
    {
        ms = 0;
        var parts = line.Split(' ');
        return parts is ["second-page", var a] && TryValue(a, "ms=", out ms);
    }

    private static bool TryValue(string text, string prefix, out double value)
    {
        value = 0;
        return text.StartsWith(prefix, StringComparison.Ordinal)
            && double.TryParse(text.AsSpan(prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>Starts the probe once and reads its output.</summary>
static class Runner
{
    /// <summary>How long the script waits for a run; the probe ends itself after 30 s with exit code 3.</summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Runs the probe. Returns the whole stdout and the milliseconds from just before <see cref="Process.Start()"/> to
    /// the moment the line starting with <paramref name="timedLine"/> was read (null when there is none), or why the
    /// run failed.
    /// </summary>
    public static (string? Output, double? LineMs, string? Error) RunOnce(string exe, string[] arguments, string timedLine = "first-frame")
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var start = Stopwatch.GetTimestamp();
        using var process = Process.Start(info);
        if (process is null)
        {
            return (null, null, $"{exe} could not be started");
        }

        // stdout is read line by line on a thread of its own, so the time is taken right when the line arrives.
        double? lineMs = null;
        var output = new StringBuilder();
        var reader = new Thread(() =>
        {
            while (process.StandardOutput.ReadLine() is { } line)
            {
                var now = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                lock (output)
                {
                    output.AppendLine(line);
                    if (lineMs is null && line.StartsWith(timedLine, StringComparison.Ordinal))
                    {
                        lineMs = now;
                    }
                }
            }
        })
        { IsBackground = true };
        reader.Start();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return (null, null, $"time limit of {Timeout.TotalSeconds:0} s exceeded, process killed");
        }

        reader.Join(TimeSpan.FromSeconds(5));
        var error = errorTask.GetAwaiter().GetResult().Trim();
        string text;
        double? ms;
        lock (output)
        {
            text = output.ToString().Trim();
            ms = lineMs;
        }

        if (process.ExitCode != 0)
        {
            return (null, null, $"exit code {process.ExitCode}{(error.Length > 0 ? $", stderr: {error}" : string.Empty)}{(text.Length > 0 ? $", stdout: {text}" : string.Empty)}");
        }

        return ms is null
            ? (null, null, $"no line \"{timedLine}\" on stdout (stdout: '{text}', stderr: '{error}')")
            : (text, ms, null);
    }
}

/// <summary>The command line: <c>--name value</c> pairs, <c>--in</c> takes one or more values.</summary>
sealed class Options
{
    private readonly Dictionary<string, List<string>> _values = [];

    public string? Get(string name) => _values.TryGetValue(name, out var list) ? list[^1] : null;

    public IReadOnlyList<string> GetAll(string name) => _values.TryGetValue(name, out var list) ? list : [];

    public static Options? Parse(string[] args, out string error)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"unexpected argument: {args[i]}";
                return null;
            }

            var name = args[i];
            var list = new List<string>();
            while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                list.Add(args[++i]);
                if (name != "--in")
                {
                    break;
                }
            }

            if (list.Count == 0)
            {
                error = $"{name} needs a value";
                return null;
            }

            options._values[name] = list;
        }

        error = string.Empty;
        return options;
    }

    public string? Require(params string[] names) => names.FirstOrDefault(n => Get(n) is null && GetAll(n).Count == 0);
}

/// <summary>The warning for a machine without a usable compositor rate (the predecessor's DwmFlush check).</summary>
static class FrameCadence
{
    /// <summary>A median DWM interval above this (below 25 Hz) makes the measured times invalid.</summary>
    public const double SlowIntervalMs = 40;

    private const int Samples = 5;

    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Returns a warning when the composition is missing or too slow, otherwise null. Never throws.</summary>
    public static string? Measure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            // On a pool thread, so that a DwmFlush that does not return cannot stall the script.
            var probe = Task.Run(Probe);
            if (!probe.Wait(ProbeTimeout))
            {
                return AssessFailure($"DwmFlush did not return within {ProbeTimeout.TotalSeconds:0} s");
            }

            var (hr, median) = probe.Result;
            return Assess(hr, median);
        }
        catch (Exception ex)
        {
            var inner = ex is AggregateException { InnerException: { } first } ? first : ex;
            return AssessFailure($"{inner.GetType().Name}: {inner.Message}");
        }
    }

    public static string AssessFailure(string reason) =>
        $"Warning: no desktop composition can be measured ({reason}). The probe then probably gets no or few frames, and the measurement is invalid.";

    /// <summary>A message for a GitHub Actions workflow command (escapes %, CR and LF).</summary>
    public static string ForAnnotation(string message) =>
        message.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);

    public static string? Assess(int hresult, double medianIntervalMs)
    {
        if (hresult != 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Warning: no desktop composition (DwmFlush 0x{hresult:X8}), for example in a service session without a desktop. The probe then probably gets no frames, and the measurement is invalid.");
        }

        return medianIntervalMs > SlowIntervalMs
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Warning: the desktop composition shows a new image only every {medianIntervalMs:0} ms ({1000 / medianIntervalMs:0.0} Hz, 60 Hz expected). Every run contains up to two such waits; the times say nothing about the probe. The usual cause is a VM without a GPU, a display that is off or a remote session; a machine with a GPU and an active display measures validly.")
            : null;
    }

    private static (int HResult, double MedianMs) Probe()
    {
        var hr = DwmFlush();
        var intervals = new List<double>(Samples);
        var last = Stopwatch.GetTimestamp();
        for (var i = 0; i < Samples && hr == 0; i++)
        {
            hr = DwmFlush();
            var now = Stopwatch.GetTimestamp();
            intervals.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
            last = now;
        }

        return hr != 0 ? (hr, 0) : (0, Stats.Median(intervals));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}

/// <summary><c>run</c>: the cases cold, warm, input and memory of one mode.</summary>
static class RunCommand
{
    private sealed record Case(string Name, int Runs, int Warmup, int PauseMs, string Switch);

    private static readonly Case[] Cases =
    [
        new("cold", 10, 0, 5000, "--measure-startup"),
        new("warm", 10, 3, 500, "--measure-startup"),
        new("input", 1, 0, 0, "--measure-input"),
        new("memory", 10, 0, 500, "--measure-memory"),
    ];

    /// <summary>The name of the case in the CSV: <c>--label</c> (for example cold-reboot) or the case itself.</summary>
    /// <summary>The number of measured runs: <c>--runs</c> (the cold run after a reboot is one) or the default of the case.</summary>
    public static int RunCount(Options o, int fallback) => int.TryParse(o.Get("--runs"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : fallback;

    /// <summary>The file next to a CSV that holds the warning about an invalid machine.</summary>
    public static string WarningPath(string csv) => csv + ".warning.txt";

    public static string Label(Options o) => o.Get("--label") ?? o.Get("--case")!;

    /// <summary>The arguments of one run. The gallery shows the frame view and gets a settings file of its own, so that a measurement never reads or writes the settings of the user; it has no <c>--mode</c>.</summary>
    public static string[] Arguments(string target, string? mode, string caseName, string settingsFile)
    {
        var caseSwitch = Cases.First(x => x.Name == caseName).Switch;
        return target == "gallery"
            ? ["--view", "frame", caseSwitch, "--settings-file", settingsFile]
            : ["--mode", mode!, caseSwitch];
    }

    /// <summary>The value of the CSV column <c>mode</c>: <c>gallery</c> for the gallery, else the probe's mode.</summary>
    public static string CsvMode(string target, string? mode) => target is "gallery" or "pdf" ? target : mode!;

    public static int Execute(Options o)
    {
        var target = o.Get("--target") ?? "probe";
        if (target is not ("probe" or "gallery" or "pdf"))
        {
            Console.Error.WriteLine("--target must be probe, gallery or pdf.");
            return 2;
        }

        if (target == "pdf")
        {
            return PdfRun.Execute(o);
        }

        string[] required = target == "probe" ? ["--exe", "--mode", "--case", "--out"] : ["--exe", "--case", "--out"];
        if (o.Require(required) is { } missing)
        {
            Console.Error.WriteLine($"{missing} is required.");
            return 2;
        }

        if (target == "gallery" && o.Get("--mode") is not null)
        {
            Console.Error.WriteLine("--mode belongs to the probe; the gallery runs the frame view (--target gallery).");
            return 2;
        }

        var exe = Path.GetFullPath(o.Get("--exe")!);
        var mode = o.Get("--mode");
        var c = Cases.FirstOrDefault(x => x.Name == o.Get("--case"));
        if (c is null || (target == "probe" && mode is not ("empty" or "frame")) || !File.Exists(exe))
        {
            Console.Error.WriteLine(!File.Exists(exe) ? $"file not found: {exe}" : "--mode must be empty or frame (probe only) and --case cold, warm, input or memory.");
            return 2;
        }

        var machine = o.Get("--machine") ?? Environment.MachineName;
        var csvMode = CsvMode(target, mode);
        var name = $"{csvMode}/{c.Name}";
        Console.WriteLine($"{name}: {exe} {string.Join(' ', Arguments(target, mode, c.Name, "<settings file>"))}");
        if (c.Name == "cold")
        {
            Console.WriteLine("note: \"cold\" does not empty the file cache of the operating system; a real cold start is a manual measurement right after a reboot (cold-reboot).");
        }

        // Only a warning: the run itself stays as it is, whatever the machine. It is repeated next to the result.
        var warning = FrameCadence.Measure();
        if (warning is not null)
        {
            Warn(warning);
        }

        var rows = new List<string>();
        var values = new List<double>();
        string? failure = null;
        var runs = RunCount(o, c.Runs);
        for (var run = -c.Warmup + 1; run <= runs; run++)
        {
            // A fresh settings file per run (the gallery); it is never created by the script and removed afterwards.
            var settingsFile = Path.Combine(Path.GetTempPath(), $"raffica-measure-{Environment.ProcessId}-{Guid.NewGuid():N}.json");
            string? output;
            double? ms;
            string? error;
            try
            {
                (output, ms, error) = Runner.RunOnce(exe, Arguments(target, mode, c.Name, settingsFile), c.Name == "input" ? "input" : c.Name == "memory" ? "memory-bytes" : "first-frame");
            }
            finally
            {
                File.Delete(settingsFile);
            }

            if (error is not null)
            {
                failure = $"{name}: run {(run <= 0 ? "warm-up" : run.ToString(CultureInfo.InvariantCulture))} failed: {error}";
                break;
            }

            if (c.Name == "input")
            {
                var line = output!.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("input", StringComparison.Ordinal));
                if (!Lines.TryParseInput(line, out var median, out var p99, out var max))
                {
                    failure = $"{name}: unreadable line: {line}";
                    break;
                }

                foreach (var (stat, value) in new[] { ("median", median), ("p99", p99), ("max", max) })
                {
                    rows.Add(Lines.Csv(machine, csvMode, Label(o), stat, value, "ms"));
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {stat}: {value:0.00} ms"));
                }

                values.Add(median);
                continue;
            }

            double value2;
            string unit;
            if (c.Name == "memory")
            {
                var line = output!.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("memory-bytes", StringComparison.Ordinal));
                if (!Lines.TryParseMemory(line, out var bytes))
                {
                    failure = $"{name}: unreadable line: {line}";
                    break;
                }

                (value2, unit) = (bytes, "bytes");
            }
            else
            {
                (value2, unit) = (ms!.Value, "ms");
            }

            var label = run <= 0 ? $"warm-up {run + c.Warmup}" : $"run {run}";
            Console.WriteLine(unit == "ms"
                ? string.Create(CultureInfo.InvariantCulture, $"  {label}: {value2:0.0} ms")
                : string.Create(CultureInfo.InvariantCulture, $"  {label}: {value2 / Stats.MiB:0.0} MiB"));
            if (run > 0)
            {
                values.Add(value2);
                rows.Add(Lines.Csv(machine, csvMode, Label(o), run.ToString(CultureInfo.InvariantCulture), value2, unit));
            }

            if (run < runs)
            {
                Thread.Sleep(c.PauseMs);
            }
        }

        WriteCsv(o.Get("--out")!, rows);
        if (warning is not null)
        {
            File.WriteAllText(WarningPath(Path.GetFullPath(o.Get("--out")!)), warning + Environment.NewLine, new UTF8Encoding(false));
        }

        if (failure is not null)
        {
            Repeat(warning);
            Console.Error.WriteLine($"FAILED: {failure}");
            return 1;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: median {Stats.Median(values):0.0}, min {values.Min():0.0}, max {values.Max():0.0} ({values.Count} {(c.Name == "input" ? "run, input in ms" : "runs")}{(c.Name == "memory" ? ", bytes" : string.Empty)})"));
        Repeat(warning);
        return 0;
    }

    /// <summary>The warning on the console and as a GitHub Actions annotation, so it also shows on the summary of the run.</summary>
    private static void Warn(string warning)
    {
        Console.WriteLine(warning);
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Console.WriteLine($"::warning title=Shell probe measurement::{FrameCadence.ForAnnotation(warning)}");
        }
    }

    private static void Repeat(string? warning)
    {
        if (warning is not null)
        {
            Console.WriteLine($"Result with reservation: {warning}");
        }
    }

    public static void WriteCsv(string path, IReadOnlyList<string> rows)
    {
        var full = Path.GetFullPath(path);
        if (Path.GetDirectoryName(full) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(full, Lines.CsvHeader + "\n" + string.Concat(rows.Select(r => r + "\n")), new UTF8Encoding(false));
        Console.WriteLine($"CSV: {full}");
    }
}

/// <summary>
/// <c>run --target pdf</c>: the cases of the PDF app (spec section 5). The app is started with its measurement switch, a file of
/// <c>testdata/pdfs</c> and a settings file in a fresh temporary folder per run (so a measurement never reads or writes the settings, the
/// recent files or the instance pipe of the user, and no run sees the recent file of the run before). The CSV mode is <c>pdf</c>.
/// </summary>
static class PdfRun
{
    public sealed record Case(string Name, int Runs, int Warmup, int PauseMs, string Switch, string File, string Line);

    /// <summary>The file a second launch hands to the running instance in the case <c>second</c>.</summary>
    public const string SecondFile = "outline.pdf";

    public static readonly Case[] Cases =
    [
        new("cold", 10, 0, 5000, "--measure-startup", "hello_world.pdf", "first-page"),
        new("warm", 10, 3, 500, "--measure-startup", "hello_world.pdf", "first-page"),
        new("thousand", 10, 3, 500, "--measure-startup", "thousand-pages.pdf", "first-page"),
        new("second", 10, 1, 500, "--measure-second", "hello_world.pdf", "second-page"),
        new("scroll", 5, 1, 500, "--measure-scroll", "thousand-pages.pdf", "scroll"),
        new("memory", 10, 0, 500, "--measure-memory", "thousand-pages.pdf", "memory-bytes"),
    ];

    /// <summary>The arguments of one run of the app: the switch, the settings file and the file to open.</summary>
    public static string[] Arguments(string caseName, string settingsFile, string testdata)
    {
        var c = Cases.First(x => x.Name == caseName);
        return [c.Switch, "--settings-file", settingsFile, Path.Combine(testdata, c.File)];
    }

    /// <summary>The arguments of the second launch of the case <c>second</c>: the same settings file (so it finds the running instance) and the second file.</summary>
    public static string[] SecondArguments(string settingsFile, string testdata) => ["--settings-file", settingsFile, Path.Combine(testdata, SecondFile)];

    public static int Execute(Options o)
    {
        if (o.Require("--exe", "--case", "--out") is { } missing)
        {
            Console.Error.WriteLine($"{missing} is required.");
            return 2;
        }

        if (o.Get("--mode") is not null)
        {
            Console.Error.WriteLine("--mode belongs to the probe; the PDF app has none.");
            return 2;
        }

        var exe = Path.GetFullPath(o.Get("--exe")!);
        var c = Cases.FirstOrDefault(x => x.Name == o.Get("--case"));
        if (c is null || !File.Exists(exe))
        {
            Console.Error.WriteLine(!File.Exists(exe) ? $"file not found: {exe}" : "--case must be cold, warm, thousand, second, scroll or memory (--target pdf).");
            return 2;
        }

        var testdata = Path.GetFullPath(o.Get("--testdata") ?? Path.Combine("testdata", "pdfs"));
        var files = c.Name == "second" ? new[] { c.File, SecondFile } : [c.File];
        if (files.FirstOrDefault(f => !File.Exists(Path.Combine(testdata, f))) is { } absent)
        {
            Console.Error.WriteLine($"file not found: {Path.Combine(testdata, absent)} (--testdata <folder> names the folder with the test files).");
            return 2;
        }

        var machine = o.Get("--machine") ?? Environment.MachineName;
        var name = $"pdf/{c.Name}";
        Console.WriteLine($"{name}: {exe} {string.Join(' ', Arguments(c.Name, "<settings file>", testdata))}");
        if (c.Name == "cold")
        {
            Console.WriteLine("note: \"cold\" does not empty the file cache of the operating system; a real cold start is a manual measurement right after a reboot (cold-reboot).");
        }

        var warning = FrameCadence.Measure();
        if (warning is not null)
        {
            Console.WriteLine(warning);
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                Console.WriteLine($"::warning title=PDF measurement::{FrameCadence.ForAnnotation(warning)}");
            }
        }

        var rows = new List<string>();
        var values = new List<double>();
        string? failure = null;
        var runs = RunCommand.RunCount(o, c.Runs);
        for (var run = -c.Warmup + 1; run <= runs; run++)
        {
            var folder = Path.Combine(Path.GetTempPath(), $"raffica-measure-{Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            var settingsFile = Path.Combine(folder, "settings.json");
            (double? Value, double? Second, string? Error) result;
            try
            {
                result = RunOnce(exe, c, settingsFile, testdata);
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (IOException)
                {
                    // A process that is still ending; the folder is in the temp folder anyway.
                }
            }

            if (result.Error is not null)
            {
                failure = $"{name}: run {(run <= 0 ? "warm-up" : run.ToString(CultureInfo.InvariantCulture))} failed: {result.Error}";
                break;
            }

            var label = run <= 0 ? $"warm-up {run + c.Warmup}" : $"run {run}";
            if (c.Name == "scroll")
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {label}: p99 {result.Value:0.00} ms, max {result.Second:0.00} ms"));
                if (run > 0)
                {
                    values.Add(result.Value!.Value);
                    rows.Add(Lines.Csv(machine, "pdf", RunCommand.Label(o), "p99", result.Value!.Value, "ms"));
                    rows.Add(Lines.Csv(machine, "pdf", RunCommand.Label(o), "max", result.Second!.Value, "ms"));
                }
            }
            else
            {
                var unit = c.Name == "memory" ? "bytes" : "ms";
                var v = result.Value!.Value;
                Console.WriteLine(unit == "ms"
                    ? string.Create(CultureInfo.InvariantCulture, $"  {label}: {v:0.0} ms")
                    : string.Create(CultureInfo.InvariantCulture, $"  {label}: {v / Stats.MiB:0.0} MiB"));
                if (run > 0)
                {
                    values.Add(v);
                    rows.Add(Lines.Csv(machine, "pdf", RunCommand.Label(o), run.ToString(CultureInfo.InvariantCulture), v, unit));
                }
            }

            if (run < runs)
            {
                Thread.Sleep(c.PauseMs);
            }
        }

        RunCommand.WriteCsv(o.Get("--out")!, rows);
        if (warning is not null)
        {
            File.WriteAllText(RunCommand.WarningPath(Path.GetFullPath(o.Get("--out")!)), warning + Environment.NewLine, new UTF8Encoding(false));
        }

        if (failure is not null)
        {
            Console.Error.WriteLine($"FAILED: {failure}");
            return 1;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: median {Stats.Median(values):0.0}, min {values.Min():0.0}, max {values.Max():0.0} ({values.Count} runs{(c.Name == "memory" ? ", bytes" : c.Name == "scroll" ? ", p99 in ms" : string.Empty)})"));
        if (warning is not null)
        {
            Console.WriteLine($"Result with reservation: {warning}");
        }

        return 0;
    }

    /// <summary>One run: the value (ms for the time cases and the scroll p99, bytes for memory), the scroll maximum and an error text.</summary>
    private static (double? Value, double? Second, string? Error) RunOnce(string exe, Case c, string settingsFile, string testdata)
    {
        if (c.Name == "second")
        {
            var (ms, secondError) = RunSecond(exe, Arguments(c.Name, settingsFile, testdata), SecondArguments(settingsFile, testdata));
            return (ms, null, secondError);
        }

        var (output, lineMs, error) = Runner.RunOnce(exe, Arguments(c.Name, settingsFile, testdata), c.Line);
        if (error is not null)
        {
            return (null, null, error);
        }

        var line = output!.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith(c.Line, StringComparison.Ordinal));
        switch (c.Name)
        {
            case "memory":
                return Lines.TryParseMemory(line, out var bytes) ? (bytes, null, null) : (null, null, $"unreadable line: {line}");
            case "scroll":
                return Lines.TryParseScroll(line, out var p99, out var max) ? (p99, max, null) : (null, null, $"unreadable line: {line}");
            default:
                return (lineMs, null, null);
        }
    }

    /// <summary>
    /// The case <c>second</c>: starts the app with the first file, waits for its <c>first-page</c>, starts a second launch with the second
    /// file (it hands the file over and ends), and reads the <c>second-page ms=…</c> line of the running instance, which times itself from
    /// the arrival of the hand-over.
    /// </summary>
    public static (double? Ms, string? Error) RunSecond(string exe, string[] primaryArguments, string[] secondArguments)
    {
        var timeout = Runner.Timeout;
        using var primary = Start(exe, primaryArguments);
        if (primary is null)
        {
            return (null, $"{exe} could not be started");
        }

        using var firstSeen = new ManualResetEventSlim();
        using var secondSeen = new ManualResetEventSlim();
        string? secondLine = null;
        var reader = new Thread(() =>
        {
            while (primary.StandardOutput.ReadLine() is { } line)
            {
                if (line.StartsWith("first-page", StringComparison.Ordinal))
                {
                    firstSeen.Set();
                }
                else if (line.StartsWith("second-page", StringComparison.Ordinal))
                {
                    secondLine = line.Trim();
                    secondSeen.Set();
                }
            }
        })
        { IsBackground = true };
        reader.Start();
        var errorTask = primary.StandardError.ReadToEndAsync();
        try
        {
            if (!firstSeen.Wait(timeout))
            {
                return (null, $"no line \"first-page\" within {timeout.TotalSeconds:0} s");
            }

            // The pipe of the running instance opens right after the first sharp page; a moment of grace.
            Thread.Sleep(500);
            using var second = Start(exe, secondArguments);
            if (second is null)
            {
                return (null, "the second launch could not be started");
            }

            _ = second.StandardOutput.ReadToEndAsync();
            _ = second.StandardError.ReadToEndAsync();
            if (!second.WaitForExit(TimeSpan.FromSeconds(15)))
            {
                second.Kill(entireProcessTree: true);
                return (null, "the second launch did not end: it found no running instance to hand the file to");
            }

            if (!secondSeen.Wait(timeout))
            {
                return (null, $"no line \"second-page\" within {timeout.TotalSeconds:0} s");
            }

            if (!primary.WaitForExit(timeout))
            {
                return (null, "the running instance did not end after second-page");
            }

            reader.Join(TimeSpan.FromSeconds(5));
            if (primary.ExitCode != 0)
            {
                return (null, $"exit code {primary.ExitCode}, stderr: {errorTask.GetAwaiter().GetResult().Trim()}");
            }

            return Lines.TryParseSecond(secondLine!, out var ms) ? (ms, null) : (null, $"unreadable line: {secondLine}");
        }
        finally
        {
            if (!primary.HasExited)
            {
                primary.Kill(entireProcessTree: true);
                primary.WaitForExit();
            }
        }
    }

    private static Process? Start(string exe, string[] arguments)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info);
    }
}

/// <summary><c>size</c>: the published folder, the zip, the share of fonts and of native libraries.</summary>
static class SizeCommand
{
    public static int Execute(Options o)
    {
        var check = o.Get("--check");
        string[] required = check is null ? ["--publish-dir", "--fonts-dir", "--out"] : ["--publish-dir", "--fonts-dir"];
        if (o.Require(required) is { } missing)
        {
            Console.Error.WriteLine($"{missing} is required.");
            return 2;
        }

        var target = o.Get("--target") ?? (check is null ? "probe" : "gallery");
        if (target is not ("probe" or "gallery" or "pdf"))
        {
            Console.Error.WriteLine("--target must be probe, gallery or pdf.");
            return 2;
        }

        var platform = check is null ? null : CheckCommand.PlatformOfRid(check);
        if (check is not null && platform is null)
        {
            Console.Error.WriteLine("--check needs a runtime identifier: win-x64, linux-x64, osx-x64 or osx-arm64.");
            return 2;
        }

        var publish = Path.GetFullPath(o.Get("--publish-dir")!);
        var fonts = Path.GetFullPath(o.Get("--fonts-dir")!);
        if (!Directory.Exists(publish) || !Directory.Exists(fonts))
        {
            Console.Error.WriteLine("--publish-dir and --fonts-dir must be existing folders.");
            return 2;
        }

        var mainExe = target == "gallery" ? "RafficaGallery" : target == "pdf" ? "RafficaPdf" : "ShellProbe";
        if (check is not null && !File.Exists(Path.Combine(publish, mainExe + ".exe")) && !File.Exists(Path.Combine(publish, mainExe)))
        {
            // A failed publish leaves an empty folder; zero bytes would pass every limit.
            Console.Error.WriteLine($"--publish-dir has no {mainExe} executable ({mainExe}.exe or {mainExe}); nothing to check.");
            return 2;
        }

        // The macOS bundle and disk image (target pdf; with --check only on a macOS runtime identifier): bytes of every file of the bundle, bytes of the file.
        var appDir = o.Get("--app") is { } a ? Path.GetFullPath(a) : null;
        var dmgFile = o.Get("--dmg") is { } d ? Path.GetFullPath(d) : null;
        if ((appDir is not null || dmgFile is not null) && (target != "pdf" || (platform is not null && platform != "macos")))
        {
            Console.Error.WriteLine("--app and --dmg belong to --target pdf (and, with --check, to osx-x64 or osx-arm64).");
            return 2;
        }

        if ((appDir is not null && !Directory.Exists(appDir)) || (dmgFile is not null && !File.Exists(dmgFile)))
        {
            Console.Error.WriteLine("--app must be an existing folder (the .app bundle) and --dmg an existing file.");
            return 2;
        }

        // "app" counts the bytes of every file in the bundle, symbols included, so it deliberately differs from "total", which skips
        // .pdb/.dbg/.dSYM. Symbolic links (files and folders) are not followed and not counted, so a link never adds the size of its target.
        var appBytes = appDir is null ? (long?)null : AppBundleBytes(appDir);
        var dmgBytes = dmgFile is null ? (long?)null : new FileInfo(dmgFile).Length;

        var (total, zip, fontBytes, native) = Measure(publish, fonts, mainExe);

        if (platform is not null)
        {
            // The CI check: the size and zip limits of the check command, exit 1 above them, no CSV. The bundle and the image are judged only when given.
            var measured = new Dictionary<string, double?> { ["size"] = total / (double)Stats.MiB, ["zip"] = zip / (double)Stats.MiB };
            List<string> keys = ["size", "zip"];
            if (appBytes is { } ab)
            {
                measured["app"] = ab / (double)Stats.MiB;
                keys.Add("app");
            }

            if (dmgBytes is { } db)
            {
                measured["dmg"] = db / (double)Stats.MiB;
                keys.Add("dmg");
            }

            var pdf = target == "pdf";
            var results = pdf ? CheckCommand.JudgePdf(platform, measured, [.. keys]) : CheckCommand.Judge(platform, measured, [.. keys]);
            Console.WriteLine($"Published {target} ({check}):");
            Console.Write(CheckCommand.Render(results));
            if (pdf && CheckCommand.PdfHasNoLimits(platform))
            {
                // The sizes are printed above for the owner; a check without a limit never passes.
                Console.Error.WriteLine(CheckCommand.NoPdfLimits);
                return 2;
            }

            Console.WriteLine(CheckCommand.ExitCode(results) == 1 ? "size check: FAILED (above the limit)" : "size check: ok");
            return CheckCommand.ExitCode(results);
        }

        var machine = o.Get("--machine") ?? Environment.MachineName;
        var mode = o.Get("--mode") ?? (target is "gallery" or "pdf" ? target : "frame");

        List<(string, long)> sizes = [("total", total), ("zip", zip), ("fonts", fontBytes), ("native", native)];
        if (appBytes is { } appRow)
        {
            sizes.Add(("app", appRow));
        }

        if (dmgBytes is { } dmgRow)
        {
            sizes.Add(("dmg", dmgRow));
        }

        var rows = sizes
            .Select(r => Lines.Csv(machine, mode, "size", r.Item1, r.Item2, "bytes"))
            .ToList();
        foreach (var row in rows)
        {
            Console.WriteLine(row);
        }

        RunCommand.WriteCsv(o.Get("--out")!, rows);
        return 0;
    }

    /// <summary>The bytes of every file under the bundle folder; symbolic links are skipped (not followed, not counted); hidden files count.</summary>
    public static long AppBundleBytes(string bundle) =>
        Directory.EnumerateFiles(bundle, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Sum(f => new FileInfo(f).Length);

    /// <summary>
    /// The sizes in bytes. Debug symbols (<c>*.pdb</c>, <c>*.dbg</c>, <c>*.dSYM</c> bundles) are not shipped and count nowhere; the native
    /// libraries are every other file but the main executable; the zip uses <see cref="CompressionLevel.Optimal"/>.
    /// </summary>
    public static (long Total, long Zip, long Fonts, long Native) Measure(string publishDir, string fontsDir, string mainExe = "ShellProbe")
    {
        var files = Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories)
            .Where(f => !IsSymbols(f))
            .Select(f => new FileInfo(f))
            .ToList();
        var native = files.Where(f => Path.GetFileNameWithoutExtension(f.Name) != mainExe).Sum(f => f.Length);
        var fonts = Directory.EnumerateFiles(fontsDir, "*.ttf").Sum(f => new FileInfo(f).Length);

        var zipPath = Path.Combine(Path.GetTempPath(), $"shell-probe-size-{Environment.ProcessId}.zip");
        long zip;
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    archive.CreateEntryFromFile(file.FullName, Path.GetRelativePath(publishDir, file.FullName), CompressionLevel.Optimal);
                }
            }

            zip = new FileInfo(zipPath).Length;
        }
        finally
        {
            File.Delete(zipPath);
        }

        return (files.Sum(f => f.Length), zip, fonts, native);
    }

    private static bool IsSymbols(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".pdb" or ".dbg"
        || path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part.EndsWith(".dSYM", StringComparison.Ordinal));
}

/// <summary><c>budgets</c>: the medians, budgets and limits per machine and case as Markdown.</summary>
static class BudgetsCommand
{
    public sealed record Row(string Machine, string Mode, string Case, string Run, double Value, string Unit);

    public static int Execute(Options o)
    {
        if (o.Require("--in", "--out") is { } missing)
        {
            Console.Error.WriteLine($"{missing} is required.");
            return 2;
        }

        var rows = new List<Row>();
        var warnings = new List<string>();
        foreach (var path in o.GetAll("--in").SelectMany(Expand))
        {
            if (File.Exists(RunCommand.WarningPath(path)))
            {
                warnings.Add($"{Path.GetFileName(path)}: {File.ReadAllText(RunCommand.WarningPath(path)).Trim()}");
            }

            foreach (var line in File.ReadLines(path).Skip(1).Where(l => l.Length > 0))
            {
                var p = line.Split(',');
                if (p.Length != 6 || !double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                {
                    Console.Error.WriteLine($"unreadable CSV row in {path}: {line}");
                    return 1;
                }

                rows.Add(new Row(p[0], p[1], p[2], p[3], value, p[5]));
            }
        }

        if (rows.Count == 0)
        {
            Console.Error.WriteLine("no CSV rows found.");
            return 1;
        }

        File.WriteAllText(Path.GetFullPath(o.Get("--out")!), Render(rows, warnings), new UTF8Encoding(false));
        Console.WriteLine($"Markdown: {Path.GetFullPath(o.Get("--out")!)}");
        return 0;
    }

    /// <summary>The Markdown of all machines. Budgets and limits come from the frame mode only.</summary>
    public static string Render(IReadOnlyList<Row> rows, IReadOnlyList<string>? warnings = null)
    {
        // Label, case, run filter (null = the numbered runs), unit divisor, kind of budget.
        (string Label, string Case, string? Run, string Kind)[] metrics =
        [
            ("cold (ms)", "cold", null, "ms"),
            ("warm (ms)", "warm", null, "ms"),
            ("input median (ms)", "input", "median", "ms"),
            ("input p99 (ms)", "input", "p99", "none-ms"),
            ("input max (ms)", "input", "max", "none-ms"),
            ("memory (MiB)", "memory", null, "memory"),
            ("size total (MiB)", "size", "total", "size"),
            ("size zip (MiB)", "size", "zip", "size"),
            ("size fonts (MiB)", "size", "fonts", "none-bytes"),
            ("size native (MiB)", "size", "native", "none-bytes"),
            ("cold after reboot (ms)", "cold-reboot", null, "none-ms"),
        ];

        var text = new StringBuilder("# Shell probe: medians, budgets and limits\n\n");
        text.AppendLine("The budget is the median of the frame mode rounded up (ms to 10, memory to 5 MiB, size to 1 MiB); the limit is the budget × 1.25. Empty is the baseline; frame minus empty is the cost of the frame.");
        if (warnings is { Count: > 0 })
        {
            text.AppendLine().AppendLine("> **Invalid measurement:** these runs reported a machine without a usable compositor rate. Do not derive budgets from them.");
            foreach (var w in warnings)
            {
                text.AppendLine($"> - {w}");
            }
        }

        foreach (var machine in rows.Select(r => r.Machine).Distinct().Order(StringComparer.Ordinal))
        {
            text.AppendLine().AppendLine($"## {machine}").AppendLine();
            text.AppendLine("| Case | Empty | Frame | Frame cost | Budget | Limit |").AppendLine("|---|---|---|---|---|---|");
            foreach (var (label, @case, run, kind) in metrics)
            {
                double? Median(string mode)
                {
                    var values = rows
                        .Where(r => r.Machine == machine && r.Mode == mode && r.Case == @case && (run is null ? double.TryParse(r.Run, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) : r.Run == run))
                        .Select(r => kind.EndsWith("bytes", StringComparison.Ordinal) || kind is "memory" or "size" ? r.Value / Stats.MiB : r.Value)
                        .ToList();
                    return values.Count == 0 ? null : Stats.Median(values);
                }

                var empty = Median("empty");
                var frame = Median("frame");
                if (empty is null && frame is null)
                {
                    continue;
                }

                string? budget = null;
                string? limit = null;
                if (frame is { } f && !kind.StartsWith("none", StringComparison.Ordinal))
                {
                    var b = kind switch
                    {
                        "memory" => Stats.BudgetBytesMemory(f * Stats.MiB) / (double)Stats.MiB,
                        "size" => Stats.BudgetBytesSize(f * Stats.MiB) / (double)Stats.MiB,
                        _ => Stats.BudgetMs(f),
                    };
                    (budget, limit) = (N(b), N(Stats.Limit(b)));
                }

                var cost = empty is { } e && frame is { } g ? N(g - e) : "–";
                text.AppendLine($"| {label} | {(empty is { } e2 ? N(e2) : "–")} | {(frame is { } f2 ? N(f2) : "–")} | {cost} | {budget ?? "–"} | {limit ?? "–"} |");
            }
        }

        return text.ToString();
    }

    private static string N(double value) => value.ToString("0.0#", CultureInfo.InvariantCulture);

    public static IEnumerable<string> Expand(string pattern)
    {
        if (!pattern.Contains('*', StringComparison.Ordinal))
        {
            return [pattern];
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(pattern))!;
        var file = Path.GetFileName(pattern);
        var parts = directory.Split(Path.DirectorySeparatorChar);
        // A wildcard in a folder name (artifacts/measure/*/*.csv): expand the folders first.
        var starIndex = Array.FindIndex(parts, p => p.Contains('*', StringComparison.Ordinal));
        if (starIndex < 0)
        {
            return Directory.EnumerateFiles(directory, file).Order(StringComparer.Ordinal);
        }

        var root = string.Join(Path.DirectorySeparatorChar, parts[..starIndex]) + Path.DirectorySeparatorChar;
        var rest = string.Join(Path.DirectorySeparatorChar, parts[(starIndex + 1)..]);
        return Directory.EnumerateDirectories(root, parts[starIndex])
            .Select(d => Path.Combine(d, rest, file))
            .SelectMany(p => p.Contains('*', StringComparison.Ordinal) ? Directory.EnumerateFiles(Path.GetDirectoryName(p)!, file) : File.Exists(p) ? [p] : [])
            .Order(StringComparer.Ordinal);
    }
}

/// <summary><c>check</c>: the medians of the gallery CSVs against the limits of the speed targets, and the limit table itself.</summary>
static class CheckCommand
{
    public sealed record Result(string Key, string Label, double? Measured, double? Budget, double? Limit, string Verdict);

    // The metrics in the order of the table: key and label with unit (ms or MiB).
    private static readonly (string Key, string Label)[] s_metrics =
    [
        ("cold", "cold (ms)"),
        ("warm", "warm (ms)"),
        ("input", "input median (ms)"),
        ("memory", "memory (MiB)"),
        ("size", "size (MiB)"),
        ("zip", "zip (MiB)"),
    ];

    // THE limit table, the only place with these numbers. The source is the entry "Speed targets of the frame (M1)" in
    // Docs/DECISIONS.md: these are its budgets (the limit is budget x 1.25, see Stats.Limit). Budgets are never loosened
    // here to make a check pass; a change is a new decisions entry. macOS has no budgets (indicative only).
    private static readonly Dictionary<string, Dictionary<string, double>> s_budgets = new()
    {
        ["windows"] = new() { ["cold"] = 430, ["warm"] = 420, ["input"] = 10, ["memory"] = 125, ["size"] = 39, ["zip"] = 16 },
        ["linux"] = new() { ["cold"] = 200, ["warm"] = 210, ["input"] = 20, ["memory"] = 125, ["size"] = 35, ["zip"] = 16 },
        ["macos"] = [],
    };

    // The metrics of the PDF app (target pdf), in the order of the table.
    private static readonly (string Key, string Label)[] s_pdfMetrics =
    [
        ("cold", "cold (ms)"),
        ("warm", "warm (ms)"),
        ("thousand", "1000 pages (ms)"),
        ("second", "second file (ms)"),
        ("scroll", "scroll p99 (ms)"),
        ("memory", "memory (MiB)"),
        ("size", "size (MiB)"),
        ("zip", "zip (MiB)"),
        ("app", "app bundle (MiB)"),
        ("dmg", "disk image (MiB)"),
    ];

    // THE limit table of the PDF app (target pdf), the only place for these numbers. The source is the entry "Raffica PDF speed budgets
    // (M2.1)" in Docs/DECISIONS.md: these are its budgets, the medians of the owner's runs of 2026-11-04 (commit db90246, CSVs in
    // Docs/measurements/pdf-viewer-2026-11-04/), except the Windows ones, which the entry "Raffica PDF: software rendering is the Windows
    // default (M2.1 follow-up)" re-derived from the software rendering medians (rendering/software), rounded up as in M1 (time to 10 ms,
    // memory to 5 MiB, size to 1 MiB); the limit is
    // budget x 1.25 once (Stats.Limit). "scroll" is the median of the p99 of the frame gaps. "zip" has a budget like the gallery's
    // (the size check judges size and zip together; a metric without a budget would be "unrecorded" and fail). Size and zip are the ones of the entry
    // "Raffica PDF: published size budgets after the OCR payload (M2.5)" (the owner chose to count the OCR library and its language data). Never loosen a number
    // here to make a check pass; a change is a new decisions entry. macOS is not exempt (only the gallery is indicative there): its entry holds the
    // four size metrics of the entry "Raffica PDF on macOS: published sizes and the hosted record (M3.3)": the medians of three CI runs of the
    // larger architecture (osx-x64), rounded up to 1 MiB as in M2.5, and they bind both architectures through the one entry. It has NO time and
    // no memory limit on purpose: those bind only the owner's Intel Mac (measure.yml, platform macos), which has not been measured, so such a
    // metric is "unrecorded" (a check that asks for it exits 1) instead of passing; the size check asks only for size, zip, app and dmg.
    // The metrics "app" (bundle bytes) and "dmg" (file bytes) exist for macOS only and are judged only when given (size --app, --dmg, or rows size/app and size/dmg).
    private static readonly Dictionary<string, Dictionary<string, double>> s_pdfBudgets = new()
    {
        ["windows"] = new() { ["cold"] = 200, ["warm"] = 210, ["thousand"] = 200, ["second"] = 20, ["scroll"] = 20, ["memory"] = 40, ["size"] = 66, ["zip"] = 29 },
        ["linux"] = new() { ["cold"] = 230, ["warm"] = 230, ["thousand"] = 220, ["second"] = 40, ["scroll"] = 40, ["memory"] = 155, ["size"] = 65, ["zip"] = 30 },
        ["macos"] = new() { ["size"] = 69, ["zip"] = 32, ["app"] = 76, ["dmg"] = 38 },
    };

    /// <summary>The message of a check of the PDF app against an empty limit table.</summary>
    public const string NoPdfLimits = "no limits recorded for target pdf";

    /// <summary>Selftest only: replaces the production table of the PDF app; null restores it. Never set outside the selftest.</summary>
    public static Dictionary<string, Dictionary<string, double>>? PdfTableForTests { get; set; }

    /// <summary>The limit table of the PDF app in force.</summary>
    public static Dictionary<string, Dictionary<string, double>> PdfTable => PdfTableForTests ?? s_pdfBudgets;

    /// <summary>True when a platform of the PDF app (macOS included) has no limit at all, so a check cannot pass.</summary>
    public static bool PdfHasNoLimits(string platform) =>
        !PdfTable.TryGetValue(platform, out var table) || table.Count == 0;

    /// <summary>The limit (budget x 1.25) of a metric on a platform; null where there is none (macOS).</summary>
    public static double? LimitOf(string platform, string key) => LimitOf(s_budgets, platform, key);

    /// <summary>The limit of a metric in the given table; null where there is none.</summary>
    public static double? LimitOf(Dictionary<string, Dictionary<string, double>> budgets, string platform, string key) =>
        budgets.TryGetValue(platform, out var table) && table.TryGetValue(key, out var budget) ? Stats.Limit(budget) : null;

    /// <summary>The platform of a runtime identifier (<c>win-x64</c>, <c>linux-x64</c>, <c>osx-*</c>); null if unknown.</summary>
    public static string? PlatformOfRid(string rid) =>
        rid.StartsWith("win-", StringComparison.Ordinal) ? "windows"
        : rid.StartsWith("linux-", StringComparison.Ordinal) ? "linux"
        : rid.StartsWith("osx-", StringComparison.Ordinal) ? "macos"
        : null;

    /// <summary>
    /// Verdicts for the measured values (<paramref name="only"/> restricts the metrics): <c>ok</c> (at or below the limit), <c>over</c>,
    /// <c>missing</c> (a required value is not there) or <c>indicative</c> (the gallery on macOS has no limits).
    /// </summary>
    public static List<Result> Judge(string platform, IReadOnlyDictionary<string, double?> measured, params string[] only) =>
        Judge(s_budgets, s_metrics, platform, measured, only, indicativeOnMac: true);

    /// <summary>
    /// The verdicts of the PDF app against <see cref="PdfTable"/>; a platform without a limit for a metric (macOS included) is <c>unrecorded</c>.
    /// <c>app</c> and <c>dmg</c> exist for macOS only and are judged only when named in <paramref name="only"/>.
    /// </summary>
    public static List<Result> JudgePdf(string platform, IReadOnlyDictionary<string, double?> measured, params string[] only) =>
        Judge(PdfTable, [.. s_pdfMetrics.Where(m => m.Key is not ("app" or "dmg") || (platform == "macos" && only.Contains(m.Key)))], platform, measured, only, indicativeOnMac: false);

    private static List<Result> Judge(Dictionary<string, Dictionary<string, double>> budgets, (string Key, string Label)[] metrics, string platform, IReadOnlyDictionary<string, double?> measured, string[] only, bool indicativeOnMac)
    {
        var results = new List<Result>();
        foreach (var (key, label) in metrics.Where(m => only.Length == 0 || only.Contains(m.Key)))
        {
            var value = measured.GetValueOrDefault(key);
            var limit = LimitOf(budgets, platform, key);
            double? budget = limit / Stats.Tolerance;

            // A binding platform whose limit is not recorded must not pass silently; only the gallery on macOS is indicative.
            var verdict = limit is null ? (indicativeOnMac && platform == "macos" ? "indicative" : "unrecorded") : value is null ? "missing" : value > limit ? "over" : "ok";
            results.Add(new Result(key, label, value, budget, limit, verdict));
        }

        return results;
    }

    /// <summary>The medians of the gallery rows (mode <c>gallery</c>), then their verdicts.</summary>
    public static List<Result> Evaluate(string platform, IReadOnlyList<BudgetsCommand.Row> rows)
    {
        double? Median(string @case, string? run) => MedianOf(rows, "gallery", @case, run);

        var measured = new Dictionary<string, double?>
        {
            ["cold"] = Median("cold", null),
            ["warm"] = Median("warm", null),
            ["input"] = Median("input", "median"),
            ["memory"] = Median("memory", null),
            ["size"] = Median("size", "total"),
            ["zip"] = Median("size", "zip"),
        };
        return Judge(platform, measured);
    }

    /// <summary>The medians of the rows of the PDF app (mode <c>pdf</c>; scroll: the p99 of each run), then their verdicts.</summary>
    public static List<Result> EvaluatePdf(string platform, IReadOnlyList<BudgetsCommand.Row> rows)
    {
        double? Median(string @case, string? run) => MedianOf(rows, "pdf", @case, run);

        var measured = new Dictionary<string, double?>
        {
            ["cold"] = Median("cold", null),
            ["warm"] = Median("warm", null),
            ["thousand"] = Median("thousand", null),
            ["second"] = Median("second", null),
            ["scroll"] = Median("scroll", "p99"),
            ["memory"] = Median("memory", null),
            ["size"] = Median("size", "total"),
            ["zip"] = Median("size", "zip"),
        };

        // The bundle and the image are judged only when the rows are there (the CSV of a size run with --app and --dmg).
        List<string> only = [.. s_pdfMetrics.Select(m => m.Key).Where(k => k is not ("app" or "dmg"))];
        foreach (var key in new[] { "app", "dmg" })
        {
            if (Median("size", key) is { } bytes)
            {
                measured[key] = bytes;
                only.Add(key);
            }
        }

        return JudgePdf(platform, measured, [.. only]);
    }

    private static double? MedianOf(IReadOnlyList<BudgetsCommand.Row> rows, string mode, string @case, string? run)
    {
        var values = rows
            .Where(r => r.Mode == mode && r.Case == @case && (run is null ? double.TryParse(r.Run, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) : r.Run == run))
            .Select(r => r.Unit == "bytes" ? r.Value / Stats.MiB : r.Value)
            .ToList();
        return values.Count == 0 ? null : Stats.Median(values);
    }

    /// <summary>1 when any value is over its limit, a required value is missing or a limit is not recorded, else 0.</summary>
    public static int ExitCode(IReadOnlyList<Result> results) => results.Any(r => r.Verdict is "over" or "missing" or "unrecorded") ? 1 : 0;

    /// <summary>The Markdown table: case, measured, budget, limit, verdict.</summary>
    public static string Render(IReadOnlyList<Result> results)
    {
        static string N(double? v) => v is { } x ? x.ToString("0.0#", CultureInfo.InvariantCulture) : "–";
        var text = new StringBuilder("| Case | Measured | Budget | Limit | Verdict |\n|---|---|---|---|---|\n");
        foreach (var r in results)
        {
            text.Append(CultureInfo.InvariantCulture, $"| {r.Label} | {N(r.Measured)} | {N(r.Budget)} | {N(r.Limit)} | {r.Verdict} |\n");
        }

        return text.ToString();
    }

    public static int Execute(Options o)
    {
        if (o.Require("--platform", "--in") is { } missing)
        {
            Console.Error.WriteLine($"{missing} is required.");
            return 2;
        }

        var platform = o.Get("--platform")!;
        if (!s_budgets.ContainsKey(platform))
        {
            Console.Error.WriteLine("--platform must be windows, linux or macos.");
            return 2;
        }

        var target = o.Get("--target") ?? "gallery";
        if (target is not ("gallery" or "pdf"))
        {
            Console.Error.WriteLine("--target must be gallery or pdf.");
            return 2;
        }

        var rows = new List<BudgetsCommand.Row>();
        var warnings = new List<string>();
        var problems = new List<string>();
        foreach (var pattern in o.GetAll("--in"))
        {
            List<string> paths;
            try
            {
                paths = BudgetsCommand.Expand(pattern).ToList();
            }
            catch (DirectoryNotFoundException)
            {
                paths = [];
            }

            if (paths.Count == 0 || paths.Any(p => !File.Exists(p)))
            {
                problems.Add($"no CSV found for \"{pattern}\"");
            }

            foreach (var path in paths.Where(File.Exists))
            {
                if (File.Exists(RunCommand.WarningPath(path)))
                {
                    warnings.Add($"{Path.GetFileName(path)}: {File.ReadAllText(RunCommand.WarningPath(path)).Trim()}");
                }

                foreach (var line in File.ReadLines(path).Skip(1).Where(l => l.Length > 0))
                {
                    var p = line.Split(',');
                    if (p.Length != 6 || !double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                    {
                        problems.Add($"unreadable CSV row in {path}: {line}");
                        continue;
                    }

                    rows.Add(new BudgetsCommand.Row(p[0], p[1], p[2], p[3], value, p[5]));
                }
            }
        }

        var pdf = target == "pdf";
        var results = pdf ? EvaluatePdf(platform, rows) : Evaluate(platform, rows);
        var text = new StringBuilder(pdf ? $"## PDF app measurement ({platform})\n\n" : $"## Frame measurement ({platform})\n\n");
        if (platform == "macos" && !pdf)
        {
            text.AppendLine("macOS has no limits; the values are indicative only.").AppendLine();
        }

        var noLimits = pdf && PdfHasNoLimits(platform);
        if (noLimits)
        {
            text.AppendLine($"> **{NoPdfLimits}:** the medians below are the input for the budgets of milestone 2.1; they are not checked against anything yet.").AppendLine();
        }

        text.Append(Render(results));
        foreach (var w in warnings)
        {
            text.AppendLine().AppendLine($"> **Invalid measurement:** {w}");
        }

        foreach (var problem in problems)
        {
            text.AppendLine().AppendLine($"> **Problem:** {problem}");
        }

        Console.Write(text.ToString());
        if (o.Get("--summary") is { } summary)
        {
            File.AppendAllText(Path.GetFullPath(summary), text.ToString(), new UTF8Encoding(false));
        }

        if (noLimits)
        {
            // Nothing to check against: never a pass. The medians above are still printed and written to the summary.
            Console.Error.WriteLine(NoPdfLimits);
            return 2;
        }

        // A missing or unreadable CSV fails the check on every platform that has limits.
        var failed = ExitCode(results) == 1 || ((platform != "macos" || pdf) && problems.Count > 0);
        Console.WriteLine(failed ? "check: FAILED (a value is over its limit, a limit is not recorded or a CSV is missing)" : "check: ok");
        return failed ? 1 : 0;
    }
}

static class SelfTest
{
    private static int s_checks;
    private static int s_failures;

    public static int Run()
    {
        Expect("median odd", Stats.Median([5, 1, 3]), 3);
        Expect("median even", Stats.Median([4, 1, 3, 2]), 2.5);
        Expect("p99 of 1..100", Stats.NearestRankP99(Enumerable.Range(1, 100).Select(i => (double)i).ToArray()), 99);
        Expect("budget ms rounds up to 10", Stats.BudgetMs(201.3), 210);
        Expect("budget ms exact", Stats.BudgetMs(200.0), 200);
        Expect("budget memory", Stats.BudgetBytesMemory(52.1 * Stats.MiB), 55 * Stats.MiB);
        Expect("budget size", Stats.BudgetBytesSize(30.2 * Stats.MiB), 31 * Stats.MiB);
        Expect("limit", Stats.Limit(210), 262.5);

        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Expect("csv under de-DE", Lines.Csv("pc", "frame", "warm", "1", 201.3, "ms"), "pc,frame,warm,1,201.3,ms");
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }

        Expect("parse input line", Lines.TryParseInput("input median-ms=12.50 p99-ms=30.25 max-ms=41.00", out var med, out var p99, out var max) ? string.Create(CultureInfo.InvariantCulture, $"{med}|{p99}|{max}") : "-", "12.5|30.25|41");
        Expect("parse input line without values", Lines.TryParseInput("input median-ms=x", out _, out _, out _), false);
        Expect("parse other line as input", Lines.TryParseInput("first-frame", out _, out _, out _), false);
        Expect("parse memory line", Lines.TryParseMemory("memory-bytes 77565952", out var bytes) ? bytes : -1, 77565952);
        Expect("parse memory line without value", Lines.TryParseMemory("memory-bytes ", out _), false);

        var hang = FakeProbe.Create("hang");
        Runner.Timeout = TimeSpan.FromSeconds(2);
        var watch = Stopwatch.StartNew();
        var (output, lineMs, error) = Runner.RunOnce(hang, []);
        Expect("a probe that never prints first-frame fails the run", output is null && lineMs is null && error is not null, true);
        Expect("it is killed after the timeout", watch.Elapsed < TimeSpan.FromSeconds(20), true);

        var csvPath = Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "hang.csv");
        var exit = RunCommand.Execute(Options.Parse(["--exe", hang, "--mode", "frame", "--case", "warm", "--out", csvPath, "--machine", "selftest"], out _)!);
        Expect("the run command exits with 1 for a failed run", exit, 1);
        Runner.Timeout = TimeSpan.FromSeconds(45);

        Expect("label defaults to the case", RunCommand.Label(Options.Parse(["--case", "cold"], out _)!), "cold");
        Expect("label overrides the case name in the CSV", RunCommand.Label(Options.Parse(["--case", "cold", "--label", "cold-reboot"], out _)!), "cold-reboot");

        Expect("runs default", RunCommand.RunCount(Options.Parse(["--case", "cold"], out _)!, 10), 10);
        Expect("--runs 1 for the cold run after a reboot", RunCommand.RunCount(Options.Parse(["--runs", "1"], out _)!, 10), 1);
        Expect("warning sidecar sits next to the csv", RunCommand.WarningPath("out/cold.csv"), "out/cold.csv.warning.txt");
        Expect("budgets carry the warning of an invalid machine", BudgetsCommand.Render([new("pc", "frame", "cold", "1", 200, "ms")], ["cold-frame.csv: Warning: slow"]).Contains("cold-frame.csv: Warning: slow", StringComparison.Ordinal), true);

        var sizeFolder = Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "size");
        var fontFolder = sizeFolder + "-fonts";
        Directory.CreateDirectory(sizeFolder);
        Directory.CreateDirectory(fontFolder);
        File.WriteAllBytes(Path.Combine(sizeFolder, "ShellProbe.exe"), new byte[100]);
        File.WriteAllBytes(Path.Combine(sizeFolder, "ShellProbe.pdb"), new byte[50]);
        File.WriteAllBytes(Path.Combine(sizeFolder, "lib.dll"), new byte[30]);
        File.WriteAllBytes(Path.Combine(sizeFolder, "lib.pdb"), new byte[20]);
        Directory.CreateDirectory(Path.Combine(sizeFolder, "ShellProbe.dSYM", "Contents"));
        File.WriteAllBytes(Path.Combine(sizeFolder, "ShellProbe.dSYM", "Contents", "dwarf"), new byte[40]);
        File.WriteAllBytes(Path.Combine(fontFolder, "a.ttf"), new byte[7]);
        var (total, _, fontBytes, native) = SizeCommand.Measure(sizeFolder, fontFolder);
        Expect("size: debug symbols are not shipped", total, 130);
        Expect("size: native is everything but the main executable", native, 30);
        Expect("size: fonts", fontBytes, 7);

        var rows = new List<BudgetsCommand.Row>
        {
            new("pc", "empty", "cold", "1", 170, "ms"), new("pc", "empty", "cold", "2", 190, "ms"),
            new("pc", "frame", "cold", "1", 201.3, "ms"),
            new("pc", "empty", "memory", "1", 40 * Stats.MiB, "bytes"), new("pc", "frame", "memory", "1", 52.1 * Stats.MiB, "bytes"),
            new("pc", "frame", "size", "total", 30.2 * Stats.MiB, "bytes"),
            new("pc", "frame", "input", "median", 18.5, "ms"), new("pc", "frame", "input", "p99", 30, "ms"),
            new("mac", "frame", "warm", "1", 400, "ms"),
        };
        var markdown = BudgetsCommand.Render(rows);
        Expect("budgets: time row", markdown.Contains("| cold (ms) | 180.0 | 201.3 | 21.3 | 210.0 | 262.5 |", StringComparison.Ordinal), true);
        Expect("budgets: memory row", markdown.Contains("| memory (MiB) | 40.0 | 52.1 | 12.1 | 55.0 | 68.75 |", StringComparison.Ordinal), true);
        Expect("budgets: size row", markdown.Contains("| size total (MiB) | – | 30.2 | – | 31.0 | 38.75 |", StringComparison.Ordinal), true);
        Expect("budgets: input row", markdown.Contains("| input median (ms) | – | 18.5 | – | 20.0 | 25.0 |", StringComparison.Ordinal), true);
        Expect("budgets: one section per machine", markdown.Contains("## pc", StringComparison.Ordinal) && markdown.Contains("## mac", StringComparison.Ordinal), true);
        Expect("budgets: no frame value, no budget", markdown.Contains("| cold (ms) | – | – | – | – | – |", StringComparison.Ordinal), false);

        GalleryRunTests();
        CheckTests();
        SizeCheckTests();
        PdfTests();
        EveryCommandAcceptsThePdfTarget();

        Console.WriteLine($"selftest: {s_checks - s_failures}/{s_checks} checks passed");
        return s_failures == 0 ? 0 : 1;
    }

    private static readonly string[] s_checkKeys = ["cold", "warm", "input", "memory", "size", "zip"];
    private const double MiBd = 1024 * 1024;

    /// <summary>The arguments of a gallery run and the CSV mode: no <c>--mode</c>, the frame view, the case switch and a settings file.</summary>
    private static void GalleryRunTests()
    {
        var args = RunCommand.Arguments("gallery", null, "warm", "s.json");
        Expect("gallery run: arguments", string.Join(' ', args), "--view frame --measure-startup --settings-file s.json");
        Expect("gallery run: input case switch", RunCommand.Arguments("gallery", null, "input", "s.json")[2], "--measure-input");
        Expect("gallery run: no --mode", args.Contains("--mode"), false);
        Expect("probe run: arguments stay", string.Join(' ', RunCommand.Arguments("probe", "frame", "memory", "unused")), "--mode frame --measure-memory");
        Expect("gallery run: CSV mode is gallery", RunCommand.CsvMode("gallery", null), "gallery");
        Expect("probe run: CSV mode is the mode", RunCommand.CsvMode("probe", "empty"), "empty");

        var fake = FakeProbe.Create("hang");
        var exit = RunCommand.Execute(Options.Parse(["--exe", fake, "--target", "gallery", "--mode", "frame", "--case", "warm", "--out", Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "g.csv")], out _)!);
        Expect("gallery run: --mode is a usage error", exit, 2);
        exit = RunCommand.Execute(Options.Parse(["--exe", fake, "--target", "other", "--case", "warm", "--out", "x.csv"], out _)!);
        Expect("run: unknown target is a usage error", exit, 2);
    }

    /// <summary>Gallery rows with every metric at <c>limit + delta</c> of the platform; <paramref name="only"/> restricts the delta to one metric.</summary>
    private static List<BudgetsCommand.Row> GalleryRows(string platform, string? only = null, double delta = 0, string mode = "gallery")
    {
        double At(string key, double fallbackLimit)
        {
            var limit = CheckCommand.LimitOf(platform, key) ?? fallbackLimit;
            return limit + (only is null || only == key ? delta : 0);
        }

        // Three runs around the median: the outliers must not matter, the median is the value.
        var cold = At("cold", 1000);
        return
        [
            new("pc", mode, "cold", "1", 99999, "ms"), new("pc", mode, "cold", "2", cold, "ms"), new("pc", mode, "cold", "3", 1, "ms"),
            new("pc", mode, "warm", "1", At("warm", 1000), "ms"),
            new("pc", mode, "input", "median", At("input", 100), "ms"), new("pc", mode, "input", "p99", 999, "ms"), new("pc", mode, "input", "max", 999, "ms"),
            new("pc", mode, "memory", "1", At("memory", 1000) * MiBd, "bytes"),
            new("pc", mode, "size", "total", At("size", 1000) * MiBd, "bytes"),
            new("pc", mode, "size", "zip", At("zip", 1000) * MiBd, "bytes"),
        ];
    }

    private static int ExitOf(string platform, List<BudgetsCommand.Row> rows) => CheckCommand.ExitCode(CheckCommand.Evaluate(platform, rows));

    private static void CheckTests()
    {
        Expect("limit table windows cold", CheckCommand.LimitOf("windows", "cold"), 537.5);
        Expect("limit table windows warm", CheckCommand.LimitOf("windows", "warm"), 525);
        Expect("limit table windows input", CheckCommand.LimitOf("windows", "input"), 12.5);
        Expect("limit table windows memory", CheckCommand.LimitOf("windows", "memory"), 156.25);
        Expect("limit table windows size", CheckCommand.LimitOf("windows", "size"), 48.75);
        Expect("limit table windows zip", CheckCommand.LimitOf("windows", "zip"), 20);
        Expect("limit table linux cold", CheckCommand.LimitOf("linux", "cold"), 250);
        Expect("limit table linux warm", CheckCommand.LimitOf("linux", "warm"), 262.5);
        Expect("limit table linux input", CheckCommand.LimitOf("linux", "input"), 25);
        Expect("limit table linux memory", CheckCommand.LimitOf("linux", "memory"), 156.25);
        Expect("limit table linux size", CheckCommand.LimitOf("linux", "size"), 43.75);
        Expect("limit table linux zip", CheckCommand.LimitOf("linux", "zip"), 20);
        Expect("limit table macos has no limit", CheckCommand.LimitOf("macos", "cold"), null);

        foreach (var platform in new[] { "windows", "linux" })
        {
            Expect($"check {platform}: every value exactly at its limit passes", ExitOf(platform, GalleryRows(platform)), 0);
            Expect($"check {platform}: verdicts at the limit are ok", CheckCommand.Evaluate(platform, GalleryRows(platform)).All(r => r.Verdict == "ok"), true);
            foreach (var key in s_checkKeys)
            {
                // 0.1 above (ms, MiB): a single value above its limit fails the whole check.
                var results = CheckCommand.Evaluate(platform, GalleryRows(platform, key, 0.1));
                Expect($"check {platform}: {key} 0.1 above its limit fails", ExitOf(platform, GalleryRows(platform, key, 0.1)), 1);
                Expect($"check {platform}: only {key} is over", string.Join(',', results.Where(r => r.Verdict == "over").Select(r => r.Key)), key);
                Expect($"check {platform}: {key} 0.1 below its limit passes", ExitOf(platform, GalleryRows(platform, key, -0.1)), 0);
            }
        }

        Expect("check: probe-mode rows do not count as gallery rows", ExitOf("windows", GalleryRows("windows", mode: "frame")), 1);
        var results2 = CheckCommand.Evaluate("windows", GalleryRows("windows", mode: "frame"));
        Expect("check: no gallery rows gives the verdict missing", results2.All(r => r.Verdict == "missing"), true);
        Expect("check: a missing metric fails", ExitOf("linux", GalleryRows("linux").Where(r => r.Case != "memory").ToList()), 1);
        Expect("check: macOS is indicative, whatever the values", CheckCommand.Evaluate("macos", GalleryRows("macos", delta: 5000)).All(r => r.Verdict == "indicative"), true);
        Expect("check: macOS exits 0 with huge values", ExitOf("macos", GalleryRows("macos", delta: 5000)), 0);
        Expect("check: macOS exits 0 without rows", ExitOf("macos", []), 0);

        // The command with files: a glob, the summary, a missing CSV.
        var folder = Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "check");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        var summary = Path.Combine(folder, "summary.md");
        var csv = Path.Combine(folder, "all.csv");
        File.WriteAllText(csv, Lines.CsvHeader + "\n" + string.Concat(GalleryRows("windows").Select(r => Lines.Csv(r.Machine, r.Mode, r.Case, r.Run, r.Value, r.Unit) + "\n")));
        var exit = CheckCommand.Execute(Options.Parse(["--platform", "windows", "--in", Path.Combine(folder, "*.csv"), "--summary", summary], out _)!);
        Expect("check command: CSVs at the limit exit 0", exit, 0);
        var text = File.Exists(summary) ? File.ReadAllText(summary) : string.Empty;
        Expect("check command: summary has the table", text.Contains("| cold (ms) | 537.5 | 430.0 | 537.5 | ok |", StringComparison.Ordinal), true);

        File.WriteAllText(csv, Lines.CsvHeader + "\n" + string.Concat(GalleryRows("windows", "cold", 0.1).Select(r => Lines.Csv(r.Machine, r.Mode, r.Case, r.Run, r.Value, r.Unit) + "\n")));
        Expect("check command: a value 0.1 above exits 1", CheckCommand.Execute(Options.Parse(["--platform", "windows", "--in", csv], out _)!), 1);
        foreach (var bad in new[] { "NaN", "Infinity", "-Infinity" })
        {
            // A value that is not a finite number is an unreadable row (NaN compares as "not over" and would pass silently).
            File.WriteAllText(csv, Lines.CsvHeader + "\n" + string.Concat(GalleryRows("windows").Select(r => Lines.Csv(r.Machine, r.Mode, r.Case, r.Run, r.Value, r.Unit) + "\n")) + $"pc,gallery,warm,9,{bad},ms\n");
            Expect($"check command: a {bad} row exits 1", CheckCommand.Execute(Options.Parse(["--platform", "windows", "--in", csv], out _)!), 1);
        }

        File.WriteAllText(csv, Lines.CsvHeader + "\n" + string.Concat(GalleryRows("windows").Select(r => Lines.Csv(r.Machine, r.Mode, r.Case, r.Run, r.Value, r.Unit) + "\n")));
        Expect("check command: a missing CSV exits 1", CheckCommand.Execute(Options.Parse(["--platform", "windows", "--in", Path.Combine(folder, "nope", "*.csv")], out _)!), 1);
        Expect("check command: a missing file exits 1", CheckCommand.Execute(Options.Parse(["--platform", "windows", "--in", Path.Combine(folder, "nope.csv")], out _)!), 1);
        Expect("check command: unknown platform is a usage error", CheckCommand.Execute(Options.Parse(["--platform", "amiga", "--in", csv], out _)!), 2);
        Expect("check command: --platform is required", CheckCommand.Execute(Options.Parse(["--in", csv], out _)!), 2);
    }

    private static readonly string[] s_pdfKeys = ["cold", "warm", "thousand", "second", "scroll", "memory", "size", "zip"];

    // TEST-ONLY numbers to exercise the verdict logic; they are budgets of nothing. The recorded budgets are s_pdfBudgets in CheckCommand.
    private static Dictionary<string, Dictionary<string, double>> PdfTestTable() => new()
    {
        ["windows"] = new() { ["cold"] = 100, ["warm"] = 80, ["thousand"] = 200, ["second"] = 40, ["scroll"] = 30, ["memory"] = 120, ["size"] = 40, ["zip"] = 16 },
        ["linux"] = new() { ["cold"] = 120, ["warm"] = 90, ["thousand"] = 240, ["second"] = 50, ["scroll"] = 35, ["memory"] = 110, ["size"] = 36, ["zip"] = 16 },
        ["macos"] = new() { ["cold"] = 150, ["warm"] = 100, ["thousand"] = 250, ["second"] = 60, ["scroll"] = 40, ["memory"] = 130, ["size"] = 45, ["zip"] = 18, ["app"] = 20, ["dmg"] = 10 },
    };

    /// <summary>Rows of the PDF app with every metric at <c>limit + delta</c> of the platform in the test table; <paramref name="only"/> restricts the delta to one metric, <paramref name="without"/> leaves a metric out.</summary>
    private static List<BudgetsCommand.Row> PdfRows(string platform, string? only = null, double delta = 0, string? without = null)
    {
        double At(string key) => (CheckCommand.LimitOf(CheckCommand.PdfTable, platform, key) ?? 1000) + (only is null || only == key ? delta : 0);

        List<BudgetsCommand.Row> rows =
        [
            new("pc", "pdf", "cold", "1", 99999, "ms"), new("pc", "pdf", "cold", "2", At("cold"), "ms"), new("pc", "pdf", "cold", "3", 1, "ms"),
            new("pc", "pdf", "warm", "1", At("warm"), "ms"),
            new("pc", "pdf", "thousand", "1", At("thousand"), "ms"),
            new("pc", "pdf", "second", "1", At("second"), "ms"),
            new("pc", "pdf", "scroll", "p99", At("scroll"), "ms"), new("pc", "pdf", "scroll", "max", 999, "ms"),
            new("pc", "pdf", "memory", "1", At("memory") * MiBd, "bytes"),
            new("pc", "pdf", "size", "total", At("size") * MiBd, "bytes"),
            new("pc", "pdf", "size", "zip", At("zip") * MiBd, "bytes"),
        ];
        return without is null ? rows : [.. rows.Where(r => without == "size" ? !(r.Case == "size" && r.Run == "total") : without == "zip" ? !(r.Case == "size" && r.Run == "zip") : r.Case != without)];
    }

    private static string PdfCsv(string path, List<BudgetsCommand.Row> rows)
    {
        File.WriteAllText(path, Lines.CsvHeader + "\n" + string.Concat(rows.Select(r => Lines.Csv(r.Machine, r.Mode, r.Case, r.Run, r.Value, r.Unit) + "\n")));
        return path;
    }

    /// <summary>The target pdf: the arguments of a run, the CSV mode, the recorded production table, the empty-table behaviour (exit 2) and the verdicts with a test-only table.</summary>
    private static void PdfTests()
    {
        var td = Path.Combine("some", "testdata");
        Expect("pdf run: arguments", string.Join(' ', PdfRun.Arguments("warm", "s.json", td)), $"--measure-startup --settings-file s.json {Path.Combine(td, "hello_world.pdf")}");
        Expect("pdf run: cold uses the same file as warm", PdfRun.Arguments("cold", "s.json", td)[3], PdfRun.Arguments("warm", "s.json", td)[3]);
        Expect("pdf run: thousand opens the 1000-page file", PdfRun.Arguments("thousand", "s.json", td)[3], Path.Combine(td, "thousand-pages.pdf"));
        Expect("pdf run: scroll switch", PdfRun.Arguments("scroll", "s.json", td)[0], "--measure-scroll");
        Expect("pdf run: memory switch", PdfRun.Arguments("memory", "s.json", td)[0], "--measure-memory");
        Expect("pdf run: second switch", PdfRun.Arguments("second", "s.json", td)[0], "--measure-second");
        Expect("pdf run: the second launch shares the settings file", string.Join(' ', PdfRun.SecondArguments("s.json", td)), $"--settings-file s.json {Path.Combine(td, PdfRun.SecondFile)}");
        Expect("pdf run: every run has a settings file of its own", PdfRun.Cases.All(c => PdfRun.Arguments(c.Name, "s.json", td).Contains("--settings-file")), true);
        Expect("pdf run: the cases", string.Join(',', PdfRun.Cases.Select(c => c.Name)), "cold,warm,thousand,second,scroll,memory");
        Expect("pdf run: CSV mode is pdf", RunCommand.CsvMode("pdf", null), "pdf");
        Expect("parse scroll line", Lines.TryParseScroll("scroll p99-ms=33.25 max-ms=50.00", out var p99, out var max) ? string.Create(CultureInfo.InvariantCulture, $"{p99}|{max}") : "-", "33.25|50");
        Expect("parse scroll line without values", Lines.TryParseScroll("scroll p99-ms=x max-ms=1", out _, out _), false);
        Expect("parse second line", Lines.TryParseSecond("second-page ms=12.34", out var ms) ? ms : -1, 12.34);
        Expect("parse first-page as second line", Lines.TryParseSecond("first-page", out _), false);

        var fake = FakeProbe.Create("hang");
        var csvPath = Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "p.csv");
        Expect("pdf run: --mode is a usage error", RunCommand.Execute(Options.Parse(["--target", "pdf", "--exe", fake, "--mode", "frame", "--case", "warm", "--out", csvPath], out _)!), 2);
        Expect("pdf run: an unknown case is a usage error", RunCommand.Execute(Options.Parse(["--target", "pdf", "--exe", fake, "--case", "input", "--out", csvPath], out _)!), 2);
        Expect("pdf run: a missing test folder is a usage error", RunCommand.Execute(Options.Parse(["--target", "pdf", "--exe", fake, "--case", "warm", "--testdata", Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "nope"), "--out", csvPath], out _)!), 2);

        // The production table holds the budgets recorded from the owner's runs of 2026-11-04 (DECISIONS.md, "Raffica PDF speed budgets (M2.1)"), except size and zip, which "Raffica PDF: published size budgets after the OCR payload (M2.5)" replaced: pinned value by value.
        (string Platform, string Key, double Budget)[] recorded =
        [
            ("windows", "cold", 200), ("windows", "warm", 210), ("windows", "thousand", 200), ("windows", "second", 20), ("windows", "scroll", 20), ("windows", "memory", 40), ("windows", "size", 66), ("windows", "zip", 29),
            ("linux", "cold", 230), ("linux", "warm", 230), ("linux", "thousand", 220), ("linux", "second", 40), ("linux", "scroll", 40), ("linux", "memory", 155), ("linux", "size", 65), ("linux", "zip", 30),
            ("macos", "size", 69), ("macos", "zip", 32), ("macos", "app", 76), ("macos", "dmg", 38),
        ];
        foreach (var (platform, key, budget) in recorded)
        {
            Expect($"pdf limits: {platform} {key} is budget {budget} x 1.25", CheckCommand.LimitOf(CheckCommand.PdfTable, platform, key), budget * 1.25);
        }

        Expect("pdf limits: windows has limits", CheckCommand.PdfHasNoLimits("windows"), false);
        Expect("pdf limits: linux has limits", CheckCommand.PdfHasNoLimits("linux"), false);
        Expect("pdf limits: every binding metric is recorded", CheckCommand.PdfTable["windows"].Count == s_pdfKeys.Length && CheckCommand.PdfTable["linux"].Count == s_pdfKeys.Length, true);
        Expect("pdf limits: macOS has limits", CheckCommand.PdfHasNoLimits("macos"), false);
        Expect("pdf limits: macOS holds the four size metrics and no time or memory limit (not measured on an Intel Mac yet)", string.Join(',', CheckCommand.PdfTable["macos"].Keys.Order()), "app,dmg,size,zip");
        foreach (var key in new[] { "cold", "warm", "thousand", "second", "scroll", "memory" })
        {
            Expect($"pdf limits: macOS {key} stays unrecorded", CheckCommand.JudgePdf("macos", new Dictionary<string, double?> { [key] = 1 }, key)[0].Verdict, "unrecorded");
        }

        // The committed first measurements (CSVs of the owner's runs) are inside the recorded limits: the same files `check` is run on.
        RecordedMeasurementsTests();

        var folder = Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "pdfcheck");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        var csv = Path.Combine(folder, "pdf.csv");
        var summary = Path.Combine(folder, "summary.md");

        // A table without limits (test-only, the mechanism stays) must not pass any numbers, not even values of zero.
        CheckCommand.PdfTableForTests = new() { ["windows"] = [], ["linux"] = [], ["macos"] = [] };
        PdfCsv(csv, PdfRows("windows"));
        foreach (var platform in new[] { "windows", "linux" })
        {
            Expect($"check pdf {platform}: an empty table exits 2", CheckCommand.Execute(Options.Parse(["--target", "pdf", "--platform", platform, "--in", csv, "--summary", summary], out _)!), 2);
        }

        var text = File.Exists(summary) ? File.ReadAllText(summary) : string.Empty;
        Expect("check pdf: the summary names the missing limits", text.Contains(CheckCommand.NoPdfLimits, StringComparison.Ordinal), true);
        Expect("check pdf: the summary still has the medians", text.Contains("| 1000 pages (ms) |", StringComparison.Ordinal), true);
        Expect("check pdf macos: an empty table exits 2", CheckCommand.Execute(Options.Parse(["--target", "pdf", "--platform", "macos", "--in", csv], out _)!), 2);
        Expect("check gallery macos: still indicative with an empty table", CheckCommand.Execute(Options.Parse(["--platform", "macos", "--in", csv], out _)!), 0);
        Expect("check: an unknown target is a usage error", CheckCommand.Execute(Options.Parse(["--target", "amiga", "--platform", "windows", "--in", csv], out _)!), 2);
        CheckCommand.PdfTableForTests = null;

        // With a TEST-ONLY table the verdicts are the ones of the gallery: at the limit ok, above it over, missing fails, macOS indicative.
        CheckCommand.PdfTableForTests = PdfTestTable();
        try
        {
            Expect("pdf limits (test table): windows cold", CheckCommand.LimitOf(CheckCommand.PdfTable, "windows", "cold"), 125);
            foreach (var platform in new[] { "windows", "linux", "macos" })
            {
                Expect($"check pdf {platform}: every value exactly at its limit passes", CheckCommand.ExitCode(CheckCommand.EvaluatePdf(platform, PdfRows(platform))), 0);
                Expect($"check pdf {platform}: verdicts at the limit are ok", CheckCommand.EvaluatePdf(platform, PdfRows(platform)).All(r => r.Verdict == "ok"), true);
                foreach (var key in s_pdfKeys)
                {
                    var over = CheckCommand.EvaluatePdf(platform, PdfRows(platform, key, 0.1));
                    Expect($"check pdf {platform}: {key} 0.1 above its limit fails", CheckCommand.ExitCode(over), 1);
                    Expect($"check pdf {platform}: only {key} is over", string.Join(',', over.Where(r => r.Verdict == "over").Select(r => r.Key)), key);
                    Expect($"check pdf {platform}: {key} 0.1 below its limit passes", CheckCommand.ExitCode(CheckCommand.EvaluatePdf(platform, PdfRows(platform, key, -0.1))), 0);

                    var gone = CheckCommand.EvaluatePdf(platform, PdfRows(platform, without: key));
                    Expect($"check pdf {platform}: a missing {key} fails", string.Join(',', gone.Where(r => r.Verdict == "missing").Select(r => r.Key)), key);
                    Expect($"check pdf {platform}: a missing {key} exits 1", CheckCommand.ExitCode(gone), 1);
                }
            }

            Expect("check pdf: the scroll metric is the p99 of the rows, not the maximum", CheckCommand.EvaluatePdf("windows", PdfRows("windows")).First(r => r.Key == "scroll").Measured, 37.5);
            Expect("check pdf macos: huge values are over, not indicative", CheckCommand.EvaluatePdf("macos", PdfRows("macos", delta: 500)).All(r => r.Verdict == "over"), true);
            Expect("check pdf macos: exit 1 with huge values", CheckCommand.ExitCode(CheckCommand.EvaluatePdf("macos", PdfRows("macos", delta: 500))), 1);
            Expect("check pdf windows: no app and dmg metric", CheckCommand.EvaluatePdf("windows", PdfRows("windows")).Any(r => r.Key is "app" or "dmg"), false);
            Expect("check pdf macos: app and dmg are judged only with rows", CheckCommand.EvaluatePdf("macos", PdfRows("macos")).Any(r => r.Key is "app" or "dmg"), false);
            var withBundle = PdfRows("macos");
            withBundle.Add(new("pc", "pdf", "size", "app", 25 * MiBd, "bytes"));
            withBundle.Add(new("pc", "pdf", "size", "dmg", 12.5 * MiBd + 1, "bytes"));
            var bundleVerdicts = CheckCommand.EvaluatePdf("macos", withBundle);
            Expect("check pdf macos: the app row at its limit is ok", bundleVerdicts.First(r => r.Key == "app").Verdict, "ok");
            Expect("check pdf macos: the dmg row one byte above is over", bundleVerdicts.First(r => r.Key == "dmg").Verdict, "over");
            Expect("check pdf macos: the size rows keep their verdicts next to app and dmg", bundleVerdicts.Count(r => r.Verdict == "ok"), s_pdfKeys.Length + 1);

            PdfCsv(csv, PdfRows("windows"));
            Expect("check command pdf: CSVs at the limit exit 0", CheckCommand.Execute(Options.Parse(["--target", "pdf", "--platform", "windows", "--in", csv], out _)!), 0);
            PdfCsv(csv, PdfRows("windows", "warm", 0.1));
            Expect("check command pdf: a value 0.1 above exits 1", CheckCommand.Execute(Options.Parse(["--target", "pdf", "--platform", "windows", "--in", csv], out _)!), 1);
            PdfCsv(csv, PdfRows("windows"));
            Expect("check command pdf: rows of the PDF app do not count for the gallery", CheckCommand.Execute(Options.Parse(["--target", "gallery", "--platform", "windows", "--in", csv], out _)!), 1);

            // A binding platform with a table that lacks a metric is "unrecorded", never a silent pass.
            CheckCommand.PdfTableForTests = new() { ["windows"] = new() { ["cold"] = 100 }, ["linux"] = [], ["macos"] = [] };
            var partial = CheckCommand.EvaluatePdf("windows", PdfRows("windows"));
            Expect("check pdf: a metric without a recorded limit is unrecorded", partial.Count(r => r.Verdict == "unrecorded"), s_pdfKeys.Length - 1);
            Expect("check pdf: unrecorded fails", CheckCommand.ExitCode(partial), 1);

            CheckCommand.PdfTableForTests = PdfTestTable();
            PdfSizeCheckTests();
        }
        finally
        {
            CheckCommand.PdfTableForTests = null;
        }

        Expect("pdf limits: the test table is gone again", CheckCommand.LimitOf(CheckCommand.PdfTable, "windows", "cold"), 250);
    }

    /// <summary>
    /// The committed CSVs of the PDF measurements (<c>Docs/measurements/pdf-viewer-2026-11-04</c>, found from the working folder upwards) pass the
    /// recorded limits on both binding platforms: Linux with <c>linux</c> (first run), Windows with <c>rendering/software</c> (the run of the
    /// software rendering path, which is the app's default; the first run in <c>windows</c> was measured on Avalonia's default path and is history), through the same <c>check</c> command and through the evaluation: a regression of the table
    /// or of the files is a failed selftest.
    /// </summary>
    private static void RecordedMeasurementsTests()
    {
        string? folder = null;
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null && folder is null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Docs", "measurements", "pdf-viewer-2026-11-04");
            folder = Directory.Exists(candidate) ? candidate : null;
        }

        Expect("pdf measurements: the committed CSVs are found (run the selftest from the repository)", folder is not null, true);
        if (folder is null)
        {
            return;
        }

        foreach (var (platform, subfolder) in new[] { ("windows", Path.Combine("rendering", "software")), ("linux", "linux") })
        {
            var pattern = Path.Combine(folder, subfolder, "*.csv");
            Expect($"pdf measurements {platform}: check exits 0 on the committed CSVs", CheckCommand.Execute(Options.Parse(["--target", "pdf", "--platform", platform, "--in", pattern], out _)!), 0);

            var rows = new List<BudgetsCommand.Row>();
            foreach (var path in BudgetsCommand.Expand(pattern))
            {
                foreach (var line in File.ReadLines(path).Skip(1).Where(l => l.Length > 0))
                {
                    var p = line.Split(',');
                    rows.Add(new BudgetsCommand.Row(p[0], p[1], p[2], p[3], double.Parse(p[4], CultureInfo.InvariantCulture), p[5]));
                }
            }

            var results = CheckCommand.EvaluatePdf(platform, rows);
            Expect($"pdf measurements {platform}: every metric is judged ok", results.All(r => r.Verdict == "ok") && results.Count == s_pdfKeys.Length, true);

            // The budget is the median rounded up, so no median is above its budget.
            Expect($"pdf measurements {platform}: no median is above its budget", results.All(r => r.Measured <= r.Budget), true);
        }
    }

    /// <summary>
    /// The CI step "Size of the published PDF app" calls <c>size --target pdf ... --out</c>, a path that only the gallery had a test for.
    /// Every subcommand that takes <c>--target</c> must accept <c>pdf</c> in its argument check and refuse an unknown target with the
    /// message that names the allowed ones; this pins all of them (run, size with and without --check, check).
    /// </summary>
    private static void EveryCommandAcceptsThePdfTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "pdftarget");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        var publish = Path.Combine(root, "publish");
        var fonts = Path.Combine(root, "fonts");
        Directory.CreateDirectory(publish);
        Directory.CreateDirectory(fonts);
        File.WriteAllBytes(Path.Combine(publish, "RafficaPdf.exe"), new byte[10]);
        File.WriteAllBytes(Path.Combine(fonts, "a.ttf"), new byte[7]);
        var csv = Path.Combine(root, "in.csv");
        PdfCsv(csv, PdfRows("windows"));
        var fake = FakeProbe.Create("hang");

        // The size CSV of the CI step (no --check, with --out): exit 0 and rows of the mode pdf.
        var sizeCsv = Path.Combine(root, "size.csv");
        Expect("size --target pdf --out: exit 0", SizeCommand.Execute(Options.Parse(["--target", "pdf", "--publish-dir", publish, "--fonts-dir", fonts, "--out", sizeCsv], out _)!), 0);
        var sizeRows = File.Exists(sizeCsv) ? File.ReadAllLines(sizeCsv) : [];
        Expect("size --target pdf --out: the rows have the mode pdf", sizeRows.Length > 1 && sizeRows.Skip(1).All(r => r.Contains(",pdf,size,", StringComparison.Ordinal)), true);

        var commands = new (string Name, Func<string, int> Run)[]
        {
            ("run", t => RunCommand.Execute(Options.Parse(["--target", t, "--exe", fake, "--case", "nonexistent", "--out", Path.Combine(root, "run.csv")], out _)!)),
            ("size", t => SizeCommand.Execute(Options.Parse(["--target", t, "--publish-dir", publish, "--fonts-dir", fonts, "--out", Path.Combine(root, "s.csv")], out _)!)),
            ("size --check", t => SizeCommand.Execute(Options.Parse(["--target", t, "--check", "win-x64", "--publish-dir", publish, "--fonts-dir", fonts], out _)!)),
            ("check", t => CheckCommand.Execute(Options.Parse(["--target", t, "--platform", "windows", "--in", csv], out _)!)),
        };
        foreach (var (name, run) in commands)
        {
            var (_, pdfMessage) = Capture(() => run("pdf"));
            Expect($"{name} --target pdf: the target is accepted", pdfMessage.Contains("--target must be", StringComparison.Ordinal), false);
            var (_, badMessage) = Capture(() => run("amiga"));
            Expect($"{name} --target amiga: refused with a message naming pdf", badMessage.Contains("--target must be", StringComparison.Ordinal) && badMessage.Contains("pdf", StringComparison.Ordinal), true);
        }

        Directory.Delete(root, recursive: true);
    }

    // The exit code and what the command wrote to the error stream.
    private static (int Exit, string Error) Capture(Func<int> run)
    {
        var saved = Console.Error;
        var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            var exit = run();
            return (exit, writer.ToString());
        }
        finally
        {
            Console.SetError(saved);
        }
    }

    private static void PdfSizeCheckTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "pdfsize");
        var fonts = root + "-fonts";
        Directory.CreateDirectory(fonts);
        File.WriteAllBytes(Path.Combine(fonts, "a.ttf"), new byte[7]);

        int Check(string rid, long bytes)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "RafficaPdf.exe"), new byte[bytes]);
            return SizeCommand.Execute(Options.Parse(["--publish-dir", root, "--fonts-dir", fonts, "--target", "pdf", "--check", rid], out _)!);
        }

        const long Win = 50 * 1024 * 1024; // 40 MiB x 1.25 (test table)
        Expect("size --check pdf (test table): exactly at the limit passes", Check("win-x64", Win), 0);
        Expect("size --check pdf (test table): one byte above fails", Check("win-x64", Win + 1), 1);
        const long Mac = 56 * 1024 * 1024 + 256 * 1024; // 45 MiB x 1.25 (test table)
        Expect("size --check pdf (test table): macOS exactly at the limit passes", Check("osx-x64", Mac), 0);
        Expect("size --check pdf (test table): macOS one byte above fails", Check("osx-x64", Mac + 1), 1);
        Expect("size --check pdf (test table): the same size passes on arm64 and x64", Check("osx-arm64", Mac), 0);
        MacBundleTests(root, fonts);
        CheckCommand.PdfTableForTests = new() { ["windows"] = [], ["linux"] = [], ["macos"] = [] };
        Expect("size --check pdf: an empty table exits 2", Check("win-x64", 10), 2);
        Expect("size --check pdf: an empty table exits 2 on Linux", Check("linux-x64", 10), 2);
        Expect("size --check pdf: an empty table exits 2 on macOS", Check("osx-x64", 10), 2);

        // The recorded table: 66 MiB x 1.25 = 82.5 MiB on Windows, 65 MiB x 1.25 = 81.25 MiB on Linux (the zip of the stand-in file is far below 36.25 MiB).
        CheckCommand.PdfTableForTests = null;
        Expect("size --check pdf (recorded): windows exactly at 82.5 MiB passes", Check("win-x64", 82 * 1024 * 1024 + 512 * 1024), 0);
        Expect("size --check pdf (recorded): windows one byte above 82.5 MiB fails", Check("win-x64", 82 * 1024 * 1024 + 512 * 1024 + 1), 1);
        Expect("size --check pdf (recorded): linux exactly at 81.25 MiB passes", Check("linux-x64", 81 * 1024 * 1024 + 256 * 1024), 0);
        Expect("size --check pdf (recorded): linux one byte above 81.25 MiB fails", Check("linux-x64", 81 * 1024 * 1024 + 256 * 1024 + 1), 1);
        Expect("size --check pdf (recorded): macOS exactly at 69 MiB x 1.25 passes", Check("osx-x64", 86 * 1024 * 1024 + 256 * 1024), 0);
        Expect("size --check pdf (recorded): macOS one byte above 69 MiB x 1.25 fails", Check("osx-x64", 86 * 1024 * 1024 + 256 * 1024 + 1), 1);
        Expect("size --check pdf (recorded): the same limit binds arm64", Check("osx-arm64", 86 * 1024 * 1024 + 256 * 1024 + 1), 1);
        Expect("size --check pdf (recorded): macOS asks for no time or memory metric, so a tiny publish passes", Check("osx-x64", 10), 0);
        CheckCommand.PdfTableForTests = PdfTestTable();
        Directory.Delete(root, recursive: true);
    }

    /// <summary>The metrics <c>app</c> (bundle bytes) and <c>dmg</c> (file bytes) of <c>size</c>, with the TEST-ONLY macOS limits app 25 MiB and dmg 12.5 MiB.</summary>
    private static void MacBundleTests(string root, string fonts)
    {
        var bundle = root + "-app";
        var dmg = root + ".dmg";

        int Check(string rid, long appBytes, long? dmgBytes, string target = "pdf")
        {
            foreach (var dir in new[] { root, bundle })
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }

                Directory.CreateDirectory(dir);
            }

            // Both executables are there, so only the --app rule (not the missing-executable check) can answer 2 for the gallery.
            File.WriteAllBytes(Path.Combine(root, "RafficaPdf.exe"), new byte[10]);
            File.WriteAllBytes(Path.Combine(root, "RafficaGallery.exe"), new byte[10]);
            Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
            File.WriteAllBytes(Path.Combine(bundle, "Contents", "MacOS", "RafficaPdf"), new byte[appBytes - 4]);
            File.WriteAllBytes(Path.Combine(bundle, "Contents", "Info.plist"), new byte[4]);
            List<string> args = ["--publish-dir", root, "--fonts-dir", fonts, "--target", target, "--check", rid, "--app", bundle];
            if (dmgBytes is { } d)
            {
                File.WriteAllBytes(dmg, new byte[d]);
                args.AddRange(["--dmg", dmg]);
            }

            return SizeCommand.Execute(Options.Parse([.. args], out _)!);
        }

        const long App = 25 * 1024 * 1024;
        const long Dmg = 12 * 1024 * 1024 + 512 * 1024;
        Expect("size --check --app --dmg: both exactly at their limits pass", Check("osx-arm64", App, Dmg), 0);
        Expect("size --check --app --dmg: the app one byte above fails", Check("osx-arm64", App + 1, Dmg), 1);
        Expect("size --check --app --dmg: the dmg one byte above fails", Check("osx-x64", App, Dmg + 1), 1);
        Expect("size --check --app: a missing --dmg skips only that row", Check("osx-x64", App, null), 0);
        Expect("size --check --app: the app is still judged without --dmg", Check("osx-x64", App + 1, null), 1);
        Expect("size --check --app on Windows is a usage error", Check("win-x64", App, null), 2);
        Expect("size --check --app for the gallery is a usage error", Check("osx-x64", App, null, "gallery"), 2);
        Expect("size --check --app for the gallery: the gallery alone would pass", SizeCommand.Execute(Options.Parse(["--publish-dir", root, "--fonts-dir", fonts, "--target", "gallery", "--check", "osx-x64"], out _)!), 0);

        // A metric without a limit in a non-empty macOS table is unrecorded (exit 1), never indicative.
        CheckCommand.PdfTableForTests = new() { ["windows"] = [], ["linux"] = [], ["macos"] = new() { ["cold"] = 100 } };
        Expect("size --check --app: a macOS metric without a limit is unrecorded and exits 1", Check("osx-x64", App, null), 1);
        Expect("check pdf macos: a metric without a limit is unrecorded, not indicative", CheckCommand.JudgePdf("macos", new Dictionary<string, double?> { ["app"] = 1 }, "app")[0].Verdict, "unrecorded");

        // The recorded macOS table: app 76 MiB x 1.25 = 95 MiB and dmg 38 MiB x 1.25 = 47.5 MiB.
        CheckCommand.PdfTableForTests = null;
        const long RecApp = 95 * 1024 * 1024;
        const long RecDmg = 47 * 1024 * 1024 + 512 * 1024;
        Expect("size --check --app --dmg (recorded): both exactly at their limits pass", Check("osx-x64", RecApp, RecDmg), 0);
        Expect("size --check --app --dmg (recorded): the app one byte above fails", Check("osx-x64", RecApp + 1, RecDmg), 1);
        Expect("size --check --app --dmg (recorded): the dmg one byte above fails", Check("osx-arm64", RecApp, RecDmg + 1), 1);
        CheckCommand.PdfTableForTests = PdfTestTable();
        Check("osx-x64", App, null); // leaves the bundle at its size for the rows test below

        // Symbolic links are not counted (creating one may need privileges on Windows: then the check is skipped).
        var linkRoot = root + "-links";
        if (Directory.Exists(linkRoot))
        {
            Directory.Delete(linkRoot, recursive: true);
        }

        Directory.CreateDirectory(linkRoot);
        File.WriteAllBytes(Path.Combine(linkRoot, "real"), new byte[100]);
        try
        {
            File.CreateSymbolicLink(Path.Combine(linkRoot, "link"), Path.Combine(linkRoot, "real"));
            Expect("app bundle bytes: a symbolic link is not counted at its target's size", SizeCommand.AppBundleBytes(linkRoot), 100);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Console.WriteLine($"skipped: symbolic link check ({e.GetType().Name}: no privilege to create a link here)");
        }

        Directory.Delete(linkRoot, recursive: true);

        // Without --check: the bytes as rows of the size CSV.
        var csv = root + "-bundle.csv";
        File.WriteAllBytes(dmg, new byte[1000]);
        var exit = SizeCommand.Execute(Options.Parse(["--target", "pdf", "--publish-dir", root, "--fonts-dir", fonts, "--app", bundle, "--dmg", dmg, "--out", csv], out _)!);
        var lines = File.Exists(csv) ? File.ReadAllLines(csv) : [];
        Expect("size --app --dmg --out: exit 0", exit, 0);
        Expect("size --app --dmg --out: the app row is the bundle bytes", lines.Any(l => l.EndsWith(",pdf,size,app," + App + ",bytes", StringComparison.Ordinal)), true);
        Expect("size --app --dmg --out: the dmg row is the file bytes", lines.Any(l => l.EndsWith(",pdf,size,dmg,1000,bytes", StringComparison.Ordinal)), true);
        Expect("size --dmg: a missing file is a usage error", SizeCommand.Execute(Options.Parse(["--target", "pdf", "--publish-dir", root, "--fonts-dir", fonts, "--dmg", dmg + ".nope", "--out", csv], out _)!), 2);
        Directory.Delete(bundle, recursive: true);
        File.Delete(dmg);
    }

    private static void SizeCheckTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "sizecheck");
        var fonts = root + "-fonts";
        Directory.CreateDirectory(fonts);
        File.WriteAllBytes(Path.Combine(fonts, "a.ttf"), new byte[7]);

        int Check(string rid, long bytes, bool random = false)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(root);
            var data = new byte[bytes];
            if (random)
            {
                new Random(1).NextBytes(data);
            }

            File.WriteAllBytes(Path.Combine(root, "RafficaGallery.exe"), data);
            return SizeCommand.Execute(Options.Parse(["--publish-dir", root, "--fonts-dir", fonts, "--target", "gallery", "--check", rid], out _)!);
        }

        const long Win = 51118080; // 48.75 MiB
        const long Lin = 45875200; // 43.75 MiB
        Expect("size --check windows: exactly 48.75 MiB passes", Check("win-x64", Win), 0);
        Expect("size --check windows: one byte above fails", Check("win-x64", Win + 1), 1);
        Expect("size --check windows: the CSV is not written", Directory.EnumerateFiles(root, "*.csv").Any() || File.Exists(Path.Combine(root, "size.csv")), false);
        Expect("size --check linux: exactly 43.75 MiB passes", Check("linux-x64", Lin), 0);
        Expect("size --check linux: one byte above fails", Check("linux-x64", Lin + 1), 1);
        Expect("size --check: the same size passes on windows and fails on linux", Check("win-x64", Lin + 1), 0);
        Expect("size --check: a zip above 20 MiB fails (incompressible 21 MiB)", Check("win-x64", 21 * 1024 * 1024, random: true), 1);
        Expect("size --check: a zip at 19 MiB passes", Check("win-x64", 19 * 1024 * 1024, random: true), 0);
        Expect("size --check macOS is indicative and exits 0", Check("osx-x64", Win * 2), 0);
        Expect("size --check: unknown rid is a usage error", Check("amiga-m68k", 10), 2);

        // An empty folder (a publish that failed) must not pass: the main executable has to be there.
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(root))
        {
            File.Delete(file);
        }

        Expect("size --check: an empty publish folder is a usage error", SizeCommand.Execute(Options.Parse(["--publish-dir", root, "--fonts-dir", fonts, "--target", "gallery", "--check", "win-x64"], out _)!), 2);
        File.WriteAllBytes(Path.Combine(root, "lib.dll"), new byte[10]);
        Expect("size --check: a folder without the main executable is a usage error", SizeCommand.Execute(Options.Parse(["--publish-dir", root, "--fonts-dir", fonts, "--target", "gallery", "--check", "win-x64"], out _)!), 2);
        File.WriteAllBytes(Path.Combine(root, "RafficaGallery"), new byte[10]);
        Expect("size --check: the executable of Linux and macOS has no extension", SizeCommand.Execute(Options.Parse(["--publish-dir", root, "--fonts-dir", fonts, "--target", "gallery", "--check", "linux-x64"], out _)!), 0);
        Directory.Delete(root, recursive: true);
    }

    private static void Expect<T>(string name, T actual, T expected)
    {
        s_checks++;
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            s_failures++;
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"FAIL {name}: expected {expected}, got {actual}"));
        }
    }
}

/// <summary>A stand-in for the probe: a script that sleeps.</summary>
static class FakeProbe
{
    public static string Create(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "shell-probe-selftest");
        Directory.CreateDirectory(folder);
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(folder, name + ".cmd");
            File.WriteAllText(path, "@echo off\r\nping -n 60 127.0.0.1 >nul\r\n");
            return path;
        }

        var script = Path.Combine(folder, name + ".sh");
        File.WriteAllText(script, "#!/bin/sh\nsleep 60\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }
}
