#:property PublishAot=false

// Packaging tool of Raffica PDF (grows with Milestone 8; further subcommands are added to Commands).
//   dotnet run tools/package.cs -- layout --rid <win-x64|linux-x64> --app <dir> --cli <dir> --out <dir> [--repo <root>] [--native <dir>]
//   dotnet run tools/package.cs -- zip --layout <dir> --version <x.y.z> --out <file>
//   dotnet run tools/package.cs -- sums <file>... --out <SHA256SUMS.txt>
//   dotnet run tools/package.cs -- sums-verify --file <SHA256SUMS.txt> --sha256 <hex>
//   dotnet run tools/package.cs -- inno-install [--dir <dir>]                       (Windows)
//   dotnet run tools/package.cs -- installer --layout <dir> --version <x.y.z> --out <file> [--iscc <ISCC.exe>] [--repo <root>]   (Windows)
//   dotnet run tools/package.cs -- installer-smoke <setup.exe> [--repo <root>]          (Windows)
//   dotnet run tools/package.cs -- update-smoke <setup-old.exe> <setup-new.exe> --key <test-key.txt> [--key-id <id>]   (Windows, test build)
//   dotnet run tools/package.cs -- update-keygen --id <id> --private-out <file>   (the owner, once; never in a repository)
//   dotnet run tools/package.cs -- update-manifest --version <x.y.z> --sums <SHA256SUMS-windows.txt> <SHA256SUMS-linux.txt> [<SHA256SUMS-macos.txt>] --sizes <assets.json> --out <update-manifest.json>
//   dotnet run tools/package.cs -- update-sign --manifest <file> --key-env RAFFICA_UPDATE_SIGNING_KEY --id <id> --out <file.sig> [--source <UpdateTrust.cs>]
//   dotnet run tools/package.cs -- update-verify [--manifest <file> --sig <file>] (--keys-from-source | --source <file>)
//   dotnet run tools/package.cs -- deb --layout <dir> --version <x.y.z> --out <raffica-pdf_x.y.z_amd64.deb> [--repo <root>]   (Linux)
//   dotnet run tools/package.cs -- deb-smoke <file.deb> [--repo <root>]                  (Linux)
//   dotnet run tools/package.cs -- runtime-fetch [--out <file>]                         (Linux)
//   dotnet run tools/package.cs -- appimage --layout <dir> --version <x.y.z> --out <RafficaPdf-x.y.z-x86_64.AppImage> [--runtime <file>] [--repo <root>]   (Linux)
//   dotnet run tools/package.cs -- appimage-smoke <file.AppImage> [--repo <root>]        (Linux)
//   dotnet run tools/package.cs -- macos-app --rid <osx-arm64|osx-x64> --app <dir> --cli <dir> --out <dir> --version <x.y.z> [--native <dir>] [--repo <root>]   (macOS)
//   dotnet run tools/package.cs -- selftest
// Run from the repository root. Only inno-install and runtime-fetch use the network (one download each, with a pinned SHA-256).

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

return Cli.Run(args);

static class Cli
{
    private const string Usage =
        "Usage: dotnet run tools/package.cs -- <command>\n" +
        "  layout --rid <rid> --app <dir> --cli <dir> --out <dir> [--native <dir>]   lays out the package layout (app, cli/, LICENSE, THIRD-PARTY-NOTICES.txt; with --native the OCR library and tessdata/)\n" +
        "  zip --layout <dir> --version <x.y.z> --out <file>                 reproducible ZIP file, top-level folder RafficaPdf-<version>/\n" +
        "  sums <file>... --out <file>                                       SHA-256 sums (<sha256>  <file name>)\n" +
        "  sums-verify --file <file> --sha256 <hex>                          checks a SHA256SUMS file against the sum of the build job (empty or wrong: error)\n" +
        "  inno-install [--dir <dir>]                                        downloads Inno Setup (pinned version, SHA-256 checked) into the user profile (Windows)\n" +
        "  installer --layout <dir> --version <x.y.z> --out <file> [--iscc <ISCC.exe>]   builds RafficaPdf-<version>-win-x64-setup.exe (Windows)\n" +
        "  installer-smoke <setup.exe>                                       installs silently, checks, installs again, uninstalls (Windows)\n" +
        "  update-smoke <setup-old.exe> <setup-new.exe> --key <test-key.txt> [--key-id <id>]   update through the helper mode (Windows, test build)\n" +
        "  update-keygen --id <id> --private-out <file>                      creates the signing key: private half only into the file, public half as an entry for UpdateTrust.Keys\n" +
        "  update-manifest --version <x.y.z> --sums <SHA256SUMS-windows.txt> <SHA256SUMS-linux.txt> [<SHA256SUMS-macos.txt>] --sizes <assets.json> --out <file>   update manifest of the release\n" +
        "  update-sign --manifest <file> --key-env <variable> --id <id> --out <file.sig> [--source <UpdateTrust.cs>]   signs (the key comes only from the environment) and verifies\n" +
        "  update-verify [--manifest <file> --sig <file>] (--keys-from-source | --source <file>)   checks signature and content like the app (without a manifest only the keys)\n" +
        "  deb --layout <dir> --version <x.y.z> --out <file>                 builds raffica-pdf_<version>_amd64.deb (Linux, dpkg-deb)\n" +
        "  deb-smoke <file.deb>                                              unpacks, checks launcher and symlink, runs the CLI, lintian/desktop-file-validate when present (Linux)\n" +
        "  runtime-fetch [--out <file>]                                      downloads the type2 runtime (pinned version, SHA-256 checked) to ~/.cache/RafficaBuildTools\n" +
        "  runtime-sources --out <dir> [--from <dir>]                        downloads the sources of the AppImage runtime (libfuse, type2-runtime; SHA-256 checked) for the release\n" +
        "  appimage --layout <dir> --version <x.y.z> --out <file> [--runtime <file>]   builds RafficaPdf-<version>-x86_64.AppImage (Linux, mksquashfs)\n" +
        "  appimage-smoke <file.AppImage>                                    checks the runtime, unpacks, runs the CLI, starts the app without FUSE (Linux)\n" +
        "  macos-app --rid <osx-arm64|osx-x64> --app <dir> --cli <dir> --out <dir> --version <x.y.z> [--native <dir>]   builds \"Raffica PDF.app\" with icon and ad-hoc signature (macOS only)\n" +
        "  dmg --app <dir> --rid <osx-arm64|osx-x64> --version <x.y.z> --out <file>   builds RafficaPdf-<version>-macos-<arm64|x64>.dmg with the app and a link to /Applications (macOS only)\n" +
        "  dmg-smoke --dmg <file> --rid <rid> [--ceiling-seconds 5]          attaches, copies out, checks signature, architectures and a start of the app (macOS only)\n" +
        "  selftest                                                          checks the parts of the tool";

    // Command name -> handler (receives the arguments after the command name). Add new subcommands here.
    private static readonly Dictionary<string, Func<string[], int>> Commands = new(StringComparer.Ordinal)
    {
        ["layout"] = Layout.Run,
        ["zip"] = ZipCommand.Run,
        ["sums"] = Sums.Run,
        ["sums-verify"] = SumsVerify.Run,
        ["inno-install"] = InnoInstall.Run,
        ["installer"] = Installer.Run,
        ["installer-smoke"] = InstallerSmoke.Run,
        ["update-smoke"] = UpdateSmoke.Run,
        ["update-keygen"] = UpdateKeygen.Run,
        ["update-manifest"] = UpdateManifestCommand.Run,
        ["update-sign"] = UpdateSign.Run,
        ["update-verify"] = UpdateVerify.Run,
        ["deb"] = Deb.Run,
        ["deb-smoke"] = DebSmoke.Run,
        ["runtime-fetch"] = RuntimeFetch.Run,
        ["runtime-sources"] = RuntimeSourcesCommand.Run,
        ["appimage"] = AppImage.Run,
        ["appimage-smoke"] = AppImageSmoke.Run,
        ["macos-app"] = MacApp.Run,
        ["dmg"] = Dmg.Run,
        ["dmg-smoke"] = DmgSmoke.Run,
        ["selftest"] = SelfTest.Run,
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (!Commands.TryGetValue(args[0], out var handler))
        {
            Console.Error.WriteLine($"unknown command: {args[0]}");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        return handler(args[1..]);
    }
}

static partial class ElfRules
{
    public const string DefaultMaxGlibc = "2.35";

    // libstdc++ of Ubuntu 22.04 and Debian 12 (GCC 12) exports GLIBCXX up to 3.4.30.
    public const string DefaultMaxGlibcxx = "3.4.30";

    public static readonly string[] AllowedNeeded =
    [
        "libc.so.6", "libm.so.6", "libdl.so.2", "libpthread.so.0", "librt.so.1", "libz.so.1",
        "ld-linux-x86-64.so.2", "libstdc++.so.6", "libgcc_s.so.1",
        // libSkiaSharp.so (real linux-x64 publish, Milestone 8 task 11): the only NEEDED library beyond the glibc core.
        // Covered by the .deb Depends (packaging/linux/control.template: libfontconfig1) and, for the AppImage, by the
        // documented host-library assumption (packaging/README.md: libfontconfig1 and the X11 libraries come from the host).
        "libfontconfig.so.1",
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

static class Options
{
    // Parses "--name value" pairs; everything else is positional. Returns null (message written) on an unknown or valueless option.
    public static (Dictionary<string, string> Named, List<string> Positional)? Parse(string[] args, params string[] known)
    {
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(args[i]);
            }
            else if (known.Contains(args[i], StringComparer.Ordinal) && i + 1 < args.Length)
            {
                named[args[i]] = args[++i];
            }
            else
            {
                Console.Error.WriteLine($"unknown option or missing value: {args[i]}");
                return null;
            }
        }

        return (named, positional);
    }
}

static class Layout
{
    private static readonly string[] SkippedExtensions = [".pdb", ".dbg"];

    public static string NativeLibrary(string rid) => rid switch
    {
        "win-x64" => "pdfium.dll",
        "linux-x64" => "libpdfium.so",
        "osx-arm64" or "osx-x64" => "libpdfium.dylib",
        _ => throw new ArgumentException($"unknown RID: {rid}", nameof(rid)),
    };

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--rid", "--app", "--cli", "--out", "--repo", "--native");
        if (parsed is not { } p || p.Positional.Count != 0 ||
            !p.Named.TryGetValue("--rid", out var rid) || !p.Named.TryGetValue("--app", out var app) ||
            !p.Named.TryGetValue("--cli", out var cli) || !p.Named.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine("Usage: layout --rid <win-x64|linux-x64> --app <dir> --cli <dir> --out <dir>");
            return 2;
        }

        if (rid is not ("win-x64" or "linux-x64"))
        {
            Console.Error.WriteLine($"unknown RID: {rid}");
            return 2;
        }

        var repo = p.Named.GetValueOrDefault("--repo", ".");
        var native = NativeLibrary(rid);
        var license = Path.Combine(repo, "LICENSE");
        var notices = Path.Combine(repo, "src", "Raffica.Pdf", "Resources", "ThirdPartyNotices.txt");
        var problems = new List<string>();
        foreach (var (label, dir) in new[] { ("App", app), ("CLI", cli) })
        {
            if (!Directory.Exists(dir))
            {
                problems.Add($"{label} folder missing: {dir}");
            }
            else if (!File.Exists(Path.Combine(dir, native)))
            {
                problems.Add($"{label} folder does not contain {native}: {dir}");
            }
        }

        foreach (var file in new[] { license, notices })
        {
            if (!File.Exists(file))
            {
                problems.Add($"File missing: {file}");
            }
        }

        var nativeDir = p.Named.GetValueOrDefault("--native");
        if (nativeDir is not null)
        {
            foreach (var target in NativeFiles.For(rid))
            {
                if (!File.Exists(Path.Combine(nativeDir, target)))
                {
                    problems.Add($"Folder --native does not contain {target}: {nativeDir}");
                }
            }
        }

        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            problems.Add($"Output folder is not empty: {output}");
        }

        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                Console.Error.WriteLine($"layout: {problem}");
            }

            return 1;
        }

        Directory.CreateDirectory(output);
        CopyTree(app, output, rid);
        CopyTree(cli, Path.Combine(output, "cli"), rid);
        if (nativeDir is not null)
        {
            // The OCR library sits next to the executable of the app and of the CLI (it is only loaded from there); the language data once at the root.
            foreach (var target in NativeFiles.For(rid))
            {
                var source = Path.Combine(nativeDir, target);
                Copy(source, Path.Combine(output, target), false);
                if (!target.StartsWith("tessdata/", StringComparison.Ordinal))
                {
                    Copy(source, Path.Combine(output, "cli", target), false);
                }
            }
        }

        Copy(license, Path.Combine(output, "LICENSE"), false);
        Copy(notices, Path.Combine(output, "THIRD-PARTY-NOTICES.txt"), false);
        Console.WriteLine($"layout: {Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Count()} files in {output}");
        return 0;
    }

    private static void CopyTree(string source, string target, string rid)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (SkippedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(source, file);
            var name = Path.GetFileName(file);
            var executable = rid == "linux-x64" && string.Equals(Path.GetDirectoryName(relative), string.Empty, StringComparison.Ordinal) &&
                (name == "RafficaPdf" || name == "raffica");
            Copy(file, Path.Combine(target, relative), executable);
        }
    }

    // Executables get 0755, everything else 0644, so the ZIP does not depend on the build machine's umask.
    private static void Copy(string from, string to, bool executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(to, executable
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }
}

static class ZipCommand
{
    // Fixed timestamp so the archive does not depend on the build time (ZIP stores local time, no zone).
    public static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--layout", "--version", "--out");
        if (parsed is not { } p || p.Positional.Count != 0 ||
            !p.Named.TryGetValue("--layout", out var layout) || !p.Named.TryGetValue("--version", out var version) ||
            !p.Named.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine("Usage: zip --layout <dir> --version <x.y.z> --out <file>");
            return 2;
        }

        if (!Directory.Exists(layout))
        {
            Console.Error.WriteLine($"zip: layout folder missing: {layout}");
            return 1;
        }

        var files = Directory.EnumerateFiles(layout, "*", SearchOption.AllDirectories)
            .Select(f => (Path: f, Name: Path.GetRelativePath(layout, f).Replace('\\', '/')))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"zip: layout folder is empty: {layout}");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.Delete(output);
        using (var stream = File.Create(output))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var (path, name) in files)
            {
                var entry = archive.CreateEntry($"RafficaPdf-{version}/{name}", CompressionLevel.Optimal);
                entry.LastWriteTime = FixedTime;
                var executable = !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
                entry.ExternalAttributes = unchecked((int)((executable ? 0x81EDu : 0x81A4u) << 16)); // regular file, 0755 / 0644
                using var target = entry.Open();
                using var source = File.OpenRead(path);
                source.CopyTo(target);
            }
        }

        Console.WriteLine($"zip: {files.Count} files in {output}");
        return 0;
    }
}

static class Sums
{
    public static string Line(string file)
    {
        using var stream = File.OpenRead(file);
        return $"{Convert.ToHexStringLower(SHA256.HashData(stream))}  {Path.GetFileName(file)}\n";
    }

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--out");
        if (parsed is not { } p || p.Positional.Count == 0 || !p.Named.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine("Usage: sums <file>... --out <SHA256SUMS.txt>");
            return 2;
        }

        var missing = p.Positional.Where(f => !File.Exists(f)).ToList();
        if (missing.Count > 0)
        {
            foreach (var f in missing)
            {
                Console.Error.WriteLine($"sums: file not found: {f}");
            }

            return 1;
        }

        File.WriteAllText(output, string.Concat(p.Positional.Select(Line)), new UTF8Encoding(false));
        return 0;
    }
}

// sums-verify --file <path> --sha256 <hex>: the release workflow compares each downloaded SHA256SUMS file with the hash
// that the build job reported as a job output, before anything is signed. Fails closed: a missing, empty or malformed
// expectation is a failure, never a pass.
static class SumsVerify
{
    // null when the file has the expected SHA-256 (hex, either case); otherwise why not.
    public static string? Problem(string file, string expected)
    {
        var hex = expected.Trim();
        if (hex.Length != 64 || !hex.All(char.IsAsciiHexDigit))
        {
            return hex.Length == 0
                ? "the expected SHA-256 is empty (the build job delivered none)"
                : "the expected SHA-256 is not a 64-digit hex number";
        }

        if (!File.Exists(file))
        {
            return $"File not found: {file}";
        }

        using var stream = File.OpenRead(file);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        return string.Equals(actual, hex, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"SHA-256 of {file} is {actual}, the build job reported {hex.ToLowerInvariant()}";
    }

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--file", "--sha256");
        if (parsed is not { } p || p.Positional.Count != 0 ||
            !p.Named.TryGetValue("--file", out var file) || !p.Named.TryGetValue("--sha256", out var sha256))
        {
            Console.Error.WriteLine("Usage: sums-verify --file <file> --sha256 <hex>");
            return 2;
        }

        if (Problem(file, sha256) is { } problem)
        {
            Console.Error.WriteLine($"sums-verify: {problem}");
            return 1;
        }

        Console.WriteLine($"sums-verify: {file} matches the sum of the build job");
        return 0;
    }
}

// Downloads of build inputs with a size and SHA-256 fixed in this file (Inno Setup, the AppImage runtime).
static class Pinned
{
    // null when the value is a SHA-256 in lower-case hex; otherwise why it is not pinned (name = the constant in this file).
    public static string? PinProblem(string pinned, string name) =>
        pinned.Length == 64 && pinned.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'))
            ? null
            : $"Checksum not pinned (hash not pinned): {name} in tools/package.cs must be a lowercase SHA-256";

    // null when the stream has the expected size and the pinned SHA-256; otherwise the reason.
    public static string? VerifyDownload(Stream stream, long expectedSize, string pinned, string name)
    {
        if (PinProblem(pinned, name) is { } problem)
        {
            return problem;
        }

        if (stream.Length != expectedSize)
        {
            return $"Size {stream.Length} instead of {expectedSize} bytes";
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return hash == pinned ? null : $"SHA-256 {hash} instead of {pinned}";
    }

    // The same check for a file; a missing file is a reason too.
    public static string? VerifyFile(string file, long expectedSize, string pinned, string name)
    {
        if (!File.Exists(file))
        {
            return $"File missing: {file}";
        }

        using var stream = File.OpenRead(file);
        return VerifyDownload(stream, expectedSize, pinned, name);
    }

    // Downloads url to target (over HTTPS, through the configured proxy); the caller verifies before using the file.
    public static void Fetch(string url, string target)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var source = response.Content.ReadAsStream();
        using var file = File.Create(target);
        source.CopyTo(file);
    }
}

// The pinned Inno Setup 6 (build tool only, see Docs/DECISIONS.md, entry "Raffica PDF: packaging and release (M2.6)").
static class InnoSetup
{
    public const string Version = "6.7.3";
    public const string Url = "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe";

    // From the release's own signature file innosetup-6.7.3.exe.issig (format issig-v2, ECDSA P-256 signature checked
    // against the project key def02.ispublickey of tag is-6_7_3 in jrsoftware/issrc).
    public const string Sha256 = "9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732";
    public const long Size = 10592232;

    public static string DefaultDir(string localAppData) => Path.Combine(localAppData, "RafficaBuildTools", $"InnoSetup-{Version}");

    public static string? PinProblem(string pinned) => Pinned.PinProblem(pinned, "InnoSetup.Sha256");

    public static string? VerifyDownload(Stream stream, long expectedSize, string pinned) =>
        Pinned.VerifyDownload(stream, expectedSize, pinned, "InnoSetup.Sha256");

    // Silent, current user only, portable mode of Inno Setup's own installer (no uninstall entry, no .iss association).
    public static string InstallArguments(string dir) =>
        InnoArguments.Join(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/CURRENTUSER", "/PORTABLE=1", "/NOICONS", "/DIR=" + dir]);
}

// Command lines for programs built with Inno Setup (iscc.exe, Setup, the uninstaller). They do not split like the
// C runtime: outside quotes a character <= ' ' separates, a '"' toggles quoting and is dropped, a backslash is literal
// (GetParamStr in Projects/Src/Shared.CommonFunc.pas of jrsoftware/issrc). So an argument can never contain '"'.
static class InnoArguments
{
    public static string Join(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Argument with a quotation mark cannot be represented for Inno Setup: {argument}", nameof(argument));
        }

        return argument.Length > 0 && argument.All(c => c > ' ') ? argument : $"\"{argument}\"";
    }

    // The same splitting as Inno Setup, for the self-test.
    public static List<string> Split(string commandLine)
    {
        var result = new List<string>();
        var i = 0;
        while (i < commandLine.Length && commandLine[i] <= ' ')
        {
            i++;
        }

        while (i < commandLine.Length)
        {
            var current = new StringBuilder();
            var inQuote = false;
            while (i < commandLine.Length && (commandLine[i] > ' ' || inQuote))
            {
                if (commandLine[i] == '"')
                {
                    inQuote = !inQuote;
                }
                else
                {
                    current.Append(commandLine[i]);
                }

                i++;
            }

            result.Add(current.ToString());
            while (i < commandLine.Length && commandLine[i] <= ' ')
            {
                i++;
            }
        }

        return result;
    }
}

static class InnoInstall
{
    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--dir");
        if (parsed is not { } p || p.Positional.Count != 0)
        {
            Console.Error.WriteLine("Usage: inno-install [--dir <dir>]");
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("inno-install: Inno Setup runs only on Windows");
            return 1;
        }

        if (InnoSetup.PinProblem(InnoSetup.Sha256) is { } pinProblem)
        {
            Console.Error.WriteLine($"inno-install: {pinProblem}");
            return 1;
        }

        var dir = Path.GetFullPath(p.Named.GetValueOrDefault("--dir") ??
            InnoSetup.DefaultDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        var iscc = Path.Combine(dir, "ISCC.exe");
        if (File.Exists(iscc))
        {
            Console.WriteLine($"inno-install: already present: {iscc}");
            return 0;
        }

        var download = Path.Combine(Path.GetTempPath(), $"innosetup-{InnoSetup.Version}-{Guid.NewGuid():N}.exe");
        try
        {
            Console.WriteLine($"inno-install: downloading {InnoSetup.Url}");
            Pinned.Fetch(InnoSetup.Url, download);
            if (Pinned.VerifyFile(download, InnoSetup.Size, InnoSetup.Sha256, "InnoSetup.Sha256") is { } problem)
            {
                Console.Error.WriteLine($"inno-install: download refused, nothing executed: {problem}");
                return 1;
            }

            Console.WriteLine($"inno-install: SHA-256 matches, installing to {dir}");
            var exitCode = ProcessRunner.RunInno(download, InnoSetup.InstallArguments(dir), TimeSpan.FromMinutes(10));
            if (exitCode != 0 || !File.Exists(iscc))
            {
                Console.Error.WriteLine($"inno-install: installation failed (exit {exitCode}), {iscc} is missing");
                return 1;
            }

            Console.WriteLine($"inno-install: Inno Setup {InnoSetup.Version} in {dir}");
            return 0;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"inno-install: {e.Message}");
            return 1;
        }
        finally
        {
            File.Delete(download);
        }
    }
}

static class ProcessRunner
{
    // Starts a program built with Inno Setup with an Inno command line (no shell, so '&' is an ordinary character).
    // Returns the exit code, or -1 after the timeout (the process is killed).
    public static int RunInno(string file, string arguments, TimeSpan timeout, StringBuilder? output = null)
    {
        var psi = new ProcessStartInfo(file, arguments) { UseShellExecute = false, RedirectStandardOutput = output is not null, RedirectStandardError = output is not null };
        using var process = Process.Start(psi) ?? throw new IOException($"{file} could not be started");
        Task<string>? stdout = null;
        Task<string>? stderr = null;
        if (output is not null)
        {
            stdout = process.StandardOutput.ReadToEndAsync();
            stderr = process.StandardError.ReadToEndAsync();
        }

        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return -1;
        }

        if (output is not null)
        {
            output.Append(stdout!.Result).Append(stderr!.Result);
        }

        return process.ExitCode;
    }
}

static partial class Installer
{
    // Generated once (2026-10-19), never change: Setup finds earlier installations and the uninstaller by it.
    // packaging/windows/raffica-pdf.iss has it as AppId={{<guid>} (IssRules checks that both agree).
    public const string AppId = "E2602600-DBDA-4741-8773-A1D1284E2562";

    // Inno Setup appends "_is1" to the AppId for the uninstall key (HKCU or HKLM, by install mode).
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + AppId + "}_is1";

    public static readonly string[] RequiredLayoutFiles =
        ["RafficaPdf.exe", "pdfium.dll", "cli/raffica.exe", "cli/pdfium.dll", "LICENSE", "THIRD-PARTY-NOTICES.txt"];

    [GeneratedRegex(@"^(?<a>0|[1-9]\d*)\.(?<b>0|[1-9]\d*)\.(?<c>0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemVer();

    public static string SetupFileName(string version) => $"RafficaPdf-{version}-win-x64-setup.exe";

    // x.y.z[-pre] -> "x.y.z.0" for the Windows file version (each part at most 65535); null if it is no such version.
    public static string? NumericVersion(string version)
    {
        var m = SemVer().Match(version);
        if (!m.Success)
        {
            return null;
        }

        var parts = new[] { m.Groups["a"].Value, m.Groups["b"].Value, m.Groups["c"].Value };
        return parts.All(x => x.Length <= 5 && int.Parse(x, CultureInfo.InvariantCulture) <= 65535) ? string.Join('.', parts) + ".0" : null;
    }

    public static List<string> MissingLayoutFiles(string layout) =>
        RequiredLayoutFiles.Where(f => !File.Exists(Path.Combine(layout, f))).ToList();

    public static List<string> IsccArguments(string script, string layout, string repo, string version, string outputDir) =>
    [
        "/DAppVersion=" + version,
        "/DNumericVersion=" + NumericVersion(version),
        "/DLayoutDir=" + Path.TrimEndingDirectorySeparator(layout),
        "/DIconFile=" + Path.Combine(repo, "src", "Raffica.Pdf", "Assets", "raffica-pdf.ico"),
        "/O" + Path.TrimEndingDirectorySeparator(outputDir),
        "/F" + Path.GetFileNameWithoutExtension(SetupFileName(version)),
        script,
    ];

    // iscc prints warnings on stdout as "Warning: ..." (SCompilerStatusWarning); here a warning is an error.
    public static List<string> Warnings(string isccOutput) =>
        isccOutput.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("Warning:", StringComparison.Ordinal)).ToList();

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--layout", "--version", "--out", "--iscc", "--repo");
        if (parsed is not { } p || p.Positional.Count != 0 ||
            !p.Named.TryGetValue("--layout", out var layout) || !p.Named.TryGetValue("--version", out var version) ||
            !p.Named.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine("Usage: installer --layout <dir> --version <x.y.z> --out <RafficaPdf-<version>-win-x64-setup.exe> [--iscc <ISCC.exe>] [--repo <root>]");
            return 2;
        }

        if (NumericVersion(version) is null)
        {
            Console.Error.WriteLine($"installer: invalid version (expected x.y.z, optionally -prerelease): {version}");
            return 2;
        }

        if (Path.GetFileName(output) != SetupFileName(version))
        {
            Console.Error.WriteLine($"installer: the output file must be named {SetupFileName(version)}: {output}");
            return 2;
        }

        var repo = Path.GetFullPath(p.Named.GetValueOrDefault("--repo", "."));
        var script = Path.Combine(repo, "packaging", "windows", "raffica-pdf.iss");
        var problems = MissingLayoutFiles(layout).Select(f => $"Layout does not contain {f}: {layout}").ToList();
        foreach (var file in new[] { script, Path.Combine(repo, "src", "Raffica.Pdf", "Assets", "raffica-pdf.ico") })
        {
            if (!File.Exists(file))
            {
                problems.Add($"File missing: {file}");
            }
        }

        if (problems.Count > 0)
        {
            problems.ForEach(x => Console.Error.WriteLine($"installer: {x}"));
            return 1;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("installer: iscc.exe runs only on Windows");
            return 1;
        }

        var iscc = p.Named.GetValueOrDefault("--iscc") ??
            Path.Combine(InnoSetup.DefaultDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), "ISCC.exe");
        if (!File.Exists(iscc))
        {
            Console.Error.WriteLine($"installer: {iscc} is missing (dotnet run tools/package.cs -- inno-install)");
            return 1;
        }

        var fullOutput = Path.GetFullPath(output);
        var outputDir = Path.GetDirectoryName(fullOutput)!;
        Directory.CreateDirectory(outputDir);
        File.Delete(fullOutput);
        var arguments = InnoArguments.Join(IsccArguments(script, Path.GetFullPath(layout), repo, version, outputDir));
        Console.WriteLine($"installer: {iscc} {arguments}");
        var log = new StringBuilder();
        var exitCode = ProcessRunner.RunInno(iscc, arguments, TimeSpan.FromMinutes(20), log);
        Console.WriteLine(log.ToString().TrimEnd());
        var warnings = Warnings(log.ToString());
        if (exitCode != 0 || warnings.Count > 0 || !File.Exists(fullOutput))
        {
            Console.Error.WriteLine($"installer: failed (iscc exit {exitCode}, {warnings.Count} warning(s), file {(File.Exists(fullOutput) ? "present" : "missing")})");
            warnings.ForEach(w => Console.Error.WriteLine($"installer: {w}"));
            return 1;
        }

        Console.WriteLine($"installer: {fullOutput}");
        return 0;
    }
}

// Checks the rules of packaging/windows/raffica-pdf.iss that the plan fixes (Milestone 8, task 8), without iscc.
static class IssRules
{
    private sealed record Script(Dictionary<string, string> Setup, Dictionary<string, List<Dictionary<string, string>>> Entries, string Code);

    private static Script Parse(string text)
    {
        var setup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        var code = new StringBuilder();
        var section = string.Empty;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            if (section.Equals("Code", StringComparison.OrdinalIgnoreCase))
            {
                if (!line.StartsWith("//", StringComparison.Ordinal))
                {
                    code.Append(line).Append('\n');
                }

                continue;
            }

            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (section.Equals("Setup", StringComparison.OrdinalIgnoreCase))
            {
                var eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0)
                {
                    setup[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }

                continue;
            }

            if (!entries.TryGetValue(section, out var list))
            {
                entries[section] = list = [];
            }

            list.Add(Parameters(line));
        }

        return new Script(setup, entries, code.ToString());
    }

    // "Name: value; Name: "quoted; value"" -> parameters; inside quotes "" is a literal quote.
    private static Dictionary<string, string> Parameters(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < line.Length)
        {
            var colon = line.IndexOf(':', i);
            if (colon < 0)
            {
                break;
            }

            var name = line[i..colon].Trim();
            i = colon + 1;
            while (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            var value = new StringBuilder();
            if (i < line.Length && line[i] == '"')
            {
                i++;
                while (i < line.Length && !(line[i] == '"' && (i + 1 >= line.Length || line[i + 1] != '"')))
                {
                    value.Append(line[i]);
                    i += line[i] == '"' ? 2 : 1;
                }

                i++;
                while (i < line.Length && line[i] != ';')
                {
                    i++;
                }
            }
            else
            {
                while (i < line.Length && line[i] != ';')
                {
                    value.Append(line[i++]);
                }
            }

            result[name] = value.ToString().Trim();
            i++;
        }

        return result;
    }

    private static HashSet<string> Words(string? value) =>
        new((value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    public static List<string> Problems(string text)
    {
        var problems = new List<string>();
        var s = Parse(text);

        void Require(string directive, string expected)
        {
            var actual = s.Setup.GetValueOrDefault(directive);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                problems.Add($"[Setup] {directive}={actual ?? "<missing>"}, expected {expected}");
            }
        }

        Require("AppId", "{{" + Installer.AppId + "}");
        Require("AppName", "Raffica PDF");
        Require("PrivilegesRequired", "lowest");
        Require("DefaultDirName", @"{autopf}\Raffica PDF");
        Require("CloseApplications", "yes");
        Require("ChangesAssociations", "yes");
        Require("ArchitecturesAllowed", "x64compatible");
        Require("ArchitecturesInstallIn64BitMode", "x64compatible");
        Require("LicenseFile", @"{#LayoutDir}\LICENSE");
        if (!Words(s.Setup.GetValueOrDefault("PrivilegesRequiredOverridesAllowed")).SetEquals(["commandline", "dialog"]))
        {
            problems.Add("[Setup] PrivilegesRequiredOverridesAllowed must be exactly 'commandline dialog'");
        }

        // A silent update (the helper of the app passes neither /ALLUSERS, /CURRENTUSER nor /DIR) takes the install mode and
        // the folder of the installation it finds by the AppId; both directives default to yes and must stay so.
        foreach (var directive in new[] { "UsePreviousPrivileges", "UsePreviousAppDir" })
        {
            if (s.Setup.TryGetValue(directive, out var value) && !string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"[Setup] {directive}={value}: an update must take over the mode and folder of the existing installation (yes or omit)");
            }
        }

        if (string.Equals(s.Setup.GetValueOrDefault("DisableDirPage"), "yes", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("[Setup] DisableDirPage=yes: the target folder must be selectable");
        }

        List<Dictionary<string, string>> Section(string name) => s.Entries.GetValueOrDefault(name) ?? [];

        var languages = Section("Languages").Select(e => e.GetValueOrDefault("MessagesFile")).ToList();
        if (!languages.SequenceEqual(["compiler:Default.isl", @"compiler:Languages\German.isl"]))
        {
            problems.Add(@"[Languages] expects exactly English (compiler:Default.isl) and German (compiler:Languages\German.isl)");
        }

        if (!Section("Files").Any(e => e.GetValueOrDefault("Source") == @"{#LayoutDir}\*" && e.GetValueOrDefault("DestDir") == "{app}" &&
            Words(e.GetValueOrDefault("Flags")).IsSupersetOf(["recursesubdirs", "createallsubdirs"])))
        {
            problems.Add(@"[Files] the whole layout ({#LayoutDir}\*, recursesubdirs createallsubdirs) to {app}");
        }

        const string exe = @"{app}\RafficaPdf.exe";
        var run = Section("Run");
        var register = run.FindIndex(e => e.GetValueOrDefault("Filename") == exe &&
            e.GetValueOrDefault("Parameters") == "--register-file-types={code:FileTypeScope}");
        var launch = run.FindIndex(e => e.GetValueOrDefault("Filename") == exe && !e.ContainsKey("Parameters"));
        if (register < 0)
        {
            problems.Add("[Run] entry --register-file-types={code:FileTypeScope} is missing");
        }
        else
        {
            var flags = Words(run[register].GetValueOrDefault("Flags"));
            if (!flags.Contains("runhidden") || flags.Overlaps(["postinstall", "nowait", "skipifsilent"]))
            {
                problems.Add("[Run] the registration runs hidden (runhidden), always and to the end (no postinstall/nowait/skipifsilent)");
            }
        }

        if (launch < 0 || !Words(run[launch].GetValueOrDefault("Flags")).IsSupersetOf(["postinstall", "skipifsilent", "nowait"]))
        {
            problems.Add("[Run] 'Launch Raffica PDF' needs postinstall nowait skipifsilent (graphical mode only)");
        }
        else if (register > launch)
        {
            problems.Add("[Run] the registration must come before the launch");
        }

        var unregister = Section("UninstallRun").Find(e => e.GetValueOrDefault("Filename") == exe &&
            e.GetValueOrDefault("Parameters") == "--unregister-file-types={code:FileTypeScope}");
        if (unregister is null)
        {
            problems.Add("[UninstallRun] entry --unregister-file-types={code:FileTypeScope} is missing");
        }
        else
        {
            var flags = Words(unregister.GetValueOrDefault("Flags"));
            if (!flags.Contains("runhidden") || flags.Contains("nowait") || string.IsNullOrEmpty(unregister.GetValueOrDefault("RunOnceId")))
            {
                problems.Add("[UninstallRun] the unregistration runs hidden, to the end (no nowait) and with RunOnceId");
            }
        }

        if (!s.Code.Contains("function FileTypeScope(Param: String): String;", StringComparison.Ordinal) ||
            !s.Code.Contains("IsAdminInstallMode", StringComparison.Ordinal) ||
            !s.Code.Contains("'machine'", StringComparison.Ordinal) || !s.Code.Contains("'user'", StringComparison.Ordinal))
        {
            problems.Add("[Code] FileTypeScope returns 'machine' for IsAdminInstallMode, otherwise 'user'");
        }

        var content = string.Join('\n', text.Split('\n').Select(l => l.Trim()).Where(l => !l.StartsWith(';') && !l.StartsWith("//", StringComparison.Ordinal)));
        if (content.Contains("appdata}", StringComparison.OrdinalIgnoreCase) || content.Contains("%APPDATA", StringComparison.OrdinalIgnoreCase) ||
            s.Entries.ContainsKey("UninstallDelete"))
        {
            problems.Add("the script touches user data (appdata, [UninstallDelete]); %APPDATA%\\Raffica must be kept");
        }

        return problems;
    }
}

static class InstallerSmoke
{
    private const string ProgIdKey = @"Software\Classes\Raffica.PDF";
    private const string OpenWithKey = @"Software\Classes\.pdf\OpenWithProgids";

    public static string TestDir(string temp) => Path.Combine(temp, "Raffica PDF Test ä&");

