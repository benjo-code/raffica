using System.Text;
using Raffica.Core.Updates;

namespace Raffica.Pdf.Updating;

/// <summary>What a check found, for the command "Check for updates" (<see cref="UpdateFlow.CheckNowAsync"/>).</summary>
internal enum UpdateCheckOutcome
{
    /// <summary>Asking failed: no network, a timeout, a server answer that is not a manifest, a refused manifest.</summary>
    Failed,

    /// <summary>Nothing newer than the running version.</summary>
    UpToDate,

    /// <summary>A newer version is on offer.</summary>
    Available,
}

/// <summary>
/// The update check and installation of the running window. The window makes it on first use (after the first sharp page, when the setting
/// allows the check, or when the user asks), so nothing of it exists at startup. The check runs on the thread pool with a time limit; its result
/// is applied on the thread that called (the UI thread, where <see cref="Changed"/> is raised). A check of the start fails silently (no network,
/// a captive portal's HTML, a refused manifest: nothing changes and nothing is shown); only <see cref="CheckNowAsync"/> tells the outcome.
/// "Install" is two steps with the question about unsaved tabs between them: <see cref="DownloadAndStageAsync"/> fetches the manifest again,
/// downloads the file of this installation form and checks its size and SHA-256 against the signed manifest before anything is staged for the
/// installer, and <see cref="InstallAsync"/> hands the staged update to the installer of the system. A form without an installer (the Debian package,
/// the portable archive, any form the app has no applier for) is only told about the newer version and pointed to the download page.
/// </summary>
internal sealed class UpdateFlow : IDisposable
{
    /// <summary>How long a download may go without receiving a byte before it counts as failed.</summary>
    public static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromSeconds(60);

    // Beyond the limit of the check itself: a source that ignores its token must not hold the flow for ever.
    private static readonly TimeSpan OverallLimit = UpdateChecker.CheckTimeout + TimeSpan.FromSeconds(5);

    private readonly UpdateChecker _checker;
    private readonly IUpdateSource _source;
    private readonly IUpdateApplier? _applier;
    private readonly Func<bool> _enabled;
    private readonly TimeProvider _clock;
    private readonly string _configDirectory;
    private readonly CancellationTokenSource _lifetime = new();
    private Task<UpdateCheckOutcome>? _checking;
    private Task? _staging;
    private CancellationTokenSource? _transferCancel;
    private int _attempt;
    private UpdateState _afterFailure = UpdateState.None;
    private bool _retryable;
    private bool _disposed;

