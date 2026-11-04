using System.Diagnostics;
using Raffica.Core.Text;

namespace Raffica.Pdf.Text;

/// <summary>
/// Opens an address with the system: the browser for http and https, the mail program for mailto. The address is validated again here
/// (the same gate as the card, <see cref="ExternalLink.TryParse"/>, and it must be the very string that was validated); whatever fails
/// starts nothing. On Windows the address is the file name of a shell execution (no arguments, no shell text around it). On Linux and macOS
/// the opener (<c>xdg-open</c>, <c>open</c>) is started directly, without a shell, and the address is its one argument in the argument
/// list, so it is never split or interpreted.
/// </summary>
internal static class SystemLauncher
{
    /// <summary>Opens the address, or does nothing when it is not a valid external address.</summary>
    public static void Open(Uri uri)
    {
        if (CreateStartInfo(uri) is not { } info)
        {
            return;
        }

        using var process = Process.Start(info);
    }

    /// <summary>The way to start the opener for the address, or null when the address is not acceptable (nothing may be started then).</summary>
    internal static ProcessStartInfo? CreateStartInfo(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var text = uri.OriginalString;
        if (!ExternalLink.TryParse(text, out var link) || !string.Equals(link.Url, text, StringComparison.Ordinal))
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(link.Url) { UseShellExecute = true };
        }

        var info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(link.Url);
        return info;
    }
}
