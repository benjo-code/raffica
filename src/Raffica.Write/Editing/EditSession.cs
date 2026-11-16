using Avalonia.Threading;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Viewing;

namespace Raffica.Write.Editing;

/// <summary>Why <see cref="EditSession.CanEdit"/> is false (the status hint names it).</summary>
internal enum EditBlockedReason
{
    /// <summary>Only a prefix of the file is open: the whole model is read on a pool thread and editing starts when it is there.</summary>
    StillOpening,

    /// <summary>A save runs (<see cref="EditSession.BeginSave"/>): input is queued and replayed after it.</summary>
    Saving,

    /// <summary>The open or the layout ended with an error, or the tab is closed: nothing will become editable.</summary>
    Faulted,
}

/// <summary>
/// The editing state of one tab: the <see cref="TextEditor"/> of its whole model, the caret and the selection, and the one way the document changes,
/// <see cref="Apply"/>. Made on first use (<see cref="WriteTab.EnsureEditor"/>), never at startup; the editor itself is made on the first operation.
/// </summary>
/// <remarks>
/// Rules. All members run on the UI thread (the dispatcher is captured at construction; a call from another thread throws). The model changes only inside
/// <see cref="Apply"/> (or the replay of the queue), under the runner's edit lease: no layout step runs while the model changes, and the layout that the view
/// shows is brought up to date with <see cref="DocumentLayout.ApplyEdit"/> before the lease ends, so a view that reads pages after <see cref="Changed"/> sees
/// pages and model agree (between the engine swap and the page swap of one edit <c>Locate</c> and <c>BlockAt</c> may mix old and new: nobody reads them in
/// that moment, because the UI thread is inside Apply). Only the active view's layout follows an edit (<see cref="ActiveMode"/>): the other one is dropped
/// (<see cref="LayoutRunner.DropFlowLayout"/>) or marked stale (<see cref="LayoutRunner.DropPageLayout"/>) and made again on the switch. The model is the
/// document the tab holds once it is whole; the object does not change after that (a reload would make the session start again with a new
/// <see cref="TextEditor"/>: when the tab's model is another object the session rebinds, <see cref="TextEditor.History"/> is cleared and the selection goes
/// to the start of the document).
/// </remarks>
internal sealed class EditSession
{
    /// <summary>The most operations that are kept while a save runs; more are refused (counted in <see cref="DroppedWhileSaving"/>).</summary>
    public const int MaxQueued = 10_000;

    // The codec of the clipboard fragment (stateless, shared by every editor).
    private static readonly DocxFragmentCodec s_fragmentCodec = new();

