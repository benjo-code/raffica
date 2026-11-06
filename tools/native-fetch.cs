// Fetches the OCR library and the language data the OCR tests need.
//
//   dotnet run tools/native-fetch.cs -- --rid <win-x64|linux-x64> --out <folder> [--from <folder>]
//   dotnet run tools/native-fetch.cs -- selftest
//
// Everything is fetched into a staging folder and checked (size and SHA-256 per file) first; nothing is laid down
// unless every file passes. --from takes the files from a folder instead of the network (offline use, selftest).
// The pinned SHA-256 is what is trusted, not the place the files come from.
//
// The Tesseract library comes from the prerelease native-tesseract-5.5.4-1 of benjo-code/raffica, built by the workflow
// native-tesseract.yml (the tag never starts with "v", so it is no release of any update check). The repository is private,
// so a release asset is read through the GitHub API (releases/tags/<tag>, then assets[].url with
// "Accept: application/octet-stream"); the token comes from GITHUB_TOKEN or GH_TOKEN when one is set. Without a token the same
// calls are made anonymously (that works once the repository is public). The token goes to api.github.com only, never to
// the tessdata host.
using System.Runtime.CompilerServices;
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
    public static readonly string Placeholder = new('0', 64);

    private const string TesseractRelease = "https://github.com/benjo-code/raffica/releases/download/native-tesseract-5.5.4-1/";

    public static readonly PinnedFile[] Libraries =
    [
        // Size and SHA-256 from the job summary of the workflow native-tesseract.yml (run 42198001684); they equal the release digests.
        new("win-x64", "tesseract.dll", "tesseract.dll", 3684864, "24fc074bfefba19dfe566b54604fcf1fcbdeef85080acb461d866bb0ebecd86c", TesseractRelease + "tesseract.dll"),
        new("linux-x64", "libtesseract.so", "libtesseract.so", 6948376, "b3d19bbabb63f8af6e13728aa74940b8c6b1138cd078d9fef346adb5ac75adc4", TesseractRelease + "libtesseract.so"),
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
    public static int Fetch(IReadOnlyList<PinnedFile> files, string output, string? from, HttpMessageHandler? handler = null)
    {
        if (files.FirstOrDefault(f => f.Size == 0 && f.Sha256 == Pins.Placeholder) is { } unpinned)
        {
            Console.Error.WriteLine($"native-fetch: {unpinned.Asset} has a placeholder pin (size 0, SHA-256 all zero): the release is not built or pinned yet; nothing laid down");
            return 1;
        }

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
                    File.WriteAllBytes(staged, Download(file.Url, TokenFromEnvironment(), handler));
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
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            var hint = e is HttpRequestException && TokenFromEnvironment() is null
                ? " (no GITHUB_TOKEN or GH_TOKEN is set; a private repository needs one, for example GH_TOKEN=$(gh auth token))"
                : "";
            Console.Error.WriteLine($"native-fetch: {e.Message}{hint}");
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

    public static string? TokenFromEnvironment() =>
        new[] { "GITHUB_TOKEN", "GH_TOKEN" }.Select(Environment.GetEnvironmentVariable).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

    // https://github.com/<owner>/<repo>/releases/download/<tag>/<asset>
    private static readonly System.Text.RegularExpressions.Regex s_releaseAsset =
        new(@"^https://github\.com/(?<repo>[^/]+/[^/]+)/releases/download/(?<tag>[^/]+)/(?<asset>[^/]+)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // Returns the bytes behind url. A release asset is looked up through the API (the repository may be private); the token, when
    // given, is sent to api.github.com only. Any other URL is fetched as it is, without the token.
    public static byte[] Download(string url, string? token, HttpMessageHandler? handler = null)
    {
        using var client = handler is null ? new HttpClient { Timeout = TimeSpan.FromMinutes(10) } : new HttpClient(handler);
        var match = s_releaseAsset.Match(url);
        if (!match.Success)
        {
            return client.GetByteArrayAsync(url).GetAwaiter().GetResult();
        }

        var repo = match.Groups["repo"].Value;
        var asset = match.Groups["asset"].Value;
        using var lookup = Api(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/tags/{match.Groups["tag"].Value}", "application/vnd.github+json", token);
        using var lookupResponse = client.SendAsync(lookup).GetAwaiter().GetResult();
        lookupResponse.EnsureSuccessStatusCode();
        using var release = System.Text.Json.JsonDocument.Parse(lookupResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        string? assetUrl = null;
        if (release.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var entry in assets.EnumerateArray())
            {
                if (entry.TryGetProperty("name", out var name) && name.GetString() == asset && entry.TryGetProperty("url", out var link))
                {
                    assetUrl = link.GetString();
                    break;
                }
            }
        }

        if (assetUrl is null)
        {
            throw new InvalidOperationException($"the release has no asset {asset}");
        }

        using var download = Api(HttpMethod.Get, assetUrl, "application/octet-stream", token);
        using var response = client.SendAsync(download).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    private static HttpRequestMessage Api(HttpMethod method, string url, string accept, string? token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.UserAgent.ParseAdd("raffica-native-fetch");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return request;
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
            Expect("no pin is a placeholder (size 0 or an all-zero SHA-256)", Pins.Libraries.Concat(Pins.Tessdata).All(f => f.Size > 0 && f.Sha256 != Pins.Placeholder), true);
            Expect("every pin is a lower-case SHA-256", Pins.Libraries.Concat(Pins.Tessdata).All(f => f.Sha256.Length == 64 && f.Sha256.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'))), true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        AssetLookupUsesTheApiWithToken();
        WrongHashLaysNothingDown();
        NoPredecessorNameInTheSource();

        Console.WriteLine(s_failures == 0 ? "selftest passed" : $"selftest: {s_failures} failure(s)");
        return s_failures == 0 ? 0 : 1;
    }

    private const string ReleaseUrl = "https://github.com/benjo-code/raffica/releases/download/native-tesseract-5.5.4-1/tesseract.dll";

    // Serves the release lookup and the asset download like the GitHub API and records what it was asked.
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsoluteUri;
            if (path == "https://api.github.com/repos/benjo-code/raffica/releases/tags/native-tesseract-5.5.4-1")
            {
                const string Json = """{"assets":[{"name":"other.bin","url":"https://api.github.com/repos/benjo-code/raffica/releases/assets/1"},{"name":"tesseract.dll","url":"https://api.github.com/repos/benjo-code/raffica/releases/assets/2"}]}""";
                return Task.FromResult(new HttpResponseMessage { Content = new StringContent(Json) });
            }

            if (path == "https://api.github.com/repos/benjo-code/raffica/releases/assets/2" && request.Headers.Accept.ToString() == "application/octet-stream")
            {
                return Task.FromResult(new HttpResponseMessage { Content = new ByteArrayContent("dll-bytes"u8.ToArray()) });
            }

            if (path.StartsWith("https://github.com/tesseract-ocr/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage { Content = new ByteArrayContent([]) });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private static void AssetLookupUsesTheApiWithToken()
    {
        var withToken = new FakeGitHub();
        var bytes = Fetcher.Download(ReleaseUrl, "secret-token", withToken);
        Expect("asset lookup: the bytes of the asset are returned", System.Text.Encoding.UTF8.GetString(bytes), "dll-bytes");
        Expect("asset lookup: release lookup, then asset download", string.Join(" ", withToken.Requests.Select(r => r.RequestUri!.AbsolutePath)),
            "/repos/benjo-code/raffica/releases/tags/native-tesseract-5.5.4-1 /repos/benjo-code/raffica/releases/assets/2");
        Expect("asset lookup: the token is sent as Bearer", withToken.Requests.All(r => r.Headers.Authorization?.ToString() == "Bearer secret-token"), true);

        var anonymous = new FakeGitHub();
        Fetcher.Download(ReleaseUrl, null, anonymous);
        Expect("asset lookup: no token, no Authorization header", anonymous.Requests.All(r => r.Headers.Authorization is null), true);

        var other = new FakeGitHub();
        Expect("a URL that is no release asset is fetched as it is, without the token",
            Fetcher.Download("https://github.com/tesseract-ocr/tessdata_fast/raw/4.1.0/eng.traineddata", "secret-token", other).Length, 0);
        Expect("a URL that is no release asset is not sent to the API", other.Requests.Count == 1 && other.Requests[0].Headers.Authorization is null, true);

        Expect("an asset the release lacks is an error", Throws(() => Fetcher.Download(ReleaseUrl.Replace("tesseract.dll", "missing.dll"), null, new FakeGitHub())), true);
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
        {
            return true;
        }
    }

    // Placeholder pins (size 0, all-zero hash) and wrong hashes are refused and lay nothing down.
    private static void WrongHashLaysNothingDown()
    {
        var root = Path.Combine(Path.GetTempPath(), "native-fetch-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.bin"), "alpha");
            var sha = Convert.ToHexStringLower(SHA256.HashData("alpha"u8));
            var wrong = Path.Combine(root, "out-wrong");
            Expect("a wrong hash is refused", Fetcher.Fetch([new("any", "a.bin", "a.bin", 5, new string('1', 64), "x")], wrong, src), 1);
            Expect("a wrong hash lays nothing down", Directory.Exists(wrong) && Directory.GetFileSystemEntries(wrong).Length == 0, true);
            var placeholder = Path.Combine(root, "out-placeholder");
            Expect("a placeholder pin is refused", Fetcher.Fetch([new("any", "a.bin", "a.bin", 0, new string('0', 64), "x")], placeholder, src), 1);
            Expect("a placeholder pin lays nothing down", !Directory.Exists(placeholder) || Directory.GetFileSystemEntries(placeholder).Length == 0, true);
            Expect("the real file with a real pin is still laid down", Fetcher.Fetch([new("any", "a.bin", "a.bin", 5, sha, "x")], Path.Combine(root, "out-ok"), src), 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void NoPredecessorNameInTheSource([CallerFilePath] string path = "")
    {
        var word = "Page" + "wing"; // spelled in two pieces so this test does not trip itself
        Expect("the tool's own source does not name the predecessor", File.ReadAllText(path).Contains(word, StringComparison.OrdinalIgnoreCase), false);
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
