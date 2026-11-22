using Avalonia;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write.Viewing;

/// <summary>
/// The input method side of the page view. The platform asks for a <see cref="WriteImeClient"/> when the view takes the focus (<see cref="InputElement.TextInputMethodClientRequestedEvent"/>);
/// nothing exists before that. A composition is a preedit that the layout shows inside the paragraph at the caret (<see cref="DocumentLayout.SetPreedit"/>, underlined, reflowing
/// the line) and that is in no model: it never marks the document dirty, never enters the history and never changes the revision.
/// </summary>
/// <remarks>
/// Rules.
/// <list type="bullet">
/// <item>The preedit stands at the caret (the moving end of the selection). A selection that exists when the composition starts stays selected, and drawn, until the commit, which
/// replaces it like typed text does.</item>
/// <item>Keys belong to the input method while a preedit exists: the editing and navigation keys are claimed and do nothing (a platform delivers the keys of a composition to the
/// input method first, so a key that reaches the view while composing is one the input method did not take). Esc ends the composition without writing.</item>
/// <item>The commit is text input. Platforms differ in the order: some end the preedit (<c>SetPreeditText(null)</c>) and then send the text, some send the text while the preedit is
/// still set. Both are a commit: the preedit goes first, then the text is inserted through the same path as typed text (one history step, with the usual coalescing, queued during a save).
/// A preedit that ends and is not followed by text is a cancel (decided when <see cref="CommitWindow"/> has passed).</item>
/// <item>The composition ends without writing when the view loses the focus, when the view switches between pages and the writing view, and when a pointer press moves the caret
/// (the platform is asked to reset first, so one that commits on a reset delivers the text at the old caret).</item>
/// <item>The preedit is refused while only a prefix is open and after a failure (<see cref="InputRefused"/>, like typing); during a save it is shown (it is not in the model) and its commit queues like a key.</item>
/// <item>The cursor rectangle and the surrounding text are read from the layout and the model every time; the platform is told after each change of the caret, the layout, the scroll position and the zoom.</item>
/// </list>
/// </remarks>
internal sealed partial class PageView
{
    /// <summary>The most UTF-16 code units of the paragraph that <see cref="ImeSurrounding"/> reports (a window around the caret in a longer one).</summary>
    internal const int SurroundingLimit = 4096;

    /// <summary>How long after the platform ended a preedit text still counts as its commit (the platforms deliver both in one call; this is only the margin before a missing text is a cancel).</summary>
    internal static readonly TimeSpan CommitWindow = TimeSpan.FromMilliseconds(250);

    private WriteImeClient? _ime;
    private TextPosition? _composeAt;
    private int _composeCursor;
    private int _composeRevision;
    private bool _awaitingCommit;
    private bool _endCheckPosted;

    // The pending format the session had when the composition began (null when none): the commit takes it only while the session still holds that very object (see OnTextInput).
    private IReadOnlyList<PropertyChange>? _composePending;
    private bool _settingImeSelection;

    /// <summary>Where the path of the IME log comes from (the environment variable <see cref="ImeLog.Variable"/> unless a test says otherwise); read at every event, so a log needs no work before its first line.</summary>
    internal Func<string?> ImeLogPath { get; set; } = static () => Environment.GetEnvironmentVariable(ImeLog.Variable);

    /// <summary>True once the platform asked for the input method client.</summary>
    internal bool ImeClientCreated => _ime is not null;

    /// <summary>True while a preedit is shown.</summary>
    internal bool IsComposing => _composeAt is not null;

    /// <summary>The text of the preedit as the layout shows it, or null when none is shown.</summary>
    internal string? PreeditText => _composeAt is null ? null : _builtLayout?.PreeditText;

    /// <summary>The bar of the caret inside the preedit in the coordinates of the view, or null when no preedit is shown or its page is not in view's scroller.</summary>
    internal Rect? PreeditCaretBounds
    {
        get
        {
            EnsureCurrent();
            return _composeAt is not null && _builtLayout is { } layout && layout.PreeditCaret(_composeCursor, PageHint) is { } box ? BoundsOfBox(box) : null;
        }
    }

    private void OnImeRequested(object? sender, TextInputMethodClientRequestedEventArgs e)
    {
        if (!_released)
        {
            e.Client = _ime ??= new WriteImeClient(this, new ImeLog(() => ImeLogPath()));
        }
    }

    // The cursor rectangle the platform places its candidate window at: the caret bar (inside the preedit while composing); empty when there is no caret.
    internal Rect ImeCursorRectangle() => CaretBarBounds ?? default;

