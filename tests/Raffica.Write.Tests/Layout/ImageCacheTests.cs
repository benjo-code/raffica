using Raffica.Write.Layout;
using SkiaSharp;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class ImageCacheTests
{
    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(8, 6);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void ImagesDecodeOnceAndLazily()
    {
        var bytes = Png();
        var reads = 0;
        using var cache = new ImageCache(id =>
        {
            reads++;
            return bytes;
        });

        // Laying out a picture does not touch the cache: only a draw does.
        var body = FloatLayoutTests.InlinePicture(40f, 30f, "a") + FloatLayoutTests.InlinePicture(40f, 30f, "a");
        var layout = PageTestKit.Complete($"<w:p>{body}</w:p>" + PageTestKit.Sect());
        Assert.Equal(0, cache.DecodeCount);
        Assert.Equal(0, reads);
        Assert.Equal(2, layout.Page(0).Items.OfType<ImageItem>().Count());

        var first = cache.Get(new ImageRef("a"));
        var second = cache.Get(new ImageRef("a"));
        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(8, first.Width);
        Assert.Equal(1, cache.DecodeCount);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void AnImageThatCannotBeDecodedIsAnsweredOnceWithNull()
    {
        var reads = 0;
        using var cache = new ImageCache(id =>
        {
            reads++;
            return new byte[] { 1, 2, 3 };
        });
        Assert.Null(cache.Get(new ImageRef("x")));
        Assert.Null(cache.Get(new ImageRef("x")));
        Assert.Equal(1, reads);
        Assert.Equal(0, cache.DecodeCount);
    }

    [Fact]
    public void AReaderThatThrowsGivesNullNotAnException()
    {
        using var cache = new ImageCache(_ => throw new InvalidOperationException("part is gone"));
        Assert.Null(cache.Get(new ImageRef("x")));
    }

    [Fact]
    public void AMetafileIsNotDecodable()
    {
        var emf = new byte[100];
        emf[0] = 1;
        emf[40] = 0x20;
        emf[41] = 0x45;
        emf[42] = 0x4D;
        emf[43] = 0x46;
        using var cache = new ImageCache(_ => emf);
        Assert.Null(cache.Get(new ImageRef("emf")));
        Assert.Equal(0, cache.DecodeCount);
    }

    [Fact]
    public void ADisposedCacheAnswersNull()
    {
        var bytes = Png();
        var cache = new ImageCache(_ => bytes);
        Assert.NotNull(cache.Get(new ImageRef("a")));
        cache.Dispose();
        Assert.Null(cache.Get(new ImageRef("a")));
    }
}
