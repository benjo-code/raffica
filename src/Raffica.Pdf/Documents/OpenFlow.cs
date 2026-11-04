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
internal sealed class OpenFlow(Workspace workspace, Func<PdfEngine> engine, Func<string, bool, Task<string?>> askPassword, Action<string, PdfOpenError> showError)
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>Opens one file and makes it active.</summary>
    /// <returns>The tab, or null when the file was not opened (an error was shown, or the password question was canceled).</returns>
    public async Task<PdfTab?> OpenAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
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
            return await OpenCoreAsync(path);
        }
        finally
        {
            turn.SetResult();
        }
    }

    /// <summary>Opens the files in order; the last one that was opened is active.</summary>
    public async Task OpenManyAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            await OpenAsync(path);
        }
    }

    private async Task<PdfTab?> OpenCoreAsync(string path)
    {
        // An empty path, a blank one and one with invalid characters are unreadable files like any other.
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowError(path, PdfOpenError.CannotRead);
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            ShowError(path, PdfOpenError.CannotRead);
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

            ShowError(path, session.Error ?? PdfOpenError.Unknown);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nothing a file does may take the app down.
            ShowError(path, PdfOpenError.Unknown);
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
