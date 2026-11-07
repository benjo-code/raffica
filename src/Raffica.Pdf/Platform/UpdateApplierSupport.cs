using System.Text;
using Raffica.Core;
using Raffica.Core.Updates;
using Raffica.Pdf.Updating;

namespace Raffica.Pdf.Platform;

/// <summary>
/// The staged update after the check of the appliers: <see cref="Problem"/> says why it was refused, else <see cref="File"/>, <see cref="FilePath"/> and
/// <see cref="Directory"/> are set (and the file is held open against writing when asked, until this is disposed, so what runs is what was checked).
/// </summary>
internal sealed class VerifiedUpdate : IDisposable
{
    private readonly FileStream? _hold;

    private VerifiedUpdate(UpdateApplyProblem? problem, UpdateFile? file, string? directory, string? filePath, FileStream? hold)
    {
        Problem = problem;
        File = file;
        Directory = directory;
        FilePath = filePath;
        _hold = hold;
    }

    public UpdateApplyProblem? Problem { get; }

    public UpdateFile? File { get; }

    public string? Directory { get; }

    public string? FilePath { get; }

    public static VerifiedUpdate Refused(UpdateApplyProblem problem) => new(problem, null, null, null, null);

    public static VerifiedUpdate Accepted(UpdateFile file, string directory, string filePath, FileStream? hold) => new(null, file, directory, filePath, hold);

    public void Dispose() => _hold?.Dispose();
}

/// <summary>What the installers of the system (<see cref="IUpdateApplier"/>) and the Windows helper have in common: the check of a staging folder, worded failures, the session file.</summary>
internal static class UpdateApplierSupport
{
    /// <summary>Manifest, signature and session are small; anything larger is not one of ours.</summary>
    public const long MaxSmallFile = 1024 * 1024;

    // Windows: ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL and ERROR_WRITE_PROTECT as HRESULTs; Unix: ENOSPC and EROFS as the raw errno of an IOException.
    private const int DiskFullHResult = unchecked((int)0x80070070);
    private const int HandleDiskFullHResult = unchecked((int)0x80070027);
    private const int WriteProtectHResult = unchecked((int)0x80070013);
    private const int NoSpaceErrno = 28;
    private const int ReadOnlyFileSystemErrno = 30;

    /// <summary>The running version as the update flow compares it (<see cref="AppInfo.Version"/>; one that is not <c>X.Y.Z</c> counts as 0.0.0).</summary>
    public static UpdateVersion RunningVersion() => UpdateVersion.TryParse(AppInfo.Version, out var version) ? version : default;

    /// <summary>A failed result with the sentence of <paramref name="problem"/> in the language of the interface; it never throws.</summary>
    public static ApplyResult Fail(UpdateApplyProblem problem, bool offersDownloadPage = false, string? folder = null) =>
        ApplyResult.Failed(UpdateWording.ForApplier(problem, PdfStrings.Culture, folder), offersDownloadPage);

