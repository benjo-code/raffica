using System.Diagnostics;
using Raffica.Core.Updates;
using Raffica.Pdf.Updating;

namespace Raffica.Pdf.Platform.Windows;

/// <summary>What the helper of the Windows updater needs (<c>--finish-update</c>).</summary>
/// <param name="StagingDirectory">The folder of the command line; only a folder directly below <c>&lt;config&gt;\updates</c> is accepted.</param>
/// <param name="ConfigDirectory">The config folder of the user (a temporary folder in tests).</param>
/// <param name="Keys">The keys the manifest must be signed with (<see cref="UpdateTrust.Keys"/>; test keys only in tests).</param>
/// <param name="Running">The version of the helper, which is the installed one: the manifest must be newer.</param>
/// <param name="Starter">Starts Raffica PDF again and waits for the app that started the helper.</param>
/// <param name="Runner">Runs the setup program and waits for it.</param>
/// <param name="End">Ends the helper.</param>
internal sealed record WindowsUpdateHelperSetup(
    string StagingDirectory,
    string ConfigDirectory,
    IReadOnlyList<UpdateKey> Keys,
    UpdateVersion Running,
    IUpdateProcessStarter Starter,
    IProcessRunner Runner,
    Action End)
{
    /// <summary>How long the helper waits for the app that started it to end before it installs anyway.</summary>
    public static readonly TimeSpan DefaultExitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long the setup program may run before the helper stops it and counts the installation as failed.</summary>
    public static readonly TimeSpan DefaultInstallTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Gets the process of the app that started the helper (<c>--after-process</c>).</summary>
    public int? AfterProcess { get; init; }

    /// <summary>Gets how long the helper waits for the app to end.</summary>
    public TimeSpan ExitTimeout { get; init; } = DefaultExitTimeout;

    /// <summary>Gets how long the setup program may run.</summary>
    public TimeSpan InstallTimeout { get; init; } = DefaultInstallTimeout;

    /// <summary>
    /// Gets what reads the product version of a program file (<see cref="FileVersionInfo.ProductVersion"/>), or <see langword="null"/> when it cannot be read;
    /// tests pass a fake.
    /// </summary>
    public Func<string, string?> ProductVersionOf { get; init; } = ReadProductVersion;

    private static string? ReadProductVersion(string path)
    {
        try
        {
            return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).ProductVersion : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// The helper of the Windows updater: a copy of Raffica PDF started with <c>--finish-update &lt;staging folder&gt;</c> after the app downloaded and verified an update
/// and ended (<see cref="WindowsUpdateInstaller"/>). It takes nothing but the <c>request.json</c> of a folder directly below <c>&lt;config&gt;\updates</c>, waits until
/// the app has ended, verifies the signature of the saved manifest with the keys of the program and the SHA-256 of the setup program again (the files lay on the disk
/// in between) and keeps the setup program open against writing until it ended, then runs it silently (<see cref="InnoArguments"/>; Inno Setup keeps the install mode
/// and folder of the installation through its fixed <c>AppId</c>). Exit code 0 and the program in the folder reports the requested version: Raffica PDF starts again from
/// the installed folder, with <c>--restore-session</c> when the app wrote the session, and the helper ends. Anything else (the permission prompt declined, a failing
/// setup program, another version in the folder after exit code 0): the installed version starts again and the window shows the error with "Try again" (the setup
/// program once more) and "Close". A file that does not pass the check never runs: the installed version starts again, nothing is retried. The staging folder is deleted
/// only after success or "Close". The window follows <see cref="State"/> through <see cref="Changed"/>; used on the UI thread.
/// </summary>
internal sealed class WindowsUpdateHelper : IDisposable
{
    private readonly WindowsUpdateHelperSetup _setup;
    private readonly string? _staging;
    private readonly UpdateVersion? _version;
    private readonly CancellationTokenSource _cancel = new();
    private UpdateFile? _file;
    private byte[]? _session;
    private Task? _running;
    private bool _retryable;
    private bool _installedStarted;
    private string? _failureMessage;

    public WindowsUpdateHelper(WindowsUpdateHelperSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _setup = setup;
        _staging = UpdateApplierSupport.AcceptedStaging(setup.StagingDirectory, setup.ConfigDirectory);
        var request = _staging is null ? null : UpdateRequest.Load(Path.Combine(_staging, UpdateStaging.RequestFileName));
        if (request is not null && Usable(request, _staging!) is { } version)
        {
            Request = request;
            _version = version;
            State = new UpdateState(UpdateStage.Verifying, version, null, 0, null);
        }
        else
        {
            // Not an update of ours: nothing is run, nothing is started, and "Close" deletes nothing outside the updates.
            State = new UpdateState(UpdateStage.Failed, null, null, 0, UpdateFailure.Rejected);
        }
    }

    /// <summary>Raised on every change of <see cref="State"/>.</summary>
    public event Action? Changed;

    /// <summary>Gets the request of the app (where its progress window was), or <see langword="null"/> when it was refused.</summary>
    public UpdateRequest? Request { get; }

    /// <summary>Gets where the helper is.</summary>
    public UpdateState State { get; private set; }

    /// <summary>
    /// Gets the sentence of a failure after the setup program ran (it may have replaced files, so "your version stays" would not be true), or <see langword="null"/>
    /// for the general sentence of the failure code (a check refused before it ran). Already in the language of the interface.
    /// </summary>
    public string? FailureMessage => State.Stage == UpdateStage.Failed ? _failureMessage : null;

    /// <summary>Gets whether "Try again" can help.</summary>
    public bool CanRetry => State.Stage == UpdateStage.Failed && _retryable;

    /// <summary>
    /// Gets the arguments of the setup program: very silent, no message boxes, no restart, running copies of the program closed, and a log without a file name. An
    /// installation for all users runs Inno Setup elevated, as another account, so nothing it writes may go into a folder this user can write to (the staging folder is
    /// one): <c>/LOG</c> alone writes the log into the <c>%TEMP%</c> of the account that runs the setup program. A list, never a shell string.
    /// </summary>
    public static IReadOnlyList<string> InnoArguments { get; } = ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/LOG"];

    private string InstalledExe => Path.Combine(Request!.TargetDirectory!, WindowsUpdateInstaller.ExeName);

    private string SessionPath => UpdateApplierSupport.SessionPath(_setup.ConfigDirectory);

    /// <summary>Waits for the app, then verifies and installs (see the class). Once: a second call gets the running one. Never throws.</summary>
    public Task RunAsync() => _running ??= GuardedAsync(RunCoreAsync);

    /// <summary>
    /// "Cancel" (also closing the window) while the helper waits for the app or verifies: the installed version starts again, the update goes and the helper ends, at once
    /// (the process may end with the window before the check stops).
    /// </summary>
    public void CancelInstall()
    {
        if (State.Stage == UpdateStage.Verifying)
        {
            _cancel.Cancel();
            Abandon();
        }
    }

    /// <summary>"Try again": the setup program once more, after verifying again.</summary>
    public Task RetryAsync() => CanRetry ? _running = GuardedAsync(AttemptAsync) : Task.CompletedTask;

    /// <summary>"Close" after a failure: deletes the update and ends the helper (the installed version runs already).</summary>
    public void Dismiss()
    {
        if (State.Stage != UpdateStage.Failed)
        {
            return;
        }

        DeleteStaging();
        SetState(UpdateState.None);
        _setup.End();
    }

    /// <summary>Releases the cancellation of the helper (when the process ends).</summary>
    public void Dispose() => _cancel.Dispose();

    private static UpdateVersion? Usable(UpdateRequest request, string staging)
    {
        var target = request.TargetDirectory;
        var exe = request.ExePath;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var usable = UpdateVersion.TryParse(request.Version, out var version)
            && UpdateVersion.TryParse(Path.GetFileName(staging), out var folder) && folder == version
            && request.Form == PackageForm.WindowsInstaller
            && target is not null && Path.IsPathFullyQualified(target)
            && exe is not null && Path.IsPathFullyQualified(exe)
            && string.Equals(Path.GetFileName(exe), WindowsUpdateInstaller.ExeName, comparison)
            && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(exe) ?? string.Empty), Path.TrimEndingDirectorySeparator(target), comparison);
        return usable ? version : null;
    }

    // The callers discard the task, so nothing may escape it: an exception no step plans for (a failing file system call in the check, a starter that throws) ends like any
    // other failure of the setup program: the installed version starts again, and the window shows the sentence with "Try again". Never throws.
    private async Task GuardedAsync(Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            try
            {
                StartInstalled();
            }
            catch (Exception again) when (again is not OutOfMemoryException)
            {
                // The old version could not be started either: the window still says what happened.
            }

            Fail(UpdateFailure.InstallFailed, retryable: true, message: UpdateWording.ForApplier(UpdateApplyProblem.HelperInstallFailed, PdfStrings.Culture));
        }
    }

    private async Task RunCoreAsync()
    {
        if (State.Stage != UpdateStage.Verifying)
        {
            return;
        }

        // The session the app wrote, kept for every restart (the installed version uses the file up after a failure).
        _session = UpdateApplierSupport.ReadSmall(SessionPath);
        try
        {
            if (_setup.AfterProcess is { } process)
            {
                // A version that still runs would hold the files, and the restarted one would hand over to it.
                await _setup.Starter.WaitForExitAsync(process, _setup.ExitTimeout, _cancel.Token).ConfigureAwait(true);
            }

            _cancel.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            // Abandoned by "Cancel"; what was held open is free now, the rest of the update goes.
            DeleteStaging();
            return;
        }

        await AttemptAsync().ConfigureAwait(true);
    }

    private async Task AttemptAsync()
    {
        var ct = _cancel.Token;
        SetState(new UpdateState(UpdateStage.Verifying, _version, _file, _file?.Size ?? 0, null));
        int exitCode;
        try
        {
            // Held open against writing from before the check until the setup program ended: what runs is what was checked.
            using var verified = await Task.Run(
                () => UpdateApplierSupport.VerifyAsync(_staging!, _setup.ConfigDirectory, _setup.Keys, _setup.Running, PackageForm.WindowsInstaller, _version, holdFile: true, ct),
                ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                // "Cancel" came after the check, before the setup program (both on the UI thread): it never runs. The hold goes first, or the file could not be deleted.
                verified.Dispose();
                DeleteStaging();
                return;
            }

            if (verified.Problem is { } problem)
            {
                StartInstalled();
                Fail(problem == UpdateApplyProblem.Checksum ? UpdateFailure.Checksum : UpdateFailure.Rejected, retryable: false);
                return;
            }

            _file = verified.File;
            SetState(new UpdateState(UpdateStage.Installing, _version, _file, _file!.Size, null));
            exitCode = await RunSetupAsync(verified.FilePath!, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Abandoned by "Cancel"; what was held open is free now, the rest of the update goes.
            DeleteStaging();
            return;
        }

        // The setup program closes a Raffica PDF that holds its files (CloseApplications): what was started before may be gone.
        _installedStarted = false;
        if (exitCode == 0 && InstalledIsTheRequestedVersion())
        {
            SetState(new UpdateState(UpdateStage.Restarting, _version, _file, _file!.Size, null));
            DeleteStaging();
            StartInstalled();
            _setup.End();
            return;
        }

        // The permission prompt declined (Inno Setup reports a code other than 0), the setup program failed, or it ended well but the program in the folder is not the
        // requested version (Windows replaces files in use only at its next start, or the setup program updated another installation): what is there starts again,
        // the update stays for "Try again" until "Close".
        StartInstalled();
        Fail(
            UpdateFailure.InstallFailed,
            retryable: true,
            message: UpdateWording.ForApplier(exitCode == 0 ? UpdateApplyProblem.VersionMismatch : UpdateApplyProblem.HelperInstallFailed, PdfStrings.Culture));
    }

    /// <summary>
    /// Whether the program in the folder of the request reports the requested version as its product version (build metadata after <c>+</c> left out);
    /// <see langword="false"/> when it cannot be read.
    /// </summary>
    private bool InstalledIsTheRequestedVersion()
    {
        string? product;
        try
        {
            product = _setup.ProductVersionOf(InstalledExe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        if (product is null)
        {
            return false;
        }

        var plus = product.IndexOf('+', StringComparison.Ordinal);
        return UpdateVersion.TryParse(plus >= 0 ? product[..plus] : product, out var installed) && installed == _version;
    }

    private async Task<int> RunSetupAsync(string path, CancellationToken ct)
    {
        try
        {
            var outcome = await _setup.Runner.RunAsync(path, InnoArguments, _setup.InstallTimeout, ct).ConfigureAwait(true);

            // Not started, or stopped after the time limit: a failure of the setup program.
            return outcome?.ExitCode ?? -1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return -1;
        }
    }

    /// <summary>"Cancel": the installed version starts again, the update goes, the helper ends.</summary>
    private void Abandon()
    {
        StartInstalled();
        DeleteStaging();
        SetState(UpdateState.None);
        _setup.End();
    }

    /// <summary>
    /// Starts Raffica PDF from the installed folder, with the session of the app when it wrote one; once until the setup program runs again (a second start would only
    /// hand over to the first).
    /// </summary>
    private void StartInstalled()
    {
        if (Request is null || _installedStarted)
        {
            return;
        }

        _installedStarted = true;
        var arguments = new List<string>();
        if (_session is { } session && TryWriteSession(session))
        {
            arguments.Add("--restore-session");
        }

        _setup.Starter.Start(InstalledExe, arguments);
    }

    // The session file again, as the app wrote it: a restarted version has used it up (the start with --restore-session deletes it).
    private bool TryWriteSession(byte[] session)
    {
        var temp = SessionPath + ".tmp";
        try
        {
            File.WriteAllBytes(temp, session);
            File.Move(temp, SessionPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do.
            }

            return false;
        }
    }

    private void Fail(string error, bool retryable, string? message = null)
    {
        _retryable = retryable;
        _failureMessage = message;
        SetState(State with { Stage = UpdateStage.Failed, Error = error });
    }

    private void DeleteStaging()
    {
        if (_staging is not null)
        {
            // The next run of Raffica PDF removes what is left (UpdateStaging.DeleteOld).
            UpdateApplierSupport.TryDeleteDirectory(_staging);
        }
    }

    private void SetState(UpdateState state)
    {
        State = state;
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A handler of the window must not throw; one that does must not stop the installation.
            }
        }
    }
}
