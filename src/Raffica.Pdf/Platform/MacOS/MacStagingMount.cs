namespace Raffica.Pdf.Platform.MacOS;

/// <summary>
/// Releases what the macOS updater of an earlier run may have left in a staging folder: the disk image it mounted at <c>&lt;staging&gt;/mnt</c> when the app ended
/// (or both detaches failed) between attach and detach. A staging folder is deleted recursively before a new download, and a mount inside it is a read-only
/// volume that makes that delete fail until the next restart of the Mac, so the mount point is detached first.
/// </summary>
internal static class MacStagingMount
{
    private static readonly TimeSpan DetachTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The name of the mount folder inside a staging folder (the one <see cref="MacAppUpdater"/> mounts at).</summary>
    public const string FolderName = MacAppUpdater.MountFolderName;

    /// <summary>
    /// Detaches <c>&lt;staging&gt;/mnt</c> by force when that folder exists (best effort, the outcome is ignored). Nothing is started otherwise, so the normal
    /// case costs one folder check.
    /// </summary>
    /// <param name="runner">Runs <c>hdiutil</c>.</param>
    /// <param name="stagingDirectory">The staging folder that is about to be deleted.</param>
    /// <returns>A task that completes when the detach has ended.</returns>
    public static async Task ReleaseAsync(IProcessRunner runner, string stagingDirectory)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrEmpty(stagingDirectory);
        var mount = Path.Combine(stagingDirectory, FolderName);
        if (!Directory.Exists(mount))
        {
            return;
        }

        await runner.RunAsync(MacAppUpdater.Hdiutil, ["detach", mount, "-force"], DetachTimeout, CancellationToken.None).ConfigureAwait(false);
    }
}
