using Raffica.Core.Updates;
using Raffica.Pdf.Updating;

namespace Raffica.Pdf.Platform.MacOS;

/// <summary>What the updater of a macOS bundle needs.</summary>
/// <param name="BundlePath">The running <c>.app</c> folder (<see cref="MacBundle.PathOf"/>).</param>
/// <param name="Form">The form of the running program: <see cref="PackageForm.MacAppArm64"/> or <see cref="PackageForm.MacAppX64"/>.</param>
/// <param name="ConfigDirectory">The config folder of the user: the <c>updates</c> folder of the staging and the session the window wrote.</param>
/// <param name="Keys">The keys the manifest must be signed with (<see cref="UpdateTrust.Keys"/>; test keys only in tests).</param>
/// <param name="Running">The running version: the manifest must be newer.</param>
/// <param name="ProcessId">The process of the app, which the swap script waits for.</param>
/// <param name="Runner">Runs the system programs (<c>hdiutil</c>, <c>ditto</c>, <c>codesign</c>, <c>plutil</c>, <c>xattr</c>) with an argument list.</param>
/// <param name="Starter">Starts the swap script.</param>
/// <param name="Shutdown">Ends the app once the script runs.</param>
/// <param name="CreateFolder">Makes the folder entry of the staged bundle (a seam: tests make it fail the way a write-protected folder does); <see langword="null"/> is <see cref="Directory.CreateDirectory(string)"/>.</param>
/// <param name="DeleteEntry">Removes a folder, file or link entry (a seam: tests leave an entry behind the way a protected one stays); <see langword="null"/> is <see cref="MacAppUpdater.DeleteEntryByDefault(string)"/>.</param>
internal sealed record MacAppUpdaterSetup(
    string BundlePath,
    PackageForm Form,
    string ConfigDirectory,
    IReadOnlyList<UpdateKey> Keys,
    UpdateVersion Running,
    int ProcessId,
    IProcessRunner Runner,
    IUpdateProcessStarter Starter,
    Action Shutdown,
    Action<string>? CreateFolder = null,
    Action<string>? DeleteEntry = null);

/// <summary>
/// Installs a verified update of a macOS bundle: the disk image is mounted read-only at a mount point of our own (so no output is parsed), the one <c>.app</c> on it is
/// copied next to the installed bundle (the same volume, so the last step is a rename), the image is detached on every path, the copy is checked (<c>codesign
/// --verify</c>, the version and the bundle identifier) and, only when it carries one, the quarantine attribute is removed. Last, <see cref="MacSwapScript"/> is started: it
/// waits for this process, swaps the bundles and opens the new one. Nothing is changed in the installed bundle before the script runs; the window wrote the session
/// before it called, and every failure deletes it again. A bundle that runs from a mounted image or from a translocated copy is refused before anything is touched.
/// </summary>
internal sealed class MacAppUpdater(MacAppUpdaterSetup setup) : IUpdateApplier
{
    /// <summary>The folder entry of the new bundle next to the installed one.</summary>
    public const string StagedBundleName = ".Raffica PDF update.app";

    /// <summary>The folder entry the old bundle moves to during the swap.</summary>
    public const string BackupBundleName = ".Raffica PDF previous.app";

    /// <summary>The mount point inside the staging folder.</summary>
    public const string MountFolderName = "mnt";

    internal const string Hdiutil = "/usr/bin/hdiutil";
    internal const string Ditto = "/usr/bin/ditto";
    internal const string Codesign = "/usr/bin/codesign";
    internal const string Plutil = "/usr/bin/plutil";
    internal const string Xattr = "/usr/bin/xattr";
    internal const string Opener = "/usr/bin/open";
    internal const string Shell = "/bin/sh";
    internal const string QuarantineAttribute = "com.apple.quarantine";

    private static readonly TimeSpan AttachTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DittoTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan CodesignTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(60);

