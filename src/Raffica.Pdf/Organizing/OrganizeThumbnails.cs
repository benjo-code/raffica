using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Organizing;

/// <summary>
/// Where the pictures of the organize list come from: the thumbnails of the tab for the pages of the open document, one
/// <see cref="ThumbnailSource"/> of its own for every inserted file (made when its pages are first realised, disposed when its last slot
/// leaves the plan or the mode ends, always before the file is closed). A blank page has no source: it is drawn. The converted bitmaps come
/// from the bridge of the page view (<see cref="TileBitmaps"/>), one set per source because page numbers repeat from file to file. Nothing is
/// requested while the list is not on screen or before the tab drew its first sharp page; the caller says so with <see cref="Replan"/>.
/// UI thread.
/// </summary>
internal sealed class OrganizeThumbnails : IDisposable
{
    private const int PrimaryBitmapBudget = 16 * 1024 * 1024;
    private const int InsertedBitmapBudget = 8 * 1024 * 1024;

    // The stand-in for the size of a page that is not known yet (A4 in points).
    private static readonly PdfPageSize FallbackPageSize = new(595.28, 841.89);

    private readonly PdfTab _tab;
    private readonly OrganizeSession _session;
    private readonly Action _redraw;
    private readonly Action _resized;
    private readonly Action _replan;
    private readonly Route _primary = new(PrimaryBitmapBudget, []);
    private readonly Dictionary<PageSourceId, Route> _inserted = [];
    private bool _disposed;

    /// <param name="tab">The tab whose thumbnails and inserted files are used.</param>
    /// <param name="session">The session that holds the inserted files.</param>
    /// <param name="redraw">Asks the list to draw its thumbnails again (a picture arrived).</param>
    /// <param name="resized">Asks the list to measure its thumbnails again (the sizes of an inserted file arrived).</param>
    /// <param name="replan">Asks the list to plan the requests again (the theme changed).</param>
    public OrganizeThumbnails(PdfTab tab, OrganizeSession session, Action redraw, Action resized, Action replan)
    {
        _tab = tab;
        _session = session;
        _redraw = redraw;
        _resized = resized;
        _replan = replan;
    }

    /// <summary>The number of inserted files that have a source of thumbnails now.</summary>
    public int InsertedSourceCount => _inserted.Count;

    /// <summary>The number of converted bitmaps held (for tests).</summary>
    public int BitmapCount => _primary.Bitmaps.Count + _inserted.Values.Sum(r => r.Bitmaps.Count);

    /// <summary>Whether the pages are drawn on the dark ground.</summary>
    public bool IsDark => (_primary.Source?.PageTheme ?? _tab.Session.PageTheme) == PageTheme.Dark;

    /// <summary>The source of the thumbnails of an inserted file, or <see langword="null"/> if it has none now.</summary>
    public ThumbnailSource? InsertedSource(PageSourceId id) => _inserted.TryGetValue(id, out var route) ? route.Source : null;

    /// <summary>The requests handed to the scheduler of a source by the last plan (<c>default</c> is the open document); empty while it requests nothing.</summary>
    public IReadOnlyList<RenderRequest> PlanOf(PageSourceId id) =>
        id.Value == 0 ? _primary.Plan : _inserted.TryGetValue(id, out var route) ? route.Plan : [];

    /// <summary>The size of the page of a slot in points before its extra rotation: the real one once it is known, else a stand-in.</summary>
    public PdfPageSize SizeOf(PageSlot slot)
    {
        if (slot.Blank is { } blank)
        {
            var size = new PdfPageSize(blank.Width, blank.Height);
            return Usable(size) ? size : FallbackPageSize;
        }

        IReadOnlyList<PdfPageSize> sizes = slot.Source.Value == 0
            ? _tab.Session.LoadedSizes
            : _inserted.TryGetValue(slot.Source, out var route) ? route.Sizes : [];
        return slot.SourceIndex >= 0 && slot.SourceIndex < sizes.Count && Usable(sizes[slot.SourceIndex]) ? sizes[slot.SourceIndex] : FallbackPageSize;
    }

    /// <summary>The bitmap of the thumbnail of a slot if it has been rendered, else <see langword="null"/> (always for a blank page). Called while the thumbnail is drawn.</summary>
    public Bitmap? BitmapFor(PageSlot slot)
    {
        if (_disposed || slot.Blank is not null || slot.SourceIndex < 0)
        {
            return null;
        }

        var route = slot.Source.Value == 0 ? _primary : _inserted.GetValueOrDefault(slot.Source);
        if (route?.Source is not { } source)
        {
            return null;
        }

        if (source.PageTheme != route.BitmapTheme)
        {
            // The theme of the pages changed: the converted bitmaps show the old one, and Core has emptied the cache.
            route.Bitmaps.Clear();
            route.BitmapTheme = source.PageTheme;
            _replan();
        }

        return route.Bitmaps.Get(new TileKey(slot.SourceIndex, TileKey.PreviewScaleKey, 0, 0), source.Cache);
    }

