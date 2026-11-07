using System.Net;
using System.Text;
using Raffica.Core;
using Raffica.Core.Updates;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class GitHubUpdateSourceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-source-").FullName;
    private readonly List<HttpRequestMessage> _requests = [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the system to clean.
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer, List<HttpRequestMessage> seen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private GitHubUpdateSource Source(Func<HttpRequestMessage, HttpResponseMessage> answer) => new(() => new StubHandler(answer, _requests));

    private static HttpResponseMessage Text(string text, string mediaType = "application/json") =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, mediaType) };

    private static HttpResponseMessage Redirect(string to) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };

    private static string Path_(HttpRequestMessage request) => request.RequestUri!.ToString();

    [Fact]
    public async Task TheClientIsMadeOnTheFirstRequestAndNotBefore()
    {
        var before = GitHubUpdateSource.ClientsCreated;
        using var source = Source(_ => Text("<html></html>", "text/html"));
        Assert.Equal(before, GitHubUpdateSource.ClientsCreated);

        // Creating the shipped source opens nothing either.
        using var shipped = GitHubUpdateSource.Create();
        Assert.Equal(before, GitHubUpdateSource.ClientsCreated);

        _ = await source.FetchManifestAsync(Ct);
        Assert.Equal(before + 1, GitHubUpdateSource.ClientsCreated);
        _ = await source.FetchManifestAsync(Ct);
        Assert.Equal(before + 1, GitHubUpdateSource.ClientsCreated);
    }

    [Fact]
    public async Task TheRequestsCarryTheUserAgentOfTheApp()
    {
        using var source = Source(_ => Text("{}"));

        _ = await source.FetchManifestAsync(Ct);

        var request = Assert.Single(_requests);
        Assert.Equal(UpdateTrust.ReleasesLatestUrl, request.RequestUri!.ToString());
        Assert.Equal("RafficaPdf/" + AppInfo.Version, request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task ManifestAndSignatureAreAskedBelowTheDownloadBaseOfTheTag()
    {
        using var source = Source(request => Path_(request) switch
        {
            var url when url == UpdateTrust.ReleasesLatestUrl => Text("{\"tag_name\":\"v0.2.0\"}"),
            var url when url == UpdateTrust.DownloadBaseUrl + "/v0.2.0/update-manifest.json" => Text("MANIFEST"),
            var url when url == UpdateTrust.DownloadBaseUrl + "/v0.2.0/update-manifest.json.sig" => Text("key:SIGNATURE", "text/plain"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var documents = await source.FetchManifestAsync(Ct);

        Assert.NotNull(documents);
        Assert.Equal("MANIFEST", Encoding.UTF8.GetString(documents.Manifest));
        Assert.Equal("key:SIGNATURE", documents.Signature);
        Assert.Equal(3, _requests.Count);
    }

    [Fact]
    public async Task HtmlFromACaptivePortalAtTheReleaseAddressIsNothingReceived()
    {
        using var source = Source(_ => Text("<html><body>Please sign in</body></html>", "text/html"));

        Assert.Null(await source.FetchManifestAsync(Ct));
        Assert.Single(_requests);
    }

    [Theory]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/x", true)]
    [InlineData("https://release-assets.githubusercontent.com/x", true)]
    [InlineData("https://github.com/benjo-code/raffica/releases/download/v0.2.0/update-manifest.json", true)]
    [InlineData("https://evil.example/update-manifest.json", false)]
    [InlineData("https://githubusercontent.com.evil.example/x", false)]
    [InlineData("http://github.com/x", false)]
    [InlineData("https://user:pw@github.com/x", false)]
    [InlineData("https://github.com:8443/x", false)]
    public async Task OnlyRedirectsToHttpsHostsOfGitHubStorageAreFollowed(string target, bool followed)
    {
        using var source = Source(request => Path_(request) == UpdateTrust.ReleasesLatestUrl ? Redirect(target) : Text("{\"tag_name\":\"v0.2.0\"}"));

        _ = await source.FetchManifestAsync(Ct);

        Assert.Equal(followed, _requests.Any(r => r.RequestUri!.ToString() == target));
    }

    [Fact]
    public async Task AnAnswerOverTheSizeCapIsNothingReceived()
    {
        // Declared too large: refused before a byte is read.
        using var declared = Source(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[GitHubUpdateSource.MaxDocumentBytes + 1]) });
        Assert.Null(await declared.FetchManifestAsync(Ct));

        // Not declared (a stream without a length) and too long: stopped while reading.
        using var undeclared = Source(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new LimitlessStream(GitHubUpdateSource.MaxDocumentBytes + 10)) });
        Assert.Null(await undeclared.FetchManifestAsync(Ct));
    }

    [Fact]
    public async Task TooManyRedirectsAreNothingReceived()
    {
        using var source = Source(_ => Redirect("https://github.com/loop"));

        Assert.Null(await source.FetchManifestAsync(Ct));
        Assert.Equal(6, _requests.Count);
    }

    [Fact]
    public async Task ADownloadWritesTheFileOnlyWhenItIsComplete()
    {
        var payload = new byte[5000];
        new Random(1).NextBytes(payload);
        var file = new UpdateFile(UpdateFileKinds.WindowsSetup, "RafficaPdf-0.2.0-win-x64-setup.exe", payload.Length, new string('a', 64));
        var target = Path.Combine(_folder, file.Name);
        var reported = new List<long>();
        using var source = Source(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });

        Assert.True(await source.DownloadAsync(new UpdateVersion(0, 2, 0), file, target, new Progress<long>(reported.Add), Ct));

        Assert.Equal(payload, await File.ReadAllBytesAsync(target, Ct));
        Assert.False(File.Exists(target + ".part"));
        Assert.Equal(UpdateTrust.DownloadBaseUrl + "/v0.2.0/" + file.Name, _requests.Last().RequestUri!.ToString());
    }

    [Fact]
    public async Task ADownloadOfTheWrongLengthLeavesNoFile()
    {
        var file = new UpdateFile(UpdateFileKinds.WindowsSetup, "RafficaPdf-0.2.0-win-x64-setup.exe", 5000, new string('a', 64));
        var target = Path.Combine(_folder, file.Name);

        // Shorter than the manifest says (declared): refused up front.
        using var shortDeclared = Source(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[4999]) });
        Assert.False(await shortDeclared.DownloadAsync(new UpdateVersion(0, 2, 0), file, target, new Progress<long>(), Ct));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));

        // Longer than declared by the manifest while streaming: stopped, the part file goes.
        using var longer = Source(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new LimitlessStream(6000)) });
        Assert.False(await longer.DownloadAsync(new UpdateVersion(0, 2, 0), file, target, new Progress<long>(), Ct));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task CancellingMidStreamLeavesNoPartFileAndNoFile()
    {
        var file = new UpdateFile(UpdateFileKinds.WindowsSetup, "RafficaPdf-0.2.0-win-x64-setup.exe", 5000, new string('a', 64));
        var target = Path.Combine(_folder, file.Name);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // 1000 bytes arrive, then the stream waits until it is cancelled.
        using var source = Source(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream(1000)) });
        var progress = new Progress<long>(bytes =>
        {
            if (bytes >= 1000)
            {
                cancel.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.DownloadAsync(new UpdateVersion(0, 2, 0), file, target, progress, cancel.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    private sealed class StallingStream(int first) : Stream
    {
        private bool _gave;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_gave)
            {
                _gave = true;
                var n = Math.Min(first, buffer.Length);
                buffer.Span[..n].Clear();
                return n;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A readable stream of the given length whose content length is not known to the HTTP content (a chunked answer).
    private sealed class LimitlessStream(long length) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _read);
            _read += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
