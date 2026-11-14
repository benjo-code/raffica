using SkiaSharp;

namespace Raffica.Write.Layout;

/// <summary>
/// Decoded pictures for drawing. A picture is decoded the first time it is asked for (a draw), never at layout, and kept: the
/// next draw gets the same <see cref="SKImage"/>. The library never reads a package: the caller passes the function that turns a
/// relationship id into the bytes of the picture part. A picture that cannot be decoded (a metafile, corrupt bytes, a missing part)
/// answers null, once, and the renderer draws a marked box instead; nothing throws.
/// </summary>
/// <remarks>The cache grows with the pictures drawn; <see cref="Clear"/> drops them all. It is safe to call from any thread.</remarks>
public sealed class ImageCache : IDisposable
{
    private readonly Func<string, ReadOnlyMemory<byte>> _bytes;
    private readonly Dictionary<string, SKImage?> _images = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _decodes;
    private bool _disposed;

    /// <param name="bytes">Gives the bytes of the picture a relationship id stands for; may throw for a missing part.</param>
    public ImageCache(Func<string, ReadOnlyMemory<byte>> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = bytes;
    }

    /// <summary>How many pictures were decoded so far (an attempt that fails does not count).</summary>
    public int DecodeCount => Volatile.Read(ref _decodes);

    /// <summary>The decoded picture, or null when it cannot be drawn. The cache owns the image: do not dispose it.</summary>
    public SKImage? Get(ImageRef image)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (_images.TryGetValue(image.Id, out var known))
            {
                return known;
            }

            var decoded = Decode(image.Id);
            _images[image.Id] = decoded;
            return decoded;
        }
    }

    /// <summary>Drops every decoded picture (they are decoded again when drawn).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var image in _images.Values)
            {
                image?.Dispose();
            }

            _images.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var image in _images.Values)
            {
                image?.Dispose();
            }

            _images.Clear();
        }
    }

    private SKImage? Decode(string id)
    {
        try
        {
            var bytes = _bytes(id);
            if (bytes.IsEmpty || ImageFormats.IsMetafile(bytes.Span))
            {
                return null;
            }

            using var data = SKData.CreateCopy(bytes.Span);
            using var encoded = SKImage.FromEncodedData(data);

            // The pixels are decoded now, once, so that drawing does not decode again.
            var decoded = encoded?.ToRasterImage(true);
            if (decoded is not null)
            {
                Interlocked.Increment(ref _decodes);
            }

            return decoded;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
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
    private const int HeaderBytes = 256 * 1024;

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
            var bytes = _bytes!(id);
            if (bytes.IsEmpty || ImageFormats.IsMetafile(bytes.Span))
            {
                return null;
            }

            // The header is all that is read, and it lies in the first bytes (a long EXIF block of a photo is the longest case).
            using var data = SKData.CreateCopy(bytes.Span[..Math.Min(bytes.Length, HeaderBytes)]);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                return null;
            }

            return new ImageInfoPx(codec.Info.Width, codec.Info.Height);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
