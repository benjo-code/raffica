using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Panes;

/// <summary>
/// The pages pane of one document tab: a virtualised list with one thumbnail per page. The page the document shows is marked and
/// selected; a click or Enter goes to the page. The thumbnails come from Core's <see cref="ThumbnailSource"/> (own cache, own scheduler,
/// background priority) and are requested only for the items the list has realised, only after the page view drew its first sharp page,
/// and only while the pane is on screen: a pane that is not shown (another tab, the outline) cancels its requests and lets go of its
/// bitmaps. The converted bitmaps come from the same bridge as the page view (<see cref="TileBitmaps"/>). While the tab's page plan is open
/// (<see cref="OrganizeSession.IsActive"/>) the pane shows the <see cref="OrganizeList"/> instead and requests nothing of its own.
/// </summary>
internal sealed class PagesPane : UserControl, IDisposable
{
    // The stand-in for the size of a page that is not known yet (A4 in points).
    private static readonly PdfPageSize FallbackPageSize = new(595.28, 841.89);

    private readonly PdfTab _tab;
    private readonly PageView _view;
    private readonly ViewerSession _session;
    private readonly ListBox _list;
    private readonly TileBitmaps _bitmaps = new(16 * 1024 * 1024);
    private readonly int[] _pages;
    private readonly object _readyGate = new();
    private OrganizeSession? _organizeSession;
    private OrganizeList? _organize;
    private ThumbnailSource? _source;
    private IReadOnlyList<RenderRequest> _lastPlan = [];
    private TopLevel? _topLevel;
    private PageTheme _bitmapTheme;
    private int _sizesSeen;
    private int _marked;
    private int _syncPosted;
    private bool _replanPosted;
    private bool _readyPosted;
    private bool _requesting;
    private bool _started;
    private bool _disposed;

