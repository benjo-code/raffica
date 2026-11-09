using System.Globalization;
using System.Security.Cryptography;
using Raffica.Core.Updates;
using Raffica.Pdf.Updating;

namespace Raffica.Pdf.Platform.Linux;

/// <summary>
/// The file operations of the AppImage updater on the folder of the running AppImage, as a seam: the real one is <see cref="PhysicalAppImageFileSystem"/>, tests drive a
/// fake so every step and every failure can be exercised without a Linux system.
/// </summary>
internal interface IAppImageFileSystem
{
    /// <summary>Whether a file exists at <paramref name="path"/>.</summary>
    bool Exists(string path);

    /// <summary>Makes a new file for writing; never opens an existing name (an existing file, a symbolic link or a hard link would be written through). Throws for the file system.</summary>
    Stream CreateNew(string path);

    /// <summary>Opens a file for reading. Throws for the file system.</summary>
    Stream OpenRead(string path);

    /// <summary>Sets the mode to <c>0755</c> (a no-op where the system has no such mode). Throws for the file system.</summary>
    void SetExecutable(string path);

    /// <summary>Replaces <paramref name="to"/> by <paramref name="from"/> in one step, <c>rename(2)</c>, so a failure leaves <paramref name="to"/> as it was. Throws for the file system.</summary>
    void Rename(string from, string to);

    /// <summary>Deletes a file or link as an entry (never what a link points to); a missing file is no failure. Never throws.</summary>
    void Delete(string path);
}

/// <summary>The real file system for the AppImage updater.</summary>
internal sealed class PhysicalAppImageFileSystem : IAppImageFileSystem
{
    /// <summary>The mode <c>0755</c>: the owner reads, writes and executes; group and others read and execute.</summary>
    public const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <inheritdoc/>
    public bool Exists(string path) => File.Exists(path);

    /// <inheritdoc/>
    public Stream CreateNew(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

    /// <inheritdoc/>
    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <inheritdoc/>
    public void SetExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, ExecutableMode);
        }
    }

    /// <inheritdoc/>
    public void Rename(string from, string to) => File.Move(from, to, overwrite: true);

    /// <inheritdoc/>
    public void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be deleted is replaced by the next attempt (it is made with CreateNew after a delete).
        }
    }
}

/// <summary>What the installer of an AppImage needs.</summary>
/// <param name="TargetPath">The running AppImage: the path in the <c>APPIMAGE</c> variable.</param>
/// <param name="ProcessPath">The path of the running program; it must lie inside <paramref name="AppDirectory"/>.</param>
/// <param name="AppDirectory">The <c>APPDIR</c> of the running AppImage. Both variables are inherited by every child of an AppImage, so only a program that runs from inside this folder is that AppImage.</param>
/// <param name="ConfigDirectory">The config folder of the user: the <c>updates</c> folder of the staging and the session the window wrote.</param>
/// <param name="Keys">The keys the manifest must be signed with (<see cref="UpdateTrust.Keys"/>; test keys only in tests).</param>
/// <param name="Running">The running version: the manifest must be newer.</param>
/// <param name="ProcessId">The process of the app, which the new program waits for (<c>--after-process</c>).</param>
/// <param name="Starter">Starts the new AppImage.</param>
/// <param name="Shutdown">Ends the app once the new program runs.</param>
/// <param name="Files">The file operations in the folder of the AppImage.</param>
internal sealed record AppImageUpdaterSetup(
    string TargetPath,
    string? ProcessPath,
    string? AppDirectory,
    string ConfigDirectory,
    IReadOnlyList<UpdateKey> Keys,
    UpdateVersion Running,
    int ProcessId,
    IUpdateProcessStarter Starter,
    Action Shutdown,
    IAppImageFileSystem Files);

/// <summary>
/// Installs a verified update of an AppImage (<see cref="PackageForm.AppImage"/>): the AppImage replaces itself. The running file stays valid through its open
/// inode, so the file can be swapped while the app runs. Steps, none of which touches the old file before the new one is complete and checked: the manifest and the staged
/// file are verified again with the keys of the program; the staged file is copied to <c>&lt;AppImage&gt;.part</c> next to the old one (the same file system, so the last step is
/// atomic) as a new file (a stale file or link there is removed first, never followed); the copy is checked against the size and SHA-256 of the manifest and given the
/// mode <c>0755</c>; only then it is renamed over the old file with <c>rename(2)</c>, so a failure leaves the old file as it was; the new AppImage starts with
/// <c>--after-process &lt;this process&gt;</c> (it waits until this process has ended) and the app ends. The window wrote the session before it called; a failure deletes it again.
/// </summary>
/// <remarks>
/// A folder this user cannot write to (a read-only folder or mount) fails with the sentence for the download page, a full disk deletes the <c>.part</c> file and fails
/// with the storage sentence; a new file that does not start after the swap fails with a sentence that says it is installed and starts next time; any other failure leaves the
/// old file and the app as they are. The staging folder is left to the flow on a failure (it removes it) and deleted here once the file is replaced.
/// </remarks>
internal sealed class AppImageUpdater(AppImageUpdaterSetup setup) : IUpdateApplier
{
    /// <summary>The suffix of the copy next to the AppImage.</summary>
    public const string PartSuffix = ".part";

    private const int ChunkSize = 81920;