    /// <summary>
    /// The failure of writing into <paramref name="folder"/>: a write-protected folder or mount says so (with the folder), a full disk or any other file system trouble
    /// says that the update could not be saved. Both are solved by hand, so both offer the download page; the running version is unchanged.
    /// </summary>
    public static ApplyResult WriteFailure(Exception exception, string folder)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return IsWriteProtected(exception) && !IsDiskFull(exception)
            ? Fail(UpdateApplyProblem.FolderWriteProtected, offersDownloadPage: true, folder: folder)
            : Fail(UpdateApplyProblem.Storage, offersDownloadPage: true);
    }

    /// <summary>Whether the exception is the file system's "no space left" (Windows and Unix numbers).</summary>
    public static bool IsDiskFull(Exception exception) =>
        exception is IOException { HResult: DiskFullHResult or HandleDiskFullHResult or NoSpaceErrno };

    /// <summary>Whether the exception means that the folder cannot be written (no access, a write-protected disk, a read-only mount).</summary>
    public static bool IsWriteProtected(Exception exception) =>
        exception is UnauthorizedAccessException or IOException { HResult: WriteProtectHResult or ReadOnlyFileSystemErrno };

    /// <summary>
    /// Gets <paramref name="staging"/> as a full path when it is a folder directly below <c>&lt;config&gt;/updates</c> named by a version (no link), else <see langword="null"/>.
    /// </summary>
    public static string? AcceptedStaging(string staging, string configDirectory)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging));
            var updates = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(configDirectory, UpdateStaging.DirectoryName)));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetDirectoryName(full), updates, comparison) || !UpdateVersion.TryParse(Path.GetFileName(full), out _))
            {
                return null;
            }

            var folder = new DirectoryInfo(full);
            return folder.Exists && !folder.Attributes.HasFlag(FileAttributes.ReparsePoint)
                && !new DirectoryInfo(updates).Attributes.HasFlag(FileAttributes.ReparsePoint)
                ? full
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads a small file (at most <see cref="MaxSmallFile"/>); <see langword="null"/> when it is missing, unreadable or larger.</summary>
    public static byte[]? ReadSmall(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= MaxSmallFile ? File.ReadAllBytes(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks a staging folder again, from the disk: the folder is one of the updates, the signature of the manifest holds with <paramref name="keys"/>, the version is newer
    /// than <paramref name="running"/> and the one expected, the file is the one of <paramref name="form"/> (a plain name) and has the size and SHA-256 of the manifest. Nothing
    /// is changed. Only cancelling throws.
    /// </summary>
    /// <param name="stagingDirectory">The staging folder of the update.</param>
    /// <param name="configDirectory">The config folder whose <c>updates</c> folder the staging folder must be in.</param>
    /// <param name="keys">The keys the manifest must be signed with.</param>
    /// <param name="running">The running version: the manifest must be newer.</param>
    /// <param name="form">The installation form, which decides the file.</param>
    /// <param name="expected">The version the caller expects, or <see langword="null"/> for any newer one.</param>
    /// <param name="holdFile">Whether to keep the file open against writing until the result is disposed.</param>
    /// <param name="cancellationToken">Cancels the check of the file.</param>
    public static async Task<VerifiedUpdate> VerifyAsync(
        string stagingDirectory,
        string configDirectory,
        IReadOnlyList<UpdateKey> keys,
        UpdateVersion running,
        PackageForm form,
        UpdateVersion? expected,
        bool holdFile,
        CancellationToken cancellationToken)
    {
        if (AcceptedStaging(stagingDirectory, configDirectory) is not { } staging)
        {
            return VerifiedUpdate.Refused(UpdateApplyProblem.Rejected);
        }

        var manifestBytes = ReadSmall(Path.Combine(staging, UpdateStaging.ManifestFileName));
        var signature = ReadSmall(Path.Combine(staging, UpdateStaging.SignatureFileName));
        if (manifestBytes is null || signature is null)
        {
            return VerifiedUpdate.Refused(UpdateApplyProblem.Rejected);
        }

        var result = ManifestVerifier.Verify(manifestBytes, Encoding.UTF8.GetString(signature), keys, running);
        if (result.Manifest is not { } manifest
            || !UpdateVersion.TryParse(manifest.Version, out var version)
            || (expected is { } wanted && version != wanted)
            || !UpdateVersion.TryParse(Path.GetFileName(staging), out var folderVersion) || folderVersion != version
            || UpdateChecker.Pick(manifest, form) is not { } file
            || file.Name != Path.GetFileName(file.Name))
        {
            return VerifiedUpdate.Refused(UpdateApplyProblem.Rejected);
        }

        var path = Path.Combine(staging, file.Name);
        FileStream? hold = null;
        try
        {
            if (holdFile)
            {
                hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            if (await ChecksumVerifier.MatchesAsync(path, file.Sha256, file.Size, cancellationToken).ConfigureAwait(false))
            {
                var accepted = VerifiedUpdate.Accepted(file, staging, path, hold);
                hold = null;
                return accepted;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that is gone or cannot be read does not match.
        }
        finally
        {
            hold?.Dispose();
        }

        return VerifiedUpdate.Refused(UpdateApplyProblem.Checksum);
    }

    /// <summary>Deletes the session file (the update did not happen; the file must not outlive the attempt). Never throws.</summary>
    public static void DeleteSession(string configDirectory) =>
        new SessionStore(SessionPath(configDirectory)).Delete();

    /// <summary>The path of the session file the window wrote before it handed over to the installer.</summary>
    public static string SessionPath(string configDirectory) => Path.Combine(configDirectory, SessionStore.FileName);

    /// <summary>Whether the window wrote a session for the new version to restore.</summary>
    public static bool HasSession(string configDirectory) => File.Exists(SessionPath(configDirectory));

    /// <summary>Deletes a folder (and what is in it) if it can be; what cannot be deleted now is left for the next run.</summary>
    public static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A copy that still runs cannot be deleted; the next installation tries again.
        }
    }
}