    public static string SilentInstallArguments(string dir, string log) =>
        InnoArguments.Join(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=" + dir, "/LOG=" + log]);

    public static string SilentUninstallArguments(string log) =>
        InnoArguments.Join(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=" + log]);

    // One line per file: "<relative path> <size> <last write ticks>"; a missing folder is one line "<missing>".
    public static List<string> Snapshot(string dir) =>
        !Directory.Exists(dir)
            ? ["<missing>"]
            : Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => $"{Path.GetRelativePath(dir, f)} {new FileInfo(f).Length} {File.GetLastWriteTimeUtc(f).Ticks}")
                .Order(StringComparer.Ordinal).ToList();

    // One description per path that is new, gone or changed (path = the line without its last two fields).
    public static List<string> Differences(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        static Dictionary<string, string> ByPath(IEnumerable<string> lines) => lines.ToDictionary(
            l => l.Count(c => c == ' ') >= 2 ? l[..l.LastIndexOf(' ', l.LastIndexOf(' ') - 1)] : l, l => l, StringComparer.Ordinal);
        var old = ByPath(before);
        var now = ByPath(after);
        return old.Keys.Union(now.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(k => old.GetValueOrDefault(k) != now.GetValueOrDefault(k))
            .Select(k => $"{k}: {(old.TryGetValue(k, out var o) ? o : "<was missing>")} -> {(now.TryGetValue(k, out var n) ? n : "<missing>")}")
            .ToList();
    }

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--repo");
        if (parsed is not { } p || p.Positional.Count != 1)
        {
            Console.Error.WriteLine("Usage: installer-smoke <RafficaPdf-<version>-win-x64-setup.exe> [--repo <root>]");
            return 2;
        }

        var setup = Path.GetFullPath(p.Positional[0]);
        if (!File.Exists(setup))
        {
            Console.Error.WriteLine($"installer-smoke: setup missing: {setup}");
            return 1;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("installer-smoke: runs only on Windows");
            return 1;
        }

        var pdf = Path.GetFullPath(Path.Combine(p.Named.GetValueOrDefault("--repo", "."), "testdata", "pdfs", "text-basic.pdf"));
        if (!File.Exists(pdf))
        {
            Console.Error.WriteLine($"installer-smoke: test PDF missing: {pdf}");
            return 1;
        }

        var failures = RunOnWindows(setup, pdf);
        if (failures.Count == 0)
        {
            Console.WriteLine("installer-smoke: all steps passed");
            return 0;
        }

        failures.ForEach(f => Console.Error.WriteLine($"installer-smoke: {f}"));
        return 1;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool KeyExists(Microsoft.Win32.RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        return key is not null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<string> RunOnWindows(string setup, string pdf)
    {
        var hkcu = Microsoft.Win32.Registry.CurrentUser;
        var hklm = Microsoft.Win32.Registry.LocalMachine;
        var failures = new List<string>();

        // (0) Never run over a real installation: the test uninstalls at the end and would remove it.
        foreach (var (root, path) in new[] { (hkcu, ProgIdKey), (hkcu, Installer.UninstallKey), (hklm, Installer.UninstallKey) })
        {
            if (KeyExists(root, path))
            {
                failures.Add($@"Raffica PDF is already installed or registered ({root.Name}\{path}); uninstall it first, the test would remove it");
            }
        }

        if (failures.Count > 0)
        {
            return failures;
        }

        var temp = Path.GetTempPath();
        var dir = TestDir(temp);
        var logs = Path.Combine(temp, "raffica-installer-smoke");
        Directory.CreateDirectory(logs);
        if (Directory.Exists(dir))
        {
            Console.WriteLine($"installer-smoke: removing leftovers of an earlier run: {dir}");
            Directory.Delete(dir, true);
        }

        // (d) %APPDATA%\Raffica must stay as it is; without the folder a marker file stands in for the settings.
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Raffica");
        var marker = Path.Combine(appData, "installer-smoke-marker.txt");
        var createdAppData = !Directory.Exists(appData);
        if (createdAppData)
        {
            Directory.CreateDirectory(appData);
            File.WriteAllText(marker, "Raffica PDF installer-smoke: must survive the uninstallation\n");
        }

        var before = Snapshot(appData);
        var uninstaller = Path.Combine(dir, "unins000.exe");
        try
        {
            // (a) First silent installation, exactly the documented command line.
            if (!Install(setup, dir, Path.Combine(logs, "install-1.log"), "first installation", failures))
            {
                return failures;
            }

            // (b) Files, CLI and registration.
            CheckInstalled(dir, pdf, failures);
            if (failures.Count > 0)
            {
                return failures;
            }

            // (c) Second installation over the first.
            if (!Install(setup, dir, Path.Combine(logs, "install-2.log"), "second installation over the first", failures))
            {
                return failures;
            }

            CheckInstalled(dir, pdf, failures);
            if (failures.Count > 0)
            {
                return failures;
            }

            // (d) Silent uninstallation: folder and keys gone. The uninstaller hands over to a copy of itself in %TEMP%,
            // whose last steps may still run after the exit code arrives, so wait for the result.
            var uninstallLog = Path.Combine(logs, "uninstall.log");
            var code = ProcessRunner.RunInno(uninstaller, SilentUninstallArguments(uninstallLog), TimeSpan.FromMinutes(5));
            if (code != 0)
            {
                failures.Add($"Uninstallation: exit {code} instead of 0 (log: {uninstallLog})");
                return failures;
            }

            var remaining = new List<string>();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            do
            {
                remaining.Clear();
                if (Directory.Exists(dir))
                {
                    remaining.Add($"Folder {dir}");
                }

                foreach (var path in new[] { ProgIdKey, Installer.UninstallKey })
                {
                    if (KeyExists(hkcu, path))
                    {
                        remaining.Add($@"HKCU\{path}");
                    }
                }

                using (var openWith = hkcu.OpenSubKey(OpenWithKey))
                {
                    if (openWith?.GetValue("Raffica.PDF") is not null)
                    {
                        remaining.Add($@"HKCU\{OpenWithKey} Wert Raffica.PDF");
                    }
                }

                if (remaining.Count > 0)
                {
                    Thread.Sleep(500);
                }
            }
            while (remaining.Count > 0 && DateTime.UtcNow < deadline);
            failures.AddRange(remaining.Select(r => $"still present after the uninstallation: {r} (log: {uninstallLog})"));

            failures.AddRange(Differences(before, Snapshot(appData)).Select(d => $@"%APPDATA%\Raffica changed: {d}"));
            return failures;
        }
        finally
        {
            if (failures.Count > 0 && File.Exists(uninstaller))
            {
                Console.Error.WriteLine("installer-smoke: failure, uninstalling to clean up");
                ProcessRunner.RunInno(uninstaller, SilentUninstallArguments(Path.Combine(logs, "cleanup-uninstall.log")), TimeSpan.FromMinutes(5));
            }

            if (createdAppData && File.Exists(marker))
            {
                File.Delete(marker);
                if (!Directory.EnumerateFileSystemEntries(appData).Any())
                {
                    Directory.Delete(appData);
                }
            }
        }
    }

    private static bool Install(string setup, string dir, string log, string step, List<string> failures)
    {
        var code = ProcessRunner.RunInno(setup, SilentInstallArguments(dir, log), TimeSpan.FromMinutes(10));
        if (code != 0)
        {
            failures.Add($"{step}: exit {code} instead of 0 (log: {log})");
        }

        return code == 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CheckInstalled(string dir, string pdf, List<string> failures)
    {
        foreach (var file in new[] { "RafficaPdf.exe", @"cli\raffica.exe", "unins000.exe" })
        {
            if (!File.Exists(Path.Combine(dir, file)))
            {
                failures.Add($@"File missing: {dir}\{file}");
            }
        }

        var cli = Path.Combine(dir, "cli", "raffica.exe");
        if (File.Exists(cli))
        {
            var psi = new ProcessStartInfo(cli) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("info");
            psi.ArgumentList.Add(pdf);
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
            {
                process.Kill(entireProcessTree: true);
                failures.Add("cli\\raffica.exe info: time limit of 60 s");
            }
            else if (process.ExitCode != 0 || !stdout.Contains("Pages: 2", StringComparison.Ordinal))
            {
                failures.Add($"cli\\raffica.exe info {pdf}: exit {process.ExitCode}, expected 0 and 'Pages: 2'; output '{stdout.Trim()}' / '{stderr.Result.Trim()}'");
            }
        }

        // The command must point at the installed RafficaPdf.exe ("<exe>" "%1"), path with space, umlaut and '&'.
        using var command = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ProgIdKey + @"\shell\open\command");
        var value = command?.GetValue(null) as string;
        if (value is null)
        {
            failures.Add($@"HKCU\{ProgIdKey} is missing (the registration in the [Run] section did not run or failed)");
        }
        else
        {
            var end = value.IndexOf("\" \"%1\"", StringComparison.Ordinal);
            var registered = value.StartsWith('"') && end > 0 ? value[1..end] : null;
            if (registered is null || !File.Exists(registered) || Path.GetFileName(registered) != "RafficaPdf.exe" ||
                Path.GetFileName(Path.GetDirectoryName(registered)) != Path.GetFileName(dir))
            {
                failures.Add($@"HKCU\{ProgIdKey}\shell\open\command does not point to {dir}\RafficaPdf.exe: {value}");
            }
        }

        if (!KeyExists(Microsoft.Win32.Registry.CurrentUser, Installer.UninstallKey))
        {
            failures.Add($@"HKCU\{Installer.UninstallKey} is missing (no per-user installation?)");
        }
    }
}

// End-to-end check of the Windows updater (Milestone 8.1, task 10): installs the old version silently into a test folder
// (like installer-smoke, with the same refusal over a real installation), puts the new setup program with a manifest
// signed by the test key and request.json into a staging folder below a fake %APPDATA%, starts a copy of the program in
// the helper mode (--finish-update) with a fake %APPDATA% and %LOCALAPPDATA% and then checks: RafficaPdf.exe reports the new
// version, the staging folder is gone, settings.json and the real %APPDATA%\Raffica are unchanged. The helper verifies
// the signature with the keys compiled into the program: both setup programs must come from the test build
// (-p:RafficaUpdateTestBuild=true -p:RafficaUpdateTestKey=<public half of --key>, packaging/README.md, "Manual update check"),
// whose one key has the id test-1; otherwise the helper refuses. --key is the private file of update-keygen (PKCS#8 as
// Base64; PEM is read too). Shows windows (needs a desktop session); like installer-smoke it writes the registration
// below HKCU (file types, uninstall entry) and removes it again.
static partial class UpdateSmoke
{
    // The id of the one key of the test build (src/Raffica.Core/Updates/UpdateTrust.TestBuild.cs).
    public const string TestKeyId = "test-1";

    // A private P-256 key as update-keygen writes it (PKCS#8 as Base64) or as PEM; null if it is neither.
    public static ECDsa? ReadKey(string text)
    {
        var key = ECDsa.Create();
        try
        {
            if (text.Contains("-----BEGIN", StringComparison.Ordinal))
            {
                key.ImportFromPem(text);
            }
            else
            {
                var bytes = Convert.FromBase64String(string.Concat(text.Where(c => !char.IsWhiteSpace(c))));
                key.ImportPkcs8PrivateKey(bytes, out var read);
                CryptographicOperations.ZeroMemory(bytes);
                if (read != bytes.Length)
                {
                    throw new FormatException();
                }
            }

            if (key.KeySize == 256)
            {
                return key;
            }
        }
        catch (Exception e) when (e is ArgumentException or FormatException or CryptographicException)
        {
            // Neither form.
        }

        key.Dispose();
        return null;
    }

    [GeneratedRegex(@"^RafficaPdf-(?<v>(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*))-win-x64-setup\.exe$")]
    private static partial Regex SetupName();

    public static string TestDir(string temp) => Path.Combine(temp, "Raffica PDF Update Test ä&");

    // The version of a setup program by its release name, or null.
    public static string? VersionOf(string setup) => SetupName().Match(Path.GetFileName(setup)) is { Success: true } m ? m.Groups["v"].Value : null;

    // The manifest of the release as the release job writes it (one Windows setup program).
    public static byte[] Manifest(string version, string name, long size, string sha256) =>
        Encoding.UTF8.GetBytes("{\"format\":1,\"version\":\"" + version + "\",\"files\":[{\"kind\":\"win-x64-setup\",\"name\":\"" + name +
            "\",\"size\":" + size.ToString(CultureInfo.InvariantCulture) + ",\"sha256\":\"" + sha256 + "\"}]}");

    // "<keyId>:<base64 of the IEEE P1363 ECDSA P-256 signature over the raw bytes>", as the app verifies it.
    public static string Signature(byte[] manifest, ECDsa key, string keyId) =>
        keyId + ":" + Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    // request.json as the app writes it (UpdateRequest: camelCase, the form by its name).
    public static string Request(string version, string fileName, string exePath, string targetDirectory) =>
        new System.Text.Json.Nodes.JsonObject
        {
            ["format"] = 1,
            ["version"] = version,
            ["fileName"] = fileName,
            ["form"] = "WindowsInstaller",
            ["exePath"] = exePath,
            ["targetDirectory"] = targetDirectory,
        }.ToJsonString();

    public static List<string> HelperArguments(string staging) => ["--finish-update", staging];

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--key", "--key-id");
        if (parsed is not { } p || p.Positional.Count != 2 || !p.Named.TryGetValue("--key", out var keyFile))
        {
            Console.Error.WriteLine("Usage: update-smoke <RafficaPdf-<old>-win-x64-setup.exe> <RafficaPdf-<new>-win-x64-setup.exe> --key <test-key.pem> [--key-id <id>]");
            return 2;
        }

        var oldSetup = Path.GetFullPath(p.Positional[0]);
        var newSetup = Path.GetFullPath(p.Positional[1]);
        if (VersionOf(oldSetup) is not { } oldVersion || VersionOf(newSetup) is not { } newVersion)
        {
            Console.Error.WriteLine("update-smoke: the setups must be named RafficaPdf-<x.y.z>-win-x64-setup.exe");
            return 2;
        }

        foreach (var file in new[] { oldSetup, newSetup, Path.GetFullPath(keyFile) })
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"update-smoke: file missing: {file}");
                return 1;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("update-smoke: runs only on Windows");
            return 1;
        }

        using var key = ReadKey(File.ReadAllText(keyFile));
        if (key is null)
        {
            Console.Error.WriteLine($"update-smoke: not a private P-256 key (Base64 as written by update-keygen, or PEM): {keyFile}");
            return 1;
        }

        var failures = RunOnWindows(oldSetup, oldVersion, newSetup, newVersion, key, p.Named.GetValueOrDefault("--key-id", TestKeyId));
        if (failures.Count == 0)
        {
            Console.WriteLine($"update-smoke: {oldVersion} -> {newVersion} through the helper mode passed");
            return 0;
        }

        failures.ForEach(f => Console.Error.WriteLine($"update-smoke: {f}"));
        return 1;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<string> RunOnWindows(string oldSetup, string oldVersion, string newSetup, string newVersion, ECDsa key, string keyId)
    {
        var failures = new List<string>();
        var hkcu = Microsoft.Win32.Registry.CurrentUser;
        var hklm = Microsoft.Win32.Registry.LocalMachine;

        // (0) Never over a real installation, as installer-smoke: the test uninstalls at the end.
        foreach (var (root, path) in new[] { (hkcu, @"Software\Classes\Raffica.PDF"), (hkcu, Installer.UninstallKey), (hklm, Installer.UninstallKey) })
        {
            using var existing = root.OpenSubKey(path);
            if (existing is not null)
            {
                failures.Add($@"Raffica PDF is already installed or registered ({root.Name}\{path}); uninstall it first, the test would remove it");
            }
        }

        if (failures.Count > 0)
        {
            return failures;
        }

        var temp = Path.GetTempPath();
        var dir = TestDir(temp);
        var work = Path.Combine(temp, "raffica-update-smoke");
        foreach (var leftover in new[] { dir, work })
        {
            if (Directory.Exists(leftover))
            {
                Console.WriteLine($"update-smoke: removing leftovers of an earlier run: {leftover}");
                Directory.Delete(leftover, true);
            }
        }

        // A profile of its own: the helper and the restarted version take %APPDATA% and %LOCALAPPDATA% from the environment.
        var roaming = Path.Combine(work, "Roaming");
        var local = Path.Combine(work, "Local");
        var config = Path.Combine(roaming, "Raffica");
        var logs = Path.Combine(work, "logs");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(config, "settings.json"), "{\n  \"updateCheck\": false\n}\n");
        var settingsBefore = SettingsLine(config);

        var realAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Raffica");
        var realBefore = InstallerSmoke.Snapshot(realAppData);
        var uninstaller = Path.Combine(dir, "unins000.exe");
        var exe = Path.Combine(dir, "RafficaPdf.exe");
        try
        {
            // (1) The old version, silently, exactly the documented command line.
            var code = ProcessRunner.RunInno(oldSetup, InstallerSmoke.SilentInstallArguments(dir, Path.Combine(logs, "install-old.log")), TimeSpan.FromMinutes(10));
            if (code != 0 || !File.Exists(exe))
            {
                failures.Add($"Installation of the old version: exit {code}, {exe} {(File.Exists(exe) ? "present" : "missing")}");
                return failures;
            }

            if (ProductVersion(exe) is not { } before || !before.StartsWith(oldVersion, StringComparison.Ordinal))
            {
                failures.Add($"{exe} reports {ProductVersion(exe) ?? "no version"} instead of {oldVersion}");
                return failures;
            }

            // (2) The staging folder as the app leaves it: setup program, signed manifest, signature, request.
            var staging = Path.Combine(config, "updates", newVersion);
            Directory.CreateDirectory(staging);
            var name = Path.GetFileName(newSetup);
            File.Copy(newSetup, Path.Combine(staging, name));
            var manifest = Manifest(newVersion, name, new FileInfo(newSetup).Length, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(newSetup))));
            File.WriteAllBytes(Path.Combine(staging, "update-manifest.json"), manifest);
            File.WriteAllText(Path.Combine(staging, "update-manifest.json.sig"), Signature(manifest, key, keyId), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "request.json"), Request(newVersion, name, exe, dir), new UTF8Encoding(false));

            // (3) The helper runs from a copy of the program folder, never from the folder the setup program replaces.
            var copy = Path.Combine(local, "Raffica", "updater", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(copy);
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
            }

            var psi = new ProcessStartInfo(Path.Combine(copy, "RafficaPdf.exe")) { UseShellExecute = false, WorkingDirectory = copy };
            HelperArguments(staging).ForEach(psi.ArgumentList.Add);
            psi.Environment["APPDATA"] = roaming;
            psi.Environment["LOCALAPPDATA"] = local;
            Console.WriteLine($"update-smoke: starting the helper {psi.FileName} --finish-update {staging}");
            using (var helper = Process.Start(psi)!)
            {
                if (!helper.WaitForExit(TimeSpan.FromMinutes(10)))
                {
                    helper.Kill(entireProcessTree: true);
                    failures.Add("the helper did not end within 10 minutes (signature refused? Both setups must come from the test build with the key of --key)");
                }
                else if (helper.ExitCode != 0)
                {
                    failures.Add($"the helper ended with exit {helper.ExitCode}");
                }
            }

            // (4) The new version is installed, the update is gone, the settings are as they were.
            var after = ProductVersion(exe);
            if (after is null || !after.StartsWith(newVersion, StringComparison.Ordinal))
            {
                // The helper starts Inno Setup with "/LOG" without a name (never a path in the user-writable staging
                // folder): the log is "Setup Log <date> #<n>.txt" in the %TEMP% of the account that ran the setup program.
                failures.Add($"{exe} reports {after ?? "no version"} after the update instead of {newVersion} (log: \"Setup Log *.txt\" in {temp})");
            }

            if (Directory.Exists(staging))
            {
                failures.Add($"the staging folder is still there after the update: {staging}");
            }

            failures.AddRange(InstallerSmoke.Differences(settingsBefore, SettingsLine(config)).Select(d => $"settings.json changed: {d}"));
            return failures;
        }
        finally
        {
            // The helper starts the new version: end it (only the one from the test folder) before uninstalling.
            foreach (var process in Process.GetProcessesByName("RafficaPdf"))
            {
                using (process)
                {
                    try
                    {
                        if (string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase))
                        {
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(TimeSpan.FromSeconds(30));
                        }
                    }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        // Not ours, or gone already.
                    }
                }
            }

            if (File.Exists(uninstaller))
            {
                var code = ProcessRunner.RunInno(uninstaller, InstallerSmoke.SilentUninstallArguments(Path.Combine(logs, "uninstall.log")), TimeSpan.FromMinutes(5));
                if (code != 0)
                {
                    failures.Add($"Uninstallation: exit {code} (log: {Path.Combine(logs, "uninstall.log")})");
                }
            }

            failures.AddRange(InstallerSmoke.Differences(realBefore, InstallerSmoke.Snapshot(realAppData)).Select(d => $@"%APPDATA%\Raffica changed: {d}"));
            if (failures.Count == 0)
            {
                try
                {
                    Directory.Delete(work, true);
                }
                catch (IOException)
                {
                    Console.WriteLine($"update-smoke: {work} is removed on the next run");
                }
            }
            else
            {
                Console.Error.WriteLine($"update-smoke: logs and test profile are kept for troubleshooting: {work}");
            }
        }
    }

    // The line of settings.json in a snapshot of the folder (size and time of the last write).
    private static List<string> SettingsLine(string config) =>
        InstallerSmoke.Snapshot(config).Where(l => l.StartsWith("settings.json ", StringComparison.Ordinal)).ToList();

    private static string? ProductVersion(string exe) =>
        File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion : null;
}

// The update manifest of a release (Milestone 8.1) as Raffica.Core.Updates reads it: UpdateManifest/UpdateFile
// (camelCase JSON), ManifestVerifier (checks). The tool is a script and cannot reference the app; the rules below are
// a copy, and testdata/updates plus ReleaseToolTests (Core) prove that the app accepts what the tool writes.
static partial class UpdateManifestRules
{
    public const int Format = 1;

    // ManifestVerifier.MaxFileSize (1 GiB) and MaxFiles.
    public const long MaxFileSize = 1L << 30;
    private const int MaxFiles = 64;

    // UpdateFileKinds.All in their order, with the release file name of a version. The four Windows and Linux kinds are
    // required; the two macOS disk images are optional as a pair (both or neither: a release is never half a Mac).
    public static readonly (string Kind, Func<string, string> Name, bool Required)[] Kinds =
    [
        ("win-x64-setup", v => $"RafficaPdf-{v}-win-x64-setup.exe", true),
        ("win-x64-portable", v => $"RafficaPdf-{v}-win-x64-portable.zip", true),
        ("linux-appimage", v => $"RafficaPdf-{v}-x86_64.AppImage", true),
        ("linux-deb", v => $"raffica-pdf_{v}_amd64.deb", true),
        ("macos-arm64-dmg", v => $"RafficaPdf-{v}-macos-arm64.dmg", false),
        ("macos-x64-dmg", v => $"RafficaPdf-{v}-macos-x64.dmg", false),
    ];

    // The endings that make a release file one of the known kinds; a file with such an ending but another version is an
    // error, every other file (source archives, checksums) is not part of the manifest.
    public static readonly string[] KnownEndings = ["-win-x64-setup.exe", "-win-x64-portable.zip", "-x86_64.AppImage", "_amd64.deb", "-macos-arm64.dmg", "-macos-x64.dmg"];

    // UpdateVersion.TryParse without the "v": the app reads only stable X.Y.Z versions.
    [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\z")]
    public static partial Regex StableVersion();

    // ManifestVerifier.SafeFileName and Sha256Hex.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+~-]{0,127}\z")]
    public static partial Regex SafeFileName();

    [GeneratedRegex(@"^[0-9A-Fa-f]{64}\z")]
    private static partial Regex Sha256Hex();

    // Compact JSON, no line breaks (nothing for git or an editor to convert; the signature is over the raw bytes).
    public static byte[] Write(string version, IEnumerable<(string Kind, string Name, long Size, string Sha256)> files)
    {
        using var buffer = new MemoryStream();
        using (var json = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("format", Format);
            json.WriteString("version", version);
            json.WriteStartArray("files");
            foreach (var (kind, name, size, sha256) in files)
            {
                json.WriteStartObject();
                json.WriteString("kind", kind);
                json.WriteString("name", name);
                json.WriteNumber("size", size);
                json.WriteString("sha256", sha256);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    // null when the bytes are a manifest the app reads (apart from "newer than the running version"); else why not.
    public static string? Problem(byte[] manifest)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(manifest, new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !root.TryGetProperty("format", out var format) || !format.TryGetInt32(out var f) || f != Format)
            {
                return $"format is not {Format}";
            }

            var version = root.TryGetProperty("version", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString()! : string.Empty;
            if (!StableVersion().IsMatch(version.StartsWith('v') ? version[1..] : version))
            {
                return $"version is not a version X.Y.Z: '{version}'";
            }

            if (!root.TryGetProperty("files", out var files) || files.ValueKind != System.Text.Json.JsonValueKind.Array ||
                files.GetArrayLength() is 0 or > MaxFiles)
            {
                return $"files is missing, empty or has more than {MaxFiles} entries";
            }

            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files.EnumerateArray())
            {
                var kind = file.ValueKind == System.Text.Json.JsonValueKind.Object && file.TryGetProperty("kind", out var k) &&
                    k.ValueKind == System.Text.Json.JsonValueKind.String ? k.GetString()! : null;
                if (kind is null)
                {
                    return "an entry in files has no kind";
                }

                if (!Kinds.Any(x => x.Kind == kind))
                {
                    continue; // a later kind: the app skips it
                }

                var name = file.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString()! : string.Empty;
                var sha = file.TryGetProperty("sha256", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String ? s.GetString()! : string.Empty;
                if (!kinds.Add(kind) || !Sha256Hex().IsMatch(sha))
                {
                    return $"{kind}: duplicate or without a valid sha256";
                }

                if (!SafeFileName().IsMatch(name))
                {
                    return $"{kind}: not a plain file name: '{name}'";
                }

                if (!file.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytes) || bytes is <= 0 or > MaxFileSize)
                {
                    return $"{kind}: size is missing, 0 or larger than 1 GiB";
                }
            }

            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return "not valid JSON";
        }
    }
}

// The public keys of a C# file such as src/Raffica.Core/Updates/UpdateTrust.cs: every "new UpdateKey(<id>,
// Convert.FromBase64String("<SPKI>"))", the id as a string or as a "const string" of the same file. The placeholder of
// UpdateTrust.cs is never a usable key: its private half was discarded, and a release must not be signed for it.
static partial class TrustSource
{
    public const string DefaultPath = "src/Raffica.Core/Updates/UpdateTrust.cs";
    public const string PlaceholderId = "placeholder";
    public const string PlaceholderKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEfEyCOMU4IJ14osOpBY3eFP4/fxK726BrpSdQoUOGeMJ+2DsQaDm4maWvFaZ22iI1oti6dIVOk7thJ8J5Lkp59Q==";

    [GeneratedRegex(@"new\s+UpdateKey\(\s*(?:""(?<id>[^""\\]*)""|(?<const>[A-Za-z_][A-Za-z0-9_]*))\s*,\s*Convert\.FromBase64String\(\s*""(?<key>[A-Za-z0-9+/=]+)""\s*\)\s*\)")]
    private static partial Regex KeyEntry();

    [GeneratedRegex(@"\bnew\s+UpdateKey\(")]
    private static partial Regex AnyKeyEntry();

    // The usable keys (id -> SPKI), how many placeholder entries were skipped, or why the file cannot be read.
    public static (Dictionary<string, byte[]> Keys, int Placeholders, string? Problem) Read(string path)
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return (keys, 0, $"File missing: {path}");
        }

        var text = File.ReadAllText(path);

        // Conditional compilation (a test build with other keys) would make the text say something else than the
        // release build; refuse it instead of guessing which branch counts.
        if (Regex.IsMatch(text, @"(?m)^\s*#\s*if\b"))
        {
            return (keys, 0, $"{path}: conditional compilation (#if) is not supported, the keys would not be unambiguous");
        }

        var entries = KeyEntry().Matches(text);
        if (entries.Count == 0 || entries.Count != AnyKeyEntry().Count(text))
        {
            return (keys, 0, $"{path}: no entry, or an unreadable one, of the form new UpdateKey(\"<id>\", Convert.FromBase64String(\"<key>\"))");
        }

        var placeholders = 0;
        foreach (Match entry in entries)
        {
            var id = entry.Groups["id"].Success ? entry.Groups["id"].Value : Constant(text, entry.Groups["const"].Value);
            if (string.IsNullOrEmpty(id) || id.Contains(':'))
            {
                return (keys, 0, $"{path}: id is missing, empty, contains ':' or is not a constant of the file: {entry.Groups["const"].Value}");
            }

            var base64 = entry.Groups["key"].Value;
            if (id == PlaceholderId || base64 == PlaceholderKey)
            {
                placeholders++;
                continue;
            }

            byte[] spki;
            try
            {
                spki = Convert.FromBase64String(base64);
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(spki, out var read);
                if (read != spki.Length || key.KeySize != 256)
                {
                    return (keys, 0, $"{path}: key {id} is not a public P-256 key");
                }
            }
            catch (Exception e) when (e is FormatException or CryptographicException)
            {
                return (keys, 0, $"{path}: key {id} is not a public P-256 key");
            }

            if (!keys.TryAdd(id, spki))
            {
                return (keys, 0, $"{path}: id {id} appears twice");
            }
        }

        return (keys, placeholders, null);
    }

    private static string? Constant(string text, string name)
    {
        var m = Regex.Match(text, @"\bconst\s+string\s+" + Regex.Escape(name) + @"\s*=\s*""(?<v>[^""\\]*)""\s*;");
        return m.Success ? m.Groups["v"].Value : null;
    }

    // The keys for signing or verifying; null (message written) when there is none to trust.
    public static Dictionary<string, byte[]>? Usable(string path, string command)
    {
        var (keys, placeholders, problem) = Read(path);
        if (problem is not null)
        {
            Console.Error.WriteLine($"{command}: {problem}");
            return null;
        }

        if (keys.Count == 0)
        {
            Console.Error.WriteLine(placeholders > 0
                ? $"{command}: {path} contains only the placeholder; enter the real public key first (packaging/README.md, section \"Signing key for updates\")"
                : $"{command}: {path} contains no key");
            return null;
        }

        return keys;
    }
}

static class UpdateSignature
{
    // "<keyId>:<base64 of the 64-byte IEEE P1363 ECDSA P-256 signature over the raw bytes, SHA-256>" (ManifestVerifier).
    public static string Sign(byte[] manifest, ECDsa key, string keyId) =>
        keyId + ":" + Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

    // null when the signature text verifies with one of the keys (by its id); else why not.
    public static string? Problem(byte[] manifest, string signatureText, IReadOnlyDictionary<string, byte[]> keys)
    {
        var text = signatureText.Trim();
        var colon = text.IndexOf(':');
        if (colon <= 0)
        {
            return "Signature does not have the form <id>:<Base64>";
        }

        var id = text[..colon];
        if (!keys.TryGetValue(id, out var spki))
        {
            return $"Key id {id} is not trusted";
        }

        var signature = new byte[67];
        if (!Convert.TryFromBase64String(text[(colon + 1)..], signature, out var written) || written != 64)
        {
            return "Signature is not Base64 of 64 bytes";
        }

        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(spki, out _);
        return key.VerifyData(manifest, signature.AsSpan(0, 64), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            ? null
            : $"Signature does not match the manifest (key {id})";
    }

    // The key id rule of UpdateKey: not empty, no colon; kept to a plain token so it fits release.yml and file names.
    public static bool IsValidId(string id) => Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z");
}

// update-keygen --id <id> --private-out <file>: the private key (PKCS#8, Base64) only into the file, readable by the
// user alone; the public key as an entry for UpdateTrust.Keys on stdout. The private key is never printed.
static class UpdateKeygen
{
    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--id", "--private-out");
        if (parsed is not { } p || p.Positional.Count != 0 || !p.Named.TryGetValue("--id", out var id) ||
            !p.Named.TryGetValue("--private-out", out var output) || !UpdateSignature.IsValidId(id))
        {
            Console.Error.WriteLine("Usage: update-keygen --id <id of A-Z a-z 0-9 . _ -> --private-out <file outside every Git working tree>");
            return 2;
        }

        var full = Path.GetFullPath(output);
        if (File.Exists(full) || Directory.Exists(full))
        {
            Console.Error.WriteLine($"update-keygen: {full} already exists; a key is never overwritten");
            return 1;
        }

        if (GitWorkTree(full) is { } tree)
        {
            Console.Error.WriteLine($"update-keygen: {full} is inside the Git working tree {tree}; the private key must never go into a repository");
            return 1;
        }

        if (!Directory.Exists(Path.GetDirectoryName(full)))
        {
            Console.Error.WriteLine($"update-keygen: folder missing: {Path.GetDirectoryName(full)}");
            return 1;
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pkcs8 = key.ExportPkcs8PrivateKey();
        var chars = new char[(pkcs8.Length + 2) / 3 * 4];
        try
        {
            Convert.TryToBase64Chars(pkcs8, chars, out var length);
            using (var stream = CreateUserOnly(full))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(chars, 0, length);
                writer.Write('\n');
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
            Array.Clear(chars);
        }

        Console.WriteLine("        new UpdateKey(");
        Console.WriteLine($"            \"{id}\",");
        Console.WriteLine($"            Convert.FromBase64String(\"{Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())}\")),");
        Console.Error.WriteLine($"update-keygen: private key in {full} (readable only by this user). Next: packaging/README.md, section \"Signing key for updates\".");
        return 0;
    }

    // The folder (or an ancestor) that holds .git, or null.
    private static string? GitWorkTree(string file)
    {
        for (var dir = Path.GetDirectoryName(file); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }
        }

        return null;
    }

    // A new file (never an existing one) that only the current user can read: 0600 on Unix, on Windows an ACL with
    // inheritance cut and a single rule for the user.
    private static FileStream CreateUserOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
            var security = new System.Security.AccessControl.FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                user, System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            return new FileInfo(path).Create(FileMode.CreateNew, System.Security.AccessControl.FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
        }

        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
    }
}

// update-manifest --version <x.y.z> --sums <SHA256SUMS-windows.txt> <SHA256SUMS-linux.txt> [<SHA256SUMS-macos.txt>] --sizes <gh release view --json assets> --out <file>
static partial class UpdateManifestCommand
{
    // sums: "<sha256>  <file name>" (Sums.Line)
    [GeneratedRegex(@"^(?<h>[0-9a-f]{64})  (?<n>[^\s/\\]+)$")]
    private static partial Regex SumsLine();

    private const string Usage = "Usage: update-manifest --version <x.y.z> --sums <SHA256SUMS-windows.txt> <SHA256SUMS-linux.txt> [<SHA256SUMS-macos.txt>] --sizes <assets.json> --out <update-manifest.json>";

    public static int Run(string[] args)
    {
        // --sums takes several files: everything up to the next option.
        var sums = new List<string>();
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--sums")
            {
                while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    sums.Add(args[++i]);
                }
            }
            else
            {
                rest.Add(args[i]);
            }
        }

        var parsed = Options.Parse([.. rest], "--version", "--sizes", "--out");
        if (parsed is not { } p || p.Positional.Count != 0 || sums.Count == 0 || !p.Named.TryGetValue("--version", out var version) ||
            !p.Named.TryGetValue("--sizes", out var sizesFile) || !p.Named.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (!UpdateManifestRules.StableVersion().IsMatch(version))
        {
            Console.Error.WriteLine($"update-manifest: '{version}' is not a version X.Y.Z; the app reads only stable versions (prereleases get no manifest)");
            return 2;
        }

        var problems = new List<string>();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in sums)
        {
            if (!File.Exists(file))
            {
                problems.Add($"File missing: {file}");
                continue;
            }

            foreach (var raw in File.ReadAllText(file).Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0)
                {
                    continue;
                }

                var m = SumsLine().Match(line);
                if (!m.Success)
                {
                    problems.Add($"{file}: line is not '<sha256>  <file name>': {line}");
                }
                else if (!hashes.TryAdd(m.Groups["n"].Value, m.Groups["h"].Value))
                {
                    problems.Add($"{m.Groups["n"].Value} appears several times in the checksums");
                }
            }
        }

        var sizes = ReadSizes(sizesFile, problems);

        // Every file with a known ending must be exactly the name of this version; every known kind must be there.
        foreach (var name in hashes.Keys.Where(n => UpdateManifestRules.KnownEndings.Any(e => n.EndsWith(e, StringComparison.Ordinal))))
        {
            if (!UpdateManifestRules.Kinds.Any(k => k.Name(version) == name))
            {
                problems.Add($"{name} does not belong to version {version}");
            }
        }

        // The optional kinds come as a pair: when one is in the checksums, the other must be too.
        var optional = UpdateManifestRules.Kinds.Where(k => !k.Required).ToList();
        var optionalPresent = optional.Where(k => hashes.ContainsKey(k.Name(version))).ToList();
        var skipOptional = optionalPresent.Count == 0;
        if (!skipOptional)
        {
            foreach (var (kind, nameOf, _) in optional.Where(k => !optionalPresent.Contains(k)))
            {
                problems.Add($"{kind}: {nameOf(version)} is missing in the checksums (the macOS images come as a pair: arm64 and x64, or neither)");
            }
        }

        var files = new List<(string Kind, string Name, long Size, string Sha256)>();
        foreach (var (kind, nameOf, required) in UpdateManifestRules.Kinds)
        {
            var name = nameOf(version);
            if (!required)
            {
                if (skipOptional || !optionalPresent.Any(k => k.Kind == kind))
                {
                    continue; // absent (the pair problem is already listed) or not part of this release
                }
            }

            if (!hashes.TryGetValue(name, out var hash))
            {
                problems.Add($"{kind}: {name} is missing in the checksums");
            }
            else if (sizes is not null && !sizes.TryGetValue(name, out var size))
            {
                problems.Add($"{kind}: {name} is missing in the files of the release (--sizes)");
            }
            else if (sizes is not null)
            {
                files.Add((kind, name, sizes[name], hash));
            }
        }

        foreach (var (kind, name, size, _) in files)
        {
            if (size is <= 0 or > UpdateManifestRules.MaxFileSize)
            {
                problems.Add($"{kind}: {name} has the size {size} (allowed: 1 byte to 1 GiB)");
            }

            if (!UpdateManifestRules.SafeFileName().IsMatch(name))
            {
                problems.Add($"{kind}: {name} is not a plain file name");
            }
        }

        if (problems.Count > 0)
        {
            problems.ForEach(x => Console.Error.WriteLine($"update-manifest: {x}"));
            return 1;
        }

        var bytes = UpdateManifestRules.Write(version, files);
        if (UpdateManifestRules.Problem(bytes) is { } problem)
        {
            Console.Error.WriteLine($"update-manifest: the written manifest would be invalid: {problem}");
            return 1;
        }

        File.WriteAllBytes(output, bytes);
        Console.WriteLine($"update-manifest: {version}, {files.Count} files in {output}");
        return 0;
    }

    // name -> size from "gh release view <tag> --json assets"; only assets that are completely uploaded count.
    private static Dictionary<string, long>? ReadSizes(string file, List<string> problems)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                var uploaded = !asset.TryGetProperty("state", out var state) || state.GetString() == "uploaded";
                if (uploaded && !result.TryAdd(name, asset.GetProperty("size").GetInt64()))
                {
                    problems.Add($"{file}: {name} appears several times");
                }
            }

            return result;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            problems.Add($"{file}: no output of 'gh release view <tag> --json assets' ({e.GetType().Name})");
            return null;
        }
    }
}

// update-sign --manifest <file> --key-env <VARIABLE> --id <id> --out <file.sig> [--source <UpdateTrust.cs>]
// The private key comes only from the environment variable (never an argument, never printed, never written to disk).
// The id must be a key of UpdateTrust.cs (never the placeholder), the private key must belong to it, and the signature
// is verified with that public key before it is written.
static class UpdateSign
{
    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--manifest", "--key-env", "--id", "--out", "--source");
        if (parsed is not { } p || p.Positional.Count != 0 || !p.Named.TryGetValue("--manifest", out var manifestFile) ||
            !p.Named.TryGetValue("--key-env", out var keyEnv) || !p.Named.TryGetValue("--id", out var id) ||
            !p.Named.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine("Usage: update-sign --manifest <file> --key-env <environment variable> --id <id> --out <file.sig> [--source <UpdateTrust.cs>]   (the key comes only from the environment variable)");
            return 2;
        }