    /// <inheritdoc/>
    public async Task<ApplyResult> ApplyAsync(StagedUpdate staged, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(staged);
        if (staged.Form != setup.Form || setup.Form is not (PackageForm.MacAppArm64 or PackageForm.MacAppX64)
            || MacBundle.LocationOf(setup.BundlePath) != MacBundleLocation.Installed
            || Path.GetDirectoryName(setup.BundlePath) is not { Length: > 0 })
        {
            // From a disk image or a copy macOS moved aside the bundle cannot be replaced: nothing is touched.
            UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
            return UpdateApplierSupport.Fail(UpdateApplyProblem.RunsFromDownload, offersDownloadPage: true);
        }

        ct.ThrowIfCancellationRequested();
        var result = await InstallAsync(staged, ct).ConfigureAwait(false);
        if (result.IsRestarting)
        {
            // Taken over: from here nothing throws (IUpdateApplier). The script waits until this process has ended.
            try
            {
                setup.Shutdown();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The script runs and waits for the end of this process; a shutdown that fails here does not undo that.
            }
        }

        return result;
    }

    private async Task<ApplyResult> InstallAsync(StagedUpdate staged, CancellationToken ct)
    {
        using var verified = await UpdateApplierSupport.VerifyAsync(
            staged.Directory, setup.ConfigDirectory, setup.Keys, setup.Running, setup.Form, staged.Version, holdFile: false, ct).ConfigureAwait(false);
        if (verified.Problem is { } problem)
        {
            UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
            return UpdateApplierSupport.Fail(problem);
        }

        var parent = Path.GetDirectoryName(setup.BundlePath)!;
        var newBundle = Path.Combine(parent, StagedBundleName);
        var backup = Path.Combine(parent, BackupBundleName);
        var mount = Path.Combine(verified.Directory!, MountFolderName);
        try
        {
            var result = await StageAndSwapAsync(staged, verified, parent, newBundle, backup, mount, ct).ConfigureAwait(false);
            if (result.IsRestarting)
            {
                UpdateApplierSupport.TryDeleteDirectory(verified.Directory!);
                return result;
            }

            UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
            DeleteEntry(newBundle);
            return result;
        }
        catch (OperationCanceledException)
        {
            // Cancelled before the script started: the staged bundle goes, the flow removes the staging folder.
            UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
            DeleteEntry(newBundle);
            throw;
        }
    }

