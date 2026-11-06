#:property PublishAot=false

// Checks the dependencies of a Linux shared library: the highest GLIBC and GLIBCXX version it needs and its NEEDED libraries.
//   dotnet run tools/check-binary.cs -- <file>... [--max-glibc 2.35] [--max-glibcxx 3.4.30]
// Reads the dynamic symbols and the program headers with objdump (package binutils). Used by the workflow native-tesseract.yml.
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

return CheckBinary.Run(args);

static partial class ElfRules
{
    public const string DefaultMaxGlibc = "2.35";

    // libstdc++ of Ubuntu 22.04 and Debian 12 (GCC 12) exports GLIBCXX up to 3.4.30.
    public const string DefaultMaxGlibcxx = "3.4.30";

    public static readonly string[] AllowedNeeded =
    [
        "libc.so.6", "libm.so.6", "libdl.so.2", "libpthread.so.0", "librt.so.1", "libz.so.1",
        "ld-linux-x86-64.so.2", "libstdc++.so.6", "libgcc_s.so.1",
    ];

    // objdump -T: "0000000000000000      DF *UND*	0000000000000000 (GLIBC_2.2.5) printf"
    [GeneratedRegex(@"\*UND\*.*\(GLIBC_(?<v>\d+(?:\.\d+)*)\)")]
    private static partial Regex UndefinedSymbolVersion();

    // objdump -T: "0000000000000000      DF *UND*	0000000000000000 (GLIBCXX_3.4.30) _ZSt..."
    [GeneratedRegex(@"\*UND\*.*\(GLIBCXX_(?<v>\d+(?:\.\d+)*)\)")]
    private static partial Regex UndefinedCxxSymbolVersion();

    // objdump -p: "  NEEDED               libc.so.6"
    [GeneratedRegex(@"^\s*NEEDED\s+(?<n>\S+)\s*$")]
    private static partial Regex NeededLine();

    public static bool TryParseVersion(string text, out int[] version)
    {
        var parts = text.Split('.');
        version = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out version[i]))
            {
                return false;
            }
        }

        return true;
    }

    // Numeric tuple comparison (2.9 < 2.35), missing components count as 0.
    public static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var c = (i < a.Length ? a[i] : 0).CompareTo(i < b.Length ? b[i] : 0);
            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }

    public static string? HighestGlibc(string dynamicSymbols, out int[]? version) =>
        Highest(UndefinedSymbolVersion(), dynamicSymbols, out version);

    public static string? HighestGlibcxx(string dynamicSymbols, out int[]? version) =>
        Highest(UndefinedCxxSymbolVersion(), dynamicSymbols, out version);

    private static string? Highest(Regex symbolVersion, string dynamicSymbols, out int[]? version)
    {
        string? text = null;
        version = null;
        foreach (var line in dynamicSymbols.Split('\n'))
        {
            var m = symbolVersion.Match(line);
            if (m.Success && TryParseVersion(m.Groups["v"].Value, out var v) && (version is null || Compare(v, version) > 0))
            {
                version = v;
                text = m.Groups["v"].Value;
            }
        }

        return text;
    }

    public static List<string> Needed(string programHeaders)
    {
        var result = new List<string>();
        foreach (var line in programHeaders.Split('\n'))
        {
            var m = NeededLine().Match(line);
            if (m.Success)
            {
                result.Add(m.Groups["n"].Value);
            }
        }

        return result;
    }

    // Returns the violations for one file, given the objdump -T and objdump -p outputs.
    public static List<string> Violations(string dynamicSymbols, string programHeaders, int[] max, string maxText,
        string maxGlibcxxText = DefaultMaxGlibcxx)
    {
        var problems = new List<string>();
        var highest = HighestGlibc(dynamicSymbols, out var version);
        if (highest is not null && Compare(version!, max) > 0)
        {
            problems.Add($"needs GLIBC_{highest} (at most GLIBC_{maxText})");
        }

        var highestCxx = HighestGlibcxx(dynamicSymbols, out var cxxVersion);
        if (highestCxx is not null && TryParseVersion(maxGlibcxxText, out var maxCxx) && Compare(cxxVersion!, maxCxx) > 0)
        {
            problems.Add($"needs GLIBCXX_{highestCxx} (at most GLIBCXX_{maxGlibcxxText})");
        }

        foreach (var name in Needed(programHeaders))
        {
            if (!AllowedNeeded.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"NEEDED library not allowed: {name}");
            }
        }

        return problems;
    }
}

static class CheckBinary
{
    public static int Run(string[] args)
    {
        var files = new List<string>();
        var maxText = ElfRules.DefaultMaxGlibc;
        var maxCxxText = ElfRules.DefaultMaxGlibcxx;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--max-glibc" && i + 1 < args.Length)
            {
                maxText = args[++i];
            }
            else if (args[i] == "--max-glibcxx" && i + 1 < args.Length)
            {
                maxCxxText = args[++i];
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"unknown option: {args[i]}");
                return 2;
            }
            else
            {
                files.Add(args[i]);
            }
        }

        if (files.Count == 0 || !ElfRules.TryParseVersion(maxText, out var max) || !ElfRules.TryParseVersion(maxCxxText, out _))
        {
            Console.Error.WriteLine("usage: check-binary <file>... [--max-glibc 2.35] [--max-glibcxx 3.4.30]");
            return 2;
        }

        var failed = false;
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"{file}: file not found");
                failed = true;
                continue;
            }

            var symbols = Objdump(["-T", file]);
            var headers = Objdump(["-p", file]);
            if (symbols is null || headers is null)
            {
                failed = true;
                continue;
            }

            var problems = ElfRules.Violations(symbols, headers, max, maxText, maxCxxText);
            if (problems.Count == 0)
            {
                Console.WriteLine($"{file}: ok");
                continue;
            }

            failed = true;
            foreach (var p in problems)
            {
                Console.Error.WriteLine($"{file}: {p}");
            }
        }

        return failed ? 1 : 0;
    }

    // objdump -T prints "not a dynamic object" for a static file; that is not an error here (empty text, no GLIBC need).
    private static string? Objdump(string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("objdump") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in arguments)
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi)!;
            var stderrTask = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var stderr = stderrTask.Result;
            if (p.ExitCode != 0 && !stderr.Contains("not a dynamic object", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"{arguments[^1]}: objdump {arguments[0]} failed: {stderr.Trim()}");
                return null;
            }

            return stdout;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine("objdump not found: install the package binutils (sudo apt-get install -y binutils)");
            return null;
        }
    }
}