    /// <summary>Whether the bytes of a clipboard fragment can be read by the codec (the editor treats a fragment that cannot as absent). The bytes are untrusted: a refusal of any kind is "no".</summary>
    internal static bool FragmentReadable(byte[] bytes)
    {
        try
        {
            return s_fragmentCodec.Read(bytes).Count > 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    private readonly WriteTab _tab;
    private readonly Dispatcher _ui;
    private readonly List<(Func<TextEditor, EditResult?> Operation, int ThroughPage, object? Key, bool Typing)> _queue = [];
    private readonly RefreshableFormat _format = new();
    private ((WordDocument Model, int Revision, TextRange Range) Key, FormatFacts Facts)? _facts;
    private bool _selectionDirty;
    private TextEditor? _editor;
    private WordDocument? _bound;
    private TextRange? _selection;
    private LayoutMode _activeMode = LayoutMode.Pages;
    private int _saving;
    private int _dropped;

    /// <param name="tab">The tab; made on the UI thread (the session keeps that thread's dispatcher).</param>
    public EditSession(WriteTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tab = tab;
        _ui = Dispatcher.UIThread;
    }

    /// <summary>
    /// The character format the next typed text takes (owned by the session, never in the model, the history or the revision). Cleared by a real selection change
    /// (<see cref="SetSelection"/> or an operation that moves the selection), by every <see cref="Apply"/> that is not typed text, and by the view on Esc.
    /// </summary>
    public PendingFormat Pending { get; } = new();

    /// <summary>
    /// Raised on the UI thread when the selection really changed (another position or another paragraph object): by <see cref="SetSelection"/> at once, by an edit or by the
    /// replay of a save's queue after <see cref="Changed"/> (once per batch).
    /// </summary>
    public event Action? SelectionChanged;

    /// <summary>Raised on the UI thread once after an operation that changed the document or the selection (not for a refused one), after the lease ended.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised on the UI thread, once per replayed batch, when input that a save queued was not (fully) replayed and the user must hear of it: the session could no longer
    /// edit when the save ended (the reason says why), or a replayed operation failed and faulted the session (<see cref="EditBlockedReason.Faulted"/>). The operations that were
    /// not replayed are counted in <see cref="DroppedWhileSaving"/>. A stale-position refusal of one operation is only skipped and raises nothing.
    /// </summary>
    public event Action<EditBlockedReason>? InputDropped;

    /// <summary>
    /// Whether an exception of an operation must fault the session: every exception except <see cref="StalePositionException"/>, the text editor's own refusal of a stale position,
    /// which it throws before it has changed anything. Any other exception, argument exceptions of the framework included, may have been thrown after the model changed and before
    /// the revision was raised (the revision is raised at the end of an operation), so nothing can tell whether model, history and layout still agree. The key path, the command path
    /// and the replay of a save's queue use this one rule.
    /// </summary>
    internal static bool MustFault(Exception exception) => exception is not StalePositionException;

    /// <summary>Why editing is not possible now; null when it is. Faulted wins over StillOpening, which wins over Saving.</summary>
    public EditBlockedReason? BlockedReason
    {
        get
        {
            if (_tab.IsDisposed || _tab.Runner.IsFaulted)
            {
                return EditBlockedReason.Faulted;
            }

            if (!_tab.Runner.FullModelReady)
            {
                return EditBlockedReason.StillOpening;
            }

            return _saving > 0 ? EditBlockedReason.Saving : null;
        }
    }

    /// <summary>True from <see cref="BeginSave"/> until the last handle is disposed (the replay of what was queued runs at that moment, on the UI thread).</summary>
    public bool IsSaving => _saving > 0;

    /// <summary>True when the whole model is loaded, nothing failed and no save runs.</summary>
    public bool CanEdit => BlockedReason is null;

    /// <summary>The caret: the moving end of the selection; null before the first operation or for a document without a paragraph at the top level.</summary>
    public TextPosition? Caret => Selection?.End;

    /// <summary>The selection (collapsed for a caret); the start of the document before the first operation once the model is whole. Positions belong to the current model.</summary>
    public TextRange? Selection
    {
        get
        {
            if (_selection is null && CanEdit)
            {
                Bind();
            }

            return _selection;
        }
    }

    /// <summary>Puts the selection (the pointer and keys of the view do; the positions must belong to the model). Raises nothing.</summary>
    public void SetSelection(TextRange? selection)
    {
        VerifyAccess();
        if (SameSelection(_selection, selection))
        {
            return;
        }

        _selection = selection;
        Pending.Clear();
        SelectionChanged?.Invoke();
    }

    private static bool SameSelection(TextRange? a, TextRange? b) =>
        a is null || b is null ? a is null && b is null : SamePosition(a.Value.Start, b.Value.Start) && SamePosition(a.Value.End, b.Value.End);

    private static bool SamePosition(TextPosition a, TextPosition b) => ReferenceEquals(a.Paragraph, b.Paragraph) && a.Offset == b.Offset && a.Story.Equals(b.Story);

    // Puts the selection of an operation (inside Run): the event is due when it moved, and anything but typed text ends the pending format.
    private void MoveSelection(TextRange selection, bool typing)
    {
        if (!SameSelection(_selection, selection))
        {
            _selectionDirty = true;
        }

        _selection = selection;
        if (!typing)
        {
            Pending.Clear();
        }
    }

    private void RaiseSelectionChanged()
    {
        if (_selectionDirty)
        {
            _selectionDirty = false;
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>
    /// The layout the next edit follows: pages (the default) or strips. Switching to pages makes a stale pages layout again at once
    /// (<see cref="LayoutRunner.RebuildPageLayoutIfStale"/>), so the pages layout is never both active and stale.
    /// </summary>
    public LayoutMode ActiveMode
    {
        get => _activeMode;
        set
        {
            VerifyAccess();
            _activeMode = value;
            if (value == LayoutMode.Pages && !_tab.IsDisposed)
            {
                _tab.Runner.RebuildPageLayoutIfStale();
            }
        }
    }

    /// <summary>The operations refused because the queue of a save was full.</summary>
    public int DroppedWhileSaving => Volatile.Read(ref _dropped);

    /// <summary>The operations waiting for a save to end.</summary>
    public int PendingCount => _queue.Count;

    /// <summary>True when an undo step is there. Safe: read on the UI thread between operations (no lease is held then); false before the first operation.</summary>
    public bool CanUndo => _editor?.History.CanUndo ?? false;

    /// <summary>True when a redo step is there (see <see cref="CanUndo"/>).</summary>
    public bool CanRedo => _editor?.History.CanRedo ?? false;

    /// <summary>
    /// The facts the rules of the formatting commands need about <paramref name="range"/> (see <see cref="TextEditor.ReadFormatFacts"/>, which bounds the read for a large selection). The answer
    /// is remembered for the model, its revision and the range, so every further question while nothing of the three changed (a scroll, a zoom, the next command asked) costs nothing; a
    /// selection change, an edit (a style edit raises the revision too) or a new model reads again. Call it on the UI thread and only while <see cref="CanEdit"/>.
    /// </summary>
    public FormatFacts ReadFormatFacts(TextRange range)
    {
        var model = _tab.Docx.Model;
        var key = (model, model.Edits.Revision, range);
        if (_facts is { } known && known.Key.Equals(key))
        {
            return known.Facts;
        }

        var facts = Editor.ReadFormatFacts(range);
        FactReads++;
        _facts = (key, facts);
        return facts;
    }

    /// <summary>How many times <see cref="ReadFormatFacts"/> read the model instead of answering from memory (tests).</summary>
    internal int FactReads { get; private set; }

    /// <summary>The text editor of the current model; made on first use. Throws unless <see cref="CanEdit"/>. Use it only inside an operation of <see cref="Apply"/>.</summary>
    public TextEditor Editor => Bind();

    /// <summary>
    /// Runs one editing operation on the model and brings the active layout up to date: takes the edit lease, runs <paramref name="operation"/> on the UI thread,
    /// calls <see cref="DocumentLayout.ApplyEdit"/> for every <see cref="BlockChange"/> of the result in order on the active layout (pages through
    /// <paramref name="throughPage"/> are right when this returns), drops the other layout, sets the caret from the result (a <see cref="EditResult.Selected"/>
    /// range becomes the selection and the document stays as it is), ends the lease and raises <see cref="Changed"/> once. A refused operation (a null result, or
    /// no changes and no <c>Selected</c>) changes nothing and raises nothing. Returns the result, or null when refused, blocked or queued (<see cref="BlockedReason"/>
    /// says which of the last two).
    /// </summary>
    /// <remarks>
    /// While a save runs the operation is queued (null is returned) and replayed in arrival order when the save ends, through the same path under one lease for the
    /// whole batch with one <see cref="Changed"/>; an operation that is queued must read the caret and selection when it runs, not when it is queued. The queue holds
    /// <see cref="MaxQueued"/> operations; later ones are refused and counted. An operation with a <paramref name="coalesceKey"/> that directly follows a queued operation with the same key (the same object) replaces it, so a gesture that
    /// changes the selection many times (a drag) costs one slot; an operation of another kind in between ends the run. If an operation of the replay throws, the exception does not reach the caller of the save handle's Dispose: a
    /// <see cref="StalePositionException"/> only skips that operation (counted in <see cref="DroppedWhileSaving"/>), any other exception faults the session (<see cref="MustFault"/>), the failed
    /// operation and the rest of the batch are counted in <see cref="DroppedWhileSaving"/> and <see cref="InputDropped"/> is raised once. An operation that is not queued and throws propagates to
    /// the caller of <see cref="Apply"/>, which decides with <see cref="MustFault"/> whether the session must fault. The <see cref="BlockChange.OldBlocks"/> of the returned result are read-only snapshots (the history's own for an undo or redo): never
    /// change them. A cut is a copy and a delete: when its result has no <c>Changes</c> the delete was refused and the clipboard must not be replaced. An exception of
    /// the layout (it is faulted for good) makes the session faulted (<see cref="EditBlockedReason.Faulted"/>, the runner's fault is the same one): it goes to the caller once, after the lease ended and <see cref="Changed"/> was raised; every later call is refused without touching the model.
    /// </remarks>
    public EditResult? Apply(Func<TextEditor, EditResult?> operation, int throughPage, object? coalesceKey = null, bool typing = false)
    {
        ArgumentNullException.ThrowIfNull(operation);
        VerifyAccess();
        switch (BlockedReason)
        {
            case null:
                break;
            case EditBlockedReason.Saving:
                if (coalesceKey is not null && _queue.Count > 0 && ReferenceEquals(_queue[^1].Key, coalesceKey))
                {
                    // The same gesture again (a drag): the latest operation replaces the one before it, which nothing has followed.
                    _queue[^1] = (operation, throughPage, coalesceKey, typing);
                }
                else if (_queue.Count >= MaxQueued)
                {
                    Interlocked.Increment(ref _dropped);
                }
                else
                {
                    _queue.Add((operation, throughPage, coalesceKey, typing));
                }

                return null;
            default:
                return null;
        }

        EditResult? result = null;
        var changed = false;
        try
        {
            using var lease = _tab.Runner.PauseForEdit();
            result = Run(operation, throughPage, typing, ref changed);
        }
        finally
        {
            if (changed)
            {
                Changed?.Invoke();
            }

            RaiseSelectionChanged();
        }

        return result;
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the input method preedit at <paramref name="at"/> on the active layout, or removes it (null or empty text), under the edit
    /// lease: the preedit is in no model, so it does not touch the history, the revision or the dirty state, and a save may run while it is shown (it is allowed
    /// while <see cref="EditBlockedReason.Saving"/>). Refused (false) while only a prefix is open, after a failure, and when the layout cannot show it there. A layout failure
    /// faults the session as for an edit and goes on to the caller.
    /// </summary>
    public bool SetPreedit(TextPosition at, string? text, int throughPage)
    {
        VerifyAccess();
        if (BlockedReason is EditBlockedReason.Faulted or EditBlockedReason.StillOpening)
        {
            return false;
        }

        var runner = _tab.Runner;
        var active = _activeMode == LayoutMode.Flow ? runner.FlowLayout : (runner.PageLayoutIsStale ? null : runner.PageLayout);
        if (active is null)
        {
            return false;
        }

        try
        {
            using var lease = runner.PauseForEdit();
            return active.SetPreedit(at, text, throughPage);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            runner.FailEdit(ex);
            DropOther(runner);
            throw;
        }
    }

    /// <summary>
    /// Starts a save: <see cref="CanEdit"/> is false with the reason <see cref="EditBlockedReason.Saving"/> until the returned handle is disposed (twice is safe), and
    /// input meanwhile is queued. Disposing the last open handle replays the queue. For the save flow of 4.3b; nests. <c>BeginSave</c> is a UI-thread call; the
    /// handle may be disposed on any thread (twice, or from two threads, ends the save once): a dispose off the UI thread is marshaled to the UI thread through
    /// the dispatcher captured at construction, so the save ends (and the replay runs) when that thread next runs its queue, and <see cref="CanEdit"/> stays false until then.
    /// An exception of the replay then reaches the dispatcher's unhandled-exception path instead of the disposing caller.
    /// </summary>
    public IDisposable BeginSave()
    {
        VerifyAccess();
        _saving++;
        return new SaveScope(this);
    }

    // On the UI thread (SaveScope marshals).
    private void EndSave()
    {
        if (--_saving > 0 || _queue.Count == 0)
        {
            return;
        }

        var batch = _queue.ToArray();
        _queue.Clear();
        if (BlockedReason is { } blocked)
        {
            // Nothing can be replayed (the session failed during the save, or the model is no longer whole): the input is dropped, counted, and the user is told.
            Interlocked.Add(ref _dropped, batch.Length);
            InputDropped?.Invoke(blocked);
            return;
        }

        var changed = false;
        EditBlockedReason? dropped = null;
        try
        {
            using var lease = _tab.Runner.PauseForEdit();
            for (var i = 0; i < batch.Length; i++)
            {
                try
                {
                    Run(batch[i].Operation, batch[i].ThroughPage, batch[i].Typing, ref changed);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    var runner = _tab.Runner;
                    if (BlockedReason != EditBlockedReason.Faulted)
                    {
                        if (!MustFault(ex))
                        {
                            // The editor refused a stale position before changing anything: this one operation is lost, the rest still run.
                            Interlocked.Increment(ref _dropped);
                            continue;
                        }

                        runner.FailEdit(ex);
                        DropOther(runner);
                    }

                    // The session is faulted (by the layout inside Run, or just now): the failed operation and the rest of the batch are not replayed.
                    Interlocked.Add(ref _dropped, batch.Length - i);
                    changed = true;
                    dropped = EditBlockedReason.Faulted;
                    break;
                }
            }
        }
        finally
        {
            if (changed)
            {
                Changed?.Invoke();
            }

            RaiseSelectionChanged();
        }

        if (dropped is { } reason)
        {
            InputDropped?.Invoke(reason);
        }
    }

    // The operation and the layout, under the lease. Sets changed when the document or the selection changed (also when the layout then throws).
    private EditResult? Run(Func<TextEditor, EditResult?> operation, int throughPage, bool typing, ref bool changed)
    {
        var editor = Bind();
        var result = operation(editor);
        if (result is null)
        {
            return null;
        }

        var styled = result.Style is not null;
        if (result.Changes.Count == 0 && !styled)
        {
            if (result.Selected is { } selected)
            {
                MoveSelection(selected, typing);
                changed = true;
                return result;
            }

            return null;
        }

        // The model has changed now: whatever happens next, the view must hear of it.
        changed = true;
        // A formatting operation changes no text: the selection that was there stays (a toggle pressed twice must see the same selection twice).
        MoveSelection(result.KeepSelection && _selection is { } kept ? kept : new TextRange(result.Caret, result.Caret), typing);
        if (styled)
        {
            // The style sheet changed (a style step, or an undo or redo that carries one): the editor's resolver snapshots the styles and is made again.
            _format.Invalidate(_tab.Docx.Model);
        }

        var runner = _tab.Runner;
        var active = _activeMode == LayoutMode.Flow ? runner.FlowLayout : (runner.PageLayoutIsStale ? null : runner.PageLayout);
        try
        {
            if (active is not null)
            {
                if (styled)
                {
                    // Restyle starts the layout over on a fresh engine that reads the model as it is now, block changes included: applying the block changes into it as well
                    // would plan the same blocks twice, so they are not applied (the oracle gates of the formatting steps do the same).
                    active.Restyle(throughPage);
                }
                else
                {
                    foreach (var change in result.Changes)
                    {
                        active.ApplyEdit(change, throughPage);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The layout is faulted for good and no longer shows the model: the session is faulted with it (CanEdit false, nothing more is edited), the other
            // layout is dropped too, and the caller hears of it once (Apply raises Changed, then the exception goes on).
            runner.FailEdit(ex);
            DropOther(runner);
            throw;
        }

        DropOther(runner);
        return result;
    }

    private void DropOther(LayoutRunner runner)
    {
        if (_activeMode == LayoutMode.Flow)
        {
            runner.DropPageLayout();
        }
        else
        {
            runner.DropFlowLayout();
        }
    }

    // The editor of the tab's current model: made on first use; another model object makes a new one (and ends the history of the old).
    private TextEditor Bind()
    {
        if (BlockedReason is { } reason)
        {
            throw new InvalidOperationException($"The document cannot be edited now ({reason}).");
        }

        var model = _tab.Docx.Model;
        if (_editor is null || !ReferenceEquals(_bound, model))
        {
            _editor?.History.Clear();
            _format.Invalidate(model);
            _editor = new TextEditor(model, Host, format: _format) { FragmentCodec = s_fragmentCodec };
            _bound = model;
            _selection = StartOf(model);
        }

        return _editor;
    }

    private IProjectionHost Host(StoryRef story, Paragraph paragraph)
    {
        var runner = _tab.Runner;
        var layout = (_activeMode == LayoutMode.Flow ? runner.FlowLayout : null) ?? runner.PageLayout;
        return layout.ProjectionHost(story, paragraph);
    }

    private static TextRange? StartOf(WordDocument model)
    {
        foreach (var block in model.Body.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                var start = new TextPosition(StoryRef.Body, paragraph, 0);
                return new TextRange(start, start);
            }
        }

        return null;
    }

    private void VerifyAccess()
    {
        if (!_ui.CheckAccess())
        {
            throw new InvalidOperationException("The editing session belongs to the UI thread.");
        }
    }

    // The editor's format resolver snapshots the styles when it is made: made again after every style change and for another model.
    private sealed class RefreshableFormat : IEffectiveFormat
    {
        private LayoutEffectiveFormat? _inner;
        private WordDocument? _model;

        public void Invalidate(WordDocument model)
        {
            _model = model;
            _inner = null;
        }

        public EffectiveRun ForRun(StoryRef story, Paragraph paragraph, Run run) => Inner().ForRun(story, paragraph, run);

        public EffectiveRun ForMark(StoryRef story, Paragraph paragraph) => Inner().ForMark(story, paragraph);

        private LayoutEffectiveFormat Inner() => _inner ??= new LayoutEffectiveFormat(_model ?? throw new InvalidOperationException("No document."));
    }

    private sealed class SaveScope(EditSession owner) : IDisposable
    {
        private EditSession? _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is not { } session)
            {
                return;
            }

            if (session._ui.CheckAccess())
            {
                session.EndSave();
            }
            else
            {
                session._ui.Post(session.EndSave, DispatcherPriority.Normal);
            }
        }
    }
}