    /// <summary>
    /// Plans the thumbnails of the realised slots and hands each source its plan. With <paramref name="onScreen"/> false (the list is not shown,
    /// or the tab has not drawn its first sharp page) every source is told to want nothing and the bitmaps are let go.
    /// </summary>
    /// <param name="realized">The slots whose items the list has realised, the most important first.</param>
    /// <param name="renderScaling">Device pixels per device-independent pixel.</param>
    /// <param name="onScreen">Whether anything may be requested.</param>
    public void Replan(IReadOnlyList<PageSlot> realized, double renderScaling, bool onScreen)
    {
        if (_disposed)
        {
            return;
        }

        _primary.Bitmaps.ReleaseRetired();
        if (!onScreen)
        {
            StopAll();
            return;
        }

        var wanted = new Dictionary<PageSourceId, List<int>>();
        foreach (var slot in realized)
        {
            if (slot.Blank is not null || slot.SourceIndex < 0)
            {
                continue;
            }

            if (!wanted.TryGetValue(slot.Source, out var pages))
            {
                wanted[slot.Source] = pages = [];
            }

            pages.Add(slot.SourceIndex);
        }

        PlanPrimary(wanted.GetValueOrDefault(default) ?? [], renderScaling);
        foreach (var (id, pages) in wanted)
        {
            if (id.Value != 0 && RouteOf(id) is { } route)
            {
                Plan(route, route.Sizes, pages, renderScaling);
            }
        }

        foreach (var (id, route) in _inserted)
        {
            route.Bitmaps.ReleaseRetired();
            if (!wanted.ContainsKey(id))
            {
                Stop(route);
            }
        }
    }

    /// <summary>Disposes the source of every inserted file that no slot of the plan names any more.</summary>
    public void Prune(IReadOnlySet<int> usedSources)
    {
        foreach (var id in _inserted.Keys.Where(id => !usedSources.Contains(id.Value)).ToList())
        {
            Release(id);
        }
    }

    /// <summary>Disposes the sources of all inserted files (the mode ends: the files are closed next).</summary>
    public void ReleaseInserted()
    {
        foreach (var id in _inserted.Keys.ToList())
        {
            Release(id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseInserted();
        Stop(_primary);
        if (_primary.Source is { } source)
        {
            source.Scheduler.TileReady -= _primary.OnTileReady;
        }

        _primary.Source = null;
    }

    private static bool Usable(PdfPageSize size) => double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width > 0 && size.Height > 0;

    private void PlanPrimary(IReadOnlyList<int> pages, double renderScaling)
    {
        var source = _tab.Session.EnsureThumbnails();
        if (!ReferenceEquals(source, _primary.Source))
        {
            // The document was replaced: the thumbnails are made anew.
            if (_primary.Source is { } old)
            {
                old.Scheduler.TileReady -= _primary.OnTileReady;
            }

            _primary.Source = source;
            if (source is not null)
            {
                _primary.OnTileReady = Ready(_primary);
                source.Scheduler.TileReady += _primary.OnTileReady;
                _primary.BitmapTheme = source.PageTheme;
            }
        }

        if (source is null)
        {
            _primary.Plan = [];
            return;
        }

        Plan(_primary, _tab.Session.LoadedSizes, pages, renderScaling);
    }

    private void Plan(Route route, IReadOnlyList<PdfPageSize> sizes, IReadOnlyList<int> pages, double renderScaling)
    {
        if (route.Source is not { } source)
        {
            return;
        }

        if (!ReferenceEquals(route, _primary) && source.PageTheme != _tab.Session.PageTheme)
        {
            source.SetPageTheme(_tab.Session.PageTheme);
        }

        var plan = ThumbnailPlanner.Plan(sizes, pages, renderScaling);
        route.Plan = plan;
        route.Requesting = true;
        source.Scheduler.Update(plan);
    }

    // The route of an inserted file, made on first need: a source of its own and the sizes of its pages, read off the UI thread.
    private Route? RouteOf(PageSourceId id)
    {
        if (_inserted.TryGetValue(id, out var existing))
        {
            return existing;
        }

        if (!_session.Inserted.TryGetValue(id, out var file))
        {
            return null;
        }

        var route = new Route(InsertedBitmapBudget, new PdfPageSize[file.PageCount])
        {
            Source = ThumbnailSource.Create(file.Document, _tab.Session.PageTheme),
        };
        route.BitmapTheme = route.Source.PageTheme;
        route.OnTileReady = Ready(route);
        route.Source.Scheduler.TileReady += route.OnTileReady;
        _inserted[id] = route;
        _ = LoadSizesAsync(route, file);
        return route;
    }

    private async Task LoadSizesAsync(Route route, InsertedFile file)
    {
        try
        {
            var sizes = await file.Document.GetPageSizesAsync(0, file.PageCount, PdfPriority.Background).ConfigureAwait(false);
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (route.Source is null || _disposed)
                    {
                        return;
                    }

                    for (var i = 0; i < sizes.Count && i < route.Sizes.Length; i++)
                    {
                        route.Sizes[i] = sizes[i];
                    }

                    _resized();
                    _replan();
                },
                DispatcherPriority.Normal);
        }
        catch (Exception)
        {
            // The file was closed meanwhile (the mode ended) or a page cannot be read: its thumbnails keep the stand-in size, and nothing awaits this.
        }
    }