        var source = p.Named.GetValueOrDefault("--source", TrustSource.DefaultPath);
        if (TrustSource.Usable(source, "update-sign") is not { } keys)
        {
            return 1;
        }

        if (!keys.TryGetValue(id, out var trusted))
        {
            Console.Error.WriteLine($"update-sign: id {id} is not in {source} (present: {string.Join(", ", keys.Keys)})");
            return 1;
        }

        if (!File.Exists(manifestFile))
        {
            Console.Error.WriteLine($"update-sign: file missing: {manifestFile}");
            return 1;
        }

        var manifest = File.ReadAllBytes(manifestFile);
        if (UpdateManifestRules.Problem(manifest) is { } problem)
        {
            Console.Error.WriteLine($"update-sign: {manifestFile} is not a valid manifest: {problem}");
            return 1;
        }

        using var key = ReadKey(keyEnv);
        if (key is null)
        {
            return 1;
        }

        if (!key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(trusted))
        {
            Console.Error.WriteLine($"update-sign: the key in {keyEnv} does not belong to the id {id} in {source}");
            return 1;
        }

        var signature = UpdateSignature.Sign(manifest, key, id);
        if (UpdateSignature.Problem(manifest, signature, new Dictionary<string, byte[]> { [id] = trusted }) is { } check)
        {
            Console.Error.WriteLine($"update-sign: the new signature does not verify: {check}");
            return 1;
        }

        File.WriteAllText(output, signature, new UTF8Encoding(false));
        Console.WriteLine($"update-sign: {manifestFile} signed with {id} and verified: {output}");
        return 0;
    }

    // The P-256 private key from the environment (PKCS#8 as Base64, as update-keygen writes it); null with a message
    // that never contains the value.
    private static ECDsa? ReadKey(string variable)
    {
        var text = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(text))
        {
            Console.Error.WriteLine($"update-sign: environment variable {variable} is missing or empty");
            return null;
        }

        var compact = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        var bytes = new byte[compact.Length];
        var key = ECDsa.Create();
        try
        {
            if (!Convert.TryFromBase64String(compact, bytes, out var length))
            {
                throw new FormatException();
            }

            // The curve is checked by the caller: the public key must equal the trusted P-256 key (the SPKI names the curve).
            key.ImportPkcs8PrivateKey(bytes.AsSpan(0, length), out var read);
            if (read != length || key.KeySize != 256)
            {
                throw new FormatException();
            }

            return key;
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            key.Dispose();
            Console.Error.WriteLine($"update-sign: {variable} contains no private P-256 key (PKCS#8, Base64, as update-keygen writes it)");
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

// update-verify [--manifest <file> --sig <file>] (--keys-from-source | --source <file>): signature and content as the
// app checks them; --keys-from-source takes the keys of src/Raffica.Core/Updates/UpdateTrust.cs and fails while it
// holds only the placeholder. Without manifest and signature it checks only that (preflight job of release.yml).
static class UpdateVerify
{
    public static int Run(string[] args)
    {
        var fromSource = args.Contains("--keys-from-source", StringComparer.Ordinal);
        var parsed = Options.Parse([.. args.Where(a => a != "--keys-from-source")], "--manifest", "--sig", "--source");
        if (parsed is not { } p || p.Positional.Count != 0 || fromSource == p.Named.ContainsKey("--source") ||
            p.Named.ContainsKey("--manifest") != p.Named.ContainsKey("--sig"))
        {
            Console.Error.WriteLine("Usage: update-verify [--manifest <file> --sig <file>] (--keys-from-source | --source <C# file with new UpdateKey(...)>)   (without a manifest: only checks that a real key is trusted)");
            return 2;
        }

        var source = p.Named.GetValueOrDefault("--source", TrustSource.DefaultPath);
        if (TrustSource.Usable(source, "update-verify") is not { } keys)
        {
            return 1;
        }

        // Without a manifest: only the keys (the preflight job of release.yml, before anything is uploaded).
        if (!p.Named.TryGetValue("--manifest", out var manifestFile) || !p.Named.TryGetValue("--sig", out var sigFile))
        {
            Console.WriteLine($"update-verify: {source} trusts {string.Join(", ", keys.Keys)} (not the placeholder alone)");
            return 0;
        }

        if (!File.Exists(manifestFile) || !File.Exists(sigFile))
        {
            Console.Error.WriteLine($"update-verify: file missing: {(File.Exists(manifestFile) ? sigFile : manifestFile)}");
            return 1;
        }

        var manifest = File.ReadAllBytes(manifestFile);
        var problem = UpdateSignature.Problem(manifest, File.ReadAllText(sigFile), keys) ?? UpdateManifestRules.Problem(manifest);
        if (problem is not null)
        {
            Console.Error.WriteLine($"update-verify: {manifestFile} rejected: {problem}");
            return 1;
        }

        Console.WriteLine($"update-verify: {manifestFile} is validly signed ({source})");
        return 0;
    }
}

enum DebEntryKind
{
    Directory,
    File,
    Symlink,
}

// One entry of a package tree (.deb, AppImage); Path is relative to the package root ("usr/bin/raffica"), Mode the Unix mode
// (0755 = 0x1ED, 0644 = 0x1A4). A file comes from Source or, when generated, from Content (UTF-8 without BOM).
sealed record DebEntry(string Path, DebEntryKind Kind, int Mode, string? Source = null, string? LinkTarget = null, string? Content = null);

static class Tree
{
    // Writes the entries below root with their modes and the fixed time (directories last, children first: they change the
    // parent's time). Symlinks keep the time of their creation; tools that need a fixed one clamp it (SOURCE_DATE_EPOCH).
    public static void Write(IReadOnlyList<DebEntry> entries, string root, DateTimeOffset time, IEnumerable<string>? extraDirectories = null)
    {
        Directory.CreateDirectory(root);
        var directories = new List<string> { root };
        foreach (var entry in entries)
        {
            var target = Path.Combine(root, entry.Path);
            switch (entry.Kind)
            {
                case DebEntryKind.Directory:
                    Directory.CreateDirectory(target);
                    File.SetUnixFileMode(target, (UnixFileMode)entry.Mode);
                    directories.Add(target);
                    break;
                case DebEntryKind.File:
                    if (entry.Content is { } content)
                    {
                        File.WriteAllText(target, content, new UTF8Encoding(false));
                    }
                    else
                    {
                        File.Copy(entry.Source!, target);
                    }

                    File.SetUnixFileMode(target, (UnixFileMode)entry.Mode);
                    File.SetLastWriteTimeUtc(target, time.UtcDateTime);
                    break;
                default:
                    File.CreateSymbolicLink(target, entry.LinkTarget!);
                    break;
            }
        }

        File.SetUnixFileMode(root, (UnixFileMode)0x1ED);
        foreach (var directory in directories.Concat(extraDirectories ?? []).OrderByDescending(d => d.Length))
        {
            Directory.SetLastWriteTimeUtc(directory, time.UtcDateTime);
        }
    }

    public static IEnumerable<string> Parents(string path)
    {
        for (var i = path.IndexOf('/', StringComparison.Ordinal); i > 0; i = path.IndexOf('/', i + 1))
        {
            yield return path[..i];
        }
    }

    // Adds a 0755 directory entry for every parent of every entry and sorts by path (ordinal).
    public static List<DebEntry> WithParents(IEnumerable<DebEntry> entries)
    {
        var all = entries.ToList();
        foreach (var directory in all.SelectMany(e => Parents(e.Path)).Distinct(StringComparer.Ordinal).ToList())
        {
            all.Add(new DebEntry(directory, DebEntryKind.Directory, 0x1ED));
        }

        return all.OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
    }
}

static partial class Deb
{
    // PLACEHOLDER until the owner decides (open question of Milestone 8, task 9). A noreply address is
    // used on purpose; replace it here, nowhere else (control.template takes it from this constant).
    public const string Maintainer = "Raffica contributors <noreply@users.noreply.github.com>";

    public static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const int Dir = 0x1ED;   // 0755
    private const int Exec = 0x1ED;  // 0755
    private const int Plain = 0x1A4; // 0644
    private const string LibDir = "usr/lib/raffica-pdf";

    [GeneratedRegex(@"^(?<core>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))(?:-(?<pre>[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*))?$")]
    private static partial Regex VersionPattern();

    // x.y.z stays, x.y.z-rc1 becomes x.y.z~rc1 (a tilde sorts before the release); anything else is null.
    public static string? DebVersion(string version)
    {
        var m = VersionPattern().Match(version);
        if (!m.Success)
        {
            return null;
        }

        return m.Groups["pre"].Success ? $"{m.Groups["core"].Value}~{m.Groups["pre"].Value}" : m.Groups["core"].Value;
    }

    // Reads one control field with dpkg-deb -f (selftest helper).
    public static string Field(string deb, string name) => Tool.Run("dpkg-deb", ["-f", deb, name], out var text) == 0 ? text.Trim() : "?";

    public static string FileName(string debVersion) => $"raffica-pdf_{debVersion}_amd64.deb";

    // Installed-Size is in KiB; every file counts rounded up, like a block-based size would.
    public static long InstalledSizeKiB(IEnumerable<long> fileSizes) => Math.Max(1, fileSizes.Sum(s => (s + 1023) / 1024));

    public static string Control(string template, string debVersion, long installedSizeKiB, string maintainer) =>
        template.Replace("@VERSION@", debVersion, StringComparison.Ordinal)
            .Replace("@INSTALLED_SIZE@", installedSizeKiB.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("@MAINTAINER@", maintainer, StringComparison.Ordinal);

    // The package tree: layout under /usr/lib/raffica-pdf, launcher link, .desktop, icons, copyright. Throws IOException when an input is missing.
    public static List<DebEntry> Plan(string layout, string repo)
    {
        var files = new List<DebEntry>();
        foreach (var required in new[] { "RafficaPdf", "libpdfium.so", "THIRD-PARTY-NOTICES.txt", "cli/raffica", "cli/libpdfium.so" })
        {
            if (!File.Exists(Path.Combine(layout, required)))
            {
                throw new IOException($"Layout does not contain {required}: {layout}");
            }
        }

        foreach (var file in Directory.EnumerateFiles(layout, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(layout, file).Replace('\\', '/');
            if (relative == "LICENSE")
            {
                continue; // the licence is in /usr/share/doc/raffica-pdf/copyright and THIRD-PARTY-NOTICES.txt
            }

            files.Add(new DebEntry($"{LibDir}/{relative}", DebEntryKind.File, relative is "RafficaPdf" or "cli/raffica" ? Exec : Plain, file));
        }

        var icons = Path.Combine(repo, "Docs", "design", "icons", "linux", "hicolor");
        if (!Directory.Exists(icons))
        {
            throw new IOException($"Icon folder missing: {icons}");
        }

        foreach (var icon in Directory.EnumerateDirectories(icons))
        {
            var png = Path.Combine(icon, "apps", "raffica-pdf.png");
            if (File.Exists(png))
            {
                files.Add(new DebEntry($"usr/share/icons/hicolor/{Path.GetFileName(icon)}/apps/raffica-pdf.png", DebEntryKind.File, Plain, png));
            }
        }

        if (files.All(f => !f.Path.StartsWith("usr/share/icons/", StringComparison.Ordinal)))
        {
            throw new IOException($"No icons in {icons}");
        }

        files.Add(Source(repo, "packaging/linux/raffica-pdf.desktop", "usr/share/applications/raffica-pdf.desktop"));
        files.Add(Source(repo, "packaging/linux/copyright.template", "usr/share/doc/raffica-pdf/copyright"));
        files.Add(new DebEntry("usr/bin/raffica", DebEntryKind.Symlink, 0x1FF, null, "../lib/raffica-pdf/cli/raffica"));
        files.Add(new DebEntry("usr/bin/raffica-pdf", DebEntryKind.Symlink, 0x1FF, null, "../lib/raffica-pdf/RafficaPdf"));
        return Tree.WithParents(files);
    }

    private static DebEntry Source(string repo, string relative, string target)
    {
        var file = Path.Combine(repo, relative);
        return File.Exists(file) ? new DebEntry(target, DebEntryKind.File, Plain, file) : throw new IOException($"File missing: {file}");
    }

    // Writes the tree with modes and the fixed time; dpkg-deb --root-owner-group makes everything root:root.
    public static void Stage(IReadOnlyList<DebEntry> entries, string stage, string control)
    {
        var debian = Path.Combine(stage, "DEBIAN");
        Directory.CreateDirectory(debian);
        File.SetUnixFileMode(debian, (UnixFileMode)Dir);
        var controlFile = Path.Combine(debian, "control");
        File.WriteAllText(controlFile, control, new UTF8Encoding(false));
        File.SetUnixFileMode(controlFile, (UnixFileMode)Plain);
        File.SetLastWriteTimeUtc(controlFile, FixedTime.UtcDateTime);
        Tree.Write(entries, stage, FixedTime, [debian]);
    }

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--layout", "--version", "--out", "--repo");
        if (parsed is not { } p || p.Positional.Count != 0 ||
            !p.Named.TryGetValue("--layout", out var layout) || !p.Named.TryGetValue("--version", out var version) ||
            !p.Named.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine("Usage: deb --layout <dir> --version <x.y.z> --out <file>");
            return 2;
        }

        if (DebVersion(version) is not { } debVersion)
        {
            Console.Error.WriteLine($"deb: invalid version (expected x.y.z or x.y.z-prerelease): {version}");
            return 2;
        }

        if (Path.GetFileName(output) != FileName(debVersion))
        {
            Console.Error.WriteLine($"deb: the output file must be named {FileName(debVersion)}: {output}");
            return 2;
        }

        if (OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("deb: only on Linux (dpkg-deb)");
            return 1;
        }

        var repo = Path.GetFullPath(p.Named.GetValueOrDefault("--repo", "."));
        var work = Path.Combine(Path.GetTempPath(), "raffica-deb-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!Directory.Exists(layout))
            {
                throw new IOException($"Layout folder missing: {layout}");
            }

            var entries = Plan(layout, repo);
            var size = InstalledSizeKiB(entries.Where(e => e.Kind == DebEntryKind.File).Select(e => new FileInfo(e.Source!).Length));
            var template = File.ReadAllText(Path.Combine(repo, "packaging", "linux", "control.template"));
            Stage(entries, Path.Combine(work, "stage"), Control(template, debVersion, size, Maintainer));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.Delete(output);
            var env = new Dictionary<string, string> { ["SOURCE_DATE_EPOCH"] = FixedTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) };
            var code = Tool.Run("dpkg-deb", ["--root-owner-group", "-Zxz", "--build", Path.Combine(work, "stage"), output], out var text, env: env);
            Console.Write(text);
            if (code != 0)
            {
                Console.Error.WriteLine($"deb: dpkg-deb ended with code {code}");
                return 1;
            }

            Console.WriteLine($"deb: {entries.Count(e => e.Kind == DebEntryKind.File)} files in {output}");
            return 0;
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"deb: {e.Message}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
        }
    }
}

/// <summary>Resolves POSIX paths as text, independent of the host OS (Path.GetFullPath would add a drive letter on Windows).</summary>
static class PosixPath
{
    public static string Resolve(string directory, string relative)
    {
        var parts = new List<string>();
        foreach (var segment in (relative.StartsWith('/') ? relative : directory + "/" + relative).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (segment != ".")
            {
                parts.Add(segment);
            }
        }

        return "/" + string.Join('/', parts);
    }
}

static class Tool
{
    // Runs a program without a shell and returns its exit code with stdout and stderr in one text; -1 when it is not installed.
    public static int Run(string file, IEnumerable<string> arguments, out string output, string? workingDirectory = null, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        foreach (var (key, value) in env ?? new Dictionary<string, string>())
        {
            psi.Environment[key] = value;
        }

        try
        {
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
            {
                process.Kill(entireProcessTree: true);
                output = "time limit";
                return -2;
            }

            process.WaitForExit();
            output = stdout.Result + stderr.Result;
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            output = $"{file} not found";
            return -1;
        }
    }
}

static partial class DebSmoke
{
    [GeneratedRegex(@"^(?<type>[-dl])\S+\s+(?<owner>\S+)\s+\d+\s+\S+\s+\S+\s+(?<path>.+)$")]
    private static partial Regex ListingLine();

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--repo");
        if (parsed is not { } p || p.Positional.Count != 1)
        {
            Console.Error.WriteLine("Usage: deb-smoke <file.deb> [--repo <dir>]");
            return 2;
        }

        var deb = Path.GetFullPath(p.Positional[0]);
        var repo = Path.GetFullPath(p.Named.GetValueOrDefault("--repo", "."));
        if (!File.Exists(deb))
        {
            Console.Error.WriteLine($"deb-smoke: file missing: {deb}");
            return 1;
        }

        var failures = new List<string>();
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok    " : "FAILED")} {what}");
            if (!ok)
            {
                failures.Add(what);
            }
        }

        var work = Path.Combine(Path.GetTempPath(), "raffica-deb-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            Check(Tool.Run("dpkg-deb", ["-I", deb], out var info) == 0, "dpkg-deb -I reads the package");
            Console.Write(info);
            foreach (var field in new[] { "Package", "Version", "Architecture", "Maintainer", "Depends", "Installed-Size" })
            {
                Check(Tool.Run("dpkg-deb", ["-f", deb, field], out var value) == 0 && value.Trim().Length > 0, $"field {field}: {value.Trim()}");
            }

            Check(Tool.Run("dpkg-deb", ["-c", deb], out var listing) == 0, "dpkg-deb -c lists the content");
            Console.Write(listing);
            var lines = listing.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => ListingLine().Match(l)).ToList();
            Check(lines.Count > 0 && lines.All(m => m.Success && m.Groups["owner"].Value == "root/root"), "all entries belong to root:root");
            Check(lines.Any(m => m.Success && m.Groups["path"].Value.StartsWith("./usr/bin/raffica -> ", StringComparison.Ordinal)), "/usr/bin/raffica is a symlink");
            Check(lines.Any(m => m.Success && m.Groups["path"].Value.StartsWith("./usr/bin/raffica-pdf -> ", StringComparison.Ordinal)), "/usr/bin/raffica-pdf is a symlink");
            Check(lines.Any(m => m.Success && m.Groups["path"].Value == "./usr/share/doc/raffica-pdf/copyright"), "copyright present");

            var root = Path.Combine(work, "root");
            Check(Tool.Run("dpkg-deb", ["-x", deb, root], out var extract) == 0, "dpkg-deb -x unpacks");
            if (failures.Count > 0)
            {
                Console.Write(extract);
                return Finish(failures);
            }

            var desktop = Path.Combine(root, "usr", "share", "applications", "raffica-pdf.desktop");
            var exec = File.Exists(desktop) ? File.ReadLines(desktop).FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal)) : null;
            var target = exec?["Exec=".Length..].Split(' ')[0] ?? string.Empty;
            var targetFile = Path.Combine(root, target.TrimStart('/'));
            Check(target.StartsWith('/') && File.Exists(targetFile) && (File.GetUnixFileMode(targetFile) & UnixFileMode.UserExecute) != 0, $"Exec of the .desktop ({target}) is in the package and executable");
            Check(Directory.GetFiles(Path.Combine(root, "usr", "share", "icons", "hicolor"), "raffica-pdf.png", SearchOption.AllDirectories).Length > 0, "icons present");

            var link = Path.Combine(root, "usr", "bin", "raffica");
            var linkTarget = new FileInfo(link).LinkTarget;
            var resolved = linkTarget is null ? null : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link)!, linkTarget));
            Check(resolved is not null && resolved.StartsWith(root + "/", StringComparison.Ordinal) && File.Exists(resolved), $"/usr/bin/raffica resolves inside the package ({linkTarget})");

            var appLink = Path.Combine(root, "usr", "bin", "raffica-pdf");
            var appLinkTarget = new FileInfo(appLink).LinkTarget;
            var appResolved = appLinkTarget is null ? null : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(appLink)!, appLinkTarget));
            Check(
                appResolved == Path.Combine(root, "usr", "lib", "raffica-pdf", "RafficaPdf") && File.Exists(appResolved) && (File.GetUnixFileMode(appResolved) & UnixFileMode.UserExecute) != 0,
                $"/usr/bin/raffica-pdf resolves to the executable program inside /usr/lib/raffica-pdf ({appLinkTarget})");

            var pdf = Path.Combine(repo, "testdata", "pdfs", "text-basic.pdf");
            if (File.Exists(pdf))
            {
                var code = Tool.Run(link, ["info", pdf], out var cli);
                Check(code == 0 && cli.Trim().Length > 0, $"raffica info text-basic.pdf (Code {code})");
                if (code != 0)
                {
                    Console.Write(cli);
                }
            }
            else
            {
                Console.WriteLine($"Note: {pdf} is missing, CLI call skipped");
            }

            Optional("desktop-file-validate", [desktop], Check);
            RunLintian(deb, Check);
            return Finish(failures);
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
        }
    }

    // Lintian errors that follow from shipping an upstream-built .deb with prebuilt native libraries (SkiaSharp, HarfBuzzSharp, PDFium
    // with its bundled FreeType, lcms2, libjpeg, libpng, OpenJPEG) and no Debian changelog. Reason: Docs/DECISIONS.md, entry "Raffica PDF: packaging and release (M2.6)".
    // Every other error, and every error without a tag we know, fails the smoke test.
    public static readonly string[] AcceptedLintianErrors = ["embedded-library", "unstripped-binary-or-object", "no-changelog"];

    // The error lines ("E: raffica-pdf: <tag> ...") of a lintian report whose tag is not accepted.
    public static List<string> RejectedLintianErrors(string report) =>
        report.Split('\n')
            .Where(l => l.StartsWith("E: ", StringComparison.Ordinal))
            .Where(l => !AcceptedLintianErrors.Any(t => l.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries) is [_, _, var rest] && (rest == t || rest.StartsWith(t + " ", StringComparison.Ordinal))))
            .ToList();

    private static void RunLintian(string deb, Action<bool, string> check)
    {
        var code = Tool.Run("lintian", ["--tag-display-limit", "0", deb], out var text);
        if (code == -1)
        {
            Console.WriteLine("Note: lintian is not installed, check skipped");
            return;
        }

        Console.Write(text);
        var rejected = RejectedLintianErrors(text);
        foreach (var line in rejected)
        {
            Console.Error.WriteLine($"lintian: error not allowed: {line}");
        }

        // Exit code 2 is "errors found"; with only accepted errors that is fine, any other code is not.
        check(rejected.Count == 0 && code is 0 or 2, $"lintian (code {code}, only known errors: {string.Join(", ", AcceptedLintianErrors)})");
    }

    // A tool that is not installed is a notice, not a failure; a tool that reports errors fails the smoke test.
    private static void Optional(string tool, string[] arguments, Action<bool, string> check)
    {
        var code = Tool.Run(tool, arguments, out var text);
        if (code == -1)
        {
            Console.WriteLine($"Note: {tool} is not installed, check skipped");
            return;
        }

        Console.Write(text);
        check(code == 0, $"{tool} (Code {code})");
    }

    private static int Finish(List<string> failures)
    {
        Console.WriteLine(failures.Count == 0 ? "deb-smoke: passed." : $"deb-smoke: {failures.Count} check(s) failed.");
        return failures.Count == 0 ? 0 : 1;
    }
}

// The pinned static type2 runtime of the AppImage project (build input, see Docs/DECISIONS.md, entry "Raffica PDF: packaging and release (M2.6)").
static class AppImageRuntime
{
    public const string Version = "20251108";
    public const string Url = "https://github.com/AppImage/type2-runtime/releases/download/" + Version + "/runtime-x86_64";

    // From the release's own detached GnuPG signature runtime-x86_64.sig, checked against signing-pubkey.asc of
    // AppImage/type2-runtime (ed25519 key 570C77ACEA40C0F1B758902CBF96CCA56490F695, in the repository since 2024-05).
    public const string Sha256 = "2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d";
    public const long Size = 944632;

    public static (long Size, string Sha256) Pin => (Size, Sha256);

    // $XDG_CACHE_HOME (or ~/.cache)/RafficaBuildTools/type2-runtime-<version>-x86_64
    public static string DefaultPath(string cacheHome) => Path.Combine(cacheHome, "RafficaBuildTools", $"type2-runtime-{Version}-x86_64");

    public static string DefaultPath()
    {
        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        return DefaultPath(string.IsNullOrEmpty(cache) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache") : cache);
    }
}

// One pinned source archive that belongs to the AppImage runtime.
record RuntimeSource(string FileName, string Url, long Size, string Sha256);

// libfuse is inside the statically linked AppImage runtime under LGPL-2.1, which obliges us to offer its source with the
// binary. The two archives are attached to every release (Docs/DECISIONS.md, entry "Raffica PDF: packaging and release (M2.6)").
static class RuntimeSources
{
    // Both pins were computed from the downloads on 2026-10-23. fuse-3.15.0.tar.xz equals the SHA-256 that the runtime's own
    // scripts/common/install-dependencies.sh checks. The type2-runtime archive is GitHub's generated tag archive; if
    // GitHub ever changes its bytes, runtime-sources fails loudly and the pin must be renewed after comparing the contents.
    public static readonly RuntimeSource[] All =
    [
        new("fuse-3.15.0.tar.xz",
            "https://github.com/libfuse/libfuse/releases/download/fuse-3.15.0/fuse-3.15.0.tar.xz",
            4589124,
            "70589cfd5e1cff7ccd6ac91c86c01be340b227285c5e200baa284e401eea2ca0"),
        new($"type2-runtime-{AppImageRuntime.Version}.tar.gz",
            $"https://github.com/AppImage/type2-runtime/archive/refs/tags/{AppImageRuntime.Version}.tar.gz",
            31045,
            "4c4f6df4647c9f01f871d7edd3716d8aeeffda9d22ffbebe3fccd95a6ab52c95"),
    ];

    // Puts every verified source into outDir (from fromDir instead of the network when given). 0 ok, 1 refused or failed.
    public static int Fetch(IReadOnlyList<RuntimeSource> sources, string outDir, string? fromDir)
    {
        foreach (var source in sources)
        {
            if (Pinned.PinProblem(source.Sha256, $"RuntimeSources ({source.FileName})") is { } pinProblem)
            {
                Console.Error.WriteLine($"runtime-sources: {pinProblem}");
                return 1;
            }
        }

        Directory.CreateDirectory(outDir);
        foreach (var source in sources)
        {
            var name = $"RuntimeSources ({source.FileName})";
            var target = Path.Combine(outDir, source.FileName);
            if (File.Exists(target))
            {
                if (Pinned.VerifyFile(target, source.Size, source.Sha256, name) is { } existing)
                {
                    Console.Error.WriteLine($"runtime-sources: existing file {target} does not match ({existing}); nothing downloaded, please check and delete the file");
                    return 1;
                }

                Console.WriteLine($"runtime-sources: already present and verified: {target}");
                continue;
            }

            var download = target + ".download-" + Guid.NewGuid().ToString("N");
            try
            {
                if (fromDir is null)
                {
                    Console.WriteLine($"runtime-sources: downloading {source.Url}");
                    Pinned.Fetch(source.Url, download);
                }
                else
                {
                    File.Copy(Path.Combine(fromDir, source.FileName), download);
                }

                if (Pinned.VerifyFile(download, source.Size, source.Sha256, name) is { } problem)
                {
                    Console.Error.WriteLine($"runtime-sources: {source.FileName} rejected: {problem}");
                    return 1;
                }

                File.Move(download, target);
                Console.WriteLine($"runtime-sources: {source.FileName} (SHA-256 verified) in {target}");
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"runtime-sources: {source.FileName}: {e.Message}");
                return 1;
            }
            finally
            {
                if (File.Exists(download))
                {
                    File.Delete(download);
                }
            }
        }

        return 0;
    }
}

static class RuntimeSourcesCommand
{
    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--out", "--from");
        if (parsed is not { } p || p.Positional.Count != 0 || !p.Named.TryGetValue("--out", out var outDir))
        {
            Console.Error.WriteLine("Usage: runtime-sources --out <dir> [--from <dir>]");
            return 2;
        }

        return RuntimeSources.Fetch(RuntimeSources.All, Path.GetFullPath(outDir), p.Named.GetValueOrDefault("--from") is { } from ? Path.GetFullPath(from) : null);
    }
}

// The OCR library set that `layout --native` lays out: the library of the RID and the language data. tools/native-fetch.cs fetches
// the files and checks their pinned size and SHA-256; here only the names matter.
static class NativeFiles
{
    private static readonly string[] Tessdata = ["tessdata/eng.traineddata", "tessdata/deu.traineddata"];

    public static IReadOnlyList<string> For(string rid) => rid switch
    {
        "win-x64" => ["tesseract.dll", .. Tessdata],
        "linux-x64" => ["libtesseract.so", .. Tessdata],
        "osx-arm64" or "osx-x64" => ["libtesseract.dylib", .. Tessdata],
        _ => Tessdata,
    };
}

static class RuntimeFetch
{
    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--out");
        if (parsed is not { } p || p.Positional.Count != 0)
        {
            Console.Error.WriteLine("Usage: runtime-fetch [--out <file>]");
            return 2;
        }

        if (Pinned.PinProblem(AppImageRuntime.Sha256, "AppImageRuntime.Sha256") is { } pinProblem)
        {
            Console.Error.WriteLine($"runtime-fetch: {pinProblem}");
            return 1;
        }

        var target = Path.GetFullPath(p.Named.GetValueOrDefault("--out") ?? AppImageRuntime.DefaultPath());
        if (File.Exists(target))
        {
            if (Pinned.VerifyFile(target, AppImageRuntime.Size, AppImageRuntime.Sha256, "AppImageRuntime.Sha256") is { } existing)
            {
                Console.Error.WriteLine($"runtime-fetch: existing file {target} does not match ({existing}); nothing downloaded, please check and delete the file");
                return 1;
            }

            Console.WriteLine($"runtime-fetch: already present and verified: {target}");
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var download = target + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            Console.WriteLine($"runtime-fetch: downloading {AppImageRuntime.Url}");
            Pinned.Fetch(AppImageRuntime.Url, download);
            if (Pinned.VerifyFile(download, AppImageRuntime.Size, AppImageRuntime.Sha256, "AppImageRuntime.Sha256") is { } problem)
            {
                Console.Error.WriteLine($"runtime-fetch: download rejected: {problem}");
                return 1;
            }

            File.Move(download, target);
            Console.WriteLine($"runtime-fetch: type2 runtime {AppImageRuntime.Version} (SHA-256 verified) in {target}");
            return 0;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"runtime-fetch: {e.Message}");
            return 1;
        }
        finally
        {
            File.Delete(download);
        }
    }
}

static partial class AppImage
{
    private const int Exec = 0x1ED;  // 0755
    private const int Plain = 0x1A4; // 0644
    private const string LibDir = "usr/lib/raffica-pdf";
    private const string RootIconSize = "256x256";

    // Fixed time of everything in the image (SOURCE_DATE_EPOCH for mksquashfs), the same as ZIP and .deb.
    public static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // exec "$APPDIR/<path>" ... -> <path>
    [GeneratedRegex(@"^\s*exec\s+""\$\{?APPDIR\}?/(?<p>[^""$]+)""", RegexOptions.Multiline)]
    private static partial Regex ExecLine();

    public static string FileName(string version) => $"RafficaPdf-{version}-x86_64.AppImage";

    // The program that AppRun starts, relative to the AppDir; null when AppRun has no exec "$APPDIR/..." line.
    public static string? AppRunTarget(string appRun) => ExecLine().Match(appRun) is { Success: true } m ? m.Groups["p"].Value : null;

    // The .desktop template with the one Exec line replaced: inside the image the program is found through AppRun.
    public static string Desktop(string template)
    {
        var lines = template.Split('\n');
        if (lines.Count(l => l.StartsWith("Exec=", StringComparison.Ordinal)) != 1)
        {
            throw new IOException("the .desktop template needs exactly one Exec line");
        }

        return string.Join('\n', lines.Select(l => l.StartsWith("Exec=", StringComparison.Ordinal) ? "Exec=RafficaPdf %F" : l));
    }

    // The AppDir: AppRun, raffica-pdf.desktop, raffica-pdf.png and .DirIcon in the root, the layout in usr/lib/raffica-pdf,
    // the hicolor icons in usr/share/icons (LinuxDefaultAppService copies them from $APPDIR). Throws IOException when an input is missing.
    public static List<DebEntry> Plan(string layout, string repo)
    {
        foreach (var required in new[] { "RafficaPdf", "libpdfium.so", "LICENSE", "THIRD-PARTY-NOTICES.txt", "cli/raffica", "cli/libpdfium.so" })
        {
            if (!File.Exists(Path.Combine(layout, required)))
            {
                throw new IOException($"Layout does not contain {required}: {layout}");
            }
        }

        var entries = new List<DebEntry>();
        foreach (var file in Directory.EnumerateFiles(layout, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(layout, file).Replace('\\', '/');
            entries.Add(new DebEntry($"{LibDir}/{relative}", DebEntryKind.File, relative is "RafficaPdf" or "cli/raffica" ? Exec : Plain, file));
        }

        var icons = Path.Combine(repo, "Docs", "design", "icons", "linux", "hicolor");
        var rootIcon = Path.Combine(icons, RootIconSize, "apps", "raffica-pdf.png");
        if (!File.Exists(rootIcon))
        {
            throw new IOException($"Icon missing: {rootIcon}");
        }

        foreach (var icon in Directory.EnumerateDirectories(icons))
        {
            var png = Path.Combine(icon, "apps", "raffica-pdf.png");
            if (File.Exists(png))
            {
                entries.Add(new DebEntry($"usr/share/icons/hicolor/{Path.GetFileName(icon)}/apps/raffica-pdf.png", DebEntryKind.File, Plain, png));
            }
        }

        var appRun = Path.Combine(repo, "packaging", "linux", "AppRun");
        var desktop = Path.Combine(repo, "packaging", "linux", "raffica-pdf.desktop");
        foreach (var file in new[] { appRun, desktop })
        {
            if (!File.Exists(file))
            {
                throw new IOException($"File missing: {file}");
            }
        }

        entries.Add(new DebEntry("AppRun", DebEntryKind.File, Exec, appRun));
        entries.Add(new DebEntry("raffica-pdf.desktop", DebEntryKind.File, Plain, null, null, Desktop(File.ReadAllText(desktop))));
        entries.Add(new DebEntry("raffica-pdf.png", DebEntryKind.File, Plain, rootIcon));
        entries.Add(new DebEntry(".DirIcon", DebEntryKind.Symlink, 0x1FF, null, "raffica-pdf.png"));
        return Tree.WithParents(entries);
    }

    // Stages the AppDir, builds the SquashFS image and writes runtime + image to output (0755). Returns 0 or 1 (message written).
    // The runtime must match pin before anything is built; on any failure no output file remains.
    public static int Build(string layout, string repo, string runtime, (long Size, string Sha256) pin, string output)
    {
        if (Pinned.VerifyFile(runtime, pin.Size, pin.Sha256, "AppImageRuntime.Sha256") is { } problem)
        {
            Console.Error.WriteLine($"appimage: runtime rejected ({runtime}): {problem}");
            return 1;
        }

        var work = Path.Combine(Path.GetTempPath(), "raffica-appimage-" + Guid.NewGuid().ToString("N"));
        var fullOutput = Path.GetFullPath(output);
        var partial = fullOutput + ".partial";
        try
        {
            if (!Directory.Exists(layout))
            {
                throw new IOException($"Layout folder missing: {layout}");
            }

            var entries = Plan(layout, repo);
            var appDir = Path.Combine(work, "AppDir");
            Tree.Write(entries, appDir, FixedTime);
            var image = Path.Combine(work, "image.squashfs");

            // SOURCE_DATE_EPOCH (squashfs-tools 4.4 and later) fixes the creation time and clamps every file time to it;
            // -all-time exists only from 4.6 on and must not be combined with it. The runtime reads zstd (it links libzstd).
            var env = new Dictionary<string, string> { ["SOURCE_DATE_EPOCH"] = FixedTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) };
            var code = Tool.Run("mksquashfs", [appDir, image, "-root-owned", "-noappend", "-comp", "zstd", "-no-xattrs", "-no-progress", "-quiet"], out var text, env: env);
            if (code != 0)
            {
                Console.Error.Write(text);
                Console.Error.WriteLine(code == -1
                    ? "appimage: mksquashfs is missing (sudo apt-get install -y squashfs-tools)"
                    : $"appimage: mksquashfs ended with code {code}");
                return 1;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
            File.Delete(fullOutput);
            using (var target = File.Create(partial))
            {
                using (var source = File.OpenRead(runtime))
                {
                    source.CopyTo(target);
                }

                using (var source = File.OpenRead(image))
                {
                    source.CopyTo(target);
                }
            }

            File.SetUnixFileMode(partial, (UnixFileMode)Exec);
            File.Move(partial, fullOutput);
            Console.WriteLine($"appimage: {entries.Count(e => e.Kind == DebEntryKind.File)} files in {fullOutput}");
            return 0;
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"appimage: {e.Message}");
            return 1;
        }
        finally
        {
            File.Delete(partial);
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
        }
    }

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--layout", "--version", "--runtime", "--out", "--repo");
        if (parsed is not { } p || p.Positional.Count != 0 ||
            !p.Named.TryGetValue("--layout", out var layout) || !p.Named.TryGetValue("--version", out var version) ||
            !p.Named.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine("Usage: appimage --layout <dir> --version <x.y.z> --out <RafficaPdf-<version>-x86_64.AppImage> [--runtime <file>] [--repo <root>]");
            return 2;
        }

        if (Deb.DebVersion(version) is null)
        {
            Console.Error.WriteLine($"appimage: invalid version (expected x.y.z or x.y.z-prerelease): {version}");
            return 2;
        }

        if (Path.GetFileName(output) != FileName(version))
        {
            Console.Error.WriteLine($"appimage: the output file must be named {FileName(version)}: {output}");
            return 2;
        }

        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("appimage: only on Linux (mksquashfs)");
            return 1;
        }

        var runtime = p.Named.GetValueOrDefault("--runtime") ?? AppImageRuntime.DefaultPath();
        return Build(layout, Path.GetFullPath(p.Named.GetValueOrDefault("--repo", ".")), runtime, AppImageRuntime.Pin, output);
    }
}

