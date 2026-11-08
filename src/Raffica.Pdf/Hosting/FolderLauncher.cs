using System.Diagnostics;

namespace Raffica.Pdf.Hosting;

/// <summary>
/// Opens a folder in the file manager of the system. Only an existing directory is handed over, as one argument of the opener in the argument
/// list (<c>explorer.exe</c>, <c>open</c>, <c>xdg-open</c>, started without a shell), so the path is never split or interpreted.
/// </summary>
internal static class FolderLauncher
{
    /// <summary>Opens the folder, or does nothing when it does not exist or the opener cannot be started.</summary>
    public static void Open(string directory)
    {
        if (CreateStartInfo(directory) is not { } info)
        {
            return;
        }

        try
        {
            using var process = Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // No file manager: the card still says where the log is.
        }
    }

    /// <summary>The way to start the opener for the folder, or null when it is not an existing directory.</summary>
    internal static ProcessStartInfo? CreateStartInfo(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        string full;
        try
        {
            full = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!Directory.Exists(full))
        {
            return null;
        }

        var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(full);
        return info;
    }
}
