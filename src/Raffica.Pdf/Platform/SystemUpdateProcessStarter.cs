using System.ComponentModel;
using System.Diagnostics;

namespace Raffica.Pdf.Platform;

/// <summary>Starts and waits for real processes: no shell, arguments as a list, no redirection, the working folder is the folder of the program.</summary>
internal sealed class SystemUpdateProcessStarter : IUpdateProcessStarter
{
    /// <summary>The start information for <paramref name="file"/>: <c>Arguments</c> stays empty, every argument is an element of <c>ArgumentList</c>.</summary>
    public static ProcessStartInfo CreateStartInfo(string file, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    /// <inheritdoc/>
    public bool Start(string file, IReadOnlyList<string> arguments)
    {
        try
        {
            var info = CreateStartInfo(file, arguments);
            info.WorkingDirectory = Path.GetDirectoryName(file) ?? string.Empty;
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public Task WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => WaitForExit(processId, timeout, cancellationToken), cancellationToken);
    }

    /// <summary>Waits (blocking) until the process has ended or the time is up; a process that does not exist counts as ended.</summary>
    public static void WaitForExit(int processId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            try
            {
                // Waited in short steps so a cancellation is seen soon.
                var until = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
                while (!process.WaitForExit(250))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Stopwatch.GetTimestamp() >= until)
                    {
                        return;
                    }
                }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                // Gone, or not ours to wait for: nothing holds the files then.
            }
        }
    }
}
