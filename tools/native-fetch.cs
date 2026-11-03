// Fetches the OCR library and the language data the OCR tests need.
//
//   dotnet run tools/native-fetch.cs -- --rid <win-x64|linux-x64> --out <folder> [--from <folder>]
//   dotnet run tools/native-fetch.cs -- selftest
//
// Everything is fetched into a staging folder and checked (size and SHA-256 per file) first; nothing is laid down
// unless every file passes. --from takes the files from a folder instead of the network (offline use, selftest).
// The pinned SHA-256 is what is trusted, not the place the files come from.
//
// The Tesseract library comes from the prerelease native-tesseract-5.5.4-1 of the Pagewing repository (built there by its
// own workflow; the tag never starts with "v", so it is no release of any update check). Raffica does not build it yet.
using System.Security.Cryptography;

if (args is ["selftest"])
{
    return SelfTest.Run();
}

return Cli.Run(args);

// One file of the set: where it comes from and where it goes in the layout.
sealed record PinnedFile(string Rid, string Asset, string Target, long Size, string Sha256, string Url);

static class Pins
{
    private const string TesseractRelease = "https://github.com/benjo-code/Pagewing/releases/download/native-tesseract-5.5.4-1/";

    public static readonly PinnedFile[] Libraries =
    [
        new("win-x64", "tesseract.dll", "tesseract.dll", 3684864, "248de9ef2aeaadea588c0269d45098cc06a334564ec1e08ed0b66c37700b044f", TesseractRelease + "tesseract.dll"),
        new("linux-x64", "libtesseract.so", "libtesseract.so", 6948376, "a1611008fc2dfbf2692bf883ea3d7b1b1412c1310180ba9ca90ef287763997e4", TesseractRelease + "libtesseract.so"),
    ];

    // tessdata_fast 4.1.0 (Apache-2.0).
    public static readonly PinnedFile[] Tessdata =
    [
        new("any", "eng.traineddata", "tessdata/eng.traineddata", 4113088, "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2",
            "https://github.com/tesseract-ocr/tessdata_fast/raw/4.1.0/eng.traineddata"),
        new("any", "deu.traineddata", "tessdata/deu.traineddata", 1525436, "19d219bbb6672c869d20a9636c6816a81eb9a71796cb93ebe0cb1530e2cdb22d",
            "https://github.com/tesseract-ocr/tessdata_fast/raw/4.1.0/deu.traineddata"),
    ];

    public static IReadOnlyList<PinnedFile> For(string rid) => [.. Libraries.Where(f => f.Rid == rid), .. Tessdata];
}

static class Cli
{
    public static int Run(string[] args)
    {
        string? rid = null, output = null, from = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--rid" when i + 1 < args.Length: rid = args[++i]; break;
                case "--out" when i + 1 < args.Length: output = args[++i]; break;
                case "--from" when i + 1 < args.Length: from = args[++i]; break;
                default: return Usage();
            }
        }

        if (rid is null || output is null)
        {
            return Usage();
        }

        if (rid is not ("win-x64" or "linux-x64"))
        {
            Console.Error.WriteLine($"unknown RID: {rid}");
            return 2;
        }

        return Fetcher.Fetch(Pins.For(rid), Path.GetFullPath(output), from is null ? null : Path.GetFullPath(from));
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: native-fetch --rid <win-x64|linux-x64> --out <folder> [--from <folder>]");
        return 2;
    }
}

static class Fetcher
{
    // Returns 0 when every file is laid down, 1 when any file is missing or does not match its pin (then nothing is laid down).
    public static int Fetch(IReadOnlyList<PinnedFile> files, string output, string? from)
    {
        Directory.CreateDirectory(output);
        var stage = Path.Combine(output, ".native-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var file in files)
            {
                var staged = Path.Combine(stage, file.Asset);
                if (from is not null)
                {
                    var source = Path.Combine(from, file.Asset);
                    if (!File.Exists(source))
                    {
                        Console.Error.WriteLine($"native-fetch: {source} is missing");
                        return 1;
                    }

                    File.Copy(source, staged);
                }
                else
                {
                    Console.WriteLine($"native-fetch: fetching {file.Url}");
                    Download(file.Url, staged);
                }

                if (Verify(staged, file.Size, file.Sha256) is { } mismatch)
                {
                    Console.Error.WriteLine($"native-fetch: {file.Asset} rejected: {mismatch}; nothing laid down");
                    return 1;
                }
            }

            foreach (var file in files)
            {
                var target = Path.Combine(output, file.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(Path.Combine(stage, file.Asset), target, overwrite: true);
            }

            Console.WriteLine($"native-fetch: {files.Count} files (SHA-256 checked) in {output}");
            return 0;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"native-fetch: {e.Message}");
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(stage, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // null when the file has the expected size and the pinned SHA-256; otherwise the reason.
    public static string? Verify(string file, long expectedSize, string pinned)
    {
        if (!File.Exists(file))
        {
            return $"file missing: {file}";
        }

        using var stream = File.OpenRead(file);
        if (stream.Length != expectedSize)
        {
            return $"size {stream.Length} instead of {expectedSize} bytes";
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return hash == pinned ? null : $"SHA-256 {hash} instead of {pinned}";
    }

    private static void Download(string url, string target)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var source = response.Content.ReadAsStream();
        using var file = File.Create(target);
        source.CopyTo(file);
    }
}

static class SelfTest
{
    private static int s_failures;

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "native-fetch-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A source folder with files that carry the right content for made-up pins.
            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.bin"), "alpha");
            File.WriteAllText(Path.Combine(src, "b.bin"), "beta");
            string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            PinnedFile[] good = [new("any", "a.bin", "a.bin", 5, Sha("alpha"), "x"), new("any", "b.bin", "sub/b.bin", 4, Sha("beta"), "x")];
            PinnedFile[] bad = [good[0], new("any", "b.bin", "sub/b.bin", 4, Sha("other"), "x")];

            var outGood = Path.Combine(root, "out-good");
            Expect("matching files are laid down", Fetcher.Fetch(good, outGood, src), 0);
            Expect("target layout is kept", File.Exists(Path.Combine(outGood, "sub", "b.bin")) && File.Exists(Path.Combine(outGood, "a.bin")), true);
            Expect("no staging folder is left", Directory.GetDirectories(outGood, ".native-stage-*").Length, 0);

            var outBad = Path.Combine(root, "out-bad");
            Expect("a wrong hash is refused", Fetcher.Fetch(bad, outBad, src), 1);
            Expect("nothing is laid down on a mismatch", File.Exists(Path.Combine(outBad, "a.bin")), false);
            Expect("a missing source file is refused", Fetcher.Fetch(good, Path.Combine(root, "out-missing"), Path.Combine(root, "nope")), 1);
            Expect("an unknown RID gives 2", Cli.Run(["--rid", "mac", "--out", Path.Combine(root, "x")]), 2);
            Expect("missing arguments give 2", Cli.Run(["--rid", "win-x64"]), 2);
            Expect("every pin is a lower-case SHA-256", Pins.Libraries.Concat(Pins.Tessdata).All(f => f.Sha256.Length == 64 && f.Sha256.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'))), true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        Console.WriteLine(s_failures == 0 ? "selftest passed" : $"selftest: {s_failures} failure(s)");
        return s_failures == 0 ? 0 : 1;
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
