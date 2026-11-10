#:property PublishAot=false

// Checks the dependencies of a native library. The format is read from the first four bytes of the file.
// ELF (Linux): the highest GLIBC and GLIBCXX version it needs and its NEEDED libraries.
//   dotnet run tools/check-binary.cs -- <file>... [--max-glibc 2.35] [--max-glibcxx 3.4.30] [--allow-needed <lib>]...
// Mach-O (macOS, thin 64-bit or universal): architecture, deployment target and dependencies (otool -L, lipo -archs, vtool -show-build; Mac only).
// A universal (fat) file is accepted for a *.dylib only (the NuGet natives of Avalonia, SkiaSharp and HarfBuzz are universal): it must hold the
// expected slice and that slice is checked. The executables stay single-architecture. Any other format is a failure.
//   dotnet run tools/check-binary.cs -- <file> --arch <x86_64|arm64> --max-macos <x.y> --bundle <folder>
//   dotnet run tools/check-binary.cs -- selftest   (the rules on sample text, runs anywhere)
// --allow-needed adds one NEEDED library to the allow-list for this run (the release build allows libfontconfig.so.1 of Skia).
// Reads the dynamic symbols and the program headers with objdump (package binutils). Used by the workflow native-tesseract.yml.
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

if (args is ["selftest"])
{
    return SelfTest.Run();
}

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
        string maxGlibcxxText = DefaultMaxGlibcxx, IReadOnlyCollection<string>? extraAllowedNeeded = null)
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
            if (!AllowedNeeded.Contains(name, StringComparer.Ordinal) && extraAllowedNeeded?.Contains(name, StringComparer.Ordinal) != true)
            {
                problems.Add($"NEEDED library not allowed: {name}");
            }
        }

        return problems;
    }
}

enum BinaryFormat
{
    Unknown,
    Elf,
    MachO,
}

// The rules for a Mach-O file (the OCR library, the NuGet natives, the executables), from the text of otool -L, lipo -archs and vtool -show-build.
static partial class MachORules
{
    // otool -L: "\t/usr/lib/libSystem.B.dylib (compatibility version 1.0.0, current version 1351.0.0)"
    [GeneratedRegex(@"^\s+(?<p>\S.*?)\s+\(compatibility version")]
    private static partial Regex DependencyLine();

    // vtool -show-build: "    minos 13.0" (LC_BUILD_VERSION) or "  version 13.0" after "cmd LC_VERSION_MIN_MACOSX".
    [GeneratedRegex(@"^\s*minos\s+(?<v>\d+(?:\.\d+)*)\s*$")]
    private static partial Regex MinosLine();

    [GeneratedRegex(@"^\s*version\s+(?<v>\d+(?:\.\d+)*)\s*$")]
    private static partial Regex LegacyVersionLine();

    public static List<string> Dependencies(string otoolOutput) => Dependencies(otoolOutput, out _);

    // Unparsable receives every indented non-blank line that is not a dependency line (the parser must not fail open on a changed format).
    public static List<string> Dependencies(string otoolOutput, out List<string> unparsable)
    {
        var result = new List<string>();
        unparsable = [];
        foreach (var raw in otoolOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var m = DependencyLine().Match(line);
            if (m.Success)
            {
                result.Add(m.Groups["p"].Value);
            }
            else if (line.Length > 0 && char.IsWhiteSpace(line[0]) && line.Trim().Length > 0)
            {
                unparsable.Add(line.Trim());
            }
        }

        return result;
    }

    // The deployment target, or null when the text names none.
    public static string? MinOs(string vtoolOutput)
    {
        var legacy = false;
        foreach (var raw in vtoolOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var m = MinosLine().Match(line);
            if (m.Success)
            {
                return m.Groups["v"].Value;
            }

            if (line.Contains("cmd ", StringComparison.Ordinal))
            {
                legacy = line.Contains("LC_VERSION_MIN_MACOSX", StringComparison.Ordinal);
            }
            else if (legacy && LegacyVersionLine().Match(line) is { Success: true } v)
            {
                return v.Groups["v"].Value;
            }
        }

        return null;
    }