    internal void NotifyImeCursor() => _ime?.NotifyCursor();

    internal void NotifyImeText()
    {
        if (!_settingImeSelection)
        {
            _ime?.NotifyText();
        }
    }

    /// <summary>
    /// The paragraph of the caret as the platform sees it: its text (the whole paragraph, or a window of <see cref="SurroundingLimit"/> units around the caret that
    /// never splits a surrogate pair), the selection in it (a selection that leaves the paragraph is reported as the caret) and where the window starts in the paragraph.
    /// Empty without a caret.
    /// </summary>
    internal (string Text, int Start, int End, int Origin) ImeSurrounding()
    {
        if (_released || !CanPlaceCaret || CurrentSelection() is not { } selection || ActiveLayout() is not { } layout)
        {
            return ("", 0, 0, 0);
        }

        var text = ProjectionOf(layout, selection.End).Text;
        var caret = Math.Clamp(selection.End.Offset, 0, text.Length);
        var anchor = ReferenceEquals(selection.Start.Paragraph, selection.End.Paragraph) && selection.Start.Story == selection.End.Story ? Math.Clamp(selection.Start.Offset, 0, text.Length) : caret;
        var begin = 0;
        var end = text.Length;
        if (text.Length > SurroundingLimit)
        {
            begin = Math.Max(0, Math.Min(caret - (SurroundingLimit / 2), text.Length - SurroundingLimit));
            end = begin + SurroundingLimit;
            if (begin > 0 && char.IsLowSurrogate(text[begin]))
            {
                begin++;
            }

            if (end < text.Length && char.IsHighSurrogate(text[end - 1]))
            {
                end--;
            }
        }

        return (text[begin..end], Math.Clamp(anchor - begin, 0, end - begin), Math.Clamp(caret - begin, 0, end - begin), begin);
    }

    // The platform sets the selection (a reconversion, a correction): offsets of the window of ImeSurrounding, in the caret's paragraph.
    internal void ImeSetSelection(int start, int end)
    {
        if (_released || IsComposing || !CanPlaceCaret || CurrentSelection() is not { } selection || ActiveLayout() is not { } layout)
        {
            return;
        }

        var origin = ImeSurrounding().Origin;
        var length = ProjectionOf(layout, selection.End).Text.Length;
        var paragraph = selection.End.Paragraph;
        var story = selection.End.Story;
        var a = new TextPosition(story, paragraph, Math.Clamp(origin + start, 0, length));
        var b = new TextPosition(story, paragraph, Math.Clamp(origin + end, 0, length));
        _settingImeSelection = true;
        try
        {
            Place(_tab.EnsureEditor(), new TextRange(a, b), CaretAffinity.Downstream);
        }
        finally
        {
            _settingImeSelection = false;
        }
    }

    /// <summary>
    /// The platform sets or changes the preedit (<c>SetPreeditText</c>): shows it at the caret, scrolls it into view and announces the new cursor rectangle. An empty or null
    /// text ends the composition on the platform's side (a commit follows as text input, or nothing does and it was a cancel). <paramref name="cursor"/> is the cursor inside the
    /// text (UTF-16 units; the end when the platform gives none) at which Write draws its own caret.
    /// </summary>
    internal void ImePreedit(string? text, int? cursor)
    {
        if (_released)
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            PlatformEndedPreedit();
            return;
        }

        if (BlockReason() is { } reason and not EditBlockedReason.Saving)
        {
            InputRefused?.Invoke(reason);
            return;
        }

        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        var start = _composeAt ?? session.Selection?.End;
        if (start is not { } at)
        {
            return;
        }

        SetLayoutPreedit(session, at, text);
        if (ActiveLayout() is not { PreeditText: { } shown })
        {
            _composeAt = null;
            return;
        }

        if (_composeAt is null)
        {
            // A composition begins: the format given at the caret before it (Ctrl+B, then the input method) is the one its commit takes.
            _composePending = session.Pending.Changes.Count > 0 ? session.Pending.Changes : null;
        }

