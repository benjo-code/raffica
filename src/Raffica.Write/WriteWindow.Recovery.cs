using Raffica.Hosting;
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
        if (_closeConfirmed || tab.IsDisposed)
        {
            return;
        }

        if (tab.IsDirty)
        {
            _autosave ??= new AutosaveScheduler(new RecoveryStore(RecoveryFolder), RecoveryClockFactory(), () => Settings.Shell.Autosave);
            _autosave.Track(SubjectOf(tab));
        }
        else if (_autosave is not null && _recoverySubjects.TryGetValue(tab, out var subject))
        {
            _autosave.Cleaned(subject);
        }
    }

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
}
