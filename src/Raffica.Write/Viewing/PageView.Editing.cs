using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Shell;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write.Viewing;

/// <summary>Which side of a line break the caret belongs to when one offset ends a wrapped line and starts the next.</summary>
internal enum CaretAffinity
{
    /// <summary>The start of the later line (the default, and what the layout's caret geometry says).</summary>
    Downstream,

    /// <summary>The end of the earlier line (a click past the end of a wrapped line).</summary>
    Upstream,
}

/// <summary>
/// The editing side of the page view: the caret and the selection of the tab's <see cref="EditSession"/> drawn over the pages, the pointer rules (click, Shift+click,
/// drag with auto-scroll, double click for a word, triple click for a paragraph, double click in a header or footer to enter it) and keeping the caret in view.
/// </summary>
/// <remarks>
/// Nothing here exists before the first click: no session, no timer and no overlay work (the overlay draws nothing without a selection). The caret and the selection are
/// drawn by Avalonia in <see cref="EditOverlay"/> over the page draw operations, never inside them, and read the active layout (<c>_builtLayout</c>: the flow layout in the
/// writing view, never the tab's stale page layout) with <see cref="DocumentLayout.TryGetPage"/>-safe calls. Rules:
/// <list type="bullet">
/// <item>Affinity: the layout has none (an offset that ends a wrapped line is the start of the next one). The view keeps one <see cref="Affinity"/> for the caret it last
/// placed: a click that lands past the end of a wrapped line makes it upstream (the caret is drawn at the end of that line), every other caret, also one moved by typing or
/// keys through the session, is downstream because the affinity is valid only for the position it was set for. Home, End and Up/Down (Task 16) must read it.</item>
/// <item>Header and footer: a double click in the header or footer region of a page (<see cref="DocumentLayout.HitTestHeaderFooter"/>) enters that story; while in it
/// every click in a header or footer region is in a header or footer and a click anywhere else leaves it, Esc leaves it and restores the body selection from before. A single click on header text outside the mode does nothing.</item>
/// <item>A drag stops at the story it started in and at the container of blocks (table cell) it started in: a point beyond leaves the selection at the edge of the container, beside a table
/// for a table that lies in the container, and keeps it unchanged for another story.</item>
/// <item>While a save runs a pointer selection is queued as an operation in arrival order (like typing) and shows when the save ends; while only a prefix is open or the session
/// is faulted a press changes nothing.</item>
/// <item>Blink: 530 ms (Avalonia exposes no system caret blink time; <see cref="TextBox.CaretBlinkIntervalProperty"/> is a per-box property), only while the view has focus, the window
/// is active and a caret exists, restarted (solid) by every caret move; not at all in high contrast (theme variant or Windows contrast preference) or when
/// <see cref="ReduceMotion"/> is set (Avalonia exposes no reduced-motion preference: the app sets the property when it knows one).</item>
/// <item>Nested tables are not entered by caret movement (a limit of the layout's cell scoping); the pointer enters them as any cell.</item>
/// </list>
/// </remarks>
internal sealed partial class PageView
{
    /// <summary>The width of the caret bar (DIPs); drawn at a whole device pixel and at least one.</summary>
    internal const double CaretWidth = 1.5;

    /// <summary>How long the caret is on and off.</summary>
    internal static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(530);

    private const double SelectionOpacity = 0.35;
    private const double RevealMargin = 24;

    private static Cursor? s_textCursor;

    private readonly EditOverlay _overlay;
    private EditSession? _session;
    private DispatcherTimer? _blink;
    private DispatcherTimer? _autoScroll;
    private bool _blinkOn = true;
    private bool _windowActive = true;
    private WindowBase? _window;
    private CaretAffinity _affinity;
    private TextPosition? _affinityFor;
    private int _caretPage = -1;
    private StoryRef? _story;
    private TextRange? _bodySelection;
    private TextPosition? _dragAnchor;
    private object _gesture = new();
    private IList<Block>? _dragContainer;
    private IPointer? _dragPointer;
    private Point _lastPointer;
    private bool _cursorIsText;
    private bool _reduceMotion;

    /// <summary>The caret: the moving end of the selection of the tab's session; null before the first click, while the session is faulted or when the document has no paragraph.</summary>
    public TextPosition? Caret => CurrentSelection()?.End;

    /// <summary>The selection of the tab's session (collapsed for a caret), or null as for <see cref="Caret"/>.</summary>
    public TextRange? Selection => CurrentSelection();

    /// <summary>Which side of a line break the caret belongs to (downstream unless a click placed it at the end of a wrapped line and it has not moved).</summary>
    public CaretAffinity Affinity => _affinityFor is { } at && Caret is { } caret && SamePosition(at, caret) ? _affinity : CaretAffinity.Downstream;

