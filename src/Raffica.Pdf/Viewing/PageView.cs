using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Raffica.Core.Pdf;
using Raffica.Core.Settings;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Viewing;

/// <summary>
/// The page view of one document tab: the pages one below the other (or in rows of two, or one at a time), scrolled with the wheel, the
/// keys and two scroll bars. It owns no PDFium state: Core's <see cref="ViewerSession"/> of the tab plans and renders, this control
/// asks the planner what the viewport needs after every scroll, zoom or size change, hands the plan to the scheduler (which drops
/// whatever is no longer wanted), and draws page grounds, scaled-up previews and sharp tiles from the render cache in one pass.
/// The pixels of a dark page already arrive dark from Core; the control only paints the matching ground.
/// </summary>
/// <remarks>
/// <para>
/// The state is the top of the view as an anchor (page and fraction), the horizontal distance from the content centre, the zoom, the
/// zoom mode and the layout mode; the offsets are derived from it whenever the layout changes. Only a view that is on screen
/// (attached, visible, with a size) requests anything; a view that leaves the screen cancels its requests.
/// </para>
/// <para>
/// Avalonia repaints a custom control as a whole: a tile that arrives repaints the view only when its page is on screen, not for
/// the buffer pages around it. The recorded frame of one pass holds just the pages that are visible.
/// </para>
/// </remarks>
internal sealed partial class PageView : Control
{
    /// <summary>The tab shown; its session is the only source of pages.</summary>
    public static readonly StyledProperty<PdfTab?> TabProperty = AvaloniaProperty.Register<PageView, PdfTab?>(nameof(Tab));

    /// <summary>The desk behind the pages.</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<PageView>();

    /// <summary>The thin line around each page.</summary>
    public static readonly StyledProperty<IBrush?> PageBorderBrushProperty = AvaloniaProperty.Register<PageView, IBrush?>(nameof(PageBorderBrush));

    // The time the smooth wheel needs to cover half of the way to its target.
    private const double HalfLifeSeconds = 0.045;

    // The stand-in for a document whose first page size is not known yet (A4 in points).
    private static readonly PdfPageSize FallbackPageSize = new(595.28, 841.89);

