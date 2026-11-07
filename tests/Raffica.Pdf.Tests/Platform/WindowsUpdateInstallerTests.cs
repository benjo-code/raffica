using System.Globalization;
using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.Windows;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class WindowsUpdateInstallerTests : IDisposable
{
    private const string Pid = "4242";

    private readonly ApplierKit _kit = new();
    private readonly FakeStarter _starter = new();
    private int _shutdowns;

    public void Dispose() => _kit.Dispose();

    private WindowsUpdateInstaller Installer(string programDirectory) => new(new WindowsUpdateInstallerSetup(
        programDirectory, _kit.UpdaterDirectory, _kit.Config, _kit.Keys, ApplierKit.RunningVersion, int.Parse(Pid, CultureInfo.InvariantCulture), _starter, () => _shutdowns++, _kit.Clock));

    private static string InstalledExe(string programDirectory) => Path.Combine(programDirectory, WindowsUpdateInstaller.ExeName);

    private static readonly string[] SetupArguments = ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/LOG"];

    [Fact]
    public async Task ArgumentsAreAnArrayAndSurviveSpacesAndNonAscii()
    {
        // Review Focus 1: C:\Users\Zoë Müller\AppData\Local\Programs\Raffica PDF\RafficaPdf.exe, and a staging folder below the same profile.
        var program = _kit.MakeProgramDirectory();
        var staged = _kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: InstalledExe(program), targetDirectory: program);
        Assert.Contains("Zoë Müller", staged.Directory, StringComparison.Ordinal);
        Assert.Contains(' ', program);

        var result = await Installer(program).ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        Assert.Equal(1, _shutdowns);
        var start = Assert.Single(_starter.Starts);

        // The helper is the copy of the running program, never the folder the setup program replaces.
        Assert.StartsWith(_kit.UpdaterDirectory + Path.DirectorySeparatorChar, start.File, StringComparison.Ordinal);
        Assert.Equal(WindowsUpdateInstaller.ExeName, Path.GetFileName(start.File));
        Assert.True(File.Exists(start.File));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(start.File)!, "libpdfium.dll")));

        // One element per argument, exactly the path: no quoting, no shell string, nothing that would split at the space or lose the umlaut.
        Assert.Equal(["--finish-update", staged.Directory, "--after-process", Pid], start.Arguments);
        Assert.All(start.Arguments, argument => Assert.DoesNotContain('"', argument));
    }

    [Fact]
    public void TheRealStarterPassesTheArgumentsAsAList()
    {
        var info = SystemUpdateProcessStarter.CreateStartInfo(
            @"C:\Users\Zoë Müller\AppData\Local\Raffica\updater\a1\RafficaPdf.exe", ["--finish-update", @"C:\Users\Zoë Müller\AppData\Roaming\Raffica\updates\0.2.0"]);

        Assert.False(info.UseShellExecute);
        Assert.Equal(string.Empty, info.Arguments);
        Assert.Equal(["--finish-update", @"C:\Users\Zoë Müller\AppData\Roaming\Raffica\updates\0.2.0"], info.ArgumentList);
    }

    [Fact]
    public async Task AFileThatDoesNotMatchTheSignedManifestChangesNothing()
    {
        var program = _kit.MakeProgramDirectory();
        var staged = _kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: InstalledExe(program), targetDirectory: program);
        await File.AppendAllTextAsync(staged.FilePath, "tampered", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);

        var result = await Installer(program).ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.Checksum, PdfStrings.Culture), result.Message);
        Assert.Empty(_starter.Starts);
        Assert.Equal(0, _shutdowns);
        Assert.False(Directory.Exists(_kit.UpdaterDirectory));
        Assert.False(File.Exists(_kit.SessionPath));
    }

    [Fact]
    public async Task AManifestThatDoesNotVerifyChangesNothing()
    {
        var program = _kit.MakeProgramDirectory();
        var staged = _kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: InstalledExe(program), targetDirectory: program);
        await File.AppendAllTextAsync(Path.Combine(staged.Directory, UpdateStaging.ManifestFileName), " ", TestContext.Current.CancellationToken);

        var result = await Installer(program).ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.Rejected, PdfStrings.Culture), result.Message);
        Assert.Empty(_starter.Starts);
        Assert.False(Directory.Exists(_kit.UpdaterDirectory));
    }

    [Fact]
    public async Task AHelperThatDoesNotStartLeavesNoCopyAndNoSessionAndKeepsTheApp()
    {
        var program = _kit.MakeProgramDirectory();
        var staged = _kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: InstalledExe(program), targetDirectory: program);
        await File.WriteAllTextAsync(_kit.SessionPath, "{}", TestContext.Current.CancellationToken);
        _starter.StartResult = false;

        var result = await Installer(program).ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.InstallFailed, PdfStrings.Culture), result.Message);
        Assert.Equal(0, _shutdowns);
        Assert.Empty(Directory.EnumerateDirectories(_kit.UpdaterDirectory));
        Assert.False(File.Exists(_kit.SessionPath));
        Assert.True(File.Exists(Path.Combine(program, "RafficaPdf.exe")));
    }

    [Fact]
    public async Task AShutdownThatThrowsAfterTheHelperStartedStillReturnsRestarting()
    {
        var program = _kit.MakeProgramDirectory();
        var staged = _kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: InstalledExe(program), targetDirectory: program);
        var installer = new WindowsUpdateInstaller(new WindowsUpdateInstallerSetup(
            program, _kit.UpdaterDirectory, _kit.Config, _kit.Keys, ApplierKit.RunningVersion, 1, _starter, () => throw new InvalidOperationException("closing"), _kit.Clock));

        var result = await installer.ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
    }

    [Fact]
    public async Task AWrongFormIsRefusedBeforeAnythingIsTouched()
    {
        var program = _kit.MakeProgramDirectory();
        var staged = _kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: InstalledExe(program), targetDirectory: program, form: PackageForm.WindowsPortable);

        var result = await Installer(program).ApplyAsync(staged, CancellationToken.None);

        Assert.False(result.IsRestarting);
        Assert.True(result.OffersDownloadPage);
        Assert.Empty(_starter.Starts);
    }

    [Fact]
    public async Task AnOldHelperCopyIsDeletedAndAFreshOneKept()
    {
        var program = _kit.MakeProgramDirectory();
        var staged = _kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: InstalledExe(program), targetDirectory: program);
        var old = Path.Combine(_kit.UpdaterDirectory, Guid.NewGuid().ToString("N"));
        var fresh = Path.Combine(_kit.UpdaterDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(fresh);
        Directory.SetLastWriteTimeUtc(old, _kit.Clock.GetUtcNow().UtcDateTime - WindowsUpdateInstaller.CopyLifetime - TimeSpan.FromMinutes(1));
        Directory.SetLastWriteTimeUtc(fresh, _kit.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(5));

        var result = await Installer(program).ApplyAsync(staged, CancellationToken.None);

        Assert.True(result.IsRestarting);
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh));
    }

    // The helper ---------------------------------------------------------------------------------------------------------------------------------------

    private sealed class HelperRig : IDisposable
    {
        public HelperRig(ApplierKit kit, bool withSession = false)
        {
            Kit = kit;
            Program = kit.MakeProgramDirectory();
            Staged = kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: InstalledExe(Program), targetDirectory: Program);
            if (withSession)
            {
                File.WriteAllText(kit.SessionPath, "{\"windows\":[]}");
            }

            Helper = new WindowsUpdateHelper(new WindowsUpdateHelperSetup(Staged.Directory, kit.Config, kit.Keys, ApplierKit.RunningVersion, Starter, Runner, () => Ended++)
            {
                AfterProcess = 777,
                ProductVersionOf = _ => ProductVersionThrows is { } exception ? throw exception : ProductVersion,
            });
        }

        public ApplierKit Kit { get; }

        public string Program { get; }

        public StagedUpdate Staged { get; }

        public FakeStarter Starter { get; } = new();

        public FakeRunner Runner { get; } = new();

        public WindowsUpdateHelper Helper { get; }

        public string? ProductVersion { get; set; } = "0.2.0+abc";

        public Exception? ProductVersionThrows { get; set; }

        public int Ended { get; private set; }

        public string Installed => InstalledExe(Program);

        public void Dispose() => Helper.Dispose();
    }

    [Fact]
    public async Task AVerificationFailureInTheHelperLeavesTheOldVersionRunning()
    {
        foreach (var tamper in new[] { "file", "manifest", "signature", "manifest-missing" })
        {
            using var kit = new ApplierKit();
            using var rig = new HelperRig(kit);
            switch (tamper)
            {
                case "file":
                    await File.AppendAllTextAsync(rig.Staged.FilePath, "x", TestContext.Current.CancellationToken);
                    break;
                case "manifest":
                    await File.AppendAllTextAsync(Path.Combine(rig.Staged.Directory, UpdateStaging.ManifestFileName), " ", TestContext.Current.CancellationToken);
                    break;
                case "signature":
                    await File.WriteAllTextAsync(Path.Combine(rig.Staged.Directory, UpdateStaging.SignatureFileName), "test-key:" + Convert.ToBase64String(new byte[64]), TestContext.Current.CancellationToken);
                    break;
                default:
                    File.Delete(Path.Combine(rig.Staged.Directory, UpdateStaging.ManifestFileName));
                    break;
            }

            await rig.Helper.RunAsync();

            // The setup program never ran; the installed version was started again and the helper stays to say why.
            Assert.Empty(rig.Runner.Runs);
            var restart = Assert.Single(rig.Starter.Starts);
            Assert.Equal(rig.Installed, restart.File);
            Assert.Equal(UpdateStage.Failed, rig.Helper.State.Stage);
            Assert.False(rig.Helper.CanRetry);
            Assert.Equal(0, rig.Ended);
            Assert.True(Directory.Exists(rig.Staged.Directory));
            Assert.Equal(tamper == "file" ? UpdateFailure.Checksum : UpdateFailure.Rejected, rig.Helper.State.Error);
        }
    }

    [Fact]
    public async Task AnUnexpectedExceptionAfterTheSetupProgramEndsInTheWordedFailure()
    {
        // The reader of the product version throws a type nobody planned for: the task of the helper must not fault (the caller discards it),
        // the installed version starts again and the window shows the failure with "Try again".
        using var rig = new HelperRig(_kit, withSession: true) { ProductVersionThrows = new InvalidOperationException("unplanned") };
        rig.Runner.Outcomes.Enqueue(new ProcessOutcome(0, string.Empty));

        await rig.Helper.RunAsync();

        Assert.Equal(UpdateStage.Failed, rig.Helper.State.Stage);
        Assert.True(rig.Helper.CanRetry);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.HelperInstallFailed, PdfStrings.Culture), rig.Helper.FailureMessage);
        var restart = Assert.Single(rig.Starter.Starts);
        Assert.Equal(rig.Installed, restart.File);
        Assert.Equal(["--restore-session"], restart.Arguments);
        Assert.Equal(0, rig.Ended);
    }

    [Fact]
    public async Task AnUnexpectedExceptionWhileWaitingForTheAppEndsInTheWordedFailureToo()
    {
        using var rig = new HelperRig(_kit);
        rig.Starter.WaitThrows = new InvalidOperationException("unplanned");

        await rig.Helper.RunAsync();

        // Nothing was installed; the old version runs again and the window says so.
        Assert.Empty(rig.Runner.Runs);
        Assert.Equal(UpdateStage.Failed, rig.Helper.State.Stage);
        Assert.Equal(rig.Installed, Assert.Single(rig.Starter.Starts).File);
        Assert.NotNull(rig.Helper.FailureMessage);
        Assert.Equal(0, rig.Ended);
    }

    [Fact]
    public async Task UacRefusalRestartsTheOldVersionAndOffersRetry()
    {
        using var rig = new HelperRig(_kit, withSession: true);
        rig.Runner.Outcomes.Enqueue(new ProcessOutcome(1, string.Empty));

        await rig.Helper.RunAsync();

        // The helper waited for the app, ran the setup program with the fixed arguments and, after the refusal, started what is installed.
        Assert.Equal([777], rig.Starter.Waits);
        var run = Assert.Single(rig.Runner.Runs);
        Assert.Equal(rig.Staged.FilePath, run.File);
        Assert.Equal(SetupArguments, run.Arguments);
        var restart = Assert.Single(rig.Starter.Starts);
        Assert.Equal(rig.Installed, restart.File);
        Assert.Equal(["--restore-session"], restart.Arguments);
        Assert.Equal(UpdateStage.Failed, rig.Helper.State.Stage);
        Assert.True(rig.Helper.CanRetry);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.HelperInstallFailed, PdfStrings.Culture), rig.Helper.FailureMessage);
        Assert.True(Directory.Exists(rig.Staged.Directory));
        Assert.Equal(0, rig.Ended);

        // "Try again": the setup program once more after checking again, and now the new version starts and the helper ends.
        rig.Runner.Outcomes.Enqueue(new ProcessOutcome(0, string.Empty));
        await rig.Helper.RetryAsync();

        Assert.Equal(2, rig.Runner.Runs.Count);
        Assert.Equal(2, rig.Starter.Starts.Count);
        Assert.Equal(rig.Installed, rig.Starter.Starts[1].File);
        Assert.Equal(["--restore-session"], rig.Starter.Starts[1].Arguments);
        Assert.Equal(1, rig.Ended);
        Assert.False(Directory.Exists(rig.Staged.Directory));
    }

    [Fact]
    public async Task ASetupProgramThatCannotBeStartedFailsLikeARefusal()
    {
        using var rig = new HelperRig(_kit);
        rig.Runner.Outcomes.Enqueue(null);

        await rig.Helper.RunAsync();

        Assert.Equal(UpdateStage.Failed, rig.Helper.State.Stage);
        Assert.True(rig.Helper.CanRetry);
        Assert.Equal(rig.Installed, Assert.Single(rig.Starter.Starts).File);
    }

    [Fact]
    public async Task ExitCodeZeroWithAnotherVersionInstalledIsAFailureWithItsOwnWording()
    {
        using var rig = new HelperRig(_kit);
        rig.ProductVersion = "0.1.0+abc";

        await rig.Helper.RunAsync();

        Assert.Equal(UpdateStage.Failed, rig.Helper.State.Stage);
        Assert.True(rig.Helper.CanRetry);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.VersionMismatch, PdfStrings.Culture), rig.Helper.FailureMessage);
        Assert.Equal(0, rig.Ended);
    }

    [Fact]
    public async Task ASuccessfulUpdateStartsTheNewVersionDeletesTheStagingAndEndsTheHelper()
    {
        using var rig = new HelperRig(_kit);

        await rig.Helper.RunAsync();

        Assert.Equal(UpdateStage.Restarting, rig.Helper.State.Stage);
        Assert.Equal(rig.Installed, Assert.Single(rig.Starter.Starts).File);
        Assert.Empty(rig.Starter.Starts[0].Arguments);
        Assert.False(Directory.Exists(rig.Staged.Directory));
        Assert.Equal(1, rig.Ended);
    }

    [Fact]
    public async Task AFolderOutsideTheUpdatesIsNotRunAndNothingIsStarted()
    {
        var program = _kit.MakeProgramDirectory();
        var staged = _kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: InstalledExe(program), targetDirectory: program);
        var elsewhere = Path.Combine(_kit.Root, "0.2.0");
        Directory.CreateDirectory(elsewhere);
        foreach (var file in Directory.EnumerateFiles(staged.Directory))
        {
            File.Copy(file, Path.Combine(elsewhere, Path.GetFileName(file)));
        }

        var starter = new FakeStarter();
        var runner = new FakeRunner();
        using var helper = new WindowsUpdateHelper(new WindowsUpdateHelperSetup(elsewhere, _kit.Config, _kit.Keys, ApplierKit.RunningVersion, starter, runner, () => { }));
        await helper.RunAsync();

        Assert.Equal(UpdateStage.Failed, helper.State.Stage);
        Assert.Empty(runner.Runs);
        Assert.Empty(starter.Starts);
        helper.Dismiss();
        Assert.True(Directory.Exists(elsewhere));
    }

    [Fact]
    public async Task CloseAfterAFailureDeletesTheUpdateAndEndsTheHelper()
    {
        using var rig = new HelperRig(_kit);
        rig.Runner.Outcomes.Enqueue(new ProcessOutcome(1, string.Empty));
        await rig.Helper.RunAsync();

        rig.Helper.Dismiss();

        Assert.False(Directory.Exists(rig.Staged.Directory));
        Assert.Equal(1, rig.Ended);
    }
}
