using System.Text;
using Raffica.Core.Updates;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class UpdateFlowTests : IDisposable
{
    private readonly UpdateKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoConnectionWhenTheSwitchIsOff()
    {
        var source = new ThrowingSource();
        using var flow = _kit.Flow(enabled: false, source: source);
        var changes = 0;
        flow.Changed += _ => changes++;

        await flow.CheckOnStartAsync(Ct);

        Assert.Equal(0, source.Calls);
        Assert.Equal(0, changes);
        Assert.Equal(UpdateStage.None, flow.State.Stage);

        // Not even the status file or the staging folder is touched: the config folder stays empty.
        Assert.Empty(Directory.EnumerateFileSystemEntries(_kit.Directory));
    }

    [Fact]
    public async Task ACheckIsNotRepeatedWithin24Hours()
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow();

        await flow.CheckOnStartAsync(Ct);
        Assert.Equal(1, _kit.Source.FetchCalls);
        Assert.Equal(UpdateStage.Available, flow.State.Stage);

        // The same run, an hour later: the schedule says "not due", nothing is asked.
        _kit.Clock.Advance(TimeSpan.FromHours(1));
        await flow.CheckOnStartAsync(Ct);
        Assert.Equal(1, _kit.Source.FetchCalls);

        // A new run (a new flow over the same config folder) a day minus a minute after the check: still not due.
        _kit.Clock.Advance(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(59));
        using (var later = _kit.Flow())
        {
            await later.CheckOnStartAsync(Ct);
            Assert.Equal(1, _kit.Source.FetchCalls);
            Assert.Equal(UpdateStage.Available, later.State.Stage);
        }

        // Past 24 hours the next start asks again.
        _kit.Clock.Advance(TimeSpan.FromMinutes(2));
        await flow.CheckOnStartAsync(Ct);
        Assert.Equal(2, _kit.Source.FetchCalls);
    }

    [Fact]
    public async Task ManualCheckIgnoresTheInterval()
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow();
        await flow.CheckOnStartAsync(Ct);
        Assert.Equal(1, _kit.Source.FetchCalls);

        Assert.Equal(UpdateCheckOutcome.Available, await flow.CheckNowAsync(Ct));
        Assert.Equal(2, _kit.Source.FetchCalls);

        // The user asked, so it works with the background check switched off too.
        using var off = _kit.Flow(enabled: false);
        await off.CheckOnStartAsync(Ct);
        Assert.Equal(2, _kit.Source.FetchCalls);
        Assert.Equal(UpdateCheckOutcome.Available, await off.CheckNowAsync(Ct));
        Assert.Equal(3, _kit.Source.FetchCalls);
        Assert.Equal(UpdateStage.Available, off.State.Stage);
    }

    [Fact]
    public async Task ANewerRunningVersionIsUpToDate()
    {
        _kit.Release("0.1.0");
        using var flow = _kit.Flow();

        Assert.Equal(UpdateCheckOutcome.UpToDate, await flow.CheckNowAsync(Ct));
        Assert.Equal(UpdateStage.None, flow.State.Stage);
    }

    [Fact]
    public async Task HtmlAnswerFromACaptivePortalIsSilentAtStartAndWordedOnManualCheck()
    {
        // A captive portal or proxy answers HTML where the manifest should be: the documents are not a signed manifest.
        _kit.Source.Documents = new UpdateDocuments(Encoding.UTF8.GetBytes("<html><body>Please sign in to the network</body></html>"), "<html>");
        using var flow = _kit.Flow();
        var changes = new List<UpdateState>();
        flow.Changed += changes.Add;

        // At the start: nothing changes, nothing is raised, nothing is remembered (the next start tries again).
        await flow.CheckOnStartAsync(Ct);
        Assert.Equal(UpdateState.None, flow.State);
        Assert.Empty(changes);
        Assert.False(File.Exists(Path.Combine(_kit.Directory, UpdateStatusStore.FileName)));

        // The manual check ends with one outcome the window words as "could not check"; the state still is untouched.
        Assert.Equal(UpdateCheckOutcome.Failed, await flow.CheckNowAsync(Ct));
        Assert.Equal(UpdateState.None, flow.State);
        Assert.Empty(changes);
        Assert.Equal(2, _kit.Source.FetchCalls);
    }

    [Fact]
    public async Task NothingReceivedIsAFailedCheckToo()
    {
        _kit.Source.Documents = null;
        using var flow = _kit.Flow();

        await flow.CheckOnStartAsync(Ct);
        Assert.Equal(UpdateStage.None, flow.State.Stage);
        Assert.Equal(UpdateCheckOutcome.Failed, await flow.CheckNowAsync(Ct));
    }

    [Fact]
    public async Task DownloadVerifiesChecksumBeforeAnythingIsStaged()
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);
        var staging = _kit.StagingOf("0.2.0");

        // At the moment the file is being checked (Verifying) only the file is in the folder: no manifest, no signature, no request for the installer yet.
        var stages = new List<UpdateStage>();
        var atVerifying = new List<string>();
        flow.Changed += state =>
        {
            stages.Add(state.Stage);
            if (state.Stage == UpdateStage.Verifying)
            {
                atVerifying.AddRange(Directory.EnumerateFiles(staging).Select(Path.GetFileName)!);
            }
        };

        await flow.DownloadAndStageAsync(Ct);

        Assert.Equal([UpdateStage.Downloading, UpdateStage.Verifying, UpdateStage.Ready], stages.Distinct());
        Assert.Equal(["RafficaPdf-0.2.0-win-x64-setup.exe"], atVerifying);
        Assert.Equal(UpdateStage.Ready, flow.State.Stage);
        var staged = Assert.IsType<StagedUpdate>(flow.Staged);
        Assert.Equal(_kit.Payload, await File.ReadAllBytesAsync(staged.FilePath, Ct));
        Assert.True(File.Exists(Path.Combine(staging, UpdateStaging.ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(staging, UpdateStaging.SignatureFileName)));
        var request = UpdateRequest.Load(staged.RequestPath);
        Assert.NotNull(request);
        Assert.Equal("0.2.0", request.Version);
        Assert.Equal(staged.File.Name, request.FileName);
        Assert.Equal(PackageForm.WindowsInstaller, request.Form);
    }

    [Fact]
    public async Task AWrongChecksumDeletesTheFileAndFailsWithChecksum()
    {
        var payload = new byte[2000];
        var tampered = new byte[2000];
        tampered[1000] = 1;
        _kit.Release("0.2.0", payload, download: tampered);
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);

        await flow.DownloadAndStageAsync(Ct);

        Assert.Equal(UpdateStage.Failed, flow.State.Stage);
        Assert.Equal(UpdateFailure.Checksum, flow.State.Error);
        Assert.Equal(UpdateStage.Verifying, flow.FailedAt);
        Assert.True(flow.CanRetry);
        Assert.Null(flow.Staged);
        Assert.False(Directory.Exists(_kit.StagingOf("0.2.0")));
        Assert.Equal(0, _kit.Applier.Calls);

        // Closing the message goes back to the offer.
        flow.Dismiss();
        Assert.Equal(UpdateStage.Available, flow.State.Stage);
    }

    [Fact]
    public async Task ASizeThatDiffersFromTheManifestIsAChecksumFailureToo()
    {
        _kit.Release("0.2.0", new byte[2000], download: new byte[1999]);
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);

        await flow.DownloadAndStageAsync(Ct);

        Assert.Equal(UpdateFailure.Checksum, flow.State.Error);
        Assert.False(Directory.Exists(_kit.StagingOf("0.2.0")));
    }

    [Fact]
    public async Task ADownloadThatDoesNotArriveFailsWithUnreachableAndCanBeRetried()
    {
        _kit.Release("0.2.0");
        _kit.Source.DownloadBytes = null;
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);

        await flow.DownloadAndStageAsync(Ct);

        Assert.Equal(UpdateFailure.Unreachable, flow.State.Error);
        Assert.True(flow.CanRetry);
        Assert.False(Directory.Exists(_kit.StagingOf("0.2.0")));
        flow.Dismiss();
        _kit.Source.DownloadBytes = _kit.Payload;
        await flow.DownloadAndStageAsync(Ct);
        Assert.Equal(UpdateStage.Ready, flow.State.Stage);
    }

    [Fact]
    public async Task ANewerReleaseThanTheOneOnOfferIsOfferedAfreshAndNotInstalled()
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);

        _kit.Release("0.3.0");
        await flow.DownloadAndStageAsync(Ct);

        Assert.Equal(UpdateFailure.Superseded, flow.State.Error);
        Assert.False(flow.CanRetry);
        Assert.Equal(0, _kit.Source.DownloadCalls);
        flow.Dismiss();
        Assert.Equal(UpdateStage.Available, flow.State.Stage);
        Assert.Equal(new UpdateVersion(0, 3, 0), flow.State.Version);
    }

    [Fact]
    public async Task AManifestThatFailsTheSignatureAtDownloadTimeStagesNothing()
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);

        var good = _kit.Source.Documents!;
        _kit.Source.Documents = good with { Signature = "test-key:" + Convert.ToBase64String(new byte[64]) };
        await flow.DownloadAndStageAsync(Ct);

        Assert.Equal(UpdateFailure.Rejected, flow.State.Error);
        Assert.Equal(0, _kit.Source.DownloadCalls);
        Assert.False(Directory.Exists(_kit.StagingOf("0.2.0")));
    }

    [Theory]
    [InlineData(PackageForm.WindowsInstaller)]
    [InlineData(PackageForm.AppImage)]
    [InlineData(PackageForm.MacAppArm64)]
    [InlineData(PackageForm.MacAppX64)]
    public void TheMacFormsCanInstall(PackageForm form)
    {
        using var flow = _kit.Flow(form);

        Assert.True(flow.CanInstall);
    }

    [Theory]
    [InlineData(PackageForm.Deb)]
    [InlineData(PackageForm.WindowsPortable)]
    [InlineData(PackageForm.Unknown)]
    public async Task DebAndPortableOnlyOfferTheDownloadPage(PackageForm form)
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow(form);

        Assert.Equal(UpdateCheckOutcome.Available, await flow.CheckNowAsync(Ct));
        Assert.False(flow.CanInstall);

        // Nothing is downloaded or staged, and an install attempt ends in the worded "no installer" with the download page.
        await flow.DownloadAndStageAsync(Ct);
        Assert.Equal(0, _kit.Source.DownloadCalls);
        Assert.Equal(UpdateStage.Available, flow.State.Stage);
        Assert.False(Directory.Exists(Path.Combine(_kit.Directory, UpdateStaging.DirectoryName)));
        var result = await flow.InstallAsync(Ct);
        Assert.False(result.IsRestarting);
        Assert.True(result.OffersDownloadPage);
        Assert.Equal(UpdateFailure.NoInstaller, flow.State.Error);
        Assert.True(flow.FailureOffersDownloadPage);
        Assert.Equal(0, _kit.Applier.Calls);
    }

    [Fact]
    public async Task AFormWithoutAnApplierBehavesLikeTheDownloadPageOnly()
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow(PackageForm.WindowsInstaller, withApplier: false);

        await flow.CheckNowAsync(Ct);
        Assert.False(flow.CanInstall);
        await flow.DownloadAndStageAsync(Ct);
        Assert.Equal(0, _kit.Source.DownloadCalls);
    }

    [Fact]
    public async Task InstallHandsTheStagedUpdateToTheApplierAndEndsRestarting()
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);
        await flow.DownloadAndStageAsync(Ct);

        var result = await flow.InstallAsync(Ct);

        Assert.True(result.IsRestarting);
        Assert.Equal(1, _kit.Applier.Calls);
        Assert.True(_kit.Applier.FileExistedWhenCalled);
        Assert.Equal(new UpdateVersion(0, 2, 0), _kit.Applier.Staged!.Version);
        Assert.Equal(UpdateStage.Restarting, flow.State.Stage);
    }

    [Fact]
    public async Task AFailedInstallDeletesTheStagingFolderAndKeepsTheOffer()
    {
        _kit.Release("0.2.0");
        _kit.Applier.Result = ApplyResult.Failed("worded by the applier", offersDownloadPage: true);
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);
        await flow.DownloadAndStageAsync(Ct);

        var result = await flow.InstallAsync(Ct);

        Assert.False(result.IsRestarting);
        Assert.Equal(UpdateStage.Failed, flow.State.Stage);
        Assert.Equal(UpdateFailure.InstallFailed, flow.State.Error);
        Assert.Equal("worded by the applier", flow.FailureMessage);
        Assert.True(flow.FailureOffersDownloadPage);
        Assert.True(flow.CanRetry);
        Assert.False(Directory.Exists(_kit.StagingOf("0.2.0")));
        flow.Dismiss();
        Assert.Equal(UpdateStage.Available, flow.State.Stage);
    }

    [Fact]
    public async Task AStagedUpdateThatWasNotInstalledIsCheckedAgainAndUsedWithoutANewDownload()
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);
        await flow.DownloadAndStageAsync(Ct);
        Assert.Equal(1, _kit.Source.DownloadCalls);

        // The user cancelled at the question about unsaved tabs: the update stays ready and the next Install uses the file.
        await flow.DownloadAndStageAsync(Ct);

        Assert.Equal(1, _kit.Source.DownloadCalls);
        Assert.Equal(UpdateStage.Ready, flow.State.Stage);
        Assert.True(File.Exists(flow.Staged!.FilePath));

        // A file that no longer matches is not used: it is downloaded again.
        await File.WriteAllBytesAsync(flow.Staged.FilePath, new byte[2000], Ct);
        await flow.DownloadAndStageAsync(Ct);
        Assert.Equal(2, _kit.Source.DownloadCalls);
        Assert.Equal(UpdateStage.Ready, flow.State.Stage);
    }

    [Fact]
    public async Task CancelStopsTheDownloadAndRemovesTheStagingFolderAndThePartFile()
    {
        _kit.Release("0.2.0");
        var source = new HangingDownloadSource(_kit.Source);
        using var flow = _kit.Flow(source: source);
        await flow.CheckNowAsync(Ct);

        var staging = _kit.StagingOf("0.2.0");
        var download = flow.DownloadAndStageAsync(Ct);
        await source.Entered.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.True(Directory.Exists(staging));
        Assert.Single(Directory.EnumerateFiles(staging), path => path.EndsWith(".part", StringComparison.Ordinal));
        Assert.Equal(UpdateStage.Downloading, flow.State.Stage);

        flow.Cancel();
        await download.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // Back to the offer: nothing staged, no folder, no .part, the running version never touched.
        Assert.Equal(UpdateStage.Available, flow.State.Stage);
        Assert.Equal(new UpdateVersion(0, 2, 0), flow.State.Version);
        Assert.Null(flow.Staged);
        Assert.False(Directory.Exists(staging));
        Assert.Equal(0, _kit.Applier.Calls);
    }

    [Fact]
    public async Task ADownloadWithoutABytePastTheStallTimeoutFailsAsUnreachableAndLeavesNothing()
    {
        _kit.Release("0.2.0");
        var source = new HangingDownloadSource(_kit.Source);
        using var flow = _kit.Flow(source: source);
        await flow.CheckNowAsync(Ct);
        var staging = _kit.StagingOf("0.2.0");

        var download = flow.DownloadAndStageAsync(Ct);
        await source.Entered.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // The clock of the flow drives the limit: just under it nothing happens, past it the download counts as failed.
        _kit.Clock.Advance(UpdateFlow.DownloadStallTimeout - TimeSpan.FromSeconds(1));
        await Task.Delay(100, Ct);
        Assert.False(download.IsCompleted);
        _kit.Clock.Advance(TimeSpan.FromSeconds(2));
        await download.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(UpdateStage.Failed, flow.State.Stage);
        Assert.Equal(UpdateFailure.Unreachable, flow.State.Error);
        Assert.True(flow.CanRetry);
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public async Task DisposingTheFlowDeletesAStagedUpdateNobodyInstalled()
    {
        _kit.Release("0.2.0");
        var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);
        await flow.DownloadAndStageAsync(Ct);
        Assert.True(Directory.Exists(_kit.StagingOf("0.2.0")));

        flow.Dispose();

        Assert.False(Directory.Exists(_kit.StagingOf("0.2.0")));
        Assert.Equal(UpdateCheckOutcome.Failed, await flow.CheckNowAsync(Ct));
    }
}
