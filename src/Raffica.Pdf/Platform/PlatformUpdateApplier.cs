using Raffica.Core.Updates;
using Raffica.Pdf.Platform.Linux;
using Raffica.Pdf.Platform.MacOS;
using Raffica.Pdf.Platform.Windows;

namespace Raffica.Pdf.Platform;

/// <summary>Chooses the installer of the operating system for an update; called on first use, never at startup.</summary>
internal static class PlatformUpdateApplier
{
    /// <summary>
    /// Creates the applier for the installation <paramref name="form"/> of the running program, or <see langword="null"/> where the app installs nothing by itself (the
    /// Debian package, the portable archive, an unknown form): the update flow then only points to the download page.
    /// </summary>
    /// <param name="form">How the running program was installed.</param>
    /// <param name="configDirectory">The config folder of the update files (staging, session).</param>
    /// <param name="shutdown">Ends the app once the installer took over.</param>
    public static IUpdateApplier? Create(PackageForm form, string configDirectory, Action shutdown) =>
        Create(
            form,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsLinux(),
            Environment.ProcessPath,
            Environment.GetEnvironmentVariable("APPIMAGE"),
            Environment.GetEnvironmentVariable("APPDIR"),
            configDirectory,
            shutdown,
            new SystemUpdateProcessStarter(),
            OperatingSystem.IsMacOS());

    /// <summary>
    /// <see cref="Create(PackageForm, string, Action)"/> with the system, the program path, the <c>APPIMAGE</c> and <c>APPDIR</c> variables, the process starter and (macOS) the process runner as arguments (tests).
    /// </summary>
    internal static IUpdateApplier? Create(
        PackageForm form,
        bool isWindows,
        bool isLinux,
        string? processPath,
        string? appImage,
        string? appDir,
        string configDirectory,
        Action shutdown,
        IUpdateProcessStarter starter,
        bool isMacOS = false,
        IProcessRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(configDirectory);
        ArgumentNullException.ThrowIfNull(shutdown);
        ArgumentNullException.ThrowIfNull(starter);

        if (isWindows && form == PackageForm.WindowsInstaller)
        {
            if (Path.GetDirectoryName(processPath) is not { Length: > 0 } programDirectory)
            {
                return null;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                localAppData = Path.GetTempPath();
            }

            return new WindowsUpdateInstaller(new WindowsUpdateInstallerSetup(
                programDirectory,
                WindowsUpdateInstaller.UpdaterDirectoryIn(localAppData),
                configDirectory,
                UpdateTrust.Keys,
                UpdateApplierSupport.RunningVersion(),
                Environment.ProcessId,
                starter,
                shutdown,
                TimeProvider.System));
        }

        // The variables are inherited by every child of an AppImage: only a program inside APPDIR may replace APPIMAGE.
        if (isLinux && form == PackageForm.AppImage && !string.IsNullOrEmpty(appImage) && PackageFormDetector.IsInsideAppDir(processPath, appDir))
        {
            return new AppImageUpdater(new AppImageUpdaterSetup(
                appImage,
                processPath,
                appDir,
                configDirectory,
                UpdateTrust.Keys,
                UpdateApplierSupport.RunningVersion(),
                Environment.ProcessId,
                starter,
                shutdown,
                new PhysicalAppImageFileSystem()));
        }

        if (isMacOS && form is PackageForm.MacAppArm64 or PackageForm.MacAppX64 && MacBundle.PathOf(processPath) is { } bundle)
        {
            return new MacAppUpdater(new MacAppUpdaterSetup(
                bundle,
                form,
                configDirectory,
                UpdateTrust.Keys,
                UpdateApplierSupport.RunningVersion(),
                Environment.ProcessId,
                runner ?? new SystemProcessRunner(),
                starter,
                shutdown));
        }

        return null;
    }
}
