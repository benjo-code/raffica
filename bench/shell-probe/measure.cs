// Measurement script of the shell probe (bench/shell-probe/README.md, spec section 9).
//   dotnet run bench/shell-probe/measure.cs -- run --exe <probe> --mode empty|frame --case cold|warm|input|memory --out <csv> [--machine <name>] [--label <name>]
//   dotnet run bench/shell-probe/measure.cs -- size --publish-dir <dir> --fonts-dir <dir> --out <csv> [--mode frame] [--machine <name>]
//   dotnet run bench/shell-probe/measure.cs -- budgets --in <csv>... --out <md>
//   dotnet run bench/shell-probe/measure.cs -- selftest
// CSV columns: machine,mode,case,run,value,unit. unit is ms or bytes. Rows of the case input carry the statistic
// (median, p99, max) in the column run; rows of the case size carry total, zip, fonts or native.
//
// Exit codes: 0 ok, 1 a failed run (or a failed selftest), 2 usage error.

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

    /// <summary>The limit of a check: budget × 1.25 (as in Pagewing).</summary>
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

/// <summary>The warning for a machine without a usable compositor rate (Pagewing's DwmFlush check).</summary>
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
    public static string Label(Options o) => o.Get("--label") ?? o.Get("--case")!;

    public static int Execute(Options o)
    {
        if (o.Require("--exe", "--mode", "--case", "--out") is { } missing)
        {
            Console.Error.WriteLine($"{missing} is required.");
            return 2;
        }

        var exe = Path.GetFullPath(o.Get("--exe")!);
        var mode = o.Get("--mode")!;
        var c = Cases.FirstOrDefault(x => x.Name == o.Get("--case"));
        if (c is null || mode is not ("empty" or "frame") || !File.Exists(exe))
        {
            Console.Error.WriteLine(!File.Exists(exe) ? $"file not found: {exe}" : "--mode must be empty or frame and --case cold, warm, input or memory.");
            return 2;
        }

        var machine = o.Get("--machine") ?? Environment.MachineName;
        string[] arguments = ["--mode", mode, c.Switch];
        var name = $"{mode}/{c.Name}";
        Console.WriteLine($"{name}: {exe} {string.Join(' ', arguments)}");
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
        for (var run = -c.Warmup + 1; run <= c.Runs; run++)
        {
            var (output, ms, error) = Runner.RunOnce(exe, arguments, c.Name == "input" ? "input" : c.Name == "memory" ? "memory-bytes" : "first-frame");
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
                    rows.Add(Lines.Csv(machine, mode, Label(o), stat, value, "ms"));
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
                rows.Add(Lines.Csv(machine, mode, Label(o), run.ToString(CultureInfo.InvariantCulture), value2, unit));
            }

            if (run < c.Runs)
            {
                Thread.Sleep(c.PauseMs);
            }
        }

        WriteCsv(o.Get("--out")!, rows);
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

/// <summary><c>size</c>: the published folder, the zip, the share of fonts and of native libraries.</summary>
static class SizeCommand
{
    public static int Execute(Options o)
    {
        if (o.Require("--publish-dir", "--fonts-dir", "--out") is { } missing)
        {
            Console.Error.WriteLine($"{missing} is required.");
            return 2;
        }

        var publish = Path.GetFullPath(o.Get("--publish-dir")!);
        var fonts = Path.GetFullPath(o.Get("--fonts-dir")!);
        if (!Directory.Exists(publish) || !Directory.Exists(fonts))
        {
            Console.Error.WriteLine("--publish-dir and --fonts-dir must be existing folders.");
            return 2;
        }

        var machine = o.Get("--machine") ?? Environment.MachineName;
        var mode = o.Get("--mode") ?? "frame";
        var (total, zip, fontBytes, native) = Measure(publish, fonts);

        var rows = new[] { ("total", total), ("zip", zip), ("fonts", fontBytes), ("native", native) }
            .Select(r => Lines.Csv(machine, mode, "size", r.Item1, r.Item2, "bytes"))
            .ToList();
        foreach (var row in rows)
        {
            Console.WriteLine(row);
        }

        RunCommand.WriteCsv(o.Get("--out")!, rows);
        return 0;
    }

    /// <summary>
    /// The sizes in bytes. Debug symbols (<c>*.pdb</c>, <c>*.dbg</c>) are not shipped and count nowhere; the native
    /// libraries are every other file but the main executable; the zip uses <see cref="CompressionLevel.Optimal"/>.
    /// </summary>
    public static (long Total, long Zip, long Fonts, long Native) Measure(string publishDir, string fontsDir)
    {
        var files = Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories)
            .Where(f => !IsSymbols(f))
            .Select(f => new FileInfo(f))
            .ToList();
        var native = files.Where(f => Path.GetFileNameWithoutExtension(f.Name) != "ShellProbe").Sum(f => f.Length);
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
        Path.GetExtension(path).ToLowerInvariant() is ".pdb" or ".dbg";
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
        foreach (var path in o.GetAll("--in").SelectMany(Expand))
        {
            foreach (var line in File.ReadLines(path).Skip(1).Where(l => l.Length > 0))
            {
                var p = line.Split(',');
                if (p.Length != 6 || !double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
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

        File.WriteAllText(Path.GetFullPath(o.Get("--out")!), Render(rows), new UTF8Encoding(false));
        Console.WriteLine($"Markdown: {Path.GetFullPath(o.Get("--out")!)}");
        return 0;
    }

    /// <summary>The Markdown of all machines. Budgets and limits come from the frame mode only.</summary>
    public static string Render(IReadOnlyList<Row> rows)
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

    private static IEnumerable<string> Expand(string pattern)
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

        var sizeFolder = Path.Combine(Path.GetTempPath(), "shell-probe-selftest", "size");
        var fontFolder = sizeFolder + "-fonts";
        Directory.CreateDirectory(sizeFolder);
        Directory.CreateDirectory(fontFolder);
        File.WriteAllBytes(Path.Combine(sizeFolder, "ShellProbe.exe"), new byte[100]);
        File.WriteAllBytes(Path.Combine(sizeFolder, "ShellProbe.pdb"), new byte[50]);
        File.WriteAllBytes(Path.Combine(sizeFolder, "lib.dll"), new byte[30]);
        File.WriteAllBytes(Path.Combine(sizeFolder, "lib.pdb"), new byte[20]);
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

        Console.WriteLine($"selftest: {s_checks - s_failures}/{s_checks} checks passed");
        return s_failures == 0 ? 0 : 1;
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
