using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Raffica.Core.Forms;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Forms;

/// <summary>
/// The pointer side of the form of one tab. Forms are live whenever no annotate tool is active (there is no fill mode): a left-button press
/// of the mouse or the pen on an unlocked widget takes the press (a tunnel handler, so the text selection and the links do not see it), puts
/// the form focus on the widget and toggles a check box, chooses a radio button, or opens a <see cref="FormFieldEditor"/> over a text field,
/// a combo box or a list. Anything else (a locked widget, a button, a press on no widget, touch, a scroll bar, a card open) is left to the
/// 2.1 behaviour. While an annotate tool is active, or when the document forbids filling, the fields are inert.
/// </summary>
/// <remarks>
/// A text draft is written when the field loses the focus, on Enter in a single-line field (then the form focus moves on) or on
/// Ctrl+Enter; Esc drops it; a choice in a combo box or list is written at once. Every value is one step of the tab's history and goes through
/// <see cref="FormSession.SetValueAsync"/>, in the order it was made. A draft that differs from the field holds a pending handle of the tab
/// from its first change until it is written or dropped; a text the field refuses stays in its editor with the reason in the hint and holds an
/// unresolved handle instead (dirty, nothing waits for it). A save, an undo, a close and a tool change end the draft first. Used on the
/// UI thread only; the hover reason of a locked or required widget is also shown here, because the overlay cannot carry a tooltip.
/// </remarks>
internal sealed class FormInteraction : IDisposable
{
    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly ToolState _tools;
    private readonly FormSession _forms;
    private readonly FormFocus _focus;
    private readonly DialogQueue _dialogs;
    private readonly Func<bool?> _fillAllowed;
    private readonly IDisposable _discardRegistration;
    private Task _chain = Task.CompletedTask;
    private Draft? _draft;
    private FormWidgetKey? _tipFor;
    private bool _forbiddenShown;
    private bool _disposed;

