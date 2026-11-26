using Raffica.Write.Documents;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Refreshes the results of the fields of one tab after an editing burst and before a save. A refresh is not an edit: it takes the edit lease like one, rewrites results in the model through
/// <see cref="TextEditor.RefreshFields"/>, tells the layout through <see cref="DocumentLayout.ApplyEdit"/> and the selection through <see cref="EditSession.FollowDerivedContent"/>, and records nothing: no revision, no dirty mark, no recovery
/// copy, no undo step (it raises <see cref="DocumentEdits.DerivedVersion"/>).
/// </summary>
/// <remarks>
/// <b>Timing.</b> <see cref="Notify"/> (called by the session after every batch of operations) only remembers which blocks changed and restarts a one-shot timer of <see cref="Delay"/>; nothing is refreshed in a key handler. The tick runs the
/// passes on the UI thread, never while a save, an IME composition (the layout holds a preedit), a prefix document, a stale layout or a pending redo (a structural result waits for it) stands in the way: then it looks again after the same delay
/// (a faulted session or tab ends the work for good). A failure is counted (<see cref="Failures"/>) and retried (at most <see cref="MaxConsecutiveFailures"/> times in a row, until the next edit) and shows nothing. An edit that arrives during the
/// passes (<see cref="Notify"/> from inside one) ends them: they start over with the new blocks after the delay. <see cref="SettleAsync"/> runs the passes at once, synchronously, before a save writes the model.
/// <b>Threading.</b> Everything runs on the UI thread, which the dispatcher of the timer owns; the lease is taken there through <see cref="LayoutRunner.PauseForEdit"/> like every edit does, so no layout step, save snapshot or recovery
/// snapshot reads the model while the results are rewritten.
/// <b>First settle.</b> A runner is made by the first dirty edit, by the first completion of the layout of a document that has fields, or by a save of such a document; its first pass looks at the whole body and, when it was started by the timer,
/// waits for the layout to be complete. A document with no field at all is checked once (a scan that parses nothing) and then looked at only for the blocks an edit touched.
/// </remarks>
internal sealed class FieldRefreshRunner : IDisposable
{
    /// <summary>The time between the last edit and the refresh.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    /// <summary>The most passes of one refresh (a page result may need more than one; the last result stays and the case is counted).</summary>
    public const int MaxPasses = 3;

    /// <summary>The most failed refreshes in a row that are retried; the next edit starts the count again.</summary>
    public const int MaxConsecutiveFailures = 5;

    private readonly WriteTab _tab;
    private readonly IFieldTimer _timer;
    private readonly FieldHost _host;
    private readonly List<BlockChange> _touched = [];
    private bool _all = true;
    private bool _pending;
    private bool _hasFields;
    private bool _fieldsKnown;
    private bool _waitForRedo;
    private bool _disposed;
    private bool _running;
    private int _consecutiveFailures;
    private TextEditor? _bare;
    private WordDocument? _bareModel;

