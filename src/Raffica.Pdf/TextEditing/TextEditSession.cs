using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Core.TextEdit;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.TextEditing;

/// <summary>What the hint bar says about a text typed into the field: the decision of <see cref="TextEditCheck"/> and what the new line may run into.</summary>
/// <param name="Text">The text the decision is about.</param>
/// <param name="WholeRun">Whether the whole line in the replacement font was asked for.</param>
/// <param name="Decision">What <see cref="TextEditCheck.Evaluate"/> decided (the one call the commit makes too).</param>
/// <param name="Overflow">What the new line may run into (an estimate from the number of characters; never blocks).</param>
internal sealed record TextEditPreview(string Text, bool WholeRun, TextEditDecision Decision, TextEditOverflow Overflow)
{
    /// <summary>Whether the switch "Whole line in replacement font" is offered: every character is in the replacement font and the text is a change that is written.</summary>
    public bool WholeRunOffered => Decision.WholeRunInGeistPossible && Decision.Outcome is TextEditOutcome.KeepFont or TextEditOutcome.SubstituteMissing or TextEditOutcome.WholeRunInGeist;

    /// <summary>Whether the text may be written as it is.</summary>
    public bool CanCommit => Decision.Outcome != TextEditOutcome.Rejected;
}

/// <summary>How a commit of the draft ended.</summary>
internal enum TextEditCommitKind
{
    /// <summary>There was no draft.</summary>
    NoDraft,

    /// <summary>The text is the old one; the draft is closed and nothing was written.</summary>
    Unchanged,

    /// <summary>The line was written (one history step); the draft is closed.</summary>
    Applied,

    /// <summary>The text is refused as a whole (see <see cref="TextEditCommitResult.Decision"/>); the draft stays open.</summary>
    Refused,

    /// <summary>The page changed since the line was read; nothing was written and the draft stays open.</summary>
    Stale,

    /// <summary>The line cannot be edited (see <see cref="TextEditCommitResult.Lock"/>); nothing was written and the draft stays open.</summary>
    Locked,

    /// <summary>Writing failed (the page may have changed, the draft is closed) or could not be tried (nothing changed, the draft stays open).</summary>
    Failed,

    /// <summary>The history has no room for another step; nothing was written and the draft stays open.</summary>
    HistoryFull,
}

/// <summary>What <see cref="TextEditSession.CommitAsync"/> did.</summary>
/// <param name="Kind">How it ended.</param>
/// <param name="Decision">The decision for the text (refusals and writes), or <see langword="null"/>.</param>
/// <param name="Lock">Why the line cannot be edited (<see cref="TextEditCommitKind.Locked"/>), or <see langword="null"/>.</param>
/// <param name="Note">What the user is told, in the UI language; never Core's English sentence and never the text of the line.</param>
internal sealed record TextEditCommitResult(TextEditCommitKind Kind, TextEditDecision? Decision, TextEditLockReason? Lock, string Note);

/// <summary>
/// The Text-edit session of a tab: made on first use, disposed with the tab. While the mode is on it keeps the runs (lines) of the pages that are in
/// view (read on demand, one page per job, never at start), the focused run and at most one draft: the new text of one run. The draft is an
/// unresolved edit of the annotation session (<see cref="AnnotationSession.BeginUnresolved"/>) from the first change of the text until it is written or
/// dropped, so the tab counts as changed and a close asks; undo and redo end the draft instead of touching the history; a flush (save, print, close)
/// writes a valid draft and parks a refused one. The text is written as one history step by <see cref="AnnotationEditor.ApplyTextEditAsync"/>; a run
/// that no longer matches the page is refused (stale), never written wrong. The operations belong to the UI thread.
/// </summary>
internal sealed class TextEditSession : IDisposable
{
    // The hint bar waits this long after the last key before it asks for the decision.
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(120);