    private async Task<ApplyResult> StageAndSwapAsync(
        StagedUpdate staged, VerifiedUpdate verified, string parent, string newBundle, string backup, string mount, CancellationToken ct)
    {
        // The staged bundle's entry next to the installed one proves that the folder can be written before any program runs.
        try
        {
            DeleteEntry(newBundle);
            DeleteEntry(backup);

            // A leftover that stayed (a protected entry, a busy file) is not merged into: the copy would mix old and new files, and the swap
            // would move a backup entry that is not ours. The folder is named as not writable.
            foreach (var stale in new[] { newBundle, backup })
            {
                if (EntryExists(stale))
                {
                    throw new UnauthorizedAccessException($"A leftover of an earlier update could not be removed: {stale}");
                }
            }

            (setup.CreateFolder ?? (path => Directory.CreateDirectory(path)))(newBundle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UpdateApplierSupport.WriteFailure(exception, parent);
        }

        // A mount a crashed attempt left behind (the result does not matter).
        await setup.Runner.RunAsync(Hdiutil, ["detach", mount, "-force"], QuickTimeout, CancellationToken.None).ConfigureAwait(false);
        DeleteEmptyFolder(mount);

        var copied = await CopyFromImageAsync(verified.FilePath!, mount, newBundle, ct).ConfigureAwait(false);
        if (copied is not null)
        {
            return copied;
        }

        ct.ThrowIfCancellationRequested();
        var check = await CheckBundleAsync(staged, newBundle, ct).ConfigureAwait(false);
        if (check is not null)
        {
            return check;
        }

        // Only a bundle that passed every check loses its quarantine mark, and only when it has one.
        var quarantine = await setup.Runner.RunAsync(Xattr, ["-p", QuarantineAttribute, newBundle], QuickTimeout, ct).ConfigureAwait(false);
        if (quarantine is { ExitCode: 0 })
        {
            var removed = await setup.Runner.RunAsync(Xattr, ["-dr", QuarantineAttribute, newBundle], QuickTimeout, ct).ConfigureAwait(false);
            if (removed is not { ExitCode: 0 })
            {
                return UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed);
            }
        }

        // From here the update is taken: nothing below observes the cancellation token.
        var arguments = MacSwapScript.Arguments(
            setup.ProcessId, setup.BundlePath, newBundle, backup, Opener, Codesign, UpdateApplierSupport.HasSession(setup.ConfigDirectory));
        return setup.Starter.Start(Shell, arguments)
            ? ApplyResult.Restarting
            : UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed);
    }

    // Mounts the image, copies its one app and detaches again on every path after the attach. Returns a failure, or null.
    private async Task<ApplyResult?> CopyFromImageAsync(string image, string mount, string newBundle, CancellationToken ct)
    {
        var mounted = false;
        try
        {
            Directory.CreateDirectory(mount);
            mounted = true;
            var attach = await setup.Runner.RunAsync(
                Hdiutil, ["attach", "-readonly", "-nobrowse", "-noautoopen", "-noverify", "-mountpoint", mount, image], AttachTimeout, ct).ConfigureAwait(false);
            if (attach is not { ExitCode: 0 })
            {
                // A null outcome (not started, or no answer within the time limit) is a failure like a non-zero exit, never success. Only a
                // real exit code proves that nothing was mounted: after a timeout the image may have been attached just before, so it stays
                // in the detach below.
                mounted = attach is null;
                return UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed);
            }

            string[] apps;
            try
            {
                apps = [.. Directory.EnumerateDirectories(mount, "*.app")];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                apps = [];
            }

            if (apps.Length != 1)
            {
                return UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed);
            }

            var copy = await setup.Runner.RunAsync(Ditto, [apps[0], newBundle], DittoTimeout, ct).ConfigureAwait(false);
            return copy is { ExitCode: 0 } ? null : UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed);
        }
        finally
        {
            if (mounted)
            {
                await DetachAsync(mount).ConfigureAwait(false);
                DeleteEmptyFolder(mount);
            }
        }
    }

    private async Task DetachAsync(string mount)
    {
        var plain = await setup.Runner.RunAsync(Hdiutil, ["detach", mount], QuickTimeout, CancellationToken.None).ConfigureAwait(false);
        if (plain is not { ExitCode: 0 })
        {
            await setup.Runner.RunAsync(Hdiutil, ["detach", mount, "-force"], QuickTimeout, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // The signature, the version and the identifier of the copy. Returns a failure, or null.
    private async Task<ApplyResult?> CheckBundleAsync(StagedUpdate staged, string newBundle, CancellationToken ct)
    {
        var signature = await setup.Runner.RunAsync(Codesign, ["--verify", "--deep", "--strict", newBundle], CodesignTimeout, ct).ConfigureAwait(false);
        if (signature is not { ExitCode: 0 })
        {
            return UpdateApplierSupport.Fail(UpdateApplyProblem.Rejected);
        }

        var plist = Path.Combine(newBundle, "Contents", "Info.plist");
        var version = await ReadPlistAsync("CFBundleShortVersionString", plist, ct).ConfigureAwait(false);
        var identifier = await ReadPlistAsync("CFBundleIdentifier", plist, ct).ConfigureAwait(false);
        return version == staged.Version.ToString() && identifier == MacBundle.Identifier
            ? null
            : UpdateApplierSupport.Fail(UpdateApplyProblem.VersionMismatch);
    }

    private async Task<string?> ReadPlistAsync(string key, string plist, CancellationToken ct)
    {
        var outcome = await setup.Runner.RunAsync(Plutil, ["-extract", key, "raw", "-o", "-", plist], QuickTimeout, ct).ConfigureAwait(false);
        return outcome is { ExitCode: 0 } ? outcome.Output.Trim() : null;
    }

    private void DeleteEntry(string path) => (setup.DeleteEntry ?? DeleteEntryByDefault)(path);

    // Whether anything (a folder, a file, a link even to nothing) is at the path.
    private static bool EntryExists(string path) =>
        Directory.Exists(path) || File.Exists(path) || new FileInfo(path).LinkTarget is not null;

    /// <summary>Removes a folder, file or link as an entry (a link is never followed); a failure leaves it in place.</summary>
    /// <param name="path">The entry.</param>
    internal static void DeleteEntryByDefault(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The caller looks again whether the entry is gone (EntryExists); a leftover that stays is a failure there.
        }
    }

    // Never recursive: a folder that is still a mount point must not lose what is mounted on it.
    private static void DeleteEmptyFolder(string path)
    {
        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not empty or not there: nothing to remove.
        }
    }
}
