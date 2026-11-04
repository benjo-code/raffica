// Measurement script of the shell probe (bench/shell-probe/README.md, spec section 9).
//   dotnet run bench/shell-probe/measure.cs -- run --exe <probe> --mode empty|frame --case cold|warm|input|memory --out <csv> [--machine <name>] [--label <name>]
//   dotnet run bench/shell-probe/measure.cs -- run --target gallery --exe <RafficaGallery> --case cold|warm|input|memory --out <csv> [--machine <name>]
//   dotnet run bench/shell-probe/measure.cs -- size --publish-dir <dir> --fonts-dir <dir> --out <csv> [--target probe|gallery] [--mode frame] [--machine <name>]
//   dotnet run bench/shell-probe/measure.cs -- size --check <win-x64|linux-x64|osx-x64> --publish-dir <dir> --fonts-dir <dir> [--target gallery]
//   dotnet run bench/shell-probe/measure.cs -- check --platform windows|linux|macos --in "<csv glob>" [--summary <md>]
//   dotnet run bench/shell-probe/measure.cs -- budgets --in <csv>... --out <md>
//   dotnet run bench/shell-probe/measure.cs -- selftest
// CSV columns: machine,mode,case,run,value,unit. unit is ms or bytes. Rows of the case input carry the statistic
// (median, p99, max) in the column run; rows of the case size carry total, zip, fonts or native. The gallery
// (--target gallery) writes the mode gallery.
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
    public static string CsvMode(string target, string? mode) => target == "gallery" ? "gallery" : mode!;

    public static int Execute(Options o)
    {
        var target = o.Get("--target") ?? "probe";
        if (target is not ("probe" or "gallery"))
        {
            Console.Error.WriteLine("--target must be probe or gallery.");
            return 2;
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
        if (target is not ("probe" or "gallery"))
        {
            Console.Error.WriteLine("--target must be probe or gallery.");
            return 2;
        }

        var platform = check is null ? null : CheckCommand.PlatformOfRid(check);
        if (check is not null && platform is null)
        {
            Console.Error.WriteLine("--check needs a runtime identifier: win-x64, linux-x64 or osx-x64.");
            return 2;
        }

        var publish = Path.GetFullPath(o.Get("--publish-dir")!);
        var fonts = Path.GetFullPath(o.Get("--fonts-dir")!);
        if (!Directory.Exists(publish) || !Directory.Exists(fonts))
        {
            Console.Error.WriteLine("--publish-dir and --fonts-dir must be existing folders.");
            return 2;
        }

        var (total, zip, fontBytes, native) = Measure(publish, fonts, target == "gallery" ? "RafficaGallery" : "ShellProbe");

        if (platform is not null)
        {
            // The CI check: the size and zip limits of the check command, exit 1 above them, no CSV.
            var measured = new Dictionary<string, double?> { ["size"] = total / (double)Stats.MiB, ["zip"] = zip / (double)Stats.MiB };
            var results = CheckCommand.Judge(platform, measured, "size", "zip");
            Console.WriteLine($"Published {target} ({check}):");
            Console.Write(CheckCommand.Render(results));
            Console.WriteLine(CheckCommand.ExitCode(results) == 1 ? "size check: FAILED (above the limit)" : "size check: ok");
            return CheckCommand.ExitCode(results);
        }

        var machine = o.Get("--machine") ?? Environment.MachineName;
        var mode = o.Get("--mode") ?? (target == "gallery" ? "gallery" : "frame");

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

    /// <summary>The limit (budget x 1.25) of a metric on a platform; null where there is none (macOS).</summary>
    public static double? LimitOf(string platform, string key) =>
        s_budgets.TryGetValue(platform, out var table) && table.TryGetValue(key, out var budget) ? Stats.Limit(budget) : null;

    /// <summary>The platform of a runtime identifier (<c>win-x64</c>, <c>linux-x64</c>, <c>osx-*</c>); null if unknown.</summary>
    public static string? PlatformOfRid(string rid) =>
        rid.StartsWith("win-", StringComparison.Ordinal) ? "windows"
        : rid.StartsWith("linux-", StringComparison.Ordinal) ? "linux"
        : rid.StartsWith("osx-", StringComparison.Ordinal) ? "macos"
        : null;

    /// <summary>
    /// Verdicts for the measured values (<paramref name="only"/> restricts the metrics): <c>ok</c> (at or below the limit), <c>over</c>,
    /// <c>missing</c> (a required value is not there) or <c>indicative</c> (the platform has no limits).
    /// </summary>
    public static List<Result> Judge(string platform, IReadOnlyDictionary<string, double?> measured, params string[] only)
    {
        var results = new List<Result>();
        foreach (var (key, label) in s_metrics.Where(m => only.Length == 0 || only.Contains(m.Key)))
        {
            var value = measured.GetValueOrDefault(key);
            var limit = LimitOf(platform, key);
            double? budget = limit / Stats.Tolerance;
            var verdict = limit is null ? "indicative" : value is null ? "missing" : value > limit ? "over" : "ok";
            results.Add(new Result(key, label, value, budget, limit, verdict));
        }

        return results;
    }

    /// <summary>The medians of the gallery rows (mode <c>gallery</c>), then their verdicts.</summary>
    public static List<Result> Evaluate(string platform, IReadOnlyList<BudgetsCommand.Row> rows)
    {
        double? Median(string @case, string? run)
        {
            var values = rows
                .Where(r => r.Mode == "gallery" && r.Case == @case && (run is null ? double.TryParse(r.Run, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) : r.Run == run))
                .Select(r => r.Unit == "bytes" ? r.Value / Stats.MiB : r.Value)
                .ToList();
            return values.Count == 0 ? null : Stats.Median(values);
        }

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

    /// <summary>1 when any value is over its limit or a required value is missing, else 0.</summary>
    public static int ExitCode(IReadOnlyList<Result> results) => results.Any(r => r.Verdict is "over" or "missing") ? 1 : 0;

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
                    if (p.Length != 6 || !double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        problems.Add($"unreadable CSV row in {path}: {line}");
                        continue;
                    }

                    rows.Add(new BudgetsCommand.Row(p[0], p[1], p[2], p[3], value, p[5]));
                }
            }
        }

        var results = Evaluate(platform, rows);
        var text = new StringBuilder($"## Frame measurement ({platform})\n\n");
        if (platform == "macos")
        {
            text.AppendLine("macOS has no limits; the values are indicative only.").AppendLine();
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

        // A missing or unreadable CSV fails the check on every platform that has limits.
        var failed = ExitCode(results) == 1 || (platform != "macos" && problems.Count > 0);
        Console.WriteLine(failed ? "check: FAILED (a value is over its limit or a CSV is missing)" : "check: ok");
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
        Expect("check command: a missing CSV exits 1", CheckCommand.Execute(Options.Parse(["--platform", "windows", "--in", Path.Combine(folder, "nope", "*.csv")], out _)!), 1);
        Expect("check command: a missing file exits 1", CheckCommand.Execute(Options.Parse(["--platform", "windows", "--in", Path.Combine(folder, "nope.csv")], out _)!), 1);
        Expect("check command: unknown platform is a usage error", CheckCommand.Execute(Options.Parse(["--platform", "amiga", "--in", csv], out _)!), 2);
        Expect("check command: --platform is required", CheckCommand.Execute(Options.Parse(["--in", csv], out _)!), 2);
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
