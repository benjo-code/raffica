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
    private bool _wantPages = true;
    private int _roundPasses;
    private long? _settledSignature;
    private bool _pagesOwed;
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

    /// <summary>How many times a refresh laid the whole rest of the document out on the calling thread (the save path only; telling a layout of a result lays out through the page in view on the timer path, tests).</summary>
    internal int SyncLayoutCompletions { get; private set; }

    /// <summary>How many first passes skipped the page pass because nothing could have moved a page (tests).</summary>
    internal int PagePassesSkipped { get; private set; }

    /// <summary>How many saves in the writing view made a pages layout of their own to read the page results (tests).</summary>
    internal int SettlePageLayouts { get; private set; }

    /// <summary>Test seam: called after each step of laying out the pages of a save in the writing view, with that layout.</summary>
    internal Action<DocumentLayout>? SettlePageStep { get; set; }

    /// <summary>Test seam: called under the lease after the results were told to a layout, while no background layout runs.</summary>
    internal Action<DocumentLayout>? AfterApply { get; set; }

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
        _roundPasses = 0;
        _timer.Restart(Delay);
    }

    /// <summary>
    /// The pages view is followed again: when a pass that ran without pages (in the writing view) left page results owed, the timer is started so that they are brought current after the delay. Nothing is read or refreshed here.
    /// </summary>
    public void PagesBack()
    {
        if (!_disposed && _pagesOwed)
        {
            _timer.Restart(Delay);
        }
    }

    /// <summary>
    /// Brings the results up to date now, before a save reads the model: the same passes as the timer's, run synchronously (the task is complete when this returns; it is a task for the passes that may yield later). A refresh that is blocked
    /// (a prefix document, a stale pages layout, an IME composition, a faulted session) is skipped, and so is one when the token is cancelled or the runner disposed; nothing is thrown for any of them.
    /// <para>
    /// The writing view has no pages: a save there that owes page results makes a pages layout of its own under the lease, lays it out to its end and reads them from it, within the same <see cref="MaxPasses"/> passes. The view and
    /// its layouts are left as they are, and results that changed leave the tab's pages layout stale, so a switch back makes it anew. A save that owes no page result lays nothing out, and the timer never makes such a layout.
    /// </para>
    /// </summary>
    public Task SettleAsync(CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested || _running)
        {
            return Task.CompletedTask;
        }

        try
        {
            ClaimOwedPages(forSettle: true);
            if (_pending && Blocked(forSettle: true) == Hold.None)
            {
                _timer.Stop();
                RunPasses(forSettle: true, token);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A save must never fail or wait because the refresh did: the failure is counted and the timer looks again later.
            Failed();
        }

        return Task.CompletedTask;
    }

    /// <summary>Runs the tick of the timer now, as a pause of the user would (the measurement runs): nothing happens when nothing is pending or the refresh is held back.</summary>
    internal void Flush()
    {
        if (!_disposed)
        {
            _timer.Stop();
            OnTick();
        }
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
        if (_disposed || _running)
        {
            return;
        }

        try
        {
            ClaimOwedPages(forSettle: false);
            if (!_pending)
            {
                return;
            }

            switch (Blocked(forSettle: false))
            {
                case Hold.Never:
                    _pending = false;
                    _touched.Clear();
                    _settledSignature = null;
                    break;
                case Hold.Later:
                    _timer.Restart(Delay);
                    break;
                default:
                    RunPasses(forSettle: false, _tab.Token);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The timer runs on the dispatcher and Write has no crash guard here: nothing may escape.
            Failed();
        }
    }

    // Page results owed since a pass without pages are work again once there are pages (for the tick), and for the settle of a save in any view (in the writing view it makes pages of its own). The touched blocks stay as they are: that pass
    // dropped the signature of the settled page results, so the next one reads every page result. They stay owed until a pass with pages ran.
    private void ClaimOwedPages(bool forSettle)
    {
        if (_pagesOwed && !_tab.IsDisposed && (forSettle || _host.HasPages))
        {
            _pending = true;
        }
    }

    // A failure outside the passes (the scan, the checks): counted, and looked at again after the delay, a few times in a row.
    private void Failed()
    {
        Failures++;
        if (!_disposed && ++_consecutiveFailures <= MaxConsecutiveFailures)
        {
            _pending = true;
            _timer.Restart(Delay);
        }
    }

    // Why the refresh cannot run now: for good (nothing to follow any more), or for a while.
    private Hold Blocked(bool forSettle)
    {
        if (_tab.IsDisposed || !_tab.RefreshesFields || _tab.Runner.IsFaulted || _tab.Editor?.BlockedReason == EditBlockedReason.Faulted)
        {
            return Hold.Never;
        }

        // A session that cannot edit now (a save runs, the model is still opening) holds the refresh, so a structural result never goes to a history that is not the session's while one exists. The bare editor made by EditorFor is still
        // used when the tab has no session at all; that is safe because its history is empty, so an attach there ends as NothingToProtect and the change stands.
        if (_tab.Docx.IsPrefix || !_tab.Runner.FullModelReady || _tab.Editor?.IsSaving == true || _tab.Editor is { CanEdit: false })
        {
            return Hold.Later;
        }

        // The strips of the writing view may not be made yet when a save settles: it needs no layout of the view (it lays out pages of its own for the page results), and without one there is no composition to wait for.
        var layout = _host.Layout;
        if (layout is null ? !(forSettle && _tab.Editor is { ActiveMode: LayoutMode.Flow }) : layout.PreeditText is not null)
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

        // The first look at the whole body, and any look at a document that has page results (a table of contents, a PAGEREF), waits for the layout to be complete, so that nothing is asked of a half-built layout and the page loop (which needs
        // it) can follow; a save does not wait (it completes the layout itself).
        return !forSettle && (_all || _wantPages) && layout is { IsComplete: false } ? Hold.Later : Hold.None;
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

    private void RunPasses(bool forSettle, CancellationToken token)
    {
        // The signature of the settled page results is only kept by a pass that leaves them current (below): whatever else takes the touched blocks from here on (a pass without pages, as in the writing view, a document without fields, a failure, a
        // cancel) loses it, so the next burst looks at the page results again.
        var settledBefore = _settledSignature;
        _settledSignature = null;
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

            // The pages of a save in the writing view (see LayOutOwnPages); null in every other refresh. A failure of that layout is counted and gives up on it: it is no layout of the document's, so nothing is faulted.
            DocumentLayout? own = null;
            var ownFailed = false;
            bool OwesOwnPages() => forSettle && own is null && !ownFailed && _pagesOwed && !_host.HasPages;

            void WithOwnPages(Func<DocumentLayout?> make)
            {
                try
                {
                    own = make();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Failures++;
                    ownFailed = true;
                    own = null;
                }

                _host.SettlePages = own;
            }

            // Lays out the rest of the pages (the lease holder may; the background layout is paused) so that the next pass reads final page numbers. A fault of the layout is a fault of the edit path, like in ApplyToLayout.
            void CompleteLayout()
            {
                if (_host.Layout is { Mode: LayoutMode.Pages, IsComplete: false } layout)
                {
                    SyncLayoutCompletions++;
                    layoutFailed = true;
                    layout.LayOutThrough(int.MaxValue);
                    layoutFailed = false;
                }
            }

            // A save writes the numbers the page shows: the layout is completed first when the document has page results (or may have: nothing is known before the first pass).
            if (forSettle && _wantPages && _host.HasPages)
            {
                CompleteLayout();
            }

            // A refresh that waited for the background layout goes on counting its passes (the bound is three per burst, whichever path); a save starts its own count.
            var first = forSettle ? 0 : _roundPasses;
            _roundPasses = 0;
            for (var pass = first; pass < MaxPasses && !token.IsCancellationRequested && !_tab.IsDisposed; pass++)
            {
                // A save in the writing view that owes page results reads them from pages of its own, made before the pass that reads them.
                if (OwesOwnPages())
                {
                    WithOwnPages(() => LayOutOwnPages(token));
                    if (token.IsCancellationRequested || _tab.IsDisposed)
                    {
                        break;
                    }
                }

                // Page results need every block and a complete layout: such a pass looks at the whole body; any other pass at the touched blocks of the first pass. The first pass of a burst of plain edits skips the page pass when the layout's page
                // table is the one the last settled page pass saw and no touched block carries something a page result points at (the editor decides: a skipped pass gives what a whole pass gives).
                var complete = _wantPages && _host.IsComplete;
                var skip = complete && pass == 0 && own is null && touched is not null && editor.PagePassCanBeSkipped(_host, touched, settledBefore);
                var pagesOn = complete && !skip;
                var scope = pagesOn ? RefreshScope.WithPages : skip ? RefreshScope.OrderBasedPagesCurrent : RefreshScope.OrderBased;
                var blocks = pass == 0 && !pagesOn ? touched : null;
                if (skip)
                {
                    PagePassesSkipped++;
                }

                try
                {
                    last = Refresher is { } seam ? seam(editor, _host, scope, blocks) : editor.RefreshFields(_host, scope, blocks);
                }
                catch (FieldRefreshException partial) when (partial.Content.Count > 0)
                {
                    // The walk wrote part of the results before it failed: the layout is told of them now (under the lease), then the failure is handled like any other and the refresh is tried again.
                    content.AddRange(partial.Content);
                    layoutFailed = true;
                    ApplyToLayout(partial.Content, forSettle);
                    layoutFailed = false;
                    if (partial.Structural is { } done)
                    {
                        // The paragraphs of a table of contents were replaced before the failure: the history must carry that change even though the refresh is tried again. A failure of the attach must not hide the
                        // first failure (the history clears itself when it cannot place a record), so it is swallowed here and the original is rethrown below.
                        try
                        {
                            HandleStructural(editor, done);
                        }
                        catch (Exception attach) when (attach is not OutOfMemoryException)
                        {
                            Failures++;
                        }
                    }

                    throw;
                }

                Passes++;
                if (blocks is null || last.PageFields > 0)
                {
                    // A pass over the whole body saw every page field; one over some blocks can only find new ones.
                    _wantPages = last.PageFields > 0;
                }

                // A pass without pages (the writing view) leaves the page results the document has, or may have, owed; a pass with pages pays them.
                if (!_host.HasPages)
                {
                    _pagesOwed = _wantPages;
                }
                else if (pagesOn)
                {
                    _pagesOwed = false;
                }

                if (last.Content.Count > 0)
                {
                    content.AddRange(last.Content);
                    layoutFailed = true;
                    ApplyToLayout(last.Content, forSettle);
                    layoutFailed = false;
                    if (own is { } pages)
                    {
                        // The save's own pages follow the results too, so the next pass reads the numbers of the document as it is now. When they cannot (a cancel, a failure) the results just written were never checked and stay owed.
                        WithOwnPages(() => FollowOnOwnPages(pages, last.Content, token));
                        _pagesOwed |= own is null;
                    }
                }

                if (last.Structural is { } record)
                {
                    HandleStructural(editor, record);
                }

                if (last.StructuralDeferred)
                {
                    // A table of contents changes its paragraph count while a redo is pending: the refresh left it as it was and looks again, from the whole body, once the redo stack is empty.
                    WaitForRedo();
                }

                // The page table the page results are current for: the one a settled page pass read, or the one a settled skip was checked against. Any other pass (without pages, or one that may still change a page result) drops it.
                _settledSignature = !last.Settled ? null : pagesOn ? _host.PageSignature : skip ? settledBefore : null;

                // A save in the writing view that now owes page results goes on: the next pass makes pages of its own and reads them. A table of contents that waits for a redo does not hold the numbers of the other results back.
                if (OwesOwnPages() && pass < MaxPasses - 1 && (!_pending || _waitForRedo))
                {
                    continue;
                }

                if (_pending)
                {
                    // An edit arrived during the passes: they end here and start over with the new blocks when the timer fires.
                    break;
                }

                if (last.Settled || !_host.HasPages)
                {
                    // Nothing page-based may still change, or there are no pages to follow (the timer in the writing view): the stored page results stay.
                    break;
                }

                if (pass == MaxPasses - 1)
                {
                    Unsettled++;
                    break;
                }

                // Write, apply, repeat: the numbers are read again from a complete layout. A save lays the rest out here and now. The timer path does not lay the document out to its end on the UI thread: telling the layout of the results laid out through
                // the page in view (as an edit does), the background layout goes on once the lease is released, and the next tick that finds it complete runs the next pass.
                if (!_host.IsComplete)
                {
                    if (!forSettle)
                    {
                        _roundPasses = pass + 1;
                        _all = true;
                        _pending = true;
                        _timer.Restart(Delay);
                        break;
                    }

                    CompleteLayout();
                }
            }

            if (wholeBody && last is not null)
            {
                NotUpdated = last.NotUpdated;
                reported = true;
            }

            if (last is null && !_tab.IsDisposed)
            {
                // Cancelled before the first pass: the blocks of this try stay to be looked at.
                _touched.InsertRange(0, snapshot);
                _all |= wholeBody;
                _pending = true;
                _timer.Restart(Delay);
            }

            _consecutiveFailures = 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failures++;
            _roundPasses = 0;
            _settledSignature = null;
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
            _host.SettlePages = null;
            _running = false;
        }

        // What the owners of the document do with the news is theirs: a subscriber that throws is counted and does not stop the others, the save or the timer.
        if (content.Count > 0 && !_disposed && !_tab.IsDisposed)
        {
            Guarded(() => _tab.Editor?.FollowDerivedContent(content));
            Guarded(_tab.NotifyFieldsRefreshed);
        }

        if (reported && !_disposed && !_tab.IsDisposed)
        {
            Guarded(() => _tab.ReportNotUpdated(NotUpdated));
        }
    }

    // The pages of a save in the writing view: a layout of its own over the model, made under the lease the caller holds and laid out to its end. Null when the save was cancelled or the tab closed on the way.
    private DocumentLayout? LayOutOwnPages(CancellationToken token)
    {
        var layout = _tab.Runner.CreateDetachedPageLayout();
        SettlePageLayouts++;
        return Complete(layout, token) ? layout : null;
    }

    // The results of a pass told to the save's own pages, which are then laid out to their end again.
    private DocumentLayout? FollowOnOwnPages(DocumentLayout layout, IReadOnlyList<BlockChange> content, CancellationToken token)
    {
        foreach (var change in content)
        {
            layout.ApplyEdit(change, Math.Max(0, layout.LaidOutPages - 1));
        }

        return Complete(layout, token) ? layout : null;
    }

    // One page at a time, so a cancelled save or a closed tab ends the work at a page end.
    private bool Complete(DocumentLayout layout, CancellationToken token)
    {
        while (!layout.IsComplete)
        {
            if (token.IsCancellationRequested || _disposed || _tab.IsDisposed)
            {
                return false;
            }

            layout.LayOutThrough(layout.LaidOutPages);
            SettlePageStep?.Invoke(layout);
        }

        return !token.IsCancellationRequested && !_disposed && !_tab.IsDisposed;
    }

    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failures++;
        }
    }

    // False when there is nothing to look at: a document with no field at all (checked once; later only the blocks an edit touched are scanned for one).
    private bool ScanForFields(WordDocument model)
    {
        if (!_fieldsKnown)
        {
            _fieldsKnown = true;
            _hasFields = _tab.AnyFields();
        }
        else if (!_hasFields && (_all ? _tab.AnyFields() : _touched.Any(TouchesFields)))
        {
            // A field that arrived after the first look: found in the touched blocks, or, when the whole body is asked for (a style or section change drops the list of touched blocks), by a new scan.
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

    // The new results are told to the layouts that exist: the one the session follows (the other is dropped and made again on its next use), or, while there is no session, the pages layout and the strips layout alike. A save keeps every page that was
    // laid out; the timer path lays out through the page in view only and leaves the rest to the background.
    private void ApplyToLayout(IReadOnlyList<BlockChange> content, bool forSettle)
    {
        var session = _tab.Editor;
        var runner = _tab.Runner;
        if (session is not null)
        {
            if (session.ActiveLayout is { } active)
            {
                Apply(active, content, forSettle);
            }

            session.DropInactiveLayout();
            return;
        }

        if (!runner.PageLayoutIsStale)
        {
            Apply(runner.PageLayout, content, forSettle);
        }

        if (runner.FlowLayout is { } flow)
        {
            Apply(flow, content, forSettle);
        }
    }

    private void Apply(DocumentLayout layout, IReadOnlyList<BlockChange> content, bool forSettle)
    {
        var inView = forSettle ? 0 : _tab.PageInView?.Invoke(layout.Mode) ?? 0;
        foreach (var change in content)
        {
            layout.ApplyEdit(change, forSettle ? Math.Max(0, layout.LaidOutPages - 1) : inView);
        }

        AfterApply?.Invoke(layout);
    }

    // A structural result (the paragraph count of a table of contents changed) belongs to the newest history step of the session's editor. The refresh itself already left a table of contents alone while a redo was pending
    // (FieldRefreshResult.StructuralDeferred), so MustWait here is only the double check of a result made some other way.
    private void HandleStructural(TextEditor editor, DerivedRecord record)
    {
        switch (editor.History.AttachDerived(record))
        {
            case AttachOutcome.Attached:
                StructuralAttached++;
                break;
            case AttachOutcome.MustWait:
                WaitForRedo();
                break;
            default:
                break;
        }
    }

    // Look again from the whole body once the redo stack is empty.
    private void WaitForRedo()
    {
        StructuralWaits++;
        _waitForRedo = true;
        _all = true;
        _pending = true;
        _timer.Restart(Delay);
    }
}
