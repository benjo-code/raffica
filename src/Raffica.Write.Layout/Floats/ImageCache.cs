using SkiaSharp;

namespace Raffica.Write.Layout;

/// <summary>
/// The memory limits of an <see cref="ImageCache"/>: a byte budget for decoded pixels and a cap on the pixels of one picture. A
/// decoded picture takes width x height x 4 bytes, and the cap must stay at or below half the budget (<c>MaxPixels * 4 * 2 &lt;= ByteBudget</c>),
/// so that any picture under the cap fits after evicting others. The constructor refuses an invalid pair; the cache checks the pair
/// again, so a <c>with</c> copy or <c>default</c> cannot bypass it.
/// </summary>
public readonly record struct ImageCacheLimits
{
    public ImageCacheLimits(long byteBudget, long maxPixels)
    {
        Validate(byteBudget, maxPixels);
        ByteBudget = byteBudget;
        MaxPixels = maxPixels;
    }

    /// <summary>Bytes of decoded pixels the cache keeps at most.</summary>
    public long ByteBudget { get; init; }

    /// <summary>Pixels of one picture above which it is not decoded.</summary>
    public long MaxPixels { get; init; }

    /// <summary>256 MiB of decoded pixels, 32,000,000 pixels per picture.</summary>
    public static ImageCacheLimits Default { get; } = new(256L * 1024 * 1024, 32_000_000);

    internal void Validate() => Validate(ByteBudget, MaxPixels);

    private static void Validate(long byteBudget, long maxPixels)
    {
        if (byteBudget <= 0 || maxPixels <= 0 || maxPixels > byteBudget / 8)
        {
            throw new ArgumentException("The pixel cap must be positive and at most one eighth of the byte budget (MaxPixels * 4 * 2 <= ByteBudget).");
        }
    }
}

/// <summary>
/// Decoded pictures for drawing. A picture is decoded when it is first asked for (<see cref="Get"/> with decode on get, the default),
/// or ahead of the draw by <see cref="Warm"/> and <see cref="Prefetch"/>; never at layout. The library never reads a package: the
/// caller passes the function that turns a relationship id into the bytes of the picture part. A picture that cannot be decoded (a
/// metafile, corrupt bytes, a missing part) or whose header says more than <see cref="ImageCacheLimits.MaxPixels"/> pixels answers
/// null, once, and the renderer draws a marked box instead; nothing throws.
/// </summary>
/// <remarks>
/// <para>Thread-safe: one lock guards the state; decoding runs outside it. Two threads may decode the same picture at the same time;
/// the first to finish is kept, the other disposes its copy unseen (wasted work, never counted twice or leaked).</para>
/// <para>The decoded bytes are kept under <see cref="ImageCacheLimits.ByteBudget"/>: inserting evicts the least recently used picture
/// first. An evicted picture (and everything <see cref="Clear"/> drops) is only retired, not disposed, because a running draw may hold
/// it; the owner calls <see cref="ReleaseRetired"/> after a draw returns.</para>
/// </remarks>
public sealed class ImageCache : IDisposable
{
    private readonly Func<string, ReadOnlyMemory<byte>> _bytes;
    private readonly ImageCacheLimits _limits;
    private readonly bool _decodeOnGet;
    private readonly Dictionary<string, Entry> _images = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly List<SKImage> _retired = [];
    private readonly object _gate = new();
    private int _decodes;
    private int _refused;
    private long _decodedBytes;
    private bool _disposed;

