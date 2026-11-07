using System.ComponentModel;
using System.Diagnostics;

namespace Raffica.Pdf.Platform;

/// <summary>Runs real programs: no shell, no window, output read, killed when the time is up.</summary>
internal sealed class SystemProcessRunner : IProcessRunner
{
    /// <inheritdoc/>
    public async Task<ProcessOutcome?> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(args);
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };
        foreach (var argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        Process? process = null;
        try
        {
            process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false);
            await error.ConfigureAwait(false);
            return new ProcessOutcome(process.ExitCode, text);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            Kill(process);
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void Kill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }
}
