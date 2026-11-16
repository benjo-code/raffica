using System.Globalization;

namespace WriteLayoutBench;

/// <summary>
/// <c>write-layout-bench</c>: the headless benchmark of the Write layout engine, no UI. Commands: <c>check</c>, <c>first-page</c>
/// (the cold child), <c>phases</c>, <c>run</c>, <c>edit</c> and <c>render</c>. Exit codes: 0 ok, 1 findings, 2 bad input or a build that must not
/// be timed. The output never contains document text.
/// </summary>
internal static class Program
{
    private const string Usage = """
        usage:
          write-layout-bench check [--file <docx>] [--root <dir>] [--budget-ms <n>]
          write-layout-bench first-page --file <docx> [--phases]
          write-layout-bench phases --file <docx> [--runs 5]
          write-layout-bench run --file <docx> [--runs 10] [--warmup 2] [--csv <path>]
          write-layout-bench edit [--file <docx>] [--runs <keystrokes per case, 200>] [--csv <path>] [--phases] [--background]
          write-layout-bench render <page> <out.png> [--file <docx>] [--root <dir>]
        """;

    private static readonly DateTime s_mainEntered = DateTime.Now;

    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
        {
            error.WriteLine(Usage);
            return 2;
        }

        try
        {
            var options = Options.Parse(args[1..]);
            return args[0] switch
            {
                "check" => Check.Run(options, output, error),
                "first-page" => FirstPageCommand(options, output, error),
                "phases" => Phases.Run(options, output, error),
                "run" => RunCommand.Run(options, output, error),
                "edit" => EditCommand.Run(options, output, error),
                "render" => Render.Run(options, output, error),
                _ => Fail(error, $"unknown command '{args[0]}'"),
            };
        }
        catch (UsageException ex)
        {
            return Fail(error, ex.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The exception type only: a message can hold paths or document values.
            error.WriteLine($"write-layout-bench: failed ({ex.GetType().Name})");
            return 2;
        }
    }

    internal static int Fail(TextWriter error, string message)
    {
        error.WriteLine("write-layout-bench: " + message);
        error.WriteLine(Usage);
        return 2;
    }

    private static int FirstPageCommand(Options options, TextWriter output, TextWriter error)
    {
        var file = options.RequireFile();
        if (!File.Exists(file))
        {
            return Fail(error, "the file does not exist");
        }

        var times = new PhaseTimes { StartToMainMs = FirstPage.SinceProcessStart(s_mainEntered) };
        using var result = FirstPage.Draw(file, times);
        if (options.Flag("--phases"))
        {
            output.WriteLine(times.Serialize());
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"done ms={result.Milliseconds:0.0}"));
        return 0;
    }
}

/// <summary>A command line the tool cannot use.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>The words after the command: <c>--name value</c> pairs, flags and positional values.</summary>
internal sealed class Options
{
    private static readonly HashSet<string> s_flags = ["--phases", "--background"];

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    public List<string> Positional { get; } = [];

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                options.Positional.Add(arg);
            }
            else if (s_flags.Contains(arg))
            {
                options._flags.Add(arg);
            }
            else if (i + 1 < args.Length)
            {
                options._values[arg] = args[++i];
            }
            else
            {
                throw new UsageException($"missing value after {arg}");
            }
        }

        return options;
    }

    public bool Flag(string name) => _flags.Contains(name);

    public string? Value(string name) => _values.GetValueOrDefault(name);

    public int Int(string name, int fallback)
    {
        var text = Value(name);
        if (text is null)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new UsageException($"{name} needs a non-negative number");
    }

    public string RequireFile() => Value("--file") ?? throw new UsageException("--file <docx> is required");

    /// <summary>The thesis of the corpus, found from <c>--root</c> or by walking up from the tool to <c>Raffica.slnx</c>.</summary>
    public string FileOrThesis()
    {
        var file = Value("--file");
        if (file is not null)
        {
            return file;
        }

        var root = Value("--root") ?? FindRoot();
        return Path.Combine(root, "testdata", "write-corpus", "thesis", "thesis.docx");
    }

    private static string FindRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Raffica.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new UsageException("the repository root was not found; pass --file or --root");
    }
}
