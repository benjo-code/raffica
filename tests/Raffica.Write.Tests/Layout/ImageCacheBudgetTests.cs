using Raffica.Write.Layout;
using SkiaSharp;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class ImageCacheBudgetTests
{
    // 10 x 10 pixels = 400 decoded bytes; the budget holds three, the cap is 100 pixels (half would be 150).
    private const int Side = 10;
    private const long OneImage = Side * Side * 4;
    private static readonly ImageCacheLimits s_three = new(OneImage * 3, Side * Side);

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Green);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static ImageCache Cache(ImageCacheLimits? limits = null, bool decodeOnGet = true, int width = Side, int height = Side)
    {
        var bytes = Png(width, height);
        return new ImageCache(_ => bytes, limits ?? s_three, decodeOnGet);
    }

    private static ImageRef Ref(string id) => new(id);

    [Fact]
    public void DefaultLimitsAreThePinnedOnes()
    {
        Assert.Equal(268_435_456L, ImageCacheLimits.Default.ByteBudget);
        Assert.Equal(32_000_000L, ImageCacheLimits.Default.MaxPixels);
        Assert.True(ImageCacheLimits.Default.MaxPixels * 4 * 2 <= ImageCacheLimits.Default.ByteBudget);
    }

    [Fact]
    public void ACapAboveHalfTheBudgetIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ImageCacheLimits(1000, 126));
        Assert.Throws<ArgumentException>(() => new ImageCacheLimits(0, 0));
        _ = new ImageCacheLimits(1000, 125);

        // A copy made with "with" and a default struct cannot get around the check: the cache refuses them.
        var bad = new ImageCacheLimits(1000, 100) with { MaxPixels = 500 };
        Assert.Throws<ArgumentException>(() => new ImageCache(_ => default, bad));
        Assert.Throws<ArgumentException>(() => new ImageCache(_ => default, default(ImageCacheLimits)));
    }

    [Fact]
    public void LeastRecentlyUsedIsEvictedFirst()
    {
        using var cache = Cache();
        cache.Get(Ref("a"));
        cache.Get(Ref("b"));
        cache.Get(Ref("c"));
        Assert.Equal(OneImage * 3, cache.DecodedBytes);

        // Touch "a": "b" is now the oldest and goes when "d" arrives.
        Assert.NotNull(cache.TryGet(Ref("a")));
        cache.Get(Ref("d"));

        Assert.Equal(OneImage * 3, cache.DecodedBytes);
        Assert.NotNull(cache.TryGet(Ref("a")));
        Assert.Null(cache.TryGet(Ref("b")));
        Assert.NotNull(cache.TryGet(Ref("c")));
        Assert.NotNull(cache.TryGet(Ref("d")));
    }

    [Fact]
    public void EvictionDoesNotDisposeUntilReleaseRetired()
    {
        using var cache = Cache();
        var held = cache.Get(Ref("a"));
        Assert.NotNull(held);
        cache.Get(Ref("b"));
        cache.Get(Ref("c"));
        cache.Get(Ref("d"));
        Assert.Null(cache.TryGet(Ref("a")));

        // Evicted, but a running draw may still hold it.
        Assert.NotEqual(IntPtr.Zero, held.Handle);
        Assert.Equal(Side, held.Width);

        cache.ReleaseRetired();
        Assert.Equal(IntPtr.Zero, held.Handle);
    }

    [Fact]
    public void AnImageAboveThePixelCapIsRefusedWithoutDecoding()
    {
        using var cache = Cache(width: Side + 1, height: Side);
        Assert.Null(cache.Get(Ref("big")));
        Assert.Null(cache.Get(Ref("big")));
        Assert.Equal(0, cache.DecodeCount);
        Assert.Equal(1, cache.RefusedCount);
        Assert.Equal(0, cache.DecodedBytes);
    }

    [Fact]
    public void TryGetNeverDecodes()
    {
        using var cache = Cache();
        Assert.Null(cache.TryGet(Ref("a")));
        Assert.Equal(0, cache.DecodeCount);
        cache.Get(Ref("a"));
        Assert.NotNull(cache.TryGet(Ref("a")));
        Assert.Equal(1, cache.DecodeCount);
    }

    [Fact]
    public void GetWithDecodeOnGetFalseNeverDecodes()
    {
        using var cache = Cache(decodeOnGet: false);
        Assert.Null(cache.Get(Ref("a")));
        Assert.Equal(0, cache.DecodeCount);
        cache.Warm([Ref("a")], TestContext.Current.CancellationToken);
        Assert.Equal(1, cache.DecodeCount);
        Assert.NotNull(cache.Get(Ref("a")));
        Assert.Equal(1, cache.DecodeCount);
    }

    [Fact]
    public void PrefetchRaisesDecodedOnce()
    {
        using var cache = Cache();
        using var raised = new SemaphoreSlim(0);
        var count = 0;
        cache.Decoded += () =>
        {
            Interlocked.Increment(ref count);
            raised.Release();
        };

        cache.Prefetch([Ref("a"), Ref("b")], TestContext.Current.CancellationToken);
        Assert.True(raised.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.False(raised.Wait(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));
        Assert.Equal(1, count);
        Assert.Equal(2, cache.DecodeCount);
        Assert.NotNull(cache.TryGet(Ref("a")));
        Assert.NotNull(cache.TryGet(Ref("b")));
    }

    [Fact]
    public void PrefetchOfADecodedImageRaisesNothing()
    {
        using var cache = Cache();
        cache.Get(Ref("a"));
        var count = 0;
        cache.Decoded += () => Interlocked.Increment(ref count);

        cache.Prefetch([Ref("a")], TestContext.Current.CancellationToken);

        Assert.False(SpinWait.SpinUntil(() => Volatile.Read(ref count) > 0, TimeSpan.FromMilliseconds(300)));
        Assert.Equal(0, count);
        Assert.Equal(1, cache.DecodeCount);
    }

    [Fact]
    public void ConcurrentGetAndPrefetchAreSafe()
    {
        var limits = new ImageCacheLimits(OneImage * 5, Side * Side);
        using var cache = Cache(limits);
        var ids = Enumerable.Range(0, 12).Select(i => Ref("i" + i)).ToArray();
        var failures = new List<Exception>();
        var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
        {
            try
            {
                var random = new Random(t);
                for (var n = 0; n < 300; n++)
                {
                    var id = ids[random.Next(ids.Length)];
                    switch (n % 4)
                    {
                        case 0:
                            cache.Get(id);
                            break;
                        case 1:
                            cache.TryGet(id);
                            break;
                        case 2:
                            cache.Prefetch([id, ids[random.Next(ids.Length)]]);
                            break;
                        default:
                            cache.ReleaseRetired();
                            break;
                    }

                    Assert.True(cache.DecodedBytes <= limits.ByteBudget);
                }
            }
            catch (Exception ex)
            {
                lock (failures)
                {
                    failures.Add(ex);
                }
            }
        })).ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Assert.Empty(failures);

        // Let the queued prefetches drain, then everything must still be inside the budget.
        Assert.True(SpinWait.SpinUntil(() =>
        {
            var before = cache.DecodeCount;
            Thread.Sleep(100);
            return before == cache.DecodeCount;
        }, TimeSpan.FromSeconds(20)));
        cache.ReleaseRetired();
        Assert.True(cache.DecodedBytes <= limits.ByteBudget);
    }

    [Fact]
    public void DisposeStopsPrefetchResults()
    {
        using var gate = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var bytes = Png(Side, Side);
        var cache = new ImageCache(
            _ =>
            {
                entered.Set();
                gate.Wait(TimeSpan.FromSeconds(10));
                return bytes;
            },
            s_three);
        var raised = 0;
        cache.Decoded += () => Interlocked.Increment(ref raised);

        cache.Prefetch([Ref("a")], TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        cache.Dispose();
        gate.Set();

        Assert.False(SpinWait.SpinUntil(() => raised > 0, TimeSpan.FromMilliseconds(500)));
        Assert.Equal(0, cache.DecodeCount);
        Assert.Equal(0, cache.DecodedBytes);
        Assert.Null(cache.Get(Ref("a")));
    }

    [Fact]
    public void IsKnownSeesDecodedAndFailedPicturesButNotEvictedOrClearedOnes()
    {
        using var cache = new ImageCache(id => id == "bad" ? throw new FileNotFoundException() : Png(Side, Side), s_three);
        Assert.False(cache.IsKnown(Ref("a")));

        cache.Warm([Ref("a"), Ref("bad")], TestContext.Current.CancellationToken);
        Assert.True(cache.IsKnown(Ref("a")));
        Assert.True(cache.IsKnown(Ref("bad")));

        // Evicting makes it unknown again (a prefetch asks for it anew); so does Clear.
        cache.Warm([Ref("b"), Ref("c"), Ref("d")], TestContext.Current.CancellationToken);
        Assert.False(cache.IsKnown(Ref("a")));
        cache.Clear();
        Assert.False(cache.IsKnown(Ref("d")));
        Assert.Equal(4, cache.RetiredCount);
        cache.ReleaseRetired();
        Assert.Equal(0, cache.RetiredCount);
    }

    [Fact]
    public async Task TwoOverlappingWarmCallsDecodeAPictureOnce()
    {
        var bytes = Png(Side, Side);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reads = 0;
        using var cache = new ImageCache(
            _ =>
            {
                Interlocked.Increment(ref reads);
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return bytes;
            },
            s_three);

        var first = Task.Run(() => cache.Warm([Ref("a")], TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // The second call finds the picture in flight and leaves it to the first: it returns at once and decodes nothing.
        cache.Warm([Ref("a")], TestContext.Current.CancellationToken);
        await cache.Prefetch([Ref("a")], TestContext.Current.CancellationToken);
        Assert.Equal(0, cache.DecodeCount);

        release.Set();
        await first;
        Assert.Equal(1, reads);
        Assert.Equal(1, cache.DecodeCount);
        Assert.Equal(OneImage, cache.DecodedBytes);
    }

    [Fact]
    public async Task GetStillDecodesForItselfWhileAWarmHasThePictureInFlight()
    {
        var bytes = Png(Side, Side);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        using var cache = new ImageCache(
            _ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }

                return bytes;
            },
            s_three);

        var warm = Task.Run(() => cache.Warm([Ref("a")], TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // A Get must return the picture, so it decodes it itself; the picture is kept once and counted once.
        Assert.NotNull(cache.Get(Ref("a")));
        release.Set();
        await warm;
        Assert.Equal(1, cache.DecodeCount);
        Assert.Equal(OneImage, cache.DecodedBytes);
    }

    [Fact]
    public async Task ACancelledTokenStopsWarmBetweenPicturesAndPrefetchWithoutAFault()
    {
        var bytes = Png(Side, Side);
        using var cancel = new CancellationTokenSource();
        var reads = 0;
        using var cache = new ImageCache(
            _ =>
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    cancel.Cancel();
                }

                return bytes;
            },
            s_three);

        Assert.Throws<OperationCanceledException>(() => cache.Warm([Ref("a"), Ref("b"), Ref("c")], cancel.Token));
        Assert.Equal(1, cache.DecodeCount);
        Assert.Equal(1, reads);

        // A prefetch ends quietly and reports what it decoded before it stopped.
        using var other = new CancellationTokenSource();
        var raised = 0;
        using var second = new ImageCache(
            _ =>
            {
                other.Cancel();
                return bytes;
            },
            s_three);
        second.Decoded += () => Interlocked.Increment(ref raised);
        await second.Prefetch([Ref("a"), Ref("b")], other.Token);
        Assert.Equal(1, second.DecodeCount);
        Assert.Equal(1, raised);
    }
}