static partial class AppImageSmoke
{
    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--repo");
        if (parsed is not { } p || p.Positional.Count != 1)
        {
            Console.Error.WriteLine("Usage: appimage-smoke <RafficaPdf-<version>-x86_64.AppImage> [--repo <root>]");
            return 2;
        }

        var source = Path.GetFullPath(p.Positional[0]);
        var repo = Path.GetFullPath(p.Named.GetValueOrDefault("--repo", "."));
        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"appimage-smoke: file missing: {source}");
            return 1;
        }

        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("appimage-smoke: only on Linux");
            return 1;
        }

        var failures = new List<string>();
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok    " : "FAILED")} {what}");
            if (!ok)
            {
                failures.Add(what);
            }
        }

        // (1) The image starts with exactly the pinned static runtime (no libfuse2 needed on the host).
        using (var stream = File.OpenRead(source))
        {
            var prefix = new byte[Math.Min(stream.Length, AppImageRuntime.Size)];
            stream.ReadExactly(prefix);
            var problem = Pinned.VerifyDownload(new MemoryStream(prefix), AppImageRuntime.Size, AppImageRuntime.Sha256, "AppImageRuntime.Sha256");
            Check(problem is null, $"starts with the pinned type2 runtime {AppImageRuntime.Version}{(problem is null ? string.Empty : $" ({problem})")}");
        }

        if (failures.Count > 0)
        {
            return Finish(failures); // never execute an unknown runtime
        }

        Check((File.GetUnixFileMode(source) & UnixFileMode.UserExecute) != 0, "executable");
        if (Tool.Run("objdump", ["-p", source], out var headers) != -1)
        {
            Check(ElfRules.Needed(headers).Count == 0, "Runtime is statically linked (no NEEDED library, no libfuse2)");
        }

        // (2) In a folder with a space: --appimage-offset, --appimage-extract and the CLI from the extracted tree.
        var work = Path.Combine(Path.GetTempPath(), "raffica-appimage-smoke-" + Guid.NewGuid().ToString("N"), "Raffica PDF Test");
        try
        {
            Directory.CreateDirectory(work);
            var appImage = Path.Combine(work, Path.GetFileName(source));
            File.Copy(source, appImage);
            File.SetUnixFileMode(appImage, (UnixFileMode)0x1ED);

            var offsetCode = Tool.Run(appImage, ["--appimage-offset"], out var offset);
            Check(offsetCode == 0 && offset.Trim() == AppImageRuntime.Size.ToString(CultureInfo.InvariantCulture),
                $"--appimage-offset is the size of the runtime ({offset.Trim()})");

            var extractCode = Tool.Run(appImage, ["--appimage-extract"], out var extractLog, workingDirectory: work);
            var root = Path.Combine(work, "squashfs-root");
            Check(extractCode == 0 && Directory.Exists(root), $"--appimage-extract (Code {extractCode})");
            if (!Directory.Exists(root))
            {
                Console.Write(extractLog);
                return Finish(failures);
            }

            var appRun = Path.Combine(root, "AppRun");
            var target = File.Exists(appRun) ? AppImage.AppRunTarget(File.ReadAllText(appRun)) : null;
            Check(target is not null && File.Exists(Path.Combine(root, target)) && (File.GetUnixFileMode(Path.Combine(root, target)) & UnixFileMode.UserExecute) != 0,
                $"AppRun starts an existing program ({target})");
            var desktop = Path.Combine(root, "raffica-pdf.desktop");
            Check(File.Exists(desktop) && File.ReadLines(desktop).Contains("Exec=RafficaPdf %F"), "raffica-pdf.desktop with Exec=RafficaPdf %F");
            Check(new FileInfo(Path.Combine(root, ".DirIcon")).LinkTarget == "raffica-pdf.png" && File.Exists(Path.Combine(root, "raffica-pdf.png")), ".DirIcon -> raffica-pdf.png");
            Check(File.Exists(Path.Combine(root, "usr", "share", "icons", "hicolor", "256x256", "apps", "raffica-pdf.png")), "icons under usr/share/icons/hicolor");
            Check(File.Exists(Path.Combine(root, "usr", "lib", "raffica-pdf", "THIRD-PARTY-NOTICES.txt")), "THIRD-PARTY-NOTICES.txt");

            var pdf = Path.Combine(repo, "testdata", "pdfs", "text-basic.pdf");
            var cli = Path.Combine(root, "usr", "lib", "raffica-pdf", "cli", "raffica");
            var cliCode = Tool.Run(cli, ["info", pdf], out var info);
            Check(cliCode == 0 && info.Contains("Pages: 2", StringComparison.Ordinal), $"usr/lib/raffica-pdf/cli/raffica info text-basic.pdf (Code {cliCode})");
            if (cliCode != 0)
            {
                Console.Write(info);
            }

            Directory.Delete(root, true);

            // (3) The app through AppRun: once without FUSE (APPIMAGE_EXTRACT_AND_RUN=1), once as a user starts it. In the second
            // run the runtime mounts with FUSE. Without /dev/fuse and fusermount (a container) it does NOT extract by itself: it
            // says so and exits (measured in the release dry run 39914298456), which is the expected outcome there.
            var display = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            if (!display && Tool.Run("xvfb-run", ["--help"], out _) == -1)
            {
                Console.WriteLine("Note: neither a display nor xvfb-run, start of the app skipped (sudo apt-get install -y xvfb)");
                return Finish(failures);
            }

            foreach (var extract in new[] { true, false })
            {
                var env = extract ? new Dictionary<string, string> { ["APPIMAGE_EXTRACT_AND_RUN"] = "1" } : new Dictionary<string, string>();
                var command = display ? appImage : "xvfb-run";
                var arguments = StartArguments(display, appImage, pdf, Path.Combine(work, "smoke-settings.json"));
                var code = Tool.Run(command, arguments, out var output, workingDirectory: work, env: env);
                var fuse = File.Exists("/dev/fuse") && (Tool.Run("fusermount3", ["--version"], out _) != -1 || Tool.Run("fusermount", ["--version"], out _) != -1);
                var label = extract ? "without FUSE (APPIMAGE_EXTRACT_AND_RUN=1)"
                    : fuse ? "without environment variable (through FUSE)"
                        : "without environment variable (no FUSE on the machine: the runtime says so and does not start)";
                var ok = extract ? code == 0 && HasFirstPage(output) : SecondRunIsAcceptable(fuse, code, output);
                Check(ok, $"App starts {label} and draws text-basic.pdf (first-page, code {code})");
                if (code != 0)
                {
                    Console.Write(output);
                }
            }

            return Finish(failures);
        }
        finally
        {
            var parent = Path.GetDirectoryName(work)!;
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }
    }

    // A measurement run refuses to start without --settings-file (it never touches the settings of the user), so the smoke passes a
    // file inside its own work folder.
    public static string[] StartArguments(bool display, string appImage, string pdf, string settingsFile) =>
        display
            ? ["--measure-startup", "--settings-file", settingsFile, pdf]
            : ["-a", appImage, "--measure-startup", "--settings-file", settingsFile, pdf];

    private static bool HasFirstPage(string output) => output.Split('\n').Any(l => l.Trim() == "first-page");

    // The start as a user does it. With FUSE it must work. Without FUSE the static runtime refuses with a message about
    // fusermount (that is the expected result in a container); it counts as fine, any other failure does not.
    public static bool SecondRunIsAcceptable(bool fuseAvailable, int code, string output) =>
        code == 0 ? HasFirstPage(output) : !fuseAvailable && output.Contains("fusermount", StringComparison.Ordinal);

    private static int Finish(List<string> failures)
    {
        Console.WriteLine(failures.Count == 0 ? "appimage-smoke: passed." : $"appimage-smoke: {failures.Count} check(s) failed.");
        return failures.Count == 0 ? 0 : 1;
    }
}

// The disk image RafficaPdf-<version>-macos-<arm64|x64>.dmg (Milestone 3.3): the app and a link to /Applications, no background picture.
// macOS only (hdiutil, ditto); everything runs as an argument list, never through a shell.
static class Dmg
{
    public const string VolumeName = "Raffica PDF";
    private const string Usage = "Usage: dmg --app <\"Raffica PDF.app\"> --rid <osx-arm64|osx-x64> --version <x.y.z> --out <RafficaPdf-<version>-macos-<arm64|x64>.dmg>";

    public static string FileName(string version, string rid) => $"RafficaPdf-{version}-macos-{MacApp.Arch(rid)}.dmg";

    // The argument rules (before the macOS check, so they can be tested everywhere): null when the arguments are acceptable.
    public static string? Problem(string rid, string version, string output)
    {
        if (rid is not ("osx-arm64" or "osx-x64"))
        {
            return $"dmg: unknown RID: {rid}";
        }

        if (!MacApp.IsStable(version))
        {
            return $"dmg: only a stable version x.y.z is allowed, not: {version}";
        }

        return Path.GetFileName(output) != FileName(version, rid)
            ? $"dmg: the output file must be named {FileName(version, rid)}: {output}"
            : null;
    }

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--app", "--rid", "--version", "--out");
        if (parsed is not { } p || p.Positional.Count != 0 ||
            !p.Named.TryGetValue("--app", out var app) || !p.Named.TryGetValue("--rid", out var rid) ||
            !p.Named.TryGetValue("--version", out var version) || !p.Named.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (Problem(rid, version, output) is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 2;
        }

        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("dmg: only on macOS (hdiutil and ditto are needed)");
            return 2;
        }

        return Build(Path.GetFullPath(app), Path.GetFullPath(output));
    }

    private static int Build(string app, string output)
    {
        app = app.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(app) || !app.EndsWith(".app", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"dmg: the app bundle is missing: {app}");
            return 1;
        }

        var stage = Path.Combine(Path.GetTempPath(), "raffica-dmg-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            // ditto keeps symbolic links, extended attributes and the signature of the bundle intact.
            if (Tool.Run("ditto", [app, Path.Combine(stage, MacApp.BundleName)], out var dittoLog) != 0)
            {
                Console.Error.WriteLine($"dmg: ditto failed: {dittoLog}");
                return 1;
            }

            File.CreateSymbolicLink(Path.Combine(stage, "Applications"), "/Applications");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (Tool.Run("hdiutil", ["create", "-volname", VolumeName, "-srcfolder", stage, "-fs", "HFS+", "-format", "UDZO", "-ov", output], out var createLog) != 0)
            {
                Console.Error.WriteLine($"dmg: hdiutil create failed: {createLog}");
                return 1;
            }

            if (Tool.Run("hdiutil", ["verify", output], out var verifyLog) != 0)
            {
                Console.Error.WriteLine($"dmg: hdiutil verify failed: {verifyLog}");
                return 1;
            }

            Console.WriteLine($"dmg: {output}");
            Console.Write(Sums.Line(output));
            return 0;
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, true);
            }
        }
    }
}

// The check of a finished disk image on macOS (Milestone 3.3): contents, the copy out of it, signature, architectures and a start
// of the app. The dependency check of the Mach-O files is a workflow step (tools/check-binary.cs), not part of this command.
static class DmgSmoke
{
    private const string Usage = "Usage: dmg-smoke --dmg <file> --rid <osx-arm64|osx-x64> [--ceiling-seconds 5]";

