using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Viewing;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Organizing;

/// <summary>
/// The pages pane while a page plan is open: a virtualised multi-select list with one item per slot of the plan (the thumbnail, the position,
/// what is pending for the page as icon plus text) and a counter above it. The list shows the plan and the selection of the
/// <see cref="OrganizeSession"/> and changes them only through it. Mouse: click, Ctrl+click and Shift+click select, a drag of the selection
/// reorders (the pages keep their order and are gathered at the drop). Keys inside the list: Alt+Up and Alt+Down move the selection by one,
/// Alt+Home and Alt+End to the ends, Delete deletes, <c>[</c> and <c>]</c> turn, Ctrl+D duplicates, Esc collapses the selection to the focused
/// page; Tab leaves the list. The thumbnails follow the rules of the navigation list (<see cref="PagesPane"/>): only after the first sharp page
/// of the tab, only while shown, only for the realised items.
/// </summary>
internal sealed class OrganizeList : UserControl, IDisposable
{
    // The pointer must move this far before a press becomes a drag.
    private const double DragThreshold = 6;
    private const double AutoScrollEdge = 40;

    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly OrganizeSession _session;
    private readonly ListBox _list;
    private readonly TextBlock _counter;
    private readonly TextBlock _gone;
    private readonly TextBlock _notice;
    private readonly InsertMarker _marker = new();
    private readonly OrganizeThumbnails _thumbnails;
    private readonly DispatcherTimer _autoScroll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private ObservableCollection<OrganizeItem> _items = [];
    private TopLevel? _topLevel;
    private int _builtVersion = -1;
    private int _pressIndex = -1;
    private int _collapseTo = -1;
    private Point _pressPoint;
    private Point _dragPoint;
    private bool _dragging;
    private bool _syncing;
    private bool _replanPosted;
    private bool _started;
    private bool _disposed;

    public OrganizeList(PdfTab tab, PageView view, OrganizeSession session)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(session);
        _tab = tab;
        _view = view;
        _session = session;
        _started = view.FirstSharpPageShown;
        _thumbnails = new OrganizeThumbnails(tab, session, InvalidateThumbnails, MeasureThumbnails, RequestReplan);

        _counter = new TextBlock { FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(_counter, AutomationLiveSetting.Polite);
        _gone = new TextBlock { FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
        _gone.Classes.Add("thumbnail-number");
        _notice = new TextBlock { FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
        AutomationProperties.SetLiveSetting(_notice, AutomationLiveSetting.Polite);
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(8, 6, 8, 6) };
        header.Children.Add(_counter);
        header.Children.Add(_gone);
        header.Children.Add(_notice);

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            ItemsSource = _items,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<OrganizeItem>((item, _) => item is null ? new Panel() : new OrganizeItemView(_thumbnails, item)),
        };
        _list.Classes.Add("nav-list");
        KeyboardNavigation.SetTabNavigation(_list, KeyboardNavigationMode.Once);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_list, PdfStrings.OrganizeListName);
        _list.ContainerPrepared += (_, e) =>
        {
            Decorate(e.Container, e.Index);
            RequestReplan();
        };
        _list.ContainerClearing += (_, _) => RequestReplan();
        _list.SelectionChanged += OnListSelectionChanged;
        _list.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _list.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _list.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _list.PointerCaptureLost += (_, _) => EndDrag();
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _autoScroll.Tick += (_, _) => AutoScrollTick();

        var body = new Grid();
        body.Children.Add(_list);
        body.Children.Add(_marker);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(header);
        root.Children.Add(body);
        Grid.SetRow(body, 1);
        Content = root;