    /// <summary>The header or footer story the view is in (entered by a double click), or null in the body.</summary>
    public StoryRef? ActiveStory => _story;

    /// <summary>When true the caret does not blink (set by the app when the system asks for reduced motion; Avalonia does not report it).</summary>
    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            _reduceMotion = value;
            UpdateBlink();
        }
    }

    /// <summary>True once the blink timer was made (on the first caret).</summary>
    internal bool BlinkTimerCreated => _blink is not null;

    /// <summary>True while the blink timer ticks.</summary>
    internal bool BlinkRunning => _blink?.IsEnabled == true;

    /// <summary>Whether the caret bar is in its drawn phase.</summary>
    internal bool CaretShown => _blinkOn;

    /// <summary>True while the auto-scroll timer of a drag runs.</summary>
    internal bool AutoScrolling => _autoScroll?.IsEnabled == true;

    /// <summary>How many times the overlay layer was rendered (tests).</summary>
    internal int OverlayRenderCount => _overlay.RenderCount;

    /// <summary>True while a drag selection is in progress.</summary>
    internal bool IsDragging => _dragAnchor is not null;

    /// <summary>The place of the caret as an anchor (the top-level block and the offset in its paragraph), or null without a caret in the body.</summary>
    public ViewAnchor? CaretAnchor
    {
        get
        {
            if (Caret is not { } caret || caret.Story.Kind != StoryKind.Body)
            {
                return null;
            }

            var blocks = _tab.Docx.Model.Body.Blocks;
            var index = TextUnits.IndexUnder(blocks, caret.Paragraph);
            return index < 0 ? null : new ViewAnchor(index, ReferenceEquals(blocks[index], caret.Paragraph) ? caret.Offset : 0);
        }
    }

    private void InitializeEditing()
    {
        ActualThemeVariantChanged += (_, _) => UpdateBlink();
        if (_tab.Editor is { } existing)
        {
            AttachSession(existing);
            SyncActiveMode();
        }
    }

    private void ReleaseEditing()
    {
        EndDrag();
        _blink?.Stop();
        _autoScroll?.Stop();
        if (_session is { } session)
        {
            session.Changed -= OnEditChanged;
            session.InputDropped -= OnInputDropped;
            _session = null;
        }

        DetachWindow();
    }

    private void InvalidateAll()
    {
        InvalidateVisual();
        _overlay.InvalidateVisual();
    }

    private TextRange? CurrentSelection()
    {
        if (_tab.Editor is not { } session || session.BlockedReason == EditBlockedReason.Faulted)
        {
            return null;
        }

        return session.Selection;
    }

    private void AttachSession(EditSession session)
    {
        if (ReferenceEquals(_session, session))
        {
            return;
        }

        if (_session is not null)
        {
            _session.Changed -= OnEditChanged;
            _session.InputDropped -= OnInputDropped;
        }

        _session = session;
        session.Changed += OnEditChanged;
        session.InputDropped += OnInputDropped;
    }

    // Input that a save queued was not replayed: the same hint as for a refused key says why.
    private void OnInputDropped(EditBlockedReason reason) => InputRefused?.Invoke(reason);

    // The session says the document or the selection changed (an edit, a replayed pointer selection): the pages may have changed, the caret is solid and in view.
    private void OnEditChanged()
    {
        if (_released)
        {
            return;
        }

        var phases = _tab.Editor?.Phases;
        var stamp = EditPhases.Now(phases);
        EnsureCurrent();
        EditPhases.Lap(phases, EditPhases.ViewCurrent, ref stamp);
        ForgetLostComposition();
        UpdateScrollBars();
        EditPhases.Lap(phases, EditPhases.ViewScrollBars, ref stamp);
        RestartBlink();
        EditPhases.Lap(phases, EditPhases.ViewBlink, ref stamp);
        EnsureCaretVisible();
        EditPhases.Lap(phases, EditPhases.ViewCaret, ref stamp);
        InvalidateAll();
        EditPhases.Lap(phases, EditPhases.ViewInvalidate, ref stamp);
        PublishSelectionState();
        EditPhases.Lap(phases, EditPhases.ViewPublish, ref stamp);
        NotifyViewChanged();
        EditPhases.Lap(phases, EditPhases.ViewNotify, ref stamp);
        NotifyImeText();
        EditPhases.Lap(phases, EditPhases.ViewIme, ref stamp);
    }

    // The session follows the layout of the view that is shown.
    private void SyncActiveMode()
    {
        if (_tab.Editor is { } session && !_tab.IsDisposed && session.ActiveMode != ToLayoutMode(_mode))
        {
            session.ActiveMode = ToLayoutMode(_mode);
        }
    }

    private DocumentLayout? ActiveLayout()
    {
        EnsureCurrent();
        return _builtLayout;
    }

    // The tab can take a caret: the whole model is there, nothing failed.
    private bool CanPlaceCaret =>
        !_released && !_tab.IsDisposed && _runner.FullModelReady && !_runner.IsFaulted && _tab.Editor?.BlockedReason != EditBlockedReason.Faulted;

    // Caret geometry.

    private int PageHint => _caretPage >= 0 ? _caretPage : CurrentPage;

    private CaretBox? CaretBoxOf(DocumentLayout layout, TextPosition position, CaretAffinity affinity)
    {
        var hint = PageHint;
        if (affinity == CaretAffinity.Upstream && UpstreamBox(layout, position, hint) is { } upstream)
        {
            return upstream;
        }

        return layout.Caret(position, hint);
    }

    // The caret at the end of the line before <position> when <position> is where that line ends and the next starts (a wrapped long word, a hyphenated break): found with
    // the layout's own moves (the character before, the end of that line) and the selection boxes of the line. Null when the offset is no such break.
    private static CaretBox? UpstreamBox(DocumentLayout layout, TextPosition position, int hint)
    {
        if (position.Offset <= 0 || layout.Move(position, CaretMove.Left, null, hint) is not { } before
            || !ReferenceEquals(before.Paragraph, position.Paragraph) || before.Offset >= position.Offset
            || layout.Move(before, CaretMove.LineEnd, null, hint) is not { } end
            || !ReferenceEquals(end.Paragraph, position.Paragraph) || end.Offset != position.Offset
            || layout.Caret(before, hint) is not { } a || layout.Caret(position, hint) is not { } b
            || (a.Page == b.Page && a.Top == b.Top))
        {
            return null;
        }

        var boxes = layout.SelectionBoxes(new TextRange(before, position), a.Page);
        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            if (Math.Abs(boxes[i].Y - a.Top) < 0.01f)
            {
                return new CaretBox(a.Page, boxes[i].X + boxes[i].Width, boxes[i].Y, boxes[i].Height);
            }
        }

        return null;
    }

    /// <summary>The caret bar of a position in the coordinates of the view (as drawn, possibly outside the viewport), or null when the layout does not show the position.</summary>
    internal Rect? CaretBounds(TextPosition position, CaretAffinity affinity = CaretAffinity.Downstream)
    {
        EnsureCurrent();
        return _builtLayout is { } layout && CaretBoxOf(layout, position, affinity) is { } box ? BoundsOfBox(box) : null;
    }

    // The bar of a caret box in the coordinates of the view; null when the page of the box is not in the scroller.
    private Rect? BoundsOfBox(CaretBox box)
    {
        if (_scroller is not { } scroller || (uint)box.Page >= (uint)scroller.Count)
        {
            return null;
        }

        _caretPage = box.Page;
        var page = scroller.RectOf(box.Page).Translate(new Vector(LeftShift(scroller), -_offsetY));
        return new Rect(page.X + (box.X * scroller.Scale), page.Y + (box.Top * scroller.Scale), CaretWidth, box.Height * scroller.Scale);
    }

    /// <summary>
    /// The bar of the caret of the session in view coordinates (while an input method composes: the bar inside the preedit, see <see cref="PreeditCaretBounds"/>), or null without a caret. A session caret the layout
    /// cannot show (its page is not laid out: an outline move put it below the view) makes the page of the caret unknown again, so the next edit lays out through the last page in view and not through the page of
    /// a caret that is gone (it would pay the layout of the whole document on the first key after the move).
    /// </summary>
    internal Rect? CaretBarBounds
    {
        get
        {
            if (IsComposing)
            {
                return PreeditCaretBounds;
            }

            if (Caret is not { } caret)
            {
                return null;
            }

            var bar = CaretBounds(caret, Affinity);
            if (bar is null)
            {
                _caretPage = -1;
            }

            return bar;
        }
    }

    /// <summary>Scrolls the least that brings the caret into view (with a margin); a caret the layout does not show yet changes nothing.</summary>
    public void EnsureCaretVisible()
    {
        if (CaretBarBounds is not { } bar)
        {
            return;
        }

        var y = _offsetY;
        var x = _offsetX;
        if (bar.Top < RevealMargin)
        {
            y += bar.Top - RevealMargin;
        }
        else if (bar.Bottom > _viewport.Height - RevealMargin)
        {
            y += bar.Bottom - (_viewport.Height - RevealMargin);
        }

        if (bar.Left < RevealMargin)
        {
            x += bar.Left - RevealMargin;
        }
        else if (bar.Right > _viewport.Width - RevealMargin)
        {
            x += bar.Right - (_viewport.Width - RevealMargin);
        }

        if (x != _offsetX || y != _offsetY)
        {
            ScrollTo(x, y);
        }
    }

    // The anchor of the caret when it is on screen (the view switch keeps the paragraph the user is looking at), else null.
    private ViewAnchor? CaretAnchorIfVisible() =>
        CaretBarBounds is { } bar && bar.Bottom > 0 && bar.Top < _viewport.Height ? CaretAnchor : null;

    // Where an anchor lies at the top of the view: the block, or for an anchor with a character offset the line of that character (a third of the view below the top).
    private double? OffsetOfAnchor(ViewAnchor anchor)
    {
        if (_scroller?.OffsetOf(anchor) is not { } block)
        {
            return null;
        }

        if (anchor.Character > 0 && _builtLayout is { } layout && anchor.Block >= 0 && anchor.Block < _tab.Docx.Model.Body.Blocks.Count
            && _tab.Docx.Model.Body.Blocks[anchor.Block] is Paragraph paragraph
            && layout.Caret(new TextPosition(StoryRef.Body, paragraph, anchor.Character), layout.Locate(anchor.Block)?.Page ?? 0) is { } box
            && (uint)box.Page < (uint)_scroller.Count)
        {
            return _scroller.RectOf(box.Page).Y + (box.Top * _scroller.Scale) - (_viewport.Height / 3) + (EdgeOf(_mode) / 2);
        }

        return block;
    }

    // Pointer.

    private void PressPointer(PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        var right = properties.IsRightButtonPressed;
        if (!properties.IsLeftButtonPressed && !right)
        {
            return;
        }

        var drag = HandlePress(e.GetPosition(this), e.ClickCount, e.KeyModifiers, right);
        if (drag)
        {
            e.Pointer.Capture(this);
            _dragPointer = e.Pointer;
        }

        e.Handled = true;
    }

    /// <summary>
    /// A press of the pointer at a point of the view: places the caret, extends the selection (Shift), selects a word (2 clicks) or a paragraph (3 or more) or enters a header or
    /// footer (2 clicks in its region). Returns true when a drag selection started. Does nothing unless the whole model is loaded and the session did not fail.
    /// </summary>
    internal bool HandlePress(Point point, int clickCount, KeyModifiers modifiers, bool rightButton)
    {
        EndDrag();
        EndCompositionForPointer();
        _gesture = new object();
        if (!CanPlaceCaret || ActiveLayout() is not { } layout || !TryMapPoint(point, out var page, out var x, out var y))
        {
            return false;
        }

        var session = _tab.EnsureEditor();
        AttachSession(session);
        SyncActiveMode();
        var count = rightButton ? 1 : Math.Clamp(clickCount, 1, 3);
        if (ResolveHit(layout, session, page, x, y, count, drag: false, out var entered) is not { } hit)
        {
            return false;
        }

        var position = hit.Position;
        var current = session.Selection;
        if (rightButton && current is { } selected && SelectionContains(selected, position))
        {
            return false;
        }

        if (entered)
        {
            count = 1;
        }

        switch (count)
        {
            case 2:
                Place(session, TextUnits.WordAt(position, ProjectionOf(layout, position), hit.Trailing), CaretAffinity.Downstream);
                return false;
            case 3:
                Place(session, TextUnits.ParagraphAt(position, ProjectionOf(layout, position)), CaretAffinity.Downstream);
                return false;
        }

        var affinity = AffinityFor(layout, hit);
        if (!rightButton && modifiers.HasFlag(KeyModifiers.Shift) && current is { } existing && !entered)
        {
            var anchor = existing.Start;
            BeginDrag(anchor);
            if (Constrain(layout, anchor, hit, y, page) is { } target)
            {
                Place(session, new TextRange(anchor, target), affinity);
            }

            return true;
        }

        Place(session, new TextRange(position, position), affinity);
        if (rightButton)
        {
            return false;
        }

        BeginDrag(position);
        return true;
    }

    // The hit of a press or of a drag move, by the header/footer rule: see the remarks of the class. Only a press changes the mode: a drag keeps it (Constrain clamps the
    // selection to the story) and leaving is explicit (a press elsewhere, Esc).
    private TextHit? ResolveHit(DocumentLayout layout, EditSession session, int page, float x, float y, int count, bool drag, out bool entered)
    {
        entered = false;
        var hit = layout.HitTest(page, x, y);
        var region = layout.HitTestHeaderFooter(page, x, y);
        if (_story is { } story)
        {
            if (region is { } inRegion)
            {
                if (!drag)
                {
                    _story = inRegion.Position.Story;
                }

                return inRegion;
            }

            if (hit is { } same && same.Position.Story == story)
            {
                return same;
            }

            if (drag)
            {
                return hit;
            }

            _story = null;
            return hit is { } other && other.Position.Story.Kind == StoryKind.HeaderFooter ? null : hit;
        }

        if (count == 2 && region is { } gesture)
        {
            _bodySelection = session.Selection;
            _story = gesture.Position.Story;
            entered = true;
            return gesture;
        }

        // Header text is entered by the gesture only.
        return hit is { } found && found.Position.Story.Kind == StoryKind.HeaderFooter ? null : hit;
    }

    /// <summary>Leaves the header or footer: the selection of the body from before is back. False when the view is in the body.</summary>
    internal bool LeaveStory()
    {
        if (_story is null)
        {
            return false;
        }

        _story = null;
        if (_bodySelection is { } before && _tab.Editor is { } session && CanPlaceCaret)
        {
            _bodySelection = null;
            Place(session, before, CaretAffinity.Downstream);
            EnsureCaretVisible();
        }

        PublishActiveStory();
        InvalidateAll();
        return true;
    }

    private bool TryMapPoint(Point point, out int page, out float x, out float y)
    {
        page = 0;
        x = 0;
        y = 0;
        EnsureCurrent();
        if (_scroller is not { Count: > 0 } scroller)
        {
            return false;
        }

        page = scroller.PageAt(_offsetY + point.Y);
        var rect = scroller.RectOf(page).Translate(new Vector(LeftShift(scroller), -_offsetY));
        x = (float)((point.X - rect.X) / scroller.Scale);
        y = (float)((point.Y - rect.Y) / scroller.Scale);
        return true;
    }

    private static CaretAffinity AffinityFor(DocumentLayout layout, TextHit hit) =>
        hit.Trailing && UpstreamBox(layout, hit.Position, hit.Page) is not null ? CaretAffinity.Upstream : CaretAffinity.Downstream;

    private static ParagraphProjection ProjectionOf(DocumentLayout layout, TextPosition position) =>
        ParagraphProjection.Build(position.Paragraph, layout.ProjectionHost(position.Story, position.Paragraph));

    private static bool SamePosition(TextPosition a, TextPosition b) =>
        ReferenceEquals(a.Paragraph, b.Paragraph) && a.Offset == b.Offset && a.Story == b.Story;

    // Puts the selection: now, or (while a save runs) as an operation queued in arrival order that is replayed when the save ends.
    private void Place(EditSession session, TextRange range, CaretAffinity affinity)
    {
        _affinity = affinity;
        _affinityFor = range.End;
        if (session.BlockedReason == EditBlockedReason.Saving)
        {
            // Queued in arrival order with the typing; the whole gesture (the press and every move of a drag) is one entry. The positions are those of click time: when it runs,
            // earlier queued edits may have removed the paragraph or shortened it, so the operation checks them against the model as it is then (see ValidQueuedPosition).
            var startBefore = PreviousParagraph(range.Start);
            var endBefore = PreviousParagraph(range.End);
            session.Apply(
                _ =>
                {
                    if (ValidQueuedPosition(session, range.Start, startBefore) is not { } start || ValidQueuedPosition(session, range.End, endBefore) is not { } end)
                    {
                        return null;
                    }

                    return new EditResult(end, [], new TextRange(start, end));
                },
                0,
                _gesture);
        }
        else
        {
            session.SetSelection(range);
        }

        RestartBlink();
        _overlay.InvalidateVisual();
        PublishSelectionState();
        NotifyImeText();
    }

    private bool _publishedSelection;

    private StoryRef? _publishedStory;

    /// <summary>Raised when a header or footer is entered or left (the status line names the active one); after the caret is in its place, never in the middle of the hit.</summary>
    internal event Action? ActiveStoryChanged;

    private void PublishActiveStory()
    {
        if (!Nullable.Equals(_story, _publishedStory))
        {
            _publishedStory = _story;
            ActiveStoryChanged?.Invoke();
        }
    }

    /// <summary>Raised when the selection turns from a caret into a selection or back (the ribbon and the menu read the states of cut and copy again).</summary>
    internal event Action? SelectionStateChanged;

    // Compares the live selection of the session (not what one path last set) with what was last published and raises on any difference. Every place the selection can change calls
    // it: Place (pointer, caret keys, select all, header and footer), OnEditChanged (an edit, undo, redo, the replay of a queue after a save).
    private void PublishSelectionState()
    {
        PublishActiveStory();
        var now = CurrentSelection() is { IsEmpty: false };
        if (now != _publishedSelection)
        {
            _publishedSelection = now;
            SelectionStateChanged?.Invoke();
        }
    }

    // The nearest paragraph before a position's paragraph in its container (the fallback when the paragraph is gone when a queued selection runs), or null.
    private Paragraph? PreviousParagraph(TextPosition position)
    {
        if (TextUnits.BlocksOf(_tab.Docx.Model, position.Story) is not { } blocks || TextUnits.ContainerOf(blocks, position.Paragraph) is not { } container)
        {
            return null;
        }

        for (var i = container.IndexOf(position.Paragraph) - 1; i >= 0; i--)
        {
            if (container[i] is Paragraph paragraph)
            {
                return paragraph;
            }
        }

        return null;
    }

    // A position of click time made valid for the model now (a queued selection runs after the edits queued before it): the offset is clamped to the paragraph's text; a paragraph
    // that is gone becomes the end of the paragraph before it, or when that is gone too the start of the document; null when the document has no paragraph at all.
    private TextPosition? ValidQueuedPosition(EditSession session, TextPosition position, Paragraph? before)
    {
        var model = _tab.Docx.Model;
        var story = position.Story;
        var paragraph = position.Paragraph;
        var offset = position.Offset;
        var stillThere = TextUnits.BlocksOf(model, story) is { } blocks && TextUnits.ContainerOf(blocks, paragraph) is not null;
        if (!stillThere)
        {
            offset = int.MaxValue;
            if (before is not null && TextUnits.BlocksOf(model, story) is { } storyBlocks && TextUnits.ContainerOf(storyBlocks, before) is not null)
            {
                paragraph = before;
            }
            else
            {
                story = StoryRef.Body;
                offset = 0;
                var first = model.Body.Blocks.OfType<Paragraph>().FirstOrDefault();
                if (first is null)
                {
                    return null;
                }

                paragraph = first;
            }
        }

        var runner = _tab.Runner;
        var layout = (session.ActiveMode == LayoutMode.Flow ? runner.FlowLayout : null) ?? runner.PageLayout;
        var length = ParagraphProjection.Build(paragraph, layout.ProjectionHost(story, paragraph)).Text.Length;
        return new TextPosition(story, paragraph, Math.Clamp(offset, 0, length));
    }

    private bool SelectionContains(TextRange range, TextPosition position)
    {
        if (range.IsEmpty || range.Start.Story != position.Story || TextUnits.BlocksOf(_tab.Docx.Model, position.Story) is not { } blocks
            || TextUnits.ContainerOf(blocks, position.Paragraph) is not { } container)
        {
            return false;
        }

        var (first, last) = (range.Start, range.End);
        var a = container.IndexOf(first.Paragraph);
        var b = container.IndexOf(last.Paragraph);
        var p = container.IndexOf(position.Paragraph);
        if (a < 0 || b < 0 || p < 0)
        {
            return false;
        }

        if (a > b || (a == b && first.Offset > last.Offset))
        {
            (first, last, a, b) = (last, first, b, a);
        }

        return p >= a && p <= b && (p != a || position.Offset >= first.Offset) && (p != b || position.Offset <= last.Offset);
    }

    private void BeginDrag(TextPosition anchor)
    {
        _dragAnchor = anchor;
        _dragContainer = TextUnits.BlocksOf(_tab.Docx.Model, anchor.Story) is { } blocks ? TextUnits.ContainerOf(blocks, anchor.Paragraph) : null;
    }

    private void EndDrag()
    {
        _dragAnchor = null;
        _dragContainer = null;
        _autoScroll?.Stop();
        if (_dragPointer is { } pointer)
        {
            _dragPointer = null;
            if (ReferenceEquals(pointer.Captured, this))
            {
                pointer.Capture(null);
            }
        }
    }

    // The end of a selection that began at <anchor> for a pointer at <hit>: the hit itself, or the edge of the anchor's container (a cell), or null (another story: unchanged).
    private TextPosition? Constrain(DocumentLayout layout, TextPosition anchor, TextHit hit, float y, int page)
    {
        var target = hit.Position;
        var sameStory = target.Story == anchor.Story;

        // Another story: unchanged, except that a drag that started in a header or footer is clamped to the edge of that story (it never leaves the mode).
        if (!sameStory && (anchor.Story.Kind != StoryKind.HeaderFooter || _dragContainer is null))
        {
            return null;
        }

        if (sameStory && (ReferenceEquals(target.Paragraph, anchor.Paragraph) || _dragContainer is null))
        {
            return target;
        }

        var container = _dragContainer!;
        var index = sameStory ? TextUnits.IndexUnder(container, target.Paragraph) : -1;
        if (index >= 0 && ReferenceEquals(container[index], target.Paragraph))
        {
            return target;
        }

        var forward = layout.Caret(anchor, PageHint) is { } box ? page > box.Page || (page == box.Page && y > box.Top) : true;
        if (index >= 0)
        {
            // Over a table of the container: beside it, before it for a forward drag and after it for a backward one.
            if (forward)
            {
                for (var i = index - 1; i >= 0; i--)
                {
                    if (container[i] is Paragraph before)
                    {
                        return new TextPosition(anchor.Story, before, ProjectionOf(layout, new TextPosition(anchor.Story, before, 0)).Text.Length);
                    }
                }
            }
            else
            {
                for (var i = index + 1; i < container.Count; i++)
                {
                    if (container[i] is Paragraph after)
                    {
                        return new TextPosition(anchor.Story, after, 0);
                    }
                }
            }

            return anchor;
        }

        // In another cell or another container: the edge of the anchor's.
        for (var i = forward ? container.Count - 1 : 0; i >= 0 && i < container.Count; i += forward ? -1 : 1)
        {
            if (container[i] is Paragraph edge)
            {
                var at = new TextPosition(anchor.Story, edge, 0);
                return forward ? at with { Offset = ProjectionOf(layout, at).Text.Length } : at;
            }
        }

        return anchor;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _lastPointer = e.GetPosition(this);
        if (_dragAnchor is null)
        {
            UpdateCursor(_lastPointer);
            return;
        }

        UpdateDrag();
        UpdateAutoScroll();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragAnchor is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            EndDrag();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragAnchor is not null)
        {
            EndDrag();
        }
    }

    // The selection that a drag has now: anchor to the point under the pointer.
    private void UpdateDrag()
    {
        if (_dragAnchor is not { } anchor || _tab.Editor is not { } session || !CanPlaceCaret || ActiveLayout() is not { } layout
            || !TryMapPoint(_lastPointer, out var page, out var x, out var y)
            || ResolveHit(layout, session, page, x, y, 1, drag: true, out _) is not { } hit
            || Constrain(layout, anchor, hit, y, page) is not { } target)
        {
            return;
        }

        var range = new TextRange(anchor, target);
        if (session.Selection is { } now && SamePosition(now.Start, range.Start) && SamePosition(now.End, range.End))
        {
            return;
        }

        Place(session, range, target.Equals(hit.Position) ? AffinityFor(layout, hit) : CaretAffinity.Downstream);
    }

    private void UpdateAutoScroll()
    {
        if (OutsideOf(_lastPointer) == default)
        {
            _autoScroll?.Stop();
            return;
        }

        _autoScroll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, (_, _) => AutoScrollTick());
        if (!_autoScroll.IsEnabled)
        {
            _autoScroll.Start();
        }
    }

    private Vector OutsideOf(Point point) => new(
        point.X < 0 ? point.X : (point.X > _viewport.Width ? point.X - _viewport.Width : 0),
        point.Y < 0 ? point.Y : (point.Y > _viewport.Height ? point.Y - _viewport.Height : 0));

    /// <summary>One tick of the auto-scroll of a drag: scrolls by <see cref="PageScroller.AutoScrollStep"/> toward the pointer and updates the selection (a timer calls it; tests too).</summary>
    internal void AutoScrollTick()
    {
        var outside = OutsideOf(_lastPointer);
        if (_dragAnchor is null || outside == default)
        {
            _autoScroll?.Stop();
            return;
        }

        ScrollTo(_offsetX + PageScroller.AutoScrollStep(outside.X), _offsetY + PageScroller.AutoScrollStep(outside.Y));
        UpdateDrag();
    }

    private void UpdateCursor(Point point)
    {
        var over = CanPlaceCaret && _scroller is { Count: > 0 } scroller && InsideAPage(scroller, point);
        if (over == _cursorIsText)
        {
            return;
        }

        _cursorIsText = over;
        Cursor = over ? (s_textCursor ??= new Cursor(StandardCursorType.Ibeam)) : null;
    }

    private bool InsideAPage(PageScroller scroller, Point point)
    {
        var page = scroller.PageAt(_offsetY + point.Y);
        return scroller.RectOf(page).Translate(new Vector(LeftShift(scroller), -_offsetY)).Contains(point);
    }

    // Blink.

    private bool HasVisibleCaret => !_released && Caret is not null;

    private bool BlinkAllowed => !ReduceMotion && !IsHighContrast();

    private bool IsHighContrast() =>
        Equals(ActualThemeVariant, ShellTheme.HighContrastVariant)
        || (OperatingSystem.IsWindows() && Application.Current?.PlatformSettings?.GetColorValues().ContrastPreference == ColorContrastPreference.High);

    // The timer runs while the view has focus in an active window and a caret exists; it is made on the first caret.
    private void UpdateBlink()
    {
        if (!HasVisibleCaret || !IsFocused || !_windowActive)
        {
            _blink?.Stop();
            SetBlinkPhase(true);
            return;
        }

        _blink ??= new DispatcherTimer(BlinkInterval, DispatcherPriority.Normal, (_, _) => TickBlink());
        if (BlinkAllowed)
        {
            if (!_blink.IsEnabled)
            {
                SetBlinkPhase(true);
                _blink.Start();
            }
        }
        else
        {
            _blink.Stop();
            SetBlinkPhase(true);
        }
    }

    // A caret move makes the caret solid and starts the interval again.
    private void RestartBlink()
    {
        SetBlinkPhase(true);
        if (_blink is { IsEnabled: true } running)
        {
            running.Stop();
            running.Start();
        }

        UpdateBlink();
    }

    /// <summary>One tick of the blink timer: the caret changes phase; only the overlay is drawn again.</summary>
    internal void TickBlink() => SetBlinkPhase(!_blinkOn);

    private void SetBlinkPhase(bool on)
    {
        if (_blinkOn == on)
        {
            return;
        }

        _blinkOn = on;
        _overlay.InvalidateVisual();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        UpdateBlink();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _held.Clear();
        CancelComposition();
        UpdateBlink();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DetachWindow();
        _window = TopLevel.GetTopLevel(this) as WindowBase;
        if (_window is not null)
        {
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DetachWindow();
    }

    private void DetachWindow()
    {
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        _windowActive = true;
        UpdateBlink();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _windowActive = false;
        UpdateBlink();
    }

    // Drawing (the overlay only; it reads, it changes no visual).

    private void DrawEditing(DrawingContext context)
    {
        if (_released || CurrentSelection() is not { } selection || _scroller is not { Count: > 0 } scroller || _builtLayout is not { } layout)
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var (first, last) = scroller.Visible(_offsetY, _viewport.Height);
        var left = LeftShift(scroller);
        var tint = BrushOf("Raffica.Brush.Paper.Selection", Brushes.SteelBlue);
        for (var index = first; index <= last && index < scroller.Count; index++)
        {
            var page = Snap(scroller.RectOf(index).Translate(new Vector(left, -_offsetY)), scaling);
            if (!selection.IsEmpty)
            {
                var boxes = layout.SelectionBoxes(selection, index);
                if (boxes.Count > 0)
                {
                    using (context.PushClip(page))
                    using (context.PushOpacity(SelectionOpacity))
                    {
                        foreach (var box in boxes)
                        {
                            context.FillRectangle(tint, new Rect(page.X + (box.X * scroller.Scale), page.Y + (box.Y * scroller.Scale), box.Width * scroller.Scale, box.Height * scroller.Scale));
                        }
                    }
                }
            }

            if (!_blinkOn || (IsComposing ? layout.PreeditCaret(_composeCursor, PageHint) : CaretBoxOf(layout, selection.End, Affinity)) is not { } caret || caret.Page != index)
            {
                continue;
            }

            _caretPage = index;
            var width = Math.Max(1, Math.Round(CaretWidth * scaling)) / scaling;
            var x = Math.Round((page.X + (caret.X * scroller.Scale)) * scaling) / scaling;
            using (context.PushClip(page))
            {
                context.FillRectangle(BrushOf("Raffica.Brush.Paper.Caret", Brushes.Black), new Rect(x, page.Y + (caret.Top * scroller.Scale), width, caret.Height * scroller.Scale));
            }
        }
    }

    private IBrush BrushOf(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush ? brush : fallback;

    /// <summary>The layer that draws the selection and the caret over the pages: no hit testing, no state of its own.</summary>
    private sealed class EditOverlay : Control
    {
        private readonly PageView _owner;

        public EditOverlay(PageView owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        protected override Size MeasureOverride(Size availableSize) => default;

        /// <summary>How many times the overlay was rendered (tests).</summary>
        internal int RenderCount { get; private set; }

        public override void Render(DrawingContext context)
        {
            RenderCount++;
            _owner.DrawEditing(context);
        }
    }
}
