using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Raffica.Core.Viewing;
using Raffica.Write.Documents;
using Raffica.Write.Layout;
using Raffica.Write.Settings;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write.Viewing;

/// <summary>
/// The document of a tab on screen: a column of pages on the desk (the page view: paper with a thin shadow, a gap between the pages) or the strips of the text in
/// one paper-coloured column (the writing view: no edge, no shadow, no margins). Only the visible pages are drawn, each through a <see cref="PageDrawOperation"/>
/// on the render thread; the view reads the layout of its mode from the tab's <see cref="LayoutRunner"/> on every change and never keeps a page object across a
/// change (a page that was provisional is replaced when the layout completes). It scrolls (wheel, keys, bars), zooms (Ctrl + wheel, the commands, fit modes; the
/// point under the cursor stays), switches between the two views through a <see cref="ViewAnchor"/> and reports the first drawn page for the measurement. It draws
/// the paper white in every theme (the text colours of a document assume it) and takes the desk from the shell tokens. UI thread only, except the draw callback.
/// </summary>
internal sealed class PageView : Control, IDisposable
{
    /// <summary>The desk behind the pages.</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<PageView>();

    /// <summary>The space between two pages of the page view and around them (DIPs).</summary>
    internal const double PageGap = 16;

    /// <summary>The space around the column of the writing view (DIPs).</summary>
    internal const double StripEdge = 24;

    /// <summary>In the writing view the text column is never made wider than this by fit width (a line of 1000 DIPs is hard to read).</summary>
    internal const double WritingFitLimit = 1.5;

    // The longest the closing of a tab waits for a draw that runs on the render thread.
    private static readonly TimeSpan DrawWait = TimeSpan.FromSeconds(2);

    private static readonly IBrush Paper = Brushes.White;

