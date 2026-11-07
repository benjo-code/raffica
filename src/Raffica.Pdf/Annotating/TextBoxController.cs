using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Text;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The text box tool. A drag on a page draws the box (at least 16 points in both directions; anything smaller is a click and makes the
/// default 160 x 48 point box at the press point, shifted onto the page); the editing field (<see cref="TextBoxEditor"/>) then opens over the
/// box. Ctrl+Enter or leaving the field makes the text box, Esc drops the draft (the mode stays on). Empty text makes nothing; text the box
/// cannot show (<see cref="TextBoxSpec.CheckText"/>, the rules are Core's) keeps the field open with a hint that names up to eight
/// characters. The box grows downward to fit the text, at most to the page edge, and never shrinks.
/// </summary>
/// <remarks>
/// A draft holds a pending handle of the tab from the moment the field opens until the text box is made, the draft is dropped or this
/// controller is disposed, so the tab counts as changed and closing it never loses the text; <see cref="AnnotationSession.FlushRequested"/>
/// (save, undo, close) ends the draft first: a valid text is made, an invalid one is dropped. An own text box is edited with
/// <see cref="EditAsync"/> (the select tool calls it); another program's cannot be. Used on the UI thread only.
/// </remarks>
internal sealed class TextBoxController : IToolController
{
    /// <summary>The smallest side, in points, of a box that was dragged; a smaller drag is a click.</summary>
    public const double MinDragSide = 16;

    // The dispatcher of the UI thread this was made on: the flush may be requested from any thread, so the check and the post use it, never
    // the static Dispatcher.UIThread.
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly ToolState _tools;
    private readonly DialogQueue _dialogs;
    private readonly StrokePreview _preview = new();
    private Task _chain = Task.CompletedTask;
    private Draft? _draft;
    private bool _active;
    private int _page;
    private PdfPageSize _size;
    private DisplayPoint _anchor;
    private DisplayPoint _current;
    private bool _disposed;
    private readonly IDisposable _discardRegistration;

    public TextBoxController(PdfTab tab, PageView view, ToolState tools, DialogQueue dialogs)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(dialogs);
        _tab = tab;
        _view = view;
        _tools = tools;
        _dialogs = dialogs;
        _preview.Changed += OnPreviewChanged;
        _view.ViewChanged += OnViewChanged;
        _tab.Annotations.FlushRequested += OnFlushRequested;
        _discardRegistration = _tab.Annotations.AddDiscardHandler(DiscardForHistory);
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Whether the editing field is open.</summary>
    public bool IsEditing => _draft is not null;

    /// <summary>The editing field, or <see langword="null"/> while none is open.</summary>
    public TextBoxEditor? Editor => _draft?.Editor;

    /// <summary>The box of the draft in page points, or <see langword="null"/> while no field is open.</summary>
    public PageRect? DraftBox => _draft?.Box;

    /// <summary>The task that ends when every text box committed so far is made (for tests).</summary>
    public Task Completion => _chain;

    public bool Handles(AnnotateTool tool) => tool == AnnotateTool.TextBox;

    public void Render(DrawingContext context, PageView view) => _preview.Render(context, view);

    public Cursor? CursorAt(Point point) => TextInteraction.IBeamCursor;

    public bool Key(KeyEventArgs e) => false;

    public void Press(Point point, int clickCount, KeyModifiers modifiers)
    {
        _active = false;
        _preview.Clear();
        if (_draft is not null)
        {
            // A press away from the field normally took the focus from it and so made the text box already; a draft whose text is
            // refused stays open and this press does nothing else.
            TryCommit();
            if (_draft is not null)
            {
                return;
            }
        }

        if (_disposed || !Handles(_tools.Tool) || !_view.FirstSharpPageShown || _view.PageAt(point) is not { } spot
            || _view.SizeOfPage(spot.Page) is not { } size)
        {
            return;
        }

        _active = true;
        _page = spot.Page;
        _size = size;
        _anchor = _current = Clamp(spot.X, spot.Y);
        UpdatePreview();
    }

    public void Move(Point point, KeyModifiers modifiers)
    {
        if (!_active)
        {
            return;
        }

        Track(point);
        UpdatePreview();
    }

    public void Release(Point point, KeyModifiers modifiers)
    {
        if (!_active)
        {
            return;
        }

        Track(point);
        _active = false;
        _preview.Clear();
        if (_disposed)
        {
            return;
        }

        var dragged = BoxOf(_anchor, _current);
        var box = dragged.Width >= MinDragSide && dragged.Height >= MinDragSide ? dragged : DefaultBox(_anchor, _size);
        var style = _tools.StyleOf(AnnotateTool.TextBox);
        Open(_page, _size, box, TextBoxSpec.FontSizeOf(style.Width), DrawPalette.ColorOf(style.Color), null, string.Empty);
    }

    /// <summary>
    /// Opens the field on an own text box that is on screen, to change its text. Another program's text box (not editable by Core), one
    /// that is not on screen or a missing one is refused: no field opens.
    /// </summary>
    /// <param name="locator">The text box.</param>
    /// <returns>Whether the field opened.</returns>
    public async Task<bool> EditAsync(AnnotationLocator locator)
    {
        if (_disposed || _tab.Session.Document is not { } document || _view.SizeOfPage(locator.Page) is not { } size)
        {
            return false;
        }

        CommitOrDrop();
        PdfAnnotation? found;
        try
        {
            var all = await document.GetAnnotationsAsync(locator.Page);
            found = all.FirstOrDefault(a => locator.Name is { } name ? a.Name == name : a.Index == locator.Index);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }

        if (_disposed || _draft is not null || found is not { Kind: AnnotationKind.TextBox, Editable: true, GeometryEditable: true, TextBox: { } spec })
        {
            return false;
        }

        return Open(locator.Page, size, spec.Box, spec.FontSize, spec.Color, locator, spec.Text);
    }

    public bool Cancel()
    {
        var was = _active || _draft is not null;
        _active = false;
        _preview.Clear();
        Close();
        return was;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _active = false;
        _preview.Clear();
        Close();
        _preview.Changed -= OnPreviewChanged;
        _view.ViewChanged -= OnViewChanged;
        _tab.Annotations.FlushRequested -= OnFlushRequested;
        _discardRegistration.Dispose();
    }

    /// <summary>
    /// Ends the draft on its own account (the tool changes, the page scrolls away, a save, undo or close is about to wait for it): a valid
    /// text is made, an empty one is dropped. A refused text is never dropped here: the field stays open with its hint and the draft is
    /// parked as an unresolved change (the tab stays dirty, nothing waits for it).
    /// </summary>
    public void CommitOrDrop()
    {
        if (_draft is not { } draft)
        {
            return;
        }

        var check = TextBoxSpec.CheckText(TextBoxSpec.NormalizeText(draft.Editor.Field.Text ?? string.Empty));
        if (check.Problem == TextBoxTextProblem.Empty)
        {
            Close();
        }
        else if (check.Problem == TextBoxTextProblem.None)
        {
            TryCommit();
        }
        else
        {
            draft.Editor.ShowHint(TextBoxEditor.MessageOf(check));
            draft.Park(_tab.Annotations);
        }
    }

    /// <summary>Drops a gesture in progress (a drag); a draft is left alone.</summary>
    public void CancelGesture()
    {
        _active = false;
        _preview.Clear();
    }

    // The box a click makes: 160 x 48 points with its top left at the press point, moved to fit on the page (a page narrower or lower
    // than the box gets a box as wide or high as the page).
    private static PageRect DefaultBox(DisplayPoint at, PdfPageSize size)
    {
        var width = Math.Min(TextBoxSpec.DefaultWidth, size.Width);
        var height = Math.Min(TextBoxSpec.DefaultHeight, size.Height);
        var left = Math.Clamp(at.X, 0, size.Width - width);
        var top = Math.Clamp(at.Y, 0, size.Height - height);
        return new PageRect(left, top, left + width, top + height);
    }

    private static PageRect BoxOf(DisplayPoint a, DisplayPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private bool Open(int page, PdfPageSize size, PageRect box, double fontSize, AnnotationColor color, AnnotationLocator? existing, string text)
    {
        if (_disposed || _draft is not null || _view.ToView(page, box) is null)
        {
            return false;
        }

        var editor = new TextBoxEditor();
        editor.SetColor(color);
        editor.SetStartText(text);
        var draft = new Draft(page, size, box, fontSize, color, existing, text, editor, _tab.Annotations.BeginPending());
        _draft = draft;
        editor.CommitRequested += OnCommitRequested;
        editor.DiscardRequested += OnDiscardRequested;
        editor.FocusLeft += OnFocusLeft;
        editor.TextEdited += OnTextEdited;
        _view.Field = editor;
        Layout();
        Dispatcher.UIThread.Post(
            () =>
            {
                if (ReferenceEquals(_draft, draft))
                {
                    editor.Field.Focus();
                }
            },
            DispatcherPriority.Input);
        return true;
    }

    // Places the field over the box and grows the box (downward, to the page edge at most) when the text needs more room.
    private void Layout()
    {
        if (_draft is not { } draft)
        {
            return;
        }

        if (_view.ToView(draft.Page, draft.Box) is not { } rect || rect.Width <= 0 || draft.Box.Width <= 0)
        {
            return;
        }

        var scale = rect.Width / draft.Box.Width;
        var room = Math.Max(draft.Box.Height, draft.Size.Height - draft.Box.Top);
        draft.Editor.SetSize(rect.Width, rect.Height, room * scale, draft.FontSize * scale);
        var needed = draft.Editor.NeededHeight() / scale;
        if (needed > draft.Box.Height + 0.01)
        {
            draft.Box = new PageRect(draft.Box.Left, draft.Box.Top, draft.Box.Right, draft.Box.Top + Math.Min(needed, room));
            rect = _view.ToView(draft.Page, draft.Box) ?? rect;
            draft.Editor.SetSize(rect.Width, rect.Height, room * scale, draft.FontSize * scale);
        }

        _view.FieldRect = rect;
    }

    // Makes the text box of the draft when its text is fine and closes the field; empty text closes it; refused text keeps it open
    // with a hint.
    private void TryCommit()
    {
        if (_draft is not { } draft)
        {
            return;
        }

        var text = TextBoxSpec.NormalizeText(draft.Editor.Field.Text ?? string.Empty);
        var check = TextBoxSpec.CheckText(text);
        if (check.Problem == TextBoxTextProblem.Empty)
        {
            Close();
            return;
        }

        if (check.Problem != TextBoxTextProblem.None)
        {
            draft.Editor.ShowHint(TextBoxEditor.MessageOf(check));
            return;
        }

        var pending = draft.TakePending() ?? _tab.Annotations.BeginPending();
        var unchanged = draft.Existing is not null && string.Equals(text, TextBoxSpec.NormalizeText(draft.Original), StringComparison.Ordinal);
        var spec = new TextBoxSpec(draft.Box, text, draft.FontSize, draft.Color);
        var existing = draft.Existing;
        var page = draft.Page;
        Close();
        if (unchanged)
        {
            pending?.Dispose();
            return;
        }

        draft.ReleaseUnresolved();
        _chain = ApplyAsync(_chain, page, spec, existing, _tools.Author, pending);
    }

    // Closes the field and drops the draft with whatever it still holds.
    private void Close()
    {
        if (_draft is not { } draft)
        {
            return;
        }

        _draft = null;
        var editor = draft.Editor;
        var had = editor.Field.IsFocused;
        editor.CommitRequested -= OnCommitRequested;
        editor.DiscardRequested -= OnDiscardRequested;
        editor.FocusLeft -= OnFocusLeft;
        editor.TextEdited -= OnTextEdited;
        if (ReferenceEquals(_view.Field, editor))
        {
            // The rectangle first: clearing the slot tells the listeners that it is free, and one of them may put its own element there at
            // once, laid out at its own rectangle, which a later reset would wipe.
            _view.FieldRect = default;
            _view.Field = null;
        }

        draft.TakePending()?.Dispose();
        draft.ReleaseUnresolved();
        if (had && !_disposed)
        {
            _view.Focus();
        }
    }

    // Applies the text boxes one after the other, in the order they were committed. Never throws: the handle is always released.
    private async Task ApplyAsync(Task previous, int page, TextBoxSpec spec, AnnotationLocator? existing, string? author, IDisposable? pending)
    {
        string? failure = null;
        try
        {
            await previous;
            if (existing is { } locator)
            {
                await _tab.Annotations.Editor.ReplaceTextBoxAsync(locator, spec);
            }
            else
            {
                await _tab.Annotations.Editor.ApplyTextBoxAsync(page, spec, author);
            }
        }
        catch (OperationCanceledException)
        {
            // The document went away or the work was canceled: nothing more to do.
        }
        catch (PdfAnnotationLimitException)
        {
            failure = PdfStrings.AnnotateLimitReached;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = PdfStrings.AnnotateFailed;
        }
        finally
        {
            pending?.Dispose();
        }

        if (failure is not null && !_disposed)
        {
            _ = ShowNoticeAsync(failure);
        }
    }

    private async Task ShowNoticeAsync(string message)
    {
        try
        {
            await _dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.AnnotateCardTitle, message, () => done(true)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The card could not be shown; the text box was not made either way.
        }
    }

    private DisplayPoint Clamp(double x, double y) =>
        new(Math.Clamp(double.IsFinite(x) ? x : 0, 0, _size.Width), Math.Clamp(double.IsFinite(y) ? y : 0, 0, _size.Height));

    private void Track(Point point)
    {
        if (_view.PageBounds(_page) is not { } rect || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        _current = Clamp((point.X - rect.X) * _size.Width / rect.Width, (point.Y - rect.Y) * _size.Height / rect.Height);
    }

    private void UpdatePreview()
    {
        var style = _tools.StyleOf(AnnotateTool.TextBox);
        _preview.ShowBox(_page, DrawPalette.ColorOf(style.Color), 1, BoxOf(_anchor, _current), ellipse: false);
    }

    private void OnCommitRequested() => TryCommit();

    private void OnDiscardRequested() => Close();

    private void OnFocusLeft()
    {
        // The field is already gone when it is closed by us (Close clears the draft first), so only a user leaving the field commits.
        if (_draft is { } draft && !draft.Editor.Field.IsFocused)
        {
            TryCommit();
        }
    }

    private void OnTextEdited() => Layout();

    private void OnViewChanged()
    {
        if (_draft is not { } draft)
        {
            return;
        }

        // A page that scrolled out of the view: a valid text is made, an empty one dropped; a refused one is kept, hidden until its page is
        // back (it stays an unresolved change).
        if (_view.ToView(draft.Page, draft.Box) is not { } rect || !rect.Intersects(new Rect(_view.Bounds.Size)))
        {
            CommitOrDrop();
            if (_draft is not null)
            {
                _draft.Editor.IsVisible = false;
            }

            return;
        }

        draft.Editor.IsVisible = true;
        Layout();
    }

    private void OnFlushRequested()
    {
        if (_dispatcher.CheckAccess())
        {
            CommitOrDrop();
        }
        else
        {
            _dispatcher.Post(CommitOrDrop);
        }
    }

    // The history commands while the field is open: the draft ends like with Esc and the history stays as it is.
    private bool DiscardForHistory()
    {
        if (_draft is null)
        {
            return false;
        }

        Close();
        return true;
    }

    private void OnPreviewChanged() => Changed?.Invoke();

    private sealed class Draft(int page, PdfPageSize size, PageRect box, double fontSize, AnnotationColor color, AnnotationLocator? existing, string original, TextBoxEditor editor, IDisposable pending)
    {
        private IDisposable? _pending = pending;
        private IDisposable? _unresolved;

        public int Page { get; } = page;

        public PdfPageSize Size { get; } = size;

        public PageRect Box { get; set; } = box;

        public double FontSize { get; } = fontSize;

        public AnnotationColor Color { get; } = color;

        public AnnotationLocator? Existing { get; } = existing;

        public string Original { get; } = original;

        public TextBoxEditor Editor { get; } = editor;

        /// <summary>Hands the pending handle to the caller (once); null afterwards.</summary>
        /// <summary>Turns the pending handle (waited for) into an unresolved one (kept dirty, not waited for): the text is refused.</summary>
        public void Park(AnnotationSession session)
        {
            if (_pending is null)
            {
                return;
            }

            _unresolved ??= session.BeginUnresolved();
            _pending.Dispose();
            _pending = null;
        }

        /// <summary>Ends the unresolved state (the draft was made, dropped or closed).</summary>
        public void ReleaseUnresolved()
        {
            _unresolved?.Dispose();
            _unresolved = null;
        }

        public IDisposable? TakePending()
        {
            var taken = _pending;
            _pending = null;
            return taken;
        }
    }
}
