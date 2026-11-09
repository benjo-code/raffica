using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class TileBitmapsTests
{
    private static RenderedImage Solid(int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 0xFF;
        }

        return new RenderedImage(width, height, width * 4, pixels);
    }

    private static TileKey Key(int page, int column = 0) => new(page, 1000, column, 0);

    [AvaloniaFact]
    public void PixelsAreConvertedBgraWithoutSwappingChannels()
    {
        // Row 0: pure red, pure green; row 1: pure blue, a mixed colour. The stride is 12 (4 bytes of padding per row) and must be honoured.
        byte[] pixels =
        [
            0, 0, 255, 255, 0, 255, 0, 255, 9, 9, 9, 9,
            255, 0, 0, 255, 10, 20, 30, 255, 9, 9, 9, 9,
        ];
        var cache = new RenderCache();
        var key = Key(0);
        cache.Add(key, new RenderedImage(2, 2, 12, pixels));
        var bitmaps = new TileBitmaps(1024);

        var bitmap = Assert.IsType<WriteableBitmap>(bitmaps.Get(key, cache));

        Assert.Equal(new PixelSize(2, 2), bitmap.PixelSize);
        Assert.Equal(PixelFormat.Bgra8888, bitmap.Format);
        Assert.Equal(AlphaFormat.Opaque, bitmap.AlphaFormat);
        using var frame = bitmap.Lock();
        var read = new byte[16];
        for (var y = 0; y < 2; y++)
        {
            Marshal.Copy(frame.Address + (y * frame.RowBytes), read, y * 8, 8);
        }

        Assert.Equal<byte>([0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 10, 20, 30, 255], read);
    }

    [AvaloniaFact]
    public void StaysWithinTheByteBudgetAndEvictsLeastRecentlyUsed()
    {
        var cache = new RenderCache();
        for (var page = 0; page < 5; page++)
        {
            cache.Add(Key(page), Solid(4, 4, 1, 2, 3));
        }

        var bitmaps = new TileBitmaps(3 * 64);

        var first = bitmaps.Get(Key(0), cache);
        var second = bitmaps.Get(Key(1), cache);
        bitmaps.Get(Key(2), cache);
        Assert.Equal(3, bitmaps.Count);
        Assert.Equal(192, bitmaps.Bytes);

        // Page 0 is used again, so page 1 is the least recently used one when page 3 comes.
        Assert.Same(first, bitmaps.Get(Key(0), cache));
        bitmaps.Get(Key(3), cache);
        Assert.Equal(3, bitmaps.Count);
        Assert.Equal(192, bitmaps.Bytes);
        Assert.Same(first, bitmaps.Get(Key(0), cache));

        // Page 1 was evicted: asking for it converts it again (a new bitmap), which evicts the next least recently used one.
        Assert.NotSame(second, bitmaps.Get(Key(1), cache));
        Assert.Equal(3, bitmaps.Count);
        Assert.Equal(192, bitmaps.Bytes);
    }

    [AvaloniaFact]
    public void AnImageThatAloneExceedsTheBudgetIsNotHeld()
    {
        var cache = new RenderCache();
        cache.Add(Key(0), Solid(8, 8, 1, 2, 3));
        var bitmaps = new TileBitmaps(100);

        Assert.Null(bitmaps.Get(Key(0), cache));
        Assert.Equal(0, bitmaps.Count);
        Assert.Equal(0, bitmaps.Bytes);
    }

    [AvaloniaFact]
    public void AMissingImageGivesNull()
    {
        var bitmaps = new TileBitmaps(1024);
        Assert.Null(bitmaps.Get(Key(7), new RenderCache()));
    }

    [AvaloniaFact]
    public void EvictRemovesAllTilesOfAPage()
    {
        var cache = new RenderCache();
        TileKey[] keys = [new(0, TileKey.PreviewScaleKey, 0, 0), Key(0, 0), Key(0, 1), new(1, TileKey.PreviewScaleKey, 0, 0), Key(2)];
        foreach (var key in keys)
        {
            cache.Add(key, Solid(2, 2, 1, 2, 3));
        }

        var bitmaps = new TileBitmaps(10_000);
        foreach (var key in keys)
        {
            bitmaps.Get(key, cache);
        }

        Assert.Equal(5, bitmaps.Count);

        bitmaps.Evict([0, 2]);

        Assert.Equal(1, bitmaps.Count);
        Assert.Equal(16, bitmaps.Bytes);
        Assert.NotNull(bitmaps.Get(keys[3], cache));
    }
}