    // A dependency passes when it is a system library (/usr/lib/, /System/Library/) or one of the three @ forms naming a file the bundle has.
    public static bool DependencyAllowed(string dependency, Func<string, bool> bundleHasFile)
    {
        if (dependency.StartsWith("/usr/lib/", StringComparison.Ordinal) || dependency.StartsWith("/System/Library/", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var prefix in new[] { "@rpath/", "@loader_path/", "@executable_path/" })
        {
            if (dependency.StartsWith(prefix, StringComparison.Ordinal))
            {
                var name = dependency[prefix.Length..];
                return name.Length > 0 && !name.Contains('/', StringComparison.Ordinal) && bundleHasFile(name);
            }
        }

        return false;
    }

    // Only a dynamic library may be universal; the executables (RafficaPdf, raffica) hold exactly one architecture.
    public static bool MayBeUniversal(string fileName) => fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);

    // otool and vtool read the first slice of a universal file unless told which one: name the expected slice (a thin file needs nothing).
    public static string[] SliceArguments(bool universal, string expectedArch) => universal ? ["-arch", expectedArch] : [];

    // allowUniversal: the file may hold several slices, but the expected one must be among them (the caller then reads that slice only).
    public static List<string> Violations(string otoolOutput, string lipoArchs, string vtoolOutput, string expectedArch, int[] maxMacos, Func<string, bool> bundleHasFile, bool allowUniversal = false)
    {
        var problems = new List<string>();
        var archs = lipoArchs.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (allowUniversal)
        {
            if (!archs.Contains(expectedArch, StringComparer.Ordinal))
            {
                problems.Add($"architectures are '{string.Join(' ', archs)}' (the slice '{expectedArch}' is missing)");
            }
        }
        else if (archs.Length != 1 || archs[0] != expectedArch)
        {
            problems.Add($"architectures are '{string.Join(' ', archs)}' (exactly '{expectedArch}' expected)");
        }

        var minos = MinOs(vtoolOutput);
        if (minos is null || !ElfRules.TryParseVersion(minos, out var minosVersion))
        {
            problems.Add("no macOS deployment target (minos) found in the build version");
        }
        else if (ElfRules.Compare(minosVersion, maxMacos) > 0)
        {
            problems.Add($"deployment target macOS {minos} is higher than allowed ({string.Join('.', maxMacos)})");
        }

        var dependencies = Dependencies(otoolOutput, out var unparsable);
        if (dependencies.Count == 0)
        {
            problems.Add("no dependency found in the otool -L output (every dylib links libSystem): the output is empty or not understood");
        }

        foreach (var line in unparsable)
        {
            problems.Add($"otool -L line not understood: {line}");
        }

        foreach (var dependency in dependencies)
        {
            if (!DependencyAllowed(dependency, bundleHasFile))
            {
                problems.Add($"dependency not allowed: {dependency}");
            }
        }

        return problems;
    }
}

static class CheckBinary
{
    public static BinaryFormat DetectFormat(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4 && head[0] == 0x7F && head[1] == 0x45 && head[2] == 0x4C && head[3] == 0x46)
        {
            return BinaryFormat.Elf;
        }

        if (head.Length >= 4 && head[0] == 0xCF && head[1] == 0xFA && head[2] == 0xED && head[3] == 0xFE)
        {
            return BinaryFormat.MachO;
        }

