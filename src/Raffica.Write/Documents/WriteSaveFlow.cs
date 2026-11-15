using Raffica.Core.Saving;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Docx;

namespace Raffica.Write.Documents;

/// <summary>A path the user chose in the Save as picker and whether a file stood there when the picker answered (the picker confirmed replacing it).</summary>
/// <param name="Path">The path.</param>
/// <param name="Existed">True for an existing file the user agreed to replace; false for a name that did not exist, so a file that appears later is refused instead of replaced.</param>
internal sealed record SavePick(string Path, bool Existed);

/// <summary>What the save flow needs of the window: the questions, the picker, the error card and a place for a one-line note. Every call is on the UI thread.</summary>
internal interface ISaveInteraction
{
    /// <summary>Asks what to do with a file that changed on disk since it was opened.</summary>
    Task<ChangedChoice> AskChangedOriginalAsync(WriteTab tab);

    /// <summary>The Save as picker; null when the user cancelled.</summary>
    Task<SavePick?> PickSavePathAsync(WriteTab tab);

    /// <summary>Shows the error card of a failed save and returns when the user has dismissed it.</summary>
    Task ShowErrorAsync(WriteTab tab, SaveProblem problem);

    /// <summary>A one-line note (a file the replace could not delete).</summary>
    void Note(string text);

    /// <summary>Whether another tab has the file open (Save as onto it is refused).</summary>
    bool IsOpenInAnotherTab(string path, WriteTab tab);

    /// <summary>The tab belongs to another file now (the recent list and the title follow).</summary>
    void PathChanged(WriteTab tab);
}

/// <summary>
/// Save and Save as of a tab. Save writes over the opened file, Save as asks for a path (the picker confirms replacing an existing file, so a replace is allowed only for
/// a file that existed then; a name that did not exist is never allowed to replace a file that appears meanwhile). Both run on the UI thread up to the file write.
/// </summary>
/// <remarks>
/// <b>Threading.</b> The flow starts on the UI thread and so does everything that touches the tab: it opens a save scope on the editing session (<see cref="Editing.EditSession.BeginSave"/>:
/// input is queued and replayed after the save) and writes the file on a pool thread. The pool thread takes the edit lease (<see cref="Viewing.LayoutRunner.PauseForEdit"/>: the
/// layout stops at a page boundary and nothing changes the model) in <see cref="DocxFile.BeforeModelRead"/> and gives it back in <see cref="DocxFile.AfterModelRead"/>, which the
/// file calls on every path once the first ran: the thread that took the lease releases it, and the UI thread never holds it across the write. The lease covers only the read of the
/// model into memory, because a save of a big file spends its time compressing and writing bytes that no longer depend on the model; a request for the lease from the UI thread
/// meanwhile (the preedit of an input method takes it too) waits for that read, a few milliseconds, instead of for the whole write. The save scope stays until the file is on disk,
/// so typing is queued, not lost, and cannot change the model before <c>MarkSaved</c> names the revision that was written. Success marks that revision saved, then the scope ends
/// and replays what was typed meanwhile (which makes the document dirty again, as it should). A failure marks nothing, so the document stays dirty and the history is as it was.
/// The continuations run on the UI thread (the synchronization context of the caller); nothing here reads <c>Dispatcher.UIThread</c>; the pool thread touches only the file and
/// the lease. One save per tab at a time (<see cref="IsRunning"/>).
/// </remarks>
internal sealed class WriteSaveFlow(ISaveInteraction ui)
{
    private readonly Dictionary<WriteTab, TaskCompletionSource> _running = [];

    /// <summary>Test seam: runs on the saving (pool) thread right after it took the edit lease, before the model is read.</summary>
    internal Action<WriteTab>? AfterLeaseTaken { get; set; }

    /// <summary>Raised on the UI thread when a save starts or ends (the commands read their states again).</summary>
    public event Action? Changed;

    /// <summary>Whether a save or Save as of the tab is running (its picker or card counts).</summary>
    public bool IsRunning(WriteTab tab) => _running.ContainsKey(tab);

    /// <summary>Whether any tab has a save running.</summary>
    public bool IsAnyRunning => _running.Count > 0;

    /// <summary>A task that completes when no save of the tab runs (at once when none does).</summary>
    public Task WhenIdleAsync(WriteTab tab) => _running.TryGetValue(tab, out var running) ? running.Task : Task.CompletedTask;

    /// <summary>Saves over the opened file.</summary>
    public Task<SaveOutcome> SaveAsync(WriteTab tab) => RunAsync(tab, saveAs: false);

    /// <summary>Asks for a path and saves there; the tab belongs to the new file afterwards.</summary>
    public Task<SaveOutcome> SaveAsAsync(WriteTab tab) => RunAsync(tab, saveAs: true);

