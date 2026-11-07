using System.Globalization;
using System.Security.Cryptography;
using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.Linux;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

/// <summary>
/// A folder of memory files with a log of every operation, so the order of the steps and each failure can be driven without a Linux system (Linux code
/// is only unit-tested on a Windows machine; the real file system class is covered by <c>TheRealFileSystemReplacesTheFileAndSetsTheMode</c>).
/// </summary>
internal sealed class FakeAppImageFileSystem : IAppImageFileSystem
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, UnixFileMode> Modes { get; } = new(StringComparer.Ordinal);

    public List<string> Log { get; } = [];

    public bool FailRename { get; set; }

    public bool ReadOnlyFolder { get; set; }

    /// <summary>The write of the new file fails with a full disk after this many bytes.</summary>
    public int? DiskFullAfterBytes { get; set; }

    /// <summary>What is written is changed (a disk that returns other bytes than were written).</summary>
    public bool CorruptWrites { get; set; }

    public bool Exists(string path) => Files.ContainsKey(path);

    public Stream CreateNew(string path)
    {
        Log.Add("create " + Path.GetFileName(path));
        if (ReadOnlyFolder)
        {
            throw new UnauthorizedAccessException("read-only file system");
        }

        Files[path] = [];
        return new Sink(this, path);
    }

    public Stream OpenRead(string path)
    {
        Log.Add("read " + Path.GetFileName(path));
        return new MemoryStream(Files[path], writable: false);
    }

    public void SetExecutable(string path)
    {
        Log.Add("chmod " + Path.GetFileName(path));
        Modes[path] = (UnixFileMode)Convert.ToInt32("755", 8);
    }

    public void Rename(string from, string to)
    {
        Log.Add("rename " + Path.GetFileName(from) + " " + Path.GetFileName(to));
        if (FailRename)
        {
            throw new IOException("rename failed");
        }

        Files[to] = Files[from];
        Files.Remove(from);
        Modes[to] = Modes.GetValueOrDefault(from);
        Modes.Remove(from);
    }

    public void Delete(string path)
    {
        Log.Add("delete " + Path.GetFileName(path));
        Files.Remove(path);
        Modes.Remove(path);
    }

    private sealed class Sink(FakeAppImageFileSystem owner, string path) : Stream
    {
        private readonly List<byte> _bytes = [];

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _bytes.Count;

        public override long Position { get => _bytes.Count; set => throw new NotSupportedException(); }

        public override void Flush() => owner.Files[path] = [.. _bytes];

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (owner.DiskFullAfterBytes is { } limit && _bytes.Count + count > limit)
            {
                throw new IOException("No space left on device", 28);
            }

            for (var i = 0; i < count; i++)
            {
                _bytes.Add(owner.CorruptWrites ? (byte)(buffer[offset + i] ^ 1) : buffer[offset + i]);
            }

            owner.Files[path] = [.. _bytes];
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

public sealed class AppImageUpdaterTests : IDisposable
{
    private const string Pid = "4242";
    private static readonly byte[] OldBytes = [1, 2, 3, 4, 5];
    private static readonly UnixFileMode Mode755 = (UnixFileMode)Convert.ToInt32("755", 8);

    private readonly ApplierKit _kit = new();
    private readonly FakeStarter _starter = new();
    private readonly FakeAppImageFileSystem _files = new();
    private int _shutdowns;

    public void Dispose() => _kit.Dispose();

    private string Target => Path.Combine(_kit.Root, "Apps zoë", "RafficaPdf-0.1.0-x86_64.AppImage");

    private string Part => Target + AppImageUpdater.PartSuffix;

    private string AppDir => Path.Combine(_kit.Root, "mnt", ".mount_Raffic1");

    private string InsideProcess => Path.Combine(AppDir, "usr", "lib", "raffica-pdf", "RafficaPdf");

    private AppImageUpdater Updater(IAppImageFileSystem? files = null, string? processPath = null, string? target = null) => new(new AppImageUpdaterSetup(
        target ?? Target,
        processPath ?? InsideProcess,
        AppDir,
        _kit.Config,
        _kit.Keys,
        ApplierKit.RunningVersion,
        int.Parse(Pid, CultureInfo.InvariantCulture),
        _starter,
        () => _shutdowns++,
        files ?? _files));

    private StagedUpdate Stage(byte[]? payload = null)
    {
        _files.Files[Target] = OldBytes;
        _files.Modes[Target] = Mode755;
        return _kit.Stage("0.2.0", UpdateFileKinds.LinuxAppImage, payload, form: PackageForm.AppImage);
    }

    [Fact]
    public async Task TheNewFileIsExecutableBeforeItReplacesTheOld()
    {
        var payload = RandomNumberGenerator.GetBytes(5000);
        var staged = Stage(payload);

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        var rename = _files.Log.IndexOf("rename " + Path.GetFileName(Part) + " " + Path.GetFileName(Target));
        var chmod = _files.Log.IndexOf("chmod " + Path.GetFileName(Part));
        var check = _files.Log.IndexOf("read " + Path.GetFileName(Part));
        var create = _files.Log.IndexOf("create " + Path.GetFileName(Part));
        Assert.True(create >= 0 && create < check && check < chmod && chmod < rename, string.Join(" | ", _files.Log));
        Assert.Equal(payload, _files.Files[Target]);
        Assert.Equal(Mode755, _files.Modes[Target]);
        Assert.False(_files.Files.ContainsKey(Part));

        // Then the new file is executed with the old process to wait for, and the app ends.
        var start = Assert.Single(_starter.Starts);
        Assert.Equal(Target, start.File);
        Assert.Equal(["--after-process", Pid], start.Arguments);
        Assert.Equal(1, _shutdowns);
        Assert.False(Directory.Exists(staged.Directory));
    }

    [Fact]
    public async Task ARenameFailureKeepsTheOldFile()
    {
        var staged = Stage();
        _files.FailRename = true;
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(OldBytes, _files.Files[Target]);
        Assert.False(_files.Files.ContainsKey(Part));
        Assert.Empty(_starter.Starts);
        Assert.Equal(0, _shutdowns);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.InstallFailed, PdfStrings.Culture), result.Message);
        Assert.False(File.Exists(_kit.SessionPath));
        Assert.True(Directory.Exists(staged.Directory));
    }

    [Fact]
    public async Task DiskFullDeletesThePartFile()
    {
        var staged = Stage(RandomNumberGenerator.GetBytes(5000));
        _files.DiskFullAfterBytes = 100;

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.True(result.OffersDownloadPage);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.Storage, PdfStrings.Culture), result.Message);
        Assert.False(_files.Files.ContainsKey(Part));
        Assert.Contains("delete " + Path.GetFileName(Part), _files.Log);
        Assert.Equal(OldBytes, _files.Files[Target]);
        Assert.DoesNotContain(_files.Log, entry => entry.StartsWith("rename", StringComparison.Ordinal));
        Assert.Empty(_starter.Starts);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task AReadOnlyFolderFailsWithTheFolderAndOffersTheDownloadPage()
    {
        var staged = Stage();
        _files.ReadOnlyFolder = true;

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.True(result.OffersDownloadPage);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.FolderWriteProtected, PdfStrings.Culture, Path.GetDirectoryName(Target)), result.Message);
        Assert.Equal(OldBytes, _files.Files[Target]);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task APartFileThatDoesNotMatchTheManifestIsDeletedAndNeverReplacesTheOld()
    {
        var staged = Stage(RandomNumberGenerator.GetBytes(2000));
        _files.CorruptWrites = true;

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.Checksum, PdfStrings.Culture), result.Message);
        Assert.False(_files.Files.ContainsKey(Part));
        Assert.Equal(OldBytes, _files.Files[Target]);
        Assert.DoesNotContain(_files.Log, entry => entry.StartsWith("chmod", StringComparison.Ordinal) || entry.StartsWith("rename", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStagedFileThatNoLongerMatchesTheSignedManifestTouchesNothing()
    {
        var staged = Stage();
        await File.AppendAllTextAsync(staged.FilePath, "tampered", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.Checksum, PdfStrings.Culture), result.Message);
        Assert.Empty(_files.Log);
        Assert.Equal(OldBytes, _files.Files[Target]);
        Assert.False(File.Exists(_kit.SessionPath));
    }

    [Fact]
    public async Task AProgramOutsideTheMountedImageIsNeverReplaced()
    {
        // APPIMAGE is inherited by every child of an AppImage: only a program that runs from inside APPDIR may replace that file.
        var staged = Stage();

        var result = await Updater(processPath: Path.Combine(_kit.Root, "usr", "lib", "raffica-pdf", "RafficaPdf")).ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.True(result.OffersDownloadPage);
        Assert.Empty(_files.Log);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task ARestoredSessionIsPassedOnOnlyWhenOneWasWritten()
    {
        var staged = Stage();
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.Equal(["--after-process", Pid, "--restore-session"], Assert.Single(_starter.Starts).Arguments);
    }

    [Fact]
    public async Task AFileThatCannotBeStartedAfterTheSwapIsWordedAndKeepsTheApp()
    {
        var staged = Stage();
        _starter.StartResult = false;

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        // The file is replaced (the next start is the new version), so the text says that and never "unchanged".
        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.NotStarted, PdfStrings.Culture), result.Message);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task TheRealFileSystemReplacesTheFileAndSetsTheMode()
    {
        var folder = Path.Combine(_kit.Root, "Apps real");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "RafficaPdf-0.1.0-x86_64.AppImage");
        await File.WriteAllBytesAsync(target, OldBytes, TestContext.Current.CancellationToken);
        var payload = RandomNumberGenerator.GetBytes(4000);
        var staged = _kit.Stage("0.2.0", UpdateFileKinds.LinuxAppImage, payload, form: PackageForm.AppImage);

        var result = await Updater(new PhysicalAppImageFileSystem(), target: target).ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        Assert.Equal(payload, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(target + AppImageUpdater.PartSuffix));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(Mode755, File.GetUnixFileMode(target));
        }
    }
}