    public PagesPane(PdfTab tab, PageView view)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(view);
        _tab = tab;
        _view = view;
        _session = tab.Session;
        _pages = Enumerable.Range(0, _session.Document?.PageCount ?? 0).ToArray();
        _marked = Math.Clamp(view.CurrentPage, 0, Math.Max(0, _pages.Length - 1));
        _started = view.FirstSharpPageShown;

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemsSource = _pages,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<int>((page, _) => new ThumbnailItem(this, page)),
            SelectedIndex = _pages.Length > 0 ? _marked : -1,
        };
        _list.Classes.Add("nav-list");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_list, PdfStrings.PanePages);
        _list.ContainerPrepared += (_, e) =>
        {
            Decorate(e.Container, e.Index);
            RequestReplan();
        };
        _list.ContainerClearing += (_, e) =>
        {
            e.Container.Classes.Remove("current");
            RequestReplan();
        };
        _list.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        _list.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _list.AddHandler(GotFocusEvent, (_, e) => NavList.EnterAt(_list, e, () => _marked), RoutingStrategies.Bubble, handledEventsToo: true);
        Content = _list;

        view.ViewChanged += OnViewChanged;
        view.FirstSharpPage += OnFirstSharpPage;
        _session.Changed += OnSessionChanged;
        _session.PagesInvalidated += OnPagesInvalidated;

        // The organize session is made when it is first used; the pane must not make it, so it waits for it.
        tab.OrganizeCreated += AttachOrganize;
        if (tab.OrganizeIfCreated is { } existing)
        {
            AttachOrganize(existing);
        }
    }

    /// <summary>The list of thumbnails (for tests).</summary>
    internal ListBox List => _list;

    /// <summary>The zero-based page the pane marks as the current one: the page the document shows.</summary>
    internal int MarkedPage => _marked;

    /// <summary>The list of the organize mode while the page plan is open, else <see langword="null"/>.</summary>
    internal OrganizeList? Organize => _organize;

    /// <summary>The requests handed to the thumbnail scheduler by the last plan; empty while the pane requests nothing.</summary>
    internal IReadOnlyList<RenderRequest> LastPlan => _lastPlan;

    /// <summary>The converted bitmaps of the thumbnails (for tests).</summary>
    internal TileBitmaps Bitmaps => _bitmaps;

    /// <summary>Whether the first sharp page of the tab was drawn, so that the pane may start its work.</summary>
    internal bool HasStarted => _started;

    /// <summary>Whether <see cref="Dispose"/> has run.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>The pages whose thumbnails the list has realised (the visible ones and a few around them).</summary>
    internal IReadOnlyList<int> RealizedPages =>
        [.. _list.GetRealizedContainers().Select(_list.IndexFromContainer).Where(i => i >= 0).Order()];

    /// <summary>The list item of a page, or <see langword="null"/> when it is not realised.</summary>
    internal ListBoxItem? ContainerOf(int page) => _list.ContainerFromIndex(page) as ListBoxItem;

    /// <summary>Goes to a page (a click or Enter on its thumbnail).</summary>
    internal void Choose(int page)
    {
        if (_disposed || page < 0 || page >= _pages.Length)
        {
            return;
        }

        _list.SelectedIndex = page;
        _view.GoToPage(page);
    }

    /// <summary>The size of a page in points: the real one once the session has loaded it, else a stand-in.</summary>
    internal PdfPageSize SizeOf(int page)
    {
        var sizes = _session.LoadedSizes;
        return page >= 0 && page < sizes.Count && Usable(sizes[page]) ? sizes[page] : FallbackPageSize;
    }

    /// <summary>The dark ground when the thumbnails are rendered for the dark page, else white.</summary>
    internal bool IsDark => (_source?.PageTheme ?? _session.PageTheme) == PageTheme.Dark;

    /// <summary>The bitmap of a thumbnail if it has been rendered, else <see langword="null"/>. Called while the thumbnail is drawn.</summary>
    internal Avalonia.Media.Imaging.Bitmap? BitmapFor(int page)
    {
        if (_disposed || !_started || _source is not { } source)
        {
            return null;
        }

        if (source.PageTheme != _bitmapTheme)
        {
            // The theme of the pages changed: the converted bitmaps show the old one, and Core has emptied the cache.
            _bitmaps.Clear();
            _bitmapTheme = source.PageTheme;
            RequestReplan();
        }

        return _bitmaps.Get(new TileKey(page, TileKey.PreviewScaleKey, 0, 0), source.Cache);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.ViewChanged -= OnViewChanged;
        _view.FirstSharpPage -= OnFirstSharpPage;
        _session.Changed -= OnSessionChanged;
        _session.PagesInvalidated -= OnPagesInvalidated;
        _tab.OrganizeCreated -= AttachOrganize;
        if (_organizeSession is not null)
        {
            _organizeSession.Changed -= SyncMode;
        }

        _organize?.Dispose();
        _organize = null;
        StopRequesting();
        ReleaseSource();
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

    private static bool Usable(PdfPageSize size) => double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width > 0 && size.Height > 0;

    private void AttachOrganize(OrganizeSession session)
    {
        _organizeSession = session;
        session.Changed += SyncMode;
        SyncMode();
    }

    // The organize list stands in for the navigation list while the plan is open.
    private void SyncMode()
    {
        if (_disposed || _organizeSession is not { } session)
        {
            return;
        }

        if (session.IsActive && _organize is null)
        {
            // The navigation list and the organize list share the tab's one thumbnail source: the navigation list stops (no wish, empty cache)
            // before the organize list plans, never after it, so the hand-over does not depend on the order of posted work.
            StopRequesting();
            _organize = new OrganizeList(_tab, _view, session);
            Content = _organize;
            RequestReplan();
        }
        else if (!session.IsActive && _organize is not null)
        {
            _organize.Dispose();
            _organize = null;
            Content = _list;
            RequestReplan();
        }
    }

    private void Decorate(Control container, int index)
    {
        var current = index == _marked;
        if (current != container.Classes.Contains("current"))
        {
            container.Classes.Set("current", current);
        }

        AutomationProperties.SetName(container, PdfStrings.PageThumbnailName(index + 1, current));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        if (source.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is { } item && _list.IndexFromContainer(item) is >= 0 and var index)
        {
            Choose(index);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && NavList.RowForKey(_list) is >= 0 and var row)
        {
            e.Handled = true;
            Choose(row);
        }
    }

    private void OnViewChanged()
    {
        if (_disposed)
        {
            return;
        }

        var page = Math.Clamp(_view.CurrentPage, 0, Math.Max(0, _pages.Length - 1));
        if (page == _marked)
        {
            return;
        }

        var old = _marked;
        _marked = page;
        if (_pages.Length > 0)
        {
            _list.SelectedIndex = page;
            _list.ScrollIntoView(page);
        }

        foreach (var index in new[] { old, page })
        {
            if (_list.ContainerFromIndex(index) is { } container)
            {
                Decorate(container, index);
            }
        }

        InvalidateThumbnails();
        RequestReplan();
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

    // Any thread: only posts.
    private void OnSessionChanged()
    {
        if (Interlocked.Exchange(ref _syncPosted, 1) == 0)
        {
            Dispatcher.UIThread.Post(SyncSizes, DispatcherPriority.Normal);
        }
    }

    private void SyncSizes()
    {
        Volatile.Write(ref _syncPosted, 0);
        if (_disposed)
        {
            return;
        }

        var count = _session.LoadedSizes.Count;
        if (count != _sizesSeen)
        {
            // The sizes of more pages are known: their thumbnails get their real height.
            _sizesSeen = count;
            foreach (var image in Thumbnails())
            {
                image.InvalidateMeasure();
            }
        }

        RequestReplan();
    }

    // Any thread: only posts.
    private void OnPagesInvalidated(IReadOnlySet<int> pages) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_disposed)
                {
                    return;
                }

                _bitmaps.Evict(pages);
                InvalidateThumbnails();
                RequestReplan();
            },
            DispatcherPriority.Normal);

    // Pool thread: only posts, and one post carries every tile that arrived meanwhile.
    private void OnTileReady(TileKey key)
    {
        lock (_readyGate)
        {
            if (_readyPosted)
            {
                return;
            }

            _readyPosted = true;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                lock (_readyGate)
                {
                    _readyPosted = false;
                }

                if (!_disposed)
                {
                    InvalidateThumbnails();
                }
            },
            DispatcherPriority.Render);
    }

    private IEnumerable<ThumbnailImage> Thumbnails() =>
        _list.GetRealizedContainers().SelectMany(c => c.GetVisualDescendants().OfType<ThumbnailImage>());

    private void InvalidateThumbnails()
    {
        foreach (var image in Thumbnails())
        {
            image.InvalidateVisual();
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

    /// <summary>
    /// Plans the thumbnails of the items the list has realised and hands the plan to the scheduler, which replaces its wish list. A pane
    /// that is not on screen, or whose tab has not drawn its first sharp page, requests nothing.
    /// </summary>
    private void Replan()
    {
        _replanPosted = false;
        if (_disposed)
        {
            return;
        }

        _bitmaps.ReleaseRetired();
        var onScreen = _started && _topLevel is not null && IsEffectivelyVisible && _pages.Length > 0 && _organize is null;
        if (!onScreen)
        {
            StopRequesting();
            return;
        }

        var source = _session.EnsureThumbnails();
        if (!ReferenceEquals(source, _source))
        {
            // The document was replaced: the thumbnails are made anew.
            ReleaseSource();
            _source = source;
            if (source is not null)
            {
                source.Scheduler.TileReady += OnTileReady;
                _bitmapTheme = source.PageTheme;
            }
        }

        if (source is null)
        {
            _lastPlan = [];
            return;
        }

        var realized = RealizedPages;
        var ordered = new List<int>(realized.Count);
        if (realized.Contains(_marked))
        {
            ordered.Add(_marked);
        }

        ordered.AddRange(realized.Where(p => p != _marked));
        _sizesSeen = _session.LoadedSizes.Count;
        var plan = ThumbnailPlanner.Plan(_session.LoadedSizes, ordered, _topLevel!.RenderScaling);
        _lastPlan = plan;
        _requesting = true;
        source.Scheduler.Update(plan);
    }

    // Stops the work and gives the memory back: no wish, no converted bitmaps, an empty cache of thumbnails.
    private void StopRequesting()
    {
        _lastPlan = [];
        if (_requesting && _source is { } source)
        {
            source.Scheduler.Update([]);
            source.Cache.Clear();
        }

        _requesting = false;
        _bitmaps.Clear();
        _bitmaps.ReleaseRetired();
    }

    private void ReleaseSource()
    {
        if (_source is { } source)
        {
            source.Scheduler.TileReady -= OnTileReady;
        }

        _source = null;
    }

    /// <summary>One item of the list: the thumbnail and the page number under it.</summary>
    private sealed class ThumbnailItem : StackPanel
    {
        public ThumbnailItem(PagesPane owner, int page)
        {
            Orientation = Orientation.Vertical;
            Spacing = 4;
            HorizontalAlignment = HorizontalAlignment.Center;
            Children.Add(new ThumbnailImage(owner, page));
            var number = new TextBlock { Text = PdfStrings.Fill("{0}", page + 1), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
            number.Classes.Add("thumbnail-number");
            Children.Add(number);
        }
    }
}

/// <summary>The picture of one page: the ground, the thumbnail if it has been rendered, and a thin line (thick and in ink for the current page).</summary>
internal sealed class ThumbnailImage : Control
{
    private const double MinThumbnailHeight = 16;
    private const double MaxHeightFactor = 4;

    private static readonly IBrush LightGround = Brushes.White;
    private static readonly IBrush DarkGround = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromUInt32(PageThemeColors.DarkBackground));
    private static readonly Avalonia.Media.RenderOptions Smooth = new() { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality };

    private readonly PagesPane _owner;

    public ThumbnailImage(PagesPane owner, int page)
    {
        _owner = owner;
        Page = page;
        IsHitTestVisible = true;
    }

    public int Page { get; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = _owner.SizeOf(Page);
        var height = Math.Clamp(ThumbnailPlanner.HeightFor(size), MinThumbnailHeight, ThumbnailPlanner.Width * MaxHeightFactor);
        return new Size(ThumbnailPlanner.Width, Math.Ceiling(height));
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.FillRectangle(_owner.IsDark ? DarkGround : LightGround, rect);
        if (_owner.BitmapFor(Page) is { } bitmap)
        {
            using (context.PushRenderOptions(Smooth))
            {
                context.DrawImage(bitmap, new Rect(bitmap.Size), rect);
            }
        }

        var current = _owner.MarkedPage == Page;
        var brush = Brush(current ? "Raffica.Brush.Ink" : "Raffica.Brush.Rule");
        if (brush is not null)
        {
            var thickness = current ? 3 : 1;
            context.DrawRectangle(null, new Pen(brush, thickness), rect.Deflate(thickness / 2.0));
        }
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;
}