    /// <param name="bytes">Gives the bytes of the picture a relationship id stands for; may throw for a missing part.</param>
    /// <param name="limits">The memory limits; null is <see cref="ImageCacheLimits.Default"/>. An invalid pair (also a default struct) throws <see cref="ArgumentException"/>.</param>
    /// <param name="decodeOnGet">False: <see cref="Get"/> never decodes (like <see cref="TryGet"/>); only <see cref="Warm"/> and <see cref="Prefetch"/> do.</param>
    public ImageCache(Func<string, ReadOnlyMemory<byte>> bytes, ImageCacheLimits? limits = null, bool decodeOnGet = true)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = bytes;
        _limits = limits ?? ImageCacheLimits.Default;
        _limits.Validate();
        _decodeOnGet = decodeOnGet;
    }

    /// <summary>Raised after a <see cref="Prefetch"/> call decoded at least one picture, on the pool thread that decoded it, outside the lock.</summary>
    public event Action? Decoded;

    /// <summary>How many pictures were decoded and kept so far (an attempt that fails or is refused does not count).</summary>
    public int DecodeCount => Volatile.Read(ref _decodes);

    /// <summary>How many pictures were refused for having more pixels than the cap (not decoded).</summary>
    public int RefusedCount => Volatile.Read(ref _refused);

    /// <summary>Bytes of the decoded pictures that are kept (retired pictures are not counted).</summary>
    public long DecodedBytes
    {
        get
        {
            lock (_gate)
            {
                return _decodedBytes;
            }
        }
    }

    /// <summary>The decoded picture, or null when it cannot be drawn. Decodes on the calling thread when decode on get is on. The cache owns the image: do not dispose it.</summary>
    public SKImage? Get(ImageRef image)
    {
        if (!_decodeOnGet)
        {
            return TryGet(image);
        }

        return Obtain(image.Id, decode: true, out _);
    }

    /// <summary>The picture when it is already decoded (and marks it as used), otherwise null; never decodes.</summary>
    public SKImage? TryGet(ImageRef image) => Obtain(image.Id, decode: false, out _);

    /// <summary>Decodes the pictures that are not known yet, on the calling thread.</summary>
    public void Warm(IEnumerable<ImageRef> images) => WarmCore(images);

    /// <summary>Runs <see cref="Warm"/> on a pool thread and raises <see cref="Decoded"/> once when it decoded anything.</summary>
    public void Prefetch(IEnumerable<ImageRef> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        var list = images.ToArray();
        _ = Task.Run(() =>
        {
            try
            {
                if (WarmCore(list))
                {
                    Decoded?.Invoke();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A prefetch is an optimisation: whatever fails here is retried by the draw (or not needed).
            }
        });
    }

    /// <summary>Disposes the pictures that were evicted or cleared since the last call. Call it after a draw returned, never during one.</summary>
    public void ReleaseRetired()
    {
        SKImage[] retired;
        lock (_gate)
        {
            retired = [.. _retired];
            _retired.Clear();
        }

        foreach (var image in retired)
        {
            image.Dispose();
        }
    }

    /// <summary>
    /// Drops every decoded picture (they are decoded again when asked for, refused and unreadable ones included). The pictures are
    /// retired, not disposed: a draw may hold one, so they go with <see cref="ReleaseRetired"/>.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var entry in _images.Values)
            {
                if (entry.Image is not null)
                {
                    _retired.Add(entry.Image);
                }
            }

            _images.Clear();
            _lru.Clear();
            _decodedBytes = 0;
        }
    }

    public void Dispose()
    {
        List<SKImage> all;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            all = [.. _retired];
            foreach (var entry in _images.Values)
            {
                if (entry.Image is not null)
                {
                    all.Add(entry.Image);
                }
            }

            _retired.Clear();
            _images.Clear();
            _lru.Clear();
            _decodedBytes = 0;
        }

        foreach (var image in all)
        {
            image.Dispose();
        }
    }

    private bool WarmCore(IEnumerable<ImageRef> images)
    {
        var any = false;
        foreach (var image in images)
        {
            Obtain(image.Id, decode: true, out var decodedNow);
            any |= decodedNow;
        }

        return any;
    }

    private SKImage? Obtain(string id, bool decode, out bool decodedNow)
    {
        decodedNow = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (_images.TryGetValue(id, out var known))
            {
                if (known.Node is not null)
                {
                    _lru.Remove(known.Node);
                    _lru.AddFirst(known.Node);
                }

                return known.Image;
            }
        }

        if (!decode)
        {
            return null;
        }

        // Decoding runs outside the lock.
        var (image, refused) = Decode(id);
        var bytes = image is null ? 0L : (long)image.Width * image.Height * 4;

        lock (_gate)
        {
            if (_disposed)
            {
                image?.Dispose();
                return null;
            }

            if (_images.TryGetValue(id, out var raced))
            {
                // Another thread was first: keep its picture, drop ours (never handed out).
                image?.Dispose();
                return raced.Image;
            }

            if (refused)
            {
                _refused++;
            }

            var entry = new Entry(image, bytes);
            if (image is not null)
            {
                while (_decodedBytes + bytes > _limits.ByteBudget && _lru.Last is { } oldest)
                {
                    Evict(oldest);
                }

                entry.Node = _lru.AddFirst(id);
                _decodedBytes += bytes;
                _decodes++;
                decodedNow = true;
            }

            _images[id] = entry;
            return image;
        }
    }

    private void Evict(LinkedListNode<string> node)
    {
        _lru.Remove(node);
        if (_images.Remove(node.Value, out var entry) && entry.Image is not null)
        {
            _decodedBytes -= entry.Bytes;
            _retired.Add(entry.Image);
        }
    }

    private (SKImage? Image, bool Refused) Decode(string id)
    {
        try
        {
            var bytes = _bytes(id);
            var header = ImageHeader.Read(bytes);
            if (header is null)
            {
                return (null, false);
            }

            if ((long)header.Value.Width * header.Value.Height > _limits.MaxPixels)
            {
                return (null, true);
            }

            using var data = SKData.CreateCopy(bytes.Span);
            using var encoded = SKImage.FromEncodedData(data);

            // The pixels are decoded now, once, so that drawing does not decode again.
            var decoded = encoded?.ToRasterImage(true);
            if (decoded is not null && (long)decoded.Width * decoded.Height > _limits.MaxPixels)
            {
                // The header understated the size: refuse like any other oversized picture.
                decoded.Dispose();
                return (null, true);
            }

            return (decoded, false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, false);
        }
    }

    private sealed class Entry(SKImage? image, long bytes)
    {
        public SKImage? Image { get; } = image;

        public long Bytes { get; } = bytes;

        public LinkedListNode<string>? Node { get; set; }
    }
}

