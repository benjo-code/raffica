using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace WriteCorpus;

/// <summary>
/// <c>write-corpus</c>: the compatibility corpus of Raffica Write (testdata/write-corpus). Commands: <c>check</c>, <c>run</c> and
/// <c>add</c>. Exit codes: 0 ok, 1 findings, 2 usage. Output is plain text or CSV with root-relative paths and never contains
/// document text.
/// </summary>
internal static partial class Program
{
    private const string Usage = """
        usage:
          write-corpus check --root <dir> [--allow-uncovered]
          write-corpus run --root <dir> [--out <csv>]
          write-corpus add <docx> --root <dir> --id <id> --source <url|text> --license <spdx> --license-file <path>
        """;

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
            return args[0] switch
            {
                "check" => Check(args[1..], output, error),
                "run" => RunCommand(args[1..], output, error),
                "add" => Add(args[1..], output, error),
                _ => Fail(error, $"unknown command '{args[0]}'"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or XmlException or InvalidDataException)
        {
            error.WriteLine($"write-corpus: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine("write-corpus: " + message);
        error.WriteLine(Usage);
        return 2;
    }

    private static int Check(string[] args, TextWriter output, TextWriter error)
    {
        if (!Options.TryParse(args, ["--root"], ["--allow-uncovered"], 0, out var options, out var problem) || options.Value("--root") is not { } root)
        {
            return Fail(error, problem ?? "check needs --root");
        }

        var problems = CorpusCheck.Run(root, requireCoverage: !options.Has("--allow-uncovered"));
        foreach (var line in problems)
        {
            output.WriteLine(line);
        }

        output.WriteLine(problems.Count == 0 ? "write-corpus check: ok" : string.Create(CultureInfo.InvariantCulture, $"write-corpus check: {problems.Count} problem(s)"));
        return problems.Count == 0 ? 0 : 1;
    }

    private static int RunCommand(string[] args, TextWriter output, TextWriter error)
    {
        if (!Options.TryParse(args, ["--root", "--out"], [], 0, out var options, out var problem) || options.Value("--root") is not { } root)
        {
            return Fail(error, problem ?? "run needs --root");
        }

        if (!File.Exists(CorpusManifest.PathIn(root)))
        {
            error.WriteLine($"write-corpus: {CorpusManifest.FileName} is missing in the root");
            return 1;
        }

        using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        var code = CorpusRun.Run(root, buffer);
        output.Write(buffer.ToString());
        if (options.Value("--out") is { } csv)
        {
            File.WriteAllText(csv, buffer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new System.Text.UTF8Encoding(false));
        }

        return code;
    }

    private static int Add(string[] args, TextWriter output, TextWriter error)
    {
        if (!Options.TryParse(args, ["--root", "--id", "--source", "--license", "--license-file"], [], 1, out var options, out var problem))
        {
            return Fail(error, problem ?? "add needs a file");
        }

        string?[] values = [options.Value("--root"), options.Value("--id"), options.Value("--source"), options.Value("--license"), options.Value("--license-file")];
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            return Fail(error, "add needs --root, --id, --source, --license and --license-file");
        }

        var (root, id, source, license, licenseFile) = (values[0]!, values[1]!, values[2]!, values[3]!, values[4]!);
        var docx = options.Positional[0];
        var extension = Path.GetExtension(docx).ToLowerInvariant();
        if (!IdPattern().IsMatch(id))
        {
            error.WriteLine("write-corpus: the id must be lower-case letters, digits and hyphens");
            return 1;
        }

        if (!CorpusFeatures.AllowedLicenses.Contains(license, StringComparer.Ordinal))
        {
            error.WriteLine($"write-corpus: the licence '{license}' is not in the allowed list ({string.Join(", ", CorpusFeatures.AllowedLicenses)})");
            return 1;
        }

        if (extension is not (".docx" or ".docm") || !File.Exists(docx) || !File.Exists(licenseFile))
        {
            error.WriteLine("write-corpus: the file must be an existing .docx or .docm and the licence file must exist");
            return 1;
        }

        var manifest = File.Exists(CorpusManifest.PathIn(root)) ? CorpusManifest.Load(root) : new CorpusManifest();
        if (manifest.Files.Any(f => string.Equals(f.Id, id, StringComparison.Ordinal)))
        {
            error.WriteLine($"write-corpus: the id '{id}' is already in the manifest");
            return 1;
        }

        IReadOnlySet<string> features;
        try
        {
            features = FeatureDetector.Detect(docx);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            error.WriteLine("write-corpus: the file is not a readable package");
            return 1;
        }

        Directory.CreateDirectory(Path.Combine(root, id));
        Directory.CreateDirectory(Path.Combine(root, "licenses"));
        var relativePath = $"{id}/{id}{extension}";
        var relativeLicense = $"licenses/{id}-{Path.GetFileName(licenseFile)}";
        File.Copy(docx, Path.Combine(root, relativePath), overwrite: false);
        File.Copy(licenseFile, Path.Combine(root, relativeLicense), overwrite: false);
        manifest.Files.Add(new CorpusEntry
        {
            Id = id,
            Path = relativePath,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, relativePath)))),
            Source = source,
            License = license,
            LicenseFile = relativeLicense,
            Retrieved = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Features = [.. features.Order(StringComparer.Ordinal)],
        });
        manifest.Save(root);
        output.WriteLine($"write-corpus add: {id} ({features.Count} feature(s))");
        return 0;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex IdPattern();
}

/// <summary>A minimal option parser: named values, switches and a fixed number of positional arguments.</summary>
internal sealed class Options
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _switches = new(StringComparer.Ordinal);

    public List<string> Positional { get; } = [];

    public string? Value(string name) => _values.GetValueOrDefault(name);

    public bool Has(string name) => _switches.Contains(name);

    public static bool TryParse(string[] args, string[] valueNames, string[] switchNames, int positional, out Options options, out string? problem)
    {
        options = new Options();
        problem = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (valueNames.Contains(arg, StringComparer.Ordinal))
            {
                if (i + 1 >= args.Length)
                {
                    problem = $"{arg} needs a value";
                    return false;
                }

                options._values[arg] = args[++i];
            }
            else if (switchNames.Contains(arg, StringComparer.Ordinal))
            {
                options._switches.Add(arg);
            }
            else if (!arg.StartsWith("--", StringComparison.Ordinal) && options.Positional.Count < positional)
            {
                options.Positional.Add(arg);
            }
            else
            {
                problem = $"unexpected argument '{arg}'";
                return false;
            }
        }

        if (options.Positional.Count != positional)
        {
            problem = "a file argument is missing";
            return false;
        }

        return true;
    }
}
