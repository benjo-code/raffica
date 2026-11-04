using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;

namespace Raffica.Pdf.Viewing;

/// <summary>
/// The bridge from Core's pixels to what Avalonia draws: converts a <see cref="RenderedImage"/> (BGRA, opaque, top row first) of the render
/// cache into a <see cref="WriteableBitmap"/> without swapping channels, and keeps the bitmaps in a least-recently-used list that never
/// holds more than the byte budget. A bitmap the user may still see is handed out again without converting a second time. For the UI
/// thread only; the render cache itself is thread-safe and stays the owner of the pixels.
/// </summary>
/// <param name="maxBytes">The most bytes the bitmaps may occupy (4 bytes per pixel).</param>
internal sealed class TileBitmaps(int maxBytes)
{
    /// <summary>The default budget: 128 MiB of bitmaps next to the 256 MiB of the render cache.</summary>
    public const int DefaultMaxBytes = 128 * 1024 * 1024;

    private readonly Dictionary<TileKey, LinkedListNode<Entry>> _entries = [];

    // Most recently used first; eviction takes from the end.
    private readonly LinkedList<Entry> _order = new();

    // Evicted bitmaps wait here until the next drawing pass starts: the pass that evicted one may still draw it.
    private readonly List<WriteableBitmap> _retired = [];

    /// <summary>The number of bitmaps held.</summary>
    public int Count => _entries.Count;

    /// <summary>The bytes the held bitmaps occupy; never more than the budget.</summary>
    public long Bytes { get; private set; }

    /// <summary>
    /// Gets the bitmap of <paramref name="key"/>: the one already held, else a conversion of the image in <paramref name="cache"/>.
    /// <see langword="null"/> when the cache has no image of the key, or when its bitmap alone would exceed the budget.
    /// </summary>
    public Bitmap? Get(TileKey key, RenderCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (_entries.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Bitmap;
        }

        if (!cache.TryGet(key, out var image))
        {
            return null;
        }

        var cost = (long)image.Width * image.Height * 4;

        // An image without pixels has no bitmap (a WriteableBitmap cannot be empty), and one above the budget would evict everything.
        if (cost <= 0 || cost > maxBytes)
        {
            return null;
        }

        var bitmap = Convert(image);
        _entries[key] = _order.AddFirst(new Entry(key, bitmap, cost));
        Bytes += cost;
        while (Bytes > maxBytes && _order.Last is { } oldest)
        {
            Remove(oldest);
        }

        return bitmap;
    }

    /// <summary>Drops every bitmap of the given pages (previews and tiles at every scale); the render cache is not touched.</summary>
    public void Evict(IEnumerable<int> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var set = pages as IReadOnlySet<int> ?? pages.ToHashSet();
        if (set.Count == 0)
        {
            return;
        }

        var node = _order.First;
        while (node is not null)
        {
            var next = node.Next;
            if (set.Contains(node.Value.Key.Page))
            {
                Remove(node);
            }

            node = next;
        }
    }

    /// <summary>Drops every bitmap (the theme of the pages changed).</summary>
    public void Clear()
    {
        foreach (var entry in _order)
        {
            _retired.Add(entry.Bitmap);
        }

        _entries.Clear();
        _order.Clear();
        Bytes = 0;
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove(node.Value.Key);
        _order.Remove(node);
        Bytes -= node.Value.Cost;

        _retired.Add(node.Value.Bitmap);
    }

    /// <summary>
    /// Disposes the bitmaps evicted since the last call. Call it at the start of a drawing pass, never in the middle of one: a pass
    /// may still draw a bitmap that its own lookups evicted. A frame that was already recorded keeps its own reference to the pixels.
    /// </summary>
    public void ReleaseRetired()
    {
        foreach (var bitmap in _retired)
        {
            bitmap.Dispose();
        }

        _retired.Clear();
    }

    private static unsafe WriteableBitmap Convert(RenderedImage image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var frame = bitmap.Lock();
        var source = image.Pixels.Span;
        var rowBytes = image.Width * 4;
        for (var y = 0; y < image.Height; y++)
        {
            var target = new Span<byte>((byte*)frame.Address + ((long)y * frame.RowBytes), rowBytes);
            source.Slice(y * image.Stride, rowBytes).CopyTo(target);
        }

        return bitmap;
    }

    private readonly record struct Entry(TileKey Key, WriteableBitmap Bitmap, long Cost);
}