    private async Task<SaveOutcome> RunAsync(WriteTab tab, bool saveAs)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var state = SaveCommandState.Of(tab, IsRunning(tab));
        if (state.Refusal(saveAs) is { } refusal)
        {
            return new SaveRefused(refusal);
        }

        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _running[tab] = running;
        Changed?.Invoke();
        try
        {
            return saveAs ? await SaveAsCoreAsync(tab) : await SaveOverOriginalAsync(tab, overwriteChangedOriginal: false);
        }
        finally
        {
            _running.Remove(tab);
            running.TrySetResult();
            Changed?.Invoke();
        }
    }

    private async Task<SaveOutcome> SaveOverOriginalAsync(WriteTab tab, bool overwriteChangedOriginal)
    {
        var path = tab.Docx.OriginPath ?? tab.Path;
        var (report, error) = await WriteAsync(tab, (docx, token) => docx.SaveAsync(path, overwriteChangedOriginal, token));
        if (report is not null)
        {
            Tell(report);
            return new Saved(report.Path);
        }

        if (error is OperationCanceledException || tab.IsDisposed)
        {
            return new SaveCancelled();
        }

        if (error is SaveFailureException { Reason: SaveFailureReason.OriginalChanged })
        {
            switch (await ui.AskChangedOriginalAsync(tab))
            {
                case ChangedChoice.Overwrite:
                    return await SaveOverOriginalAsync(tab, overwriteChangedOriginal: true);
                case ChangedChoice.SaveAs:
                    return await SaveAsCoreAsync(tab);
                default:
                    return new SaveCancelled();
            }
        }

        return await FailAsync(tab, SaveProblem.Of(error!));
    }

    private async Task<SaveOutcome> SaveAsCoreAsync(WriteTab tab)
    {
        while (true)
        {
            var pick = await ui.PickSavePathAsync(tab);
            if (pick is null || tab.IsDisposed)
            {
                return new SaveCancelled();
            }

            var path = Path.GetFullPath(Path.HasExtension(pick.Path) ? pick.Path : pick.Path + ".docx");
            if (!PathRules.AreEqual(path, tab.Path) && ui.IsOpenInAnotherTab(path, tab))
            {
                return await FailAsync(tab, new SaveProblem(SaveProblemKind.OpenElsewhere));
            }

            // A name the user typed that is not there is never allowed to replace a file that appears before the write; an existing file was confirmed at the picker.
            var replaceExisting = pick.Existed;

            var overwriteChangedOriginal = false;
            while (true)
            {
                var overwrite = overwriteChangedOriginal;
                var (report, error) = await WriteAsync(tab, (docx, token) => docx.SaveAsAsync(path, replaceExisting, overwrite, token));
                if (report is not null)
                {
                    tab.Rebind(report.Path);
                    ui.PathChanged(tab);
                    Tell(report);
                    return new SavedAs(report.Path);
                }

                if (error is OperationCanceledException || tab.IsDisposed)
                {
                    return new SaveCancelled();
                }

                if (error is SaveFailureException { Reason: SaveFailureReason.OriginalChanged })
                {
                    // The path chosen is the opened file and it changed on disk: the same question as for Save; Save as asks for another path.
                    var choice = await ui.AskChangedOriginalAsync(tab);
                    if (choice == ChangedChoice.Overwrite)
                    {
                        overwriteChangedOriginal = true;
                        continue;
                    }

                    if (choice == ChangedChoice.SaveAs)
                    {
                        break;
                    }

                    return new SaveCancelled();
                }

                return await FailAsync(tab, SaveProblem.Of(error!));
            }
        }
    }

    private async Task<SaveOutcome> FailAsync(WriteTab tab, SaveProblem problem)
    {
        await ui.ShowErrorAsync(tab, problem);
        return new SaveFailed(problem);
    }

    private void Tell(SaveReport report)
    {
        if (report.LeftoverPath is { } leftover)
        {
            ui.Note(WriteStrings.Fill(WriteStrings.SaveLeftoverNoteFormat, Path.GetFileName(leftover)));
        }
    }

    // One write of the document: the save scope, the lease for the read of the model, the file on a pool thread, then (on the UI thread again) the saved revision. Never throws
    // for a failed save: the exception comes back, so the caller decides which card to show; the lease and the scope are given back on every path.
    private async Task<(SaveReport? Report, Exception? Error)> WriteAsync(WriteTab tab, Func<DocxFile, CancellationToken, Task<SaveReport>> save)
    {
        var session = tab.EnsureEditor();
        var docx = tab.Docx;
        var model = docx.Model;
        var revision = model.Edits.Revision;
        var scope = session.BeginSave();
        var previousBefore = docx.BeforeModelRead;
        var previousAfter = docx.AfterModelRead;
        Editing.EditLease? lease = null;
        try
        {
            // The lease is taken and released by the saving (pool) thread around the read of the model, never by the UI thread: the UI thread stays free during the write, and a
            // request for the lease from it (the preedit of an input method, a rebuild of the pages layout) waits for the end of that read instead of meeting "this thread holds it".
            docx.BeforeModelRead = () =>
            {
                lease = tab.Runner.PauseForEdit();
                try
                {
                    AfterLeaseTaken?.Invoke(tab);
                    previousBefore?.Invoke();
                }
                catch
                {
                    Interlocked.Exchange(ref lease, null)?.Dispose();
                    throw;
                }
            };
            docx.AfterModelRead = () =>
            {
                Interlocked.Exchange(ref lease, null)?.Dispose();
                previousAfter?.Invoke();
            };
            var report = await save(docx, tab.Token);

            // Back on the UI thread. The scope kept every edit out, so the model is the one that was written; a revision that moved anyway is never called saved.
            if (!tab.IsDisposed && ReferenceEquals(tab.Docx, docx) && model.Edits.Revision == revision)
            {
                model.Edits.MarkSaved();
                tab.UpdateDirty();
            }

            return (report, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, ex);
        }
        finally
        {
            docx.BeforeModelRead = previousBefore;
            docx.AfterModelRead = previousAfter;
            try
            {
                // Ends the save: what was typed meanwhile is replayed now. A replay that fails has faulted the session and the runner (the window shows it as any edit failure); the save itself is done.
                scope.Dispose();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Nothing more to do here.
            }
        }
    }
}
