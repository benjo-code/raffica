using Raffica.Hosting;
using Raffica.Write.Dialogs;
using Raffica.Write.Documents;
using Raffica.Write.Recovery;

namespace Raffica.Write;

/// <summary>
/// The part of the window that keeps the recovery copies: the <see cref="AutosaveScheduler"/> is made when the first tab becomes dirty (never at start), a deactivation asks for a
/// copy at once, and a tab's copy is deleted only after the close questions were answered Save or Don't save (never for a Cancel and never for a bare dispose of the window).
/// </summary>
internal sealed partial class WriteWindow
{
    private readonly Dictionary<WriteTab, WriteTabRecoverySubject> _recoverySubjects = [];
    private AutosaveScheduler? _autosave;

    /// <summary>The autosave of this window; null until a tab first became dirty.</summary>
    internal AutosaveScheduler? Autosave => _autosave;

    /// <summary>Makes the clock of the autosave (tests pass a manual one). Called on the UI thread, once, with the first dirty tab.</summary>
    internal Func<IRecoveryClock> RecoveryClockFactory { get; set; } = static () => new DispatcherRecoveryClock();

    /// <summary>The folder of the copies of this window: next to the settings file, never the folder of the user when the command line says otherwise.</summary>
    internal string RecoveryFolder => RecoveryStore.FolderFor(_options.SettingsFile);

    private void AttachRecovery() => Deactivated += (_, _) => _autosave?.TickNow();

    private void WatchRecovery(WriteTab tab) => tab.DirtyChanged += () => OnTabDirtyChanged(tab);

    private void OnTabDirtyChanged(WriteTab tab)
    {
        // No copies in a measurement run (the typing-snapshot case drives the snapshot itself); and nothing here may break the edit that raised the event.
        if (_closeConfirmed || tab.IsDisposed || _options.IsMeasuring)
        {
            return;
        }

        try
        {
            if (tab.IsDirty)
            {
                EnsureAutosave().Track(SubjectOf(tab));
            }
            else if (_autosave is not null && _recoverySubjects.TryGetValue(tab, out var subject))
            {
                _autosave.Cleaned(subject);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The autosave is a convenience: a failure here is silent and typing goes on.
            RecoveryWiringFailures++;
        }
    }

    private AutosaveScheduler EnsureAutosave() =>
        _autosave ??= new AutosaveScheduler(new RecoveryStore(RecoveryFolder), RecoveryClockFactory(), () => Settings.Shell.Autosave);

    /// <summary>How many times the wiring of the autosave threw (and was ignored).</summary>
    internal int RecoveryWiringFailures { get; private set; }

    private WriteTabRecoverySubject SubjectOf(WriteTab tab)
    {
        if (!_recoverySubjects.TryGetValue(tab, out var subject))
        {
            subject = new WriteTabRecoverySubject(tab, id: null, saveRunning: () => _saves.IsRunning(tab));
            _recoverySubjects[tab] = subject;
        }

        return subject;
    }

    // The close questions of these tabs were answered Save or Don't save (or the tab was clean): their copies go.
    private void ReleaseRecovery(IEnumerable<WriteTab> tabs)
    {
        foreach (var tab in tabs)
        {
            if (_recoverySubjects.Remove(tab, out var subject))
            {
                _autosave?.Release(subject);
            }
        }
    }

    private void ForgetRecovery(WriteTab tab) => _recoverySubjects.Remove(tab);

    private void StopRecovery()
    {
        _autosave?.Dispose();
        _recoverySubjects.Clear();
    }

    // The offer of the copies a crash left behind.

    /// <summary>
    /// The one coordinator of the process (the app sets it; null: this window never offers, as in the tests that do not look at it). The first window that asks runs the offer.
    /// </summary>
    internal RecoveryCoordinator? RecoveryOffer { get; set; }

    /// <summary>Tells whether another Write process is alive (tests replace it).</summary>
    internal IProcessProbe RecoveryProbe { get; set; } = new SystemProcessProbe();

    /// <summary>The id of this process (tests replace it).</summary>
    internal int SelfProcessId { get; set; } = Environment.ProcessId;

    /// <summary>The task of the offer this window started (null when it started none); tests await it.</summary>
    internal Task? RecoveryOfferTask { get; private set; }

    /// <summary>
    /// Offers the recovery copies once per process: called after the first sharp page of the first tab, or after <c>Opened</c> when the window has no file. Never in a measurement run
    /// and never for a window that is closing; nothing of the folder is read before this call.
    /// </summary>
    internal void RequestRecoveryOffer()
    {
        if (RecoveryOffer is not { } coordinator || _options.IsMeasuring || IsClosing)
        {
            return;
        }

        RecoveryOfferTask = coordinator.OfferOnce(() =>
        {
            var flow = new RecoveryFlow(new RecoveryStore(RecoveryFolder), RecoveryProbe, new WindowRecoveryUi(this), SelfProcessId);
            return GuardAsync(() => flow.RunAsync(_closeToken));
        });
    }

    /// <summary>
    /// Opens a recovery copy as a tab that belongs to no file (title: the old name or the next "Untitled N"; Save is Save as with the old name suggested), adopts it into the autosave
    /// under the id of the copy (so the next copy replaces it and no second one is made) and marks it dirty. The copy is deleted only after the tab was saved or closed with Save or Don't save.
    /// Null when the copy cannot be opened.
    /// </summary>
    internal async Task<WriteTab?> OpenRecoveredAsync(RecoveryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (IsClosing)
        {
            return null;
        }

        WriteTab? tab;
        try
        {
            tab = await Flow.OpenRecoveredAsync(new RecoveryStore(RecoveryFolder).DocxPathOf(entry.Id), entry.OriginalPath, _closeToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        if (tab is null)
        {
            return null;
        }

        // Adopt first: the dirty mark below makes the autosave track the tab, and it must find the subject with the id of the copy, not make a new one.
        try
        {
            var subject = new WriteTabRecoverySubject(tab, entry.Id, saveRunning: () => _saves.IsRunning(tab));
            _recoverySubjects[tab] = subject;
            EnsureAutosave().Adopt(subject, entry.Id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            RecoveryWiringFailures++;
        }

        // The document is whole (a copy is not opened as a prefix), so the revision may be raised at once; the tab now has changes no file has.
        tab.Docx.Model.Edits.Raise();
        tab.UpdateDirty();
        return tab;
    }

    private sealed class WindowRecoveryUi(WriteWindow window) : IRecoveryUi
    {
        public Task<RecoveryChoice> AskAsync(IReadOnlyList<RecoveryEntry> entries) =>
            window.Dialogs.ShowAsync<RecoveryChoice>(done => RecoveryCard.Create(entries, done));

        public Task<WriteTab?> OpenRecoveredAsync(RecoveryEntry entry) => window.OpenRecoveredAsync(entry);

        public void ShowOpenError(RecoveryEntry entry) => window.ShowOpenError(entry.OriginalPath ?? entry.Title, OpenError.Corrupt);
    }
}
