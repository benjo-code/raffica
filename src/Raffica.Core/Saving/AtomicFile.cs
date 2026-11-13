using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
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

    /// <summary>
    /// Flushes a finished file to disk (the data, not only the file system cache), so a crash right after the replace cannot
    /// leave a target with missing content. For a file another component wrote and closed.
    /// </summary>
    public static void FlushToDisk(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FailureOf(ex, "The file could not be written.");
        }
    }

    /// <summary>
    /// What an existing target passes on to the file that replaces it: the permissions on Unix, the Hidden attribute and the
    /// access rules on Windows. Shared by the save of a PDF and the save of a DOCX. Read it before the temporary file is made,
    /// give it to the finished temporary file with <see cref="ApplyTo(string)"/> and, after the replace, call <see cref="AfterReplace"/>.
    /// </summary>
    public sealed class TargetMetadata
    {
        private bool _hidden;
        private byte[]? _accessList;
        private byte[]? _lateAccessList;

        internal TargetMetadata()
        {
        }

        /// <summary>The permission bits of the replaced file on Unix (to create the temporary file with), otherwise null.</summary>
        public UnixFileMode? UnixMode { get; private init; }

        /// <summary>Gives the temporary file the metadata (call after its content is written and flushed).</summary>
        public void ApplyTo(string temporary) => ApplyTo(temporary, null);

        internal void ApplyTo(string temporary, SaveFaults? faults)
        {
            CopyTargetMetadata(temporary);
            _lateAccessList = OperatingSystem.IsWindows() ? GiveAccessList(temporary, _accessList, faults) : null;
        }

        /// <summary>After the replace: sets the access rules that could not be on the temporary file while it was renamed (see <see cref="GiveAccessList"/>).</summary>
        public void AfterReplace(string target)
        {
            if (_lateAccessList is not null && OperatingSystem.IsWindows())
            {
                // The rules could not be on the file while it was renamed; the replaced file gets them now.
                WriteAccessList(target, _lateAccessList);
            }
        }

        /// <summary>
        /// What an existing target passes on to its replacement; a read-only target is refused here (<see cref="SaveFailureReason.NotWritable"/>).
        /// A target that does not exist (or cannot be read) passes on nothing.
        /// </summary>
        public static TargetMetadata Read(string target)
        {
            FileAttributes attributes;
            UnixFileMode? mode = null;
            byte[]? acl = null;
            try
            {
                if (!File.Exists(target))
                {
                    return new TargetMetadata();
                }

                attributes = File.GetAttributes(target);
                if (!OperatingSystem.IsWindows())
                {
                    const UnixFileMode Permissions = (UnixFileMode)0x1FF;
                    mode = File.GetUnixFileMode(target) & Permissions;
                }
                else
                {
                    acl = ReadAccessList(target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The target went away or cannot be read; the save then goes on as for a new file.
                return new TargetMetadata();
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                throw new SaveFailureException(SaveFailureReason.NotWritable, "The file is read-only.");
            }

            return new TargetMetadata { _hidden = (attributes & FileAttributes.Hidden) != 0, UnixMode = mode, _accessList = acl };
        }

        /// <summary>
        /// The access rules (DACL, with the flag that switches inheritance off) of the target as a security descriptor, or
        /// <see langword="null"/> when they cannot be read: the replacement then gets the rules of the folder, as before.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static byte[]? ReadAccessList(string target)
        {
            try
            {
                return new FileInfo(target).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or SecurityException)
            {
                return null;
            }
        }

        /// <summary>
        /// Gives the temporary file the access rules of the replaced target, after its content is written (it was open with
        /// <see cref="FileShare.None"/> until now, so nobody else could read it; from its creation until here it has the rules of
        /// the folder, as before the rules were copied). Without this, a file that only its owner may read would come back with
        /// the rules of its folder, which are often much wider. The rename that replaces the target needs DELETE on the
        /// temporary file: if the copied rules do not give that (probed with exactly that access), the rules of the folder are
        /// put back and the target's rules are set on the replaced file instead, right after the rename; the save never fails
        /// for this. A file system without access rules (FAT, some network shares) keeps the rules it has.
        /// </summary>
        /// <returns>The rules to set after the rename, or <see langword="null"/> when there is nothing left to do.</returns>
        [SupportedOSPlatform("windows")]
        private static byte[]? GiveAccessList(string temporary, byte[]? accessList, SaveFaults? faults)
        {
            if (accessList is null)
            {
                return null;
            }

            var inherited = ReadAccessList(temporary);
            if (!WriteAccessList(temporary, accessList))
            {
                return null;
            }

            var canDelete = faults?.CanDelete is { } probe ? probe(temporary) : WindowsFileRename.CanOpenForDelete(temporary);
            if (canDelete)
            {
                return null;
            }

            if (inherited is not null)
            {
                WriteAccessList(temporary, inherited);
            }

            return accessList;
        }

        [SupportedOSPlatform("windows")]
        private static bool WriteAccessList(string file, byte[] descriptor)
        {
            try
            {
                var security = new FileSecurity();
                security.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Access);
                new FileInfo(file).SetAccessControl(security);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or SecurityException)
            {
                // The rules the file has stay; the save itself goes on.
                return false;
            }
        }

        /// <summary>
        /// Gives the temporary file the permissions of the target on Unix (exactly, without the umask that applied when it was
        /// created) and its Hidden attribute on Windows.
        /// </summary>
        private void CopyTargetMetadata(string temporary)
        {
            try
            {
                if (UnixMode is { } mode && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(temporary, mode);
                }

                if (_hidden && OperatingSystem.IsWindows())
                {
                    File.SetAttributes(temporary, File.GetAttributes(temporary) | FileAttributes.Hidden);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw AtomicFile.FailureOf(ex, "The file could not be written.");
            }
        }
    }
}