        // Universal (fat): CA FE BA BE, or CA FE BA BF with 64-bit offsets.
        return IsUniversal(head) ? BinaryFormat.MachO : BinaryFormat.Unknown;
    }

    public static bool IsUniversal(ReadOnlySpan<byte> head) =>
        head.Length >= 4 && head[0] == 0xCA && head[1] == 0xFE && head[2] == 0xBA && (head[3] == 0xBE || head[3] == 0xBF);

    public static int Run(string[] args)
    {
        var files = new List<string>();
        var extraAllowed = new List<string>();
        var maxText = ElfRules.DefaultMaxGlibc;
        var maxCxxText = ElfRules.DefaultMaxGlibcxx;
        string? arch = null, maxMacosText = null, bundle = null;
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
            else if (args[i] == "--arch" && i + 1 < args.Length)
            {
                arch = args[++i];
            }
            else if (args[i] == "--max-macos" && i + 1 < args.Length)
            {
                maxMacosText = args[++i];
            }
            else if (args[i] == "--bundle" && i + 1 < args.Length)
            {
                bundle = args[++i];
            }
            else if (args[i] == "--allow-needed" && i + 1 < args.Length)
            {
                extraAllowed.Add(args[++i]);
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
            Console.Error.WriteLine("usage: check-binary <file>... [--max-glibc 2.35] [--max-glibcxx 3.4.30] [--allow-needed <lib>]... | <file> --arch <x86_64|arm64> --max-macos <x.y> --bundle <folder> | selftest");
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

            var head = new byte[4];
            using (var stream = File.OpenRead(file))
            {
                _ = stream.Read(head, 0, head.Length);
            }

            var format = DetectFormat(head);
            if (format == BinaryFormat.Unknown)
            {
                // Never guess ELF: a file that is neither ELF nor Mach-O must not pass as "ok" or fall into objdump.
                Console.Error.WriteLine($"{file}: unknown binary format (neither ELF nor Mach-O)");
                failed = true;
                continue;
            }

            if (format == BinaryFormat.MachO)
            {
                if (!CheckMachO(file, arch, maxMacosText, bundle, IsUniversal(head)))
                {
                    failed = true;
                }

                continue;
            }

            var symbols = Objdump(["-T", file]);
            var headers = Objdump(["-p", file]);
            if (symbols is null || headers is null)
            {
                failed = true;
                continue;
            }

            var problems = ElfRules.Violations(symbols, headers, max, maxText, maxCxxText, extraAllowed);
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

    private static bool CheckMachO(string file, string? arch, string? maxMacosText, string? bundle, bool universal)
    {
        if (arch is not ("x86_64" or "arm64") || maxMacosText is null || !ElfRules.TryParseVersion(maxMacosText, out var maxMacos) || bundle is null)
        {
            Console.Error.WriteLine($"{file}: a Mach-O file needs --arch <x86_64|arm64>, --max-macos <x.y> and --bundle <folder>");
            return false;
        }

        // A universal file is read on the expected slice; whether it may be universal at all is the rule's business (MayBeUniversal).
        var slice = MachORules.SliceArguments(universal, arch);
        var otool = RunTool("otool", [.. slice, "-L", file]);
        var lipo = RunTool("lipo", ["-archs", file]);
        var vtool = RunTool("vtool", [.. slice, "-show-build", file]);
        if (otool is null || lipo is null || vtool is null)
        {
            return false;
        }

        var folder = Path.GetFullPath(bundle);
        var problems = MachORules.Violations(otool, lipo, vtool, arch, maxMacos, name => File.Exists(Path.Combine(folder, name)),
            allowUniversal: MachORules.MayBeUniversal(Path.GetFileName(file)));
        if (problems.Count == 0)
        {
            Console.WriteLine($"{file}: ok");
            return true;
        }

        foreach (var p in problems)
        {
            Console.Error.WriteLine($"{file}: {p}");
        }

        return false;
    }

    // Runs one of the Xcode command line tools (otool, lipo, vtool); null with a message when it is missing or fails.
    private static string? RunTool(string tool, string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in arguments)
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi)!;
            var stderrTask = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                Console.Error.WriteLine($"{arguments[^1]}: {tool} {arguments[0]} failed: {stderrTask.Result.Trim()}");
                return null;
            }

            return stdout;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"{tool} not found: it is part of the Xcode command line tools (xcode-select --install); a Mach-O file can only be checked on a Mac");
            return null;
        }
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

static class SelfTest
{
    private static int s_failures;