        view.FirstSharpPage += OnFirstSharpPage;
        session.Changed += OnSessionChanged;
        session.Ending += OnEnding;
        tab.Session.Changed += OnViewerSessionChanged;
        Sync();
    }

    /// <summary>The list control (for tests).</summary>
    internal ListBox List => _list;

    /// <summary>The items, one per slot of the plan, in plan order.</summary>
    internal IReadOnlyList<OrganizeItem> Items => _items;

    internal OrganizeThumbnails Thumbnails => _thumbnails;

    internal InsertMarker Marker => _marker;

    internal TextBlock Counter => _counter;

    internal TextBlock Notice => _notice;

    /// <summary>The counter text: "12 → 10 pages", or just the count while the plan keeps it.</summary>
    internal string CounterText => _counter.Text ?? string.Empty;

    /// <summary>How many pages of the document the plan removes ("3 removed"), or <see langword="null"/>.</summary>
    internal string? GoneText => _gone.IsVisible ? _gone.Text : null;

    /// <summary>The reason the last operation was refused, or <see langword="null"/>.</summary>
    internal string? NoticeText => _notice.IsVisible ? _notice.Text : null;

    /// <summary>The gap of the list a drag in progress would drop into, or <see langword="null"/> when no drag runs.</summary>
    internal int? DropGap { get; private set; }

    /// <summary>Whether the first sharp page of the tab was drawn, so that the list may start its work.</summary>
    internal bool HasStarted => _started;

    internal bool IsDisposed => _disposed;

    /// <summary>The positions whose items the list has realised (the visible ones and a few around them).</summary>
    internal IReadOnlyList<int> RealizedPositions =>
        [.. _list.GetRealizedContainers().Select(_list.IndexFromContainer).Where(i => i >= 0).Order()];

    /// <summary>The list item of a position, or <see langword="null"/> when it is not realised.</summary>
    internal ListBoxItem? ContainerOf(int position) => _list.ContainerFromIndex(position) as ListBoxItem;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _autoScroll.Stop();
        _view.FirstSharpPage -= OnFirstSharpPage;
        _session.Changed -= OnSessionChanged;
        _session.Ending -= OnEnding;
        _tab.Session.Changed -= OnViewerSessionChanged;
        _thumbnails.Dispose();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        Replan();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel = null;
        Replan();
    }

    // The gap of the list a pointer at this height (in the list's coordinates) is in: before the first realised item whose middle lies below it.
    internal int GapAt(Point point)
    {
        var last = -1;
        foreach (var position in RealizedPositions)
        {
            if (ContainerOf(position) is not { } container || container.TranslatePoint(default, _list) is not { } top)
            {
                continue;
            }

            if (point.Y < top.Y + container.Bounds.Height / 2)
            {
                return position;
            }

            last = position;
        }

        return last + 1;
    }

    // Scrolls while a drag is held near the top or the bottom edge of the list, and moves the marker with the content.
    internal void AutoScrollTick()
    {
        if (!_dragging || _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroll)
        {
            return;
        }

        var height = _list.Bounds.Height;
        var step = _dragPoint.Y < AutoScrollEdge ? -(AutoScrollEdge - _dragPoint.Y) / 2 : _dragPoint.Y > height - AutoScrollEdge ? (_dragPoint.Y - (height - AutoScrollEdge)) / 2 : 0;
        if (step != 0)
        {
            var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + step, 0, max));
            _list.UpdateLayout();
            UpdateDrop();
        }
    }

    private void Decorate(Control container, int index)
    {
        if (index >= 0 && index < _items.Count)
        {
            AutomationProperties.SetName(container, _items[index].AutomationName);
        }
    }

    private void OnFirstSharpPage()
    {
        if (_disposed)
        {
            return;
        }

        _started = true;
        RequestReplan();
    }

    private void OnViewerSessionChanged()
    {
        // Any thread (the theme of the pages, the sizes of the pages): only posts.
        Dispatcher.Post(
            () =>
            {
                if (!_disposed)
                {
                    MeasureThumbnails();
                    RequestReplan();
                }
            },
            DispatcherPriority.Normal);
    }

    // The mode ends or the tab closes: the sources of the inserted files go before the files do.
    private void OnEnding() => _thumbnails.ReleaseInserted();

    private void OnSessionChanged()
    {
        if (_disposed || !_session.IsActive)
        {
            return;
        }

        Sync();
    }

    // Takes the plan, the selection and the counter over from the session.
    private void Sync()
    {
        if (!_session.IsActive)
        {
            return;
        }

        if (_builtVersion != _session.Version || _items.Count == 0)
        {
            _builtVersion = _session.Version;
            SyncItems();
            UpdateHeader();
            ClearNotice();
        }

        SyncSelection();
    }

    private void SyncItems()
    {
        var plan = _session.Plan;
        var built = new List<OrganizeItem>(plan.Slots.Count);
        var seen = new HashSet<(int, int)>();
        var used = new HashSet<int>();
        for (var i = 0; i < plan.Slots.Count; i++)
        {
            var slot = plan.Slots[i];
            var copy = false;
            string? file = null;
            if (slot.Blank is null)
            {
                copy = !seen.Add((slot.Source.Value, slot.SourceIndex));
                if (slot.Source.Value != 0)
                {
                    used.Add(slot.Source.Value);
                    file = _session.Inserted.TryGetValue(slot.Source, out var inserted) ? inserted.Name : null;
                }
            }

            built.Add(new OrganizeItem(i, slot, copy, file));
        }

        _thumbnails.Prune(used);
        _syncing = true;
        try
        {
            if (built.Count == _items.Count)
            {
                // The same number of pages (a turn, a swap): the items are replaced where they differ and the scroll position stays.
                for (var i = 0; i < built.Count; i++)
                {
                    if (!_items[i].Equals(built[i]))
                    {
                        _items[i] = built[i];
                    }
                }
            }
            else
            {
                var scroll = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                var offset = scroll?.Offset ?? default;
                _items = new ObservableCollection<OrganizeItem>(built);
                _list.ItemsSource = _items;
                if (scroll is not null)
                {
                    Dispatcher.UIThread.Post(() => scroll.Offset = offset, DispatcherPriority.Loaded);
                }
            }

            foreach (var container in _list.GetRealizedContainers())
            {
                Decorate(container, _list.IndexFromContainer(container));
            }
        }
        finally
        {
            _syncing = false;
        }

        RequestReplan();
    }

    private void SyncSelection()
    {
        var wanted = _session.Selection.Positions;
        var current = _list.Selection.SelectedIndexes;
        if (wanted.Count == current.Count && wanted.SequenceEqual(current.Order()))
        {
            return;
        }

        _syncing = true;
        try
        {
            _list.Selection.BeginBatchUpdate();
            _list.Selection.Clear();
            foreach (var position in wanted)
            {
                if (position >= 0 && position < _items.Count)
                {
                    _list.Selection.Select(position);
                }
            }

            _list.Selection.EndBatchUpdate();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _disposed || !_session.IsActive)
        {
            return;
        }

        _session.Selection.Set(_items.Count, _list.Selection.SelectedIndexes.Order());
    }

    private void UpdateHeader()
    {
        var plan = _session.Plan;
        var before = plan.PrimaryPageCount;
        var after = plan.Slots.Count;
        _counter.Text = before == after
            ? after == 1 ? PdfStrings.OrganizeCounterSameOne : PdfStrings.Fill(PdfStrings.OrganizeCounterSameFormat, after)
            : after == 1 ? PdfStrings.Fill(PdfStrings.OrganizeCounterChangedOneFormat, before) : PdfStrings.Fill(PdfStrings.OrganizeCounterChangedFormat, before, after);
        AutomationProperties.SetName(_counter, _counter.Text);

        var kept = new HashSet<int>();
        foreach (var slot in plan.Slots)
        {
            if (slot.Blank is null && slot.Source.Value == 0)
            {
                kept.Add(slot.SourceIndex);
            }
        }

        var gone = before - kept.Count;
        _gone.IsVisible = gone > 0;
        _gone.Text = gone > 0 ? PdfStrings.Fill(PdfStrings.OrganizeGoneFormat, gone) : null;
    }

    private void ClearNotice()
    {
        _notice.IsVisible = false;
        _notice.Text = null;
    }

    // A refusal is said in the list (a live region); a step that worked takes an old notice away.
    private void Apply(OrganizeResult result)
    {
        switch (result.Problem)
        {
            case null:
                ClearNotice();
                break;
            case OrganizeProblem.LastPage:
                Say(PdfStrings.OrganizeLastPageRefused);
                break;
            case OrganizeProblem.LimitExceeded:
                Say(PdfStrings.Fill(PdfStrings.OrganizeLimitRefusedFormat, PageEditLimits.MaxSlots));
                break;
            default:
                ClearNotice();
                break;
        }
    }

    /// <summary>Says what a refused step of a ribbon command was (the list's notice); a step that worked takes an old notice away.</summary>
    internal void Report(OrganizeResult result) => Apply(result);

    private void Say(string text)
    {
        _notice.Text = text;
        _notice.IsVisible = true;
    }

    private int ItemIndexOf(object? source) =>
        source is Visual visual && visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is { } item ? _list.IndexFromContainer(item) : -1;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressIndex = -1;
        _collapseTo = -1;
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || ItemIndexOf(e.Source) is not (>= 0 and var index))
        {
            return;
        }

        _pressPoint = e.GetPosition(_list);
        var plain = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) == 0;
        if (!plain)
        {
            return;
        }

        _pressIndex = index;
        if (_session.Selection.Count > 1 && _session.Selection.Contains(index))
        {
            // A press on one of several selected pages keeps the selection (it may be the start of a drag); a release without a move collapses it.
            _collapseTo = index;
            e.Handled = true;
            ContainerOf(index)?.Focus(NavigationMethod.Pointer);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressIndex < 0)
        {
            return;
        }

        var point = e.GetPosition(_list);
        if (!_dragging)
        {
            if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed)
            {
                _pressIndex = -1;
                return;
            }

            var moved = point - _pressPoint;
            if (Math.Sqrt(moved.X * moved.X + moved.Y * moved.Y) < DragThreshold)
            {
                return;
            }

            if (!_session.Selection.Contains(_pressIndex))
            {
                _pressIndex = -1;
                return;
            }

            _dragging = true;
            e.Pointer.Capture(_list);
            _autoScroll.Start();
        }

        _dragPoint = point;
        UpdateDrop();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            var gap = DropGap;
            EndDrag();
            e.Pointer.Capture(null);
            if (gap is { } g && !OrganizeDrag.IsNoOp(_session.Selection.Positions, g))
            {
                Apply(_session.Move(g));
            }
        }
        else if (_collapseTo >= 0 && _collapseTo < _items.Count)
        {
            _session.Selection.Select(_collapseTo);
        }

        _pressIndex = -1;
        _collapseTo = -1;
    }

    private void UpdateDrop()
    {
        var gap = GapAt(_dragPoint);
        DropGap = gap;
        double y;
        if (ContainerOf(gap) is { } below && below.TranslatePoint(default, _list) is { } top)
        {
            y = top.Y;
        }
        else if (ContainerOf(gap - 1) is { } above && above.TranslatePoint(default, _list) is { } aboveTop)
        {
            y = aboveTop.Y + above.Bounds.Height;
        }
        else
        {
            y = 0;
        }

        _marker.ShowAt(y);
    }

    private void EndDrag()
    {
        _dragging = false;
        _pressIndex = -1;
        _collapseTo = -1;
        DropGap = null;
        _marker.Hide();
        _autoScroll.Stop();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_disposed || !_session.IsActive)
        {
            return;
        }

        if (e.Key == Key.Escape && _dragging)
        {
            e.Handled = true;
            EndDrag();
            return;
        }

        var wasFocused = NavList.FocusedIndex(_list);
        if (BracketTurn(e) is { } turn)
        {
            e.Handled = Edit(() => _session.Rotate(turn));
        }
        else
        {
            HandleKey(e, wasFocused);
        }

        if (e.Handled && wasFocused >= 0)
        {
            KeepFocus(wasFocused);
        }
    }

    /// <summary>The modifier of the page list's everyday keys (duplicate, select all): Ctrl, Cmd on macOS; settable for tests.</summary>
    internal KeyModifiers CommandModifier { get; set; } = KeyNames.CommandModifier;

    private void HandleKey(KeyEventArgs e, int wasFocused)
    {
        if (e.KeyModifiers == CommandModifier)
        {
            if (e.Key == Key.D)
            {
                e.Handled = Edit(_session.Duplicate);
            }
            else if (e.Key == Key.A)
            {
                e.Handled = true;
                _session.Selection.SelectAll();
            }

            return;
        }

        switch (e.KeyModifiers)
        {
            case KeyModifiers.Alt:
                e.Handled = e.Key switch
                {
                    Key.Up => Edit(() => _session.MoveBy(-1)),
                    Key.Down => Edit(() => _session.MoveBy(1)),
                    Key.Home => Edit(() => _session.Move(0)),
                    Key.End => Edit(() => _session.Move(_items.Count)),
                    _ => false,
                };
                break;
            case KeyModifiers.None:
                if (e.Key == Key.Delete)
                {
                    e.Handled = Edit(_session.Delete);
                }
                else if (e.Key == Key.Escape && wasFocused >= 0)
                {
                    // Escape collapses the selection to the page that has the focus; leaving the mode is a command, never a key of the list.
                    e.Handled = true;
                    _session.Selection.Select(wasFocused);
                }

                break;
        }
    }

    // The turn a bracket key asks for (left -90, right 90), or null. The character wins where the platform gives it: the bracket sits elsewhere
    // on other layouts, and on a German one it is AltGr+8 and AltGr+9, which Windows reports as Control+Alt. Without a character the key decides.
    private static int? BracketTurn(KeyEventArgs e)
    {
        var plain = e.KeyModifiers == KeyModifiers.None;
        var altGr = e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt);
        if (e.KeySymbol is { Length: > 0 } symbol)
        {
            return (plain || altGr) && symbol is "[" or "]" ? (symbol == "[" ? -90 : 90) : null;
        }

        return !plain ? null : e.Key == Key.OemOpenBrackets ? -90 : e.Key == Key.OemCloseBrackets ? 90 : null;
    }

    private bool Edit(Func<OrganizeResult> operation)
    {
        Apply(operation());
        return true;
    }

    // After a change the item that had the focus may be gone or moved: the focus stays on its row while that row is still selected (or nothing
    // is selected and the row exists), else it goes to the selected row nearest to it, else to the nearest row.
    private void KeepFocus(int was)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_disposed || _items.Count == 0)
                {
                    return;
                }

                var selection = _session.Selection;
                var row = selection.Count == 0 || selection.Contains(was)
                    ? Math.Min(was, _items.Count - 1)
                    : selection.Positions.MinBy(p => Math.Abs(p - was));
                _list.ScrollIntoView(row);
                if (ContainerOf(row) is null)
                {
                    _list.UpdateLayout();
                }

                ContainerOf(row)?.Focus(NavigationMethod.Directional);
            },
            DispatcherPriority.Input);
    }

    private IEnumerable<OrganizeThumbnail> ThumbnailControls() =>
        _list.GetRealizedContainers().SelectMany(c => c.GetVisualDescendants().OfType<OrganizeThumbnail>());

    private void InvalidateThumbnails()
    {
        foreach (var image in ThumbnailControls())
        {
            image.InvalidateVisual();
        }
    }

    private void MeasureThumbnails()
    {
        foreach (var image in ThumbnailControls())
        {
            image.InvalidateMeasure();
        }
    }

    private void RequestReplan()
    {
        if (_replanPosted || _disposed)
        {
            return;
        }

        _replanPosted = true;
        Dispatcher.UIThread.Post(Replan, DispatcherPriority.Render);
    }

    // Plans the thumbnails of the realised items. A list that is not on screen, or whose tab has not drawn its first sharp page, requests nothing.
    private void Replan()
    {
        _replanPosted = false;
        if (_disposed)
        {
            return;
        }

        var onScreen = _started && _topLevel is not null && IsEffectivelyVisible && _session.IsActive && _items.Count > 0;
        var slots = new List<PageSlot>();
        if (onScreen)
        {
            foreach (var position in RealizedPositions)
            {
                if (position < _items.Count)
                {
                    slots.Add(_items[position].Slot);
                }
            }
        }

        _thumbnails.Replan(slots, _topLevel?.RenderScaling ?? 1, onScreen);
    }
}
