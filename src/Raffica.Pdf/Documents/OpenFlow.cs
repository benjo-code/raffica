using Raffica.Core.Pdf;
using Raffica.Core.Viewing;

namespace Raffica.Pdf.Documents;

/// <summary>
/// Opens files into the <see cref="Workspace"/>: an already open file is selected, a password is asked as often as needed, and a file that
/// does not reach <see cref="ViewerState.Ready"/> leaves nothing behind (no tab, its session disposed) and ends in <c>showError</c>.
/// Opens run one after the other, so the same file dropped twice becomes one tab and at most one question is open at a time.
/// </summary>
/// <param name="workspace">The open documents.</param>
/// <param name="engine">The PDF engine (asked for each open, so creating it can wait until the first file).</param>
/// <param name="askPassword">Asks for the password of a file (name, whether the last one was wrong); null: the user canceled.</param>
/// <param name="showError">Tells the user why a file could not be opened.</param>
/// <param name="showFailures">Tells the user about several files of one <see cref="OpenManyAsync"/> that could not be opened, in one summary (two or more; a single failure still goes to <paramref name="showError"/>); null: every failure goes to <paramref name="showError"/>.</param>
internal sealed class OpenFlow(Workspace workspace, Func<PdfEngine> engine, Func<string, bool, Task<string?>> askPassword, Action<string, PdfOpenError> showError, Action<IReadOnlyList<OpenFailure>>? showFailures = null)
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>Opens one file and makes it active.</summary>
    /// <param name="path">The file.</param>
    /// <param name="onError">Told the reason when the file could not be opened (before the error is shown); not called when the user canceled the password question.</param>
    /// <returns>The tab, or null when the file was not opened (an error was shown, or the password question was canceled).</returns>
    public Task<PdfTab?> OpenAsync(string path, Action<PdfOpenError>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return OpenCoreAsync(path, (failed, error) =>
        {
            try
            {
                onError?.Invoke(error);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The observer of the reason must not keep the user from the error.
            }

            ShowError(failed, error);
        });
    }

    private async Task<PdfTab?> OpenCoreAsync(string path, Action<string, PdfOpenError> report)
    {
        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task before;
        lock (_gate)
        {
            before = _tail;
            _tail = turn.Task;
        }

        await before;
        try
        {
            return await OpenInTurnAsync(path, report);
        }
        finally
        {
            turn.SetResult();
        }
    }

    /// <summary>Opens the files in order; the last one that was opened is active. Failures of two or more files end in one summary instead of one card each.</summary>
    public async Task OpenManyAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var failures = new List<OpenFailure>();
        foreach (var path in paths)
        {
            await OpenCoreAsync(path, (failed, error) => failures.Add(new OpenFailure(failed, error)));
        }

        if (failures.Count == 1 || (failures.Count > 1 && showFailures is null))
        {
            foreach (var failure in failures)
            {
                ShowError(failure.Path, failure.Error);
            }
        }
        else if (failures.Count > 1)
        {
            try
            {
                showFailures!(failures);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The user cannot be told; the opens ended cleanly anyway.
            }
        }
    }

    private async Task<PdfTab?> OpenInTurnAsync(string path, Action<string, PdfOpenError> report)
    {
        // An empty path, a blank one and one with invalid characters are unreadable files like any other.
        if (string.IsNullOrWhiteSpace(path))
        {
            report(path, PdfOpenError.CannotRead);
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            report(path, PdfOpenError.CannotRead);
            return null;
        }

        if (workspace.Find(full) is { } open)
        {
            workspace.Select(open);
            return open;
        }

        ViewerSession? session = null;
        try
        {
            session = new ViewerSession(engine());
            await session.OpenAsync(full);
            var incorrect = false;
            while (session.State is ViewerState.PasswordRequired or ViewerState.PasswordIncorrect)
            {
                var password = await askPassword(Path.GetFileName(full), incorrect);
                if (password is null)
                {
                    return null;
                }

                await session.OpenAsync(full, password);
                incorrect = true;
            }

            if (session.State == ViewerState.Ready)
            {
                var tab = workspace.Add(session, full);
                session = null;
                return tab;
            }

            report(path, session.Error ?? PdfOpenError.Unknown);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nothing a file does may take the app down.
            report(path, PdfOpenError.Unknown);
        }
        finally
        {
            session?.Dispose();
        }

        return null;
    }

    /// <summary>Tells the user; a callback that throws must neither take the app down nor wedge the chain of opens.</summary>
    private void ShowError(string path, PdfOpenError error)
    {
        try
        {
            showError(path, error);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The user cannot be told; the open still ends cleanly (no tab, session disposed).
        }
    }
}

/// <summary>A file that <see cref="OpenFlow"/> could not open and why.</summary>
internal readonly record struct OpenFailure(string Path, PdfOpenError Error);