    private readonly WriteTab _tab;
    private readonly LayoutRunner _runner;
    private readonly ImageCache _images;
    private readonly DrawGate _gate = new();
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical, Visibility = ScrollBarVisibility.Auto, Focusable = false };
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, Visibility = ScrollBarVisibility.Auto, Focusable = false };
    private PageScroller? _scroller;
    private DocumentLayout? _builtLayout;
    private int _builtCount = -1;
    private int _builtRevision = -1;
    private bool _dirty = true;
    private WriteView _mode;
    private WriteView _scrollerMode;
    private ZoomMode _zoomMode;
    private double _zoom = ZoomSteps.Default;
    private double _offsetX;
    private double _offsetY;
    private Size _viewport;
    private ViewAnchor? _pending;
    private double _wheelZoomRemainder;
    private bool _syncingBars;
    private bool _arranging;
    private bool _raising;
    private bool _viewChangedPosted;
    private bool _released;
    private int _firstSharpPosted;
    private (int Page, int Count) _namedFor = (-1, -1);

    public PageView(WriteTab tab, WriteView mode = WriteView.Page, ZoomMode zoomMode = ZoomMode.FitWidth)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tab = tab;
        _runner = tab.Runner;
        _images = new ImageCache(tab.Images.Read);
        _mode = mode;
        _scrollerMode = mode;
        _zoomMode = zoomMode;
        if (zoomMode == ZoomMode.ActualSize)
        {
            _zoom = 1.0;
        }

        Focusable = true;
        ClipToBounds = true;
        _vertical.ValueChanged += (_, e) => OnBarMoved(vertical: true, e.NewValue);
        _horizontal.ValueChanged += (_, e) => OnBarMoved(vertical: false, e.NewValue);
        VisualChildren.Add(_vertical);
        VisualChildren.Add(_horizontal);
        LogicalChildren.Add(_vertical);
        LogicalChildren.Add(_horizontal);
        _runner.Changed += OnRunnerChanged;
        tab.Disposing += Release;
        if (mode == WriteView.Writing)
        {
            _runner.EnsureFlow();
        }

        UpdateAutomationName();
    }

    /// <summary>
    /// Raised on the UI thread when what the view shows changed: scroll position, zoom, mode, page count or the current page. It is never raised inside the arrange
    /// pass and never from inside a handler of itself (a change in either case is reported right after).
    /// </summary>
    public event Action? ViewChanged;

    /// <summary>Raised once, on the UI thread, after page 1 of the view was drawn by the render thread. Heavy work starts here.</summary>
    public event Action? FirstSharpPage;

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    /// <summary>Whether <see cref="FirstSharpPage"/> was raised.</summary>
    public bool FirstSharpPageShown { get; private set; }

    public WriteTab Tab => _tab;

    /// <summary>The tab's runner (progress, completion, the layouts).</summary>
    public LayoutRunner Runner => _runner;

    /// <summary>The view that is shown. Setting it keeps the place in the document (the block at the top) and waits for it when its page is not laid out yet.</summary>
    public WriteView Mode
    {
        get => _mode;
        set
        {
            if (value == _mode)
            {
                return;
            }

            var anchor = _scroller?.AnchorAt(_offsetY);
            _mode = value;
            if (value == WriteView.Writing)
            {
                _runner.EnsureFlow();
            }

            _pending = anchor;
            _offsetY = 0;
            _dirty = true;
            EnsureCurrent();
            InvalidateVisual();
            NotifyViewChanged();
        }
    }

    /// <summary>The zoom (1 = 100 %); the setter is a manual zoom around the middle of the view and ends a fit mode.</summary>
    public double Zoom
    {
        get => _zoom;
        set => SetZoom(value);
    }

    /// <summary>The fit mode in force (<see cref="ZoomMode.Free"/> after a manual zoom).</summary>
    public ZoomMode ZoomMode => _zoomMode;

    public double OffsetX => _offsetX;

    public double OffsetY => _offsetY;

    public Size Viewport => _viewport;

    /// <summary>The height of the content known so far (DIPs).</summary>
    public double ContentHeight => _scroller?.ContentHeight ?? 0;

    public double ContentWidth => _scroller?.ContentWidth ?? 0;

    /// <summary>The pages of the current view published so far.</summary>
    public int PageCount => _scroller?.Count ?? 0;

    /// <summary>True once the layout of the current view has run out of pages (the page count is final).</summary>
    public bool IsLayoutComplete => _runner.LayoutFor(ToLayoutMode(_mode)) is { IsComplete: true };

    /// <summary>The page in the middle of the view, from 0.</summary>
    public int CurrentPage => _scroller is { Count: > 0 } scroller ? scroller.PageAt(_offsetY + (_viewport.Height / 2)) : 0;

    /// <summary>The place at the top of the view (the start of the document before a layout exists).</summary>
    public ViewAnchor TopAnchor => _scroller?.AnchorAt(_offsetY) ?? ViewAnchor.Top;

    /// <summary>The scroller of the pages published so far (a snapshot); null before page 1 exists. For the outline and the measurement.</summary>
    internal PageScroller? Scroller
    {
        get
        {
            EnsureCurrent();
            return _scroller;
        }
    }

    /// <summary>The rectangle of a page in the coordinates of the view (as drawn), or null when there is no such page.</summary>
    public Rect? PageBounds(int page)
    {
        EnsureCurrent();
        if (_scroller is not { } scroller || (uint)page >= (uint)scroller.Count)
        {
            return null;
        }

        return scroller.RectOf(page).Translate(new Vector(LeftShift(scroller), -_offsetY));
    }

    /// <summary>The number of draws of a page that ran on the render thread (tests).</summary>
    internal int DrawCount => Volatile.Read(ref _drawCount);

    private int _drawCount;

    /// <summary>The gate the draws pass (tests).</summary>
    internal DrawGate Gate => _gate;

    /// <summary>True once the tab closed and the pictures were let go.</summary>
    internal bool IsReleased => _released;

    /// <summary>True once the picture cache was disposed (after the last draw ended).</summary>
    internal bool PicturesReleased => Volatile.Read(ref _picturesReleased);

    private bool _picturesReleased;

    // The tab closes: no draw starts any more, the ones that run end, and then the pictures go. The runner and the file are let go by the tab after this.
    public void Dispose() => Release();

    private void Release()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _runner.Changed -= OnRunnerChanged;
        _tab.Disposing -= Release;
        if (_gate.Close(DrawWait))
        {
            _images.Dispose();
            Volatile.Write(ref _picturesReleased, true);
        }
    }

    // Layout.

    private static LayoutMode ToLayoutMode(WriteView view) => view == WriteView.Writing ? LayoutMode.Flow : LayoutMode.Pages;

    private static double GapOf(WriteView view) => view == WriteView.Writing ? 0 : PageGap;

    private static double EdgeOf(WriteView view) => view == WriteView.Writing ? StripEdge : PageGap;

    private void OnRunnerChanged()
    {
        EnsureCurrent();
        InvalidateVisual();
        UpdateScrollBars();
        NotifyViewChanged();
    }

    // Makes the scroller match the layout of the current mode: another layout object, more pages, a replaced provisional page or a changed viewport or zoom.
    private void EnsureCurrent()
    {
        if (_released)
        {
            return;
        }

        var layout = _runner.LayoutFor(ToLayoutMode(_mode));
        var count = layout?.LaidOutPages ?? 0;
        var revision = layout?.Revision ?? 0;
        if (!_dirty && ReferenceEquals(layout, _builtLayout) && count == _builtCount && revision == _builtRevision)
        {
            return;
        }

        _dirty = false;
        _builtLayout = layout;
        _builtCount = count;
        _builtRevision = revision;
        var old = _scroller;
        var sameMode = _scrollerMode == _mode;
        if (layout is null || count == 0)
        {
            _scroller = null;
            _scrollerMode = _mode;
            ClampOffsets();
            TryApplyPending();
            return;
        }

        var sizes = new (float Width, float Height)[count];
        for (var i = 0; i < count; i++)
        {
            // Read again on every rebuild: a page that was provisional has been replaced by an object that is not this one.
            var page = layout.Page(i);
            sizes[i] = (page.Width, page.Height);
        }

        var zoom = ResolveZoom(sizes, sameMode ? old : null);
        var next = new PageScroller(sizes, (float)GapOf(_mode), zoom, new LayoutBlockMap(layout), (float)EdgeOf(_mode));
        if (old is not null && sameMode && old.Zoom != next.Zoom)
        {
            _offsetY = old.OffsetKeepingPoint(_offsetY, 0, next);
            _offsetX = old.HorizontalOffsetKeepingPoint(_offsetX, 0, next);
        }

        _scroller = next;
        _scrollerMode = _mode;
        _zoom = zoom;
        ClampOffsets();
        TryApplyPending();
        UpdateScrollBars();
    }

    private double ResolveZoom((float Width, float Height)[] sizes, PageScroller? old)
    {
        if (_zoomMode == ZoomMode.Free)
        {
            return _zoom;
        }

        if (_zoomMode == ZoomMode.ActualSize)
        {
            return 1.0;
        }

        if (_viewport.Width <= 0 || _viewport.Height <= 0)
        {
            return _zoom;
        }

        var reference = old is { Count: > 0 } ? Math.Min(old.PageAt(_offsetY + (_viewport.Height / 2)), sizes.Length - 1) : 0;
        var (width, height) = sizes[reference];
        var edge = EdgeOf(_mode);
        var byWidth = (_viewport.Width - (2 * edge)) / (width * PageScroller.DipsPerPoint);
        var zoom = byWidth;
        if (_zoomMode == ZoomMode.FitPage && _mode == WriteView.Page)
        {
            zoom = Math.Min(byWidth, (_viewport.Height - (2 * edge)) / (height * PageScroller.DipsPerPoint));
        }

        if (_mode == WriteView.Writing)
        {
            zoom = Math.Min(zoom, WritingFitLimit);
        }

        return double.IsFinite(zoom) && zoom > 0 ? ZoomSteps.Clamp(zoom) : _zoom;
    }

    private void ClampOffsets()
    {
        var height = _scroller?.ContentHeight ?? 0;
        var width = _scroller?.ContentWidth ?? 0;
        _offsetY = PageScroller.Clamp(_offsetY, height, _viewport.Height);
        _offsetX = PageScroller.Clamp(_offsetX, width, _viewport.Width);
    }

    // A wanted place whose page is laid out now: go there. Until then the view waits (a scroll by the user cancels the wish).
    private void TryApplyPending()
    {
        if (_pending is not { } anchor || _scroller is null)
        {
            return;
        }

        if (_scroller.OffsetOf(anchor) is { } offset)
        {
            _pending = null;
            _offsetY = PageScroller.Clamp(offset - (EdgeOf(_mode) / 2), _scroller.ContentHeight, _viewport.Height);
        }
    }

    private double LeftShift(PageScroller scroller) =>
        scroller.ContentWidth < _viewport.Width ? (_viewport.Width - scroller.ContentWidth) / 2 : -_offsetX;

    // Scrolling and zooming.

    /// <summary>Scrolls to the place of a block (the outline); waits for it when its page is not laid out yet.</summary>
    public void ScrollToBlock(int block) => ScrollToAnchor(new ViewAnchor(block, 0));

    /// <summary>Scrolls so that an anchor is at the top of the view; waits for it when its page is not laid out yet.</summary>
    public void ScrollToAnchor(ViewAnchor anchor)
    {
        _pending = anchor;
        EnsureCurrent();
        TryApplyPending();
        UpdateScrollBars();
        InvalidateVisual();
        NotifyViewChanged();
    }

    /// <summary>Scrolls so that the top of a page is at the top of the view.</summary>
    public void ScrollToPage(int page)
    {
        EnsureCurrent();
        if (_scroller is not { } scroller || scroller.Count == 0)
        {
            return;
        }

        ScrollTo(_offsetX, scroller.RectOf(Math.Clamp(page, 0, scroller.Count - 1)).Y - EdgeOf(_mode));
    }

    private void ScrollTo(double x, double y)
    {
        _pending = null;
        EnsureCurrent();
        var height = _scroller?.ContentHeight ?? 0;
        var width = _scroller?.ContentWidth ?? 0;
        x = PageScroller.Clamp(x, width, _viewport.Width);
        y = PageScroller.Clamp(y, height, _viewport.Height);
        if (x == _offsetX && y == _offsetY)
        {
            return;
        }

        _offsetX = x;
        _offsetY = y;
        UpdateScrollBars();
        InvalidateVisual();
        NotifyViewChanged();
    }

    /// <summary>Scrolls by wheel notches (positive scrolls up, as the wheel reports it); false when the view is at that end already.</summary>
    internal bool ScrollByWheel(double verticalNotches, double horizontalNotches = 0)
    {
        EnsureCurrent();
        var y = PageScroller.Clamp(_offsetY - (verticalNotches * PageScroller.WheelStep), ContentHeight, _viewport.Height);
        var x = PageScroller.Clamp(_offsetX - (horizontalNotches * PageScroller.WheelStep), ContentWidth, _viewport.Width);
        if (x == _offsetX && y == _offsetY)
        {
            return false;
        }

        ScrollTo(x, y);
        return true;
    }

    /// <summary>One page down (the top of the next page at the top of the view; one viewport less a step in the writing view).</summary>
    public void PageDown()
    {
        EnsureCurrent();
        if (_scroller is not { Count: > 0 } scroller)
        {
            return;
        }

        if (_mode == WriteView.Writing)
        {
            ScrollTo(_offsetX, _offsetY + PageScroller.PageStep(_viewport.Height));
            return;
        }

        var next = Math.Min(CurrentPage + 1, scroller.Count - 1);
        ScrollTo(_offsetX, scroller.RectOf(next).Y - EdgeOf(_mode));
    }

    /// <summary>One page up (the top of the current page, or of the one before when the view is at the top of it already).</summary>
    public void PageUp()
    {
        EnsureCurrent();
        if (_scroller is not { Count: > 0 } scroller)
        {
            return;
        }

        if (_mode == WriteView.Writing)
        {
            ScrollTo(_offsetX, _offsetY - PageScroller.PageStep(_viewport.Height));
            return;
        }

        var current = CurrentPage;
        var top = scroller.RectOf(current).Y - EdgeOf(_mode);
        ScrollTo(_offsetX, _offsetY - top > 1 ? top : (current > 0 ? scroller.RectOf(current - 1).Y - EdgeOf(_mode) : 0));
    }

    public void ScrollToStart() => ScrollTo(_offsetX, 0);

    public void ScrollToEnd()
    {
        EnsureCurrent();
        ScrollTo(_offsetX, PageScroller.MaxOffset(ContentHeight, _viewport.Height));
    }

    /// <summary>Sets a manual zoom (clamped to the range of <see cref="ZoomSteps"/>) around the middle of the view; ends a fit mode.</summary>
    public void SetZoom(double zoom) => ZoomAround(zoom, new Point(_viewport.Width / 2, _viewport.Height / 2));

    public void ZoomIn() => SetZoom(ZoomSteps.Next(_zoom));

    public void ZoomOut() => SetZoom(ZoomSteps.Previous(_zoom));

    public void ActualSize() => SetZoomMode(ZoomMode.ActualSize);

    public void FitWidth() => SetZoomMode(ZoomMode.FitWidth);

    public void FitPage() => SetZoomMode(ZoomMode.FitPage);

    /// <summary>Takes a zoom mode: the zoom follows the page and the viewport from now on (<see cref="ZoomMode.Free"/> keeps the zoom).</summary>
    public void SetZoomMode(ZoomMode mode)
    {
        _zoomMode = mode;
        _dirty = true;
        EnsureCurrent();
        InvalidateVisual();
        NotifyViewChanged();
    }

    // A manual zoom: the page and the fraction of it under the point stay under the point.
    internal void ZoomAround(double zoom, Point point)
    {
        zoom = ZoomSteps.Clamp(zoom);
        _zoomMode = ZoomMode.Free;
        EnsureCurrent();
        if (_scroller is not { } old)
        {
            _zoom = zoom;
            NotifyViewChanged();
            return;
        }

        if (zoom == old.Zoom)
        {
            return;
        }

        var next = old.WithZoom(zoom);
        _offsetY = old.OffsetKeepingPoint(_offsetY, point.Y, next);
        _offsetX = old.HorizontalOffsetKeepingPoint(_offsetX, point.X, next);
        _scroller = next;
        _zoom = zoom;
        _pending = null;
        ClampOffsets();
        UpdateScrollBars();
        InvalidateVisual();
        NotifyViewChanged();
    }

    // Input.

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl + wheel zooms around the pointer, one step of the zoom table per notch; it never scrolls.
            var zoom = ZoomByWheel(_zoom, e.Delta.Y, ref _wheelZoomRemainder);
            if (zoom != _zoom)
            {
                ZoomAround(zoom, e.GetPosition(this));
            }

            e.Handled = true;
            return;
        }

        _wheelZoomRemainder = 0;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        ScrollByWheel(shift ? 0 : e.Delta.Y, shift ? e.Delta.Y : e.Delta.X);
        e.Handled = true;
    }

    /// <summary>The zoom after a Ctrl + wheel turn: one step per whole notch (a touch pad's parts are collected in <paramref name="remainder"/>).</summary>
    internal static double ZoomByWheel(double zoom, double delta, ref double remainder)
    {
        if (!double.IsFinite(delta))
        {
            return zoom;
        }

        remainder += delta;
        var steps = (int)Math.Clamp(Math.Truncate(remainder), -12, 12);
        remainder = Math.Clamp(remainder - Math.Truncate(remainder), -1, 1);
        for (var i = 0; i < Math.Abs(steps); i++)
        {
            zoom = steps > 0 ? ZoomSteps.Next(zoom) : ZoomSteps.Previous(zoom);
        }

        return zoom;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Meta | KeyModifiers.Control)) != 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.PageDown:
                PageDown();
                break;
            case Key.PageUp:
                PageUp();
                break;
            case Key.Home:
                ScrollToStart();
                break;
            case Key.End:
                ScrollToEnd();
                break;
            case Key.Down:
                ScrollByWheel(-1);
                break;
            case Key.Up:
                ScrollByWheel(1);
                break;
            case Key.Right:
                ScrollByWheel(0, -1);
                break;
            case Key.Left:
                ScrollByWheel(0, 1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnBarMoved(bool vertical, double value)
    {
        if (_syncingBars)
        {
            return;
        }

        ScrollTo(vertical ? _offsetX : value, vertical ? value : _offsetY);
    }

    private void UpdateScrollBars()
    {
        _syncingBars = true;
        try
        {
            Configure(_vertical, ContentHeight, _viewport.Height, _offsetY);
            Configure(_horizontal, ContentWidth, _viewport.Width, _offsetX);
        }
        finally
        {
            _syncingBars = false;
        }
    }

    private static void Configure(ScrollBar bar, double extent, double viewport, double offset)
    {
        bar.Minimum = 0;
        bar.Maximum = PageScroller.MaxOffset(extent, viewport);
        bar.ViewportSize = viewport;
        bar.SmallChange = PageScroller.WheelStep;
        bar.LargeChange = PageScroller.PageStep(viewport);
        bar.Value = offset;
    }

    // Measure, arrange and render.

    protected override Size MeasureOverride(Size availableSize)
    {
        _vertical.Measure(availableSize);
        _horizontal.Measure(availableSize);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _arranging = true;
        try
        {
            if (finalSize != _viewport)
            {
                _viewport = finalSize;
                _dirty = true;
                EnsureCurrent();
                UpdateScrollBars();
                InvalidateVisual();
                NotifyViewChanged();
            }

            var barWidth = _vertical.IsVisible ? _vertical.DesiredSize.Width : 0;
            var barHeight = _horizontal.IsVisible ? _horizontal.DesiredSize.Height : 0;
            _vertical.Arrange(new Rect(Math.Max(0, finalSize.Width - barWidth), 0, barWidth, Math.Max(0, finalSize.Height - barHeight)));
            _horizontal.Arrange(new Rect(0, Math.Max(0, finalSize.Height - barHeight), Math.Max(0, finalSize.Width - barWidth), barHeight));
        }
        finally
        {
            _arranging = false;
        }

        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background ?? DeskBrush(), bounds);
        if (_released)
        {
            return;
        }

        // Render only reads: the scroller follows the layout through EnsureCurrent, which the arrange pass and the runner's events call (a visual may not be changed in a render pass).
        var layout = _builtLayout;
        if (_scroller is not { Count: > 0 } scroller || layout is null)
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var (first, last) = scroller.Visible(_offsetY, _viewport.Height);
        var left = LeftShift(scroller);
        var shadow = ShadowOfPage();
        using (context.PushClip(bounds))
        {
            for (var index = first; index <= last && index < layout.LaidOutPages; index++)
            {
                var rect = Snap(scroller.RectOf(index).Translate(new Vector(left, -_offsetY)), scaling);

                // Read again for every frame: a provisional page may have been replaced since the last one.
                var page = layout.Page(index);
                if (page.IsStrip)
                {
                    context.FillRectangle(Paper, rect);
                }
                else
                {
                    context.DrawRectangle(Paper, null, rect, 0, 0, shadow);
                }

                context.Custom(new PageDrawOperation(rect, page, scroller.Scale, _images, _gate, OnDrawn));
            }
        }
    }

    private IBrush DeskBrush() =>
        this.TryFindResource("Raffica.Brush.Surface.Desk", ActualThemeVariant, out var found) && found is IBrush brush ? brush : Brushes.LightGray;

    private BoxShadows ShadowOfPage() =>
        this.TryFindResource("Raffica.Shadow.Page", ActualThemeVariant, out var found) && found is BoxShadows shadow ? shadow : default;

    // Whole device pixels at every edge, so the paper and the page drawn into it agree.
    private static Rect Snap(Rect rect, double scaling)
    {
        var l = Math.Round(rect.Left * scaling);
        var t = Math.Round(rect.Top * scaling);
        var r = Math.Round(rect.Right * scaling);
        var b = Math.Round(rect.Bottom * scaling);
        return new Rect(l / scaling, t / scaling, Math.Max(0, r - l) / scaling, Math.Max(0, b - t) / scaling);
    }

    // Render thread: only counts and posts.
    private void OnDrawn(int pageIndex)
    {
        Interlocked.Increment(ref _drawCount);
        if (pageIndex == 0 && Interlocked.Exchange(ref _firstSharpPosted, 1) == 0)
        {
            // The dispatcher of the control, read on the UI thread when it was made; the callback only posts to it.
            Dispatcher.Post(
                () =>
                {
                    FirstSharpPageShown = true;
                    FirstSharpPage?.Invoke();
                },
                DispatcherPriority.Background);
        }
    }

    // Change notification and the automation name.

    private void NotifyViewChanged()
    {
        if (_arranging || _raising)
        {
            if (!_viewChangedPosted)
            {
                _viewChangedPosted = true;
                Dispatcher.Post(RaisePostedViewChanged, DispatcherPriority.Normal);
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
            RefreshAutomationName();
            ViewChanged?.Invoke();
        }
        finally
        {
            _raising = false;
        }
    }

    private void UpdateAutomationName()
    {
        var count = PageCount;
        var current = CurrentPage;
        _namedFor = (current, count);
        var name = count > 0
            ? WriteStrings.Fill(WriteStrings.PageViewPageNameFormat, _tab.Title, current + 1, count)
            : WriteStrings.Fill(WriteStrings.PageViewNameFormat, _tab.Title);
        var old = AutomationProperties.GetName(this);
        if (name == old)
        {
            return;
        }

        AutomationProperties.SetName(this, name);
        (ControlAutomationPeer.FromElement(this) as PageViewAutomationPeer)?.NameChanged(old, name);
    }

    // Only a new page or a new page count builds the name again: scrolling inside a page costs nothing.
    private void RefreshAutomationName()
    {
        if (_namedFor != (CurrentPage, PageCount))
        {
            UpdateAutomationName();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PageViewAutomationPeer(this);

    /// <summary>The page view as a document for assistive technology: a control and content element of type Document, focusable, named after the file and the page. The text of the pages is not exposed.</summary>
    private sealed class PageViewAutomationPeer(PageView owner) : ControlAutomationPeer(owner)
    {
        /// <summary>How many times the name was announced as changed (the platform bridge is not there in a headless run; tests read it).</summary>
        internal int NameChanges { get; private set; }

        internal void NameChanged(string? oldName, string? newName)
        {
            NameChanges++;
            RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, oldName, newName);
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}
