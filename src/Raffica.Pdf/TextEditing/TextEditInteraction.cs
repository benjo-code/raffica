using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Raffica.Core.TextEdit;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.TextEditing;

/// <summary>
/// The pointer, key and field side of the text edit mode of one tab. A click (a press and a release at the same place; a touch tap too) on a line gives it
/// the focus and opens the field over it, a click on a locked line shows its reason. Keys, while the page view has the focus: the arrows and Tab walk the
/// lines in reading order (Tab leaves the page when the walk is over, so the focus is never trapped), Enter opens the field on the focused line, Esc drops
/// an open draft and, with none, asks to leave the mode. There is no window-wide key capture: the keys are the page view's (tunnel, like the redact mode). The
/// field has its own keys (<see cref="RunFieldEditor"/>): Enter writes, Esc drops, Tab writes and leaves. The lines of the pages in view are read here, on
/// demand, when the mode starts, when the view moves and after every change of the document. Used on the UI thread only.
/// </summary>
internal sealed class TextEditInteraction : IDisposable
{
    // A press that never got farther than this (in DIPs) from where it went down is a click.
    private const double ClickSlop = 4;

    private readonly PageView _view;
    private readonly TextEditSession _session;
    private RunFieldEditor? _field;
    private TextRun? _fieldRun;
    private bool _closingField;
    private Point _press;
    private TextRun? _pressed;
    private bool _enterHeld;
    private bool _badgePress;
    private int _revealedPage = -1;
    private int _revealedIndex = -1;
    private bool _disposed;