    /// <param name="fillAllowed">Whether the permissions of the document allow filling; <see langword="null"/> while they are not read (nothing is refused yet).</param>
    public FormInteraction(PdfTab tab, PageView view, ToolState tools, FormSession forms, FormFocus focus, DialogQueue dialogs, Func<bool?>? fillAllowed = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(dialogs);
        _tab = tab;
        _view = view;
        _tools = tools;
        _forms = forms;
        _focus = focus;
        _dialogs = dialogs;
        _fillAllowed = fillAllowed ?? (() => null);
        Mover = new FormMover(view, forms, focus);
        view.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        view.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble);
        view.AddHandler(InputElement.PointerExitedEvent, OnExited, RoutingStrategies.Bubble);
        view.ViewChanged += OnViewChanged;
        view.FieldChanged += OnFieldSlotChanged;
        tools.Changed += OnToolsChanged;
        tab.Annotations.FlushRequested += OnFlushRequested;
        _discardRegistration = tab.Annotations.AddDiscardHandler(DiscardForHistory);
    }

    /// <summary>Whether an editor is open (hidden or not).</summary>
    public bool IsEditing => _draft is not null;

    /// <summary>The open editor, or <see langword="null"/>.</summary>
    public FormFieldEditor? Editor => _draft?.Editor;

    /// <summary>The widget of the open editor, or <see langword="null"/>.</summary>
    public FormField? EditedField => _draft?.Field;

    /// <summary>The one walk of the form focus over the widgets (it reads pages and scrolls); Enter in an editor, the keys and the commands share it.</summary>
    public FormMover Mover { get; }

    /// <summary>The task that ends when every value committed so far is written (for tests).</summary>
    public Task Completion => _chain;

    /// <summary>
    /// Does what a press on <paramref name="field"/> does: puts the form focus on it and toggles a check box, chooses a radio button or opens
    /// an editor. The keys of the form (Enter, Space on the focused widget) call it too.
    /// </summary>
    /// <returns>Whether the widget was taken (a locked widget, a button and the like are not).</returns>
    public bool Activate(FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (_disposed || !IsEditableKind(field.Kind) || field.Lock != FormLock.None)
        {
            return false;
        }

        if (_fillAllowed() == false)
        {
            ShowForbiddenOnce();
            return true;
        }

        // A draft that is still open (its text is refused) keeps the press: nothing else happens until it is fixed or dropped. It is shown
        // (scrolled to, focused) so its hint says why, even when it was hidden because its field is out of sight.
        if (_draft is not null && _draft.Field.Key != field.Key)
        {
            CommitOrPark();
            if (_draft is not null)
            {
                RevealDraft();
                return true;
            }
        }
        else if (_draft is not null)
        {
            RevealDraft();
            return true;
        }

        _focus.Set(field.Key);
        switch (field.Kind)
        {
            case FormFieldKind.CheckBox:
                _chain = ApplyAsync(_chain, field, current => FormFieldState.OfChecked(current.State.Checked != true), pending: _tab.Annotations.BeginPending());
                break;
            case FormFieldKind.RadioButton:
                if (field.ExportValue is { } export)
                {
                    _chain = ApplyAsync(_chain, field, _ => FormFieldState.OfText(export), pending: _tab.Annotations.BeginPending());
                }

                break;
            default:
                Open(field);
                break;
        }

        return true;
    }

    /// <summary>
    /// Tab (<paramref name="forward"/>) or Shift+Tab in an open editor: ends it like Enter does, then the form focus moves on (back). A refused
    /// text stays in its editor and the focus stays too.
    /// </summary>
    public void CommitAndMove(bool forward)
    {
        if (_draft is { } draft)
        {
            TryCommit(draft, advance: true, forward);

            // A refused text stays: show it, so the hint explains why the key did not move on.
            if (_draft is not null)
            {
                RevealDraft();
            }
        }
    }

    /// <summary>Whether an editor is open that is not on screen: hidden (its field is out of sight, a tool is active) or without the slot of the view.</summary>
    public bool HasHiddenDraft => _draft is { } d && (!d.Editor.IsVisible || !ReferenceEquals(_view.Field, d.Editor));

    /// <summary>
    /// Shows the open editor: scrolls its field into view, puts it in the slot of the view when that is free and gives it the keyboard focus, so
    /// its hint says why the draft stays. Used wherever a refused draft stops an action. Does nothing without an editor.
    /// </summary>
    /// <returns>Whether there is an editor.</returns>
    public bool RevealDraft()
    {
        if (_draft is not { } draft)
        {
            return false;
        }

        // While an annotate tool is active the fields are inert and the slot belongs to the tool: there is nothing to show, and a scroll that
        // shows nothing would only move the page.
        if (_tools.Tool != AnnotateTool.None)
        {
            return true;
        }

        _view.Reveal(draft.Field.Key.Page, draft.Field.Bounds);
        if (ReferenceEquals(_draft, draft))
        {
            Show(draft);
        }

        PostLayoutAndFocus(draft);
        return true;
    }

    // After the dispatcher has settled the draft is laid out once more and gets the focus, whatever the order of the callers that touched the slot.
    private void PostLayoutAndFocus(Draft draft) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (ReferenceEquals(_draft, draft) && draft.Editor.IsVisible && ReferenceEquals(_view.Field, draft.Editor))
                {
                    Layout(draft);
                    draft.Editor.FocusInput();
                }
            },
            DispatcherPriority.Input);

    /// <summary>Ends an open editor like <see cref="CommitOrPark"/>; a draft that stays (its text is refused) is shown and the caller is told to stop.</summary>
    /// <returns><see langword="true"/> when no draft is left and the caller may go on.</returns>
    public bool EndOrReveal()
    {
        CommitOrPark();
        if (_draft is null)
        {
            return true;
        }

        RevealDraft();
        return false;
    }

    /// <summary>Ends an open editor like a click elsewhere does: a valid text is written, an unchanged one closes, a refused one stays.</summary>
    public void CommitOrPark()
    {
        if (_draft is { } draft)
        {
            TryCommit(draft, advance: false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Mover.Dispose();
        _view.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        _view.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _view.RemoveHandler(InputElement.PointerExitedEvent, OnExited);
        _view.ViewChanged -= OnViewChanged;
        _view.FieldChanged -= OnFieldSlotChanged;
        _tools.Changed -= OnToolsChanged;
        _tab.Annotations.FlushRequested -= OnFlushRequested;
        _discardRegistration.Dispose();
        Close();
        ClearTip();
    }

    private static bool IsEditableKind(FormFieldKind kind) =>
        kind is FormFieldKind.Text or FormFieldKind.CheckBox or FormFieldKind.RadioButton or FormFieldKind.ComboBox or FormFieldKind.ListBox;

    private bool Live => !_disposed && _tools.Tool == AnnotateTool.None && _forms.HasForm;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        // Touch scrolls and zooms the page as in 2.2; a press on the editor, the badge, a scroll bar or while a card is open is not ours.
        if (!Live || e.Pointer.Type == PointerType.Touch || _view.IsInField(e.Source) || _view.IsInBadge(e.Source) || _dialogs.IsOpen)
        {
            return;
        }

        var point = e.GetCurrentPoint(_view);
        if (!point.Properties.IsLeftButtonPressed || _view.IsOverScrollBar(point.Position))
        {
            return;
        }

        if (_view.PageAt(point.Position) is not { } spot || _forms.FieldAt(spot.Page, spot.X, spot.Y) is not { } field || !Activate(field))
        {
            return;
        }

        e.Handled = true;
        if (_draft is null)
        {
            _view.Focus();
        }
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!Live)
        {
            ClearTip();
            return;
        }

        var position = e.GetPosition(_view);
        if (_view.IsInField(e.Source) || _view.IsOverScrollBar(position) || _view.PageAt(position) is not { } spot
            || _forms.FieldAt(spot.Page, spot.X, spot.Y) is not { } field || TipOf(field) is not { } tip)
        {
            ClearTip();
            return;
        }

        if (_tipFor != field.Key)
        {
            // The tip is the one of the widget under the pointer; the tooltip service of the view shows it (after its delay) because the pointer is over the view.
            ClearTip();
            _tipFor = field.Key;
            ToolTip.SetTip(_view, tip);
        }
    }

    private void OnExited(object? sender, PointerEventArgs e) => ClearTip();

    private void ClearTip()
    {
        if (_tipFor is null)
        {
            return;
        }

        _tipFor = null;
        ToolTip.SetTip(_view, null);
    }

    /// <summary>The reason a widget is marked: a lock names why, a star says the field is required and still empty; null for a widget with neither.</summary>
    private string? TipOf(FormField field)
    {
        if (FormAutomation.LockText(field) is { } locked)
        {
            return locked;
        }

        if (_fillAllowed() == false && IsEditableKind(field.Kind))
        {
            return PdfStrings.FormFillForbidden;
        }

        return FormMarkers.IsMarkedRequired(field) ? PdfStrings.FormTipRequired : null;
    }

    private void Open(FormField field)
    {
        if (!_view.FirstSharpPageShown || FormEditorLayout.RectOf(_view, field) is null)
        {
            return;
        }

        var editor = new FormFieldEditor(field);
        var draft = new Draft(field, editor, OriginalTextOf(field, editor));
        _draft = draft;
        editor.CommitRequested += advance => TryCommit(draft, advance);
        editor.DiscardRequested += () => Close(draft);
        editor.FocusLeft += () => OnFocusLeft(draft);
        editor.TextEdited += () => OnTextEdited(draft);
        editor.ChoiceMade += () => OnChoiceMade(draft);
        _view.Field = editor;
        Layout(draft);
        Dispatcher.UIThread.Post(
            () =>
            {
                if (ReferenceEquals(_draft, draft))
                {
                    editor.FocusInput();
                }
            },
            DispatcherPriority.Input);
    }

    // The text the draft starts from, as the field has it: for an editable combo box the label of the chosen option or the typed text.
    private static string? OriginalTextOf(FormField field, FormFieldEditor editor)
    {
        if (field.Kind == FormFieldKind.ComboBox && editor.IsTextDraft)
        {
            return field.State.SelectedOptions.Count > 0 && field.State.SelectedOptions[0] is var i && i >= 0 && i < field.Options.Count
                ? field.Options[i].Label
                : field.State.Text ?? string.Empty;
        }

        return editor.DraftText;
    }

    private void Layout(Draft draft)
    {
        if (FormEditorLayout.RectOf(_view, draft.Field) is not { } rect)
        {
            return;
        }

        draft.Editor.Place(rect.Width, rect.Height, FormEditorLayout.FontSize(draft.Field, FormEditorLayout.ScaleOf(rect, draft.Field)));
        _view.FieldRect = rect;
    }

    // A text the field takes is written and the editor closes; an unchanged one closes; a refused one stays with the reason in the hint and
    // turns the pending handle into an unresolved one.
    private void TryCommit(Draft draft, bool advance, bool forward = true)
    {
        if (!ReferenceEquals(_draft, draft))
        {
            return;
        }

        var state = draft.Editor.ReadState();
        if (state is null)
        {
            // Nothing chosen and nothing typed: when the field had a value that is an edit (the text was cleared) and the empty value is
            // written (or refused visibly); a field that was empty already is unchanged.
            if (!draft.Editor.IsTextDraft || !draft.HadValue)
            {
                Close(draft);
                MoveOnIf(advance, forward);
                return;
            }

            state = FormFieldState.OfText(string.Empty);
        }

        if (state.Text is { } text && draft.Field.Kind is FormFieldKind.Text or FormFieldKind.ComboBox)
        {
            // Unchanged comes first: a document may hold a value the rules refuse (other characters, too long, a line break in one line),
            // and leaving it as it is is no error and no draft.
            if (string.Equals(text, draft.OriginalText, StringComparison.Ordinal))
            {
                Close(draft);
                MoveOnIf(advance, forward);
                return;
            }

            var check = FormValueRules.CheckText(draft.Field, text);
            if (!check.Accepted)
            {
                draft.Editor.ShowHint(FormFieldEditor.MessageOf(FormErrors.FromCheck(check)));
                draft.Park(_tab.Annotations);
                return;
            }
        }

        var pending = draft.TakePending() ?? _tab.Annotations.BeginPending();
        Close(draft);
        _chain = ApplyAsync(_chain, draft.Field, _ => state, pending);
        MoveOnIf(advance, forward);
    }

    private void OnTextEdited(Draft draft)
    {
        if (!ReferenceEquals(_draft, draft))
        {
            return;
        }

        // The first change makes the tab count as changed; looking at a field and leaving it does not.
        if (draft.NeedsPending(draft.Editor.DraftText))
        {
            draft.BeginPending(_tab.Annotations);
        }

        // The hint is live: a text the field will refuse says so while it is typed, and nothing is written.
        if (draft.Editor.DraftText is { } text && draft.Field.Kind is FormFieldKind.Text or FormFieldKind.ComboBox
            && !string.Equals(text, draft.OriginalText, StringComparison.Ordinal)
            && FormValueRules.CheckText(draft.Field, text) is { Accepted: false } check)
        {
            draft.Editor.ShowHint(FormFieldEditor.MessageOf(FormErrors.FromCheck(check)));
        }
    }

    private void OnChoiceMade(Draft draft)
    {
        if (!ReferenceEquals(_draft, draft) || draft.Editor.ReadState() is not { } state)
        {
            return;
        }

        var pending = _tab.Annotations.BeginPending();
        if (draft.Editor.Input is ListBox)
        {
            // The list stays open for the next choice of a multi-select list; it closes when the focus leaves or the write is refused.
            _chain = ApplyAsync(_chain, draft.Field, _ => state, pending, onRefused: () => Close(draft));
            return;
        }

        Close(draft);
        _chain = ApplyAsync(_chain, draft.Field, _ => state, pending);
    }

    private void OnFocusLeft(Draft draft)
    {
        if (ReferenceEquals(_draft, draft) && !draft.Editor.IsKeyboardFocusWithin)
        {
            if (draft.Editor.Input is ListBox || draft.Editor.IsTextDraft)
            {
                TryCommit(draft, advance: false);
            }
            else
            {
                Close(draft);
            }
        }
    }

    // Enter in a single-line field (Tab, Shift+Tab in any editor) commits and moves the form focus on (back); where the next page is not read yet
    // it is read first.
    private void MoveOnIf(bool advance, bool forward = true)
    {
        if (advance && !_disposed)
        {
            _ = MoveOnAsync(forward);
        }
    }

    private async Task MoveOnAsync(bool forward)
    {
        try
        {
            await Mover.MoveAsync(forward);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The focus stays where it is.
        }
    }

    // Writes the values one after the other in the order they were made. Never throws: the handle is always released.
    private async Task ApplyAsync(Task previous, FormField field, Func<FormField, FormFieldState?> stateOf, IDisposable pending, Action? onRefused = null)
    {
        string? failure = null;
        try
        {
            await previous;
            var current = _forms.FieldsOn(field.Key.Page).FirstOrDefault(f => f.Key == field.Key) ?? field;
            if (stateOf(current) is { } state && await _forms.SetValueAsync(field, state) is FormEditResult.Refused refused)
            {
                failure = FormFieldEditor.MessageOf(refused);
                onRefused?.Invoke();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The document went away or the work was canceled: nothing more to do.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = PdfStrings.FormInvalid;
        }
        finally
        {
            pending.Dispose();
        }

        if (failure is not null && !_disposed)
        {
            _ = ShowNoticeAsync(failure);
        }
    }

    private void ShowForbiddenOnce()
    {
        // The card comes once; the hover text of every field keeps saying it.
        if (!_forbiddenShown)
        {
            _forbiddenShown = true;
            _ = ShowNoticeAsync(PdfStrings.FormFillForbidden);
        }
    }

    private async Task ShowNoticeAsync(string message)
    {
        try
        {
            await _dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.FormCardTitle, message, () => done(true)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The card could not be shown; the value was not written either way.
        }
    }

    // Closes the editor and drops the draft with whatever it still holds.
    private void Close(Draft? draft = null)
    {
        if (_draft is not { } open || (draft is not null && !ReferenceEquals(open, draft)))
        {
            return;
        }

        _draft = null;
        var had = open.Editor.IsKeyboardFocusWithin;
        if (ReferenceEquals(_view.Field, open.Editor))
        {
            _view.FieldRect = default;
            _view.Field = null;
        }

        open.Release();
        if (had && !_disposed)
        {
            _view.Focus();
        }
    }

    private void OnViewChanged()
    {
        if (_draft is not { } draft)
        {
            return;
        }

        // A field scrolled out of sight: a valid text is written and the editor is gone; a refused one stays, hidden until its field is back
        // (it stays an unresolved change).
        if (_tools.Tool != AnnotateTool.None)
        {
            return;
        }

        if (FormEditorLayout.RectOf(_view, draft.Field) is not { } rect || !FormEditorLayout.IsInView(_view, rect))
        {
            TryCommit(draft, advance: false);
            if (_draft is { } kept)
            {
                kept.Editor.IsVisible = false;
            }

            return;
        }

        Show(draft);
    }

    private void Show(Draft draft)
    {
        if (_view.Field is null || ReferenceEquals(_view.Field, draft.Editor))
        {
            _view.Field = draft.Editor;
            draft.Editor.IsVisible = true;
            Layout(draft);
        }
    }

    // The slot of the view was freed (the text box field closed): a refused draft that waited for it comes back when its field is in view.
    private void OnFieldSlotChanged()
    {
        if (_draft is { } draft && HasHiddenDraft && _tools.Tool == AnnotateTool.None && _view.Field is null
            && FormEditorLayout.RectOf(_view, draft.Field) is { } rect && FormEditorLayout.IsInView(_view, rect))
        {
            Show(draft);
            PostLayoutAndFocus(draft);
        }
    }

    private void OnToolsChanged()
    {
        ClearTip();
        if (_draft is not { } draft)
        {
            return;
        }

        if (_tools.Tool != AnnotateTool.None)
        {
            // The field edit and the text box edit share the slot of the view: a valid text is written first, a refused one waits hidden.
            TryCommit(draft, advance: false);
            if (_draft is { } kept)
            {
                kept.Editor.IsVisible = false;
            }
        }
        else
        {
            Show(draft);
        }
    }

    private void OnFlushRequested()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CommitOrPark();
        }
        else
        {
            Dispatcher.UIThread.Post(CommitOrPark);
        }
    }

    // The history commands while an editor is open: the draft ends like with Esc and the history stays as it is.
    private bool DiscardForHistory()
    {
        if (_draft is not { } draft)
        {
            return false;
        }

        // A refused draft the user cannot see is not dropped behind their back: it is shown, and the history stays as it is. While an annotate
        // tool is active it cannot be shown, so it must not hold the undo either: it ends, and the undo goes on.
        if (HasHiddenDraft && draft.IsParked && _tools.Tool == AnnotateTool.None)
        {
            RevealDraft();
            return true;
        }

        var hiddenByTool = _tools.Tool != AnnotateTool.None && HasHiddenDraft;
        Close();
        return !hiddenByTool;
    }

    private sealed class Draft(FormField field, FormFieldEditor editor, string? originalText)
    {
        private IDisposable? _pending;
        private IDisposable? _unresolved;

        public FormField Field { get; } = field;

        public FormFieldEditor Editor { get; } = editor;

        /// <summary>Whether the field had a value when the editor opened (a chosen option or a non-empty text).</summary>
        public bool HadValue { get; } = field.State.SelectedOptions.Count > 0 || !string.IsNullOrEmpty(field.State.Text);

        /// <summary>The text the draft started with (line breaks as LF), or <see langword="null"/> for a draft that is no text.</summary>
        public string? OriginalText { get; } = originalText is null ? null : FormFieldEditor.Normalize(originalText);

        /// <summary>Whether the text differs from the start text and no handle is held yet.</summary>
        public bool NeedsPending(string? text) =>
            _pending is null && _unresolved is null && text is not null && !string.Equals(text, OriginalText, StringComparison.Ordinal);

        /// <summary>Whether the text was refused and the draft is held as an unresolved change.</summary>
        public bool IsParked => _unresolved is not null;

        public void BeginPending(AnnotationSession session) => _pending ??= session.BeginPending();

        /// <summary>Hands the pending handle to the caller (once); null afterwards.</summary>
        public IDisposable? TakePending()
        {
            var taken = _pending;
            _pending = null;
            return taken;
        }

        /// <summary>Turns the pending handle (waited for) into an unresolved one (kept dirty, not waited for): the text is refused.</summary>
        public void Park(AnnotationSession session)
        {
            _unresolved ??= session.BeginUnresolved();
            _pending?.Dispose();
            _pending = null;
        }

        /// <summary>Ends whatever the draft still holds.</summary>
        public void Release()
        {
            _pending?.Dispose();
            _pending = null;
            _unresolved?.Dispose();
            _unresolved = null;
        }
    }
}
