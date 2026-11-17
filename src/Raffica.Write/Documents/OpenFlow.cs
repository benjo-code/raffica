using Raffica.Write.Docx;

namespace Raffica.Write.Documents;

/// <summary>
/// Opens files into the <see cref="WriteWorkspace"/>: an already open file is selected, and a file that does not open leaves nothing behind (no tab, nothing
/// held) and ends in a <see cref="Failed"/> result. The work of a file (reading, the first blocks, the layout of page 1) runs on a pool thread
/// (<see cref="DocumentOpener"/>); the tab is made and added on the thread that called, the UI thread. Opens run one after the other, so the same file
/// dropped twice becomes one tab. The flow ends after page 1 is laid out; the full open and the layout of the rest belong to what the window starts on the tab.
/// </summary>
/// <param name="workspace">The open documents.</param>
/// <param name="work">The fonts of the process and the counters (made at the first file).</param>
/// <param name="showError">Tells the user why one file of <see cref="OpenManyAsync"/> or <see cref="Report"/> could not be opened.</param>
/// <param name="showFailures">Tells the user about two or more failures in one summary; null: every failure goes to <paramref name="showError"/>.</param>
/// <param name="maxFileLength">The largest file to open (tests make it small).</param>
internal sealed class OpenFlow(WriteWorkspace workspace, OpenWork work, Action<string, OpenError> showError, Action<IReadOnlyList<OpenFailure>>? showFailures = null, long maxFileLength = DocumentOpener.DefaultMaxFileLength)
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>The work of one file on the pool thread (tests replace it with a gated one); <see cref="DocumentOpener.Open"/> by default.</summary>
    internal Func<string, OpenWork, long, CancellationToken, OpenAttempt> Opener { get; set; } = (path, work, max, token) => DocumentOpener.Open(path, work, max, token);

    /// <summary>The work of a new document on the pool thread (tests replace it); <see cref="DocumentOpener.OpenBlank"/> by default.</summary>
    internal Func<byte[], OpenWork, CancellationToken, OpenAttempt> BlankOpener { get; set; } = DocumentOpener.OpenBlank;

    /// <summary>Opens one file and makes its tab active; shows nothing (see <see cref="Report"/>).</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before the open ended; no tab was made.</exception>
    public async Task<OpenResult> OpenAsync(string path, CancellationToken token = default)
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
            return await OpenInTurnAsync(path, token);
        }
        finally
        {
            turn.SetResult();
        }
    }

    /// <summary>
    /// Opens the files in order; the last one that was opened is active. Failures of two or more files end in one summary instead of one card each. A cancelled
    /// token (the window closes) stops at once: the file in progress adds no tab, the rest are never started, and no card is shown.
    /// </summary>
    public async Task OpenManyAsync(IEnumerable<string> paths, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var failures = new List<OpenFailure>();
        try
        {
            foreach (var path in paths)
            {
                if (await OpenAsync(path, token) is Failed failed)
                {
                    failures.Add(new OpenFailure(path, failed.Error));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Report(failures);
    }

    /// <summary>Tells the user about failures: one goes to <c>showError</c>, two or more to one summary (every one to <c>showError</c> without a summary). Never throws.</summary>
    public void Report(IReadOnlyList<OpenFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        try
        {
            if (failures.Count > 1 && showFailures is not null)
            {
                showFailures(failures);
                return;
            }

            foreach (var failure in failures)
            {
                showError(failure.Path, failure.Error);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The user cannot be told; the opens ended cleanly anyway (no tab, nothing held).
        }
    }

    /// <summary>
    /// Makes a new untitled document of the given paper and adds its tab (active); null when the window is closing (nothing is left behind) or the document could not be made.
    /// The work runs on a pool thread, the tab is made on the thread that called, the UI thread; it is never in the recent list.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before the tab was added.</exception>
    public async Task<WriteTab?> NewDocumentAsync(PaperSize paper, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var attempt = await Task.Run(() => BlankOpener(BlankDocument.Create(paper), work, token), token);
        return AddUntitled(attempt, null, token);
    }

    /// <summary>
    /// Opens a recovery copy (the DOCX of <see cref="Recovery.RecoveryStore"/>) as a tab that belongs to no file: its title is the name of <paramref name="originalPath"/>, or the next
    /// "Untitled N" when there is none, and <see cref="WriteTab.SuggestedSavePath"/> is that path. The bytes are read at once (the tab holds no handle on the copy, which the
    /// autosave replaces). Null when the copy cannot be read or opened, or the window is closing; nothing is left behind and the copy is not touched.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before the tab was added.</exception>
    public async Task<WriteTab?> OpenRecoveredAsync(string copyPath, string? originalPath, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(copyPath);
        token.ThrowIfCancellationRequested();
        OpenAttempt attempt;
        try
        {
            attempt = await Task.Run(
                () =>
                {
                    if (new FileInfo(copyPath).Length > maxFileLength)
                    {
                        return OpenAttempt.Failure(OpenError.TooLarge);
                    }

                    return BlankOpener(File.ReadAllBytes(copyPath), work, token);
                },
                token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return AddUntitled(attempt, originalPath, token);
    }

    private WriteTab? AddUntitled(OpenAttempt attempt, string? originalPath, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            attempt.Document?.Dispose();
            token.ThrowIfCancellationRequested();
        }

        if (attempt.Document is not { } document)
        {
            return null;
        }

        try
        {
            var name = originalPath is null ? string.Empty : System.IO.Path.GetFileName(originalPath);
            var tab = new WriteTab(document, name.Length > 0 ? name : UntitledNames.Next(workspace.Tabs)) { SuggestedSavePath = originalPath };
            if (!workspace.TryAdd(tab))
            {
                tab.Dispose();
                throw new OperationCanceledException(token);
            }

            return tab;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private async Task<OpenResult> OpenInTurnAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // An empty path, a blank one and one with invalid characters are unreadable files like any other.
        string full;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new Failed(OpenError.Other);
            }

            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return new Failed(OpenError.Other);
        }

        if (workspace.Find(full) is { } open)
        {
            workspace.Select(open);
            return new Opened(open);
        }

        OpenAttempt attempt;
        try
        {
            attempt = await Task.Run(() => Opener(full, work, maxFileLength, token), token);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            // Nothing a file does may take the app down; DocumentOpener already answers every file problem itself.
            return new Failed(OpenError.Other);
        }

        // The window closed while the file was read: a document that finished late is let go and nothing else happens (no tab, no recent entry, no card).
        if (token.IsCancellationRequested)
        {
            attempt.Document?.Dispose();
            token.ThrowIfCancellationRequested();
        }

        if (attempt.Document is not { } document)
        {
            return new Failed(attempt.Error);
        }

        try
        {
            var tab = new WriteTab(full, document);
            if (!workspace.TryAdd(tab))
            {
                // Sealed: the window is closing and nobody would ever dispose this tab.
                tab.Dispose();
                throw new OperationCanceledException(token);
            }

            return new Opened(tab);
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }
}
