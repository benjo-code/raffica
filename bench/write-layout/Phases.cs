using System.Globalization;
using System.Text;

namespace WriteLayoutBench;

/// <summary><c>phases</c>: the breakdown of the first-page path, one cold child process per run, the median of each phase.</summary>
internal static class Phases
{
    public static int Run(Options options, TextWriter output, TextWriter error)
    {
        var file = options.RequireFile();
        if (!File.Exists(file))
        {
            return Program.Fail(error, "the file does not exist");
        }

        var runs = Math.Max(1, options.Int("--runs", 5));
        var all = new List<PhaseTimes>();
        for (var i = 0; i < runs; i++)
        {
            var lines = ChildProcess.FirstPage(file, withPhases: true, error);
            var times = lines?.Select(PhaseTimes.Parse).FirstOrDefault(t => t is not null);
            if (times is null)
            {
                return 2;
            }

            all.Add(times);
        }

        output.Write(Format(all));
        return 0;
    }

    /// <summary>A table with the median of each phase over the given runs.</summary>
    internal static string Format(IReadOnlyList<PhaseTimes> runs)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{"phase",-20}{"median ms",10}   ({runs.Count} cold run(s))"));
        for (var i = 0; i < PhaseTimes.Names.Length; i++)
        {
            var median = Median([.. runs.Select(r => r.Values[i])]);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{PhaseTimes.Names[i],-20}{median,10:0.0}"));
        }

        return text.ToString();
    }

    internal static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }
}
