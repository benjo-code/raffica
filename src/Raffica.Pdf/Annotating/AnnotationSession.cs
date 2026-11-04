using Raffica.Core.Annotations;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The annotation state of one tab: it owns the tab's <see cref="AnnotationEditor"/> and drives <see cref="PdfTab.IsDirty"/> from it. The tab
/// counts as dirty while the editor has unsaved changes, while an operation is in flight (<see cref="AnnotationEditor.IsBusy"/>) or while
/// a draft or stroke is pending (<see cref="BeginPending"/>), so a close right after a mark still asks. <see cref="AnnotationEditor.Changed"/>
/// arrives on any thread and is posted to the UI context before the tab or a handler is touched; nothing here waits synchronously for the editor.
/// </summary>
internal sealed class AnnotationSession : IDisposable
{
    private readonly PdfTab _tab;
    private readonly SynchronizationContext _ui;
    private readonly object _gate = new();
    private int _pending;
    private TaskCompletionSource? _idle;
    private volatile bool _disposed;
    private bool _marked;

    public AnnotationSession(PdfTab tab, SynchronizationContext ui)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(ui);
        _tab = tab;
        _ui = ui;
        Editor = new AnnotationEditor(tab.Session);
        Editor.Changed += OnEditorChanged;
    }

    /// <summary>Raised on the UI thread after the dirty, undo or redo state may have changed.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Raised at the start of <see cref="FlushAsync"/> (and so before a save, an undo or a close), on the calling thread, so that an open
    /// draft (the text box field) can end itself: a flush that waits for a draft nobody ends would never return.
    /// </summary>
    public event Action? FlushRequested;

    public AnnotationEditor Editor { get; }

    /// <summary>Whether the document has changes that are not saved, in flight or about to be made.</summary>
    public bool IsDirty => Editor.IsDirty || Editor.IsBusy || PendingCount > 0;

    public bool CanUndo => Editor.CanUndo;

    public bool CanRedo => Editor.CanRedo;

    private int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    /// <summary>
    /// A draft or stroke registers itself here until it is applied or dropped; dispose the result to end it. Call it on the UI thread.
    /// The handle must be disposed on every path (success, failure, cancel): closing the tab waits for it.
    /// </summary>
    public IDisposable BeginPending()
    {
        lock (_gate)
        {
            _pending++;
        }

        Refresh();
        return new Pending(this);
    }

    /// <summary>Waits for the pending drafts, then for the editor's operations; the tab's dirty state is current afterwards.</summary>
    public async Task FlushAsync()
    {
        FlushRequested?.Invoke();
        while (true)
        {
            Task? drafts;
            lock (_gate)
            {
                drafts = _pending > 0 && !_disposed ? (_idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task : null;
            }

            if (drafts is not null)
            {
                await drafts;
            }

            await Editor.WhenIdleAsync();
            if (PendingCount == 0 || _disposed)
            {
                break;
            }
        }

        Refresh();
    }

    public async Task UndoAsync()
    {
        await FlushAsync();
        await Editor.UndoAsync();
        Refresh();
    }

    public async Task RedoAsync()
    {
        await FlushAsync();
        await Editor.RedoAsync();
        Refresh();
    }

    /// <summary>Takes the state of the editor over into the tab and tells the listeners. UI thread.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        // The session clears only what it set itself: a tab marked dirty by other means (the 2.1 tests) stays so.
        if (IsDirty)
        {
            _marked = true;
            _tab.MarkDirty();
        }
        else if (_marked)
        {
            _marked = false;
            _tab.MarkClean();
        }

        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        Editor.Changed -= OnEditorChanged;

        // A flush that waits for a draft nobody will end (its owner is gone with the tab) must not hang. The flag is set under the lock,
        // so a flush either sees it or has its signal released here.
        TaskCompletionSource? release;
        lock (_gate)
        {
            _disposed = true;
            release = _idle;
            _idle = null;
        }

        release?.TrySetResult();
    }

    private void OnEditorChanged()
    {
        if (!_disposed)
        {
            _ui.Post(static state => ((AnnotationSession)state!).Refresh(), this);
        }
    }

    private void EndPending()
    {
        TaskCompletionSource? release = null;
        lock (_gate)
        {
            if (--_pending == 0)
            {
                release = _idle;
                _idle = null;
            }
        }

        release?.TrySetResult();
        Refresh();
    }

    private sealed class Pending(AnnotationSession owner) : IDisposable
    {
        private int _ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                owner.EndPending();
            }
        }
    }
}