    // The argument rules (before the macOS check, so they can be tested everywhere): null when the arguments are acceptable.
    public static string? Problem(string rid, string ceilingText, out double ceiling)
    {
        ceiling = 0;
        if (rid is not ("osx-arm64" or "osx-x64"))
        {
            return $"dmg-smoke: unknown RID: {rid}";
        }

        return !double.TryParse(ceilingText, NumberStyles.Float, CultureInfo.InvariantCulture, out ceiling) || !(ceiling > 0) || double.IsInfinity(ceiling)
            ? $"dmg-smoke: --ceiling-seconds must be a positive number: {ceilingText}"
            : null;
    }

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--dmg", "--rid", "--ceiling-seconds");
        if (parsed is not { } p || p.Positional.Count != 0 ||
            !p.Named.TryGetValue("--dmg", out var dmg) || !p.Named.TryGetValue("--rid", out var rid))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        // The ceiling is the coarse "starts within a few seconds" value (spec section 6): it catches a build that cannot run, not speed.
        if (Problem(rid, p.Named.GetValueOrDefault("--ceiling-seconds", "5"), out var ceiling) is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 2;
        }

        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("dmg-smoke: only on macOS (hdiutil, ditto, codesign and lipo are needed)");
            return 2;
        }

        var source = Path.GetFullPath(dmg);
        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"dmg-smoke: file missing: {source}");
            return 1;
        }

        var failures = new List<string>();
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok    " : "FAILED")} {what}");
            if (!ok)
            {
                failures.Add(what);
            }
        }

        var work = Path.Combine(Path.GetTempPath(), "raffica-dmg-smoke-" + Guid.NewGuid().ToString("N"));
        var mount = Path.Combine(work, "mount");
        var copy = Path.Combine(work, "copy folder");
        var stuck = false;

        // Attaches, checks the contents, copies the app out and detaches again; the detach also happens on every failure.
        bool CopyOut()
        {
            var attached = false;
            try
            {
                Directory.CreateDirectory(mount);
                var attachCode = Tool.Run("hdiutil", ["attach", "-readonly", "-nobrowse", "-noautoopen", "-noverify", "-mountpoint", mount, source], out var attachLog);
                attached = attachCode == 0;
                Check(attached, $"hdiutil attach, read-only and hidden (code {attachCode})");
                if (!attached)
                {
                    Console.Write(attachLog);
                    return false;
                }

                // Names starting with a dot (.fseventsd, .Trashes, .DS_Store) are the volume's, not content.
                var names = Directory.EnumerateFileSystemEntries(mount).Select(Path.GetFileName).Where(n => !n!.StartsWith('.')).Order(StringComparer.Ordinal).ToList();
                Check(string.Join('|', names) == "Applications|" + MacApp.BundleName, $"the image holds exactly {MacApp.BundleName} and Applications ({string.Join(", ", names)})");
                var link = new FileInfo(Path.Combine(mount, "Applications")).LinkTarget;
                Check(link == "/Applications", $"Applications links to /Applications ({link ?? "no link"})");

                var inImage = Path.Combine(mount, MacApp.BundleName);
                var dittoCode = Directory.Exists(inImage) ? Tool.Run("ditto", [inImage, Path.Combine(copy, MacApp.BundleName)], out _) : -1;
                Check(dittoCode == 0, $"app copied out of the image with ditto (code {dittoCode})");
                return dittoCode == 0;
            }
            finally
            {
                if (attached)
                {
                    var detached = Tool.Run("hdiutil", ["detach", mount], out _) == 0 || Tool.Run("hdiutil", ["detach", "-force", mount], out _) == 0;
                    stuck = !detached;
                    Check(detached, "hdiutil detach");
                }
            }
        }

        try
        {
            Directory.CreateDirectory(copy);
            if (!CopyOut())
            {
                return Finish(failures);
            }

            var bundle = Path.Combine(copy, MacApp.BundleName);
            var signCode = Tool.Run("codesign", ["--verify", "--deep", "--strict", bundle], out var signLog);
            Check(signCode == 0, $"codesign --verify --deep --strict on the copy (code {signCode})");
            if (signCode != 0)
            {
                Console.Write(signLog);
            }

            var macOs = Path.Combine(bundle, "Contents", "MacOS");
            var expected = MacApp.Arch(rid) == "x64" ? "x86_64" : "arm64";
            var machO = Directory.Exists(macOs)
                ? Directory.EnumerateFiles(macOs, "*", SearchOption.AllDirectories)
                    .Where(f => Path.GetFileName(f) is "RafficaPdf" or "raffica" || f.EndsWith(".dylib", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal).ToList()
                : [];
            Check(machO.Any(f => Path.GetFileName(f) == "RafficaPdf") && machO.Any(f => Path.GetFileName(f) == "raffica"), "Contents/MacOS holds RafficaPdf and raffica");
            foreach (var file in machO)
            {
                var name = Path.GetRelativePath(macOs, file);
                var code = Tool.Run("lipo", ["-archs", file], out var archs);
                var found = archs.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                if (code == 0 && found.Length > 1 && file.EndsWith(".dylib", StringComparison.Ordinal))
                {
                    Console.WriteLine($"ok     {name}: universal library ({string.Join(' ', found)}), not compared");
                }
                else
                {
                    // RafficaPdf and raffica are never universal: exactly the one architecture of the RID.
                    Check(code == 0 && found.Length == 1 && found[0] == expected, $"{name}: architecture {(code == 0 ? string.Join(' ', found) : "unreadable")} (expected {expected})");
                }
            }

            var program = Path.Combine(macOs, "RafficaPdf");
            if (File.Exists(program))
            {
                // An Intel image on an Apple silicon host runs under Rosetta; arch picks the Intel slice.
                var viaArch = rid == "osx-x64" && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64;
                foreach (var (lang, text) in new[] { ("de", "Ansicht"), ("en", "View") })
                {
                    string[] arguments = ["--print-string", "TabView", "--lang", lang];
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var code = viaArch ? Tool.Run("arch", ["-x86_64", program, .. arguments], out var printed) : Tool.Run(program, arguments, out printed);
                    watch.Stop();
                    var seconds = watch.Elapsed.TotalSeconds;
                    Check(code == 0 && printed.Trim() == text && seconds <= ceiling,
                        $"RafficaPdf --print-string TabView --lang {lang} prints {text} within {ceiling.ToString(CultureInfo.InvariantCulture)} s (code {code}, {seconds.ToString("0.00", CultureInfo.InvariantCulture)} s{(viaArch ? ", under arch -x86_64" : string.Empty)})");
                    if (printed.Trim() != text)
                    {
                        Console.WriteLine($"       output: {printed.Trim()}");
                    }
                }
            }
            else
            {
                Check(false, "RafficaPdf is missing in the copy");
            }

            return Finish(failures);
        }
        finally
        {
            // Never recurse into a mount that could not be detached.
            if (!stuck)
            {
                try
                {
                    Directory.Delete(work, true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static int Finish(List<string> failures)
    {
        Console.WriteLine(failures.Count == 0 ? "dmg-smoke: passed." : $"dmg-smoke: {failures.Count} check(s) failed.");
        return failures.Count == 0 ? 0 : 1;
    }
}

// The tag check of .github/workflows/release.yml: the same rule once in PowerShell (-cnotmatch) and once in bash (=~).
static class TagRules
{
    // PowerShell: if ($env:TAG -cnotmatch '<pattern>') { throw ... }
    public static string? PowerShellPattern(string workflow)
    {
        var m = Regex.Match(workflow, @"-cnotmatch '(?<p>[^']+)'");
        return m.Success ? m.Groups["p"].Value : null;
    }

    // bash: [[ "$TAG" =~ <pattern> ]]
    public static string? BashPattern(string workflow)
    {
        var m = Regex.Match(workflow, @"\[\[ ""\$TAG"" =~ (?<p>\S+) \]\]");
        return m.Success ? m.Groups["p"].Value : null;
    }

    // Case-sensitive, culture-independent; the pattern is anchored itself.
    public static bool Accepts(string pattern, string tag) =>
        Regex.IsMatch(tag, pattern, RegexOptions.CultureInvariant);
}

// The rule for the dispatch input "ref" of release.yml (a dry run builds that ref): empty, a commit id, or a plain branch
// name. Unqualified "pull/N/head" must not pass: GitHub resolves it, and a runner without git downloads that zip.
// The first step of every job in release.yml holds the same rule in bash or PowerShell; the self-test runs those steps.
static class RefInputRule
{
    public static bool Accepts(string value)
    {
        if (value.Length == 0 || Regex.IsMatch(value, "^[0-9a-f]{40}$"))
        {
            return true;
        }

        return Regex.IsMatch(value, @"^[A-Za-z0-9._/-]+$") && !value.StartsWith("pull/", StringComparison.Ordinal) &&
            !value.Contains("..", StringComparison.Ordinal) &&
            (!value.StartsWith("refs/", StringComparison.Ordinal) || value.StartsWith("refs/heads/", StringComparison.Ordinal));
    }
}

// The macOS app bundle "Raffica PDF.app" (Milestone 3.3): the layout of Contents/, Info.plist, the icon and the ad-hoc signature.
// macOS only (sips, iconutil, plutil, codesign); the pure parts (InfoPlist, MinSystemVersion, Arch) run everywhere.
static partial class MacApp
{
    public const string BundleName = "Raffica PDF.app";
    public const string BundleId = "io.github.benjo-code.raffica.pdf";
    private const string Usage = "Usage: macos-app --rid <osx-arm64|osx-x64> --app <publish dir> --cli <publish dir> --out <dir> --version <x.y.z> [--native <dir>] [--repo <dir>]";

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")]
    private static partial Regex StableVersion();

    public static string Arch(string rid) => rid switch
    {
        "osx-arm64" => "arm64",
        "osx-x64" => "x64",
        _ => throw new ArgumentException($"unknown RID: {rid}", nameof(rid)),
    };

    // Reads MACOS_DEPLOYMENT_TARGET from the text of packaging/native/versions.txt (the one place the minimum is written down).
    public static string MinSystemVersion(string versionsFileText)
    {
        foreach (var raw in versionsFileText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("MACOS_DEPLOYMENT_TARGET=", StringComparison.Ordinal))
            {
                var value = line["MACOS_DEPLOYMENT_TARGET=".Length..].Trim();
                return value.Length > 0 ? value : throw new IOException("MACOS_DEPLOYMENT_TARGET is empty in versions.txt");
            }
        }

        throw new IOException("MACOS_DEPLOYMENT_TARGET is missing in versions.txt");
    }

    // Only stable versions have a numeric plist form (CFBundleVersion); callers check with IsStable first.
    public static bool IsStable(string version) => StableVersion().IsMatch(version);

    public static string InfoPlist(string version, string minSystemVersion)
    {
        var v = System.Security.SecurityElement.Escape(version);
        var m = System.Security.SecurityElement.Escape(minSystemVersion);
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>CFBundleIdentifier</key>
              <string>{BundleId}</string>
              <key>CFBundleExecutable</key>
              <string>RafficaPdf</string>
              <key>CFBundleName</key>
              <string>Raffica PDF</string>
              <key>CFBundleDisplayName</key>
              <string>Raffica PDF</string>
              <key>CFBundlePackageType</key>
              <string>APPL</string>
              <key>CFBundleShortVersionString</key>
              <string>{v}</string>
              <key>CFBundleVersion</key>
              <string>{v}</string>
              <key>CFBundleIconFile</key>
              <string>RafficaPdf</string>
              <key>NSHighResolutionCapable</key>
              <true/>
              <key>LSApplicationCategoryType</key>
              <string>public.app-category.productivity</string>
              <key>LSMinimumSystemVersion</key>
              <string>{m}</string>
              <key>CFBundleDocumentTypes</key>
              <array>
                <dict>
                  <key>CFBundleTypeName</key>
                  <string>PDF Document</string>
                  <key>CFBundleTypeRole</key>
                  <string>Editor</string>
                  <key>LSHandlerRank</key>
                  <string>Alternate</string>
                  <key>LSItemContentTypes</key>
                  <array>
                    <string>com.adobe.pdf</string>
                  </array>
                </dict>
              </array>
            </dict>
            </plist>

            """;
    }

    public static int Run(string[] args)
    {
        var parsed = Options.Parse(args, "--rid", "--app", "--cli", "--out", "--version", "--native", "--repo");
        if (parsed is not { } p || p.Positional.Count != 0 ||
            !p.Named.TryGetValue("--rid", out var rid) || !p.Named.TryGetValue("--app", out var app) ||
            !p.Named.TryGetValue("--cli", out var cli) || !p.Named.TryGetValue("--out", out var output) ||
            !p.Named.TryGetValue("--version", out var version))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (rid is not ("osx-arm64" or "osx-x64"))
        {
            Console.Error.WriteLine($"macos-app: unknown RID: {rid}");
            return 2;
        }

        if (!IsStable(version))
        {
            Console.Error.WriteLine($"macos-app: only a stable version x.y.z has a plist form, not: {version}");
            return 2;
        }

        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("macos-app: only on macOS (sips, iconutil, plutil and codesign are needed)");
            return 2;
        }

        return Build(rid, app, cli, output, version, p.Named.GetValueOrDefault("--native"), p.Named.GetValueOrDefault("--repo", "."));
    }

    private static readonly string[] SkippedExtensions = [".pdb", ".dbg"];

    private static bool Skipped(string relative) =>
        SkippedExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase) ||
        relative.Split('/', '\\').Any(s => s.EndsWith(".dSYM", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Files(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f))
            .Where(r => !Skipped(r))
            .Order(StringComparer.Ordinal);

    private static int Build(string rid, string app, string cli, string output, string version, string? nativeDir, string repo)
    {
        var library = Layout.NativeLibrary(rid);
        var license = Path.Combine(repo, "LICENSE");
        var notices = Path.Combine(repo, "src", "Raffica.Pdf", "Resources", "ThirdPartyNotices.txt");
        var icon = Path.Combine(repo, "Docs", "design", "icons", "brand", "pdf.png");
        var versions = Path.Combine(repo, "packaging", "native", "versions.txt");
        var bundle = Path.Combine(output, BundleName);
        var problems = new List<string>();
        foreach (var (label, dir, exe) in new[] { ("App", app, "RafficaPdf"), ("CLI", cli, "raffica") })
        {
            if (!Directory.Exists(dir))
            {
                problems.Add($"{label} folder missing: {dir}");
            }
            else
            {
                foreach (var need in new[] { exe, library })
                {
                    if (!File.Exists(Path.Combine(dir, need)))
                    {
                        problems.Add($"{label} folder does not contain {need}: {dir}");
                    }
                }
            }
        }

        foreach (var file in new[] { license, notices, icon, versions })
        {
            if (!File.Exists(file))
            {
                problems.Add($"File missing: {file}");
            }
        }

        if (Directory.Exists(app) && nativeDir is not null)
        {
            foreach (var target in NativeFiles.For(rid))
            {
                if (!File.Exists(Path.Combine(nativeDir, target)))
                {
                    problems.Add($"Folder --native does not contain {target}: {nativeDir}");
                }

                if (File.Exists(Path.Combine(app, target)) || File.Exists(Path.Combine(cli, target)))
                {
                    problems.Add($"The app or CLI folder already contains {target}; --native does not overwrite it");
                }
            }
        }

        // The CLI's own files (its native libraries) must be the app's, byte for byte: the bundle holds one copy next to both programs.
        var extraFromCli = new List<string>();
        if (Directory.Exists(app) && Directory.Exists(cli))
        {
            foreach (var relative in Files(cli).Where(r => r != "raffica"))
            {
                var inApp = Path.Combine(app, relative);
                if (!File.Exists(inApp))
                {
                    extraFromCli.Add(relative);
                }
                else if (!File.ReadAllBytes(inApp).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(cli, relative))))
                {
                    problems.Add($"The CLI's {relative} differs from the app's; the bundle keeps one copy");
                }
            }
        }

        if (Directory.Exists(bundle))
        {
            problems.Add($"Bundle exists already: {bundle}");
        }

        if (problems.Count > 0)
        {
            foreach (var problem in problems)
            {
                Console.Error.WriteLine($"macos-app: {problem}");
            }

            return 1;
        }

        var work = Path.Combine(Path.GetTempPath(), "raffica-macapp-" + Guid.NewGuid().ToString("N"));
        var success = false;
        try
        {
            var macOs = Path.Combine(bundle, "Contents", "MacOS");
            var resources = Path.Combine(bundle, "Contents", "Resources");
            Directory.CreateDirectory(macOs);
            Directory.CreateDirectory(resources);
            foreach (var relative in Files(app))
            {
                Copy(Path.Combine(app, relative), Path.Combine(macOs, relative), relative == "RafficaPdf");
            }

            Copy(Path.Combine(cli, "raffica"), Path.Combine(macOs, "raffica"), true);
            foreach (var relative in extraFromCli)
            {
                Copy(Path.Combine(cli, relative), Path.Combine(macOs, relative), false);
            }

            if (nativeDir is not null)
            {
                foreach (var target in NativeFiles.For(rid))
                {
                    Copy(Path.Combine(nativeDir, target), Path.Combine(macOs, target), false);
                }
            }

            Copy(license, Path.Combine(resources, "LICENSE"), false);
            Copy(notices, Path.Combine(resources, "THIRD-PARTY-NOTICES.txt"), false);

            var plist = Path.Combine(bundle, "Contents", "Info.plist");
            File.WriteAllText(plist, InfoPlist(version, MinSystemVersion(File.ReadAllText(versions))), new UTF8Encoding(false));
            if (Step("plutil", ["-lint", plist]) != 0)
            {
                return 1;
            }

            Directory.CreateDirectory(work);
            if (!MakeIcon(icon, Path.Combine(work, "RafficaPdf.iconset"), Path.Combine(resources, "RafficaPdf.icns")))
            {
                return 1;
            }

            // Ad-hoc signing (no Developer ID, no notarization): every Mach-O first, then the bundle, then the strict check.
            var machO = Directory.EnumerateFiles(macOs, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) is "RafficaPdf" or "raffica" || f.EndsWith(".dylib", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);
            foreach (var file in machO)
            {
                if (Step("codesign", ["--force", "-s", "-", file]) != 0)
                {
                    return 1;
                }
            }

            if (Step("codesign", ["--force", "-s", "-", bundle]) != 0 ||
                Step("codesign", ["--verify", "--deep", "--strict", bundle]) != 0)
            {
                return 1;
            }

            Console.WriteLine($"macos-app: {Arch(rid)} bundle {bundle}");
            success = true;
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"macos-app: {e.Message}");
            return 1;
        }
        finally
        {
            // A failed run leaves no half-built (or half-signed) bundle behind.
            foreach (var path in success ? new[] { work } : new[] { work, bundle })
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    // sips resizes, iconutil packs; the names are the ones iconutil requires (icon_<n>x<n>.png and icon_<n>x<n>@2x.png).
    private static bool MakeIcon(string source, string iconset, string icns)
    {
        Directory.CreateDirectory(iconset);
        foreach (var size in new[] { 16, 32, 128, 256, 512 })
        {
            foreach (var (suffix, pixels) in new[] { ("", size), ("@2x", size * 2) })
            {
                var target = Path.Combine(iconset, $"icon_{size}x{size}{suffix}.png");
                if (Step("sips", ["-z", pixels.ToString(CultureInfo.InvariantCulture), pixels.ToString(CultureInfo.InvariantCulture), source, "--out", target]) != 0)
                {
                    return false;
                }
            }
        }

        return Step("iconutil", ["-c", "icns", "-o", icns, iconset]) == 0;
    }

    private static int Step(string tool, string[] arguments)
    {
        var code = Tool.Run(tool, arguments, out var output);
        if (code != 0)
        {
            Console.Error.WriteLine($"macos-app: {tool} {string.Join(' ', arguments.Take(2))} ... ended with code {code}: {output.Trim()}");
        }

        return code;
    }

    // Executables get 0755, everything else 0644, so the bundle does not depend on the build machine's umask.
    private static void Copy(string from, string to, bool executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(to, executable
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }
}

static class SelfTest
{
    private static int s_checks;
    private static int s_failures;

    private const string Header = "\nDYNAMIC SYMBOL TABLE:\n";

    private static string Symbols(params string[] versions) =>
        Header + string.Concat(versions.Select((v, i) =>
            $"0000000000000000      DF *UND*\t0000000000000000 (GLIBC_{v}) sym{i}\n")) +
        "0000000000001139 g    DF .text\t000000000000000b  Base        main\n";

    private static string CxxSymbols(params string[] versions) =>
        Header + string.Concat(versions.Select((v, i) =>
            $"0000000000000000      DF *UND*\t0000000000000000 (GLIBCXX_{v}) _ZSt{i}\n"));

    private static string Needs(params string[] libs) =>
        "\nDynamic Section:\n" + string.Concat(libs.Select(l => $"  NEEDED               {l}\n")) +
        "  SONAME               libx.so\n";

    public static int Run(string[] args)
    {
        _ = args;
        var max = new[] { 2, 35 };
        const string maxText = "2.35";
        var libc = Needs("libc.so.6");

        Expect("2.34 passes", ElfRules.Violations(Symbols("2.34"), libc, max, maxText).Count, 0);
        Expect("2.35 passes (limit)", ElfRules.Violations(Symbols("2.35"), libc, max, maxText).Count, 0);
        Expect("2.38 fails", ElfRules.Violations(Symbols("2.2.5", "2.38"), libc, max, maxText).Count, 1);
        Expect("2.9 is smaller than 2.35 (number tuple, not text)", ElfRules.Violations(Symbols("2.9"), libc, max, maxText).Count, 0);
        Expect("2.100 is greater than 2.35", ElfRules.Violations(Symbols("2.100"), libc, max, maxText).Count, 1);
        Expect("unknown NEEDED fails", ElfRules.Violations(Symbols("2.2.5"), Needs("libc.so.6", "libfoo.so.1"), max, maxText).Count, 1);
        Expect("libfontconfig.so.1 (Skia) is allowed", ElfRules.Violations(Symbols("2.2.5"), Needs("libc.so.6", "libfontconfig.so.1"), max, maxText).Count, 0);
        Expect("another system library (libX11) still fails", ElfRules.Violations(Symbols("2.2.5"), Needs("libc.so.6", "libX11.so.6"), max, maxText).Count, 1);
        Expect("all allowed NEEDED libraries pass", ElfRules.Violations(Symbols("2.2.5"), Needs(ElfRules.AllowedNeeded), max, maxText).Count, 0);
        Expect("SONAME is not a NEEDED", ElfRules.Needed(Needs("libc.so.6")).Count, 1);
        Expect("a defined symbol does not count", ElfRules.HighestGlibc("0000 g DF .text 000b (GLIBC_2.99) x\n", out _) ?? "none", "none");
        Expect("Version of the highest symbol", ElfRules.HighestGlibc(Symbols("2.2.5", "2.34", "2.17"), out _) ?? "none", "2.34");
        Expect("comparison 2.35 = 2.35.0", ElfRules.Compare([2, 35], [2, 35, 0]), 0);

        Expect("GLIBCXX 3.4.30 passes (limit)", ElfRules.Violations(CxxSymbols("3.4.29", "3.4.30"), libc, max, maxText).Count, 0);
        Expect("GLIBCXX 3.4.31 fails", ElfRules.Violations(CxxSymbols("3.4.19", "3.4.31"), libc, max, maxText).Count, 1);
        Expect("GLIBCXX 3.4.9 is smaller than 3.4.30 (number tuple, not text)", ElfRules.Violations(CxxSymbols("3.4.9"), libc, max, maxText).Count, 0);
        Expect("GLIBCXX 3.4.100 is greater than 3.4.30", ElfRules.Violations(CxxSymbols("3.4.100"), libc, max, maxText).Count, 1);
        Expect("GLIBCXX: --max-glibcxx raises the limit", ElfRules.Violations(CxxSymbols("3.4.31"), libc, max, maxText, "3.4.31").Count, 0);
        Expect("GLIBCXX: the message names symbol and limit", ElfRules.Violations(CxxSymbols("3.4.31"), libc, max, maxText).SingleOrDefault() ?? "none",
            "needs GLIBCXX_3.4.31 (at most GLIBCXX_3.4.30)");
        Expect("GLIBCXX and GLIBC are reported separately", ElfRules.Violations(CxxSymbols("3.4.31") + Symbols("2.38"), libc, max, maxText).Count, 2);
        Expect("without GLIBCXX symbols passes", ElfRules.Violations(Symbols("2.2.5"), libc, max, maxText).Count, 0);
        Expect("GLIBCXX does not count as GLIBC", ElfRules.HighestGlibc(CxxSymbols("3.4.31"), out _) ?? "none", "none");
        Expect("highest GLIBCXX symbol", ElfRules.HighestGlibcxx(CxxSymbols("3.4.9", "3.4.30", "3.4.19"), out _) ?? "none", "3.4.30");

        PackagingCommands();
        InstallerParts();
        DebParts();
        AppImageParts();
        TagRuleParts();
        RuntimeSourceParts();
        BuildTelemetryParts();
        WorkflowYamlParts();
        LinuxContainerParts();
        UpdateReleaseParts();
        ManifestJobParts();
        PublicRepoWorkflowParts();
        UpdateTestBuildGuardParts();
        MacAppParts();
        DmgParts();
        Expect("unknown command returns 2", Cli.Run(["does-not-exist"]), 2);

        Console.WriteLine(s_failures == 0
            ? $"selftest: {s_checks} checks passed."
            : $"selftest: {s_failures} of {s_checks} checks failed.");
        return s_failures == 0 ? 0 : 1;
    }

    // The tag rule exists twice in release.yml (PowerShell and bash). Both must be the same and must accept
    // exactly SemVer tags, and a dry run must be able to build a branch without uploading it.
    private static void TagRuleParts()
    {
        const string workflow = ".github/workflows/release.yml";
        Expect($"tag: {workflow} present", File.Exists(workflow), true);
        if (!File.Exists(workflow))
        {
            return;
        }

        var yml = File.ReadAllText(workflow);
        var powerShell = TagRules.PowerShellPattern(yml);
        var bash = TagRules.BashPattern(yml);
        Expect("tag: PowerShell rule found", powerShell is not null, true);
        Expect("tag: bash rule found", bash is not null, true);
        if (powerShell is null || bash is null)
        {
            return;
        }

        Expect("tag: both rules are the same", powerShell, bash);
        foreach (var tag in new[] { "v0.1.0", "v10.20.30", "v0.1.0-rc.1", "v1.0.0-beta" })
        {
            Expect($"tag: {tag} is accepted", TagRules.Accepts(bash, tag), true);
        }

        foreach (var tag in new[] { "V0.1.0", "v0.1", "v0.1.0.", "0.1.0", "v0.1.0-", "v0.1.0 ", " v0.1.0", "v0.1.0-rc 1", "" })
        {
            Expect($"tag: '{tag}' is rejected", TagRules.Accepts(bash, tag), false);
        }

        Expect("tag: all bash rules are the same (Linux and manifest job)",
            Regex.Matches(yml, @"\[\[ ""\$TAG"" =~ (?<p>\S+) \]\]").Select(m => m.Groups["p"].Value).Distinct(StringComparer.Ordinal).Count(), 1);
        Expect("tag: both checkouts of the build jobs also build a branch (inputs.ref)", Regex.Count(yml, @"ref: \$\{\{ inputs\.ref \|\| "), 2);
        Expect("tag: all four jobs refuse to upload a branch build", Regex.Count(yml, "name: Refuse to upload a branch build"), 4);
    }

    // The AppImage contains libfuse (LGPL-2.1): its source and the runtime's build scripts are attached to every release.
    private static void RuntimeSourceParts()
    {
        foreach (var source in RuntimeSources.All)
        {
            Expect($"sources: {source.FileName} is pinned", Pinned.PinProblem(source.Sha256, source.FileName), null);
            Expect($"sources: {source.FileName} has a size", source.Size > 0, true);
            Expect($"sources: {source.FileName} comes from a pinned version", source.Url.StartsWith("https://github.com/", StringComparison.Ordinal), true);
        }

        Expect("sources: libfuse 3.15.0 (as in third-party/appimage-runtime/README.md)", RuntimeSources.All.Any(x => x.FileName == "fuse-3.15.0.tar.xz"), true);
        Expect("sources: type2-runtime in the version of the runtime", RuntimeSources.All.Any(x => x.FileName == $"type2-runtime-{AppImageRuntime.Version}.tar.gz"), true);

        const string container = "packaging/linux/container-build.sh";
        const string workflow = ".github/workflows/release.yml";
        Expect($"sources: {container} present", File.Exists(container), true);
        Expect($"sources: {workflow} present", File.Exists(workflow), true);
        if (File.Exists(container) && File.Exists(workflow))
        {
            var script = File.ReadAllText(container);
            var yml = File.ReadAllText(workflow);
            Expect("sources: container-build.sh calls runtime-sources", script.Contains("runtime-sources --out out/release", StringComparison.Ordinal), true);
            foreach (var source in RuntimeSources.All)
            {
                Expect($"sources: {source.FileName} is in SHA256SUMS-linux.txt", script.Contains($"out/release/{source.FileName}", StringComparison.Ordinal), true);
                Expect($"sources: {source.FileName} is uploaded", yml.Contains($"out/release/{source.FileName}", StringComparison.Ordinal), true);
            }
        }

        var dir = Path.Combine(Path.GetTempPath(), "raffica-sources-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var from = Path.Combine(dir, "from");
            Directory.CreateDirectory(from);
            var good = new RuntimeSource("a.tar.gz", "https://example.invalid/a.tar.gz", 5, Convert.ToHexStringLower(SHA256.HashData("hello"u8)));
            var other = new RuntimeSource("b.tar.xz", "https://example.invalid/b.tar.xz", 3, Convert.ToHexStringLower(SHA256.HashData("abc"u8)));
            File.WriteAllText(Path.Combine(from, "a.tar.gz"), "hello");
            File.WriteAllText(Path.Combine(from, "b.tar.xz"), "abc");

            var outDir = Path.Combine(dir, "out");
            Expect("sources: correct files are accepted", RuntimeSources.Fetch([good, other], outDir, from), 0);
            Expect("sources: file is in the output folder", File.ReadAllText(Path.Combine(outDir, "a.tar.gz")), "hello");
            Expect("sources: no leftover of the download", Directory.GetFiles(outDir).Length, 2);
            Expect("sources: second run recognizes existing, verified files", RuntimeSources.Fetch([good, other], outDir, from), 0);

            File.WriteAllText(Path.Combine(from, "b.tar.xz"), "xyz");
            var refused = Path.Combine(dir, "refused");
            Expect("sources: wrong SHA-256 returns 1", RuntimeSources.Fetch([good, other], refused, from), 1);
            Expect("sources: wrong file is not put down", File.Exists(Path.Combine(refused, "b.tar.xz")), false);
            Expect("sources: no download leftover after a refusal", Directory.GetFiles(refused).All(f => !f.Contains(".download-", StringComparison.Ordinal)), true);

            File.WriteAllText(Path.Combine(outDir, "a.tar.gz"), "tampered");
            Expect("sources: existing wrong file is refused", RuntimeSources.Fetch([good], outDir, from), 1);
            Expect("sources: wrong file is left untouched", File.ReadAllText(Path.Combine(outDir, "a.tar.gz")), "tampered");

            var unpinned = good with { Sha256 = "not pinned" };
            Expect("sources: unpinned checksum returns 1", RuntimeSources.Fetch([unpinned], Path.Combine(dir, "u"), from), 1);
            Expect("sources: missing source returns 1", RuntimeSources.Fetch([good with { FileName = "nope.tar.gz" }], Path.Combine(dir, "m"), from), 1);
            Expect("runtime-sources: without --out returns 2", Cli.Run(["runtime-sources"]), 2);
            Expect("runtime-sources: unknown option returns 2", Cli.Run(["runtime-sources", "--out", dir, "--does-not-exist", "x"]), 2);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Avalonia.BuildServices collects usage data while compiling unless AVALONIA_TELEMETRY_OPTOUT=1 is set
    // (Docs/DECISIONS.md, entry "Raffica PDF: packaging and release (M2.6)"). Every place that compiles the app must set it.
    private static void BuildTelemetryParts()
    {
        foreach (var file in new[]
        {
            ".github/workflows/ci.yml", ".github/workflows/release.yml", ".github/workflows/measure.yml",
            "packaging/linux/container-build.sh",
        })
        {
            Expect($"telemetry: {file} present", File.Exists(file), true);
            if (File.Exists(file))
            {
                Expect($"telemetry: {file} sets AVALONIA_TELEMETRY_OPTOUT=1",
                    Regex.IsMatch(File.ReadAllText(file), @"AVALONIA_TELEMETRY_OPTOUT(: '1'|=1)"), true);
            }
        }
    }

    // There is no YAML parser in the base library. The one mistake that broke release.yml once: an unquoted one-line
    // "run: ..." value that contains ": " (YAML reads the second colon as a mapping and the whole file is invalid).
    private static void WorkflowYamlParts()
    {
        foreach (var file in Directory.GetFiles(".github/workflows", "*.yml"))
        {
            var bad = File.ReadLines(file)
                .Select((line, i) => (line, number: i + 1))
                .Where(x => Regex.IsMatch(x.line, @"^\s*(- )?(run|name|if): [^|>'""\s#{\[&*!%@`-][^#]*: "))
                .Select(x => $"{x.number}")
                .ToList();
            Expect($"yaml: {file} has no one-line value containing ': ' without quotation marks (lines {string.Join(',', bad)})", bad.Count, 0);
        }
    }

    // The Linux job builds as root in ubuntu:22.04 (packaging/linux/container-build.sh). Two things must hold even when
    // the run is cancelled or times out: the workspace goes back to the runner user (else the next checkout of any
    // workflow fails with "Permission denied"), and the tool's own self-test runs on the host, not in the container
    // (jammy has squashfs-tools 4.5; the reproducibility checks of the self-test are verified with 4.6 only).
    private static void LinuxContainerParts()
    {
        const string workflow = ".github/workflows/release.yml";
        const string container = "packaging/linux/container-build.sh";
        if (!File.Exists(workflow) || !File.Exists(container))
        {
            Expect("container: release.yml and container-build.sh present", false, true);
            return;
        }

        var yml = File.ReadAllText(workflow);
        Expect("container: the work folder is handed back even after an abort (if: always() with chown)",
            Regex.IsMatch(yml, @"name: Give the workspace back to the runner user \(Linux\)\s+if: always\(\)\s+run: \|[\s\S]*?chown -R"), true);
        Expect("container: self-test of the packaging tool runs in all three jobs on the host", Regex.Count(yml, "name: Self-test of the packaging tool"), 3);
        Expect("container: container-build.sh sets NUGET_PACKAGES (dotnet and licenses.cs look in the same place)",
            Regex.IsMatch(File.ReadAllText(container), @"(?m)^export .*\bNUGET_PACKAGES="), true);
        Expect("container: container-build.sh has no pipe into head (pipefail turns it into exit 141)",
            Regex.IsMatch(File.ReadAllText(container), @"\|\s*head\b"), false);
        Expect("container: container-build.sh does not start the self-test of the packaging tool",
            File.ReadAllText(container).Contains("package.cs -- selftest", StringComparison.Ordinal), false);
        // libSkiaSharp.so needs libfontconfig.so.1 (as ElfRules.AllowedNeeded says); tools/check-binary.cs has a shorter allow-list.
        Expect("container: the check-binary call allows libfontconfig.so.1 (libSkiaSharp.so needs it)",
            Regex.IsMatch(File.ReadAllText(container), @"check-binary\.cs -- .*--allow-needed libfontconfig\.so\.1"), true);
    }

    private static void PackagingCommands()
    {
        var dir = Path.Combine(Path.GetTempPath(), "raffica-package-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = Path.Combine(dir, "repo");
            var app = Path.Combine(dir, "app");
            var cli = Path.Combine(dir, "cli");
            Directory.CreateDirectory(Path.Combine(repo, "src", "Raffica.Pdf", "Resources"));
            File.WriteAllText(Path.Combine(repo, "LICENSE"), "license");
            File.WriteAllText(Path.Combine(repo, "src", "Raffica.Pdf", "Resources", "ThirdPartyNotices.txt"), "notices");
            foreach (var d in new[] { app, Path.Combine(app, "sub"), cli })
            {
                Directory.CreateDirectory(d);
            }

            File.WriteAllText(Path.Combine(app, "RafficaPdf"), "app");
            File.WriteAllText(Path.Combine(app, "RafficaPdf.pdb"), "symbols");
            File.WriteAllText(Path.Combine(app, "RafficaPdf.dbg"), "symbols");
            File.WriteAllText(Path.Combine(app, "libpdfium.so"), "pdfium");
            File.WriteAllText(Path.Combine(app, "sub", "x.txt"), "x");
            File.WriteAllText(Path.Combine(cli, "raffica"), "cli");
            File.WriteAllText(Path.Combine(cli, "libpdfium.so"), "pdfium");

            string[] LayoutArgs(string output) =>
                ["layout", "--rid", "linux-x64", "--app", app, "--cli", cli, "--out", output, "--repo", repo];

            var layout = Path.Combine(dir, "layout");
            Expect("layout: success", Cli.Run(LayoutArgs(layout)), 0);
            Expect("layout: the app is in the root", File.Exists(Path.Combine(layout, "RafficaPdf")), true);
            Expect("layout: subfolders are kept", File.Exists(Path.Combine(layout, "sub", "x.txt")), true);
            Expect("layout: .pdb is missing", File.Exists(Path.Combine(layout, "RafficaPdf.pdb")), false);
            Expect("layout: .dbg is missing", File.Exists(Path.Combine(layout, "RafficaPdf.dbg")), false);
            Expect("layout: cli/ with CLI and library", File.Exists(Path.Combine(layout, "cli", "raffica")) && File.Exists(Path.Combine(layout, "cli", "libpdfium.so")), true);
            Expect("layout: LICENSE", File.ReadAllText(Path.Combine(layout, "LICENSE")), "license");
            Expect("layout: THIRD-PARTY-NOTICES.txt", File.ReadAllText(Path.Combine(layout, "THIRD-PARTY-NOTICES.txt")), "notices");
            if (!OperatingSystem.IsWindows())
            {
                Expect("layout: program is executable", (File.GetUnixFileMode(Path.Combine(layout, "RafficaPdf")) & UnixFileMode.UserExecute) != 0, true);
                Expect("layout: library is not executable", (File.GetUnixFileMode(Path.Combine(layout, "libpdfium.so")) & UnixFileMode.UserExecute) != 0, false);
            }

            Expect("layout: non-empty output folder aborts", Cli.Run(LayoutArgs(layout)), 1);

            File.Delete(Path.Combine(cli, "libpdfium.so"));
            Expect("layout: without PDFium in the CLI aborts", Cli.Run(LayoutArgs(Path.Combine(dir, "layout2"))), 1);
            Expect("layout: nothing is created on abort", Directory.Exists(Path.Combine(dir, "layout2")), false);
            File.WriteAllText(Path.Combine(cli, "libpdfium.so"), "pdfium");
            File.Delete(Path.Combine(app, "libpdfium.so"));
            Expect("layout: without PDFium in the app aborts", Cli.Run(LayoutArgs(Path.Combine(dir, "layout3"))), 1);
            Expect("layout: win-x64 requires pdfium.dll", Cli.Run(["layout", "--rid", "win-x64", "--app", app, "--cli", cli, "--out", Path.Combine(dir, "layout4"), "--repo", repo]), 1);
            Expect("layout: unknown RID returns 2", Cli.Run(["layout", "--rid", "mac", "--app", app, "--cli", cli, "--out", dir]), 2);
            Expect("layout: missing option returns 2", Cli.Run(["layout", "--rid", "linux-x64"]), 2);

            // The OCR library set: the layout puts the library next to the app and the CLI and the language data once at the root.
            File.WriteAllText(Path.Combine(app, "libpdfium.so"), "pdfium");
            var nativeDir = Path.Combine(dir, "native");
            Directory.CreateDirectory(Path.Combine(nativeDir, "tessdata"));
            File.WriteAllText(Path.Combine(nativeDir, "libtesseract.so"), "tesseract");
            File.WriteAllText(Path.Combine(nativeDir, "tessdata", "eng.traineddata"), "eng");
            File.WriteAllText(Path.Combine(nativeDir, "tessdata", "deu.traineddata"), "deu");
            var layoutNative = Path.Combine(dir, "layout-native");
            Expect("layout: with --native", Cli.Run([.. LayoutArgs(layoutNative), "--native", nativeDir]), 0);
            Expect("layout: library next to the app", File.ReadAllText(Path.Combine(layoutNative, "libtesseract.so")), "tesseract");
            Expect("layout: library next to the CLI", File.ReadAllText(Path.Combine(layoutNative, "cli", "libtesseract.so")), "tesseract");
            Expect("layout: tessdata/ once at the root", File.Exists(Path.Combine(layoutNative, "tessdata", "eng.traineddata")) && File.Exists(Path.Combine(layoutNative, "tessdata", "deu.traineddata")), true);
            Expect("layout: no tessdata/ in cli/", Directory.Exists(Path.Combine(layoutNative, "cli", "tessdata")), false);
            File.Delete(Path.Combine(nativeDir, "tessdata", "deu.traineddata"));
            Expect("layout: --native without a language file aborts", Cli.Run([.. LayoutArgs(Path.Combine(dir, "layout-native2")), "--native", nativeDir]), 1);
            Expect("layout: nothing is created on abort (native)", Directory.Exists(Path.Combine(dir, "layout-native2")), false);

            var zip1 = Path.Combine(dir, "a.zip");
            var zip2 = Path.Combine(dir, "b.zip");
            Expect("zip: success", Cli.Run(["zip", "--layout", layout, "--version", "1.2.3", "--out", zip1]), 0);
            Thread.Sleep(1100);
            File.SetLastWriteTimeUtc(Path.Combine(layout, "LICENSE"), DateTime.UtcNow);
            Expect("zip: second run", Cli.Run(["zip", "--layout", layout, "--version", "1.2.3", "--out", zip2]), 0);
            Expect("zip: reproducible (same SHA-256)", Sums.Line(zip1)[..64], Sums.Line(zip2)[..64]);
            using (var archive = ZipFile.OpenRead(zip1))
            {
                var names = archive.Entries.Select(e => e.FullName).ToList();
                Expect("zip: top-level folder RafficaPdf-<version>/", names.All(n => n.StartsWith("RafficaPdf-1.2.3/", StringComparison.Ordinal)), true);
                Expect("zip: stable order", names.SequenceEqual(names.Order(StringComparer.Ordinal)), true);
                Expect("zip: fixed timestamps", archive.Entries.All(e => e.LastWriteTime.DateTime == new DateTime(2000, 1, 1)), true);
                Expect("zip: cli/ included", names.Contains("RafficaPdf-1.2.3/cli/raffica"), true);
                Expect("zip: program 0755", archive.GetEntry("RafficaPdf-1.2.3/RafficaPdf")!.ExternalAttributes >> 16 & 0xFFFF, OperatingSystem.IsWindows() ? 0x81A4 : 0x81ED);
                Expect("zip: file 0644", archive.GetEntry("RafficaPdf-1.2.3/LICENSE")!.ExternalAttributes >> 16 & 0xFFFF, 0x81A4);
            }

            Expect("zip: missing layout returns 1", Cli.Run(["zip", "--layout", Path.Combine(dir, "nope"), "--version", "1", "--out", zip1]), 1);
            Expect("zip: without a version returns 2", Cli.Run(["zip", "--layout", layout, "--out", zip1]), 2);

            var sums = Path.Combine(dir, "SHA256SUMS-test.txt");
            Expect("sums: success", Cli.Run(["sums", zip1, zip2, "--out", sums]), 0);
            var lines = File.ReadAllText(sums).Split('\n');
            var expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip1)));
            Expect("sums: format <sha256>  <file name>", lines[0], $"{expected}  a.zip");
            Expect("sums: one line per file, LF at the end", lines.Length, 3);
            Expect("sums: missing file returns 1", Cli.Run(["sums", Path.Combine(dir, "nope"), "--out", sums]), 1);
            Expect("sums: without a file returns 2", Cli.Run(["sums", "--out", sums]), 2);

            var sumsHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sums)));
            Expect("sums-verify: matching sum returns 0", Cli.Run(["sums-verify", "--file", sums, "--sha256", sumsHash]), 0);
            Expect("sums-verify: uppercase returns 0", Cli.Run(["sums-verify", "--file", sums, "--sha256", sumsHash.ToUpperInvariant()]), 0);
            var manipulated = (sumsHash[0] == '0' ? "1" : "0") + sumsHash[1..];
            Expect("sums-verify: tampered sum returns 1", Cli.Run(["sums-verify", "--file", sums, "--sha256", manipulated]), 1);
            Expect("sums-verify: too short sum returns 1", Cli.Run(["sums-verify", "--file", sums, "--sha256", sumsHash[..63]]), 1);
            Expect("sums-verify: too long sum returns 1", Cli.Run(["sums-verify", "--file", sums, "--sha256", sumsHash + "0"]), 1);
            Expect("sums-verify: non-hex sum returns 1", Cli.Run(["sums-verify", "--file", sums, "--sha256", "g" + sumsHash[1..]]), 1);
            Expect("sums-verify: empty sum returns 1", Cli.Run(["sums-verify", "--file", sums, "--sha256", string.Empty]), 1);
            Expect("sums-verify: spaces alone return 1", Cli.Run(["sums-verify", "--file", sums, "--sha256", "  "]), 1);
            Expect("sums-verify: missing file returns 1", Cli.Run(["sums-verify", "--file", Path.Combine(dir, "nope"), "--sha256", sumsHash]), 1);
            Expect("sums-verify: without --sha256 returns 2", Cli.Run(["sums-verify", "--file", sums]), 2);
            Expect("sums-verify: without --file returns 2", Cli.Run(["sums-verify", "--sha256", sumsHash]), 2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // Everything of the .deb that needs no dpkg-deb: version mapping, control, file list with modes, copyright.
    private static void DebParts()
    {
        Expect("deb: Version 0.1.0", Deb.DebVersion("0.1.0"), "0.1.0");
        Expect("deb: prerelease x.y.z-rc1 becomes x.y.z~rc1", Deb.DebVersion("1.2.3-rc1"), "1.2.3~rc1");
        Expect("deb: prerelease with a dot", Deb.DebVersion("1.0.0-beta.2"), "1.0.0~beta.2");
        Expect("deb: version without patch is invalid", Deb.DebVersion("1.2"), null);
        Expect("deb: version with a leading zero is invalid", Deb.DebVersion("01.2.3"), null);
        Expect("deb: version with a space is invalid", Deb.DebVersion("1.2.3 x"), null);
        Expect("deb: empty prerelease id is invalid", Deb.DebVersion("1.2.3-"), null);
        Expect("deb: file name", Deb.FileName("1.2.3~rc1"), "raffica-pdf_1.2.3~rc1_amd64.deb");

        var template = File.ReadAllText(Path.Combine("packaging", "linux", "control.template"));
        var control = Deb.Control(template, "0.1.0", 1234, "Jane Doe <jane@example.org>");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in control.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (line.Length > 0 && line[0] != ' ' && colon > 0)
            {
                fields[line[..colon]] = line[(colon + 1)..].Trim();
            }
        }

        Expect("deb: Package", fields.GetValueOrDefault("Package"), "raffica-pdf");
        Expect("deb: Version", fields.GetValueOrDefault("Version"), "0.1.0");
        Expect("deb: Architecture", fields.GetValueOrDefault("Architecture"), "amd64");
        Expect("deb: Maintainer", fields.GetValueOrDefault("Maintainer"), "Jane Doe <jane@example.org>");
        Expect("deb: Installed-Size", fields.GetValueOrDefault("Installed-Size"), "1234");
        Expect("deb: Section", fields.GetValueOrDefault("Section"), "graphics");
        Expect("deb: Priority", fields.GetValueOrDefault("Priority"), "optional");
        Expect("deb: Depends", fields.GetValueOrDefault("Depends"), "libc6 (>= 2.35), libfontconfig1, libx11-6, libice6, libsm6, libssl3 | libssl3t64");
        Expect("deb: Recommends", fields.GetValueOrDefault("Recommends"), "libcups2, xdg-utils");
        Expect("deb: Homepage", fields.GetValueOrDefault("Homepage"), "https://github.com/benjo-code/raffica");
        Expect("deb: Description (short text)", fields.GetValueOrDefault("Description")?.Length > 0, true);
        Expect("deb: no placeholder remains", new[] { "@VERSION@", "@MAINTAINER@", "@INSTALLED_SIZE@" }.Any(m => control.Contains(m, StringComparison.Ordinal)), false);
        Expect("deb: control ends with LF", control.EndsWith('\n'), true);
        Expect("deb: Installed-Size 0 KiB becomes 1", Deb.InstalledSizeKiB([0L]), 1L);
        Expect("deb: Installed-Size rounds up per file", Deb.InstalledSizeKiB([1L, 1024L, 1025L]), 1L + 1L + 2L);
        var lintianReport = "E: raffica-pdf: embedded-library freetype [usr/lib/x.so]\nE: raffica-pdf: no-changelog usr/share/doc/raffica-pdf/changelog.gz (native package)\nW: raffica-pdf: no-manual-page [usr/bin/raffica]\n";
        Expect("deb: lintian, known errors are accepted", DebSmoke.RejectedLintianErrors(lintianReport).Count, 0);
        Expect("deb: lintian, another error fails", DebSmoke.RejectedLintianErrors(lintianReport + "E: raffica-pdf: file-in-etc-not-marked-as-conffile etc/x\n").Count, 1);
        Expect("deb: lintian, a tag with the same beginning fails", DebSmoke.RejectedLintianErrors("E: raffica-pdf: no-changelog-extra x\n").Count, 1);
        Expect("deb: maintainer placeholder is pinned", Deb.Maintainer.Contains("noreply", StringComparison.Ordinal), true);

        var copyright = File.ReadAllText(Path.Combine("packaging", "linux", "copyright.template"));
        Expect("deb: copyright Format 1.0", copyright.StartsWith("Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/\n", StringComparison.Ordinal), true);
        Expect("deb: copyright names GPL-3+", copyright.Contains("License: GPL-3+", StringComparison.Ordinal), true);
        Expect("deb: copyright refers to THIRD-PARTY-NOTICES.txt", copyright.Contains("/usr/lib/raffica-pdf/THIRD-PARTY-NOTICES.txt", StringComparison.Ordinal), true);
        Expect("deb: copyright without tabs", copyright.Contains('\t', StringComparison.Ordinal), false);

        var dir = Path.Combine(Path.GetTempPath(), "raffica-deb-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = Path.Combine(dir, "layout");
            var repo = Path.Combine(dir, "repo");
            Directory.CreateDirectory(Path.Combine(layout, "cli"));
            Directory.CreateDirectory(Path.Combine(layout, "sub"));
            File.WriteAllText(Path.Combine(layout, "RafficaPdf"), "app");
            File.WriteAllText(Path.Combine(layout, "libpdfium.so"), "pdfium");
            File.WriteAllText(Path.Combine(layout, "LICENSE"), "license");
            File.WriteAllText(Path.Combine(layout, "THIRD-PARTY-NOTICES.txt"), "notices");
            File.WriteAllText(Path.Combine(layout, "sub", "x.dat"), "x");
            File.WriteAllText(Path.Combine(layout, "cli", "raffica"), "cli");
            File.WriteAllText(Path.Combine(layout, "cli", "libpdfium.so"), "pdfium");
            foreach (var size in new[] { "16x16", "512x512" })
            {
                var icons = Path.Combine(repo, "Docs", "design", "icons", "linux", "hicolor", size, "apps");
                Directory.CreateDirectory(icons);
                File.WriteAllText(Path.Combine(icons, "raffica-pdf.png"), "png" + size);
            }

            Directory.CreateDirectory(Path.Combine(repo, "packaging", "linux"));
            File.WriteAllText(Path.Combine(repo, "packaging", "linux", "raffica-pdf.desktop"), "[Desktop Entry]\n");
            File.WriteAllText(Path.Combine(repo, "packaging", "linux", "copyright.template"), "Format: x\n");

            var entries = Deb.Plan(layout, repo);
            DebEntry? Find(string path) => entries.FirstOrDefault(e => e.Path == path);
            Expect("deb: plan has no duplicate paths", entries.Select(e => e.Path).Distinct().Count(), entries.Count);
            Expect("deb: plan is sorted", entries.Select(e => e.Path).SequenceEqual(entries.Select(e => e.Path).Order(StringComparer.Ordinal)), true);
            Expect("deb: program 0755", Find("usr/lib/raffica-pdf/RafficaPdf")?.Mode, 0x1ED);
            Expect("deb: CLI 0755", Find("usr/lib/raffica-pdf/cli/raffica")?.Mode, 0x1ED);
            Expect("deb: library 0644", Find("usr/lib/raffica-pdf/libpdfium.so")?.Mode, 0x1A4);
            Expect("deb: CLI library 0644", Find("usr/lib/raffica-pdf/cli/libpdfium.so")?.Mode, 0x1A4);
            Expect("deb: subfolders are kept", Find("usr/lib/raffica-pdf/sub/x.dat")?.Mode, 0x1A4);
            Expect("deb: THIRD-PARTY-NOTICES.txt in the program folder", Find("usr/lib/raffica-pdf/THIRD-PARTY-NOTICES.txt") is not null, true);
            Expect("deb: directory 0755", Find("usr/lib/raffica-pdf/cli")?.Mode, 0x1ED);
            Expect("deb: directory usr/share/applications", Find("usr/share/applications")?.Kind, DebEntryKind.Directory);
            Expect("deb: launcher 0644", Find("usr/share/applications/raffica-pdf.desktop")?.Mode, 0x1A4);
            Expect("deb: icon 16x16", Find("usr/share/icons/hicolor/16x16/apps/raffica-pdf.png")?.Mode, 0x1A4);
            Expect("deb: icon 512x512", Find("usr/share/icons/hicolor/512x512/apps/raffica-pdf.png") is not null, true);
            Expect("deb: copyright 0644", Find("usr/share/doc/raffica-pdf/copyright")?.Mode, 0x1A4);
            var link = Find("usr/bin/raffica");
            Expect("deb: /usr/bin/raffica is a symlink", link?.Kind, DebEntryKind.Symlink);
            Expect("deb: symlink points relatively to the CLI", link?.LinkTarget, "../lib/raffica-pdf/cli/raffica");
            Expect("deb: symlink resolves to the CLI", link is null ? null : PosixPath.Resolve("/usr/bin", link.LinkTarget!), "/usr/lib/raffica-pdf/cli/raffica");
            var appLink = Find("usr/bin/raffica-pdf");
            Expect("deb: /usr/bin/raffica-pdf is a symlink", appLink?.Kind, DebEntryKind.Symlink);
            Expect("deb: raffica-pdf link points relatively to the program", appLink?.LinkTarget, "../lib/raffica-pdf/RafficaPdf");
            Expect("deb: raffica-pdf link resolves to the program", appLink is null ? null : PosixPath.Resolve("/usr/bin", appLink.LinkTarget!), "/usr/lib/raffica-pdf/RafficaPdf");
            Expect("PosixPath: relative with ..", PosixPath.Resolve("/usr/bin", "../lib/a/./b"), "/usr/lib/a/b");
            Expect("PosixPath: absolute target", PosixPath.Resolve("/usr/bin", "/etc/x"), "/etc/x");
            Expect("PosixPath: .. beyond the root", PosixPath.Resolve("/a", "../../b"), "/b");
            Expect("deb: no entry outside usr/", entries.All(e => e.Path.StartsWith("usr", StringComparison.Ordinal)), true);
            Expect("deb: all files have a source", entries.Where(e => e.Kind == DebEntryKind.File).All(e => e.Source is not null && File.Exists(e.Source)), true);
            Expect("deb: parent directories are present", entries.Where(e => e.Path.Contains('/', StringComparison.Ordinal))
                .All(e => Find(e.Path[..e.Path.LastIndexOf('/')]) is { Kind: DebEntryKind.Directory }), true);

            if (!OperatingSystem.IsWindows())
            {
                var stage = Path.Combine(dir, "stage");
                Deb.Stage(entries, stage, control);
                Expect("deb: Staging control", File.ReadAllText(Path.Combine(stage, "DEBIAN", "control")), control);
                Expect("deb: staging program 0755", File.GetUnixFileMode(Path.Combine(stage, "usr/lib/raffica-pdf/RafficaPdf")), (UnixFileMode)0x1ED);
                Expect("deb: staging file 0644", File.GetUnixFileMode(Path.Combine(stage, "usr/lib/raffica-pdf/libpdfium.so")), (UnixFileMode)0x1A4);
                Expect("deb: staging directory 0755", File.GetUnixFileMode(Path.Combine(stage, "usr/lib/raffica-pdf/cli")), (UnixFileMode)0x1ED);
                Expect("deb: Staging DEBIAN 0755", File.GetUnixFileMode(Path.Combine(stage, "DEBIAN")), (UnixFileMode)0x1ED);
                Expect("deb: Staging control 0644", File.GetUnixFileMode(Path.Combine(stage, "DEBIAN", "control")), (UnixFileMode)0x1A4);
                Expect("deb: Staging raffica-pdf Symlink", new FileInfo(Path.Combine(stage, "usr/bin/raffica-pdf")).LinkTarget, "../lib/raffica-pdf/RafficaPdf");
                Expect("deb: Staging Symlink", new FileInfo(Path.Combine(stage, "usr/bin/raffica")).LinkTarget, "../lib/raffica-pdf/cli/raffica");
                Expect("deb: staging fixed time", File.GetLastWriteTimeUtc(Path.Combine(stage, "usr/lib/raffica-pdf/RafficaPdf")), Deb.FixedTime.UtcDateTime);
                Expect("deb: staging fixed time of the directories", Directory.GetLastWriteTimeUtc(Path.Combine(stage, "usr/lib/raffica-pdf")), Deb.FixedTime.UtcDateTime);
            }

            Expect("deb: wrong file name returns 2", Cli.Run(["deb", "--layout", layout, "--version", "1.2.3", "--out", Path.Combine(dir, "x.deb"), "--repo", repo]), 2);
            Expect("deb: invalid version returns 2", Cli.Run(["deb", "--layout", layout, "--version", "1.2", "--out", Path.Combine(dir, "raffica-pdf_1.2_amd64.deb"), "--repo", repo]), 2);
            Expect("deb: missing layout returns 1", Cli.Run(["deb", "--layout", Path.Combine(dir, "nope"), "--version", "1.2.3", "--out", Path.Combine(dir, "raffica-pdf_1.2.3_amd64.deb"), "--repo", repo]), 1);
            Expect("deb: layout without cli/raffica returns 1", Cli.Run(["deb", "--layout", Path.Combine(repo, "packaging"), "--version", "1.2.3", "--out", Path.Combine(dir, "raffica-pdf_1.2.3_amd64.deb"), "--repo", repo]), 1);
            Expect("deb: without an option returns 2", Cli.Run(["deb"]), 2);
            if (!OperatingSystem.IsWindows() && Exec("dpkg-deb", "--version") == 0)
            {
                File.WriteAllText(Path.Combine(repo, "packaging", "linux", "control.template"), template);
                var deb1 = Path.Combine(dir, "a", "raffica-pdf_1.2.3~rc1_amd64.deb");
                var deb2 = Path.Combine(dir, "b", "raffica-pdf_1.2.3~rc1_amd64.deb");
                Expect("deb: dpkg-deb builds", Cli.Run(["deb", "--layout", layout, "--version", "1.2.3-rc1", "--out", deb1, "--repo", repo]), 0);
                Thread.Sleep(1100);
                File.SetLastWriteTimeUtc(Path.Combine(layout, "RafficaPdf"), DateTime.UtcNow);
                Expect("deb: second run", Cli.Run(["deb", "--layout", layout, "--version", "1.2.3-rc1", "--out", deb2, "--repo", repo]), 0);
                Expect("deb: reproducible (same SHA-256)", Sums.Line(deb1)[..64], Sums.Line(deb2)[..64]);
                Expect("deb: dpkg-deb -f Version", Deb.Field(deb1, "Version"), "1.2.3~rc1");
                Expect("deb: dpkg-deb -f Maintainer", Deb.Field(deb1, "Maintainer"), Deb.Maintainer);
                Expect("deb: smoke test without a real CLI fails (fake programs)", Cli.Run(["deb-smoke", deb1, "--repo", repo]), 1);
            }

            Expect("deb-smoke: without a file returns 2", Cli.Run(["deb-smoke"]), 2);
            Expect("deb-smoke: missing file returns 1", Cli.Run(["deb-smoke", Path.Combine(dir, "nope.deb")]), 1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // The AppImage: runtime pin, AppRun, .desktop, the AppDir plan and, with mksquashfs, a reproducible image from a fake runtime.
    private static void AppImageParts()
    {
        const string noFuse = "Error: No suitable fusermount binary found on the $PATH\nfuse: device not found";
        Expect("smoke: with FUSE and a start is ok", AppImageSmoke.SecondRunIsAcceptable(true, 0, "first-page\n"), true);
        Expect("smoke: with FUSE and a failed start fails", AppImageSmoke.SecondRunIsAcceptable(true, 127, noFuse), false);
        Expect("smoke: without FUSE the runtime says so clearly and does not start (ok)", AppImageSmoke.SecondRunIsAcceptable(false, 127, noFuse), true);
        Expect("smoke: without FUSE but another crash fails", AppImageSmoke.SecondRunIsAcceptable(false, 139, "Segmentation fault"), false);
        Expect("smoke: without FUSE and started anyway is ok", AppImageSmoke.SecondRunIsAcceptable(false, 0, "first-page\n"), true);
        Expect("smoke: a start without first-page fails", AppImageSmoke.SecondRunIsAcceptable(true, 0, "nothing\n"), false);
        Expect("smoke: the start passes --settings-file (a measurement run needs it)",
            AppImageSmoke.StartArguments(true, "a.AppImage", "t.pdf", "s.json").SequenceEqual(["--measure-startup", "--settings-file", "s.json", "t.pdf"]), true);
        Expect("smoke: the xvfb start passes --settings-file as well",
            AppImageSmoke.StartArguments(false, "a.AppImage", "t.pdf", "s.json").SequenceEqual(["-a", "a.AppImage", "--measure-startup", "--settings-file", "s.json", "t.pdf"]), true);

        Expect("appimage: runtime checksum is pinned", Pinned.PinProblem(AppImageRuntime.Sha256, "AppImageRuntime.Sha256"), null);
        Expect("appimage: runtime address names the version", AppImageRuntime.Url,
            $"https://github.com/AppImage/type2-runtime/releases/download/{AppImageRuntime.Version}/runtime-x86_64");
        Expect("appimage: runtime size is pinned", AppImageRuntime.Size > 0, true);
        Expect("appimage: runtime location in the cache", AppImageRuntime.DefaultPath("/home/x/.cache"),
            Path.Combine("/home/x/.cache", "RafficaBuildTools", $"type2-runtime-{AppImageRuntime.Version}-x86_64"));
        Expect("appimage: file name", AppImage.FileName("1.2.3"), "RafficaPdf-1.2.3-x86_64.AppImage");
        Expect("appimage: file name of a prerelease", AppImage.FileName("1.2.3-rc1"), "RafficaPdf-1.2.3-rc1-x86_64.AppImage");

        // AppRun: POSIX sh, starts the app inside the AppDir.
        var appRun = Path.Combine("packaging", "linux", "AppRun");
        Expect("appimage: packaging/linux/AppRun present", File.Exists(appRun), true);
        var appRunText = File.Exists(appRun) ? File.ReadAllText(appRun) : string.Empty;
        Expect("appimage: AppRun starts with #!/bin/sh", appRunText.StartsWith("#!/bin/sh\n", StringComparison.Ordinal), true);
        Expect("appimage: AppRun without CR", appRunText.Contains('\r', StringComparison.Ordinal), false);
        Expect("appimage: AppRun startet usr/lib/raffica-pdf/RafficaPdf", AppImage.AppRunTarget(appRunText), "usr/lib/raffica-pdf/RafficaPdf");
        // The detector in Core must agree with the layout: DebDirectory is the install folder, the AppRun target the AppImage path.
        const string detectorFile = "src/Raffica.Core/Updates/PackageForm.cs";
        var detector = File.Exists(detectorFile) ? File.ReadAllText(detectorFile) : string.Empty;
        Expect("layout: PackageFormDetector.DebDirectory is /usr/lib/raffica-pdf/", detector.Contains("DebDirectory = \"/usr/lib/raffica-pdf/\"", StringComparison.Ordinal), true);
        Expect("layout: AppRun target lies below the detector's folder", "/" + AppImage.AppRunTarget(appRunText), "/usr/lib/raffica-pdf/RafficaPdf");
        var desktopLines = File.ReadAllLines(Path.Combine("packaging", "linux", "raffica-pdf.desktop"));
        Expect("desktop: MimeType is application/pdf;", desktopLines.Contains("MimeType=application/pdf;"), true);
        Expect("desktop: Icon is raffica-pdf", desktopLines.Contains("Icon=raffica-pdf"), true);
        Expect("desktop: StartupWMClass is RafficaPdf", desktopLines.Contains("StartupWMClass=RafficaPdf"), true);
        Expect("appimage: target without an exec line is null", AppImage.AppRunTarget("#!/bin/sh\necho x\n"), null);

        // .desktop inside the image: only Exec differs from the template.
        var template = File.ReadAllText(Path.Combine("packaging", "linux", "raffica-pdf.desktop"));
        var desktop = AppImage.Desktop(template);
        Expect("appimage: .desktop Exec=RafficaPdf %F", desktop.Split('\n').Count(l => l == "Exec=RafficaPdf %F"), 1);
        Expect("appimage: .desktop otherwise the same", string.Join('\n', desktop.Split('\n').Where(l => !l.StartsWith("Exec=", StringComparison.Ordinal))),
            string.Join('\n', template.Split('\n').Where(l => !l.StartsWith("Exec=", StringComparison.Ordinal))));
        Expect("appimage: .desktop without Exec throws", ThrowsIo(() => AppImage.Desktop("[Desktop Entry]\nName=x\n")), true);
        Expect("appimage: .desktop with two Exec lines throws", ThrowsIo(() => AppImage.Desktop("Exec=a\nExec=b\n")), true);

        var dir = Path.Combine(Path.GetTempPath(), "raffica-appimage-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = Path.Combine(dir, "layout");
            var repo = Path.Combine(dir, "repo");
            Directory.CreateDirectory(Path.Combine(layout, "cli"));
            File.WriteAllText(Path.Combine(layout, "RafficaPdf"), "app");
            File.WriteAllText(Path.Combine(layout, "libpdfium.so"), "pdfium");
            File.WriteAllText(Path.Combine(layout, "LICENSE"), "license");
            File.WriteAllText(Path.Combine(layout, "THIRD-PARTY-NOTICES.txt"), "notices");
            File.WriteAllText(Path.Combine(layout, "cli", "raffica"), "cli");
            File.WriteAllText(Path.Combine(layout, "cli", "libpdfium.so"), "pdfium");
            foreach (var size in new[] { "16x16", "256x256" })
            {
                var icons = Path.Combine(repo, "Docs", "design", "icons", "linux", "hicolor", size, "apps");
                Directory.CreateDirectory(icons);
                File.WriteAllText(Path.Combine(icons, "raffica-pdf.png"), "png" + size);
            }

            Directory.CreateDirectory(Path.Combine(repo, "packaging", "linux"));
            File.WriteAllText(Path.Combine(repo, "packaging", "linux", "raffica-pdf.desktop"), template);
            File.WriteAllText(Path.Combine(repo, "packaging", "linux", "AppRun"), appRunText);

            var entries = AppImage.Plan(layout, repo);
            DebEntry? Find(string path) => entries.FirstOrDefault(e => e.Path == path);
            Expect("appimage: plan without duplicate paths", entries.Select(e => e.Path).Distinct().Count(), entries.Count);
            Expect("appimage: plan is sorted", entries.Select(e => e.Path).SequenceEqual(entries.Select(e => e.Path).Order(StringComparer.Ordinal)), true);
            Expect("appimage: AppRun 0755", Find("AppRun")?.Mode, 0x1ED);
            Expect("appimage: AppRun target is an executable program in the plan", Find(AppImage.AppRunTarget(appRunText) ?? "?") is { Kind: DebEntryKind.File, Mode: 0x1ED }, true);
            Expect("appimage: raffica-pdf.desktop in the root", Find("raffica-pdf.desktop")?.Content, desktop);
            Expect("appimage: raffica-pdf.png is the 256 icon", Find("raffica-pdf.png")?.Source, Path.Combine(repo, "Docs", "design", "icons", "linux", "hicolor", "256x256", "apps", "raffica-pdf.png"));
            Expect("appimage: .DirIcon points to raffica-pdf.png", Find(".DirIcon")?.LinkTarget, "raffica-pdf.png");
            Expect("appimage: program 0755", Find("usr/lib/raffica-pdf/RafficaPdf")?.Mode, 0x1ED);
            Expect("appimage: CLI 0755", Find("usr/lib/raffica-pdf/cli/raffica")?.Mode, 0x1ED);
            Expect("appimage: library 0644", Find("usr/lib/raffica-pdf/libpdfium.so")?.Mode, 0x1A4);
            Expect("appimage: LICENSE in the program folder", Find("usr/lib/raffica-pdf/LICENSE")?.Mode, 0x1A4);
            Expect("appimage: THIRD-PARTY-NOTICES.txt in the program folder", Find("usr/lib/raffica-pdf/THIRD-PARTY-NOTICES.txt") is not null, true);
            Expect("appimage: icon 16x16 under usr/share/icons", Find("usr/share/icons/hicolor/16x16/apps/raffica-pdf.png")?.Mode, 0x1A4);
            Expect("appimage: parent directories are present", entries.Where(e => e.Path.Contains('/', StringComparison.Ordinal))
                .All(e => Find(e.Path[..e.Path.LastIndexOf('/')]) is { Kind: DebEntryKind.Directory, Mode: 0x1ED }), true);
            File.Delete(Path.Combine(repo, "Docs", "design", "icons", "linux", "hicolor", "256x256", "apps", "raffica-pdf.png"));
            Expect("appimage: without the 256 icon throws", ThrowsIo(() => AppImage.Plan(layout, repo)), true);
            File.WriteAllText(Path.Combine(repo, "Docs", "design", "icons", "linux", "hicolor", "256x256", "apps", "raffica-pdf.png"), "png256x256");

            if (!OperatingSystem.IsWindows() && Exec("sh", "-c true") == 0)
            {
                // AppRun keeps every argument as it is, also with spaces in APPDIR and in the arguments.
                var appDir = Path.Combine(dir, "App Dir");
                var program = Path.Combine(appDir, (AppImage.AppRunTarget(appRunText) ?? "usr/lib/raffica-pdf/RafficaPdf").Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(program)!);
                File.WriteAllText(program, "#!/bin/sh\nfor a in \"$@\"; do printf '<%s>' \"$a\"; done\n");
                File.SetUnixFileMode(program, (UnixFileMode)0x1ED);
                Expect("appimage: sh -n AppRun", Tool.Run("sh", ["-n", appRun], out _), 0);
                var code = Tool.Run("sh", [appRun, "a b", "", "c*"], out var echoed, env: new Dictionary<string, string> { ["APPDIR"] = appDir });
                Expect("appimage: AppRun passes the arguments on unchanged", (code, echoed), (0, "<a b><><c*>"));
            }

            Expect("appimage: wrong file name returns 2", Cli.Run(["appimage", "--layout", layout, "--version", "1.2.3", "--runtime", appRun, "--out", Path.Combine(dir, "x.AppImage"), "--repo", repo]), 2);
            Expect("appimage: invalid version returns 2", Cli.Run(["appimage", "--layout", layout, "--version", "1.2", "--runtime", appRun, "--out", Path.Combine(dir, "RafficaPdf-1.2-x86_64.AppImage"), "--repo", repo]), 2);
            Expect("appimage: without an option returns 2", Cli.Run(["appimage"]), 2);
            var refused = Path.Combine(dir, "out", AppImage.FileName("1.2.3"));
            Expect("appimage: runtime with a wrong SHA-256 returns 1", Cli.Run(["appimage", "--layout", layout, "--version", "1.2.3", "--runtime", appRun, "--out", refused, "--repo", repo]), 1);
            Expect("appimage: nothing is created with a wrong runtime", File.Exists(refused), false);
            Expect("appimage: missing runtime returns 1", Cli.Run(["appimage", "--layout", layout, "--version", "1.2.3", "--runtime", Path.Combine(dir, "nope"), "--out", refused, "--repo", repo]), 1);
            Expect("runtime-fetch: unknown option returns 2", Cli.Run(["runtime-fetch", "--does-not-exist", "x"]), 2);
            var wrong = Path.Combine(dir, "wrong-runtime");
            File.WriteAllText(wrong, "not the runtime");
            Expect("runtime-fetch: existing wrong file is refused, nothing downloaded", Cli.Run(["runtime-fetch", "--out", wrong]), 1);
            Expect("runtime-fetch: wrong file is left untouched", File.ReadAllText(wrong), "not the runtime");
            Expect("appimage-smoke: without a file returns 2", Cli.Run(["appimage-smoke"]), 2);
            Expect("appimage-smoke: missing file returns 1", Cli.Run(["appimage-smoke", Path.Combine(dir, "nope.AppImage")]), 1);

            if (OperatingSystem.IsLinux() && Exec("mksquashfs", "-version") == 0)
            {
                // A fake runtime with its own pin: the image is built and reproducible; the real runtime is checked by appimage-smoke.
                var fake = Encoding.ASCII.GetBytes("FAKE-RUNTIME\n");
                var fakeRuntime = Path.Combine(dir, "fake-runtime");
                File.WriteAllBytes(fakeRuntime, fake);
                var pin = (Size: (long)fake.Length, Sha256: Convert.ToHexStringLower(SHA256.HashData(fake)));
                var image1 = Path.Combine(dir, "a", AppImage.FileName("1.2.3"));
                var image2 = Path.Combine(dir, "b", AppImage.FileName("1.2.3"));
                Expect("appimage: builds with mksquashfs", AppImage.Build(layout, repo, fakeRuntime, pin, image1), 0);
                Thread.Sleep(1100);
                File.SetLastWriteTimeUtc(Path.Combine(layout, "RafficaPdf"), DateTime.UtcNow);
                Expect("appimage: second run", AppImage.Build(layout, repo, fakeRuntime, pin, image2), 0);
                Expect("appimage: reproducible (same SHA-256)", Sums.Line(image1)[..64], Sums.Line(image2)[..64]);
                Expect("appimage: starts with the runtime", File.ReadAllBytes(image1).AsSpan(0, fake.Length).SequenceEqual(fake), true);
                Expect("appimage: SquashFS directly behind the runtime (hsqs)", Encoding.ASCII.GetString(File.ReadAllBytes(image1), fake.Length, 4), "hsqs");
                Expect("appimage: executable 0755", File.GetUnixFileMode(image1), (UnixFileMode)0x1ED);
                if (Tool.Run("unsquashfs", ["-o", fake.Length.ToString(CultureInfo.InvariantCulture), "-lln", image1], out var listing) == 0)
                {
                    var rows = listing.Split('\n').Where(l => l.Contains("squashfs-root", StringComparison.Ordinal)).ToList();
                    Expect("appimage: all entries belong to root (0/0)", rows.Count > 0 && rows.All(r => r.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] == "0/0"), true);
                    Expect("appimage: fixed time 2000-01-01", rows.All(r => r.Contains(" 2000-01-01 ", StringComparison.Ordinal)), true);
                    Expect("appimage: AppRun in the image is executable", rows.Any(r => r.StartsWith("-rwxr-xr-x", StringComparison.Ordinal) && r.EndsWith("squashfs-root/AppRun", StringComparison.Ordinal)), true);
                    Expect("appimage: .DirIcon is a symlink", rows.Any(r => r.StartsWith('l') && r.EndsWith("squashfs-root/.DirIcon -> raffica-pdf.png", StringComparison.Ordinal)), true);
                }
                else
                {
                    Console.WriteLine("selftest: unsquashfs is missing or does not know -o, content check of the image skipped.");
                }

                Expect("appimage-smoke: image with a wrong runtime fails", Cli.Run(["appimage-smoke", image1]), 1);
            }
            else
            {
                Console.WriteLine("selftest: mksquashfs is missing, build of the AppImage skipped (sudo apt-get install -y squashfs-tools).");
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static bool ThrowsIo(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    // Everything of the Windows installer that can be checked without Windows: pinning, quoting, names, the script.
    private static void InstallerParts()
    {
        // Pinned download of Inno Setup.
        Expect("inno: checksum is pinned", InnoSetup.PinProblem(InnoSetup.Sha256), null);
        Expect("inno: empty checksum counts as not pinned", InnoSetup.PinProblem("") is not null, true);
        Expect("inno: uppercase checksum is not accepted", InnoSetup.PinProblem(InnoSetup.Sha256.ToUpperInvariant()) is not null, true);
        Expect("inno: checksum with 63 characters is not accepted", InnoSetup.PinProblem(InnoSetup.Sha256[..63]) is not null, true);
        Expect("inno: version is 6.x", InnoSetup.Version.StartsWith("6.", StringComparison.Ordinal), true);
        Expect("inno: address names the version", InnoSetup.Url.EndsWith($"/innosetup-{InnoSetup.Version}.exe", StringComparison.Ordinal), true);
        Expect("inno: address is HTTPS", InnoSetup.Url.StartsWith("https://github.com/jrsoftware/issrc/releases/download/", StringComparison.Ordinal), true);
        var payload = Encoding.ASCII.GetBytes("not the installer");
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(payload));
        Expect("inno: matching file passes", InnoSetup.VerifyDownload(new MemoryStream(payload), payload.Length, payloadHash), null);
        Expect("inno: another checksum fails", InnoSetup.VerifyDownload(new MemoryStream(payload), payload.Length, InnoSetup.Sha256) is not null, true);
        Expect("inno: another size fails", InnoSetup.VerifyDownload(new MemoryStream(payload), payload.Length + 1, payloadHash) is not null, true);
        Expect("inno: unpinned checksum fails", InnoSetup.VerifyDownload(new MemoryStream(payload), payload.Length, "") is not null, true);
        Expect("inno: target folder in the user profile", InnoSetup.DefaultDir(@"C:\Users\ci\AppData\Local"),
            Path.Combine(@"C:\Users\ci\AppData\Local", "RafficaBuildTools", $"InnoSetup-{InnoSetup.Version}"));
        var innoArgs = InnoSetup.InstallArguments(@"C:\Users\ci\AppData\Local\RafficaBuildTools\InnoSetup-6");
        Expect("inno: silent, per-user only, portable",
            InnoArguments.Split(innoArgs).SequenceEqual(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/CURRENTUSER", "/PORTABLE=1", "/NOICONS", @"/DIR=C:\Users\ci\AppData\Local\RafficaBuildTools\InnoSetup-6"]), true);

        // Command lines for Inno Setup programs (no backslash escapes: a quote toggles quoting, see GetParamStr in issrc).
        string[] tricky = [@"/DIR=C:\Users\a b\Raffica PDF Test ä&", @"C:\dir\", "", "/DAppVersion=1.2.3-beta.1", "plain", "tab\there"];
        Expect("inno-args: round trip", InnoArguments.Split(InnoArguments.Join(tricky)).SequenceEqual(tricky), true);
        Expect("inno-args: spaces are quoted", InnoArguments.Join([@"/DIR=C:\a b"]), "\"/DIR=C:\\a b\"");
        Expect("inno-args: a quotation mark is rejected", Throws(() => InnoArguments.Join(["a\"b"])), true);
        Expect("inno-args: parser like Inno (quote in the middle of a word)", InnoArguments.Split("/DIR=\"C:\\a b\" x").SequenceEqual([@"/DIR=C:\a b", "x"]), true);

        // Names and versions of the installer.
        Expect("installer: file name", Installer.SetupFileName("1.2.3"), "RafficaPdf-1.2.3-win-x64-setup.exe");
        Expect("installer: Version 1.2.3 -> 1.2.3.0", Installer.NumericVersion("1.2.3"), "1.2.3.0");
        Expect("installer: prerelease -> numeric part", Installer.NumericVersion("0.1.0-beta.2"), "0.1.0.0");
        Expect("installer: 1.2 is not a version", Installer.NumericVersion("1.2"), null);
        Expect("installer: v1.2.3 is not a version", Installer.NumericVersion("v1.2.3"), null);
        Expect("installer: 70000 does not fit into the file version", Installer.NumericVersion("1.70000.0"), null);
        Expect("installer: space in the version", Installer.NumericVersion("1.2.3 x"), null);
        var iscc = Installer.IsccArguments(@"C:\r\packaging\windows\raffica-pdf.iss", @"C:\l a", @"C:\r", "1.2.3", @"C:\o u\t");
        Expect("installer: iscc arguments", string.Join("|", iscc),
            @"/DAppVersion=1.2.3|/DNumericVersion=1.2.3.0|/DLayoutDir=C:\l a|/DIconFile=" + Path.Combine(@"C:\r", "src", "Raffica.Pdf", "Assets", "raffica-pdf.ico") +
            @"|/OC:\o u\t|/FRafficaPdf-1.2.3-win-x64-setup|C:\r\packaging\windows\raffica-pdf.iss");
        Expect("installer: warnings of iscc are recognized",
            Installer.Warnings("Compiling\r\nWarning: Constant \"x\" is deprecated.\r\n  Warning: second\r\nSuccessful compile").Count, 2);
        Expect("installer: empty without a warning", Installer.Warnings("Successful compile (1.0 sec).\nNo warnings here: none").Count, 0);
        Expect("installer: uninstall key", Installer.UninstallKey,
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + Installer.AppId + "}_is1");
        Expect("installer: AppId is a GUID in uppercase", Guid.TryParseExact(Installer.AppId, "D", out _) && !Installer.AppId.Any(char.IsLower), true);

        // The silent command of the smoke test is exactly the documented one.
        var smokeArgs = InnoArguments.Split(InstallerSmoke.SilentInstallArguments(@"C:\T\Raffica PDF Test ä&", @"C:\T\install-1.log"));
        Expect("smoke: silent installation",
            smokeArgs.SequenceEqual(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", @"/DIR=C:\T\Raffica PDF Test ä&", @"/LOG=C:\T\install-1.log"]), true);
        Expect("smoke: silent uninstallation", InnoArguments.Split(InstallerSmoke.SilentUninstallArguments(@"C:\T\u.log"))
            .SequenceEqual(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", @"/LOG=C:\T\u.log"]), true);
        Expect("smoke: test folder with an umlaut and &", Path.GetFileName(InstallerSmoke.TestDir(Path.GetTempPath())), "Raffica PDF Test ä&");
        Expect("smoke: comparing equal snapshots", InstallerSmoke.Differences(["a 1 2"], ["a 1 2"]).Count, 0);
        Expect("smoke: a changed file is noticed", InstallerSmoke.Differences(["a 1 2"], ["a 1 3"]).Count, 1);
        Expect("smoke: a deleted file is noticed", InstallerSmoke.Differences(["a 1 2", "b 1 2"], ["a 1 2"]).Count, 1);

        // The script itself (packaging/windows/raffica-pdf.iss, read from the repository root).
        const string script = "packaging/windows/raffica-pdf.iss";
        Expect($"iss: {script} present", File.Exists(script), true);
        if (!File.Exists(script))
        {
            return;
        }

        var text = File.ReadAllText(script);
        var problems = IssRules.Problems(text);
        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"selftest: iss: {problem}");
        }

        Expect("iss: all rules met", problems.Count, 0);
        Expect("iss: ASCII only (texts come from the .isl files)", text.All(c => c < 128), true);

        // Each rule must catch its own violation (mutations of the real script).
        string Mutate(string from, string to)
        {
            Expect($"iss mutation finds '{from}'", text.Contains(from, StringComparison.Ordinal), true);
            return text.Replace(from, to, StringComparison.Ordinal);
        }

        Expect("iss: PrivilegesRequired=admin is noticed", IssRules.Problems(Mutate("PrivilegesRequired=lowest", "PrivilegesRequired=admin")).Count > 0, true);
        Expect("iss: missing dialog is noticed", IssRules.Problems(Mutate("PrivilegesRequiredOverridesAllowed=commandline dialog", "PrivilegesRequiredOverridesAllowed=commandline")).Count > 0, true);
        Expect("iss: AppId differs from the one of the predecessor app (never reuse it)", Installer.AppId != "472398B4-29BA-4DDB-8475-7928DA355D74", true);
        Expect("iss: another AppId is noticed", IssRules.Problems(Mutate(Installer.AppId, Guid.Empty.ToString("D"))).Count > 0, true);
        Expect("iss: CloseApplications=no is noticed", IssRules.Problems(Mutate("CloseApplications=yes", "CloseApplications=no")).Count > 0, true);
        Expect("iss: without ChangesAssociations is noticed", IssRules.Problems(Mutate("ChangesAssociations=yes", "")).Count > 0, true);
        Expect("iss: without German is noticed", IssRules.Problems(Mutate(@"compiler:Languages\German.isl", @"compiler:Languages\French.isl")).Count > 0, true);
        Expect("iss: launch in silent mode too is noticed", IssRules.Problems(Mutate("postinstall nowait skipifsilent", "postinstall nowait")).Count > 0, true);
        Expect("iss: visible registration is noticed", IssRules.Problems(Mutate("Parameters: \"--register-file-types={code:FileTypeScope}\"; Flags: runhidden", "Parameters: \"--register-file-types={code:FileTypeScope}\"; Flags: waituntilterminated")).Count > 0, true);
        Expect("iss: uninstallation without RunOnceId is noticed", IssRules.Problems(Mutate("; RunOnceId: \"UnregisterFileTypes\"", "")).Count > 0, true);
        Expect("iss: touching user data is noticed", IssRules.Problems(text + "\n[UninstallDelete]\nType: filesandordirs; Name: \"{userappdata}\\Raffica\"\n").Count > 0, true);
        Expect("iss: scope without IsAdminInstallMode is noticed", IssRules.Problems(Mutate("if IsAdminInstallMode then", "if True then")).Count > 0, true);
        Expect("iss: UsePreviousPrivileges=no is noticed", IssRules.Problems(Mutate("SolidCompression=yes", "SolidCompression=yes\nUsePreviousPrivileges=no")).Count > 0, true);
        Expect("iss: UsePreviousAppDir=no is noticed", IssRules.Problems(Mutate("SolidCompression=yes", "SolidCompression=yes\nUsePreviousAppDir=no")).Count > 0, true);
        Expect("iss: an explicit UsePreviousPrivileges=yes passes", IssRules.Problems(Mutate("SolidCompression=yes", "SolidCompression=yes\nUsePreviousPrivileges=yes\nUsePreviousAppDir=yes")).Count, 0);

        // update-smoke: what it lays out is what the app writes and the helper reads (no installation here).
        // A path with the separators of the running OS: update-smoke gets real paths, and Path.GetFileName splits only on '/' outside Windows.
        Expect("update-smoke: version from the setup name", UpdateSmoke.VersionOf(Path.Combine(Path.GetTempPath(), "o u", "RafficaPdf-0.0.2-win-x64-setup.exe")), "0.0.2");
        Expect("update-smoke: another name has no version", UpdateSmoke.VersionOf("setup.exe"), null);
        Expect("update-smoke: a prerelease is not installed", UpdateSmoke.VersionOf("RafficaPdf-0.0.2-rc.1-win-x64-setup.exe"), null);
        var smokeManifest = UpdateSmoke.Manifest("0.0.2", "RafficaPdf-0.0.2-win-x64-setup.exe", 1234, new string('a', 64));
        using (var parsed = System.Text.Json.JsonDocument.Parse(smokeManifest))
        {
            var file = parsed.RootElement.GetProperty("files")[0];
            Expect("update-smoke: Manifest Format 1", parsed.RootElement.GetProperty("format").GetInt32(), 1);
            Expect("update-smoke: Manifest Version", parsed.RootElement.GetProperty("version").GetString(), "0.0.2");
            Expect("update-smoke: manifest kind of the Windows setup", file.GetProperty("kind").GetString(), "win-x64-setup");
            Expect("update-smoke: manifest size", file.GetProperty("size").GetInt64(), 1234L);
        }

        // A throwaway key in memory: the signature has the form the app verifies (key id, 64 bytes P1363, over the raw bytes).
        using (var throwaway = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var signature = UpdateSmoke.Signature(smokeManifest, throwaway, "test");
            var raw = Convert.FromBase64String(signature["test:".Length..]);
            Expect("update-smoke: signature names the id", signature.StartsWith("test:", StringComparison.Ordinal), true);
            Expect("update-smoke: signature has 64 bytes", raw.Length, 64);
            Expect("update-smoke: signature verifies over the raw bytes", throwaway.VerifyData(smokeManifest, raw, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), true);

            // --key takes the private file of update-keygen (PKCS#8 as Base64, with a line break) and PEM.
            var spki = throwaway.ExportSubjectPublicKeyInfo();
            using (var fromKeygen = UpdateSmoke.ReadKey(Convert.ToBase64String(throwaway.ExportPkcs8PrivateKey()) + "\n"))
            {
                Expect("update-smoke: reads the key from update-keygen", fromKeygen?.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(spki), true);
            }

            using (var fromPem = UpdateSmoke.ReadKey(throwaway.ExportPkcs8PrivateKeyPem()))
            {
                Expect("update-smoke: reads the key as PEM", fromPem?.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(spki), true);
            }
        }

        Expect("update-smoke: no key from nonsense", UpdateSmoke.ReadKey("no key") is null, true);
        Expect("update-smoke: no key from a public key", UpdateSmoke.ReadKey("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEfEyCOMU4IJ14osOpBY3eFP4/fxK726BrpSdQoUOGeMJ+2DsQaDm4maWvFaZ22iI1oti6dIVOk7thJ8J5Lkp59Q==") is null, true);
        const string testTrust = "src/Raffica.Core/Updates/UpdateTrust.TestBuild.cs";
        Expect("update-smoke: the id of the test build is the default of --key-id",
            File.Exists(testTrust) && File.ReadAllText(testTrust).Contains($"private const string TestKeyId = \"{UpdateSmoke.TestKeyId}\";", StringComparison.Ordinal), true);

        using (var request = System.Text.Json.JsonDocument.Parse(UpdateSmoke.Request("0.0.2", "RafficaPdf-0.0.2-win-x64-setup.exe", @"C:\P a\RafficaPdf.exe", @"C:\P a")))
        {
            Expect("update-smoke: request.json package form", request.RootElement.GetProperty("form").GetString(), "WindowsInstaller");
            Expect("update-smoke: request.json program", request.RootElement.GetProperty("exePath").GetString(), @"C:\P a\RafficaPdf.exe");
            Expect("update-smoke: request.json target folder", request.RootElement.GetProperty("targetDirectory").GetString(), @"C:\P a");
        }

        Expect("update-smoke: helper only with the staging folder", string.Join("|", UpdateSmoke.HelperArguments(@"C:\a b\updates\0.0.2")), @"--finish-update|C:\a b\updates\0.0.2");
        Expect("update-smoke: test folder with an umlaut and &", Path.GetFileName(UpdateSmoke.TestDir(Path.GetTempPath())), "Raffica PDF Update Test ä&");

        // Command handling without Windows and without iscc.
        var dir = Path.Combine(Path.GetTempPath(), "raffica-package-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var layout = Path.Combine(dir, "layout");
            Directory.CreateDirectory(Path.Combine(layout, "cli"));
            foreach (var f in Installer.RequiredLayoutFiles)
            {
                File.WriteAllText(Path.Combine(layout, f), "x");
            }

            Expect("installer: complete layout", Installer.MissingLayoutFiles(layout).Count, 0);
            File.Delete(Path.Combine(layout, "cli", "pdfium.dll"));
            Expect("installer: missing CLI library is noticed", Installer.MissingLayoutFiles(layout).SequenceEqual(["cli/pdfium.dll"]), true);
            var output = Path.Combine(dir, Installer.SetupFileName("1.2.3"));
            Expect("installer: incomplete layout returns 1", Cli.Run(["installer", "--layout", layout, "--version", "1.2.3", "--out", output]), 1);
            Expect("installer: wrong file name returns 2", Cli.Run(["installer", "--layout", layout, "--version", "1.2.3", "--out", Path.Combine(dir, "setup.exe")]), 2);
            Expect("installer: wrong version returns 2", Cli.Run(["installer", "--layout", layout, "--version", "1.2", "--out", output]), 2);
            Expect("installer: without options returns 2", Cli.Run(["installer"]), 2);
            Expect("installer-smoke: without a setup returns 2", Cli.Run(["installer-smoke"]), 2);
            Expect("installer-smoke: missing setup returns 1", Cli.Run(["installer-smoke", Path.Combine(dir, "nope.exe")]), 1);
            Expect("update-smoke: without a key returns 2", Cli.Run(["update-smoke", "a.exe", "b.exe"]), 2);
            Expect("update-smoke: without a second setup returns 2", Cli.Run(["update-smoke", "a.exe", "--key", "k.pem"]), 2);
            Expect("update-smoke: wrong setup names return 2", Cli.Run(["update-smoke", "a.exe", "b.exe", "--key", "k.pem"]), 2);
            Expect("update-smoke: missing setups return 1", Cli.Run(["update-smoke", Path.Combine(dir, "RafficaPdf-0.0.1-win-x64-setup.exe"),
                Path.Combine(dir, "RafficaPdf-0.0.2-win-x64-setup.exe"), "--key", Path.Combine(dir, "k.pem")]), 1);
            Expect("inno-install: unknown option returns 2", Cli.Run(["inno-install", "--does-not-exist", "x"]), 2);
            if (!OperatingSystem.IsWindows())
            {
                Expect("inno-install: outside Windows returns 1", Cli.Run(["inno-install", "--dir", Path.Combine(dir, "inno")]), 1);
                Expect("inno-install: outside Windows nothing is downloaded", Directory.Exists(Path.Combine(dir, "inno")), false);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    // Builds tiny ELF files with clang in a temp directory and runs the real command on them.
    // The fixed inputs of the test vector in testdata/updates (version 0.2.0): the four known kinds with made-up sizes;
    // the SHA-256 of a file is the SHA-256 of its name, so the inputs need no real files.
    private static readonly (string Kind, string Name, long Size)[] VectorFiles =
    [
        ("win-x64-setup", "RafficaPdf-0.2.0-win-x64-setup.exe", 41_234_567),
        ("win-x64-portable", "RafficaPdf-0.2.0-win-x64-portable.zip", 43_210_987),
        ("linux-appimage", "RafficaPdf-0.2.0-x86_64.AppImage", 52_345_678),
        ("linux-deb", "raffica-pdf_0.2.0_amd64.deb", 38_765_432),
    ];

    private const string VectorDirectory = "testdata/updates";

    // The C# file that holds the public key of the test vector (as update-keygen prints it); update-verify reads it
    // like UpdateTrust.cs, and the Core test compiles it, so the printed literal is proven to be valid C#.
    private const string VectorKeySource = "tests/Raffica.Core.Tests/Updates/ReleaseToolTests.cs";

    private static string NameHash(string name) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)));

    private static string SumsOf(params string[] names) => string.Concat(names.Select(n => $"{NameHash(n)}  {n}\n"));

    // As "gh release view <tag> --json assets" prints it (more fields than the tool reads).
    private static string AssetsJson(IEnumerable<(string Name, long Size)> assets) =>
        new System.Text.Json.Nodes.JsonObject
        {
            ["assets"] = new System.Text.Json.Nodes.JsonArray(assets.Select(a => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
            {
                ["apiUrl"] = "https://api.github.invalid/assets/1",
                ["contentType"] = "application/octet-stream",
                ["downloadCount"] = 0,
                ["name"] = a.Name,
                ["size"] = a.Size,
                ["state"] = "uploaded",
            }).ToArray()),
        }.ToJsonString();

    // Runs a command with stdout and stderr captured (both are checked for key material).
    private static (int Code, string Out, string Err) Capture(params string[] args)
    {
        var (oldOut, oldErr) = (Console.Out, Console.Error);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            return (Cli.Run(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }
    }

    // Whether text contains the secret or any 16-character piece of it (a partial print counts as a leak).
    private static bool Leaks(string text, string secret)
    {
        if (secret.Length == 0)
        {
            return false;
        }

        for (var i = 0; i + 16 <= secret.Length; i += 8)
        {
            if (text.Contains(secret.Substring(i, 16), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return text.Contains(secret, StringComparison.Ordinal);
    }

    // The literal update-keygen prints: new UpdateKey("<id>", Convert.FromBase64String("<SPKI>")),
    private static (string Id, byte[] Key)? KeyLiteral(string text)
    {
        var m = Regex.Match(text, @"new UpdateKey\(\s*""(?<id>[^""]+)"",\s*Convert\.FromBase64String\(""(?<key>[A-Za-z0-9+/=]+)""\)\),");
        return m.Success ? (m.Groups["id"].Value, Convert.FromBase64String(m.Groups["key"].Value)) : null;
    }

    // An independent check of "<keyId>:<base64 of the 64-byte IEEE P1363 ECDSA P-256 signature>" (as ManifestVerifier does).
    private static bool SignatureVerifies(byte[] data, string signatureText, string keyId, byte[] publicKey)
    {
        var parts = signatureText.Trim().Split(':');
        if (parts.Length != 2 || parts[0] != keyId)
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKey, out _);
        return signature.Length == 64 && key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static List<(string Kind, string Name, long Size, string Sha256)> ManifestFiles(string file)
    {
        var result = new List<(string, string, long, string)>();
        if (!File.Exists(file))
        {
            return result;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
        foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
        {
            result.Add((f.GetProperty("kind").GetString()!, f.GetProperty("name").GetString()!, f.GetProperty("size").GetInt64(), f.GetProperty("sha256").GetString()!));
        }

        return result;
    }

    // The release tool for the update manifest (Milestone 8.1). Only throwaway keys, in a temp folder that is deleted at
    // the end and in an environment variable of this process that is removed again; the real signing key never appears.
    private static void UpdateReleaseParts()
    {
        const string keyEnv = "RAFFICA_SELFTEST_SIGNING_KEY";
        var dir = Path.Combine(Path.GetTempPath(), "raffica-update-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // update-manifest
            var win = Path.Combine(dir, "SHA256SUMS-windows.txt");
            var linux = Path.Combine(dir, "SHA256SUMS-linux.txt");
            var sizes = Path.Combine(dir, "assets.json");
            var others = new[] { "fuse-3.15.0.tar.xz", "type2-runtime-20251108.tar.gz" };
            File.WriteAllText(win, SumsOf(VectorFiles[1].Name, VectorFiles[0].Name));
            File.WriteAllText(linux, SumsOf(VectorFiles[3].Name, VectorFiles[2].Name, others[0], others[1]));
            var assets = VectorFiles.Select(f => (f.Name, f.Size)).Concat(others.Select(o => (o, 1000L)))
                .Concat([("SHA256SUMS-windows.txt", 200L), ("SHA256SUMS-linux.txt", 400L)]).ToList();
            File.WriteAllText(sizes, AssetsJson(assets));
            string[] ManifestArgs(string version, string output, string sizesFile, params string[] sums) =>
                ["update-manifest", "--version", version, "--sums", .. sums, "--sizes", sizesFile, "--out", output];

            var manifest = Path.Combine(dir, "update-manifest.json");
            Expect("update-manifest: success", Cli.Run(ManifestArgs("0.2.0", manifest, sizes, win, linux)), 0);
            var files = ManifestFiles(manifest);
            Expect("update-manifest: only known files (in a fixed order)", string.Join(",", files.Select(f => f.Kind)),
                "win-x64-setup,win-x64-portable,linux-appimage,linux-deb");
            foreach (var (kind, name, size) in VectorFiles)
            {
                Expect($"update-manifest: size and SHA-256 match ({kind})", files.Contains((kind, name, size, NameHash(name))), true);
            }

            var vectorManifest = Path.Combine(VectorDirectory, "update-manifest.json");
            Expect($"update-manifest: writes exactly the bytes of {vectorManifest} (test vector of the app)",
                File.Exists(manifest) && File.Exists(vectorManifest) && File.ReadAllBytes(manifest).AsSpan().SequenceEqual(File.ReadAllBytes(vectorManifest)), true);

            // The two macOS disk images: optional as a pair, listed after the four others in the order of Kinds.
            var macSums = Path.Combine(dir, "SHA256SUMS-macos.txt");
            var macNames = new[] { "RafficaPdf-0.2.0-macos-arm64.dmg", "RafficaPdf-0.2.0-macos-x64.dmg" };
            File.WriteAllText(macSums, SumsOf(macNames[1], macNames[0]));
            var macAssets = assets.Concat(macNames.Select(n => (n, 60_000_000L))).Concat([("SHA256SUMS-macos.txt", 300L)]).ToList();
            var macSizes = Path.Combine(dir, "assets-macos.json");
            File.WriteAllText(macSizes, AssetsJson(macAssets));
            var sixManifest = Path.Combine(dir, "update-manifest-six.json");
            Expect("ManifestWithThreeSumsHasSixKindsInOrder: success", Cli.Run(ManifestArgs("0.2.0", sixManifest, macSizes, win, linux, macSums)), 0);
            var sixFiles = ManifestFiles(sixManifest);
            Expect("ManifestWithThreeSumsHasSixKindsInOrder: kinds", string.Join(",", sixFiles.Select(f => f.Kind)),
                "win-x64-setup,win-x64-portable,linux-appimage,linux-deb,macos-arm64-dmg,macos-x64-dmg");
            Expect("ManifestWithThreeSumsHasSixKindsInOrder: dmg size and SHA-256", sixFiles.Contains(("macos-x64-dmg", macNames[1], 60_000_000L, NameHash(macNames[1]))), true);
            Expect("ManifestWithTwoSumsIsByteIdenticalToTheVector: the extra macOS files in the release change nothing",
                Cli.Run(ManifestArgs("0.2.0", Path.Combine(dir, "four.json"), macSizes, win, linux)) == 0 &&
                File.ReadAllBytes(Path.Combine(dir, "four.json")).AsSpan().SequenceEqual(File.ReadAllBytes(vectorManifest)), true);
            var oneMac = Path.Combine(dir, "SHA256SUMS-macos-one.txt");
            File.WriteAllText(oneMac, SumsOf(macNames[0]));
            var oneRefused = Path.Combine(dir, "one-refused.json");
            var (oneCode, _, oneErr) = Capture(ManifestArgs("0.2.0", oneRefused, macSizes, win, linux, oneMac));
            Expect("OnlyOneMacArchitectureIsRefused: returns 1", oneCode, 1);
            Expect("OnlyOneMacArchitectureIsRefused: names the missing kind", oneErr.Contains("macos-x64-dmg", StringComparison.Ordinal), true);
            Expect("OnlyOneMacArchitectureIsRefused: no file", File.Exists(oneRefused), false);
            var otherMac = Path.Combine(dir, "SHA256SUMS-macos-other.txt");
            File.WriteAllText(otherMac, SumsOf(macNames[0], macNames[1], "RafficaPdf-0.1.0-macos-x64.dmg"));
            var (otherCode, _, otherErr) = Capture(ManifestArgs("0.2.0", oneRefused, macSizes, win, linux, otherMac));
            Expect("AMacDmgOfAnotherVersionIsListedAsNotBelongingToTheVersion: returns 1", otherCode, 1);
            Expect("AMacDmgOfAnotherVersionIsListedAsNotBelongingToTheVersion: message",
                otherErr.Contains("RafficaPdf-0.1.0-macos-x64.dmg does not belong to version 0.2.0", StringComparison.Ordinal), true);

            var refused = Path.Combine(dir, "refused.json");
            Expect("update-manifest: a prerelease returns 2 (the app reads only X.Y.Z)", Cli.Run(ManifestArgs("0.2.0-rc.1", refused, sizes, win, linux)), 2);
            Expect("update-manifest: file names of another version return 1", Cli.Run(ManifestArgs("0.3.0", refused, sizes, win, linux)), 1);
            Expect("update-manifest: without the Linux sums kinds are missing, returns 1", Cli.Run(ManifestArgs("0.2.0", refused, sizes, win)), 1);
            var withoutSize = Path.Combine(dir, "without-size.json");
            File.WriteAllText(withoutSize, AssetsJson(assets.Where(a => a.Item1 != VectorFiles[2].Name)));
            Expect("update-manifest: missing size returns 1", Cli.Run(ManifestArgs("0.2.0", refused, withoutSize, win, linux)), 1);
            var zeroSize = Path.Combine(dir, "zero-size.json");
            File.WriteAllText(zeroSize, AssetsJson(assets.Select(a => a.Item1 == VectorFiles[0].Name ? (a.Item1, 0L) : a)));
            Expect("update-manifest: size 0 returns 1", Cli.Run(ManifestArgs("0.2.0", refused, zeroSize, win, linux)), 1);
            var broken = Path.Combine(dir, "SHA256SUMS-broken.txt");
            File.WriteAllText(broken, SumsOf(VectorFiles[1].Name) + "abc  RafficaPdf-0.2.0-win-x64-setup.exe\n");
            Expect("update-manifest: broken line in SHA256SUMS returns 1", Cli.Run(ManifestArgs("0.2.0", refused, sizes, broken, linux)), 1);
            var twice = Path.Combine(dir, "SHA256SUMS-twice.txt");
            File.WriteAllText(twice, SumsOf(VectorFiles[1].Name, VectorFiles[0].Name, VectorFiles[0].Name));
            Expect("update-manifest: duplicate file returns 1", Cli.Run(ManifestArgs("0.2.0", refused, sizes, twice, linux)), 1);
            var notJson = Path.Combine(dir, "not-json.json");
            File.WriteAllText(notJson, "<html>");
            Expect("update-manifest: --sizes without JSON returns 1", Cli.Run(ManifestArgs("0.2.0", refused, notJson, win, linux)), 1);
            Expect("update-manifest: no file is created on errors", File.Exists(refused), false);
            Expect("update-manifest: without --sizes returns 2", Cli.Run(["update-manifest", "--version", "0.2.0", "--sums", win, linux, "--out", refused]), 2);

            // update-keygen
            var keys = Path.Combine(dir, "keys");
            Directory.CreateDirectory(keys);
            var keyFile = Path.Combine(keys, "signing-key.txt");
            var (keygenCode, keygenOut, keygenErr) = Capture("update-keygen", "--id", "selftest", "--private-out", keyFile);
            Expect("update-keygen: success", keygenCode, 0);
            var privateText = File.Exists(keyFile) ? File.ReadAllText(keyFile).Trim() : string.Empty;
            Expect("update-keygen: the private key is in the file", privateText.Length > 0, true);
            Expect("update-keygen: the private key is never in the output", privateText.Length > 0 && !Leaks(keygenOut + keygenErr, privateText), true);
            var literal = KeyLiteral(keygenOut);
            Expect("update-keygen: stdout is an entry for UpdateTrust.Keys with the id", literal?.Id, "selftest");
            var matches = false;
            if (literal is { } lit && privateText.Length > 0)
            {
                using var fromFile = ECDsa.Create();
                fromFile.ImportPkcs8PrivateKey(Convert.FromBase64String(privateText), out _);
                using var fromLiteral = ECDsa.Create();
                fromLiteral.ImportSubjectPublicKeyInfo(lit.Key, out _);
                matches = fromFile.KeySize == 256 && fromLiteral.KeySize == 256 &&
                    fromFile.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(lit.Key);
            }

            Expect("update-keygen: P-256, PKCS#8 in the file matches the printed public key", matches, true);
            Expect("update-keygen: an existing file is never overwritten", Cli.Run(["update-keygen", "--id", "selftest", "--private-out", keyFile]), 1);
            Expect("update-keygen: the file stays unchanged", File.Exists(keyFile) && File.ReadAllText(keyFile).Trim() == privateText, true);
            Expect("update-keygen: id with ':' returns 2", Cli.Run(["update-keygen", "--id", "a:b", "--private-out", Path.Combine(keys, "x.txt")]), 2);
            Expect("update-keygen: without --private-out returns 2", Cli.Run(["update-keygen", "--id", "selftest"]), 2);
            var repo = Path.Combine(dir, "repo");
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "sub"));
            var inRepo = Path.Combine(repo, "sub", "key.txt");
            Expect("update-keygen: never into a Git working tree", Cli.Run(["update-keygen", "--id", "selftest", "--private-out", inRepo]), 1);
            Expect("update-keygen: nothing is created inside a Git working tree", File.Exists(inRepo), false);
            if (File.Exists(keyFile))
            {
                if (OperatingSystem.IsWindows())
                {
                    Expect("update-keygen: private file readable only by the user (Windows ACL)", UserOnlyAcl(keyFile), true);
                }
                else
                {
                    Expect("update-keygen: private file readable only by the user (0600)", File.GetUnixFileMode(keyFile), UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            // update-sign and update-verify with the throwaway key (trusted through a C# file like UpdateTrust.cs).
            var trust = Path.Combine(dir, "Trust.cs");
            File.WriteAllText(trust, "IReadOnlyList<UpdateKey> Keys = [\n" + keygenOut + "];\n");
            var sig = Path.Combine(dir, "update-manifest.json.sig");
            string[] SignArgs(string output, string id = "selftest", string source = "") =>
                ["update-sign", "--manifest", manifest, "--key-env", keyEnv, "--id", id, "--out", output, "--source", source.Length > 0 ? source : trust];

            Environment.SetEnvironmentVariable(keyEnv, privateText);
            var (signCode, signOut, signErr) = Capture(SignArgs(sig));
            Expect("update-sign: success", signCode, 0);
            var sigText = File.Exists(sig) ? File.ReadAllText(sig) : string.Empty;
            Expect("update-sign: the signature verifies with the key", literal is { } l && File.Exists(manifest) &&
                SignatureVerifies(File.ReadAllBytes(manifest), sigText, "selftest", l.Key), true);
            Expect("update-sign: the key is never in the output", privateText.Length > 0 && !Leaks(signOut + signErr + sigText, privateText), true);
            Expect("update-verify: success", Cli.Run(["update-verify", "--manifest", manifest, "--sig", sig, "--source", trust]), 0);

            var tampered = Path.Combine(dir, "tampered.json");
            if (File.Exists(manifest))
            {
                File.WriteAllText(tampered, File.ReadAllText(manifest).Replace("41234567", "41234568", StringComparison.Ordinal));
            }

            Expect("update-verify: tampered -> error", Cli.Run(["update-verify", "--manifest", tampered, "--sig", sig, "--source", trust]), 1);
            var badSig = Path.Combine(dir, "bad.sig");
            File.WriteAllText(badSig, sigText.Length > 20 ? sigText[..12] + (sigText[12] == 'A' ? 'B' : 'A') + sigText[13..] : "selftest:AAAA");
            Expect("update-verify: changed signature -> error", Cli.Run(["update-verify", "--manifest", manifest, "--sig", badSig, "--source", trust]), 1);
            var otherId = Path.Combine(dir, "other-id.sig");
            File.WriteAllText(otherId, sigText.Replace("selftest:", "other:", StringComparison.Ordinal));
            Expect("update-verify: unknown id -> error", Cli.Run(["update-verify", "--manifest", manifest, "--sig", otherId, "--source", trust]), 1);
            Expect("update-verify: without a key source returns 2", Cli.Run(["update-verify", "--manifest", manifest, "--sig", sig]), 2);
            Expect("update-verify: manifest without a signature returns 2", Cli.Run(["update-verify", "--manifest", manifest, "--source", trust]), 2);
            Expect("update-verify: without a manifest only the keys are checked (real key: 0)", Cli.Run(["update-verify", "--source", trust]), 0);

            var noSig = Path.Combine(dir, "none.sig");
            Expect("update-sign: --key as an argument returns 2", Cli.Run(["update-sign", "--manifest", manifest, "--key", privateText, "--id", "selftest", "--out", noSig, "--source", trust]), 2);
            Expect("update-sign: unknown id returns 1", Cli.Run(SignArgs(noSig, id: "other")), 1);

            using (var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                var otherText = Convert.ToBase64String(otherKey.ExportPkcs8PrivateKey());
                Environment.SetEnvironmentVariable(keyEnv, otherText);
                var (code, output, error) = Capture(SignArgs(noSig));
                Expect("update-sign: foreign key (does not match the id) returns 1", code, 1);
                Expect("update-sign: foreign key is never in the output", Leaks(output + error, otherText), false);
            }

            Environment.SetEnvironmentVariable(keyEnv, "no-key-ZQXJ");
            var (garbageCode, garbageOut, garbageErr) = Capture(SignArgs(noSig));
            Expect("update-sign: unreadable key returns 1", garbageCode, 1);
            Expect("update-sign: unreadable key is never in the output", (garbageOut + garbageErr).Contains("ZQXJ", StringComparison.Ordinal), false);

            Environment.SetEnvironmentVariable(keyEnv, null);
            Expect("update-sign: error without the environment variable", Cli.Run(SignArgs(noSig)), 1);
            Expect("update-sign: no signature is created on errors", File.Exists(noSig), false);

            // The placeholder of UpdateTrust.cs: no release is ever signed or verified with it.
            var placeholder = Path.Combine(dir, "PlaceholderTrust.cs");
            File.WriteAllText(placeholder,
                "public const string PlaceholderKeyId = \"placeholder\";\n" +
                "IReadOnlyList<UpdateKey> Keys { get; } =\n[\n    new UpdateKey(\n        PlaceholderKeyId,\n" +
                "        Convert.FromBase64String(\"MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEfEyCOMU4IJ14osOpBY3eFP4/fxK726BrpSdQoUOGeMJ+2DsQaDm4maWvFaZ22iI1oti6dIVOk7thJ8J5Lkp59Q==\")),\n];\n");
            Environment.SetEnvironmentVariable(keyEnv, privateText);
            Expect("update-sign: the placeholder never signs", Cli.Run(SignArgs(noSig, id: "placeholder", source: placeholder)), 1);
            Expect("update-verify: placeholder as the only key returns 1", Cli.Run(["update-verify", "--manifest", manifest, "--sig", sig, "--source", placeholder]), 1);
            Expect("update-verify: without a manifest the key check fails on the placeholder", Cli.Run(["update-verify", "--source", placeholder]), 1);
            Expect("update-sign: without --source it takes UpdateTrust.cs (the id selftest is not in it) and returns 1",
                Cli.Run(["update-sign", "--manifest", manifest, "--key-env", keyEnv, "--id", "selftest", "--out", noSig]), 1);
            Environment.SetEnvironmentVariable(keyEnv, null);

            // The committed test vector (signed once by this tool with a throwaway key; ReleaseToolTests checks it with
            // the app's ManifestVerifier). update-verify must accept it with the key from the test source.
            var vectorSig = Path.Combine(VectorDirectory, "update-manifest.json.sig");
            Expect("update-verify: the test vector verifies with the key from ReleaseToolTests.cs",
                Cli.Run(["update-verify", "--manifest", vectorManifest, "--sig", vectorSig, "--source", VectorKeySource]), 0);
            var (realCode, _, realErr) = Capture("update-verify", "--manifest", vectorManifest, "--sig", vectorSig, "--keys-from-source");
            Expect("update-verify --keys-from-source: the test key is not trusted in the program", realCode, 1);
            var trustText = File.Exists(TrustFile) ? File.ReadAllText(TrustFile) : string.Empty;
            if (Regex.Count(trustText, @"new UpdateKey\(") == 1 && Regex.IsMatch(trustText, @"new UpdateKey\(\s*PlaceholderKeyId\s*,"))
            {
                Expect("update-verify --keys-from-source: fails with a hint while UpdateTrust.cs holds only the placeholder",
                    realErr.Contains("placeholder", StringComparison.Ordinal), true);
                Expect("update-verify --keys-from-source without a manifest (preflight): fails while UpdateTrust.cs holds only the placeholder",
                    Cli.Run(["update-verify", "--keys-from-source"]), 1);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyEnv, null);
            Directory.Delete(dir, recursive: true);
        }
    }

    private const string TrustFile = "src/Raffica.Core/Updates/UpdateTrust.cs";

    // Windows: inheritance cut and exactly one rule, for the current user.
    private static bool UserOnlyAcl(string file)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var security = new FileInfo(file).GetAccessControl();
        var rules = security.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<System.Security.AccessControl.FileSystemAccessRule>().ToList();
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
        return security.AreAccessRulesProtected && rules.Count == 1 && rules[0].IdentityReference.Equals(user) &&
            rules[0].AccessControlType == System.Security.AccessControl.AccessControlType.Allow;
    }

    // Trust model once the repository is public: code from a fork PR never reaches a persistent self-hosted runner,
    // the checkout leaves no token behind, the signing secret lives in one hosted, environment-guarded job, and every
    // action is pinned to a commit. A missing workflow file (codeql.yml before it exists) is no violation.
    private const string PullRequestGuard = "github.event_name == 'pull_request'";

    private static string TriggersOf(string yml) =>
        Regex.Match(yml, @"(?m)^on:\n(?<b>(?:[ #].*\n|\n)*)").Groups["b"].Value;

    private static bool HasTrigger(string yml, string trigger) =>
        Regex.IsMatch(TriggersOf(yml), @"(?m)^  " + trigger + @":");

    private static List<string> PullRequestTargetViolations(string yml) =>
        HasTrigger(yml, "pull_request_target") ? ["pull_request_target"] : [];

    // The labels dotnet-linux, dotnet-windows and dotnet-windows-gpu exist only on the owner's runners.
    private static bool IsSelfHosted(string line) =>
        Regex.IsMatch(line, @"self-hosted|dotnet-linux|dotnet-windows");

    // A runs-on line that picks a hosted runner for foreign pull requests and the own runner only otherwise: either every pull
    // request goes to the hosted runner (the guard and matrix.hosted), a pull request from a fork does (the head repository
    // differs from this one), or the job "changes" of ci.yml decided so (needs.changes.outputs.hosted, true only for a fork,
    // Docs/DECISIONS.md, entry "CI on self-hosted runners, as few runs as possible").
    private static bool IsGuarded(string line) =>
        (line.Contains(PullRequestGuard, StringComparison.Ordinal) && line.Contains("matrix.hosted", StringComparison.Ordinal)) ||
        (line.Contains(PullRequestGuard, StringComparison.Ordinal) && line.Contains("github.event.pull_request.head.repo.full_name != github.repository", StringComparison.Ordinal) &&
            line.Contains("ubuntu-24.04", StringComparison.Ordinal)) ||
        (line.Contains("needs.changes.outputs.hosted == 'true'", StringComparison.Ordinal) && line.Contains("matrix.hosted", StringComparison.Ordinal));

    private static List<string> SelfHostedOnPullRequestViolations(string file, string yml)
    {
        var violations = new List<string>();
        var runsOn = Regex.Matches(yml, @"(?m)^    runs-on:.*$").Select(m => m.Value).ToList();
        if (file == "measure.yml" && HasTrigger(yml, "pull_request"))
        {
            violations.Add("trigger pull_request");
        }

        if (!HasTrigger(yml, "pull_request"))
        {
            return violations;
        }

        var noSelfHostedAtAll = file is "codeql.yml";
        foreach (var line in runsOn.Where(IsSelfHosted))
        {
            if (noSelfHostedAtAll || !IsGuarded(line))
            {
                violations.Add(line.Trim());
            }
        }

        if (file == "ci.yml" && !runsOn.Any(IsGuarded))
        {
            violations.Add("no hosted runs-on for pull requests");
        }

        return violations;
    }

    private static List<string> CheckoutViolations(string yml) =>
        [.. Regex.Split(yml, @"(?m)^(?=      - )").Where(s => s.Contains("actions/checkout@", StringComparison.Ordinal))
            .Where(s => !Regex.IsMatch(s, @"(?m)^ +persist-credentials: false\s*$")).Select(_ => "checkout without persist-credentials: false")];

    private static List<string> UnpinnedActionViolations(string yml) =>
        [.. Regex.Matches(yml, @"(?m)^\s*(?:- )?uses: (?<a>[^\s#]+)").Select(m => m.Groups["a"].Value)
            .Where(a => !a.StartsWith("./", StringComparison.Ordinal) && !Regex.IsMatch(a, @"@[0-9a-f]{40}$"))];

    private static List<string> PermissionsViolations(string yml)
    {
        var violations = new List<string>();
        if (!Regex.IsMatch(yml, @"(?m)^permissions:"))
        {
            violations.Add("permissions missing");
        }

        if (Regex.IsMatch(yml, @"permissions:\s*write-all"))
        {
            violations.Add("write-all");
        }

        return violations;
    }

    private static void PublicRepoWorkflowParts()
    {
        const string dir = ".github/workflows";
        string? Read(string file) => File.Exists(Path.Combine(dir, file))
            ? File.ReadAllText(Path.Combine(dir, file)).Replace("\r\n", "\n", StringComparison.Ordinal) : null;
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.yml").Concat(Directory.GetFiles(dir, "*.yaml")).Select(Path.GetFileName).OfType<string>().Order().ToList() : [];
        Expect("Workflows: the folder .github/workflows has files", files.Count > 0, true);

        // Negative controls: the rules must bite on a deliberately bad workflow.
        const string bad = "on:\n  pull_request_target:\n  pull_request:\npermissions: write-all\njobs:\n  x:\n    runs-on: [self-hosted, dotnet-linux]\n" +
            "    steps:\n      - uses: actions/checkout@v4\n      - uses: actions/cache@0123456789abcdef0123456789abcdef01234567\n";
        Expect("Control: pull_request_target is detected", PullRequestTargetViolations(bad).Count, 1);
        Expect("Control: self-hosted on pull requests is detected", SelfHostedOnPullRequestViolations("x.yml", bad).Count, 1);
        Expect("Control: checkout without persist-credentials is detected", CheckoutViolations(bad).Count, 1);
        Expect("Control: an unpinned action is detected", UnpinnedActionViolations(bad).Count, 1);
        Expect("Control: write-all is detected", PermissionsViolations(bad).Count, 1);
        const string good = "on:\n  pull_request:\njobs:\n  build:\n" +
            "    runs-on: ${{ fromJSON(github.event_name == 'pull_request' && format('[\"{0}\"]', matrix.hosted) || format('[\"self-hosted\",\"{0}\"]', matrix.label)) }}\n";
        Expect("Control: the correctly protected runs-on of ci.yml has no finding", SelfHostedOnPullRequestViolations("ci.yml", good).Count, 0);
        Expect("Control: the label dotnet-windows alone counts as self-hosted", SelfHostedOnPullRequestViolations("x.yml", "on:\n  pull_request:\njobs:\n  x:\n    runs-on: [dotnet-windows]\n").Count, 1);
        Expect("Control: measure.yml with pull_request is detected",
            SelfHostedOnPullRequestViolations("measure.yml", "on:\n  pull_request:\njobs:\n  x:\n    runs-on: ubuntu-24.04\n").Count, 1);

        string Join(IEnumerable<string> v) => string.Join("; ", v);
        var noTarget = new List<string>();
        var selfHosted = new List<string>();
        var checkout = new List<string>();
        var unpinned = new List<string>();
        var permissions = new List<string>();
        var secretsOutside = new List<string>();
        foreach (var file in files)
        {
            var yml = Read(file)!;
            Expect($"Workflows: {file} has a readable on: block", TriggersOf(yml).Trim().Length > 0, true);
            noTarget.AddRange(PullRequestTargetViolations(yml).Select(v => $"{file}: {v}"));
            selfHosted.AddRange(SelfHostedOnPullRequestViolations(file, yml).Select(v => $"{file}: {v}"));
            checkout.AddRange(CheckoutViolations(yml).Select(v => $"{file}: {v}"));
            unpinned.AddRange(UnpinnedActionViolations(yml).Select(v => $"{file}: {v}"));
            permissions.AddRange(PermissionsViolations(yml).Select(v => $"{file}: {v}"));
            if (file != "release.yml" && yml.Contains("secrets.", StringComparison.Ordinal))
            {
                secretsOutside.Add(file);
            }
        }

        // A workflow that is missing (codeql.yml before it exists) is simply not listed above.
        Expect("Workflows: no pull_request_target", Join(noTarget), "");
        Expect("Workflows: pull request runs never on self-hosted", Join(selfHosted), "");
        Expect("Workflows: every checkout has persist-credentials: false", Join(checkout), "");
        Expect("Workflows: permissions at the top and no write-all", Join(permissions), "");
        Expect("Workflows: every action is pinned to a 40-digit SHA", Join(unpinned), "");
        Expect("Workflows: secrets. only in release.yml", Join(secretsOutside), "");

        var release = Read("release.yml");
        if (release is not null)
        {
            Expect("release.yml: no release trigger", HasTrigger(release, "release"), false);
            var manifest = Regex.Match(release, @"(?m)^  manifest:\n(?<b>(?:    .*\n|\n)*)").Groups["b"].Value;
            Expect("release.yml: job manifest found", manifest.Length > 0, true);
            var otherJobs = manifest.Length > 0 ? release.Replace(manifest, string.Empty, StringComparison.Ordinal) : release;
            Expect("release.yml: secrets. only in the job manifest", otherJobs.Contains("secrets.", StringComparison.Ordinal), false);
            Expect("release.yml: job manifest with environment: release-signing",
                Regex.IsMatch(manifest, @"(?m)^    environment: release-signing\s*$"), true);
            var manifestRunsOn = Regex.Match(manifest, @"(?m)^    runs-on:.*$").Value;
            Expect("release.yml: job manifest on a hosted runner", manifestRunsOn.Length > 0 && !IsSelfHosted(manifestRunsOn), true);
        }

        // Code scanning is paid in a private repository: the job must be skipped there, and the write right to the
        // security events belongs to the job only, never to the workflow.
        var codeql = Read("codeql.yml");
        if (codeql is not null)
        {
            var topLevel = Regex.Match(codeql, @"(?m)^permissions:\n(?<b>(?:  .*\n)*)").Groups["b"].Value;
            Expect("codeql.yml: top-level permissions only contents: read", topLevel.Trim(), "contents: read");
            Expect("codeql.yml: security-events: write exactly once, in the job", Regex.Count(codeql, @"security-events: write"), 1);
            Expect("codeql.yml: security-events: write is under the job (permissions indented by four spaces)",
                Regex.IsMatch(codeql, @"(?m)^    permissions:\n      contents: read\n      security-events: write\n"), true);
            Expect("codeql.yml: job only in the public repository", Regex.IsMatch(codeql, @"(?m)^    if: github\.event\.repository\.private == false\s*$"), true);
            Expect("codeql.yml: hosted runner only", Regex.Match(codeql, @"(?m)^    runs-on:.*$").Value.Trim(), "runs-on: ubuntu-24.04");
        }
    }

    // The third job of release.yml signs the update manifest. The signing key is a secret of the environment
    // release-signing: it may appear in exactly one place, the env of the step that runs the signing command, and
    // nowhere else (no job or workflow env, no other step, no action). The workflow is dispatch-only, so neither a
    // pull request nor a fork ever starts it; the tag whose code runs in the approved job must be on main or dev.
    private static void ManifestJobParts()
    {
        const string workflow = ".github/workflows/release.yml";
        const string container = "packaging/linux/container-build.sh";
        if (!File.Exists(workflow) || !File.Exists(container))
        {
            Expect("release.yml: release.yml and container-build.sh present", false, true);
            return;
        }

        var yml = File.ReadAllText(workflow).Replace("\r\n", "\n", StringComparison.Ordinal);
        var on = Regex.Match(yml, @"(?m)^on:\n(?<b>(?:[ #].*\n|\n)*)").Groups["b"].Value;
        // Dispatch only (M12.1): the workflow ref of a release event is a tag, and the runner-group restriction can name
        // branch refs only (refs/heads/main, refs/heads/dev).
        Expect("release.yml: trigger only workflow_dispatch",
            string.Join(",", Regex.Matches(on, @"(?m)^  (?<k>[a-z_]+):").Select(m => m.Groups["k"].Value)), "workflow_dispatch");
        Expect("release.yml: no trigger for pull requests", yml.Contains("pull_request", StringComparison.Ordinal), false);
        Expect("release.yml: nothing reads a release event any more (github.event.release, event_name == 'release')",
            Regex.IsMatch(yml, @"github\.event\.release|event_name == 'release'"), false);
        Expect("release.yml: TAG comes from inputs.tag only in every job", Regex.Count(yml, @"(?m)^      TAG: \$\{\{ inputs\.tag \}\}$"), Regex.Count(yml, @"(?m)^      TAG:"));
        Expect("release.yml: TAG is set in four jobs", Regex.Count(yml, @"(?m)^      TAG:"), 4);
        Expect("release.yml: UPLOAD comes from inputs.upload only in every job", Regex.Count(yml, @"(?m)^      UPLOAD: \$\{\{ inputs\.upload \}\}$"), Regex.Count(yml, @"(?m)^      UPLOAD:"));
        Expect("release.yml: UPLOAD is set in three jobs", Regex.Count(yml, @"(?m)^      UPLOAD:"), 3);
        Expect("release.yml: concurrency only per inputs.tag", yml.Contains("  group: release-${{ inputs.tag }}\n", StringComparison.Ordinal), true);

        string Job(string name) => Regex.Match(yml, @"(?m)^  " + name + @":\n(?<b>(?:    .*\n|\n)*)").Groups["b"].Value;
        List<string> StepsOf(string jobText) => [.. Regex.Split(jobText, @"(?m)^(?=      - )").Skip(1)];
        string Name(string step) => Regex.Match(step, @"^      - name: (?<n>.+)$|^        name: (?<n>.+)$", RegexOptions.Multiline).Groups["n"].Value;

        // preflight: before anything is uploaded, the tag must trust a real key (a release with only the placeholder
        // could never update itself, and a rerun cannot fix a tag). Without upload (dry run) the check is skipped.
        var preflight = Job("preflight");
        Expect("release.yml: job preflight present", preflight.Length > 0, true);
        Expect("release.yml: job preflight on the Linux runner", preflight.Contains("    runs-on: [self-hosted, dotnet-linux]\n", StringComparison.Ordinal), true);
        Expect("release.yml: job preflight only reads (contents: read)", Regex.IsMatch(preflight, @"    permissions:\n      contents: read\n"), true);
        Expect("release.yml: job preflight needs no other job", preflight.Contains("    needs:", StringComparison.Ordinal), false);
        var trustCheck = StepsOf(preflight).FirstOrDefault(s => Name(s) == "Check that the tag trusts a real update key") ?? string.Empty;
        Expect("release.yml: preflight checks the keys of the tag when uploading (update-verify --keys-from-source)",
            trustCheck.Contains("        if: env.UPLOAD == 'true'\n", StringComparison.Ordinal) &&
            Regex.IsMatch(trustCheck, @"\n        run: dotnet run tools/package\.cs -- update-verify --keys-from-source\n"), true);
        // With upload=true the tag's commit must lie on main or dev (identical or an ancestor), checked before any step
        // checks out or runs code of the tag: the environment's branch rule constrains only the workflow ref, not the tag.
        const string onBranchName = "Check that the tag is on main or dev";
        void ExpectOnBranchStep(string step, string label)
        {
            Expect($"release.yml: {label} has the step '{onBranchName}'", step.Length > 0, true);
            Expect($"release.yml: {label}: tag check against main and dev (compare, identical/behind, otherwise abort)",
                step.Contains("for branch in main dev", StringComparison.Ordinal) &&
                step.Contains("compare/$branch...$sha", StringComparison.Ordinal) &&
                step.Contains("identical", StringComparison.Ordinal) && step.Contains("behind", StringComparison.Ordinal) &&
                step.Contains("exit 1", StringComparison.Ordinal) && step.Contains("set -euo pipefail", StringComparison.Ordinal), true);
            Expect($"release.yml: {label}: tag check resolves the tag through refs/tags (no branch of the same name)",
                step.Contains("commits/refs/tags/$TAG", StringComparison.Ordinal), true);
            // The verified commit id leaves the step; the checkouts use it (never the tag name again: a tag can move).
            Expect($"release.yml: {label}: tag check outputs the verified commit id (id ontag, sha=$sha into GITHUB_OUTPUT, only after the check passed)",
                step.Contains("        id: ontag\n", StringComparison.Ordinal) &&
                Regex.IsMatch(step, @"behind \]\]; then\n(?:.*\n)*?\s+echo ""sha=\$sha"" >> ""\$GITHUB_OUTPUT""\n\s+exit 0"), true);
            var runAt = step.IndexOf("\n        run: |\n", StringComparison.Ordinal);
            Expect($"release.yml: {label}: tag check takes GH_TOKEN with GH_REPO from the env of the step, TAG only from the environment",
                Regex.IsMatch(step, @"\n        env:\n          GH_TOKEN: \$\{\{ github\.token \}\}\n(?:          #[^\n]*\n)?          GH_REPO: \$\{\{ github\.repository \}\}\n") &&
                runAt >= 0 && !step[runAt..].Contains("${{", StringComparison.Ordinal), true);
        }

        var preflightSteps = StepsOf(preflight);
        var preflightTagCheck = preflightSteps.FindIndex(x => Name(x) == "Check the tag");
        var preflightOnBranch = preflightSteps.FindIndex(x => Name(x) == onBranchName);
        ExpectOnBranchStep(preflightOnBranch >= 0 ? preflightSteps[preflightOnBranch] : string.Empty, "preflight");
        Expect("release.yml: preflight checks the tag directly after the tag rule", preflightTagCheck >= 0 && preflightOnBranch == preflightTagCheck + 1, true);
        Expect("release.yml: preflight checks the tag when uploading and when no ref is given", preflightOnBranch >= 0 &&
            preflightSteps[preflightOnBranch].Contains("        if: env.UPLOAD == 'true' || inputs.ref == ''\n", StringComparison.Ordinal), true);
        Expect("release.yml: preflight checks out the tag only after the branch check",
            preflightOnBranch >= 0 && preflightOnBranch < preflightSteps.FindIndex(x => x.Contains("actions/checkout@", StringComparison.Ordinal)), true);
        // No checkout by tag name on the upload path (the tag could move after the check): preflight and manifest check out
        // the commit id of their own verifying step, the build jobs the id from preflight (the tag name only as the
        // fallback of a dry run, where preflight does not resolve it and nothing is uploaded).
        Expect("release.yml: preflight and manifest check out the verified commit id (steps.ontag.outputs.sha)",
            Regex.Count(yml, @"(?m)^          ref: \$\{\{ steps\.ontag\.outputs\.sha \}\}$"), 2);
        Expect("release.yml: build jobs check out inputs.ref, otherwise the commit id of preflight (tag name only as the fallback of the dry run)",
            Regex.Count(yml, @"(?m)^          ref: \$\{\{ inputs\.ref \|\| needs\.preflight\.outputs\.tag_sha \|\| format\('refs/tags/\{0\}', inputs\.tag\) \}\}$"), 2);
        Expect("release.yml: no checkout by the tag name alone", Regex.IsMatch(yml, @"(?m)^          ref: (refs/tags/)?\$\{\{ inputs\.tag \}\}$"), false);
        Expect("release.yml: preflight outputs the verified commit id (outputs.tag_sha)",
            preflight.Contains("    outputs:\n      tag_sha: ${{ steps.ontag.outputs.sha }}\n", StringComparison.Ordinal), true);
        Expect("release.yml: every checkout of the build jobs comes after 'Require the verified commit' (uploading without a verified commit aborts)",
            new[] { "windows", "linux" }.All(j =>
            {
                var st = StepsOf(Job(j));
                var guard = st.FindIndex(x => Name(x) == "Require the verified commit");
                return guard >= 0 && guard < st.FindIndex(x => x.Contains("actions/checkout@", StringComparison.Ordinal)) &&
                    st[guard].Contains("        if: env.UPLOAD == 'true'\n", StringComparison.Ordinal) &&
                    st[guard].Contains("TAG_SHA: ${{ needs.preflight.outputs.tag_sha }}", StringComparison.Ordinal) &&
                    st[guard].Contains("[0-9a-f]{40}", StringComparison.Ordinal);
            }), true);

        // The ref input is built on runners that also build the release: a job that takes it checks, before its first checkout,
        // that it is a commit of main or dev (compare status identical or behind), so a fork's pull request commit is refused.
        foreach (var guardedJob in new[] { "preflight", "windows", "linux" })
        {
            var st = StepsOf(Job(guardedJob));
            var refGuard = st.FindIndex(x => Name(x) == "Check that the ref is on main or dev");
            Expect($"release.yml: Job {guardedJob} checks that inputs.ref is on main or dev before its first checkout",
                refGuard >= 0 && refGuard < st.FindIndex(x => x.Contains("actions/checkout@", StringComparison.Ordinal)) &&
                st[refGuard].Contains("        if: inputs.ref != ''\n", StringComparison.Ordinal) &&
                st[refGuard].Contains("compare/$branch...$sha", StringComparison.Ordinal) &&
                st[refGuard].Contains("identical", StringComparison.Ordinal) && st[refGuard].Contains("behind", StringComparison.Ordinal), true);
        }

        // The workflows that build the ref input on the owner's own machines (not the release builders) check it the same way
        // before the checkout: a branch of this repository, or a commit that is on main or dev.
        foreach (var ownerWorkflow in new[] { "measure.yml", "windows-update-check.yml", "linux-update-check.yml" })
        {
            var ownerPath = ".github/workflows/" + ownerWorkflow;
            var ownerYml = File.Exists(ownerPath) ? File.ReadAllText(ownerPath).Replace("\r\n", "\n", StringComparison.Ordinal) : string.Empty;
            var guardAt = ownerYml.IndexOf("- name: Check that the ref is code of this repository", StringComparison.Ordinal);
            var checkoutAt = ownerYml.IndexOf("actions/checkout@", StringComparison.Ordinal);
            var checkoutOfRefAt = ownerYml.IndexOf("ref: ${{ inputs.ref }}", StringComparison.Ordinal);
            Expect($"{ownerWorkflow}: the ref input is checked as code of this repository before any checkout of it",
                guardAt >= 0 && guardAt < checkoutAt && guardAt < checkoutOfRefAt &&
                ownerYml.Contains("compare/$branch...$r", StringComparison.Ordinal) && ownerYml.Contains("branches/$r", StringComparison.Ordinal) &&
                ownerYml.Contains("identical", StringComparison.Ordinal) && ownerYml.Contains("behind", StringComparison.Ordinal), true);
        }

        // The dispatch input ref accepts any text; pull/N/head and refs/pull/N/head would run foreign code on the persistent
        // runners with a write token. Every job that sees the input checks it in its first step against a positive list
        // (empty, a commit id, a plain branch name): the rule of RefInputRule, in bash or PowerShell.
        string[] refAccepted = ["main", "dev", "feature/x", "refs/heads/dev", new string('a', 40), string.Empty];
        string[] refRejected = ["refs/pull/1/head", "refs/tags/v1", "pull/35/head", "pull/1/merge", "refs/heads/../x", "a b", "$(x)"];
        foreach (var value in refAccepted)
        {
            Expect($"ref rule: '{value}' is accepted", RefInputRule.Accepts(value), true);
        }

        foreach (var value in refRejected)
        {
            Expect($"ref rule: '{value}' is rejected", RefInputRule.Accepts(value), false);
        }

        foreach (var refJob in new[] { "preflight", "windows", "linux", "manifest" })
        {
            var refSteps = StepsOf(Job(refJob));
            var first = refSteps.Count > 0 ? refSteps[0] : string.Empty;
            Expect($"release.yml: Job {refJob} takes inputs.ref only through the environment (REF_INPUT)",
                Job(refJob).Contains("      REF_INPUT: ${{ inputs.ref }}\n", StringComparison.Ordinal), true);
            Expect($"release.yml: Job {refJob} checks REF_INPUT in the first step against the allow list (empty, 40 hex, branch name)",
                first.Contains("REF_INPUT", StringComparison.Ordinal) && first.Contains("[0-9a-f]{40}", StringComparison.Ordinal) &&
                first.Contains("[A-Za-z0-9._/-]+", StringComparison.Ordinal) && first.Contains("pull/", StringComparison.Ordinal) &&
                first.Contains("refs/heads/", StringComparison.Ordinal) && !first.Contains("inputs.ref", StringComparison.Ordinal), true);

            // Run the real step with every value (bash for the Linux jobs, PowerShell for the Windows job).
            var powerShell = refJob == "windows";
            var runAt = first.IndexOf("\n        run: |\n", StringComparison.Ordinal);
            if (runAt < 0)
            {
                Expect($"release.yml: Job {refJob}: first step has a script block", false, true);
                continue;
            }

            var script = string.Join('\n', first[(runAt + "\n        run: |\n".Length)..].Split('\n')
                .TakeWhile(l => l.Length == 0 || l.StartsWith("          ", StringComparison.Ordinal)).Select(l => l.Length >= 10 ? l[10..] : l)) + "\n";
            var scriptDir = Path.Combine(Path.GetTempPath(), "raffica-refstep-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scriptDir);
            try
            {
                var scriptFile = Path.Combine(scriptDir, powerShell ? "step.ps1" : "step.sh").Replace('\\', '/');
                File.WriteAllText(scriptFile, script, new UTF8Encoding(powerShell));
                var probeFile = Path.Combine(scriptDir, "probe.sh").Replace('\\', '/');
                File.WriteAllText(probeFile, "echo raffica-probe\n");
                // On Windows "bash" is often the WSL launcher, which cannot see these paths: Git for Windows first.
                var interpreters = powerShell ? new[] { "pwsh", "powershell" }
                    : OperatingSystem.IsWindows() ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"), "bash" }
                    : new[] { "bash" };
                string? shell = null;
                foreach (var candidate in interpreters)
                {
                    var (code, output) = powerShell ? RunScript(candidate, ["-NoProfile", "-NonInteractive", "-Command", "'raffica-probe'"], []) : RunScript(candidate, [probeFile], []);
                    if (code == 0 && output.Contains("raffica-probe", StringComparison.Ordinal))
                    {
                        shell = candidate;
                        break;
                    }
                }

                if (shell is null)
                {
                    Console.WriteLine($"selftest: {(powerShell ? "PowerShell" : "bash")} is missing, the first step of {refJob} was not run.");
                    continue;
                }

                foreach (var value in refAccepted.Concat(refRejected))
                {
                    var environment = new Dictionary<string, string>
                    {
                        ["REF_INPUT"] = value, ["TAG"] = "v0.1.0", ["UPLOAD"] = "false", ["UPDATE_KEY_ID"] = "release-1", ["GITHUB_ENV"] = Path.Combine(scriptDir, "env.txt").Replace('\\', '/'),
                    };
                    var (exit, _) = powerShell
                        ? RunScript(shell, ["-NoProfile", "-NonInteractive", "-File", scriptFile], environment)
                        : RunScript(shell, [scriptFile], environment);
                    Expect($"release.yml: first step of {refJob} with ref '{value}' {(RefInputRule.Accepts(value) ? "passes" : "aborts")}",
                        exit == 0, RefInputRule.Accepts(value));
                }
            }
            finally
            {
                Directory.Delete(scriptDir, true);
            }
        }

        foreach (var buildJob in new[] { "windows", "linux" })
        {
            Expect($"release.yml: job {buildJob} waits for preflight", Job(buildJob).Contains("    needs: preflight\n", StringComparison.Ordinal), true);
        }

        var job = Job("manifest");
        Expect("release.yml: job manifest present", job.Length > 0, true);
        Expect("release.yml: job manifest waits for preflight and both build jobs", job.Contains("    needs: [preflight, windows, linux]\n", StringComparison.Ordinal), true);
        Expect("release.yml: job manifest runs only after both jobs succeeded and only when uploading", job.Contains(
            "    if: ${{ !cancelled() && needs.windows.result == 'success' && needs.linux.result == 'success' && inputs.upload }}\n",
            StringComparison.Ordinal), true);
        // The only job that sees the signing key runs on a fresh GitHub-hosted runner, behind the environment.
        Expect("release.yml: job manifest on the hosted runner ubuntu-24.04", job.Contains("    runs-on: ubuntu-24.04\n", StringComparison.Ordinal), true);
        Expect("release.yml: job manifest names no self-hosted label", IsSelfHosted(Regex.Match(job, @"(?m)^    runs-on:.*$").Value), false);
        Expect("release.yml: job manifest with environment: release-signing", job.Contains("    environment: release-signing\n", StringComparison.Ordinal), true);
        foreach (var buildJob in new[] { "windows", "linux" })
        {
            var buildSteps = StepsOf(Job(buildJob));
            var uploadAt = buildSteps.FindIndex(x => Name(x) == "Upload to the release");
            var reportAt = buildSteps.FindIndex(x => Name(x) == "Report the SHA-256 of the checksum file");
            var report = reportAt >= 0 ? buildSteps[reportAt] : string.Empty;
            Expect($"release.yml: Job {buildJob}: step 'Report the SHA-256' directly after the upload", uploadAt >= 0 && reportAt == uploadAt + 1, true);
            Expect($"release.yml: Job {buildJob}: report step (id sums, only when uploading, hashes the uploaded file, writes sums_sha256 into GITHUB_OUTPUT)",
                report.Contains("        id: sums\n", StringComparison.Ordinal) && report.Contains("        if: env.UPLOAD == 'true'\n", StringComparison.Ordinal) &&
                report.Contains($"out/release/SHA256SUMS-{buildJob}.txt", StringComparison.Ordinal) &&
                report.Contains("sums_sha256=$hash", StringComparison.Ordinal) && report.Contains("GITHUB_OUTPUT", StringComparison.Ordinal) &&
                report.Contains("[0-9a-f]{64}", StringComparison.Ordinal), true);
            Expect($"release.yml: Job {buildJob} outputs the SHA-256 of its checksum file (outputs.sums_sha256)",
                Job(buildJob).Contains("    outputs:\n      sums_sha256: ${{ steps.sums.outputs.sums_sha256 }}\n", StringComparison.Ordinal), true);
        }
        Expect("release.yml: job manifest may upload (contents: write)", Regex.IsMatch(job, @"    permissions:\n      contents: write\n"), true);

        var steps = StepsOf(job);
        int Index(string name) => steps.FindIndex(s => Name(s) == name);

        var manifestOnBranch = Index(onBranchName);
        ExpectOnBranchStep(manifestOnBranch >= 0 ? steps[manifestOnBranch] : string.Empty, "manifest");
        Expect("release.yml: manifest checks the tag directly after the tag rule", manifestOnBranch >= 1 && Name(steps[manifestOnBranch - 1]) == "Check the tag and derive the version", true);
        var sameSha = Index("Compare the verified commit with preflight");
        var sameShaStep = sameSha >= 0 ? steps[sameSha] : string.Empty;
        Expect("release.yml: manifest compares its verified commit id with that of preflight, directly after it, before the checkout (fails closed, through the environment)",
            sameSha == manifestOnBranch + 1 && sameSha < steps.FindIndex(x => x.Contains("actions/checkout@", StringComparison.Ordinal)) &&
            sameShaStep.Contains("OWN_SHA: ${{ steps.ontag.outputs.sha }}", StringComparison.Ordinal) &&
            sameShaStep.Contains("PREFLIGHT_SHA: ${{ needs.preflight.outputs.tag_sha }}", StringComparison.Ordinal) &&
            sameShaStep.Contains("[0-9a-f]{40}", StringComparison.Ordinal) && sameShaStep.Contains(@"""$OWN_SHA"" == ""$PREFLIGHT_SHA""", StringComparison.Ordinal) &&
            sameShaStep.Contains("exit 1", StringComparison.Ordinal) &&
            !sameShaStep[sameShaStep.IndexOf("\n        run: |\n", StringComparison.Ordinal)..].Contains("${{", StringComparison.Ordinal), true);
        Expect("release.yml: manifest checks the tag before every checkout and before every step that runs code of the tag",
            manifestOnBranch >= 0 && steps.Take(manifestOnBranch + 1).All(x => !x.Contains("actions/checkout@", StringComparison.Ordinal) && !Regex.IsMatch(x, @"dotnet (run|build|test|project)|package\.cs")), true);

        Expect("release.yml: exactly one place names a secret", Regex.Count(yml, @"secrets\."), 1);
        var sign = Index("Sign the update manifest");
        Expect("release.yml: step 'Sign the update manifest' present", sign >= 0, true);
        var signStep = sign >= 0 ? steps[sign] : string.Empty;
        Expect("release.yml: the secret only in the signing step (env of the step)", Regex.IsMatch(signStep,
            @"\n        env:\n(?:          \S.*\n)*          RAFFICA_UPDATE_SIGNING_KEY: \$\{\{ secrets\.RAFFICA_UPDATE_SIGNING_KEY \}\}\n"), true);
        Expect("release.yml: the signing step starts no action", signStep.Contains("uses:", StringComparison.Ordinal), false);
        Expect("release.yml: the signing step runs only the previously built update-sign (one line, key from the environment)", Regex.IsMatch(signStep,
            @"\n        run: dotnet ""\$PACKAGE_TOOL"" update-sign --manifest \S+ --key-env RAFFICA_UPDATE_SIGNING_KEY --id ""\$UPDATE_KEY_ID"" --out \S+\n*$"), true);
        Expect("release.yml: the signing step builds nothing (no dotnet run/build/publish, no package.cs)",
            Regex.IsMatch(signStep, @"dotnet (run|build|publish)|package\.cs"), false);

        // The tool that sees the secret is built from the tag's checkout into a folder of this job, wiped first, as a
        // converted project (own obj and bin, no shared script cache), without build servers or directory imports.
        var buildTool = Index("Build the packaging tool for signing");
        var buildToolStep = buildTool >= 0 ? steps[buildTool] : string.Empty;
        Expect("release.yml: a separate step builds the tool before signing", buildTool >= 0 && buildTool < sign, true);
        Expect("release.yml: the tool build without a secret", buildToolStep.Contains("secrets.", StringComparison.Ordinal), false);
        Expect("release.yml: the tool is built into a freshly emptied folder of the job", buildToolStep.Contains(@"rm -rf ""$RUNNER_TEMP/package-tool""", StringComparison.Ordinal) &&
            buildToolStep.Contains(@"dotnet project convert tools/package.cs -o ""$RUNNER_TEMP/package-tool/src""", StringComparison.Ordinal), true);
        Expect("release.yml: the tool is built without a build server and without Directory imports", Regex.IsMatch(buildToolStep,
            @"dotnet build ""\$RUNNER_TEMP/package-tool/src"" -c Release -o ""\$RUNNER_TEMP/package-tool/bin"" --disable-build-servers -noAutoResponse -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:ImportDirectoryPackagesProps=false"), true);
        Expect("release.yml: PACKAGE_TOOL points to the freshly built folder",
            buildToolStep.Contains(@"echo ""PACKAGE_TOOL=$RUNNER_TEMP/package-tool/bin/package.dll"" >> ""$GITHUB_ENV""", StringComparison.Ordinal), true);
        Expect("release.yml: GH_TOKEN only in the env of steps", Regex.Count(yml, @"(?m)^          GH_TOKEN: \$\{\{ github\.token \}\}$"), Regex.Count(yml, "GH_TOKEN:"));
        Expect("release.yml: every step with GH_TOKEN sets GH_REPO", Regex.Count(yml, @"GH_REPO: \$\{\{ github\.repository \}\}"), Regex.Count(yml, "GH_TOKEN:"));
        Expect("release.yml: every checkout without stored credentials", Regex.Count(yml, "persist-credentials: false"), Regex.Count(yml, "uses: actions/checkout@"));

        var test = Index("Check that the release key is not the placeholder");
        var download = Index("Download the checksums and the file sizes");
        var sumsCheck = Index("Verify the checksum files against the build jobs");
        var build = Index("Build the update manifest");
        var verify = Index("Verify the signature with the keys in UpdateTrust.cs");
        var upload = Index("Upload the manifest to the release");
        Expect("release.yml: order placeholder test, download, sum comparison, build, signature, verification, upload",
            test >= 0 && test < download && download < sumsCheck && sumsCheck < build && build < sign && sign < verify && verify < upload, true);
        var sumsStep = sumsCheck >= 0 ? steps[sumsCheck] : string.Empty;
        Expect("release.yml: the sum comparison comes after the tool build (uses PACKAGE_TOOL)", buildTool >= 0 && buildTool < sumsCheck, true);
        Expect("release.yml: the sum comparison takes the hashes of the build jobs through the environment (no ${{ }} in the command line)",
            Regex.IsMatch(sumsStep, @"\n        env:\n(?:          \S.*\n)*          WINDOWS_SUMS_SHA256: \$\{\{ needs\.windows\.outputs\.sums_sha256 \}\}\n") &&
            Regex.IsMatch(sumsStep, @"\n        env:\n(?:          \S.*\n)*          LINUX_SUMS_SHA256: \$\{\{ needs\.linux\.outputs\.sums_sha256 \}\}\n") &&
            sumsStep.Contains("\n        run: |\n", StringComparison.Ordinal) &&
            !sumsStep[sumsStep.IndexOf("\n        run: |\n", StringComparison.Ordinal)..].Contains("${{", StringComparison.Ordinal), true);
        Expect("release.yml: the sum comparison checks both downloaded files with sums-verify and aborts on an error",
            sumsStep.Contains("set -euo pipefail", StringComparison.Ordinal) &&
            sumsStep.Contains(@"sums-verify --file out/manifest/SHA256SUMS-windows.txt --sha256 ""$WINDOWS_SUMS_SHA256""", StringComparison.Ordinal) &&
            sumsStep.Contains(@"sums-verify --file out/manifest/SHA256SUMS-linux.txt --sha256 ""$LINUX_SUMS_SHA256""", StringComparison.Ordinal), true);
        Expect("release.yml: the sum comparison without a secret and without an action", sumsStep.Contains("secrets.", StringComparison.Ordinal) || sumsStep.Contains("uses:", StringComparison.Ordinal), false);
        Expect("release.yml: the placeholder test is armed (RAFFICA_REQUIRE_RELEASE_KEY)", test >= 0 &&
            steps[test].Contains("RAFFICA_REQUIRE_RELEASE_KEY: '1'", StringComparison.Ordinal) &&
            steps[test].Contains("ReleaseKeysAreNotThePlaceholderWhenReleasing", StringComparison.Ordinal), true);
        Expect("release.yml: verification with the keys of the program", verify >= 0 &&
            steps[verify].Contains("update-verify --manifest out/manifest/update-manifest.json --sig out/manifest/update-manifest.json.sig --keys-from-source", StringComparison.Ordinal), true);
        Expect("release.yml: manifest and signature are uploaded with --clobber", upload >= 0 &&
            steps[upload].Contains(@"gh release upload ""$TAG"" out/manifest/update-manifest.json out/manifest/update-manifest.json.sig --clobber", StringComparison.Ordinal), true);

        var keyId = Regex.Match(job, @"(?m)^      UPDATE_KEY_ID: (?<id>[A-Za-z0-9._-]+)$").Groups["id"].Value;
        Expect("release.yml: job manifest names the key id (UPDATE_KEY_ID)", keyId.Length > 0, true);
        var trustText = File.Exists(TrustFile) ? File.ReadAllText(TrustFile) : string.Empty;
        var shippedIds = Regex.Matches(trustText, @"new UpdateKey\(\s*""(?<id>[^""]+)""").Select(m => m.Groups["id"].Value).ToList();
        if (shippedIds.Count > 0)
        {
            Expect($"release.yml: UPDATE_KEY_ID {keyId} is in UpdateTrust.cs", shippedIds.Contains(keyId), true);
        }

        foreach (var (file, text) in new[] { (workflow, yml), (container, File.ReadAllText(container)) })
        {
            Expect($"release.yml: {file} never sets RafficaUpdateTestBuild",
                Regex.IsMatch(text, "RafficaUpdateTest|RAFFICA_UPDATE_TESTBUILD", RegexOptions.IgnoreCase), false);
        }
    }

    // The test build of the updater (packaging/README.md, "Manual update check") may only come from an explicit -p: on the
    // command line. MSBuild turns environment variables into properties (names ignore case), so a variable inherited by
    // a release job would otherwise switch it on: Directory.Build.props neutralizes such values and its guard
    // (CheckRafficaUpdateTestBuildSource) fails the build. Runs dotnet msbuild on the Core project in child processes
    // with an environment cleaned of the three names; nothing is built (only the guard target or an evaluation).
    private static void UpdateTestBuildGuardParts()
    {
        const string project = "src/Raffica.Core/Raffica.Core.csproj";
        const string guard = "-t:CheckRafficaUpdateTestBuildSource";
        if (!File.Exists(project))
        {
            Expect("Test-build guard: Core project present", false, true);
            return;
        }

        // A valid public P-256 key (the placeholder of UpdateTrust.cs; its private half was discarded).
        const string key = "-p:RafficaUpdateTestKey=" + TrustSource.PlaceholderKey;
        string[] names = ["RafficaUpdateTestBuild", "RafficaUpdateTestPort", "RafficaUpdateTestKey"];
        (int Code, string Output) Msbuild(Dictionary<string, string> environment, params string[] arguments)
        {
            var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in (string[])["msbuild", project, "-nologo", "-nodeReuse:false", "-v:m", .. arguments])
            {
                psi.ArgumentList.Add(argument);
            }

            foreach (var name in psi.Environment.Keys.Where(k => names.Any(n => string.Equals(n, k, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                psi.Environment.Remove(name);
            }

            foreach (var (name, value) in environment)
            {
                psi.Environment[name] = value;
            }

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, stdout.Result + stderr.Result);
        }

        var none = new Dictionary<string, string>();
        Expect("Test-build guard: passes without a property and a variable", Msbuild(none, guard).Code, 0);
        Expect("Test-build guard: -p: on the command line is allowed (also with -c Release)",
            Msbuild(none, guard, "-p:Configuration=Release", "-p:RafficaUpdateTestBuild=true", "-p:RafficaUpdateTestPort=8765", key).Code, 0);

        foreach (var (name, value) in new[]
        {
            ("RafficaUpdateTestBuild", "True"), ("RafficaUpdateTestPort", "8765"), ("RafficaUpdateTestKey", TrustSource.PlaceholderKey),
            ("RAFFICAUPDATETESTBUILD", "true"),
        })
        {
            var (code, output) = Msbuild(new() { [name] = value }, guard, "-p:Configuration=Release");
            Expect($"Test-build guard: environment variable {name} aborts the build", code != 0 && output.Contains("environment variable", StringComparison.Ordinal), true);
        }

        // Even with -p: on the command line, an environment variable of the same name is refused.
        Expect("Test-build guard: an environment variable next to -p: aborts too",
            Msbuild(new() { ["RafficaUpdateTestBuild"] = "true" }, guard, "-p:RafficaUpdateTestBuild=true", "-p:RafficaUpdateTestPort=8765", key).Code != 0, true);

        // The evaluation itself never switches: an environment variable leaves the release trust in place.
        var all = new Dictionary<string, string>
        {
            ["RafficaUpdateTestBuild"] = "True",
            ["RafficaUpdateTestPort"] = "8765",
            ["RafficaUpdateTestKey"] = TrustSource.PlaceholderKey,
        };
        var evaluated = Msbuild(all, "-p:Configuration=Release", "-getProperty:DefineConstants", "-getProperty:RafficaUpdateTestBuild", "-getItem:Compile", "-getItem:AssemblyMetadata");
        Expect("Test-build guard: with environment variables no constant RAFFICA_UPDATE_TESTBUILD",
            evaluated.Code == 0 && !evaluated.Output.Contains("RAFFICA_UPDATE_TESTBUILD", StringComparison.Ordinal), true);
        Expect("Test-build guard: with environment variables UpdateTrust.cs stays, no UpdateTrust.TestBuild.cs",
            evaluated.Output.Contains("UpdateTrust.cs", StringComparison.Ordinal) && !evaluated.Output.Contains("UpdateTrust.TestBuild.cs", StringComparison.Ordinal), true);
        Expect("Test-build guard: with environment variables no metadata of the test build",
            evaluated.Output.Contains("RafficaUpdateTestKey", StringComparison.Ordinal), false);
        var explicitBuild = Msbuild(none, "-p:RafficaUpdateTestBuild=true", "-p:RafficaUpdateTestPort=8765", key, "-getProperty:DefineConstants", "-getItem:Compile");
        Expect("Test-build guard: with -p: the test build applies (control)",
            explicitBuild.Output.Contains("RAFFICA_UPDATE_TESTBUILD", StringComparison.Ordinal) && explicitBuild.Output.Contains("UpdateTrust.TestBuild.cs", StringComparison.Ordinal), true);
    }

    // Runs a program with the given environment added; (-1, "") when it is not installed.
    private static (int Code, string Output) RunScript(string file, string[] arguments, Dictionary<string, string> environment)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }

            foreach (var (name, value) in environment)
            {
                psi.Environment[name] = value;
            }

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, string.Empty);
        }
    }

    private static int Exec(string file, string arguments)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1;
        }
    }

    // macos-app: the pure parts run everywhere, the real run (sips, iconutil, codesign) only on macOS.
    private static void MacAppParts()
    {
        var plist = System.Xml.Linq.XDocument.Parse(MacApp.InfoPlist("0.1.0", "15.0"));
        var dict = plist.Root!.Element("dict")!;
        var values = new Dictionary<string, System.Xml.Linq.XElement>(StringComparer.Ordinal);
        var children = dict.Elements().ToList();
        for (var i = 0; i + 1 < children.Count; i += 2)
        {
            values[children[i].Value] = children[i + 1];
        }

        string Text(string key) => values.TryGetValue(key, out var e) ? e.Value : "(missing)";
        Expect("macos-app: bundle id", Text("CFBundleIdentifier"), "io.github.benjo-code.raffica.pdf");
        Expect("macos-app: executable", Text("CFBundleExecutable"), "RafficaPdf");
        Expect("macos-app: name", Text("CFBundleName"), "Raffica PDF");
        Expect("macos-app: display name", Text("CFBundleDisplayName"), "Raffica PDF");
        Expect("macos-app: package type", Text("CFBundlePackageType"), "APPL");
        Expect("macos-app: icon file", Text("CFBundleIconFile"), "RafficaPdf");
        Expect("macos-app: category", Text("LSApplicationCategoryType"), "public.app-category.productivity");
        Expect("macos-app: short version", Text("CFBundleShortVersionString"), "0.1.0");
        Expect("macos-app: bundle version", Text("CFBundleVersion"), "0.1.0");
        Expect("macos-app: minimum system version", Text("LSMinimumSystemVersion"), "15.0");
        Expect("macos-app: NSHighResolutionCapable is true", values.TryGetValue("NSHighResolutionCapable", out var hi) ? hi.Name.LocalName : "(missing)", "true");
        var types = values.TryGetValue("CFBundleDocumentTypes", out var docs) ? docs.Elements("dict").ToList() : [];
        Expect("macos-app: one document type", types.Count, 1);
        var typeText = types.Count == 1 ? string.Join('|', types[0].Descendants().Select(e => e.Value)) : "";
        Expect("macos-app: document type is com.adobe.pdf, Editor, Alternate",
            typeText.Contains("com.adobe.pdf", StringComparison.Ordinal) && typeText.Contains("Editor", StringComparison.Ordinal) && typeText.Contains("Alternate", StringComparison.Ordinal), true);

        var versionsFile = Path.Combine("packaging", "native", "versions.txt");
        Expect("macos-app: versions.txt present", File.Exists(versionsFile), true);
        Expect("macos-app: minimum system version from the real versions.txt",
            File.Exists(versionsFile) ? MacApp.MinSystemVersion(File.ReadAllText(versionsFile)) : "(missing)", "15.0");
        Expect("macos-app: minimum system version, last line without a newline and with CR",
            MacApp.MinSystemVersion("A=1\r\nMACOS_DEPLOYMENT_TARGET=16.2\r"), "16.2");
        Expect("macos-app: a text without the key throws", ThrowsIo(() => MacApp.MinSystemVersion("A=1\n")), true);
        Expect("macos-app: an empty value throws", ThrowsIo(() => MacApp.MinSystemVersion("MACOS_DEPLOYMENT_TARGET=\n")), true);

        Expect("macos-app: arch of osx-arm64", MacApp.Arch("osx-arm64"), "arm64");
        Expect("macos-app: arch of osx-x64", MacApp.Arch("osx-x64"), "x64");
        Expect("macos-app: native library name", Layout.NativeLibrary("osx-arm64") + "|" + Layout.NativeLibrary("osx-x64"), "libpdfium.dylib|libpdfium.dylib");
        Expect("macos-app: native files of osx-x64", string.Join(',', NativeFiles.For("osx-x64")),
            "libtesseract.dylib,tessdata/eng.traineddata,tessdata/deu.traineddata");
        Expect("macos-app: layout stays Windows and Linux only", Cli.Run(["layout", "--rid", "osx-x64", "--app", "a", "--cli", "b", "--out", "c"]), 2);

        string[] Args(string rid, string version) =>
            ["macos-app", "--rid", rid, "--app", "app-does-not-exist", "--cli", "cli-does-not-exist", "--out", "out-does-not-exist", "--version", version];
        Expect("macos-app: a prerelease version returns 2", Cli.Run(Args("osx-arm64", "0.2.0-rc.1")), 2);
        Expect("macos-app: a version with a leading v returns 2", Cli.Run(Args("osx-arm64", "v0.2.0")), 2);
        Expect("macos-app: an unknown RID returns 2", Cli.Run(Args("linux-x64", "0.1.0")), 2);
        Expect("macos-app: a missing option returns 2", Cli.Run(["macos-app", "--rid", "osx-x64"]), 2);
        if (!OperatingSystem.IsMacOS())
        {
            Expect("macos-app: anywhere but macOS returns 2", Cli.Run(Args("osx-arm64", "0.1.0")), 2);
            Console.WriteLine("selftest: skipped macos-app real run, reason: macOS: needs sips, iconutil and codesign (proven by the CI job build-macos).");
            return;
        }

        MacOsRealRun();
    }

    private static void MacOsRealRun()
    {
        var dir = Path.Combine(Path.GetTempPath(), "raffica-macapp-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = Path.Combine(dir, "app");
            var cli = Path.Combine(dir, "cli");
            var native = Path.Combine(dir, "native");
            var otherCli = Path.Combine(dir, "cli-other");
            var output = Path.Combine(dir, "out folder");
            foreach (var d in new[] { app, cli, otherCli, Path.Combine(native, "tessdata") })
            {
                Directory.CreateDirectory(d);
            }

            const string Template = "/usr/bin/true";
            foreach (var (folder, name) in new[] { (app, "RafficaPdf"), (app, "libpdfium.dylib"), (cli, "raffica"), (cli, "libpdfium.dylib"), (otherCli, "raffica"), (otherCli, "libpdfium.dylib") })
            {
                File.Copy(Template, Path.Combine(folder, name));
            }

            File.AppendAllText(Path.Combine(otherCli, "libpdfium.dylib"), "different");
            File.WriteAllText(Path.Combine(app, "RafficaPdf.pdb"), "symbols");
            File.Copy(Template, Path.Combine(native, "libtesseract.dylib"));
            File.WriteAllText(Path.Combine(native, "tessdata", "eng.traineddata"), "eng");
            File.WriteAllText(Path.Combine(native, "tessdata", "deu.traineddata"), "deu");

            string[] Args(string cliDir, string? nativeDir) =>
            [
                "macos-app", "--rid", "osx-arm64", "--app", app, "--cli", cliDir, "--out", output, "--version", "0.1.0",
                .. nativeDir is null ? Array.Empty<string>() : ["--native", nativeDir],
            ];

            Expect("macos-app run: a CLI with another libpdfium.dylib exits 1", Cli.Run(Args(otherCli, native)), 1);
            Expect("macos-app run: nothing is left after the refusal", Directory.Exists(Path.Combine(output, MacApp.BundleName)), false);
            Expect("macos-app run: exit 0", Cli.Run(Args(cli, native)), 0);
            var bundle = Path.Combine(output, MacApp.BundleName);
            var macOs = Path.Combine(bundle, "Contents", "MacOS");
            Expect("macos-app run: Info.plist", File.Exists(Path.Combine(bundle, "Contents", "Info.plist")), true);
            Expect("macos-app run: icns", File.Exists(Path.Combine(bundle, "Contents", "Resources", "RafficaPdf.icns")), true);
            Expect("macos-app run: LICENSE", File.Exists(Path.Combine(bundle, "Contents", "Resources", "LICENSE")), true);
            Expect("macos-app run: THIRD-PARTY-NOTICES.txt", File.Exists(Path.Combine(bundle, "Contents", "Resources", "THIRD-PARTY-NOTICES.txt")), true);
            Expect("macos-app run: no .pdb in the bundle", File.Exists(Path.Combine(macOs, "RafficaPdf.pdb")), false);
            Expect("macos-app run: OCR library and tessdata", File.Exists(Path.Combine(macOs, "libtesseract.dylib")) && File.Exists(Path.Combine(macOs, "tessdata", "eng.traineddata")), true);
            Expect("macos-app run: RafficaPdf is 0755", File.GetUnixFileMode(Path.Combine(macOs, "RafficaPdf")), (UnixFileMode)0x1ED);
            Expect("macos-app run: raffica is 0755", File.GetUnixFileMode(Path.Combine(macOs, "raffica")), (UnixFileMode)0x1ED);
            Expect("macos-app run: the library is 0644", File.GetUnixFileMode(Path.Combine(macOs, "libpdfium.dylib")), (UnixFileMode)0x1A4);
            Expect("macos-app run: tessdata is 0644", File.GetUnixFileMode(Path.Combine(macOs, "tessdata", "eng.traineddata")), (UnixFileMode)0x1A4);
            Expect("macos-app run: plutil -lint", Tool.Run("plutil", ["-lint", Path.Combine(bundle, "Contents", "Info.plist")], out _), 0);
            Expect("macos-app run: codesign --verify --deep --strict", Tool.Run("codesign", ["--verify", "--deep", "--strict", bundle], out var verify), 0);
            if (verify.Length > 0)
            {
                Console.Error.WriteLine(verify);
            }

            Expect("macos-app run: an existing bundle is refused", Cli.Run(Args(cli, native)), 1);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // dmg and dmg-smoke: the pure parts run everywhere, the real run (hdiutil, ditto, codesign, lipo) only on macOS.
    private static void DmgParts()
    {
        Expect("dmg: file name of osx-arm64", Dmg.FileName("0.1.0", "osx-arm64"), "RafficaPdf-0.1.0-macos-arm64.dmg");
        Expect("dmg: file name of osx-x64", Dmg.FileName("1.2.3", "osx-x64"), "RafficaPdf-1.2.3-macos-x64.dmg");
        Expect("dmg: another RID throws", ThrowsArgument(() => Dmg.FileName("0.1.0", "linux-x64")), true);

        string[] Args(string rid, string version, string name) =>
            ["dmg", "--app", "app-does-not-exist/Raffica PDF.app", "--rid", rid, "--version", version, "--out", Path.Combine("out-does-not-exist", name)];

        // The argument rules are a pure method that runs before the macOS check; the CLI checks also assert the message, so a
        // refusal by the macOS check (which also returns 2) cannot pass for them.
        const string Arm = "RafficaPdf-0.1.0-macos-arm64.dmg";
        static string SmokeProblem(string rid, string ceilingText, out double ceiling) => DmgSmoke.Problem(rid, ceilingText, out ceiling) ?? "(none)";
        Expect("dmg: valid arguments have no problem", Dmg.Problem("osx-arm64", "0.1.0", Path.Combine("out", Arm)) ?? "(none)", "(none)");
        Expect("dmg: another RID is a problem", Dmg.Problem("linux-x64", "0.1.0", Arm) ?? "(none)", "dmg: unknown RID: linux-x64");
        Expect("dmg: a prerelease version is a problem", Dmg.Problem("osx-arm64", "0.2.0-rc.1", "RafficaPdf-0.2.0-rc.1-macos-arm64.dmg") ?? "(none)",
            "dmg: only a stable version x.y.z is allowed, not: 0.2.0-rc.1");
        Expect("dmg: a wrong out name is a problem", Dmg.Problem("osx-arm64", "0.1.0", "x.dmg") ?? "(none)", $"dmg: the output file must be named {Arm}: x.dmg");
        Expect("dmg: the name of the other architecture is a problem", Dmg.Problem("osx-arm64", "0.1.0", "RafficaPdf-0.1.0-macos-x64.dmg")?.StartsWith("dmg: the output file must be named ", StringComparison.Ordinal) ?? false, true);
        Expect("dmg: another version in the name is a problem", Dmg.Problem("osx-x64", "0.1.0", "RafficaPdf-0.1.1-macos-x64.dmg")?.StartsWith("dmg: the output file must be named ", StringComparison.Ordinal) ?? false, true);
        Expect("dmg-smoke: valid arguments have no problem and the default ceiling is 5", SmokeProblem("osx-x64", "5", out var five) + "|" + five, "(none)|5");
        Expect("dmg-smoke: another RID is a problem", SmokeProblem("linux-x64", "5", out _), "dmg-smoke: unknown RID: linux-x64");
        foreach (var bad in new[] { "0", "-1", "soon", "NaN", "Infinity" })
        {
            Expect($"dmg-smoke: the ceiling \"{bad}\" is a problem", SmokeProblem("osx-x64", bad, out _), $"dmg-smoke: --ceiling-seconds must be a positive number: {bad}");
        }

        (int Code, string Error) CliError(string[] args)
        {
            var original = Console.Error;
            using var captured = new StringWriter();
            Console.SetError(captured);
            try
            {
                return (Cli.Run(args), captured.ToString());
            }
            finally
            {
                Console.SetError(original);
            }
        }

        var (wrongCode, wrongError) = CliError(Args("osx-arm64", "0.1.0", "x.dmg"));
        Expect("dmg: a wrong out name returns 2 with the name message", wrongCode + "|" + wrongError.Contains("the output file must be named", StringComparison.Ordinal), "2|True");
        var (preCode, preError) = CliError(Args("osx-arm64", "0.2.0-rc.1", "RafficaPdf-0.2.0-rc.1-macos-arm64.dmg"));
        Expect("dmg: a prerelease version returns 2 with the version message", preCode + "|" + preError.Contains("only a stable version", StringComparison.Ordinal), "2|True");
        var (ridCode, ridError) = CliError(Args("linux-x64", "0.1.0", Arm));
        Expect("dmg: another RID returns 2 with the RID message", ridCode + "|" + ridError.Contains("unknown RID", StringComparison.Ordinal), "2|True");
        var (ceilCode, ceilError) = CliError(["dmg-smoke", "--dmg", "x.dmg", "--rid", "osx-x64", "--ceiling-seconds", "0"]);
        Expect("dmg-smoke: a ceiling of 0 returns 2 with the ceiling message", ceilCode + "|" + ceilError.Contains("must be a positive number", StringComparison.Ordinal), "2|True");
        Expect("dmg: a missing option returns 2", Cli.Run(["dmg", "--rid", "osx-x64"]), 2);
        Expect("dmg-smoke: a missing option returns 2", Cli.Run(["dmg-smoke", "--rid", "osx-x64"]), 2);
        if (!OperatingSystem.IsMacOS())
        {
            Expect("dmg: anywhere but macOS returns 2", Cli.Run(Args("osx-arm64", "0.1.0", "RafficaPdf-0.1.0-macos-arm64.dmg")), 2);
            Expect("dmg-smoke: anywhere but macOS returns 2", Cli.Run(["dmg-smoke", "--dmg", "x.dmg", "--rid", "osx-x64"]), 2);
            Console.WriteLine("selftest: skipped dmg real run (ADmgOfAFakeBundleHoldsTheAppAndTheLink, TheSmokeFailsForABinaryThatDoesNotPrintTheString), reason: macOS: needs hdiutil, ditto, codesign and lipo (proven by the CI job build-macos).");
            return;
        }

        DmgRealRun();
    }

    private static bool ThrowsArgument(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void DmgRealRun()
    {
        var dir = Path.Combine(Path.GetTempPath(), "raffica-dmg-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);

            // The fake programs are single-architecture slices of /usr/bin/true (a system binary is universal, and the smoke refuses a
            // universal RafficaPdf or raffica). Intel is preferred because every macOS 15 /usr/bin/true has that slice (the other is
            // arm64e, which is not arm64); on an Apple silicon host the Intel fake runs under Rosetta, and without Rosetta it simply
            // prints nothing, which is the failure this test expects anyway.
            Tool.Run("lipo", ["-archs", "/usr/bin/true"], out var trueArchs);
            var slices = trueArchs.Split([' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            var (rid, arch) = slices.Contains("x86_64") ? ("osx-x64", "x86_64") : slices.Contains("arm64") ? ("osx-arm64", "arm64") : (string.Empty, string.Empty);
            Expect($"dmg run: /usr/bin/true has an x86_64 or arm64 slice to thin ({trueArchs.Trim()})", rid.Length > 0, true);
            if (rid.Length == 0)
            {
                return;
            }

            var fake = Path.Combine(dir, "fake-program");
            Expect("dmg run: the fake program is thinned", Tool.Run("lipo", ["/usr/bin/true", "-thin", arch, "-output", fake], out var thinLog), 0);
            if (!File.Exists(fake))
            {
                Console.Error.WriteLine(thinLog);
                return;
            }

            File.SetUnixFileMode(fake, (UnixFileMode)0x1ED);
            var app = Path.Combine(dir, "app");
            var cli = Path.Combine(dir, "cli");
            var bundles = Path.Combine(dir, "bundles");
            foreach (var d in new[] { app, cli })
            {
                Directory.CreateDirectory(d);
            }

            foreach (var (folder, name) in new[] { (app, "RafficaPdf"), (app, "libpdfium.dylib"), (cli, "raffica"), (cli, "libpdfium.dylib") })
            {
                File.Copy(fake, Path.Combine(folder, name));
            }

            Expect("dmg run: the fake bundle is built", Cli.Run(["macos-app", "--rid", rid, "--app", app, "--cli", cli, "--out", bundles, "--version", "0.1.0"]), 0);
            var bundle = Path.Combine(bundles, MacApp.BundleName);
            var images = Path.Combine(dir, "images folder");
            Directory.CreateDirectory(images);
            var image = Path.Combine(images, Dmg.FileName("0.1.0", rid));

            Expect("dmg run: ADmgOfAFakeBundleHoldsTheAppAndTheLink: create", Cli.Run(["dmg", "--app", bundle + "/", "--rid", rid, "--version", "0.1.0", "--out", image]), 0);
            Expect("dmg run: the image exists", File.Exists(image), true);
            var mount = Path.Combine(dir, "mount");
            Directory.CreateDirectory(mount);
            var attach = Tool.Run("hdiutil", ["attach", "-readonly", "-nobrowse", "-noautoopen", "-noverify", "-mountpoint", mount, image], out var attachLog);
            Expect("dmg run: attach", attach, 0);
            if (attach == 0)
            {
                try
                {
                    var names = Directory.EnumerateFileSystemEntries(mount).Select(Path.GetFileName).Where(n => !n!.StartsWith('.')).Order(StringComparer.Ordinal).ToList();
                    Expect("dmg run: the image holds the app and Applications", string.Join('|', names), "Applications|Raffica PDF.app");
                    Expect("dmg run: Applications links to /Applications", new FileInfo(Path.Combine(mount, "Applications")).LinkTarget ?? "(none)", "/Applications");
                    Expect("dmg run: the app in the image is signed",
                        Tool.Run("codesign", ["--verify", "--deep", "--strict", Path.Combine(mount, MacApp.BundleName)], out _), 0);
                }
                finally
                {
                    Expect("dmg run: detach", Tool.Run("hdiutil", ["detach", mount], out _) == 0 || Tool.Run("hdiutil", ["detach", "-force", mount], out _) == 0, true);
                }
            }
            else
            {
                Console.Error.WriteLine(attachLog);
            }

            // TheSmokeFailsForABinaryThatDoesNotPrintTheString: the fake prints nothing, so exactly the two print checks (de and en)
            // fail; attach, listing, link, copy, detach, signature and every architecture must have passed before them.
            var original = Console.Out;
            using var captured = new StringWriter();
            Console.SetOut(captured);
            int smokeCode;
            try
            {
                smokeCode = Cli.Run(["dmg-smoke", "--dmg", image, "--rid", rid]);
            }
            finally
            {
                Console.SetOut(original);
            }

            var lines = captured.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            bool Passed(string what) => lines.Any(l => l.StartsWith("ok ", StringComparison.Ordinal) && l.Contains(what, StringComparison.Ordinal));
            var failed = lines.Where(l => l.StartsWith("FAILED", StringComparison.Ordinal)).ToList();
            if (smokeCode != 1 || failed.Count != 2)
            {
                Console.Error.WriteLine(captured.ToString());
            }

            Expect("dmg run: TheSmokeFailsForABinaryThatDoesNotPrintTheString: exit 1", smokeCode, 1);
            foreach (var what in new[]
            {
                "hdiutil attach", "the image holds exactly Raffica PDF.app and Applications", "Applications links to /Applications", "app copied out of the image",
                "hdiutil detach", "codesign --verify --deep --strict", "Contents/MacOS holds RafficaPdf and raffica",
                $"RafficaPdf: architecture {arch}", $"raffica: architecture {arch}", $"libpdfium.dylib: architecture {arch}",
            })
            {
                Expect($"dmg run: the smoke passed \"{what}\"", Passed(what), true);
            }

            Expect("dmg run: only the two print-string checks failed", failed.Count == 2 && failed.All(l => l.Contains("RafficaPdf --print-string TabView --lang", StringComparison.Ordinal)), true);
            Expect("dmg run: the smoke ends with its failure count", lines.Contains("dmg-smoke: 2 check(s) failed."), true);
            Tool.Run("hdiutil", ["info"], out var info);
            Expect("dmg run: the smoke detached the image", info.Contains(Path.GetFileName(image), StringComparison.Ordinal), false);
            Expect("dmg run: a missing image returns 1", Cli.Run(["dmg-smoke", "--dmg", Path.Combine(dir, "nope.dmg"), "--rid", rid]), 1);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void Expect<T>(string name, T actual, T expected)
    {
        s_checks++;
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            s_failures++;
            Console.Error.WriteLine($"selftest: {name}: expected {expected}, got {actual}");
        }
    }
}
