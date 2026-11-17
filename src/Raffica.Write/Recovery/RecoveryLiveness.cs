using System.ComponentModel;
using System.Diagnostics;

namespace Raffica.Write.Recovery;

/// <summary>
/// Reads the start time of a process: <see langword="null"/> when there is no such process, and <see cref="DateTimeOffset.MaxValue"/> (the sentinel)
/// when the process exists but its start time cannot be read (another user or an elevated process); <see cref="RecoveryLiveness"/> counts the sentinel as live.
/// </summary>
internal interface IProcessProbe
{
    DateTimeOffset? StartTimeOf(int processId);
}

/// <summary>The real probe over <see cref="Process"/>; every failure means "unknown".</summary>
internal sealed class SystemProcessProbe : IProcessProbe
{
    public DateTimeOffset? StartTimeOf(int processId)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }

        using (process)
        {
            try
            {
                return new DateTimeOffset(process.StartTime);
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
                // It exited between the two calls.
                return null;
            }
            catch (Exception ex) when (ex is Win32Exception or NotSupportedException or UnauthorizedAccessException)
            {
                return DateTimeOffset.MaxValue;
            }
        }
    }
}

/// <summary>Decides whether the owner of a copy is another running Write process, whose copy must be left alone.</summary>
internal static class RecoveryLiveness
{
    private static readonly TimeSpan s_tolerance = TimeSpan.FromSeconds(2);

    /// <summary>Live: the process exists, is not this one, and started within 2 seconds of the recorded start (the id may be reused by an unrelated process).</summary>
    public static bool IsLive(RecoveryEntry entry, int selfProcessId, IProcessProbe probe)
    {
        if (entry.OwnerProcessId == selfProcessId)
        {
            return false;
        }

        var started = probe.StartTimeOf(entry.OwnerProcessId);
        if (started == DateTimeOffset.MaxValue)
        {
            return true;
        }

        return started is { } value && (value - entry.OwnerStarted).Duration() <= s_tolerance;
    }
}
