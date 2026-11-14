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
        var reads = new Dictionary<string, int>();
        ReadOnlyMemory<byte> Read(string id)
        {
            reads[id] = reads.GetValueOrDefault(id) + 1;
            return bytes;
        }

        using var cache = new ImageCache(Read);

        // The layout gets the same function: it reads each picture's header at most once and decodes no bitmap; the cache is not touched.
        var body = FloatLayoutTests.InlinePicture(40f, 30f, "a") + FloatLayoutTests.InlinePicture(40f, 30f, "a");
        var layout = PageTestKit.Complete($"<w:p>{body}</w:p>" + PageTestKit.Sect(), options: new LayoutOptions(ImageBytes: Read));
        Assert.Equal(0, cache.DecodeCount);
        Assert.Equal(1, reads["document#a"]);
        var items = layout.Page(0).Items.OfType<ImageItem>().ToList();
        Assert.Equal(2, items.Count);

        // The first draw decodes; the second does not.
        var first = cache.Get(items[0].Image);
        Assert.NotNull(first);
        Assert.Equal(1, cache.DecodeCount);
        var second = cache.Get(items[1].Image);
        Assert.Same(first, second);
        Assert.Equal(8, first.Width);
        Assert.Equal(1, cache.DecodeCount);
        Assert.Equal(2, reads["document#a"]);
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
