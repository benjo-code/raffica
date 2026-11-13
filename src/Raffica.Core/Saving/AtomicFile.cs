using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Raffica.Core.Saving;

/// <summary>
/// Replaces a file atomically: a finished temporary file next to the target takes the target's place. Shared by the save of a
/// PDF (<see cref="DocumentSaver"/>), the image export and the save of a DOCX. Failures are <see cref="SaveFailureException"/>
/// with a <see cref="SaveFailureReason"/>; the target stays as it was.
/// </summary>
public static class AtomicFile
{
    private const string InUse = "The file is in use by another program.";

    /// <summary><c>ERROR_SHARING_VIOLATION</c>: another handle without the needed sharing has the file open.</summary>
    private const int ErrorSharingViolation = 32;

    /// <summary><c>ERROR_LOCK_VIOLATION</c>.</summary>
    private const int ErrorLockViolation = 33;

    /// <summary>Whether <paramref name="error"/> or one of its inner exceptions reports a full disk or quota.</summary>
    internal static bool IsDiskFull(Exception? error)
    {
        for (; error is not null; error = error.InnerException)
        {
            if (error is IOException && IsDiskFullCode(error.HResult))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Windows reports <c>ERROR_DISK_FULL</c> (112) or <c>ERROR_HANDLE_DISK_FULL</c> (39) as an HRESULT of FACILITY_WIN32;
    /// .NET on Linux puts the raw errno into the HResult, <c>ENOSPC</c> (28) or <c>EDQUOT</c> (122).
    /// </summary>
    private static bool IsDiskFullCode(int hresult)
    {
        if (OperatingSystem.IsWindows())
        {
            return Win32CodeOf(hresult) is 112 or 39;
        }

        return hresult is 28 or 122;
    }

    /// <summary>Whether a Windows error says another handle without the needed sharing has the file open.</summary>
    private static bool IsSharingViolation(Exception error) =>
        OperatingSystem.IsWindows() && error is IOException && Win32CodeOf(error.HResult) is ErrorSharingViolation or ErrorLockViolation;

    /// <summary>The Win32 error code of an HRESULT of FACILITY_WIN32, otherwise -1.</summary>
    private static int Win32CodeOf(int hresult)
    {
        const int FacilityWin32 = 7;
        return ((hresult >> 16) & 0x1FFF) == FacilityWin32 ? hresult & 0xFFFF : -1;
    }

    /// <summary>
    /// The file a symbolic link at <paramref name="path"/> finally points to (a full path), or <paramref name="path"/> itself when
    /// it is no link, the chain cannot be followed or the system refuses to look. Never throws for I/O or permission errors: the
    /// input path is returned then.
    /// </summary>
    public static string ResolveLink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } final ? final.FullName : path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>A short name independent of the target's name, so a long target name never makes it too long.</summary>
    internal static string ShortName(string extension) => ".pw-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)) + extension;

    /// <summary>
    /// Moves the checked temporary file over the target. Linux: one atomic <c>rename</c>, which replaces the target even while
    /// it is open. Windows: the atomic POSIX rename (<see cref="WindowsFileRename.ReplaceAtomically"/>); Windows' ordinary
    /// rename refuses to replace a file another handle has open, even one shared for deleting, and the old document of the
    /// tab always has it open. Only when the file system does not offer the POSIX rename (FAT, exFAT, some SMB servers) is
    /// the ordinary rename tried, and if the open target blocks it, the target is moved aside to <c>.pw-&lt;12 hex&gt;.old</c>
    /// (renaming a file opened shared for deleting is allowed), the temporary file takes its name and the old file is
    /// deleted (four attempts, 100 ms apart). If that second move fails, the old file is moved back.
    /// </summary>
    /// <returns>The path of the old file when the fallback could not delete it, otherwise <see langword="null"/>.</returns>
    internal static string? Replace(string temporary, string target, string folder, bool replaceExisting, SaveFaults? faults)
    {
        if (OperatingSystem.IsWindows())
        {
            var error = faults?.AtomicReplace is { } replace ? replace(temporary, target) : WindowsFileRename.ReplaceAtomically(temporary, target, replaceExisting);
            if (error == 0)
            {
                return null;
            }

            if (!replaceExisting && error is WindowsFileRename.ErrorAlreadyExists or WindowsFileRename.ErrorFileExists)
            {
                throw TargetExists(new Win32Exception(error));
            }

            if (!WindowsFileRename.IsUnsupported(error))
            {
                throw FailureOfWin32(error);
            }
        }

        if (!replaceExisting)
        {
            // Never replaces: .NET moves without overwriting by linking the new name first and then removing the old one, so a
            // file that appears at the target at any moment before that ends here and stays as it is.
            try
            {
                File.Move(temporary, target, overwrite: false);
                return null;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                throw File.Exists(target) || Directory.Exists(target)
                    ? TargetExists(ex)
                    : FailureOf(ex, "The file could not be written.");
            }
        }

        try
        {
            File.Move(temporary, target, overwrite: true);
            return null;
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows() && File.Exists(target))
        {
            // Without POSIX rename Windows refuses to rename over an open target (ACCESS_DENIED); a read-only target was
            // refused before anything was written. Replace it in three steps below.
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw FailureOf(ex, "The file could not be replaced.");
        }

        var aside = Path.Combine(folder, ShortName(".old"));
        try
        {
            File.Move(target, aside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw FailureOf(ex, "The file could not be replaced.");
        }

        try
        {
            faults?.AfterTargetMovedAside?.Invoke();
            File.Move(temporary, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            try
            {
                File.Move(aside, target);
            }
            catch (Exception restore) when (restore is UnauthorizedAccessException or IOException)
            {
                // Practically unreachable: the name was free a moment ago. The old content is kept under the other name.
                throw new SaveFailureException(
                    SaveFailureReason.WriteFailed,
                    $"The file could not be replaced, and the original could not be moved back; it is kept as '{aside}'.",
                    new AggregateException(ex, restore));
            }

            throw FailureOf(ex, "The file could not be replaced.");
        }

        return DeleteAside(aside, faults);
    }

    /// <summary>
    /// Moves a finished temporary file over (or, without <paramref name="replaceExisting"/>, next to) <paramref name="target"/>
    /// with the same rules as the save of a document: atomic where the file system allows it, and without
    /// <paramref name="replaceExisting"/> never replacing a file that appears at the target meanwhile. For any file the caller wrote
    /// to a temporary file in the folder of the target first.
    /// </summary>
    /// <param name="temporary">The full path of the finished temporary file, in the folder of <paramref name="target"/>.</param>
    /// <param name="target">The full path of the file to create or replace.</param>
    /// <param name="replaceExisting">Whether an existing target may be replaced.</param>
    /// <exception cref="SaveFailureException">The move failed; with <see cref="SaveFailureReason.TargetExists"/> a file stands at the target and was not replaced.</exception>
    public static void MoveIntoPlace(string temporary, string target, bool replaceExisting = true) =>
        _ = Replace(temporary, target, Path.GetDirectoryName(target) ?? string.Empty, replaceExisting, faults: null);

    /// <summary>A short temporary name independent of the target's name (<c>.pw-&lt;12 hex&gt;.tmp</c>), so a long target name never makes it too long.</summary>
    internal static string TemporaryName() => ShortName(".tmp");

    /// <summary>
    /// A path for a temporary file next to the file <paramref name="targetFullPath"/> finally points to (a symbolic link is followed),
    /// named <c>.pw-&lt;12 hex&gt;.tmp</c>; the file is not created. Write into it and move it with <see cref="MoveIntoPlace"/> onto <see cref="ResolveLink"/> of the target.
    /// </summary>
    public static string NewTemporaryPath(string targetFullPath)
    {
        var resolved = ResolveLink(targetFullPath);
        return Path.Combine(Path.GetDirectoryName(resolved) ?? string.Empty, TemporaryName());
    }

    /// <summary>
    /// Deletes the old file the Windows fallback moved aside: one attempt and three more, 100 ms apart (a virus scanner or an
    /// indexer may still hold it for a moment).
    /// </summary>
    /// <returns>The path when the file is still there after the last attempt, otherwise <see langword="null"/>.</returns>
    internal static string? DeleteAside(string path, SaveFaults? faults)
    {
        const int Retries = 3;
        var delete = faults?.DeleteAside ?? File.Delete;
        var delay = faults?.DeleteAsideDelay ?? TimeSpan.FromMilliseconds(100);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                delete(path);
                if (!File.Exists(path))
                {
                    return null;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Tried again below; the last answer is the existence of the file.
            }

            if (attempt == Retries)
            {
                return File.Exists(path) ? path : null;
            }

            Thread.Sleep(delay);
        }
    }

    private static SaveFailureException TargetExists(Exception inner) =>
        new(SaveFailureReason.TargetExists, "A file already exists at the target.", inner);

    /// <summary>Maps a write or replace error: full disk, file in use, no access, anything else.</summary>
    internal static SaveFailureException FailureOf(Exception error, string message)
    {
        if (IsDiskFull(error))
        {
            return new SaveFailureException(SaveFailureReason.DiskFull, "The disk is full.", error);
        }

        if (IsSharingViolation(error))
        {
            return new SaveFailureException(SaveFailureReason.WriteFailed, InUse, error);
        }

        return error is UnauthorizedAccessException
            ? new SaveFailureException(SaveFailureReason.NotWritable, "The file cannot be replaced.", error)
            : new SaveFailureException(SaveFailureReason.WriteFailed, message, error);
    }

    [SupportedOSPlatform("windows")]
    private static SaveFailureException FailureOfWin32(int error)
    {
        var inner = new Win32Exception(error);
        return error switch
        {
            ErrorSharingViolation or ErrorLockViolation => new SaveFailureException(SaveFailureReason.WriteFailed, InUse, inner),
            WindowsFileRename.ErrorAccessDenied => new SaveFailureException(SaveFailureReason.NotWritable, "The file cannot be replaced.", inner),
            112 or 39 => new SaveFailureException(SaveFailureReason.DiskFull, "The disk is full.", inner),
            _ => new SaveFailureException(SaveFailureReason.WriteFailed, "The file could not be replaced.", inner),
        };
    }
}