    public FieldRefreshRunner(WriteTab tab, IFieldClock clock)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(clock);
        _tab = tab;
        _host = new FieldHost(tab);
        _timer = clock.CreateTimer(OnTick);
    }

    /// <summary>The refreshes (passes of <see cref="TextEditor.RefreshFields"/>) that ran.</summary>
    public int Passes { get; private set; }

    /// <summary>The refreshes that threw (each is counted once and retried).</summary>
    public int Failures { get; private set; }

    /// <summary>The refreshes that used all <see cref="MaxPasses"/> passes and still had a page result that may change.</summary>
    public int Unsettled { get; private set; }

    /// <summary>The structural results that had to wait for a pending redo.</summary>
    public int StructuralWaits { get; private set; }

    /// <summary>The structural results that were attached to the newest history step.</summary>
    public int StructuralAttached { get; private set; }

    /// <summary>The fields Write left alone in the last whole-body refresh (see <see cref="FieldRefreshResult.NotUpdated"/>).</summary>
    public int NotUpdated { get; private set; }

    /// <summary>True while edits wait for the timer (or for a condition that holds it back).</summary>
    public bool IsPending => _pending;

    /// <summary>The host the refresh asks for labels and notes (tests).</summary>
    internal FieldHost Host => _host;

    /// <summary>Test seam: replaces the call of <see cref="TextEditor.RefreshFields"/> (the editor, the host, the scope and the touched blocks, null for the whole body).</summary>
    internal Func<TextEditor, IFieldHost, RefreshScope, IReadOnlyList<BlockChange>?, FieldRefreshResult>? Refresher { get; set; }

    /// <summary>Remembers that blocks changed (null: the refresh has to look at everything) and restarts the timer. Cheap: no model is read.</summary>
    public void Notify(IReadOnlyList<BlockChange>? changes)
    {
        if (_disposed)
        {
            return;
        }

        if (changes is null)
        {
            _all = true;
        }
        else
        {
            _touched.AddRange(changes);
        }

        _pending = true;
        _consecutiveFailures = 0;
        _timer.Restart(Delay);
    }

    /// <summary>
    /// Brings the results up to date now, before a save reads the model: the same passes as the timer's, run synchronously (the task is complete when this returns; it is a task for the passes that may yield later). A refresh that is blocked
    /// (a prefix document, a stale layout, an IME composition, a faulted session) is skipped, and so is one when the token is cancelled or the runner disposed; nothing is thrown for any of them.
    /// </summary>
    public Task SettleAsync(CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested || !_pending || _running)
        {
            return Task.CompletedTask;
        }

        if (Blocked(forSettle: true) == Hold.None)
        {
            _timer.Stop();
            RunPasses(token);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
        _touched.Clear();
    }

    private enum Hold
    {
        None,
        Later,
        Never,
    }

    private void OnTick()
    {
        if (_disposed || !_pending || _running)
        {
            return;
        }

        switch (Blocked(forSettle: false))
        {
            case Hold.Never:
                _pending = false;
                _touched.Clear();
                break;
            case Hold.Later:
                _timer.Restart(Delay);
                break;
            default:
                RunPasses(_tab.Token);
                break;
        }
    }

    // Why the refresh cannot run now: for good (nothing to follow any more), or for a while.
    private Hold Blocked(bool forSettle)
    {
        if (_tab.IsDisposed || !_tab.RefreshesFields || _tab.Runner.IsFaulted || _tab.Editor?.BlockedReason == EditBlockedReason.Faulted)
        {
            return Hold.Never;
        }

        if (_tab.Docx.IsPrefix || !_tab.Runner.FullModelReady || _tab.Editor?.IsSaving == true)
        {
            return Hold.Later;
        }

        if (_host.Layout is not { } layout || layout.PreeditText is not null)
        {
            return Hold.Later;
        }

        if (_waitForRedo)
        {
            if (_tab.Editor?.CanRedo == true)
            {
                return Hold.Later;
            }

            _waitForRedo = false;
        }

        // The first look at the whole body waits for the layout to be complete, so that nothing is asked of a half-built layout and the page loop (which needs it) can follow; a save does not wait.
        return !forSettle && _all && !layout.IsComplete ? Hold.Later : Hold.None;
    }

    private TextEditor EditorFor(WordDocument model)
    {
        if (_tab.Editor is { CanEdit: true } session)
        {
            return session.Editor;
        }

        if (_bare is null || !ReferenceEquals(_bareModel, model))
        {
            _bare = new TextEditor(model, static (_, _) => ProjectionHost.Plain);
            _bareModel = model;
        }

        return _bare;
    }

    private void RunPasses(CancellationToken token)
    {
        var model = _tab.Docx.Model;
        if (!ScanForFields(model))
        {
            return;
        }

        var wholeBody = _all;
        var touched = wholeBody ? null : _touched.ToArray();
        var snapshot = _touched.ToArray();
        _touched.Clear();
        _all = false;
        _pending = false;
        var content = new List<BlockChange>();
        var layoutFailed = false;
        var reported = false;
        _running = true;
        try
        {
            using var lease = _tab.Runner.PauseForEdit();
            var editor = EditorFor(model);
            FieldRefreshResult? last = null;
            for (var pass = 0; pass < MaxPasses && !token.IsCancellationRequested; pass++)
            {
                var scope = _host.IsComplete ? RefreshScope.WithPages : RefreshScope.OrderBased;
                var blocks = pass == 0 ? touched : null;
                last = Refresher is { } seam ? seam(editor, _host, scope, blocks) : editor.RefreshFields(_host, scope, blocks);
                Passes++;
                if (last.Content.Count > 0)
                {
                    content.AddRange(last.Content);
                    layoutFailed = true;
                    ApplyToLayout(last.Content);
                    layoutFailed = false;
                }

                if (last.Structural is { } record)
                {
                    HandleStructural(editor, record);
                }

                if (_pending)
                {
                    // An edit arrived during the passes: they end here and start over with the new blocks when the timer fires.
                    break;
                }

                if (last.Settled)
                {
                    break;
                }

                if (pass == MaxPasses - 1)
                {
                    Unsettled++;
                }
            }

            if (wholeBody && last is not null)
            {
                NotUpdated = last.NotUpdated;
                reported = true;
            }

            _consecutiveFailures = 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failures++;
            if (layoutFailed)
            {
                // The layout threw while it followed the change: it is faulted for good (the session with it) and nothing more is refreshed.
                _tab.Runner.FailEdit(ex);
                _pending = false;
            }
            else if (++_consecutiveFailures <= MaxConsecutiveFailures)
            {
                // Retried: the blocks of this try are looked at again together with whatever arrives meanwhile.
                _touched.InsertRange(0, snapshot);
                _all |= wholeBody;
                _pending = true;
                _timer.Restart(Delay);
            }
        }
        finally
        {
            _running = false;
        }

        if (content.Count > 0 && !_disposed && !_tab.IsDisposed)
        {
            _tab.Editor?.FollowDerivedContent(content);
            _tab.NotifyFieldsRefreshed();
        }

        if (reported && !_disposed && !_tab.IsDisposed)
        {
            _tab.ReportNotUpdated(NotUpdated);
        }
    }

    // False when there is nothing to look at: a document with no field at all (checked once; later only the blocks an edit touched are scanned for one).
    private bool ScanForFields(WordDocument model)
    {
        if (!_fieldsKnown)
        {
            _fieldsKnown = true;
            _hasFields = FieldView.AnyIn(model);
        }

        if (!_hasFields && !_all && (_touched.Any(TouchesFields)))
        {
            _hasFields = true;
        }

        if (_hasFields)
        {
            return true;
        }

        _touched.Clear();
        _all = false;
        _pending = false;
        return false;
    }

    // A changed block (before or after the change) that holds a field.
    private static bool TouchesFields(BlockChange change)
    {
        var after = Enumerable.Range(change.First, Math.Max(0, Math.Min(change.Added, change.Container.Count - change.First))).Select(i => change.Container[i]);
        return FieldView.AnyIn(after) || FieldView.AnyIn(change.OldBlocks);
    }

    // The new results are told to the layouts that exist: the one the session follows (the other is dropped and made again on its next use), or, while there is no session, the pages layout and the strips layout alike.
    private void ApplyToLayout(IReadOnlyList<BlockChange> content)
    {
        var session = _tab.Editor;
        var runner = _tab.Runner;
        if (session is not null)
        {
            if (session.ActiveLayout is { } active)
            {
                Apply(active, content);
            }

            session.DropInactiveLayout();
            return;
        }

        if (!runner.PageLayoutIsStale)
        {
            Apply(runner.PageLayout, content);
        }

        if (runner.FlowLayout is { } flow)
        {
            Apply(flow, content);
        }
    }

    private static void Apply(DocumentLayout layout, IReadOnlyList<BlockChange> content)
    {
        foreach (var change in content)
        {
            layout.ApplyEdit(change, Math.Max(0, layout.LaidOutPages - 1));
        }
    }

    // A structural result (the paragraph count of a list changed) belongs to the newest history step; the real producer comes with the table of contents (task 10), the paths are in place and tested with a double.
    private void HandleStructural(TextEditor editor, DerivedRecord record)
    {
        switch (editor.History.AttachDerived(record))
        {
            case AttachOutcome.Attached:
                StructuralAttached++;
                break;
            case AttachOutcome.MustWait:
                // A redo is pending: look again from the whole body once the redo stack is empty.
                StructuralWaits++;
                _waitForRedo = true;
                _all = true;
                _pending = true;
                _timer.Restart(Delay);
                break;
            default:
                break;
        }
    }
}
