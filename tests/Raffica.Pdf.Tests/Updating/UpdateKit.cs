using System.Security.Cryptography;
using System.Text;
using Raffica.Core.Tests.Updates;
using Raffica.Core.Updates;
using Raffica.Pdf.Updating;

namespace Raffica.Pdf.Tests.Updating;

/// <summary>An applier for tests: records what it was handed (and that the file was there) and answers with a prepared result.</summary>
internal sealed class FakeApplier : IUpdateApplier
{
    public int Calls { get; private set; }

    public StagedUpdate? Staged { get; private set; }

    public bool FileExistedWhenCalled { get; private set; }

    public ApplyResult Result { get; set; } = ApplyResult.Restarting;

    public Action? OnApply { get; set; }

    public Task<ApplyResult> ApplyAsync(StagedUpdate staged, CancellationToken ct)
    {
        Calls++;
        Staged = staged;
        FileExistedWhenCalled = File.Exists(staged.FilePath) && File.Exists(staged.RequestPath);
        OnApply?.Invoke();
        return Task.FromResult(Result);
    }
}

/// <summary>A source that must never be asked: any call throws, and the calls are counted.</summary>
internal sealed class ThrowingSource : IUpdateSource
{
    public int Calls { get; private set; }

    public Task<UpdateDocuments?> FetchManifestAsync(CancellationToken ct)
    {
        Calls++;
        throw new InvalidOperationException("the source must not be asked");
    }

    public Task<bool> DownloadAsync(UpdateVersion version, UpdateFile file, string destinationPath, IProgress<long> bytesReceived, CancellationToken ct)
    {
        Calls++;
        throw new InvalidOperationException("the source must not be asked");
    }
}

/// <summary>
/// A release source whose download writes the .part file and then waits until its token is cancelled (a download that hangs or is cancelled), removing the .part
/// file before it throws, as <see cref="IUpdateSource"/> promises. The manifests come from the fake source.
/// </summary>
internal sealed class HangingDownloadSource(IUpdateSource manifests) : IUpdateSource
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the download has begun and its .part file exists.</summary>
    public Task Entered => _entered.Task;

    public Task<UpdateDocuments?> FetchManifestAsync(CancellationToken ct) => manifests.FetchManifestAsync(ct);

    public async Task<bool> DownloadAsync(UpdateVersion version, UpdateFile file, string destinationPath, IProgress<long> bytesReceived, CancellationToken ct)
    {
        var part = destinationPath + ".part";
        await File.WriteAllBytesAsync(part, new byte[100], ct);
        _entered.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
            return false;
        }
        finally
        {
            File.Delete(part);
        }
    }
}

/// <summary>A config folder, a fake release source with a manual clock, and flows over them.</summary>
internal sealed class UpdateKit : IDisposable
{
    public const string Running = "0.1.0";

    public UpdateKit()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("raffica-update-").FullName;
        Clock = new ManualClock(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    }

    public string Directory { get; }

    public FakeUpdateSource Source { get; } = new();

    public ManualClock Clock { get; }

    public FakeApplier Applier { get; } = new();

    /// <summary>Gets the payload the last <see cref="Release"/> announced.</summary>
    public byte[] Payload { get; private set; } = [];

    public string StagingOf(string version) => UpdateStaging.DirectoryFor(Directory, UpdateVersion.TryParse(version, out var parsed) ? parsed : default);

    public UpdateChecker Checker(IUpdateSource? source = null) => new(
        source ?? Source,
        new UpdateStatusStore(Path.Combine(Directory, UpdateStatusStore.FileName)),
        Clock,
        UpdateVersion.TryParse(Running, out var running) ? running : default,
        Source.Keys);

    /// <summary>Makes a flow over <paramref name="source"/> (default: the fake source) in this kit's config folder.</summary>
    public UpdateFlow Flow(PackageForm form = PackageForm.WindowsInstaller, bool enabled = true, bool withApplier = true, IUpdateSource? source = null) =>
        new(Checker(source), source ?? Source, withApplier ? Applier : null, form, () => enabled, Clock, Directory);

    /// <summary>Makes a flow whose "background check is on" answer is <paramref name="enabled"/>, asked at every check (the window passes the setting in force).</summary>
    public UpdateFlow Flow(Func<bool> enabled, PackageForm form = PackageForm.WindowsInstaller) =>
        new(Checker(), Source, Applier, form, enabled, Clock, Directory);

    /// <summary>Announces a correctly signed release of <paramref name="version"/> whose Windows installer is <paramref name="payload"/> (default 2000 bytes), and prepares the download.</summary>
    public void Release(string version, byte[]? payload = null, byte[]? download = null)
    {
        Payload = payload ?? RandomNumberGenerator.GetBytes(2000);
        var json = "{\"format\":1,\"version\":\"" + version + "\",\"files\":[{\"kind\":\"win-x64-setup\",\"name\":\"RafficaPdf-" + version
            + "-win-x64-setup.exe\",\"size\":" + Payload.Length + ",\"sha256\":\"" + Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant() + "\"}]}";
        Source.Documents = Source.Sign(Encoding.UTF8.GetBytes(json));
        Source.DownloadBytes = download ?? Payload;
    }

    public void Dispose()
    {
        Source.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that is still in use is left for the system to clean.
        }
    }
}