    private readonly Dictionary<int, IReadOnlyList<TextRun>> _runs = [];
    private readonly Dictionary<int, Task> _loading = [];
    private readonly HashSet<int> _failed = [];
    private int _generation;
    private (int Page, int Index)? _focus;
    private Draft? _draft;
    private TextEditPreview? _preview;
    private string? _note;
    private CancellationTokenSource? _previewCancel;
    private Task<TextEditCommitResult>? _commit;
    private bool _wholeRunOffered;
    private bool _committing;
    private IDisposable? _discardRegistration;
    private bool _oldTextNote;
    private bool _active;
    private bool _disposed;

    public TextEditSession(PdfTab tab, SynchronizationContext ui)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(ui);
        Tab = tab;
        Ui = ui;
    }

    /// <summary>Raised on the UI thread after the runs, the focus, the draft, the note or the mode changed.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread when the runs that were read are dropped (an edit, an undo, an OCR run): whoever shows pages reads the visible ones again.</summary>
    public event Action? RunsInvalidated;

    /// <summary>Raised on the UI thread just before the mode ends, so whoever shows or reads the runs lets go first.</summary>
    public event Action? Ending;

    /// <summary>The tab the session belongs to.</summary>
    public PdfTab Tab { get; }

    /// <summary>The UI context the session reports on.</summary>
    public SynchronizationContext Ui { get; }

    /// <summary>Whether the mode is on.</summary>
    public bool IsActive => _active;

    /// <summary>How long a key waits for the next one before the decision for the hint bar is asked (tests set it to zero).</summary>
    public TimeSpan Debounce { get; set; } = DefaultDebounce;

    /// <summary>The clock of the debounce (tests move it).</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>How the runs of a page are read (tests count the reads); the default asks the document.</summary>
    internal Func<PdfDocument, int, CancellationToken, Task<IReadOnlyList<TextRun>>> ReadRuns { get; set; } = static (document, page, token) => document.GetTextRunsAsync(page, token);

    /// <summary>Awaited by a commit after its decision and before it writes (tests hold it to type, press Esc or leave the mode while the write waits).</summary>
    internal Func<Task>? BeforeWrite { get; set; }

    /// <summary>Whether a commit runs now (the field is read-only meanwhile).</summary>
    public bool IsCommitting => _committing;

    /// <summary>
    /// Whether the switch for the whole line is on offer: the last decision offered it, and it stays on offer until the next decision says otherwise (no
    /// flicker while a key waits for its decision).
    /// </summary>
    public bool WholeRunOffered => _wholeRunOffered;

    /// <summary>How many page reads were started since the session was made.</summary>
    public int RunReads { get; private set; }

    /// <summary>Whether a draft is open (the field is over a run).</summary>
    public bool HasDraft => _draft is not null;

    /// <summary>The run the draft is about, or <see langword="null"/>.</summary>
    public TextRun? DraftRun => _draft?.Run;

    /// <summary>The text of the draft, or <see cref="string.Empty"/> while there is none.</summary>
    public string DraftText => _draft?.Text ?? string.Empty;

    /// <summary>Whether the draft asks for the whole line in the replacement font (never on by itself).</summary>
    public bool WholeRun => _draft?.WholeRun == true;

    /// <summary>The last decision for the text of the draft, or <see langword="null"/> before it was asked or after the text changed.</summary>
    public TextEditPreview? Preview => _preview;

    /// <summary>What the hint bar says now besides its fixed line (the live note, the reason of a locked line, the result of a commit), or <see langword="null"/>.</summary>
    public string? Note => _note;

    /// <summary>Whether the note about the old text in earlier versions is on (after the first save of edited text of this tab).</summary>
    public bool OldTextNoteShown => _oldTextNote;

    /// <summary>Whether a line has the focus (also while its page is being read again).</summary>
    public bool HasFocus => _focus is not null;

    /// <summary>The run that has the focus, or <see langword="null"/>.</summary>
    public TextRun? Focused => _focus is { } key && _runs.TryGetValue(key.Page, out var runs) ? runs.FirstOrDefault(r => r.ObjectIndex == key.Index) : null;

    /// <summary>Starts the mode. Reads nothing: the runs come with <see cref="LoadPageAsync"/> for the pages in view. Does nothing when the mode is on.</summary>
    public void Enter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_active)
        {
            return;
        }

        if (Tab.Session.Document is null)
        {
            throw new InvalidOperationException("No document is open.");
        }

        _active = true;
        Tab.Annotations.FlushRequested += OnFlushRequested;
        Tab.Annotations.Editor.Changed += OnEditorChanged;
        _discardRegistration = Tab.Annotations.AddDiscardHandler(DiscardForHistory);
        Changed?.Invoke();
    }

    /// <summary>Ends the mode: the runs and the focus are dropped, an open draft is dropped with its handle (the window writes or asks before it comes here).</summary>
    public void End()
    {
        if (!_active)
        {
            return;
        }

        Ending?.Invoke();
        Stop();
        Changed?.Invoke();
    }

    /// <summary>Reads the runs of a page (once; one job on the PDFium thread, off the UI thread). A page that cannot be read is remembered and not read again.</summary>
    /// <param name="page">The 0-based page.</param>
    public async Task LoadPageAsync(int page)
    {
        // A read that was started before the runs were dropped again ends without storing anything (its generation is stale), and a caller that
        // waited for it would find no runs: it asks again for the current generation (bounded; the pages in view are read again by the window too).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!_active || _disposed || page < 0 || _runs.ContainsKey(page) || _failed.Contains(page))
            {
                return;
            }

            if (!_loading.TryGetValue(page, out var load))
            {
                load = LoadCoreAsync(page, _generation);
                _loading[page] = load;
            }

            await load;
        }
    }

    /// <summary>The runs of a page that were read, in the order of the page; empty before the read and for a page that failed.</summary>
    public IReadOnlyList<TextRun> RunsOf(int page) => _runs.TryGetValue(page, out var runs) ? runs : [];

    /// <summary>Whether the runs of a page were read.</summary>
    public bool IsLoaded(int page) => _runs.ContainsKey(page);

    /// <summary>Whether the page could not be read (it is not read again until the document changes).</summary>
    public bool IsFailed(int page) => _failed.Contains(page);

    /// <summary>Drops the runs of the pages outside the range (the pages in view are the only ones kept; the page of the draft stays).</summary>
    public void Trim(int first, int last)
    {
        var keep = _draft?.Run.Page;
        foreach (var page in _runs.Keys.Where(p => (p < first || p > last) && p != keep).ToList())
        {
            _runs.Remove(page);
        }

        foreach (var page in _failed.Where(p => p < first || p > last).ToList())
        {
            _failed.Remove(page);
        }
    }

    /// <summary>The run under a position of a page in points (the smallest one when boxes overlap; a point of tolerance around each), or <see langword="null"/>.</summary>
    public TextRun? RunAt(int page, double x, double y)
    {
        TextRun? best = null;
        var bestArea = double.PositiveInfinity;
        foreach (var run in RunsOf(page))
        {
            var b = run.Bounds;
            if (x >= b.Left - 1 && x <= b.Right + 1 && y >= b.Top - 1 && y <= b.Bottom + 1)
            {
                var area = Math.Max(0, b.Width) * Math.Max(0, b.Height);
                if (area < bestArea)
                {
                    bestArea = area;
                    best = run;
                }
            }
        }

        return best;
    }

    /// <summary>Gives a run the focus (or takes it away with <see langword="null"/>); the hint bar then names the reason of a locked run. A draft keeps the field where it is.</summary>
    public void Focus(TextRun? run)
    {
        var key = run is null ? ((int, int)?)null : (run.Page, run.ObjectIndex);
        if (_focus == key && (run is null || !run.IsLocked || _draft is not null))
        {
            return;
        }

        _focus = key;
        if (_draft is null)
        {
            _note = run is { Lock: { } reason } ? TextEditWording.Lock(reason) : null;
            _preview = null;
        }

        Changed?.Invoke();
    }

    /// <summary>Moves the focus to the next or previous run in reading order over the pages that were read; false at either end (the key is then not used).</summary>
    /// <param name="forward">Whether to go to the next run.</param>
    public bool MoveFocus(bool forward)
    {
        var order = ReadingOrder();
        if (order.Count == 0)
        {
            return false;
        }

        var current = Focused;
        int at;
        if (current is null)
        {
            at = forward ? 0 : order.Count - 1;
        }
        else
        {
            var index = order.FindIndex(r => r.Page == current.Page && r.ObjectIndex == current.ObjectIndex);
            at = index < 0 ? (forward ? 0 : order.Count - 1) : index + (forward ? 1 : -1);
        }

        if (at < 0 || at >= order.Count)
        {
            return false;
        }

        Focus(order[at]);
        return true;
    }

    /// <summary>Opens the draft on a run (the field opens over it). Refused for a locked run, with a draft for another run or while the mode is off.</summary>
    /// <param name="run">The run as the session read it.</param>
    /// <returns>Whether a draft is open on the run now.</returns>
    public bool OpenDraft(TextRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!_active || _disposed)
        {
            return false;
        }

        if (_draft is { } open)
        {
            return open.Run.Page == run.Page && open.Run.ObjectIndex == run.ObjectIndex;
        }

        if (run.IsLocked)
        {
            _note = TextEditWording.Lock(run.Lock!.Value);
            Changed?.Invoke();
            return false;
        }

        _focus = (run.Page, run.ObjectIndex);
        _draft = new Draft(run);
        _preview = null;
        _wholeRunOffered = false;
        _note = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Sets the text of the draft (what the field shows). The text that differs from the line makes the draft an unresolved edit of the tab; the decision is asked after the debounce.</summary>
    /// <param name="text">The text.</param>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_draft is not { } draft || string.Equals(draft.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        draft.Text = text;
        SyncUnresolved(draft);
        _preview = null;
        _note = null;
        Changed?.Invoke();
        _ = RefreshPreviewAsync(draft, text, draft.WholeRun);
    }

    /// <summary>Turns the switch "Whole line in replacement font" on or off. It can be turned on only when the last decision offers it, and never is on by itself.</summary>
    /// <param name="value">Whether the whole line goes to the replacement font.</param>
    public void SetWholeRun(bool value)
    {
        if (_draft is not { } draft || draft.WholeRun == value || (value && !_wholeRunOffered))
        {
            return;
        }

        draft.WholeRun = value;
        _preview = null;
        Changed?.Invoke();
        _ = RefreshPreviewAsync(draft, draft.Text, value);
    }

    /// <summary>
    /// What the hint bar says about a text: the decision of <see cref="TextEditCheck.Evaluate"/> and what the new line may run into. Waits for the debounce
    /// first; a newer call or a change of the draft cancels it. The result is the session's <see cref="Preview"/> when it is still the latest.
    /// </summary>
    /// <param name="newText">The text.</param>
    /// <param name="wholeRunInReplacementFont">Whether the whole line in the replacement font is asked for.</param>
    /// <exception cref="InvalidOperationException">No draft is open.</exception>
    /// <exception cref="OperationCanceledException">A newer call came first.</exception>
    public async Task<TextEditPreview> PreviewAsync(string newText, bool wholeRunInReplacementFont)
    {
        ArgumentNullException.ThrowIfNull(newText);
        var draft = _draft ?? throw new InvalidOperationException("No draft is open.");
        _previewCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _previewCancel = cancel;
        try
        {
            if (Debounce > TimeSpan.Zero)
            {
                await Task.Delay(Debounce, Time, cancel.Token);
            }

            var (decision, whole) = await DecideAsync(draft.Run, newText, wholeRunInReplacementFont, cancel.Token);
            var overflow = await OverflowAsync(draft, newText, decision, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            var preview = new TextEditPreview(newText, whole, decision, overflow);
            if (ReferenceEquals(_draft, draft) && ReferenceEquals(_previewCancel, cancel) && string.Equals(draft.Text, newText, StringComparison.Ordinal))
            {
                if (whole != draft.WholeRun)
                {
                    draft.WholeRun = whole;
                }

                _preview = preview;
                _wholeRunOffered = preview.WholeRunOffered;
                _note = TextEditWording.Note(preview);
                Changed?.Invoke();
            }

            return preview;
        }
        finally
        {
            if (ReferenceEquals(_previewCancel, cancel))
            {
                _previewCancel = null;
            }

            cancel.Dispose();
        }
    }

    /// <summary>
    /// Writes the draft as one history step. The decision is the same <see cref="TextEditCheck.Evaluate"/> the hint bar shows; a refused text, a stale or
    /// locked run, a full history and a failure end as a note with its reason and no change (a draft that cannot be written stays open and counts as
    /// unsaved). A second call while one runs gets the result of the first.
    /// </summary>
    public Task<TextEditCommitResult> CommitAsync()
    {
        if (_commit is { IsCompleted: false } running)
        {
            return running;
        }

        if (_draft is null)
        {
            return Task.FromResult(new TextEditCommitResult(TextEditCommitKind.NoDraft, null, null, string.Empty));
        }

        // The handle that makes a flush wait is taken before the first await: a save that asked for it right now waits for the write.
        var pending = Tab.Annotations.BeginPending();
        _committing = true;
        _commit = CommitCoreAsync(_draft, pending);
        Changed?.Invoke();
        return _commit;
    }

    /// <summary>Drops the draft without writing it (Esc, undo and redo). The note goes with it.</summary>
    public void Discard()
    {
        if (_draft is not null)
        {
            DropDraft();
            _note = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Tells the session the file was saved with an edited text: the note about the old text in earlier versions is shown (once per tab; the caller decides).</summary>
    public void ShowOldTextNote()
    {
        if (!_oldTextNote)
        {
            _oldTextNote = true;
            Changed?.Invoke();
        }
    }

    /// <summary>Takes the note about the old text away.</summary>
    public void HideOldTextNote()
    {
        if (_oldTextNote)
        {
            _oldTextNote = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Drops every read run; whoever shows pages reads the visible ones again (<see cref="RunsInvalidated"/>).</summary>
    public void InvalidateRuns()
    {
        if (!_active || _disposed)
        {
            return;
        }

        _generation++;
        _runs.Clear();
        _loading.Clear();
        _failed.Clear();
        RunsInvalidated?.Invoke();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_active)
        {
            Stop();
        }
    }

    // Everything the mode holds goes; the draft's handles are released.
    private void Stop()
    {
        _active = false;
        _generation++;
        _previewCancel?.Cancel();
        DropDraft();
        _runs.Clear();
        _loading.Clear();
        _failed.Clear();
        _focus = null;
        _note = null;
        _preview = null;
        _oldTextNote = false;
        _discardRegistration?.Dispose();
        _discardRegistration = null;
        if (Tab.AnnotationsIfCreated is { } annotations)
        {
            annotations.FlushRequested -= OnFlushRequested;
            annotations.Editor.Changed -= OnEditorChanged;
        }
    }

    private async Task LoadCoreAsync(int page, int generation)
    {
        try
        {
            if (Tab.Session.Document is not { } document)
            {
                return;
            }

            RunReads++;
            var runs = await ReadRuns(document, page, CancellationToken.None);
            if (generation == _generation && _active)
            {
                _runs[page] = runs;
                Changed?.Invoke();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A damaged page (or a document that went away) is remembered and not read again; the hint bar says so once.
            if (generation == _generation && _active)
            {
                _failed.Add(page);
                _note = TextEditWording.PageUnreadable;
                Changed?.Invoke();
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _loading.Remove(page);
            }
        }
    }

    // Reading order of the pages that were read: page, then top to bottom, then left to right.
    private List<TextRun> ReadingOrder() =>
        [.. _runs.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value.OrderBy(r => Math.Round(r.Bounds.Top, 0)).ThenBy(r => r.Bounds.Left))];

    private void SyncUnresolved(Draft draft)
    {
        var changed = !string.Equals(draft.Text, draft.Run.Text, StringComparison.Ordinal);
        if (changed && draft.Unresolved is null)
        {
            draft.Unresolved = Tab.Annotations.BeginUnresolved();
        }
        else if (!changed && draft.Unresolved is not null)
        {
            draft.Unresolved.Dispose();
            draft.Unresolved = null;
        }
    }

    private void DropDraft()
    {
        _previewCancel?.Cancel();
        _preview = null;
        _wholeRunOffered = false;
        if (_draft is { } draft)
        {
            _draft = null;
            draft.Unresolved?.Dispose();
            draft.Unresolved = null;
        }
    }

    private async Task RefreshPreviewAsync(Draft draft, string text, bool whole)
    {
        try
        {
            await PreviewAsync(text, whole);
        }
        catch (OperationCanceledException)
        {
            // A newer key (or the end of the draft) came first.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (ReferenceEquals(_draft, draft) && string.Equals(draft.Text, text, StringComparison.Ordinal))
            {
                _note = ex is TextRunStaleException ? TextEditWording.Stale : TextEditWording.Failed;
                Changed?.Invoke();
            }
        }
    }

    // The one decision: the hint bar and the commit both come here, so what the bar said is what the commit does.
    private async Task<(TextEditDecision Decision, bool Whole)> DecideAsync(TextRun run, string text, bool whole, CancellationToken token)
    {
        var document = Tab.Session.Document ?? throw new InvalidOperationException("No document is open.");
        if (HasLoneSurrogate(text))
        {
            return (new TextEditDecision(TextEditOutcome.Rejected, [], [], ["�"], false, TextEditRejection.UnsupportedCharacters), false);
        }

        var coverage = await CoverageOfAsync(document, run, text, token);
        var decision = TextEditCheck.Evaluate(run.Text, text, coverage, whole ? TextEditMode.WholeRunInGeist : TextEditMode.KeepFont);

        // The switch cannot stay on for a text the replacement font cannot carry: it falls back to the font of the line.
        if (whole && decision.Outcome == TextEditOutcome.Rejected && !decision.WholeRunInGeistPossible)
        {
            whole = false;
            decision = TextEditCheck.Evaluate(run.Text, text, coverage, TextEditMode.KeepFont);
        }

        return (decision, whole);
    }

    private static async Task<IFontCoverage> CoverageOfAsync(PdfDocument document, TextRun run, string text, CancellationToken token)
    {
        // Only the characters that are new need asking (the rule of Core); a text that is too long is refused before the font is looked at.
        var known = run.Text.EnumerateRunes().Select(static r => r.Value).ToHashSet();
        var wanted = TextEditCheck.Elements(text).Count > TextEditLimits.MaxCharsPerRun
            ? []
            : text.EnumerateRunes().Select(static r => r.Value).Where(c => !known.Contains(c)).Distinct().ToArray();
        return wanted.Length == 0 ? new CodePointSet([]) : await document.GetFontCoverageAsync(run, wanted, token);
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    // What the longer line may run into: the width grows with the number of characters (an estimate, so the note says "may"); nothing is asked for a shorter text.
    private async Task<TextEditOverflow> OverflowAsync(Draft draft, string text, TextEditDecision decision, CancellationToken token)
    {
        if (decision.Outcome is not (TextEditOutcome.KeepFont or TextEditOutcome.SubstituteMissing or TextEditOutcome.WholeRunInGeist))
        {
            return TextEditOverflow.None;
        }

        var run = draft.Run;
        var before = TextEditCheck.Elements(run.Text).Count;
        var after = TextEditCheck.Elements(text).Count;
        if (after <= before || before == 0 || Tab.Session.Document is not { } document)
        {
            return TextEditOverflow.None;
        }

        if (draft.Others is null)
        {
            var page = await document.GetPageTextAsync(run.Page, token);
            var area = run.Bounds;
            draft.PageBounds = new PageRect(0, 0, page.Width, page.Height);
            draft.Others =
            [
                .. page.Chars
                    .Where(c => !c.IsGenerated && c.Box.Width > 0 && c.Box.Height > 0)
                    .Select(static c => c.Box)
                    .Where(box => !Inside(area, (box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2)),
            ];
        }

        var width = run.Bounds.Width * after / before;
        var grown = new PageRect(run.Bounds.Left, run.Bounds.Top, run.Bounds.Left + width, run.Bounds.Bottom);
        return TextEditOverlap.Find(grown, draft.Others, draft.PageBounds);
    }

    private static bool Inside(PageRect area, double x, double y) => x >= area.Left - 0.5 && x <= area.Right + 0.5 && y >= area.Top - 0.5 && y <= area.Bottom + 0.5;

    private async Task<TextEditCommitResult> CommitCoreAsync(Draft draft, IDisposable pending)
    {
        try
        {
            // The draft is held by the pending handle from here on; the unresolved one is taken again when the text stays open.
            var text = draft.Text;
            var whole = draft.WholeRun;
            var run = draft.Run;
            TextEditDecision decision;
            try
            {
                (decision, whole) = await DecideAsync(run, text, whole, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Failure(draft, ex);
            }

            if (!ReferenceEquals(_draft, draft))
            {
                // The draft was dropped while the decision was made (Esc, undo, the end of the mode): nothing is written.
                return new TextEditCommitResult(TextEditCommitKind.NoDraft, decision, null, string.Empty);
            }

            switch (decision.Outcome)
            {
                case TextEditOutcome.Unchanged:
                    DropDraft();
                    _note = TextEditWording.Unchanged;
                    Changed?.Invoke();
                    return new TextEditCommitResult(TextEditCommitKind.Unchanged, decision, null, TextEditWording.Unchanged);
                case TextEditOutcome.Rejected:
                    return Refuse(draft, decision);
            }

            if (BeforeWrite is { } hold)
            {
                await hold();
                if (!ReferenceEquals(_draft, draft))
                {
                    // Esc, undo or the end of the mode came while the write waited: nothing is written.
                    return new TextEditCommitResult(TextEditCommitKind.NoDraft, decision, null, string.Empty);
                }
            }

            TextReplaceResult? replaced;
            try
            {
                replaced = await Tab.Annotations.Editor.ApplyTextEditAsync(run, text, whole ? TextEditMode.WholeRunInGeist : TextEditMode.KeepFont);
            }
            catch (TextEditRejectedException ex)
            {
                return Refuse(draft, ex.Decision);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Failure(draft, ex);
            }

            if (ReferenceEquals(_draft, draft))
            {
                if (!string.Equals(draft.Text, text, StringComparison.Ordinal) && replaced?.After is { } after)
                {
                    // The text changed while the line was written: the line as it stands now is the base of the draft, and the new text stays in the field.
                    draft.Run = after;
                    draft.Others = null;
                    SyncUnresolved(draft);
                }
                else
                {
                    DropDraft();
                }
            }

            InvalidateRuns();
            _note = TextEditWording.Done;
            Changed?.Invoke();
            return new TextEditCommitResult(TextEditCommitKind.Applied, decision, null, TextEditWording.Done);
        }
        finally
        {
            pending.Dispose();
            _committing = false;
            Changed?.Invoke();
        }
    }

    private TextEditCommitResult Refuse(Draft draft, TextEditDecision decision)
    {
        var note = decision.Rejection == TextEditRejection.None ? TextEditWording.Failed : TextEditWording.Rejection(decision.Rejection, decision.Unsupported);
        KeepOpen(draft, note);
        return new TextEditCommitResult(TextEditCommitKind.Refused, decision, null, note);
    }

    // A failure of the write or of the check: what the user is told depends on the kind; nothing is shown as written that was not.
    private TextEditCommitResult Failure(Draft draft, Exception ex)
    {
        switch (ex)
        {
            case TextRunStaleException:
                InvalidateRuns();
                KeepOpen(draft, TextEditWording.Stale);
                return new TextEditCommitResult(TextEditCommitKind.Stale, null, null, TextEditWording.Stale);
            case TextRunLockedException locked:
                var note = TextEditWording.Lock(locked.Reason);
                KeepOpen(draft, note);
                return new TextEditCommitResult(TextEditCommitKind.Locked, null, locked.Reason, note);
            case AnnotationHistoryFullException:
                KeepOpen(draft, TextEditWording.HistoryFull);
                return new TextEditCommitResult(TextEditCommitKind.HistoryFull, null, null, TextEditWording.HistoryFull);
            case TextEditWriteException:
                // The page may have changed: the editor counts it as an unsaved change and the page is redrawn; the draft goes, the runs are read again.
                if (ReferenceEquals(_draft, draft))
                {
                    DropDraft();
                }

                InvalidateRuns();
                _note = TextEditWording.WriteFailed;
                Changed?.Invoke();
                return new TextEditCommitResult(TextEditCommitKind.Failed, null, null, TextEditWording.WriteFailed);
            default:
                KeepOpen(draft, TextEditWording.Failed);
                return new TextEditCommitResult(TextEditCommitKind.Failed, null, null, TextEditWording.Failed);
        }
    }

    // The text stays in the field with its reason; the tab keeps counting it as unsaved.
    private void KeepOpen(Draft draft, string note)
    {
        if (ReferenceEquals(_draft, draft))
        {
            SyncUnresolved(draft);
            _preview = null;
            _note = note;
            Changed?.Invoke();
        }
    }

    private void OnFlushRequested()
    {
        if (_draft is null || !_active)
        {
            return;
        }

        if (SynchronizationContext.Current == Ui)
        {
            FlushDraft();
        }
        else
        {
            Ui.Post(static state => ((TextEditSession)state!).FlushDraft(), this);
        }
    }

    // A flush (save, print, close, undo) ends the draft on its own account: an unchanged text is dropped, any other is written; one that is refused stays open as unresolved.
    private void FlushDraft()
    {
        if (_draft is not { } draft)
        {
            return;
        }

        if (string.Equals(draft.Text, draft.Run.Text, StringComparison.Ordinal))
        {
            DropDraft();
            Changed?.Invoke();
            return;
        }

        _ = CommitAsync();
    }

    // Undo and redo while the field is open: the draft ends like with Esc and the history stays as it is.
    private bool DiscardForHistory()
    {
        if (_draft is null)
        {
            return false;
        }

        Discard();
        return true;
    }

    // Any change of the history (an edit, undo, redo, an OCR run) may have changed the lines of a page: what was read is dropped.
    private void OnEditorChanged()
    {
        if (!_disposed && _active)
        {
            Ui.Post(static state =>
            {
                var session = (TextEditSession)state!;
                if (session._active && !session._disposed)
                {
                    session.InvalidateRuns();
                }
            }, this);
        }
    }

    private sealed class Draft(TextRun run)
    {
        public TextRun Run { get; set; } = run;

        public string Text { get; set; } = run.Text;

        public bool WholeRun { get; set; }

        public IDisposable? Unresolved { get; set; }

        public IReadOnlyList<PageRect>? Others { get; set; }

        public PageRect PageBounds { get; set; }
    }

    private sealed class CodePointSet(IReadOnlySet<int> codePoints) : IFontCoverage
    {
        public CodePointSet(IEnumerable<int> codePoints)
            : this(new HashSet<int>(codePoints))
        {
        }

        public bool Covers(int codePoint) => codePoints.Contains(codePoint);
    }
}
