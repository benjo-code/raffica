using Raffica.Core.Updates;

namespace Raffica.Pdf.Platform.MacOS;

/// <summary>Where a running <c>.app</c> bundle lies, which decides whether an update can replace it.</summary>
internal enum MacBundleLocation
{
    /// <summary>A normal place (<c>/Applications</c>, the user's folder, a deeper path on an external drive).</summary>
    Installed,

    /// <summary>Gatekeeper path randomization: the app was started from a quarantined download and the real folder is read-only to it.</summary>
    Translocated,

    /// <summary>The bundle sits directly on a mounted volume, as on an opened disk image.</summary>
    MountedImage,
}

/// <summary>Pure rules about the bundle of the running app; compiled and tested on every system.</summary>
internal static class MacBundle
{
    /// <summary>The bundle identifier of Raffica PDF.</summary>
    public const string Identifier = "io.github.benjo-code.raffica.pdf";

    private const string TranslocationMarker = "/AppTranslocation/";
    private const string VolumesPrefix = "/Volumes/";

    /// <summary>The bundle folder of a process path inside <c>.app/Contents/MacOS/</c>, or <c>null</c>.</summary>
    public static string? PathOf(string? processPath) => PackageFormDetector.BundlePathOf(processPath);

    /// <summary>Tells a translocated copy and one on a mounted volume from an installed one.</summary>
    public static MacBundleLocation LocationOf(string bundlePath)
    {
        if (bundlePath.Contains(TranslocationMarker, StringComparison.Ordinal))
        {
            return MacBundleLocation.Translocated;
        }

        if (bundlePath.StartsWith(VolumesPrefix, StringComparison.Ordinal))
        {
            // /Volumes/<name>/<App>.app: exactly one folder between the volume name and the end.
            var rest = bundlePath[VolumesPrefix.Length..].TrimEnd('/');
            var parts = rest.Split('/');
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].EndsWith(".app", StringComparison.Ordinal))
            {
                return MacBundleLocation.MountedImage;
            }
        }

        return MacBundleLocation.Installed;
    }
}
