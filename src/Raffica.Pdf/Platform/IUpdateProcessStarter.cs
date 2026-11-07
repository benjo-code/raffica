namespace Raffica.Pdf.Platform;

/// <summary>
/// What the appliers need to start the next program and to wait for the old one; never a shell, the arguments are a list (one element per argument, so a path
/// with spaces or non-ASCII characters stays one argument). Tests use a fake, so no process is started.
/// </summary>
internal interface IUpdateProcessStarter
{
    /// <summary>Starts <paramref name="file"/> detached (it outlives this process) with <paramref name="arguments"/>; <see langword="false"/> when it could not be started. Never throws.</summary>
    bool Start(string file, IReadOnlyList<string> arguments);

    /// <summary>
    /// Waits until the process <paramref name="processId"/> has ended, at most <paramref name="timeout"/>; returns at once when there is no such process. Only cancelling
    /// throws.
    /// </summary>
    Task WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken);
}
