using Raffica.Core.Updates;

namespace Raffica.Pdf.Platform;

/// <summary>Chooses the installer of the operating system for an update; called on first use, never at startup.</summary>
internal static class PlatformUpdateApplier
{
    /// <summary>
    /// Creates the applier for the installation <paramref name="form"/>, or <see langword="null"/> where the app installs nothing by itself (the Debian package,
    /// the portable archive, an unknown form): the update flow then only points to the download page. The appliers of the Windows installer and
    /// of the AppImage are added with the platform code of this milestone; until then every form answers <see langword="null"/>.
    /// </summary>
    public static IUpdateApplier? Create(PackageForm form)
    {
        _ = form;
        return null;
    }
}