    /// <inheritdoc/>
    public async Task<ApplyResult> ApplyAsync(StagedUpdate staged, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(staged);
        if (staged.Form != PackageForm.AppImage || !PackageFormDetector.IsInsideAppDir(setup.ProcessPath, setup.AppDirectory))
        {
            // Not provably this AppImage (APPIMAGE may be inherited from another one): no file is touched.
            return UpdateApplierSupport.Fail(UpdateApplyProblem.NoInstaller, offersDownloadPage: true);
        }

        ct.ThrowIfCancellationRequested();
        var result = await Task.Run(() => SwapAsync(staged, ct), ct).ConfigureAwait(true);
        if (result.IsRestarting)
        {
            // Taken over: from here nothing throws (IUpdateApplier). The new program waits until this process has ended.
            try
            {
                setup.Shutdown();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The new program runs and waits for the end of this process; a shutdown that fails here does not undo that.
            }
        }

        return result;
    }

    /// <summary>Gets the command line of the new AppImage: one element per argument.</summary>
    public static IReadOnlyList<string> NewProgramArguments(int processId, bool restoreSession)
    {
        var arguments = new List<string> { "--after-process", processId.ToString(CultureInfo.InvariantCulture) };
        if (restoreSession)
        {
            arguments.Add("--restore-session");
        }

        return arguments;
    }

    private async Task<ApplyResult> SwapAsync(StagedUpdate staged, CancellationToken ct)
    {
        using (var verified = await UpdateApplierSupport.VerifyAsync(
            staged.Directory, setup.ConfigDirectory, setup.Keys, setup.Running, PackageForm.AppImage, staged.Version, holdFile: false, ct).ConfigureAwait(false))
        {
            if (verified.Problem is { } problem)
            {
                UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
                return UpdateApplierSupport.Fail(problem);
            }

            return await ReplaceAsync(staged, verified, ct).ConfigureAwait(false);
        }
    }

    private async Task<ApplyResult> ReplaceAsync(StagedUpdate staged, VerifiedUpdate verified, CancellationToken ct)
    {
        var target = setup.TargetPath;
        var files = setup.Files;
        if (!Path.IsPathFullyQualified(target) || Path.GetDirectoryName(target) is not { Length: > 0 } folder || !files.Exists(target))
        {
            // Not an AppImage we can replace (the variable points nowhere): only the download page helps.
            UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
            return UpdateApplierSupport.Fail(UpdateApplyProblem.NoInstaller, offersDownloadPage: true);
        }

        var part = target + PartSuffix;
        var file = verified.File!;
        var created = false;
        try
        {
            // A new file of its own: opening an existing name would write through a symbolic link, or into the inode a hard link shares with another file.
            if (files.Exists(part))
            {
                files.Delete(part);
            }

            Stream destination;
            try
            {
                destination = files.CreateNew(part);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Nothing could be created: a write-protected folder (or a read-only mount), which the user solves by hand.
                UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
                return UpdateApplierSupport.WriteFailure(exception, folder);
            }

            created = true;
            try
            {
                await using (destination.ConfigureAwait(false))
                {
                    await using var input = new FileStream(verified.FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await input.CopyToAsync(destination, ChunkSize, ct).ConfigureAwait(false);
                    await destination.FlushAsync(ct).ConfigureAwait(false);
                    if (destination is FileStream disk)
                    {
                        disk.Flush(flushToDisk: true);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A full disk (the .part file goes below) or a staged file that is gone.
                UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
                return UpdateApplierSupport.WriteFailure(exception, folder);
            }

            // The copy is what runs next, so the copy is what is checked (the staged file may have changed since).
            if (!await CopyMatchesAsync(part, file, ct).ConfigureAwait(false))
            {
                UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
                return UpdateApplierSupport.Fail(UpdateApplyProblem.Checksum);
            }

            // From here the update is taken: nothing below observes the cancellation token. The mode is set before the rename, so the file that replaces the old one
            // is executable from its first moment.
            try
            {
                files.SetExecutable(part);
                files.Rename(part, target);
                created = false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
                return UpdateApplierSupport.Fail(UpdateApplyProblem.InstallFailed);
            }

            if (!setup.Starter.Start(target, NewProgramArguments(setup.ProcessId, UpdateApplierSupport.HasSession(setup.ConfigDirectory))))
            {
                // The file is replaced (the next start is the new version) but nothing waits for the session; the sentence says that, not that the version stays.
                UpdateApplierSupport.DeleteSession(setup.ConfigDirectory);
                return UpdateApplierSupport.Fail(UpdateApplyProblem.NotStarted);
            }

            UpdateApplierSupport.TryDeleteDirectory(verified.Directory!);
            return ApplyResult.Restarting;
        }
        finally
        {
            if (created)
            {
                files.Delete(part);
            }
        }
    }

    // The size and SHA-256 of the copy, read back through the file system seam (the file may be on a disk that returns what it was not given).
    private async Task<bool> CopyMatchesAsync(string path, UpdateFile file, CancellationToken ct)
    {
        try
        {
            if (file.Sha256 is not { Length: 64 })
            {
                return false;
            }

            var expected = Convert.FromHexString(file.Sha256);
            await using var stream = setup.Files.OpenRead(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[ChunkSize];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                total += read;
            }

            return total == file.Size && CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }
}
