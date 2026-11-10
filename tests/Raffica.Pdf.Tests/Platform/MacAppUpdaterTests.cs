using System.Globalization;
using System.Security.Cryptography;
using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.MacOS;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

/// <summary>
/// Answers the system programs of the macOS updater without starting one: <c>hdiutil attach</c> fills the mount point with the prepared apps, <c>ditto</c> makes the
/// copy, <c>plutil</c> answers the version and the identifier, <c>xattr -p</c> answers whether the quarantine mark exists. Every call is recorded in order.
/// </summary>
internal sealed class MacRunner : IProcessRunner
{
    public List<(string File, IReadOnlyList<string> Arguments)> Runs { get; } = [];

    public List<string> Apps { get; } = ["Raffica PDF.app"];

    public int AttachExit { get; set; }

    /// <summary>The attach did not end in time or could not be started (<see cref="IProcessRunner"/> answers <see langword="null"/>); the image may still have been mounted.</summary>
    public bool AttachTimesOut { get; set; }

    public int DittoExit { get; set; }

    public int CodesignExit { get; set; }

    public string Version { get; set; } = "0.2.0";

    public string Identifier { get; set; } = MacBundle.Identifier;

    public bool Quarantined { get; set; }

    /// <summary>The plain <c>detach</c> fails (a busy volume); only <c>-force</c> works.</summary>
    public bool PlainDetachFails { get; set; }

    public Action<IReadOnlyList<string>>? OnAttach { get; set; }

    public IEnumerable<string> Lines => Runs.Select(run => run.File + " " + string.Join(" ", run.Arguments));

    public Task<ProcessOutcome?> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Runs.Add((file, args.ToArray()));
        cancellationToken.ThrowIfCancellationRequested();
        var name = Path.GetFileName(file);
        var exit = 0;
        var output = string.Empty;
        switch (name)
        {
            case "hdiutil" when args[0] == "attach":
                OnAttach?.Invoke(args);
                exit = AttachExit;
                if (exit == 0)
                {
                    var mount = args[args.ToList().IndexOf("-mountpoint") + 1];
                    foreach (var app in Apps)
                    {
                        Directory.CreateDirectory(Path.Combine(mount, app));
                    }

                    if (AttachTimesOut)
                    {
                        // The worst case: the image did get mounted, the answer just never came.
                        return Task.FromResult<ProcessOutcome?>(null);
                    }
                }

                break;
            case "hdiutil" when args[0] == "detach":
                exit = PlainDetachFails && !args.Contains("-force") ? 1 : 0;
                if (exit == 0 && Directory.Exists(args[1]))
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(args[1]))
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                }

                break;
            case "ditto":
                exit = DittoExit;
                if (exit == 0)
                {
                    Directory.CreateDirectory(args[1]);
                }

                break;
            case "codesign":
                exit = CodesignExit;
                break;
            case "plutil":
                output = (args.Contains("CFBundleShortVersionString") ? Version : Identifier) + "\n";
                break;
            case "xattr" when args[0] == "-p":
                exit = Quarantined ? 0 : 1;
                break;
        }

        return Task.FromResult<ProcessOutcome?>(new ProcessOutcome(exit, output));
    }
}

public sealed class MacAppUpdaterTests : IDisposable
{
    private const string Pid = "4242";

    private readonly ApplierKit _kit = new();
    private readonly MacRunner _runner = new();
    private readonly FakeStarter _starter = new();
    private int _shutdowns;

    public MacAppUpdaterTests() => Parent = Path.Combine(_kit.Root, "Applications");

    private string Parent { get; }

    private string Bundle => Path.Combine(Parent, "Raffica PDF.app");

    private string NewBundle => Path.Combine(Parent, ".Raffica PDF update.app");

    private string BackupBundle => Path.Combine(Parent, ".Raffica PDF previous.app");

    public void Dispose() => _kit.Dispose();

    private MacAppUpdater Updater(string? bundle = null, Action<string>? createFolder = null, PackageForm form = PackageForm.MacAppArm64, Action<string>? deleteEntry = null) => new(new MacAppUpdaterSetup(
        bundle ?? Bundle,
        form,
        _kit.Config,
        _kit.Keys,
        ApplierKit.RunningVersion,
        int.Parse(Pid, CultureInfo.InvariantCulture),
        _runner,
        _starter,
        () => _shutdowns++,
        createFolder,
        deleteEntry));

    private StagedUpdate Stage(string? bundle = null)
    {
        Directory.CreateDirectory(bundle ?? Bundle);
        return _kit.Stage("0.2.0", UpdateFileKinds.MacOsArm64Dmg, form: PackageForm.MacAppArm64);
    }

    private static string Mount(StagedUpdate staged) => Path.Combine(staged.Directory, "mnt");

