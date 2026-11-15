using Avalonia.Input;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write.Viewing;

/// <summary>
/// The keyboard side of the page view: typing, deleting, Enter, Tab, the caret keys, Shift for the selection, select all and the keys that run the commands of undo, redo and the
/// clipboard (a seam: <see cref="EditCommandHandler"/>). Text arrives through <see cref="OnTextInput"/> only, never from a key; a key event that is text is left alone so the text input
/// follows (see <see cref="EditingKeys.IsTextInput(KeyEventArgs, string?)"/>).
/// </summary>
/// <remarks>
/// Rules. Every key is one <see cref="EditSession.Apply"/> (one history step; typing is coalesced by the history itself), run with the pages through the caret's page and the last page in view,
/// then the session's <c>Changed</c> keeps the caret in view. The operation reads the selection when it runs, so input typed during a save is queued and replayed in arrival order. Caret keys
/// move the caret only once there is one (a click or an edit made the session) and the session can edit; before that the keys scroll as they always did, and while only a prefix is open or the
/// session failed they scroll too. During a save they are ignored (a position computed now would be stale when the queued edits ran). Typing, Backspace, Delete, Enter and Tab need the whole
/// model: while it is still opening or the session failed the key is claimed, changes nothing and raises <see cref="InputRefused"/> (the window shows a hint once per burst). A layout failure
/// during an edit is caught here (the session is then failed and shows no more caret); it also raises <see cref="InputRefused"/>.
/// <para>
/// Backspace and Delete over a selection delete the selection; with a caret they call the editor, whose answer for a field result is a selection of it (nothing changes): the view makes it the
/// selection and the next Backspace or Delete removes it. A key held down repeats typing, deleting, Enter, Tab and the caret keys (<see cref="EditAction.AllowsRepeat"/>); select all, undo, redo
/// and the clipboard keys act once per press (<c>_held</c> sees the repeat: Avalonia gives no repeat flag).
/// </para>
/// </remarks>
internal sealed partial class PageView
{
    private readonly HashSet<Key> _held = [];
    private float? _goalX;
    private TextPosition? _goalFor;

    /// <summary>
    /// Runs a command of the window by id (undo, redo, cut, copy, paste); true when it ran. The window sets it. The ids are those of <see cref="EditingKeys"/>; until the commands exist
    /// the window answers false and the key does nothing.
    /// </summary>
    internal Func<string, bool>? EditCommandHandler { get; set; }

    /// <summary>Raised when typing or an editing key was refused because only a prefix is open or editing failed; the window shows the hint. Never raised for a save (input is queued then).</summary>
    internal event Action<EditBlockedReason>? InputRefused;

    /// <summary>Forgets the caret affinity: the caret is downstream again, also when it later returns to the position it was placed at.</summary>
    internal void ResetAffinity()
    {
        _affinity = CaretAffinity.Downstream;
        _affinityFor = null;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _held.Remove(e.Key);
        base.OnKeyUp(e);
    }

    // The block on typing and editing keys: the session's reason, or (before any session) the runner's.
    private EditBlockedReason? BlockReason()
    {
        if (_tab.Editor is { } session)
        {
            return session.BlockedReason;
        }

        if (_tab.IsDisposed || _runner.IsFaulted)
        {
            return EditBlockedReason.Faulted;
        }

        return _runner.FullModelReady ? null : EditBlockedReason.StillOpening;
    }

    /// <summary>Text from the keyboard, a dead key, a compose sequence or an input method commit (anything but a control character).</summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || _released || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Text that arrives while a composition is shown (or right after the platform ended one) is its commit: the preedit goes first, the text then goes in as typed text.
        var commit = TakeCommit();
        foreach (var c in e.Text)
        {
            if (char.IsControl(c))
            {
                // Enter and Tab come as keys; a control character is never text.
                return;
            }
        }

        e.Handled = true;
        var text = e.Text;
        if (commit)
        {
            LogCommit(text.Length);
        }