/// <summary>What the layout needs to know of a picture without decoding it.</summary>
internal readonly record struct ImageInfoPx(int Width, int Height);

internal static class ImageFormats
{
    /// <summary>True for an EMF or WMF header (Skia cannot decode these).</summary>
    public static bool IsMetafile(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 44 && bytes[0] == 1 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0 && bytes[40] == 0x20 && bytes[41] == 0x45 && bytes[42] == 0x4D && bytes[43] == 0x46)
        {
            return true;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xD7 && bytes[1] == 0xCD && bytes[2] == 0xC6 && bytes[3] == 0x9A)
        {
            return true;
        }

        // A WMF without the placeable header: type 1 or 2 (memory or disk), header size 9 words.
        return bytes.Length >= 4 && bytes[0] is 1 or 2 && bytes[1] == 0 && bytes[2] == 9 && bytes[3] == 0;
    }
}

/// <summary>
/// Asks the picture bytes function for the header of each picture the layout meets, once: is it something Skia can draw, and how
/// many pixels is it. Only the header is read (<see cref="SKCodec"/>), no bitmap is decoded. Without a function every picture is
/// assumed fine and has no known size.
/// </summary>
internal sealed class ImageProbe
{
    private readonly Func<string, ReadOnlyMemory<byte>>? _bytes;
    private readonly Dictionary<string, ImageInfoPx?> _known = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ImageProbe(Func<string, ReadOnlyMemory<byte>>? bytes) => _bytes = bytes;

    /// <summary>False when no function is known: <see cref="Info"/> then answers (0, 0) for everything.</summary>
    public bool Active => _bytes is not null;

    /// <summary>The pixel size of the picture, or null when it cannot be drawn (metafile, corrupt, missing). (0, 0) when no function is known.</summary>
    public ImageInfoPx? Info(string id)
    {
        if (_bytes is null)
        {
            return new ImageInfoPx(0, 0);
        }

        lock (_gate)
        {
            if (!_known.TryGetValue(id, out var info))
            {
                info = Read(id);
                _known[id] = info;
            }

            return info;
        }
    }

    private ImageInfoPx? Read(string id)
    {
        try
        {
            return ImageHeader.Read(_bytes!(id));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