    public TextEditInteraction(PdfTab tab, PageView view, TextEditSession session)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(session);
        _view = view;
        _session = session;
        view.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble);
        // Tunnel, not bubble: the page view's own handler scrolls with the arrows and marks them handled, so the keys of the lines must come first.
        view.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        view.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        view.ViewChanged += OnViewChanged;
        view.AddHandler(InputElement.PointerPressedEvent, OnPressedTunnel, RoutingStrategies.Tunnel);
        view.AddHandler(InputElement.PointerReleasedEvent, OnReleasedTunnel, RoutingStrategies.Tunnel);
        session.Changed += OnSessionChanged;
        session.RunsInvalidated += OnRunsInvalidated;
    }

    /// <summary>Raised when Esc found no draft to drop: the user asks to leave the mode.</summary>
    public event Action? EndRequested;

    /// <summary>The field over the line, or <see langword="null"/> while no draft is open.</summary>
    public RunFieldEditor? Field => _field;

    /// <summary>The task of the last field commit (for tests and for whoever must wait for it).</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Reads the lines of the pages in view that were not read yet, and lets go of the others (one page per job, off the UI thread).</summary>
    public Task LoadVisibleAsync()
    {
        if (_disposed || !_session.IsActive)
        {
            return Task.CompletedTask;
        }

        var (first, last) = _view.VisiblePages;
        if (last < first)
        {
            return Task.CompletedTask;
        }

        _session.Trim(first, last);
        var loads = new List<Task>();
        for (var page = first; page <= last; page++)
        {
            loads.Add(_session.LoadPageAsync(page));
        }

        return Task.WhenAll(loads);
    }

    /// <summary>Takes a key while the mode is on (the page view has the focus). Sets <c>Handled</c> when the key was used.</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!_session.IsActive || _disposed)
        {
            return false;
        }

        var used = KeyCore(e, repeat: false);
        if (used)
        {
            e.Handled = true;
        }

        return used;
    }

    /// <summary>Drops what Esc may drop: an open draft. Returns whether something was dropped; when nothing was, the same Esc asks to leave the mode.</summary>
    public bool Cancel()
    {
        if (!_session.HasDraft)
        {
            return false;
        }

        _session.Discard();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        _view.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
        _view.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _view.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _view.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        _view.ViewChanged -= OnViewChanged;
        _view.RemoveHandler(InputElement.PointerPressedEvent, OnPressedTunnel);
        _view.RemoveHandler(InputElement.PointerReleasedEvent, OnReleasedTunnel);
        _session.Changed -= OnSessionChanged;
        _session.RunsInvalidated -= OnRunsInvalidated;
        CloseField(restoreFocus: false);
        ToolTip.SetTip(_view, null);
    }

    private bool KeyCore(KeyEventArgs e, bool repeat)
    {
        var modifiers = e.KeyModifiers;
        switch (e.Key)
        {
            case Key.Escape when modifiers == KeyModifiers.None:
                if (!Cancel())
                {
                    EndRequested?.Invoke();
                }

                return true;
            case Key.Enter when modifiers == KeyModifiers.None:
                // A held key repeats the press: only the first one opens (a repeat is used up, so it does not reach the page view's own handling either).
                if (_session.Focused is { } focused)
                {
                    if (!repeat)
                    {
                        _ = OpenAsync(focused);
                    }

                    return true;
                }

                return false;
            case Key.Tab when modifiers is KeyModifiers.None or KeyModifiers.Shift:
                // Past the last line (or before the first) the key is not used, so the focus leaves the page instead of being trapped.
                return _session.MoveFocus(forward: modifiers == KeyModifiers.None);
            // The arrows walk the lines only once a line has the focus (by Tab, a click or Enter); before that, and past the first or last line, they scroll the page as always.
            case Key.Left or Key.Up when modifiers == KeyModifiers.None:
                return _session.HasFocus && _session.MoveFocus(forward: false);
            case Key.Right or Key.Down when modifiers == KeyModifiers.None:
                return _session.HasFocus && _session.MoveFocus(forward: true);
            default:
                return false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter is held when its key-down comes again before its key-up (the key-up is seen here wherever the focus is, the field being a child of the view).
        var repeat = false;
        if (e.Key == Key.Enter)
        {
            repeat = _enterHeld;
            _enterHeld = true;
        }

        // The keys of the field and of the bar belong to them; a repeated Enter is used up here, so a held key does not write the line again and again.
        if (_view.IsInField(e.Source) || _view.IsInBadge(e.Source))
        {
            if (repeat && _view.IsInField(e.Source))
            {
                e.Handled = true;
            }

            return;
        }

        if (!_session.IsActive || _disposed)
        {
            return;
        }

        if (KeyCore(e, repeat))
        {
            e.Handled = true;
        }
    }

    // The key going up belongs to whoever has the focus then (the field that Enter just opened): a press that lost the page's focus is over.
    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _enterHeld = false;
        }
    }

    // A press on the hint bar (the switch) takes the focus from the field on its way, but it is no leaving of the line: the line stays open and gets the focus back.
    private void OnPressedTunnel(object? sender, PointerPressedEventArgs e) => _badgePress = _field is not null && _view.IsInBadge(e.Source);

    private void OnReleasedTunnel(object? sender, PointerReleasedEventArgs e)
    {
        if (!_badgePress)
        {
            return;
        }

        // The click of the switch is still on its way (it bubbles after this): the flag goes and the field gets the keyboard back once it is through.
        Dispatcher.UIThread.Post(
            () =>
            {
                _badgePress = false;
                if (_session.HasDraft && !_disposed)
                {
                    FocusField();
                }
            },
            DispatcherPriority.Input);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;
        _enterHeld = false;
        if (!_session.IsActive || _disposed || _view.IsInField(e.Source) || _view.IsInBadge(e.Source))
        {
            return;
        }

        var point = e.GetPosition(_view);
        if (!e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed || _view.IsOverScrollBar(point) || _view.PageAt(point) is not { } spot)
        {
            return;
        }

        _press = point;
        _pressed = _session.RunAt(spot.Page, spot.X, spot.Y);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var run = _pressed;
        _pressed = null;
        if (!_session.IsActive || _disposed)
        {
            return;
        }

        var point = e.GetPosition(_view);
        if (Math.Abs(point.X - _press.X) > ClickSlop || Math.Abs(point.Y - _press.Y) > ClickSlop)
        {
            return;
        }

        if (run is null)
        {
            // A click away from every line: the draft (if any) was written when the field lost the focus, the focus on a line goes.
            if (!_session.HasDraft && _view.PageAt(point) is not null)
            {
                _session.Focus(null);
            }

            return;
        }

        _ = OpenAsync(run);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_session.IsActive || _disposed || _view.IsInField(e.Source) || _view.IsInBadge(e.Source))
        {
            return;
        }

        var point = e.GetPosition(_view);
        string? tip = null;
        if (_view.PageAt(point) is { } spot && _session.RunAt(spot.Page, spot.X, spot.Y) is { Lock: { } reason })
        {
            tip = TextEditWording.Lock(reason);
        }

        if (!string.Equals(ToolTip.GetTip(_view) as string, tip, StringComparison.Ordinal))
        {
            ToolTip.SetTip(_view, tip);
        }
    }

    // Focuses the line and opens the field over it; a draft on another line is written first (and stays open, with its reason, when it is refused).
    private async Task OpenAsync(TextRun run)
    {
        try
        {
            var onTheDraft = _session.DraftRun is { } open && open.Page == run.Page && open.ObjectIndex == run.ObjectIndex;
            if (!onTheDraft)
            {
                // A draft on another line is written first. The write may also be through already (the focus loss of the field writes it at the press,
                // before the release that brings the run here): either way the run is the one captured at the press, before the write.
                if (_session.HasDraft)
                {
                    Completion = _session.CommitAsync();
                    await Completion;
                }

                if (_session.HasDraft || _disposed || !_session.IsActive)
                {
                    return;
                }

                // The write may have shifted the objects of the page (a line split into pieces): the line is found again in the lines read after it,
                // and the field never opens on the index of the press.
                await _session.LoadPageAsync(run.Page);
                if (!_session.IsActive || _disposed || Resolve(run) is not { } resolved)
                {
                    return;
                }

                run = resolved;
            }

            _session.Focus(run);
            if (_session.OpenDraft(run))
            {
                FocusField();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The session reports its own refusals as notes; nothing more to do for an exception of the task.
        }
    }

    // The line as the session reads it now: the same text, font and place (the fingerprint and the box), whatever its index became.
    private TextRun? Resolve(TextRun old) =>
        _session.RunsOf(old.Page)
            .Where(r => string.Equals(r.Fingerprint, old.Fingerprint, StringComparison.Ordinal) && string.Equals(r.Text, old.Text, StringComparison.Ordinal))
            .OrderBy(r => Math.Abs(r.Bounds.Left - old.Bounds.Left) + Math.Abs(r.Bounds.Top - old.Bounds.Top))
            .FirstOrDefault();

    private void OnSessionChanged()
    {
        if (_disposed)
        {
            return;
        }

        RevealFocused();
        if (_field is { } open)
        {
            open.Field.IsReadOnly = _session.IsCommitting;
        }

        if (_session.HasDraft && _session.DraftRun is { } run)
        {
            if (_field is null || !SameRun(_fieldRun, run))
            {
                CloseField(restoreFocus: false);
                BuildField(run);
            }
        }
        else if (_field is not null)
        {
            CloseField(restoreFocus: true);
        }
    }

    // The focused line is brought into view (nothing moves while it is inside); done once per line, so the view is not pulled back after the user scrolled away.
    private void RevealFocused()
    {
        if (!_session.HasFocus)
        {
            _revealedPage = _revealedIndex = -1;
            return;
        }

        if (_session.Focused is { } focused && (focused.Page != _revealedPage || focused.ObjectIndex != _revealedIndex))
        {
            _revealedPage = focused.Page;
            _revealedIndex = focused.ObjectIndex;
            _view.Reveal(focused.Page, focused.Bounds);
        }
    }

    private void OnRunsInvalidated() => _ = LoadVisibleAsync();

    private void OnViewChanged()
    {
        if (_disposed || !_session.IsActive)
        {
            return;
        }

        _ = LoadVisibleAsync();
        Layout();
    }

    private static bool SameRun(TextRun? a, TextRun? b) => a is not null && b is not null && a.Page == b.Page && a.ObjectIndex == b.ObjectIndex;

    private void BuildField(TextRun run)
    {
        if (RunOutlines.RectOf(_view, run) is null)
        {
            return;
        }

        var editor = new RunFieldEditor();
        editor.SetColor(run.Color);
        editor.SetStartText(_session.DraftText);
        _field = editor;
        _fieldRun = run;
        editor.TextEdited += OnFieldTextEdited;
        editor.CommitRequested += OnFieldCommit;
        editor.DiscardRequested += OnFieldDiscard;
        editor.LeaveRequested += OnFieldLeave;
        editor.WholeRunToggleRequested += OnFieldWholeRun;
        editor.FocusLeft += OnFieldFocusLeft;
        _view.Field = editor;
        Layout();
        FocusField();
    }

    private void FocusField()
    {
        var editor = _field;
        if (editor is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (ReferenceEquals(_field, editor) && !_disposed)
                {
                    editor.Field.Focus();
                }
            },
            DispatcherPriority.Input);
    }

    // The field sits over the line at its size: the font size is the line's times the zoom, the width at least the line's and growing with the text up to the page edge.
    private void Layout()
    {
        if (_field is not { } editor || _fieldRun is not { } run)
        {
            return;
        }

        if (RunOutlines.RectOf(_view, run) is not { } rect || rect.Width <= 0 || run.Bounds.Width <= 0)
        {
            editor.IsVisible = false;
            return;
        }

        editor.IsVisible = true;
        // The scale is the one of the page to the view (the same for both axes, whatever the rotation of the page), not a ratio of this line's own sides.
        var scale = _view.PageBounds(run.Page) is { Width: > 0 } pageRect && _view.SizeOfPage(run.Page) is { Width: > 0 } pageSize ? pageRect.Width / pageSize.Width : rect.Width / run.Bounds.Width;
        var size = run.FontSize * scale;
        if (size < rect.Height / 4 || size > rect.Height * 4)
        {
            size = rect.Height / 1.2;
        }

        var height = Math.Max(rect.Height, size * 1.4);
        editor.SetSize(rect.Width + 6, height, size);
        var wanted = editor.NeededWidth() + 8;
        var right = _view.PageBounds(run.Page) is { } page ? page.Right : rect.Right;
        var width = Math.Max(rect.Width + 6, Math.Min(wanted, Math.Max(rect.Width + 6, right - rect.Left)));
        editor.SetSize(width, height, size);
        _view.FieldRect = new Rect(rect.Left - 2, rect.Top - ((height - rect.Height) / 2), width, height);
    }

    private void CloseField(bool restoreFocus)
    {
        if (_field is not { } editor)
        {
            return;
        }

        _closingField = true;
        try
        {
            _field = null;
            _fieldRun = null;
            var had = editor.Field.IsFocused;
            editor.TextEdited -= OnFieldTextEdited;
            editor.CommitRequested -= OnFieldCommit;
            editor.DiscardRequested -= OnFieldDiscard;
            editor.LeaveRequested -= OnFieldLeave;
            editor.WholeRunToggleRequested -= OnFieldWholeRun;
            editor.FocusLeft -= OnFieldFocusLeft;
            if (ReferenceEquals(_view.Field, editor))
            {
                // The rectangle first, as the text box tool does: clearing the slot tells the listeners that it is free.
                _view.FieldRect = default;
                _view.Field = null;
            }

            if (had && restoreFocus && !_disposed)
            {
                _view.Focus();
            }
        }
        finally
        {
            _closingField = false;
        }
    }

    private void OnFieldTextEdited()
    {
        if (_field is { } editor)
        {
            _session.SetText(editor.Field.Text ?? string.Empty);
            Layout();
        }
    }

    private void OnFieldCommit() => Completion = CommitAsync(leave: false);

    private void OnFieldLeave() => Completion = CommitAsync(leave: true);

    private void OnFieldWholeRun() => _session.SetWholeRun(!_session.WholeRun);

    private void OnFieldDiscard()
    {
        _session.Discard();
        _view.Focus();
    }

    // The user went elsewhere (a click, another control): the line is written; a refused text stays open as a parked draft with its reason.
    private void OnFieldFocusLeft()
    {
        if (_closingField || _disposed || _badgePress || !_session.HasDraft)
        {
            return;
        }

        Completion = CommitAsync(leave: false, refocus: false);
    }

    private async Task CommitAsync(bool leave, bool refocus = true)
    {
        try
        {
            var result = await _session.CommitAsync();
            if (_disposed)
            {
                return;
            }

            // Tab leaves the field for the page whatever the result was: a refused text stays parked with its note, and the focus is not trapped.
            if (leave && _session.IsActive)
            {
                _view.Focus();
            }
            else if (refocus && result.Kind is TextEditCommitKind.Refused or TextEditCommitKind.Stale or TextEditCommitKind.Locked or TextEditCommitKind.HistoryFull && _field is { } editor)
            {
                editor.Field.Focus();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The session turns its failures into notes; an exception of the task itself has nothing left to say.
        }
    }
}