    [Fact]
    public async Task TheHappyPathMountsCopiesVerifiesSwapsAndShutsDown()
    {
        var staged = Stage();
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);
        var mount = Mount(staged);

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        var plist = Path.Combine(NewBundle, "Contents", "Info.plist");
        Assert.Equal(
            [
                $"/usr/bin/hdiutil detach {mount} -force",
                $"/usr/bin/hdiutil attach -readonly -nobrowse -noautoopen -noverify -mountpoint {mount} {staged.FilePath}",
                $"/usr/bin/ditto {Path.Combine(mount, "Raffica PDF.app")} {NewBundle}",
                $"/usr/bin/hdiutil detach {mount}",
                $"/usr/bin/codesign --verify --deep --strict {NewBundle}",
                $"/usr/bin/plutil -extract CFBundleShortVersionString raw -o - {plist}",
                $"/usr/bin/plutil -extract CFBundleIdentifier raw -o - {plist}",
                $"/usr/bin/xattr -p com.apple.quarantine {NewBundle}",
            ],
            _runner.Lines);

        var start = Assert.Single(_starter.Starts);
        Assert.Equal("/bin/sh", start.File);
        Assert.Equal(MacSwapScript.Arguments(4242, Bundle, NewBundle, BackupBundle, "/usr/bin/open", "/usr/bin/codesign", restoreSession: true), start.Arguments);
        Assert.Equal("1", start.Arguments[^1]);
        Assert.Equal(1, _shutdowns);
        Assert.False(Directory.Exists(staged.Directory));
    }

    [Fact]
    public async Task WithoutASessionTheScriptDoesNotRestoreOne()
    {
        var staged = Stage();

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        Assert.Equal("0", Assert.Single(_starter.Starts).Arguments[^1]);
    }

    [Theory]
    [InlineData("/private/var/folders/ab/T/AppTranslocation/6F1D/d/Raffica PDF.app")]
    [InlineData("/Volumes/Raffica PDF/Raffica PDF.app")]
    public async Task ATranslocatedOrMountedBundleIsRefusedBeforeAnyCall(string bundle)
    {
        var staged = Stage();
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await Updater(bundle).ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.True(result.OffersDownloadPage);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.RunsFromDownload, PdfStrings.Culture), result.Message);
        Assert.Empty(_runner.Runs);
        Assert.Empty(_starter.Starts);
        Assert.Equal(0, _shutdowns);
        Assert.False(Directory.Exists(NewBundle));
        Assert.False(Directory.Exists(Mount(staged)));
    }

    [Fact]
    public void TheRefusalIsWordedInBothLanguages()
    {
        var english = UpdateWording.ForApplier(UpdateApplyProblem.RunsFromDownload, CultureInfo.GetCultureInfo("en"));
        var german = UpdateWording.ForApplier(UpdateApplyProblem.RunsFromDownload, CultureInfo.GetCultureInfo("de"));

        Assert.StartsWith("Raffica is running from the downloaded disk image", english, StringComparison.Ordinal);
        Assert.Contains("Programme", german, StringComparison.Ordinal);
        Assert.NotEqual(english, german);
    }

    [Fact]
    public async Task AWriteProtectedParentFailsWithTheFolderAndOffersTheDownloadPage()
    {
        var staged = Stage();
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await Updater(createFolder: _ => throw new UnauthorizedAccessException("read-only")).ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.True(result.OffersDownloadPage);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.FolderWriteProtected, PdfStrings.Culture, Parent), result.Message);
        Assert.Empty(_runner.Runs);
        Assert.Empty(_starter.Starts);
        Assert.False(File.Exists(_kit.SessionPath));
    }

    [Fact]
    public async Task AFailureAfterTheMountAlwaysDetaches()
    {
        // ditto fails, codesign fails, the version differs, the identifier differs: the image is detached each time.
        foreach (var setUp in new Action<MacRunner>[]
        {
            runner => runner.DittoExit = 1,
            runner => runner.CodesignExit = 1,
            runner => runner.Version = "0.9.9",
            runner => runner.Identifier = "com.example.other",
            runner => runner.Apps.Clear(),
        })
        {
            _runner.Runs.Clear();
            _runner.Apps.Clear();
            _runner.Apps.Add("Raffica PDF.app");
            _runner.DittoExit = 0;
            _runner.CodesignExit = 0;
            _runner.Version = "0.2.0";
            _runner.Identifier = MacBundle.Identifier;
            setUp(_runner);
            var staged = Stage();
            var mount = Mount(staged);

            var result = await Updater().ApplyAsync(staged, CancellationToken.None);

            Assert.False(result.IsRestarting);
            Assert.Contains($"/usr/bin/hdiutil detach {mount}", _runner.Lines);
            Assert.Empty(_starter.Starts);
            Assert.False(Directory.Exists(NewBundle));
        }
    }

    [Fact]
    public async Task AFailingDetachIsRetriedOnceWithForce()
    {
        _runner.PlainDetachFails = true;
        var staged = Stage();
        var mount = Mount(staged);

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        var lines = _runner.Lines.ToList();
        var plain = lines.IndexOf($"/usr/bin/hdiutil detach {mount}");
        Assert.True(plain > 0);
        Assert.Equal($"/usr/bin/hdiutil detach {mount} -force", lines[plain + 1]);
        Assert.Equal(2, lines.Count(line => line == $"/usr/bin/hdiutil detach {mount} -force"));
    }

    [Fact]
    public async Task AStaleMountIsDetachedFirst()
    {
        var staged = Stage();
        Directory.CreateDirectory(Path.Combine(Mount(staged), "left over"));
        Directory.CreateDirectory(NewBundle);
        Directory.CreateDirectory(BackupBundle);
        _runner.OnAttach = _ => Assert.False(Directory.Exists(BackupBundle));

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        Assert.Equal($"/usr/bin/hdiutil detach {Mount(staged)} -force", _runner.Lines.First());
        Assert.Contains("attach", _runner.Runs[1].Arguments[0], StringComparison.Ordinal);
        Assert.False(Directory.Exists(BackupBundle));
    }

    [Fact]
    public async Task AStagedFileThatNoLongerMatchesTheSignedManifestTouchesNothing()
    {
        var staged = Stage();
        await File.WriteAllBytesAsync(staged.FilePath, RandomNumberGenerator.GetBytes(3000), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.Checksum, PdfStrings.Culture), result.Message);
        Assert.Empty(_runner.Runs);
        Assert.Empty(_starter.Starts);
        Assert.False(Directory.Exists(NewBundle));
        Assert.False(File.Exists(_kit.SessionPath));
    }

    [Fact]
    public async Task ANonZeroAttachIsInstallFailedAndDeletesTheSession()
    {
        var staged = Stage();
        _runner.AttachExit = 1;
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.InstallFailed, PdfStrings.Culture), result.Message);
        Assert.False(File.Exists(_kit.SessionPath));
        Assert.Empty(_starter.Starts);
        Assert.DoesNotContain(_runner.Runs, run => run.File.EndsWith("ditto", StringComparison.Ordinal));
        Assert.False(Directory.Exists(NewBundle));
    }

    [Fact]
    public async Task AnAttachThatGaveNoAnswerIsInstallFailedAndStillDetaches()
    {
        // Not started or not ended within 60 s: the runner answers null. That is never success, and the image may have been mounted
        // just before the time ran out, so the mount point is detached.
        var staged = Stage();
        _runner.AttachTimesOut = true;
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.InstallFailed, PdfStrings.Culture), result.Message);
        Assert.Contains($"/usr/bin/hdiutil detach {Mount(staged)}", _runner.Lines);
        Assert.DoesNotContain(_runner.Runs, run => run.File.EndsWith("ditto", StringComparison.Ordinal));
        Assert.Empty(_starter.Starts);
        Assert.False(File.Exists(_kit.SessionPath));
        Assert.False(Directory.Exists(NewBundle));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AStaleStagedOrBackupEntryThatStaysFailsWithTheFolderInsteadOfMerging(bool staleStaged)
    {
        // A leftover that cannot be deleted (the delete is made to leave it) must not be merged into by the copy.
        var staged = Stage();
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);
        var stale = staleStaged ? NewBundle : BackupBundle;
        Directory.CreateDirectory(stale);

        var result = await Updater(deleteEntry: path =>
        {
            if (path != stale)
            {
                MacAppUpdater.DeleteEntryByDefault(path);
            }
        }).ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.True(result.OffersDownloadPage);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.FolderWriteProtected, PdfStrings.Culture, Parent), result.Message);
        Assert.Empty(_runner.Runs);
        Assert.Empty(_starter.Starts);
        Assert.False(File.Exists(_kit.SessionPath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task AMountWithoutExactlyOneAppIsRefused(int count)
    {
        _runner.Apps.Clear();
        for (var i = 0; i < count; i++)
        {
            _runner.Apps.Add($"App {i}.app");
        }

        var staged = Stage();

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.InstallFailed, PdfStrings.Culture), result.Message);
        Assert.DoesNotContain(_runner.Runs, run => run.File.EndsWith("ditto", StringComparison.Ordinal));
        Assert.Contains($"/usr/bin/hdiutil detach {Mount(staged)}", _runner.Lines);
        Assert.Empty(_starter.Starts);
    }

    [Fact]
    public async Task TheQuarantineAttributeIsRemovedOnlyAfterTheChecksAndOnlyWhenPresent()
    {
        var staged = Stage();

        await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.DoesNotContain(_runner.Lines, line => line.Contains("xattr -dr", StringComparison.Ordinal));

        _runner.Runs.Clear();
        _runner.Quarantined = true;
        staged = Stage();

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        var lines = _runner.Lines.ToList();
        var codesign = lines.FindIndex(line => line.Contains("codesign", StringComparison.Ordinal));
        var identifier = lines.FindIndex(line => line.Contains("CFBundleIdentifier", StringComparison.Ordinal));
        var removal = lines.IndexOf($"/usr/bin/xattr -dr com.apple.quarantine {NewBundle}");
        Assert.True(codesign >= 0 && codesign < identifier && identifier < removal, string.Join(" | ", lines));
        Assert.Equal(lines.Count - 1, removal);
    }

    [Fact]
    public async Task ACodesignOrVersionMismatchNeverReachesTheSwap()
    {
        _runner.Quarantined = true;
        (Action<MacRunner> SetUp, UpdateApplyProblem Problem)[] cases =
        [
            (runner => runner.CodesignExit = 1, UpdateApplyProblem.Rejected),
            (runner => runner.Version = "0.1.5", UpdateApplyProblem.VersionMismatch),
            (runner => runner.Identifier = "com.example.other", UpdateApplyProblem.VersionMismatch),
        ];
        foreach (var (setUp, problem) in cases)
        {
            _runner.Runs.Clear();
            _runner.CodesignExit = 0;
            _runner.Version = "0.2.0";
            _runner.Identifier = MacBundle.Identifier;
            setUp(_runner);
            var staged = Stage();
            await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

            var result = await Updater().ApplyAsync(staged, CancellationToken.None);

            Assert.False(result.IsRestarting);
            Assert.Equal(UpdateWording.ForApplier(problem, PdfStrings.Culture), result.Message);
            Assert.Empty(_starter.Starts);
            Assert.Equal(0, _shutdowns);
            Assert.DoesNotContain(_runner.Lines, line => line.Contains("xattr", StringComparison.Ordinal));
            Assert.False(Directory.Exists(NewBundle));
            Assert.False(File.Exists(_kit.SessionPath));
        }
    }

    [Fact]
    public async Task AScriptThatCannotBeStartedIsWordedAndKeepsTheApp()
    {
        var staged = Stage();
        _starter.StartResult = false;
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await Updater().ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.InstallFailed, PdfStrings.Culture), result.Message);
        Assert.Equal(0, _shutdowns);
        Assert.False(File.Exists(_kit.SessionPath));
        Assert.False(Directory.Exists(NewBundle));
        Assert.True(Directory.Exists(Bundle));
    }

    [Fact]
    public async Task ACancelBeforeTheScriptCleansUpAndNeverStartsIt()
    {
        var staged = Stage();
        using var cancel = new CancellationTokenSource();
        _runner.OnAttach = _ => cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Updater().ApplyAsync(staged, cancel.Token));

        Assert.Empty(_starter.Starts);
        Assert.Equal(0, _shutdowns);
        Assert.Contains($"/usr/bin/hdiutil detach {Mount(staged)}", _runner.Lines);
        Assert.False(Directory.Exists(NewBundle));
    }

    [Fact]
    public async Task PathsWithSpacesAreOneArgumentEach()
    {
        var bundle = Path.Combine(Parent, "My Raffica PDF.app");
        var staged = Stage(bundle);
        var newBundle = Path.Combine(Parent, ".Raffica PDF update.app");

        var result = await Updater(bundle).ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        var ditto = _runner.Runs.Single(run => run.File.EndsWith("ditto", StringComparison.Ordinal));
        Assert.Equal(2, ditto.Arguments.Count);
        Assert.Equal(newBundle, ditto.Arguments[1]);
        var codesign = _runner.Runs.Single(run => run.File.EndsWith("codesign", StringComparison.Ordinal));
        Assert.Equal(newBundle, codesign.Arguments[^1]);
        var attach = _runner.Runs.Single(run => run.Arguments[0] == "attach");
        Assert.Equal(staged.FilePath, attach.Arguments[^1]);
        Assert.Equal(Path.Combine(staged.Directory, "mnt"), attach.Arguments[^2]);

        // The script gets the bundle as its own parameter, never as part of the script text.
        var script = Assert.Single(_starter.Starts).Arguments;
        Assert.Equal(bundle, script[4]);
        Assert.Equal(newBundle, script[5]);
        Assert.Equal(Path.Combine(Parent, ".Raffica PDF previous.app"), script[6]);
        Assert.DoesNotContain("My Raffica", script[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFormThatIsNotTheMacFormIsRefusedUntouched()
    {
        var staged = Stage();

        var result = await Updater(form: PackageForm.AppImage).ApplyAsync(staged with { Form = PackageForm.AppImage }, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.RunsFromDownload, PdfStrings.Culture), result.Message);
        Assert.Empty(_runner.Runs);
    }
}
