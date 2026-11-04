using Raffica.Core.Hosting;

namespace Raffica.Pdf.Hosting;

/// <summary>
/// The two ends of the single-instance hand-over: a later launch sends its files to the running app
/// (<see cref="TryHandOver"/>), and the running app decides which of the received paths it opens (<see cref="Accept"/>).
/// What arrives through the pipe is untrusted, so nothing here throws and only existing regular files get through.
/// </summary>
internal static class InstanceHandover
{
    /// <summary>
    /// Sends the files to the running instance of <paramref name="name"/>. Relative paths are resolved against the working folder
    /// of this process first: the running app has a folder of its own. An empty list only asks the instance to come to the front.
    /// </summary>
    /// <returns>True if everything was handed over; false if there is no instance (or it did not answer in <paramref name="timeout"/>).</returns>
    public static bool TryHandOver(string name, IReadOnlyList<string> files, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            return SingleInstance.TrySend(name, [.. files.Select(Resolve)], timeout);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves each path to a full path and keeps those that are existing regular files. Everything else (a path that is empty,
    /// malformed or too long, a URL, a folder, a device, a file that does not exist) is passed to <paramref name="report"/> as it was
    /// received and left out. A file that is no PDF is kept: opening it ends in the error card of the open flow.
    /// </summary>
    public static IReadOnlyList<string> Accept(IReadOnlyList<string> paths, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(report);

        var accepted = new List<string>();
        foreach (var path in paths)
        {
            if (TryAccept(path, out var full))
            {
                accepted.Add(full);
            }
            else
            {
                report(path ?? string.Empty);
            }
        }

        return accepted;
    }

    private static string Resolve(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or IOException)
        {
            // Left as it is; the receiver reports it.
            return path;
        }
    }

    private static bool TryAccept(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(path);

            // The device namespace (\\.\NUL, \\.\pipe\x) is no file to read.
            if (full.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return false;
            }

            // File.Exists is false for a folder and for anything that cannot be asked.
            return File.Exists(full);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
