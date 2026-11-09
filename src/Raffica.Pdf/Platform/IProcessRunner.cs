namespace Raffica.Pdf.Platform;

/// <summary>What a finished program left behind.</summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="Output">Standard output.</param>
internal sealed record ProcessOutcome(int ExitCode, string Output);

/// <summary>Starts a program without a shell and waits for it with a time limit; tests use a fake so no process is started.</summary>
internal interface IProcessRunner
{
    /// <summary>Runs <paramref name="file"/> with <paramref name="args"/> (each one argument, never parsed by a shell).</summary>
    /// <param name="file">The program.</param>
    /// <param name="args">The arguments.</param>
    /// <param name="timeout">How long to wait before the program is stopped.</param>
    /// <param name="cancellationToken">Stops the program and ends the wait (throws <see cref="OperationCanceledException"/>).</param>
    /// <returns>The outcome, or <see langword="null"/> when the program could not be started or did not end in time.</returns>
    Task<ProcessOutcome?> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken cancellationToken);
}