        _composeAt = at;
        _composeCursor = Math.Clamp(cursor ?? shown.Length, 0, shown.Length);
        _composeRevision = _tab.Docx.Model.Edits.Revision;
        _awaitingCommit = false;
        _ime?.Log.Preedit(shown.Length);
        EnsureCurrent();
        UpdateScrollBars();
        RestartBlink();
        EnsureCaretVisible();
        InvalidateAll();
        NotifyViewChanged();
        NotifyImeCursor();
    }

    // Shows or removes the preedit on the layout of the session; a failure of the layout faults the session (it said so itself), any other failure is a bug (see ReportKeyFailure).
    private bool SetLayoutPreedit(EditSession session, TextPosition at, string? text)
    {
        try
        {
            return session.SetPreedit(at, text, ThroughPage());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (session.BlockedReason == EditBlockedReason.Faulted)
            {
                InputRefused?.Invoke(EditBlockedReason.Faulted);
                return false;
            }

            ReportKeyFailure(ex);
            return false;
        }
    }

    // The platform ended its preedit. If text follows it is the commit; if none does by the next idle moment it was a cancel.
    private void PlatformEndedPreedit()
    {
        if (!IsComposing)
        {
            return;
        }

        ClearComposition();
        _awaitingCommit = true;
        if (!_endCheckPosted)
        {
            _endCheckPosted = true;
            DispatcherTimer.RunOnce(CheckForCommit, CommitWindow, DispatcherPriority.Background);
        }
    }

    private void CheckForCommit()
    {
        _endCheckPosted = false;
        if (_awaitingCommit)
        {
            _awaitingCommit = false;
            _composePending = null;
            _ime?.Log.Cancel();
        }
    }

    // Text arrived: it is the commit of a composition when one is shown or the platform has just ended one. The preedit leaves the layout first.
    private bool TakeCommit()
    {
        var commit = IsComposing || _awaitingCommit;
        ClearComposition();
        _awaitingCommit = false;
        return commit;
    }

    // The pending format that goes with the commit now arriving (and forgets it): the one of the composition's start, or null.
    private IReadOnlyList<PropertyChange>? TakeComposePending()
    {
        var pending = _composePending;
        _composePending = null;
        return pending;
    }

    private void LogCommit(int length) => _ime?.Log.Commit(length);

    // Removes the preedit from the layout and the state of the view; writes nothing and logs nothing.
    private void ClearComposition()
    {
        if (_composeAt is not { } at)
        {
            return;
        }

        _composeAt = null;
        if (!_released && !_tab.IsDisposed && _tab.Editor is { } session)
        {
            SetLayoutPreedit(session, at, null);
        }

        if (_released)
        {
            return;
        }

        EnsureCurrent();
        UpdateScrollBars();
        RestartBlink();
        InvalidateAll();
        NotifyViewChanged();
        NotifyImeCursor();
    }

    /// <summary>Ends a composition without writing (Esc, focus lost, the view switched, a click): the preedit goes, the document and its history are as they were, and the log says <c>cancel</c>.</summary>
    internal void CancelComposition(bool requestReset = false)
    {
        _awaitingCommit = false;
        _composePending = null;
        if (!IsComposing)
        {
            return;
        }

        if (requestReset)
        {
            _ime?.RequestPlatformReset();
        }

        if (!IsComposing)
        {
            // The platform ended it itself while it was asked (it sent its commit or its clear).
            return;
        }

        ClearComposition();
        _ime?.Log.Cancel();
    }

    // A pointer press moves the caret: the composition is over. The platform is asked to end its own first, so that one that commits on a reset delivers at the old caret.
    private void EndCompositionForPointer()
    {
        if (IsComposing)
        {
            CancelComposition(requestReset: true);
        }
    }

    // The composition on screen is no longer the composition of the view: the layout that held the preedit was replaced (it shows none), or its slot stands on another
    // paragraph or offset than the view's record, or the model changed since the preedit was set. A preedit is never moved to follow an edit (offsets shift, paragraphs are
    // cloned): it ends, the platform is asked to end its composition too, and the next text is plain typing at the caret.
    private void ForgetLostComposition()
    {
        if (_composeAt is not { } at)
        {
            return;
        }

        if (_builtLayout?.PreeditAt is not { } shown)
        {
            _composeAt = null;
            _ime?.Log.Cancel();
            return;
        }

        if (!ReferenceEquals(shown.Paragraph, at.Paragraph) || shown.Offset != at.Offset || shown.Story != at.Story
            || _tab.Docx.Model.Edits.Revision != _composeRevision)
        {
            CancelComposition(requestReset: true);
        }
    }

    // A key reached the view while a preedit is shown: the input method did not take it. Esc ends the composition; the editing and navigation keys are claimed and ignored.
    private void HandleKeyWhileComposing(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            CancelComposition(requestReset: true);
            e.Handled = true;
            return;
        }

        if (EditingKeys.For(e.Key, e.KeyModifiers, MacOsKeys) is not null || IsNavigationKey(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
        }
    }
}