    private static readonly IBrush LightGround = Brushes.White;
    private static readonly IBrush DarkGround = new ImmutableSolidColorBrush(Color.FromUInt32(PageThemeColors.DarkBackground));
    private static readonly Avalonia.Media.RenderOptions Smooth = new() { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality };
    private static readonly Avalonia.Media.RenderOptions Crisp = new() { BitmapInterpolationMode = BitmapInterpolationMode.None };

    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical, Visibility = ScrollBarVisibility.Auto, Focusable = false };
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, Visibility = ScrollBarVisibility.Auto, Focusable = false };
    private readonly TileBitmaps _bitmaps = new(TileBitmaps.DefaultMaxBytes);
    private readonly object _readyGate = new();
    private readonly HashSet<TileKey> _ready = [];
    private readonly Dictionary<int, List<RenderRequest>> _sharpByPage = [];
    private readonly List<(Bitmap Bitmap, Rect Target)> _tiles = [];
    private ViewerSession? _session;
    private RenderScheduler? _scheduler;
    private PdfDocument? _document;
    private DocumentLayout? _layout;
    private int _appliedSizes;
    private int _syncPosted;
    private bool _flushPosted;
    private bool _syncingBars;
    private Size _viewport;
    private double _offsetX;
    private double _offsetY;
    private PageAnchor _anchor;
    private bool _hasAnchor;
    private double _centreDistance;
    private int? _navigatedPage;
    private double _zoom = ZoomSteps.Default;
    private ZoomMode _zoomMode = ZoomMode.Free;
    private PageLayoutMode _layoutMode = PageLayoutMode.Continuous;
    private IReadOnlyList<RenderRequest> _lastPlan = [];
    private bool _requesting;
    private PageTheme _bitmapTheme;
    private TopLevel? _topLevel;
    private bool _animating;
    private double _targetX;
    private double _targetY;
    private TimeSpan? _lastFrame;
    private int _firstSharpPosted;
    private string? _focusedFieldName;
    private bool _arranging;
    private bool _raising;
    private bool _viewChangedPosted;
    private double _wheelZoomRemainder;

    public PageView()
    {
        Focusable = true;
        ClipToBounds = true;
        _vertical.ValueChanged += (_, e) => OnBarMoved(vertical: true, e.NewValue);
        _horizontal.ValueChanged += (_, e) => OnBarMoved(vertical: false, e.NewValue);
        VisualChildren.Add(_vertical);
        VisualChildren.Add(_horizontal);
        LogicalChildren.Add(_vertical);
        LogicalChildren.Add(_horizontal);
    }

    /// <summary>
    /// Raised on the UI thread when what the view shows changed: scroll position, zoom, layout, current page or page sizes. It is never
    /// raised inside the arrange pass and never from inside a handler of itself: a change that happens in either case is reported right after
    /// (posted to the dispatcher), so a subscriber may change the zoom or the layout in its handler without re-entering the view.
    /// </summary>
    public event Action? ViewChanged;

    /// <summary>Raised once, on the UI thread, after the first sharp (not preview) tile of a visible page was painted. Heavy work starts here.</summary>
    public event Action? FirstSharpPage;

    public PdfTab? Tab { get => GetValue(TabProperty); set => SetValue(TabProperty, value); }

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    public IBrush? PageBorderBrush { get => GetValue(PageBorderBrushProperty); set => SetValue(PageBorderBrushProperty, value); }

    /// <summary>Whether the first sharp page was painted (see <see cref="FirstSharpPage"/>).</summary>
    public bool FirstSharpPageShown { get; private set; }

    /// <summary>The zoom (1 = 100 %); setting it is a manual zoom and ends a fit mode.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoomMode = ZoomMode.Free;
            _zoom = ZoomSteps.Clamp(value);
            if (_layout is not null)
            {
                _layout.Zoom = _zoom;
            }

            Relayout();
        }
    }

    /// <summary>How the zoom follows the window: <see cref="ZoomMode.Free"/> keeps <see cref="Zoom"/>, the others fit it to the viewport.</summary>
    public ZoomMode ZoomMode
    {
        get => _zoomMode;
        set
        {
            if (value == _zoomMode)
            {
                return;
            }

            _zoomMode = value;
            Relayout();
        }
    }

    /// <summary>How the pages are arranged: continuous (default), single page, two pages, two pages with a cover.</summary>
    public PageLayoutMode Layout
    {
        get => _layoutMode;
        set
        {
            if (value == _layoutMode)
            {
                return;
            }

            var page = CurrentPage;
            _layoutMode = value;
            if (_layout is not null)
            {
                _layout.Mode = value;
                if (value == PageLayoutMode.SinglePage && _layout.PageCount > 0)
                {
                    _layout.ShowPage(page);
                }

                _anchor = AnchorForPage(page);
                _hasAnchor = true;
                _navigatedPage = page;
            }

            Relayout();
        }
    }

    /// <summary>Zooms in by one step of <see cref="ZoomSteps"/>, around the centre of the view; ends a fit mode.</summary>
    public void ZoomIn() => SetZoom(ZoomSteps.Next(_zoom));

    /// <summary>Zooms out by one step of <see cref="ZoomSteps"/>, around the centre of the view; ends a fit mode.</summary>
    public void ZoomOut() => SetZoom(ZoomSteps.Previous(_zoom));

    /// <summary>Sets a manual zoom (limited to Core's range), around the centre of the view; ends a fit mode.</summary>
    public void SetZoom(double zoom) => ZoomAround(zoom, new Point(_viewport.Width / 2, _viewport.Height / 2));

    /// <summary>Sets how the zoom follows the window; <see cref="ZoomMode.Free"/> keeps the zoom as it is.</summary>
    public void SetZoomMode(ZoomMode mode) => ZoomMode = mode;

    /// <summary>Sets the page layout; the current page stays the current page.</summary>
    public void SetLayout(PageLayoutMode mode) => Layout = mode;

    /// <summary>The theme of the pages (light: as the file draws them; dark: light on a dark ground, images keep their colours).</summary>
    public PageTheme PageTheme
    {
        get => _session?.PageTheme ?? PageTheme.Light;
        set
        {
            if (_session is null || value == _session.PageTheme)
            {
                return;
            }

            _session.SetPageTheme(value);
            _bitmaps.Clear();
            _bitmapTheme = value;
            Replan();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Plans the visible pages again and drops the converted bitmaps: what a change of the session's render settings (the form highlight)
    /// emptied is rendered again without waiting for the next scroll or zoom.
    /// </summary>
    internal void RestartRendering()
    {
        _bitmaps.Clear();
        Replan();
        InvalidateVisual();
    }

    /// <summary>The page the view counts as current: the one a jump went to while it is still on screen, else the one with the most visible height.</summary>
    public int CurrentPage
    {
        get
        {
            if (_layout is null || _layout.PageCount == 0)
            {
                return 0;
            }

            if (_navigatedPage is { } navigated && IsOnScreen(navigated))
            {
                return navigated;
            }

            return Math.Max(0, _layout.GetMostVisiblePage(_offsetY, _offsetY + _viewport.Height));
        }
    }

    /// <summary>The scroll offset in DIPs: how far the content is moved left and up. Setting it clamps to the scrollable range.</summary>
    public Vector Offset
    {
        get => new(_offsetX, _offsetY);
        set
        {
            CancelAnimation();
            ScrollTo(value.X, value.Y);
        }
    }

    /// <summary>The number of pages of the document the view shows; 0 before it is ready.</summary>
    internal int PageCount => _layout?.PageCount ?? 0;

    /// <summary>The size in points of the current page, or <see langword="null"/> while it is not known yet.</summary>
    internal PdfPageSize? CurrentPageSize
    {
        get
        {
            if (_layout is null || _layout.PageCount == 0)
            {
                return null;
            }

            var page = CurrentPage;
            return _layout.IsSizeKnown(page) ? _layout.GetPageSize(page) : null;
        }
    }

    /// <summary>The size in points of the first page, or <see langword="null"/> while it is not known yet.</summary>
    internal PdfPageSize? FirstPageSize => _layout is { PageCount: > 0 } layout && layout.IsSizeKnown(0) ? layout.GetPageSize(0) : null;

    /// <summary>Replaces the layout (for tests: a layout that no real document gives, such as one without pages).</summary>
    internal void AdoptLayout(DocumentLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.Mode = _layoutMode;
        layout.Zoom = _zoom;
        _layout = layout;
        Relayout();
    }

    /// <summary>Whether the arrange pass of the view is running (for tests).</summary>
    internal bool IsArranging => _arranging;

    /// <summary>The requests handed to the scheduler by the last plan (for tests); empty while the view requests nothing.</summary>
    internal IReadOnlyList<RenderRequest> LastPlan => _lastPlan;

    /// <summary>The bitmaps converted from the render cache (for tests).</summary>
    internal TileBitmaps Bitmaps => _bitmaps;

    /// <summary>The pages that touch the viewport as an inclusive range; (0, -1) when there are none.</summary>
    internal (int First, int Last) VisiblePages => _layout is null ? (0, -1) : _layout.GetVisiblePages(_offsetY, _offsetY + _viewport.Height);

    /// <summary>The scrollable height in DIPs (for tests).</summary>
    internal double MaxOffsetY => _layout is null ? 0 : PageViewScroll.MaxOffset(_layout.ContentHeight, _viewport.Height);

    /// <summary>The rectangle of a page in the coordinates of the view, or <see langword="null"/> when the page is not part of the layout.</summary>
    internal Rect? PageBounds(int page)
    {
        if (_layout is null || !_layout.IsShown(page))
        {
            return null;
        }

        var rect = _layout.GetPageRect(page);
        return new Rect(rect.X - _offsetX + LeftGap(), rect.Y - _offsetY, rect.Width, rect.Height);
    }

    /// <summary>Scrolls to the top of a page (one margin above it); in the single-page layout it shows that page.</summary>
    public void GoToPage(int page)
    {
        if (_layout is null || _layout.PageCount == 0)
        {
            return;
        }

        page = Math.Clamp(page, 0, _layout.PageCount - 1);
        if (_layoutMode == PageLayoutMode.SinglePage)
        {
            _layout.ShowPage(page);
        }

        CancelAnimation();
        _anchor = AnchorForPage(page);
        _hasAnchor = true;
        _navigatedPage = page;
        Relayout();
    }

    /// <summary>The position to remember for the file: the top of the view and the horizontal distance of the view centre from the content centre.</summary>
    public ViewState CaptureView() =>
        _layout is null ? new ViewState(new PageAnchor(0, 0), 0) : new ViewState(_layout.GetAnchor(_offsetY), _centreDistance);

    /// <summary>Restores a remembered position with its zoom, zoom mode and layout.</summary>
    public void RestoreView(ViewState view, double zoom, ZoomMode mode, PageLayoutMode layout)
    {
        ArgumentNullException.ThrowIfNull(view);
        _zoom = ZoomSteps.Clamp(zoom);
        _zoomMode = mode;
        _layoutMode = layout;
        _anchor = SanitizeAnchor(view.Anchor);
        _hasAnchor = true;
        _centreDistance = double.IsFinite(view.HorizontalFromCentre) ? view.HorizontalFromCentre : 0;
        _navigatedPage = null;
        CancelAnimation();
        if (_layout is not null)
        {
            _layout.Mode = layout;
            _layout.Zoom = _zoom;
            if (layout == PageLayoutMode.SinglePage && _layout.PageCount > 0)
            {
                _layout.ShowPage(_anchor.Page);
            }
        }

        Relayout();
    }

    /// <summary>The state to remember for the file: the position and the zoom, zoom mode and layout.</summary>
    internal RememberedView CaptureRemembered() => new(CaptureView(), _zoom, _zoomMode, _layoutMode);

    /// <summary>
    /// Zooms to <paramref name="zoom"/> (limited to Core's range) and keeps the spot under <paramref name="point"/> (in the coordinates of the
    /// view) where it is. A manual zoom ends a fit mode.
    /// </summary>
    internal void ZoomAround(double zoom, Point point)
    {
        zoom = ZoomSteps.Clamp(zoom);
        var wasFitted = _zoomMode != ZoomMode.Free;
        if (_layout is null || _layout.PageCount == 0)
        {
            _zoomMode = ZoomMode.Free;
            _zoom = zoom;
            if (_layout is not null)
            {
                _layout.Zoom = zoom;
            }

            return;
        }

        if (zoom == _layout.Zoom)
        {
            _zoomMode = ZoomMode.Free;
            if (wasFitted)
            {
                Relayout();
            }

            return;
        }

        // The spot under the point: the page position at its vertical place, and its distance from the centre line of the pages.
        var contentWidth = _layout.ContentWidth;
        var fromCentre = point.X + _offsetX - LeftGap() - (contentWidth / 2);
        var spot = _layout.GetAnchor(_offsetY + point.Y);
        var ratio = zoom / _layout.Zoom;

        // Above the first page lies the margin, which does not scale with the zoom: its distance to the page stays as it is in DIPs.
        var aboveDips = spot.Fraction < 0 ? spot.Fraction * _layout.GetPageRect(spot.Page).Height : 0;

        CancelAnimation();
        _zoomMode = ZoomMode.Free;
        _layout.Zoom = zoom;
        _zoom = _layout.Zoom;
        var spotOffset = spot.Fraction < 0 ? _layout.GetOffset(new PageAnchor(spot.Page, 0)) + aboveDips : _layout.GetOffset(spot);
        _anchor = _layout.GetAnchor(Math.Max(0, spotOffset - point.Y));
        _hasAnchor = true;
        _navigatedPage = null;
        _centreDistance = (fromCentre * ratio) - point.X + (_viewport.Width / 2);
        Relayout();

        // The offset was limited to the content: what is shown is the position from now on.
        var width = _layout.ContentWidth;
        _centreDistance = width > _viewport.Width ? _offsetX + (_viewport.Width / 2) - (width / 2) : 0;
    }

    // A remembered position may come from another version of the file: the page and the fraction are limited to what the layout has.
    // The fraction may be below 0 (the margin above the first page) and above 1 (the gap below a page).
    private PageAnchor SanitizeAnchor(PageAnchor anchor)
    {
        var fraction = double.IsFinite(anchor.Fraction) ? Math.Clamp(anchor.Fraction, -1, 2) : 0;
        if (_layout is null)
        {
            return new PageAnchor(Math.Max(0, anchor.Page), fraction);
        }

        return new PageAnchor(_layout.PageCount == 0 ? 0 : Math.Clamp(anchor.Page, 0, _layout.PageCount - 1), fraction);
    }

    /// <summary>Moves a smooth wheel scroll that is still under way to its end at once (for tests).</summary>
    internal void CompleteScroll()
    {
        if (_animating)
        {
            _animating = false;
            ScrollTo(_targetX, _targetY);
        }
    }

    /// <summary>
    /// The name of the form field that has the form focus, or <see langword="null"/>. A check box or a radio button has no element of its own, so
    /// the page view carries the name of the focused field for a screen reader; without a focused field the view is named after its document.
    /// </summary>
    internal string? FocusedFieldName
    {
        get => _focusedFieldName;
        set
        {
            if (_focusedFieldName != value)
            {
                _focusedFieldName = value;
                UpdateAutomationName();
            }
        }
    }

    // Computed when the tab is set (the language is fixed for a run), so a screen reader tells the tabs apart.
    private void UpdateAutomationName() =>
        AutomationProperties.SetName(this, _focusedFieldName ?? (Tab is { } tab ? PdfStrings.PageViewName(tab.Title) : null));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TabProperty)
        {
            var tab = change.NewValue as PdfTab;
            SetSession(tab?.Session);

            UpdateAutomationName();
        }
        else if (change.Property == BackgroundProperty || change.Property == PageBorderBrushProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged += OnScalingChanged;
        }

        Replan();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel = null;
        }

        CancelAnimation();
        Replan();

        // A tab in the background keeps no converted bitmaps; the render cache keeps what its small budget allows.
        _bitmaps.Clear();
        _bitmaps.ReleaseRetired();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _vertical.Measure(availableSize);
        _horizontal.Measure(availableSize);
        _badge?.Measure(availableSize);
        _field?.Measure(new Size(Math.Max(0, _fieldRect.Width), double.PositiveInfinity));
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _arranging = true;
        try
        {
            ArrangeCore(finalSize);
        }
        finally
        {
            _arranging = false;
        }

        return finalSize;
    }

    private void ArrangeCore(Size finalSize)
    {
        if (finalSize != _viewport)
        {
            _viewport = finalSize;
            Relayout();
        }

        var barWidth = _vertical.IsVisible ? _vertical.DesiredSize.Width : 0;
        var barHeight = _horizontal.IsVisible ? _horizontal.DesiredSize.Height : 0;
        _vertical.Arrange(new Rect(Math.Max(0, finalSize.Width - barWidth), 0, barWidth, Math.Max(0, finalSize.Height - barHeight)));
        _horizontal.Arrange(new Rect(0, Math.Max(0, finalSize.Height - barHeight), Math.Max(0, finalSize.Width - barWidth), barHeight));
        if (_badge is { IsVisible: true })
        {
            var size = _badge.DesiredSize;
            _badge.Arrange(new Rect(Math.Max(0, (finalSize.Width - barWidth - size.Width) / 2), BadgeTop, Math.Min(size.Width, finalSize.Width), size.Height));
        }

        _field?.Arrange(new Rect(_fieldRect.X, _fieldRect.Y, _fieldRect.Width, Math.Max(_fieldRect.Height, _field.DesiredSize.Height)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // A press in the editing field belongs to the field: it keeps the focus.
        if (!IsInField(e.Source))
        {
            Focus();
        }

    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_layout is null)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl + wheel zooms around the pointer, one step of the zoom table per notch; it never scrolls.
            var zoom = ViewCommands.ZoomByWheel(_zoom, e.Delta.Y, ref _wheelZoomRemainder);
            if (zoom != _zoom)
            {
                ZoomAround(zoom, e.GetPosition(this));
            }

            e.Handled = true;
            return;
        }

        _wheelZoomRemainder = 0;

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        ScrollByWheel(shift ? e.Delta.Y : e.Delta.X, shift ? 0 : e.Delta.Y);
        e.Handled = true;
    }

    /// <summary>
    /// Scrolls by wheel notches (positive scrolls up and to the left, as the wheel reports it) from where the running smooth scroll is
    /// heading. Returns false when the view is already at that end, so nothing moves. The wheel handler and the scroll measurement share it.
    /// </summary>
    internal bool ScrollByWheel(double horizontalNotches, double verticalNotches)
    {
        if (_layout is null)
        {
            return false;
        }

        var fromX = _animating ? _targetX : _offsetX;
        var fromY = _animating ? _targetY : _offsetY;
        var targetX = PageViewScroll.ByWheel(fromX, horizontalNotches, _layout.ContentWidth, _viewport.Width);
        var targetY = PageViewScroll.ByWheel(fromY, verticalNotches, _layout.ContentHeight, _viewport.Height);
        if (targetX == fromX && targetY == fromY)
        {
            return false;
        }

        SmoothScrollTo(targetX, targetY);
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_layout is null || e.Handled || (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
        {
            return;
        }

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        double? y = control && e.Key is not (Key.Home or Key.End) ? null : PageViewScroll.ByKey(e.Key, _offsetY, _layout.ContentHeight, _viewport.Height);
        double? x = control ? null : PageViewScroll.ByHorizontalKey(e.Key, _offsetX, _layout.ContentWidth, _viewport.Width);
        if (y is null && x is null)
        {
            return;
        }

        CancelAnimation();
        ScrollTo(x ?? _offsetX, y ?? _offsetY);
        if (e.Key == Key.End && _layout.PageCount > 0)
        {
            // Several short pages can fill the last screen; the one the key promised is the last one.
            _navigatedPage = _layout.PageCount - 1;
        }

        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        _bitmaps.ReleaseRetired();
        var bounds = new Rect(_viewport);
        context.FillRectangle(Background ?? Brushes.Transparent, bounds);
        if (_layout is null || _session?.Cache is not { } cache)
        {
            return;
        }

        if (_session.PageTheme != _bitmapTheme)
        {
            // The theme was changed on the session itself: the converted bitmaps show the old one, and the cache was emptied.
            _bitmaps.Clear();
            _bitmapTheme = _session.PageTheme;
            Dispatcher.UIThread.Post(
                () =>
                {
                    Replan();
                    InvalidateVisual();
                },
                DispatcherPriority.Render);
        }

        var ground = _session.PageTheme == PageTheme.Dark ? DarkGround : LightGround;
        var scaling = RenderScaling();
        var (first, last) = _layout.GetVisiblePages(_offsetY, _offsetY + _viewport.Height);
        var sharp = false;
        using (context.PushClip(bounds))
        {
            for (var page = first; page <= last; page++)
            {
                if (PageBounds(page) is { } rect)
                {
                    sharp |= DrawPage(context, cache, page, Snap(rect, scaling), ground, scaling);
                }
            }

            _formOverlay?.Render(context, this);
            _overlay?.Render(context, this);
        }

        if (sharp && !FirstSharpPageShown)
        {
            FirstSharpPageShown = true;
            if (Interlocked.Exchange(ref _firstSharpPosted, 1) == 0)
            {
                // After the pass: whatever starts on the first sharp page must not run inside Render.
                Dispatcher.UIThread.Post(() => FirstSharpPage?.Invoke(), DispatcherPriority.Background);
            }
        }
    }

    private static Rect Snap(Rect rect, double scaling) => SnapEdges(rect.Left, rect.Top, rect.Right, rect.Bottom, scaling);

    // Whole device pixels at every edge, so neighbouring tiles meet without a seam.
    private static Rect SnapEdges(double left, double top, double right, double bottom, double scaling)
    {
        var l = Math.Round(left * scaling);
        var t = Math.Round(top * scaling);
        return new Rect(l / scaling, t / scaling, Math.Max(0, Math.Round(right * scaling) - l) / scaling, Math.Max(0, Math.Round(bottom * scaling) - t) / scaling);
    }

    /// <summary>Draws one page: ground, the preview scaled up (unless a whole sharp page covers it), the sharp tiles, a thin border. Returns whether a sharp tile was drawn.</summary>
    private bool DrawPage(DrawingContext context, RenderCache cache, int page, Rect rect, IBrush ground, double scaling)
    {
        context.FillRectangle(ground, rect);

        // The sharp tiles that are ready, resolved first: a whole page that is ready makes the preview unnecessary.
        _tiles.Clear();
        var wholeReady = false;
        if (_sharpByPage.TryGetValue(page, out var requests))
        {
            var pixelsPerDip = requests[0].Scale / (DocumentLayout.DipsPerPoint * _layout!.Zoom);
            foreach (var request in requests)
            {
                if (_bitmaps.Get(request.Key, cache) is not { } bitmap)
                {
                    continue;
                }

                wholeReady |= request.WholePage;
                var target = request.WholePage
                    ? rect
                    : SnapEdges(
                        rect.X + (request.Rect.X / pixelsPerDip),
                        rect.Y + (request.Rect.Y / pixelsPerDip),
                        rect.X + ((request.Rect.X + request.Rect.Width) / pixelsPerDip),
                        rect.Y + ((request.Rect.Y + request.Rect.Height) / pixelsPerDip),
                        scaling);
                _tiles.Add((bitmap, target));
            }
        }

        if (!wholeReady && _bitmaps.Get(new TileKey(page, TileKey.PreviewScaleKey, 0, 0), cache) is { } preview)
        {
            using (context.PushRenderOptions(Smooth))
            {
                context.DrawImage(preview, new Rect(preview.Size), rect);
            }
        }

        using (context.PushRenderOptions(Crisp))
        {
            foreach (var (bitmap, target) in _tiles)
            {
                context.DrawImage(bitmap, new Rect(bitmap.Size), target);
            }
        }

        DrawMarks(context, page, rect);
        if (PageBorderBrush is { } border)
        {
            context.DrawRectangle(null, new Pen(border, 1), rect.Inflate(0.5));
        }

        return _tiles.Count > 0;
    }

    private double RenderScaling() => _topLevel?.RenderScaling ?? 1.0;

    private double LeftGap() => _layout is null ? 0 : Math.Max(0, (_viewport.Width - _layout.ContentWidth) / 2);

    private bool IsOnScreen(int page)
    {
        var (first, last) = VisiblePages;
        return page >= first && page <= last;
    }

    // The top of the page less one margin, so the page sits where a document opens: the margin above its first page.
    private PageAnchor AnchorForPage(int page)
    {
        var top = Math.Max(0, _layout!.GetOffset(new PageAnchor(page, 0)) - _layout.PageMargin);
        return _layout.GetAnchor(top);
    }

    private void SetSession(ViewerSession? session)
    {
        if (_session is not null)
        {
            _session.Changed -= OnSessionChanged;
            _session.PagesInvalidated -= OnPagesInvalidated;
        }

        AttachScheduler(null);
        _session = session;
        _document = null;
        _layout = null;
        _appliedSizes = 0;
        _bitmaps.Clear();
        _lastPlan = [];
        _sharpByPage.Clear();
        _requesting = false;
        if (session is not null)
        {
            _bitmapTheme = session.PageTheme;
            session.Changed += OnSessionChanged;
            session.PagesInvalidated += OnPagesInvalidated;
            SyncFromSession();
        }

        InvalidateVisual();
    }

    private void AttachScheduler(RenderScheduler? scheduler)
    {
        if (ReferenceEquals(scheduler, _scheduler))
        {
            return;
        }

        if (_scheduler is not null)
        {
            _scheduler.TileReady -= OnTileReady;
        }

        _scheduler = scheduler;
        _requesting = false;
        if (scheduler is not null)
        {
            scheduler.TileReady += OnTileReady;
        }
    }

    // Any thread: only posts.
    private void OnSessionChanged()
    {
        if (Interlocked.Exchange(ref _syncPosted, 1) == 0)
        {
            Dispatcher.UIThread.Post(SyncFromSession, DispatcherPriority.Normal);
        }
    }

    // Any thread: only posts. The images of these pages are gone from the cache; the converted bitmaps go too and the pages are planned again.
    private void OnPagesInvalidated(IReadOnlySet<int> pages) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                _bitmaps.Evict(pages);
                Replan();
                InvalidateVisual();
            },
            DispatcherPriority.Normal);

    // Pool thread: only posts, and one post carries every tile that arrived meanwhile.
    private void OnTileReady(TileKey key)
    {
        lock (_readyGate)
        {
            _ready.Add(key);
            if (_flushPosted)
            {
                return;
            }

            _flushPosted = true;
        }

        Dispatcher.UIThread.Post(FlushReady, DispatcherPriority.Render);
    }

    private void FlushReady()
    {
        TileKey[] keys;
        lock (_readyGate)
        {
            keys = [.. _ready];
            _ready.Clear();
            _flushPosted = false;
        }

        // A tile of a page that is not on screen (a buffer preview) changes nothing that is drawn.
        var (first, last) = VisiblePages;
        if (keys.Any(key => key.Page >= first && key.Page <= last))
        {
            InvalidateVisual();
        }
    }

    private void SyncFromSession()
    {
        Volatile.Write(ref _syncPosted, 0);
        if (_session is not { } session)
        {
            return;
        }

        AttachScheduler(session.Scheduler);
        if (session.State != ViewerState.Ready || session.Document is not { } document)
        {
            return;
        }

        var changed = false;
        if (!ReferenceEquals(document, _document) && (_layout is null || _layout.PageCount != document.PageCount))
        {
            var loaded = session.LoadedSizes;
            _layout = new DocumentLayout(document.PageCount, loaded.Count > 0 ? loaded[0] : FallbackPageSize)
            {
                Mode = _layoutMode,
                Zoom = _zoom,
            };
            _appliedSizes = 0;
            changed = true;
            if (_hasAnchor)
            {
                // A position restored before the layout existed: limit it to the pages the document has.
                _anchor = SanitizeAnchor(_anchor);
                if (_layoutMode == PageLayoutMode.SinglePage && _layout.PageCount > 0)
                {
                    _layout.ShowPage(_anchor.Page);
                }
            }
        }

        _document = document;
        if (_layout is { } layout)
        {
            var sizes = session.LoadedSizes;
            var count = Math.Min(sizes.Count, layout.PageCount);
            if (count > _appliedSizes)
            {
                IReadOnlyList<PdfPageSize> fresh = sizes is PdfPageSize[] array
                    ? new ArraySegment<PdfPageSize>(array, _appliedSizes, count - _appliedSizes)
                    : [.. sizes.Skip(_appliedSizes).Take(count - _appliedSizes)];
                layout.SetSizes(_appliedSizes, fresh);
                _appliedSizes = count;
                changed = true;
            }
        }

        if (changed)
        {
            Relayout();
        }
        else
        {
            Replan();
        }
    }

    /// <summary>The layout changed (size, zoom, mode, sizes of pages): the offsets follow from the remembered top of the view.</summary>
    private void Relayout()
    {
        if (_layout is null)
        {
            return;
        }

        // A document without pages has nothing to fit (and no page to take the row from). Actual size needs no viewport.
        if (_zoomMode != ZoomMode.Free && _layout.PageCount > 0 && (_zoomMode == ZoomMode.ActualSize || (_viewport.Width > 0 && _viewport.Height > 0)))
        {
            _layout.Zoom = _layout.FitZoom(_zoomMode, Math.Clamp(_anchor.Page, 0, _layout.PageCount - 1), _viewport.Width, _viewport.Height);
        }

        _zoom = _layout.Zoom;
        if (!_hasAnchor)
        {
            // Nothing was scrolled, restored or jumped to yet: the view stays at the very top, whatever sizes the pages get.
            _anchor = _layout.GetAnchor(0);
        }

        var wanted = _layout.GetOffset(_anchor);
        _offsetY = PageViewScroll.Clamp(wanted, _layout.ContentHeight, _viewport.Height);
        if (_offsetY != wanted)
        {
            // The top of the view had to move (the content got shorter than the position): what is shown is the position from now on.
            _anchor = _layout.GetAnchor(_offsetY);
        }

        var contentWidth = _layout.ContentWidth;
        _offsetX = PageViewScroll.Clamp((contentWidth / 2) + _centreDistance - (_viewport.Width / 2), contentWidth, _viewport.Width);
        _targetX = _offsetX;
        _targetY = _offsetY;
        UpdateScrollBars();
        Replan();
        InvalidateVisual();
        NotifyViewChanged();
    }

    // Raises ViewChanged, but never inside the arrange pass and never from inside a handler of the event: then it follows, posted.
    private void NotifyViewChanged()
    {
        if (_arranging || _raising)
        {
            if (!_viewChangedPosted)
            {
                _viewChangedPosted = true;
                Dispatcher.UIThread.Post(RaisePostedViewChanged, DispatcherPriority.Normal);
            }

            return;
        }

        RaiseViewChanged();
    }

    private void RaisePostedViewChanged()
    {
        _viewChangedPosted = false;
        NotifyViewChanged();
    }

    private void RaiseViewChanged()
    {
        _raising = true;
        try
        {
            ViewChanged?.Invoke();
        }
        finally
        {
            _raising = false;
        }
    }

    /// <summary>Moves the view to an offset (clamped) and remembers the new top of the view.</summary>
    private void ScrollTo(double x, double y)
    {
        if (_layout is null)
        {
            return;
        }

        x = PageViewScroll.Clamp(x, _layout.ContentWidth, _viewport.Width);
        y = PageViewScroll.Clamp(y, _layout.ContentHeight, _viewport.Height);
        if (x == _offsetX && y == _offsetY)
        {
            return;
        }

        _offsetX = x;
        _offsetY = y;
        _anchor = _layout.GetAnchor(y);
        _hasAnchor = true;
        var contentWidth = _layout.ContentWidth;
        _centreDistance = contentWidth > _viewport.Width ? x + (_viewport.Width / 2) - (contentWidth / 2) : 0;
        _navigatedPage = null;
        if (!_animating)
        {
            _targetX = x;
            _targetY = y;
        }

        UpdateScrollBars();
        Replan();
        InvalidateVisual();
        NotifyViewChanged();
    }

    private void SmoothScrollTo(double x, double y)
    {
        _targetX = x;
        _targetY = y;
        if (_animating)
        {
            return;
        }

        if (_topLevel is null)
        {
            ScrollTo(x, y);
            return;
        }

        _animating = true;
        _lastFrame = null;
        _topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan now)
    {
        if (!_animating)
        {
            return;
        }

        var elapsed = _lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.1) : 1.0 / 60;
        _lastFrame = now;
        var share = 1 - Math.Pow(0.5, elapsed / HalfLifeSeconds);
        var x = Approach(_offsetX, _targetX, share);
        var y = Approach(_offsetY, _targetY, share);
        if (x == _targetX && y == _targetY)
        {
            _animating = false;
        }

        ScrollTo(x, y);
        if (_animating && _topLevel is not null)
        {
            _topLevel.RequestAnimationFrame(OnAnimationFrame);
        }
    }

    private static double Approach(double from, double to, double share)
    {
        var next = from + ((to - from) * share);
        return Math.Abs(to - next) < 0.5 ? to : next;
    }

    private void CancelAnimation()
    {
        _animating = false;
        _targetX = _offsetX;
        _targetY = _offsetY;
    }

    private void OnBarMoved(bool vertical, double value)
    {
        if (_syncingBars)
        {
            return;
        }

        CancelAnimation();
        ScrollTo(vertical ? _offsetX : value, vertical ? value : _offsetY);
    }

    private void UpdateScrollBars()
    {
        if (_layout is null)
        {
            return;
        }

        _syncingBars = true;
        try
        {
            Configure(_vertical, _layout.ContentHeight, _viewport.Height, _offsetY);
            Configure(_horizontal, _layout.ContentWidth, _viewport.Width, _offsetX);
        }
        finally
        {
            _syncingBars = false;
        }
    }

    private static void Configure(ScrollBar bar, double extent, double viewport, double offset)
    {
        bar.Minimum = 0;
        bar.Maximum = PageViewScroll.MaxOffset(extent, viewport);
        bar.ViewportSize = viewport;
        bar.SmallChange = PageViewScroll.WheelStep;
        bar.LargeChange = PageViewScroll.PageStep(viewport);
        bar.Value = offset;
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        // The same pages need more or fewer pixels: plan again for the new scale.
        Replan();
        InvalidateVisual();
    }

    /// <summary>
    /// Asks the planner what the viewport needs and hands it to the scheduler, which replaces its wish list (a request that is no longer
    /// wanted is dropped, the running one cancelled). A view that is not on screen requests nothing and cancels what it asked for.
    /// </summary>
    private void Replan()
    {
        _sharpByPage.Clear();
        var onScreen = _layout is not null && _scheduler is not null && _topLevel is not null && IsEffectivelyVisible
            && _viewport.Width > 0 && _viewport.Height > 0;
        if (!onScreen)
        {
            _lastPlan = [];
            if (_requesting)
            {
                _requesting = false;
                _scheduler?.Update([]);
            }

            return;
        }

        var viewport = new DipRect(_offsetX - LeftGap(), _offsetY, _viewport.Width, _viewport.Height);
        var plan = ViewportPlanner.Plan(_layout!, viewport, RenderScaling());
        _lastPlan = plan;
        foreach (var request in plan)
        {
            if (!request.Key.IsPreview)
            {
                if (!_sharpByPage.TryGetValue(request.Key.Page, out var list))
                {
                    _sharpByPage[request.Key.Page] = list = [];
                }

                list.Add(request);
            }
        }

        _requesting = true;
        _scheduler!.Update(plan);
    }
}
