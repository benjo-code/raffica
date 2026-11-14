using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace WriteLayoutBench;

/// <summary>A cold child process of this tool.</summary>
internal static class ChildProcess
{
    /// <summary>The one rule for every command that times cold children: only a Native AOT publish is timed. Returns true (and says why on <paramref name="error"/>) when this is a JIT build.</summary>
    public static bool RefuseJit(TextWriter error)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            return false;
        }

        error.WriteLine("write-layout-bench: refusing to time anything but a Native AOT publish (this is a JIT build). Use dotnet publish bench/write-layout -c Release -r <rid>.");
        return true;
    }

    /// <summary>Runs <c>first-page</c> and returns its output lines, or null (with a message on <paramref name="error"/>) when it fails.</summary>
    public static List<string>? FirstPage(string file, bool withPhases, TextWriter error)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        var info = new ProcessStartInfo(path) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("first-page");
        info.ArgumentList.Add("--file");
        info.ArgumentList.Add(file);
        if (withPhases)
        {
            info.ArgumentList.Add("--phases");
        }

        using var child = Process.Start(info) ?? throw new InvalidOperationException("the child did not start");
        var errorText = child.StandardError.ReadToEndAsync();
        var text = child.StandardOutput.ReadToEnd();
        child.WaitForExit();
        if (child.ExitCode != 0)
        {
            error.WriteLine($"write-layout-bench: the child exited with {child.ExitCode}: {errorText.GetAwaiter().GetResult().Trim()}");
            return null;
        }

        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r'))];
    }

    public static double? ParseDone(IEnumerable<string> lines)
    {
        var line = lines.FirstOrDefault(l => l.StartsWith("done ms=", StringComparison.Ordinal));
        return line is not null && double.TryParse(line["done ms=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) ? ms : null;
    }
}

/// <summary><c>run</c>: cold child processes of <c>first-page</c>, each result, the median; only for a Native AOT publish.</summary>
internal static class RunCommand
{
    public static int Run(Options options, TextWriter output, TextWriter error)
    {
        if (ChildProcess.RefuseJit(error))
        {
            return 2;
        }

        var file = options.RequireFile();
        if (!File.Exists(file))
        {
            return Program.Fail(error, "the file does not exist");
        }

        var runs = Math.Max(1, options.Int("--runs", 10));
        var warmup = options.Int("--warmup", 2);
        var results = new List<double>();
        var csv = new StringBuilder("run,ms\n");
        for (var i = 0; i < warmup + runs; i++)
        {
            var lines = ChildProcess.FirstPage(file, withPhases: false, error);
            var ms = lines is null ? null : ChildProcess.ParseDone(lines);
            if (ms is null)
            {
                if (lines is not null)
                {
                    error.WriteLine($"write-layout-bench: run {i + 1}: the child printed no 'done ms=' line");
                }

                return 2;
            }

            var counted = i >= warmup;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{(counted ? "run" : "warmup")} {(counted ? i - warmup + 1 : i + 1)}: {ms:0.0} ms"));
            if (counted)
            {
                results.Add(ms.Value);
                csv.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{results.Count},{ms:0.0}"));
            }
        }

        var median = Phases.Median([.. results]);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"median first page: {median:0.0} ms over {results.Count} cold runs"));
        csv.AppendLine(string.Create(CultureInfo.InvariantCulture, $"median,{median:0.0}"));
        var path = options.Value("--csv");
        if (path is not null)
        {
            File.WriteAllText(path, csv.ToString());
        }

        return 0;
    }
}
