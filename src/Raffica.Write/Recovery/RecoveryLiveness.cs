using System.ComponentModel;
using System.Diagnostics;

namespace Raffica.Write.Recovery;

/// <summary>Reads the start time of a process; <see langword="null"/> when there is no such process or it cannot be read.</summary>
internal interface IProcessProbe
{
    DateTimeOffset? StartTimeOf(int processId);
}

/// <summary>The real probe over <see cref="Process"/>; every failure means "unknown".</summary>
internal sealed class SystemProcessProbe : IProcessProbe
{
    public DateTimeOffset? StartTimeOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
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
        return started is { } value && (value - entry.OwnerStarted).Duration() <= s_tolerance;
    }
}
