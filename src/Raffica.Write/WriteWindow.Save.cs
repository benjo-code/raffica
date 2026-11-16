using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Raffica.Write.Commands;
using Raffica.Write.Dialogs;
using Raffica.Write.Documents;

namespace Raffica.Write;

/// <summary>The part of the window that saves: the commands Save and Save as, the questions and cards of <see cref="WriteSaveFlow"/> (through the dialog queue) and the close of a tab.</summary>
internal sealed partial class WriteWindow
{
    /// <summary>How long the one-line note of a save stays in the status bar when nothing ends it sooner.</summary>
    internal static readonly TimeSpan SaveNoteDuration = TimeSpan.FromSeconds(10);

    private bool _savePickerShowing;

    /// <summary>The save and Save as flow of the window (tests await it; the commands and the close of a tab run through it).</summary>
    internal WriteSaveFlow Saves => _saves;

    /// <summary>Replaces the system Save as dialog (tests): returns the chosen path and whether a file stood there, or null when the user cancelled. Null: the system dialog.</summary>
    internal Func<WriteTab, Task<SavePick?>>? SavePicker { get; set; }

    /// <summary>Saves the active tab over its file (the command Save); the outcome is for the tests, everything the user needs was shown.</summary>
    internal Task<SaveOutcome> SaveActiveAsync() => Workspace.Active is { } tab ? SaveAndFocusAsync(tab, saveAs: false) : Task.FromResult<SaveOutcome>(new SaveRefused(SaveRefusal.NoDocument));

    /// <summary>Saves the active tab under a path the user chooses (the command Save as).</summary>
    internal Task<SaveOutcome> SaveAsActiveAsync() => Workspace.Active is { } tab ? SaveAndFocusAsync(tab, saveAs: true) : Task.FromResult<SaveOutcome>(new SaveRefused(SaveRefusal.NoDocument));

    private async Task<SaveOutcome> SaveAndFocusAsync(WriteTab tab, bool saveAs)
    {
        var outcome = saveAs ? await _saves.SaveAsAsync(tab) : await _saves.SaveAsync(tab);
        FocusActiveView();
        RefreshCommandStates();
        return outcome;
    }

    // ISaveInteraction. The card of a tab that is not the active one selects it first, so the user sees which document is meant.

    Task<ChangedChoice> ISaveInteraction.AskChangedOriginalAsync(WriteTab tab)
    {
        ShowTab(tab);
        return Dialogs.ShowAsync<ChangedChoice>(done => ChangedOriginalCard.Create(tab.Title, done));
    }

    async Task<SavePick?> ISaveInteraction.PickSavePathAsync(WriteTab tab)
    {
        if (_savePickerShowing)
        {
            return null;
        }

        ShowTab(tab);
        _savePickerShowing = true;

        // The picker may take the key up of the S of Ctrl+Shift+S: without this the S would stay "down" and the next Ctrl+S would count as a repeat.
        _keysDown.Clear();
        try
        {
            return SavePicker is { } picker ? await picker(tab) : await PickSaveWithSystemDialogAsync(tab);
        }
        finally
        {
            _savePickerShowing = false;
            _keysDown.Clear();
        }
    }

    private async Task<SavePick?> PickSaveWithSystemDialogAsync(WriteTab tab)
    {
        IStorageFolder? start = null;
        if (Path.GetDirectoryName(tab.Path) is { Length: > 0 } folder)
        {
            start = await StorageProvider.TryGetFolderFromPathAsync(folder);
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = WriteStrings.SaveDialogTitle,
            SuggestedFileName = tab.Title,
            DefaultExtension = "docx",
            ShowOverwritePrompt = true,
            SuggestedStartLocation = start,
            FileTypeChoices =
            [
                new FilePickerFileType(WriteStrings.OpenFilterDocx)
                {
                    Patterns = ["*.docx", "*.DOCX"],
                    MimeTypes = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
                    AppleUniformTypeIdentifiers = ["org.openxmlformats.wordprocessingml.document"],
                },
            ],
        });

        // The picker asked before it answered whether an existing file may be replaced: a file that stands there now is the one the user confirmed.
        return file?.TryGetLocalPath() is { } path ? new SavePick(path, File.Exists(path)) : null;
    }

    Task ISaveInteraction.ShowErrorAsync(WriteTab tab, SaveProblem problem)
    {
        ShowTab(tab);
        return Dialogs.ShowAsync<bool>(done => SaveErrorCard.Create(tab.Title, problem, () => done(true)));
    }

    void ISaveInteraction.Note(string text)
    {
        Status.Hint = text;
        DispatcherTimer.RunOnce(
            () =>
            {
                if (Status.Hint == text)
                {
                    Status.Hint = null;
                }
            },
            SaveNoteDuration);
    }

    bool ISaveInteraction.IsOpenInAnotherTab(string path, WriteTab tab) => Workspace.Find(path) is { } other && !ReferenceEquals(other, tab);

    void ISaveInteraction.PathChanged(WriteTab tab)
    {
        _recent.Touch(tab.Path);
        StatesChanged?.Invoke();
    }

    private void ShowTab(WriteTab tab)
    {
        if (Workspace.Tabs.Contains(tab) && !ReferenceEquals(Workspace.Active, tab))
        {
            Workspace.Select(tab);
        }

        BringToFront();
    }

    // Closing a tab: asked once per tab, however it was asked for (button, key, command, the close of the window). A second request while the question is open is the same wish.

    /// <summary>
    /// Closes the tab when it has no unsaved changes or the user answered Save (and it worked) or Don't save; Cancel, or a save that failed (its card was shown), keeps
    /// the tab. A save that is running is waited for first.
    /// </summary>
    internal Task CloseTabAsync(WriteTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (_closingTabs.TryGetValue(tab, out var running))
        {
            return running;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _closingTabs[tab] = done.Task;
        _ = CloseTabCoreAsync(tab, done);
        return done.Task;
    }

    private async Task CloseTabCoreAsync(WriteTab tab, TaskCompletionSource done)
    {
        try
        {
            if (await _closeGuard.CanCloseAsync(tab) && !_closeConfirmed)
            {
                Workspace.Close(tab);
            }

            done.TrySetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            done.TrySetException(ex);
        }
        finally
        {
            _closingTabs.Remove(tab);
            DrainHandover();
        }
    }

    // "Save changes?": the answer completes where the click happened (the guard goes on in the same turn), not on a pool thread.
    private Task<CloseChoice> AskCloseAsync(WriteTab tab)
    {
        ShowTab(tab);
        var answer = new TaskCompletionSource<CloseChoice>();
        _ = Dialogs.ShowAsync<CloseChoice>(done => UnsavedChangesCard.Create(tab.Title, choice =>
        {
            done(choice);
            answer.TrySetResult(choice);
        })).ContinueWith(
            shown =>
            {
                if (shown.IsFaulted)
                {
                    answer.TrySetException(shown.Exception!.InnerExceptions);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return answer.Task;
    }

    // The save of the close question. A document whose editing failed cannot be saved over its file, but the user said "Save": that is Save as (an explicit act, the picker asks where).
    private async Task<bool> SaveForCloseAsync(WriteTab tab)
    {
        var state = SaveCommandState.Of(tab, _saves.IsRunning(tab));
        var outcome = state is { Faulted: true, FaultedByEdit: true } ? await _saves.SaveAsAsync(tab) : await _saves.SaveAsync(tab);
        return outcome is Saved or SavedAs;
    }
}
