using System.Globalization;
using Raffica.Core.Updates;
using Raffica.Pdf.Updating;

namespace Raffica.Pdf.Platform.Windows;

/// <summary>What the Windows installer of the app needs.</summary>
/// <param name="ProgramDirectory">The folder of the running program, which is copied for the helper.</param>
/// <param name="UpdaterDirectory">Where the copies go (<c>%LOCALAPPDATA%\Raffica\updater</c>, a temporary folder in tests).</param>
/// <param name="ConfigDirectory">The config folder of the user: the <c>updates</c> folder of the staging and the session the window wrote.</param>
/// <param name="Keys">The keys the manifest must be signed with (<see cref="UpdateTrust.Keys"/>; test keys only in tests).</param>
/// <param name="Running">The running version: the manifest must be newer.</param>
/// <param name="ProcessId">The process of the app, which the helper waits for.</param>
/// <param name="Starter">Starts the helper.</param>
/// <param name="Shutdown">Ends the app once the helper runs (the window has asked about unsaved work already).</param>
/// <param name="Clock">Decides which old copies are left behind.</param>
internal sealed record WindowsUpdateInstallerSetup(
    string ProgramDirectory,
    string UpdaterDirectory,
    string ConfigDirectory,
    IReadOnlyList<UpdateKey> Keys,
    UpdateVersion Running,
    int ProcessId,
    IUpdateProcessStarter Starter,
    Action Shutdown,
    TimeProvider Clock);

/// <summary>
/// Installs a verified update on Windows (<see cref="PackageForm.WindowsInstaller"/>). Windows cannot replace the running program, so it hands over to a helper:
/// first it checks the staged file against the signed manifest again (the staging folder lay on the disk since the download), then it copies the program folder
/// (with the native libraries next to the program) to <c>%LOCALAPPDATA%\Raffica\updater\&lt;guid&gt;\</c> (copies older than <see cref="CopyLifetime"/> are
/// deleted first), starts the copy with <c>--finish-update &lt;staging folder&gt; --after-process &lt;this process&gt;</c> and ends the app. The window wrote the
/// session before it called, so no session is captured here; a failure deletes it again. The helper (<see cref="WindowsUpdateHelper"/>) verifies once more,
/// installs and starts Raffica PDF again. Nothing is changed before the file matched the manifest; once the helper runs the call never throws.
/// </summary>
internal sealed class WindowsUpdateInstaller(WindowsUpdateInstallerSetup setup) : IUpdateApplier
{
    /// <summary>The name of the program in its folder and in the copy.</summary>
    public const string ExeName = "RafficaPdf.exe";

    /// <summary>How long a copy of the helper may stay before the next installation deletes it.</summary>
    public static readonly TimeSpan CopyLifetime = TimeSpan.FromDays(1);

    /// <summary>Gets the folder of the helper copies below <paramref name="localAppData"/>.</summary>
    public static string UpdaterDirectoryIn(string localAppData) => Path.Combine(localAppData, "Raffica", "updater");

    /// <summary>Gets the command line of the helper: one element per argument, so a path with spaces or non-ASCII characters is never split or quoted by hand.</summary>
    public static IReadOnlyList<string> HelperArguments(string staging, int processId) =>
        ["--finish-update", staging, "--after-process", processId.ToString(CultureInfo.InvariantCulture)];

    /// <inheritdoc/>
    public async Task<ApplyResult> ApplyAsync(StagedUpdate staged, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(staged);
        if (staged.Form != PackageForm.WindowsInstaller)
        {
            return UpdateApplierSupport.Fail(UpdateApplyProblem.NoInstaller, offersDownloadPage: true);
        }

        ct.ThrowIfCancellationRequested();

        // The file lay on the disk since the download: it must still be the one the signed manifest names before anything is changed.
        using (var verified = await UpdateApplierSupport.VerifyAsync(
            staged.Directory, setup.ConfigDirectory, setup.Keys, setup.Running, PackageForm.WindowsInstaller, staged.Version, holdFile: false, ct).ConfigureAwait(true))
        {
            if (verified.Problem is { } problem)
            {
                UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
                return UpdateApplierSupport.Fail(problem);
            }
        }

        var now = setup.Clock.GetUtcNow();
        var (copy, failure) = await Task.Run(
            () =>
            {
                DeleteOldCopies(setup.UpdaterDirectory, now);
                return CopyProgram(setup.ProgramDirectory, setup.UpdaterDirectory);
            },
            ct).ConfigureAwait(true);
        if (copy is null)
        {
            UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
            return failure ?? UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed);
        }

        if (!setup.Starter.Start(Path.Combine(copy, ExeName), HelperArguments(staged.Directory, setup.ProcessId)))
        {
            UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
            UpdateApplierSupport.TryDeleteDirectory(copy);
            return UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed);
        }

        // Taken over: from here nothing throws (IUpdateApplier). The helper waits until this process has ended.
        try
        {
            setup.Shutdown();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The helper runs and waits for the end of this process; a shutdown that fails here does not undo that.
        }

        return ApplyResult.Restarting;
    }

    /// <summary>Copies the files of the program folder (the program and its native libraries); no copy and the failure on a file system problem.</summary>
    private static (string? Copy, ApplyResult? Failure) CopyProgram(string programDirectory, string updaterDirectory)
    {
        var copy = Path.Combine(updaterDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(copy);
            foreach (var file in Directory.EnumerateFiles(programDirectory))
            {
                File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
            }

            if (File.Exists(Path.Combine(copy, ExeName)))
            {
                return (copy, null);
            }

            UpdateApplierSupport.TryDeleteDirectory(copy);
            return (null, UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            UpdateApplierSupport.TryDeleteDirectory(copy);
            return (null, UpdateApplierSupport.WriteFailure(exception, updaterDirectory));
        }
    }

    /// <summary>Deletes the copies (folders named by a GUID) last written more than <see cref="CopyLifetime"/> ago.</summary>
    private static void DeleteOldCopies(string updaterDirectory, DateTimeOffset now)
    {
        try
        {
            if (!Directory.Exists(updaterDirectory))
            {
                return;
            }

            foreach (var folder in Directory.EnumerateDirectories(updaterDirectory))
            {
                if (Guid.TryParseExact(Path.GetFileName(folder), "N", out _) && now - Directory.GetLastWriteTimeUtc(folder) > CopyLifetime)
                {
                    UpdateApplierSupport.TryDeleteDirectory(folder);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleaning up is a courtesy; the next installation tries again.
        }
    }
}