        ApplyKey(session => ed => session.Selection is { } selection ? ed.InsertText(selection, text) : null);
    }

    // The key handler of the editing keys with a net under it: a failure of a caret move or of anything else in it is a bug (Debug rethrows it, Release claims the key and traces the type).
    private bool HandleEditingKeyGuarded(KeyEventArgs e, bool repeat)
    {
        try
        {
            return HandleEditingKey(e, repeat);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ReportKeyFailure(ex);
            return true;
        }
    }

    // The key handler of the editing keys; true when the key was claimed.
    private bool HandleEditingKey(KeyEventArgs e, bool repeat)
    {
        if (_released || EditingKeys.IsTextInput(e, e.KeySymbol))
        {
            return false;
        }

        if (EditingKeys.For(e.Key, e.KeyModifiers, OperatingSystem.IsMacOS()) is not { } action)
        {
            return false;
        }

        if (repeat && !action.AllowsRepeat)
        {
            return true;
        }

        switch (action.Kind)
        {
            case EditActionKind.Command:
                return EditCommandHandler?.Invoke(action.CommandId!) == true;
            case EditActionKind.SelectAll:
                return SelectAll();
            case EditActionKind.Move:
                return MoveCaret(action);
            default:
                return EditByKey(action);
        }
    }

    // Backspace, Delete, Enter, Tab (and the claimed no-op): need the whole model. Tab and Shift+Tab are claimed only while the document can be edited: while it is still opening or
    // failed they type nothing and must keep moving the focus, or the page view would trap the keyboard.
    private bool EditByKey(EditAction action)
    {
        var blocked = BlockReason() is { } reason and not EditBlockedReason.Saving ? reason : (EditBlockedReason?)null;
        if (action.Kind is EditActionKind.Nothing or EditActionKind.Tab && blocked is not null)
        {
            return false;
        }

        if (action.Kind == EditActionKind.Nothing)
        {
            return true;
        }

        if (blocked is { } refused)
        {
            InputRefused?.Invoke(refused);
            return true;
        }

        switch (action.Kind)
        {
            case EditActionKind.Enter:
                ApplyKey(session => ed => session.Selection is { } s ? ed.SplitParagraph(s) : null);
                break;
            case EditActionKind.Tab:
                ApplyKey(session => ed => session.Selection is { } s ? ed.InsertText(s, "\t") : null);
                break;
            case EditActionKind.Delete:
                var (unit, forward) = (action.Unit, action.Forward);
                ApplyKey(session => ed => DeleteBySelectionOrCaret(ed, session.Selection, unit, forward));
                break;
        }

        return true;
    }

    // A selection is deleted; a caret deletes a unit. A field result next to the caret is selected first (the editor's answer has Selected set), the next key deletes it.
    private static EditResult? DeleteBySelectionOrCaret(TextEditor editor, TextRange? selection, DeleteUnit unit, bool forward)
    {
        if (selection is not { } range)
        {
            return null;
        }

        if (!range.IsEmpty)
        {
            return editor.Delete(range);
        }

        return forward ? editor.DeleteForward(range.End, unit) : editor.Backspace(range.End, unit);
    }

    /// <summary>
    /// One edit of the session for one key. The session is made on first use (the whole model must be there, checked here); the edit itself goes through <see cref="ApplyGuarded"/>,
    /// the one place that decides what a failure means (shared with the commands of undo, redo and the clipboard).
    /// </summary>
    private void ApplyKey(Func<EditSession, Func<TextEditor, EditResult?>> operation)
    {
        if (BlockReason() is { } reason and not EditBlockedReason.Saving)
        {
            InputRefused?.Invoke(reason);
            return;
        }

        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        ResetAffinity();
        _goalFor = null;
        ApplyGuarded(session, operation(session));
    }

    /// <summary>
    /// Runs an editing operation on the session through <see cref="EditSession.Apply"/> with the pages through the caret's page and the last page in view, and decides what a failure
    /// means. What can fail: the layout (then <see cref="EditSession.Apply"/> has failed the runner, the session is faulted and shows no more caret, and <see cref="InputRefused"/> says so
    /// once); the operation itself after it changed the model (the revision of the document differs from the one before: model, history and layout may now disagree, so the session is
    /// failed on purpose, through the runner, and <see cref="InputRefused"/> says editing is unavailable); or the operation before any change, for example the text editor refusing a stale
    /// position with an <see cref="ArgumentException"/> (it checks positions before it changes anything, so model, history and layout are as they were: the operation is refused). That last
    /// kind is a bug and no state of the document: a Debug build rethrows it, a Release build refuses silently and traces only the exception type (no message, no stack, no text). Write has
    /// no crash guard yet (shared crash handling is a later milestone). Returns the result of the operation, or null when it was refused, queued, blocked or failed.
    /// </summary>
    private EditResult? ApplyGuarded(EditSession session, Func<TextEditor, EditResult?> operation)
    {
        var model = _tab.Docx.Model;
        var revision = model.Edits.Revision;
        try
        {
            return session.Apply(operation, ThroughPage());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (session.BlockedReason == EditBlockedReason.Faulted)
            {
                // The layout failed for good (EditSession.Apply failed the runner and rethrew): the session is faulted (no caret, no more edits) and said so through Changed; the window's hint says editing is unavailable.
                InputRefused?.Invoke(EditBlockedReason.Faulted);
                return null;
            }

            if (model.Edits.Revision != revision)
            {
                // The model changed and the operation then failed: nothing guarantees that model, history and layout still agree, and the session was not told (Apply raises Changed only for
                // a result). Fault it visibly rather than let the user type on into a document that may be out of step with its pages.
                _runner.FailEdit(ex);
                InputRefused?.Invoke(EditBlockedReason.Faulted);
                InvalidateAll();
                NotifyViewChanged();
                return null;
            }

            ReportKeyFailure(ex);
            return null;
        }
    }

    // Any other failure of a key: a bug, not a state of the document. Debug builds rethrow it so that tests and developers see it; a release build refuses the key (nothing is shown and
    // nothing is logged but the type of the exception: its message and stack can hold paths or text of the document) and the session stays usable.
    private static void ReportKeyFailure(Exception ex)
    {
#if DEBUG
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
#else
        System.Diagnostics.Trace.TraceError("A key of the page view failed: " + ex.GetType().Name);
#endif
    }

    // The last page an edit must lay out before it returns: the caret's page and the last page in view.
    private int ThroughPage()
    {
        EnsureCurrent();
        if (_scroller is not { Count: > 0 } scroller)
        {
            return 0;
        }

        var lastInView = scroller.PageAt(_offsetY + _viewport.Height);
        return Math.Max(lastInView, _caretPage >= 0 ? _caretPage : lastInView);
    }

    // Select all: the whole story of the caret (the body, or the header, footer or note it is in), from its first top-level paragraph to the end of its last.
    private bool SelectAll()
    {
        if (!CanPlaceCaret || ActiveLayout() is not { } layout)
        {
            return false;
        }

        if (BlockReason() == EditBlockedReason.Saving)
        {
            return true;
        }

        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        if (session.Selection is not { } current || TextUnits.BlocksOf(_tab.Docx.Model, current.End.Story) is not { } blocks)
        {
            return true;
        }

        Paragraph? first = null;
        Paragraph? last = null;
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                first ??= paragraph;
                last = paragraph;
            }
        }

        if (first is null || last is null)
        {
            return true;
        }

        var story = current.End.Story;
        var end = new TextPosition(story, last, ProjectionOf(layout, new TextPosition(story, last, 0)).Text.Length);
        Place(session, new TextRange(new TextPosition(story, first, 0), end), CaretAffinity.Downstream);
        _goalFor = null;
        EnsureCaretVisible();
        return true;
    }

    // A caret key. Not claimed (so the view scrolls as before) while there is no caret to move.
    private bool MoveCaret(EditAction action)
    {
        if (_tab.Editor is not { } session || !CanPlaceCaret || session.BlockedReason is EditBlockedReason.Faulted or EditBlockedReason.StillOpening)
        {
            return false;
        }

        if (session.BlockedReason == EditBlockedReason.Saving)
        {
            return true;
        }

        if (session.Selection is not { } selection || ActiveLayout() is not { } layout)
        {
            return false;
        }

        AttachSession(session);
        var move = action.Move;
        var extend = action.Extend;
        var caret = selection.End;
        var anchor = selection.Start;

        // A plain Left or Right with a selection collapses it to its edge, as the editors do.
        if (!extend && !selection.IsEmpty && move is CaretMove.Left or CaretMove.Right)
        {
            var normalized = selection.Normalized();
            var edge = move == CaretMove.Left ? normalized.Start : normalized.End;
            Place(session, new TextRange(edge, edge), CaretAffinity.Downstream);
            _goalFor = null;
            EnsureCaretVisible();
            return true;
        }

        var hint = PageHint;
        var affinity = Affinity;
        var vertical = move is CaretMove.Up or CaretMove.Down or CaretMove.PageUp or CaretMove.PageDown;
        float? goal = null;
        if (vertical)
        {
            goal = _goalFor is { } at && SamePosition(at, caret) && _goalX is { } kept ? kept : CaretBoxOf(layout, caret, affinity)?.X;
        }

        // An upstream caret is drawn at the end of a line whose offset is also the start of the next one: Home, Up, Down and the page keys start from that line, End stays.
        var basis = caret;
        if (affinity == CaretAffinity.Upstream)
        {
            if (move == CaretMove.LineEnd)
            {
                return Finish(session, extend, anchor, caret, CaretAffinity.Upstream, vertical: false, goal: null);
            }

            if (move is CaretMove.LineStart || vertical)
            {
                if (layout.Move(caret, CaretMove.Left, null, hint) is { } before && ReferenceEquals(before.Paragraph, caret.Paragraph) && before.Offset < caret.Offset)
                {
                    basis = before;
                }
            }
        }

        var target = Target(layout, basis, move, goal, hint);
        if (target is not { } moved)
        {
            return true;
        }

        var after = CaretAffinity.Downstream;
        if (move == CaretMove.LineEnd && UpstreamBox(layout, moved, hint) is not null)
        {
            // The line ended where the next one starts (a wrapped long word): the caret stays at the end of the line that was asked for.
            after = CaretAffinity.Upstream;
        }

        return Finish(session, extend, anchor, moved, after, vertical, goal);
    }

    private bool Finish(EditSession session, bool extend, TextPosition anchor, TextPosition target, CaretAffinity affinity, bool vertical, float? goal)
    {
        Place(session, new TextRange(extend ? anchor : target, target), affinity);
        _goalFor = vertical ? target : null;
        _goalX = vertical ? goal : null;
        EnsureCaretVisible();
        return true;
    }

    // The target of a move. The end of the document of a layout that is not complete yet is the end of the story as the model has it (the layout knows only what is laid out); a table cell
    // keeps the layout's answer, which never leaves the cell.
    private TextPosition? Target(DocumentLayout layout, TextPosition from, CaretMove move, float? goal, int hint)
    {
        if (move == CaretMove.DocumentEnd && !layout.IsComplete && TextUnits.BlocksOf(_tab.Docx.Model, from.Story) is { } blocks && TextUnits.ContainerOf(blocks, from.Paragraph) is { } container
            && ReferenceEquals(container, blocks))
        {
            for (var i = blocks.Count - 1; i >= 0; i--)
            {
                if (blocks[i] is Paragraph last)
                {
                    var end = new TextPosition(from.Story, last, ProjectionOf(layout, new TextPosition(from.Story, last, 0)).Text.Length);
                    return end;
                }
            }
        }

        return layout.Move(from, move, goal, hint);
    }
}