    public static int Run()
    {
        const string Clean = "libtesseract.dylib:\n\t/usr/lib/libSystem.B.dylib (compatibility version 1.0.0, current version 1351.0.0)\n\t/usr/lib/libc++.1.dylib (compatibility version 1.0.0, current version 1900.180.0)\n";
        const string Own = "libtesseract.dylib:\n\t@loader_path/libtesseract.dylib (compatibility version 0.0.0, current version 0.0.0)\n\t/usr/lib/libSystem.B.dylib (compatibility version 1.0.0)\n";
        const string Homebrew = "libtesseract.dylib:\n\t/usr/lib/libSystem.B.dylib (compatibility version 1.0.0)\n\t/opt/homebrew/opt/libpng/lib/libpng16.16.dylib (compatibility version 53.0.0)\n";
        const string Rpath = "libtesseract.dylib:\n\t@rpath/libleptonica.dylib (compatibility version 6.0.0)\n\t/usr/lib/libSystem.B.dylib (compatibility version 1.0.0)\n";
        const string Local = "libtesseract.dylib:\n\t/usr/local/lib/libfoo.dylib (compatibility version 1.0.0)\n";
        const string Framework = "libtesseract.dylib:\n\t/System/Library/Frameworks/Accelerate.framework/Versions/A/Accelerate (compatibility version 1.0.0)\n";
        const string Vtool13 = "Load command 9\n      cmd LC_BUILD_VERSION\n  cmdsize 32\n platform MACOS\n    minos 13.0\n      sdk 15.2\n";
        const string Vtool15 = "Load command 9\n      cmd LC_BUILD_VERSION\n  cmdsize 32\n platform MACOS\n    minos 15.0\n      sdk 15.2\n";
        int[] max14 = [14, 0];
        static bool None(string _) => false;
        static bool All(string _) => true;

        Expect("macho: system libraries only pass", MachORules.Violations(Clean, "arm64", Vtool13, "arm64", max14, None).Count, 0);
        Expect("macho: its own install name (@loader_path, file in the bundle) passes", MachORules.Violations(Own, "arm64", Vtool13, "arm64", max14, All).Count, 0);
        Expect("macho: a Homebrew path fails", MachORules.Violations(Homebrew, "arm64", Vtool13, "arm64", max14, All).Count, 1);
        Expect("macho: /usr/local fails", MachORules.Violations(Local, "arm64", Vtool13, "arm64", max14, All).Count, 1);
        Expect("macho: a system framework passes", MachORules.Violations(Framework, "arm64", Vtool13, "arm64", max14, None).Count, 0);
        Expect("macho: @rpath fails when the bundle lacks the file", MachORules.Violations(Rpath, "arm64", Vtool13, "arm64", max14, None).Count, 1);
        Expect("macho: @rpath passes when the bundle has the file", MachORules.Violations(Rpath, "arm64", Vtool13, "arm64", max14, All).Count, 0);
        Expect("macho: the wrong architecture fails", MachORules.Violations(Clean, "arm64", Vtool13, "x86_64", max14, All).Count, 1);
        Expect("macho: a fat file fails", MachORules.Violations(Clean, "x86_64 arm64", Vtool13, "arm64", max14, All).Count, 1);
        Expect("macho: minos 15.0 against max 14.0 fails", MachORules.Violations(Clean, "arm64", Vtool15, "arm64", max14, All).Count, 1);
        Expect("macho: minos 13.0 against max 14.0 passes", MachORules.Violations(Clean, "arm64", Vtool13, "arm64", max14, All).Count, 0);
        Expect("macho: no minos in the vtool text fails", MachORules.Violations(Clean, "arm64", "nothing", "arm64", max14, All).Count, 1);
        Expect("macho: empty otool output fails", MachORules.Violations(string.Empty, "arm64", Vtool13, "arm64", max14, All).Count, 1);
        Expect("macho: a header line alone (no dependency) fails", MachORules.Violations("libtesseract.dylib:\n", "arm64", Vtool13, "arm64", max14, All).Count, 1);
        Expect("macho: an unparsable indented line fails", MachORules.Violations(Clean + "\t/usr/lib/libz.1.dylib\n", "arm64", Vtool13, "arm64", max14, All).Count, 1);
        Expect("macho: an unparsable line is named", MachORules.Violations(Clean + "\tsurprise\n", "arm64", Vtool13, "arm64", max14, All).Any(p => p.Contains("surprise", StringComparison.Ordinal)), true);
        Expect("macho: the normal sample still passes", MachORules.Violations(Clean, "arm64", Vtool13, "arm64", max14, All).Count, 0);
        Expect("macho: the legacy LC_VERSION_MIN_MACOSX form is read", MachORules.Violations(Clean, "arm64", "      cmd LC_VERSION_MIN_MACOSX\n  cmdsize 16\n  version 13.0\n      sdk 15.2\n", "arm64", max14, All).Count, 0);

        // A universal (fat) .dylib passes when the expected slice is in it; the executables stay single-architecture.
        Expect("macho: a universal file with the expected slice passes when universal files are allowed", MachORules.Violations(Clean, "x86_64 arm64", Vtool13, "arm64", max14, All, allowUniversal: true).Count, 0);
        Expect("macho: a universal file without the expected slice fails", MachORules.Violations(Clean, "x86_64 arm64e", Vtool13, "arm64", max14, All, allowUniversal: true).Count, 1);
        Expect("macho: a thin file of another architecture still fails when universal files are allowed", MachORules.Violations(Clean, "x86_64", Vtool13, "arm64", max14, All, allowUniversal: true).Count, 1);
        Expect("macho: a universal file stays refused when universal files are not allowed (the executables)", MachORules.Violations(Clean, "x86_64 arm64", Vtool13, "arm64", max14, All).Count, 1);
        Expect("macho: a universal file is checked on its slice (a high minos of the slice fails)", MachORules.Violations(Clean, "x86_64 arm64", Vtool15, "arm64", max14, All, allowUniversal: true).Count, 1);
        Expect("macho: a universal file gets -arch for otool and vtool", string.Join(' ', MachORules.SliceArguments(true, "arm64")), "-arch arm64");
        Expect("macho: a thin file gets no -arch", MachORules.SliceArguments(false, "arm64").Length, 0);
        Expect("macho: only a .dylib may be universal", (MachORules.MayBeUniversal("libSkiaSharp.dylib"), MachORules.MayBeUniversal("RafficaPdf"), MachORules.MayBeUniversal("raffica")), (true, false, false));
        Expect("format: universal Mach-O magic CA FE BA BE", CheckBinary.DetectFormat([0xCA, 0xFE, 0xBA, 0xBE]), BinaryFormat.MachO);
        Expect("format: universal 64-bit Mach-O magic CA FE BA BF", CheckBinary.DetectFormat([0xCA, 0xFE, 0xBA, 0xBF]), BinaryFormat.MachO);
        Expect("format: CA FE BA BE is universal, CF FA ED FE is thin", (CheckBinary.IsUniversal([0xCA, 0xFE, 0xBA, 0xBE]), CheckBinary.IsUniversal([0xCF, 0xFA, 0xED, 0xFE])), (true, false));
        Expect("format: a file of an unknown format is a failure (never an ELF check)", CheckUnknownFile([0x4D, 0x5A, 0x90, 0x00]), 1);
        Expect("format: a file shorter than four bytes is a failure", CheckUnknownFile([0x7F]), 1);

        // The ELF rules keep their behaviour.
        const string Symbols = "0000 DF *UND*\t0000 (GLIBC_2.34) printf\n0000 DF *UND*\t0000 (GLIBCXX_3.4.30) _ZSt\n";
        const string Headers = "  NEEDED               libc.so.6\n  NEEDED               libstdc++.so.6\n";
        int[] max = [2, 35];
        Expect("elf: a file within the limits passes", ElfRules.Violations(Symbols, Headers, max, "2.35").Count, 0);
        Expect("elf: GLIBC 2.38 fails", ElfRules.Violations(Symbols.Replace("2.34", "2.38", StringComparison.Ordinal), Headers, max, "2.35").Count, 1);
        Expect("elf: a foreign NEEDED library fails", ElfRules.Violations(Symbols, Headers + "  NEEDED               libpng16.so.16\n", max, "2.35").Count, 1);
        Expect("format: ELF magic", CheckBinary.DetectFormat([0x7F, 0x45, 0x4C, 0x46]), BinaryFormat.Elf);
        Expect("format: thin 64-bit Mach-O magic", CheckBinary.DetectFormat([0xCF, 0xFA, 0xED, 0xFE]), BinaryFormat.MachO);
        Expect("format: anything else", CheckBinary.DetectFormat([0x4D, 0x5A, 0, 0]), BinaryFormat.Unknown);

        Console.WriteLine(s_failures == 0 ? "selftest passed" : $"selftest: {s_failures} failure(s)");
        return s_failures == 0 ? 0 : 1;
    }

    // Runs the real command on a temporary file with these bytes (an unknown format must end the run with 1 before any tool is started).
    private static int CheckUnknownFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "raffica-check-binary-selftest-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, bytes);
        try
        {
            return CheckBinary.Run([path]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Expect<T>(string what, T actual, T expected)
    {
        var ok = EqualityComparer<T>.Default.Equals(actual, expected);
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
        {
            s_failures++;
        }
    }
}