    /// <param name="checker">The schedule and the checks, made over <paramref name="source"/>.</param>
    /// <param name="source">Where manifest and file come from (the same source the checker asks); the flow owns it and disposes it with itself.</param>
    /// <param name="applier">What installs on this system; <see langword="null"/> where the app has none (the flow then only points to the download page).</param>
    /// <param name="form">How the running program was installed.</param>
    /// <param name="enabled">Whether the background check is switched on (<see cref="Raffica.Core.Settings.AppSettings.UpdateCheck"/>).</param>
    /// <param name="clock">The clock of the time limits; tests drive it.</param>
    /// <param name="configDirectory">The config folder whose <c>updates</c> folder takes the download.</param>
    public UpdateFlow(UpdateChecker checker, IUpdateSource source, IUpdateApplier? applier, PackageForm form, Func<bool> enabled, TimeProvider clock, string configDirectory)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrEmpty(configDirectory);
        _checker = checker;
        _source = source;
        _applier = applier;
        Form = form;
        _enabled = enabled;
        _clock = clock;
        _configDirectory = configDirectory;
    }

    /// <summary>Raised on the thread that drives the flow (the UI thread) when <see cref="State"/> changed.</summary>
    public event Action<UpdateState>? Changed;

    /// <summary>Gets where the update is.</summary>
    public UpdateState State { get; private set; } = UpdateState.None;

    /// <summary>Gets how the running program was installed.</summary>
    public PackageForm Form { get; }

    /// <summary>Gets whether this installation can install a newer version by itself (a form with an installer and an applier for it); otherwise it is only pointed to the download page.</summary>
    public bool CanInstall => _applier is not null && Form is PackageForm.WindowsInstaller or PackageForm.AppImage or PackageForm.MacAppArm64 or PackageForm.MacAppX64;

    /// <summary>Gets the verified update that waits for the installer (<see cref="UpdateStage.Ready"/>), or <see langword="null"/>.</summary>
    public StagedUpdate? Staged { get; private set; }

    /// <summary>Gets the stage a failed installation was in.</summary>
    public UpdateStage FailedAt { get; private set; }

    /// <summary>Gets the text the installer gave for its failure (in the language of the interface), or <see langword="null"/> for the general one of the error.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>Gets whether the failure is one the user solves with the download page.</summary>
    public bool FailureOffersDownloadPage { get; private set; }

    /// <summary>Gets whether trying a failed installation again can help.</summary>
    public bool CanRetry => State.Stage == UpdateStage.Failed && _retryable;

    /// <summary>Gets or sets the path of the running executable, for the request of the installer.</summary>
    internal string? ProcessPath { get; init; }

    /// <summary>Gets or sets the folder the program is installed in (for an AppImage the folder of the AppImage), for the request.</summary>
    internal string? TargetDirectory { get; init; }

    /// <summary>Gets or sets where the progress window lies (position in physical pixels, size in device-independent pixels), for the request of the installer.</summary>
    internal Func<(int Left, int Top, double Width, double Height)?>? ProgressWindowBounds { get; set; }

    /// <summary>Gets a task that completes once nothing of the flow runs any more (tests wait for it before they delete the config folder). Never faults.</summary>
    internal Task WhenIdle =>
        Task.WhenAll(new[] { (Task?)_checking, _staging }.OfType<Task>()).ContinueWith(static _ => { }, TaskScheduler.Default);

    /// <summary>
    /// The check of the start: nothing at all when the setting is off (no file is read, no source is asked). Otherwise it checks if one is due
    /// (at most one in 24 hours; <see cref="UpdateChecker.Interval"/>). Never throws, never reports a failure.
    /// </summary>
    public async Task CheckOnStartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled() || _disposed)
        {
            return;
        }

        await RunCheckAsync(force: false, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// The command "Check for updates": checks now, also with the background check switched off and whatever the schedule says (the user asked), and
    /// tells what it found. A check that is running already is the one the caller waits for. Never throws.
    /// </summary>
    public Task<UpdateCheckOutcome> CheckNowAsync(CancellationToken cancellationToken) =>
        _disposed ? Task.FromResult(UpdateCheckOutcome.Failed) : RunCheckAsync(force: true, cancellationToken);

    /// <summary>
    /// Fetches the manifest again (it must still name the version on offer and be signed), picks the file of the installation form, downloads it into
    /// the staging folder (<see cref="UpdateStage.Downloading"/> with the bytes received), checks size and SHA-256 against the manifest
    /// (<see cref="UpdateStage.Verifying"/>) and only then writes the signed manifest, its signature and the request for the installer next to it
    /// (<see cref="UpdateStage.Ready"/>). A staged update that is still there from an earlier attempt (the user cancelled at the question about unsaved
    /// tabs) is checked again and used instead of a new download. A failure deletes what was downloaded and ends in <see cref="UpdateStage.Failed"/>;
    /// <see cref="Cancel"/> goes back to the offer. Once at a time; without an offer, or on a form that cannot install itself, it does nothing. Never throws.
    /// </summary>
    public Task DownloadAndStageAsync(CancellationToken cancellationToken)
    {
        if (_staging is { IsCompleted: false } running)
        {
            return running;
        }

        if (_disposed || !CanInstall || State is not { Stage: UpdateStage.Available or UpdateStage.Ready, Version: { } version })
        {
            return Task.CompletedTask;
        }

        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _transferCancel = cancel;
        var attempt = ++_attempt;
        FailureMessage = null;
        FailureOffersDownloadPage = false;

        // The stage leaves "Available" before anything is awaited, so a second call cannot start a second download.
        var reuse = State.Stage == UpdateStage.Ready && Staged is not null;
        if (!reuse)
        {
            Staged = null;
            SetState(new UpdateState(UpdateStage.Downloading, version, null, 0, null));
        }

        return _staging = StageAsync(version, attempt, cancel, reuse);
    }

    /// <summary>
    /// Hands the staged update to the installer of the system (<see cref="UpdateStage.Installing"/>), which ends the app
    /// (<see cref="UpdateStage.Restarting"/>). Call it only after the question about unsaved tabs was answered and the session was written. A failure
    /// deletes the staging folder and ends in <see cref="UpdateStage.Failed"/>. Without a staged update it fails with <see cref="UpdateFailure.NoInstaller"/>. Never throws.
    /// </summary>
    public async Task<ApplyResult> InstallAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _applier is null || Staged is not { } staged || State.Stage != UpdateStage.Ready)
        {
            if (!_disposed && State is { Stage: UpdateStage.Ready or UpdateStage.Available, Version: not null })
            {
                Fail(UpdateFailure.NoInstaller, State.Stage, Staged?.Directory, State.Version is { } offered ? UpdateState.Offer(offered) : UpdateState.None, retryable: false, offersDownloadPage: true);
            }

            return ApplyResult.Failed(null, offersDownloadPage: true);
        }

        SetState(new UpdateState(UpdateStage.Installing, staged.Version, staged.File, staged.File.Size, null));
        ApplyResult applied;
        try
        {
            applied = await _applier.ApplyAsync(staged, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Before it took over: the staging folder stays (a helper may read it next), the update is ready again.
            SetState(new UpdateState(UpdateStage.Ready, staged.Version, staged.File, staged.File.Size, null));
            return ApplyResult.Failed();
        }
        catch (Exception)
        {
            applied = ApplyResult.Failed();
        }

        if (applied.IsRestarting)
        {
            SetState(new UpdateState(UpdateStage.Restarting, staged.Version, staged.File, staged.File.Size, null));
            return applied;
        }

        Fail(UpdateFailure.InstallFailed, UpdateStage.Installing, staged.Directory, UpdateState.Offer(staged.Version), retryable: true, applied.OffersDownloadPage, applied.Message);
        return applied;
    }

    /// <summary>"Cancel": stops a download or check of the files that runs; the offer stays. Later stages cannot be cancelled.</summary>
    public void Cancel()
    {
        if (State.Stage is UpdateStage.Downloading or UpdateStage.Verifying)
        {
            _transferCancel?.Cancel();
        }
    }

    /// <summary>"Close" after a failure: back to the offer, or to what the failure found (a withdrawn release offers nothing, a newer one is offered).</summary>
    public void Dismiss()
    {
        if (State.Stage == UpdateStage.Failed)
        {
            SetState(_afterFailure);
        }
    }

    /// <summary>
    /// Cancels what runs and deletes a staged update that nobody installed (it is only reused by this process; the next run would remove it too).
    /// May be called more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        if (Staged is { } staged && State.Stage == UpdateStage.Ready)
        {
            Staged = null;
            DeleteStaging(staged.Directory);
        }

        _lifetime.Dispose();
        (_source as IDisposable)?.Dispose();
    }

    private Task<UpdateCheckOutcome> RunCheckAsync(bool force, CancellationToken cancellationToken) =>
        _checking is { IsCompleted: false } running ? running : _checking = CheckAsync(force, cancellationToken);

    private async Task<UpdateCheckOutcome> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            limit.CancelAfter(OverallLimit);
            var ct = limit.Token;

            // On the pool: update.json is read there and the source does its work there; the caller's thread only sets the result.
            // What earlier updates left behind goes first.
            var work = Task.Run(() => CheckOffTheCallerAsync(force, ct), ct);
            if (await work.WaitAsync(ct).ConfigureAwait(true) is not { } result)
            {
                return UpdateCheckOutcome.Failed;
            }

            Apply(result.State);
            return result.Failed ? UpdateCheckOutcome.Failed
                : result.State.Stage == UpdateStage.Available ? UpdateCheckOutcome.Available
                : UpdateCheckOutcome.UpToDate;
        }
        catch (Exception)
        {
            // A check that fails, times out or is cancelled changes nothing; only the command shows that it failed.
            return UpdateCheckOutcome.Failed;
        }
    }

    private async Task<UpdateCheckResult?> CheckOffTheCallerAsync(bool force, CancellationToken ct)
    {
        try
        {
            UpdateStaging.DeleteOld(_configDirectory, _checker.Current, _clock.GetUtcNow());
            return await _checker.CheckDetailedAsync(force, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The checker does not catch a source that throws; its failure must not end as an unobserved exception.
            return null;
        }
    }

    // Only the stages before a download are the business of a check: it never touches a flow that has begun.
    private void Apply(UpdateState state)
    {
        if (State.Stage is UpdateStage.None or UpdateStage.Available)
        {
            SetState(state);
        }
    }

    private void SetState(UpdateState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        RaiseChanged(state);
    }

    private void RaiseChanged(UpdateState state)
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        // Every listener hears it, even when one handler throws.
        foreach (var handler in handlers.GetInvocationList().Cast<Action<UpdateState>>())
        {
            try
            {
                handler(state);
            }
            catch (Exception)
            {
                // A listener must not throw; one that does must not stop the others.
            }
        }
    }

    private async Task StageAsync(UpdateVersion offered, int attempt, CancellationTokenSource cancel, bool reuse)
    {
        var ct = cancel.Token;
        var directory = UpdateStaging.DirectoryFor(_configDirectory, offered);
        var stage = UpdateStage.Downloading;
        try
        {
            // A staged update of this process (the question about unsaved tabs was cancelled): checked again, then used.
            if (reuse && Staged is { } kept && File.Exists(kept.FilePath)
                && await Task.Run(() => ChecksumVerifier.MatchesAsync(kept.FilePath, kept.File.Sha256, kept.File.Size, ct), ct).ConfigureAwait(true))
            {
                SetState(new UpdateState(UpdateStage.Ready, kept.Version, kept.File, kept.File.Size, null));
                return;
            }

            if (reuse)
            {
                Staged = null;
                SetState(new UpdateState(UpdateStage.Downloading, offered, null, 0, null));
            }

            // (1) The manifest again, verified: the offer may be a day old, and only a verified manifest says what to download.
            var (result, documents) = await Task.Run(() => _checker.FetchVerifiedDocumentsAsync(ct), ct).ConfigureAwait(true);
            if (result.Manifest is not { } manifest || documents is null)
            {
                if (result.Rejection == ManifestRejection.NotNewer)
                {
                    Fail(UpdateFailure.Withdrawn, stage, directory, UpdateState.None, retryable: false);
                }
                else
                {
                    Fail(result.Rejection is null ? UpdateFailure.Unreachable : UpdateFailure.Rejected, stage, directory, UpdateState.Offer(offered), retryable: true);
                }

                return;
            }

            // The user agreed to this version: another one (the verifier made sure it is newer) is offered afresh, not installed.
            if (!UpdateVersion.TryParse(manifest.Version, out var found))
            {
                Fail(UpdateFailure.Withdrawn, stage, directory, UpdateState.None, retryable: false);
                return;
            }

            if (found != offered)
            {
                Fail(UpdateFailure.Superseded, stage, directory, UpdateState.Offer(found), retryable: false);
                return;
            }

            // (2) The file of this installation form; a release without one has nothing for this program.
            if (UpdateChecker.Pick(manifest, Form) is not { } file)
            {
                Fail(UpdateFailure.Withdrawn, stage, directory, UpdateState.None, retryable: false);
                return;
            }

            // (3) Into a fresh staging folder, with a limit on how long no byte may arrive.
            SetState(new UpdateState(UpdateStage.Downloading, offered, file, 0, null));
            var target = Path.Combine(directory, file.Name);
            await Task.Run(
                () =>
                {
                    UpdateStaging.DeleteOld(_configDirectory, _checker.Current, _clock.GetUtcNow());
                    PrepareStaging(directory);
                },
                ct).ConfigureAwait(true);
            if (!await DownloadAsync(offered, file, target, attempt, ct).ConfigureAwait(true))
            {
                Fail(UpdateFailure.Unreachable, stage, directory, UpdateState.Offer(offered), retryable: true);
                return;
            }

            // (4) Size and SHA-256 of the signed manifest. Nothing but the file itself is in the folder before this matched.
            stage = UpdateStage.Verifying;
            SetState(new UpdateState(UpdateStage.Verifying, offered, file, file.Size, null));
            if (!await Task.Run(() => ChecksumVerifier.MatchesAsync(target, file.Sha256, file.Size, ct), ct).ConfigureAwait(true))
            {
                Fail(UpdateFailure.Checksum, stage, directory, UpdateState.Offer(offered), retryable: true);
                return;
            }

            // (5) The signed manifest, its signature and the request next to the file, for the installer to verify again.
            stage = UpdateStage.Ready;
            var request = Request(offered, file);
            if (!await Task.Run(() => WriteStaging(directory, documents, request), ct).ConfigureAwait(true))
            {
                Fail(UpdateFailure.Storage, stage, directory, UpdateState.Offer(offered), retryable: true);
                return;
            }

            // "Cancel" is accepted until "Ready" shows, also while the files were written.
            ct.ThrowIfCancellationRequested();
            Staged = new StagedUpdate(directory, offered, file, Form);
            SetState(new UpdateState(UpdateStage.Ready, offered, file, file.Size, null));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // "Cancel", or the window ends: nothing of the download stays, the running version was never touched.
            Staged = null;
            DeleteStaging(directory);
            if (!_disposed)
            {
                SetState(UpdateState.Offer(offered));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Fail(UpdateFailure.Storage, stage, directory, UpdateState.Offer(offered), retryable: true);
        }
        catch (Exception)
        {
            // Never an exception out of the flow: whatever else went wrong is a failure of its step.
            var error = stage switch
            {
                UpdateStage.Downloading => UpdateFailure.Unreachable,
                UpdateStage.Verifying => UpdateFailure.Checksum,
                _ => UpdateFailure.Storage,
            };
            Fail(error, stage, directory, UpdateState.Offer(offered), retryable: true);
        }
        finally
        {
            if (ReferenceEquals(_transferCancel, cancel))
            {
                _transferCancel = null;
            }

            cancel.Dispose();
        }
    }

    /// <summary>Downloads the file; <see langword="false"/> when it did not arrive completely, also when no byte came for <see cref="DownloadStallTimeout"/>. Throws for cancelling and for the file system.</summary>
    private async Task<bool> DownloadAsync(UpdateVersion version, UpdateFile file, string target, int attempt, CancellationToken ct)
    {
        using var stall = new CancellationTokenSource(DownloadStallTimeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, stall.Token);
        var progress = new DownloadProgress(this, attempt, stall, file.Size);
        try
        {
            return await Task.Run(() => _source.DownloadAsync(version, file, target, progress, linked.Token), linked.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // The bytes of a download, posted from the downloading thread; late ones of an earlier attempt or stage are dropped.
    private void OnBytesReceived(int attempt, long bytes)
    {
        if (attempt == _attempt && State.Stage == UpdateStage.Downloading && State.File is not null && bytes > State.BytesReceived)
        {
            SetState(State with { BytesReceived = bytes });
        }
    }

    private void Fail(string error, UpdateStage at, string? directory, UpdateState afterwards, bool retryable, bool offersDownloadPage = false, string? message = null)
    {
        Staged = null;
        if (directory is not null)
        {
            DeleteStaging(directory);
        }

        FailedAt = at;
        FailureMessage = message;
        FailureOffersDownloadPage = offersDownloadPage;
        _afterFailure = afterwards;
        _retryable = retryable;
        SetState(State with { Stage = UpdateStage.Failed, Error = error });
    }

    private UpdateRequest Request(UpdateVersion version, UpdateFile file)
    {
        var bounds = ProgressWindowBounds?.Invoke();
        return new UpdateRequest
        {
            Version = version.ToString(),
            FileName = file.Name,
            Form = Form,
            ExePath = ProcessPath,
            TargetDirectory = TargetDirectory,
            Left = bounds?.Left,
            Top = bounds?.Top,
            Width = bounds?.Width,
            Height = bounds?.Height,
        };
    }

    // A staging folder left by an earlier attempt (or a crash) is ours: it goes, so nothing old is mixed in. On Unix the
    // folder is the owner's alone (the umask would make it 0755), like the files of the user folder.
    internal static void PrepareStaging(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static bool WriteStaging(string directory, UpdateDocuments documents, UpdateRequest request)
    {
        try
        {
            File.WriteAllBytes(Path.Combine(directory, UpdateStaging.ManifestFileName), documents.Manifest);
            File.WriteAllText(Path.Combine(directory, UpdateStaging.SignatureFileName), documents.Signature, new UTF8Encoding(false));
            return request.TryWrite(Path.Combine(directory, UpdateStaging.RequestFileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void DeleteStaging(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What cannot be deleted now is replaced by the next attempt (PrepareStaging) or removed by the next run (UpdateStaging.DeleteOld).
        }
    }

    /// <summary>
    /// Reports the bytes of a download to the flow on the thread it was made on (the UI thread), at most once per percent, and holds the time
    /// limit of a stalled download off while bytes come.
    /// </summary>
    private sealed class DownloadProgress(UpdateFlow owner, int attempt, CancellationTokenSource stall, long size) : IProgress<long>
    {
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        private int _percent = -1;

        public void Report(long value)
        {
            try
            {
                stall.CancelAfter(DownloadStallTimeout);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            var percent = size > 0 ? (int)Math.Min(100, value * 100 / size) : 0;
            if (Interlocked.Exchange(ref _percent, percent) == percent && value < size)
            {
                return;
            }

            if (_context is null)
            {
                owner.OnBytesReceived(attempt, value);
            }
            else
            {
                _context.Post(static state => ((Action)state!)(), (Action)(() => owner.OnBytesReceived(attempt, value)));
            }
        }
    }
}