    // Pool thread: only posts, and one post carries every picture that arrived meanwhile.
    private Action<TileKey> Ready(Route route) => _ =>
    {
        lock (route)
        {
            if (route.ReadyPosted)
            {
                return;
            }

            route.ReadyPosted = true;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                lock (route)
                {
                    route.ReadyPosted = false;
                }

                if (!_disposed)
                {
                    _redraw();
                }
            },
            DispatcherPriority.Render);
    };

    private void StopAll()
    {
        Stop(_primary);
        foreach (var route in _inserted.Values)
        {
            Stop(route);
        }
    }

    // Stops the work of a source and gives its memory back: no wish, no converted bitmaps, an empty cache.
    private static void Stop(Route route)
    {
        route.Plan = [];
        if (route.Requesting && route.Source is { } source)
        {
            source.Scheduler.Update([]);
            source.Cache.Clear();
        }

        route.Requesting = false;
        route.Bitmaps.Clear();
        route.Bitmaps.ReleaseRetired();
    }

    private void Release(PageSourceId id)
    {
        if (!_inserted.Remove(id, out var route))
        {
            return;
        }

        Stop(route);
        if (route.Source is { } source)
        {
            source.Scheduler.TileReady -= route.OnTileReady;
            source.Dispose();
        }

        route.Source = null;
    }

    private sealed class Route(int bitmapBudget, PdfPageSize[] sizes)
    {
        public ThumbnailSource? Source { get; set; }

        public TileBitmaps Bitmaps { get; } = new(bitmapBudget);

        public PdfPageSize[] Sizes { get; } = sizes;

        public IReadOnlyList<RenderRequest> Plan { get; set; } = [];

        public PageTheme BitmapTheme { get; set; }

        public bool Requesting { get; set; }

        public bool ReadyPosted { get; set; }

        public Action<TileKey> OnTileReady { get; set; } = _ => { };
    }
}

/// <summary>
/// The picture of one page of the plan: the paper, the thumbnail if it has been rendered (turned by the slot's extra rotation), or just the
/// paper for a blank page, and a thin line. A quarter turn swaps the width and the height of the box.
/// </summary>
internal sealed class OrganizeThumbnail : Control
{
    private const double MinThumbnailHeight = 16;
    private const double MaxHeightFactor = 4;

    private static readonly IBrush LightGround = Brushes.White;
    private static readonly IBrush DarkGround = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromUInt32(PageThemeColors.DarkBackground));
    private static readonly Avalonia.Media.RenderOptions Smooth = new() { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality };

    private readonly OrganizeThumbnails _thumbnails;

    public OrganizeThumbnail(OrganizeThumbnails thumbnails, PageSlot slot)
    {
        _thumbnails = thumbnails;
        Slot = slot;
    }

    public PageSlot Slot { get; }

    private bool Quarter => Slot.ExtraRotation is 90 or 270;

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = _thumbnails.SizeOf(Slot);
        var height = Math.Ceiling(Math.Clamp(ThumbnailPlanner.HeightFor(size), MinThumbnailHeight, ThumbnailPlanner.Width * MaxHeightFactor));
        var box = Quarter ? new Size(height, ThumbnailPlanner.Width) : new Size(ThumbnailPlanner.Width, height);

        // A tall page turned on its side would be wider than the pane: the whole picture shrinks to fit.
        var fit = Math.Min(1, ThumbnailPlanner.Width / box.Width);
        return new Size(Math.Ceiling(box.Width * fit), Math.Ceiling(box.Height * fit));
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.FillRectangle(_thumbnails.IsDark ? DarkGround : LightGround, rect);
        if (_thumbnails.BitmapFor(Slot) is { } bitmap)
        {
            using (context.PushRenderOptions(Smooth))
            {
                // The bitmap is the page as it is; it is drawn into a box of its own shape around the centre and turned with it.
                var center = rect.Center;
                var width = Quarter ? rect.Height : rect.Width;
                var height = Quarter ? rect.Width : rect.Height;
                var turn = Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(Slot.ExtraRotation * Math.PI / 180) * Matrix.CreateTranslation(center.X, center.Y);
                using (context.PushTransform(turn))
                {
                    context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(center.X - width / 2, center.Y - height / 2, width, height));
                }
            }
        }

        if (this.TryFindResource("Raffica.Brush.Rule", ActualThemeVariant, out var value) && value is IBrush brush)
        {
            context.DrawRectangle(null, new Pen(brush, 1), rect.Deflate(0.5));
        }
    }
}
