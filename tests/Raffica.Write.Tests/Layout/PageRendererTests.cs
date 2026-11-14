using Raffica.Write.Layout;
using SkiaSharp;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

public sealed class PageRendererTests
{
    private static readonly ImageCache NoImages = new(_ => ReadOnlyMemory<byte>.Empty);

    private static LaidOutPage FirstPage(string body) => Pages(Complete(body + Sect()))[0];

    private static LaidOutPage Hand(float width, float height, params PageItem[] items) =>
        new(0, "1", width, height, false, items, string.Empty, []);

    private static SKBitmap Render(LaidOutPage page, float scale, ImageCache? images = null)
    {
        var bitmap = new SKBitmap((int)Math.Ceiling(page.Width * scale), (int)Math.Ceiling(page.Height * scale));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        PageRenderer.Draw(canvas, page, scale, images ?? NoImages);
        return bitmap;
    }

    private static bool Dark(SKColor c) => c.Red < 128 && c.Green < 128 && c.Blue < 128;

    private static (int Left, int Top, int Right, int Bottom)? Bounds(SKBitmap bitmap, Func<SKColor, bool> predicate)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (predicate(bitmap.GetPixel(x, y)))
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < 0 ? null : (left, top, right, bottom);
    }

    private static byte[] Png(SKColor color, int width = 4, int height = 4)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static bool Same(SKBitmap a, SKBitmap b) => a.GetPixelSpan().SequenceEqual(b.GetPixelSpan());

    [Fact]
    public void TextIsDrawnWhereTheLayoutSaysItIs()
    {
        var page = FirstPage(P("Hello renderer"));
        var run = Runs(page)[0];
        using var bitmap = Render(page, 2f);
        var bounds = Bounds(bitmap, Dark);
        Assert.NotNull(bounds);

        // All ink lies in the first line's band and none above the top margin (72 pt).
        Assert.True(bounds.Value.Top >= (run.Baseline - run.Size) * 2f - 2f);
        Assert.True(bounds.Value.Bottom <= (run.Baseline + (run.Size * 0.4f)) * 2f);
        Assert.True(bounds.Value.Top >= 72 * 2);
        Assert.True(bounds.Value.Left >= (run.X * 2f) - 2f);
    }

    [Fact]
    public void ScaleScalesTheOutput()
    {
        var page = FirstPage(P("Hello renderer"));
        using var one = Render(page, 1f);
        using var three = Render(page, 3f);
        var a = Bounds(one, Dark)!.Value;
        var b = Bounds(three, Dark)!.Value;
        Assert.Equal(a.Right * 3f, b.Right, 6f);
        Assert.Equal(a.Bottom * 3f, b.Bottom, 6f);
        Assert.Equal(three.Width, one.Width * 3);
    }

    [Fact]
    public void UnderlineStrikeAndHighlight()
    {
        var page = FirstPage("<w:p><w:r><w:rPr><w:u w:val=\"single\"/><w:highlight w:val=\"yellow\"/></w:rPr><w:t>Marked up</w:t></w:r></w:p>");
        Assert.Contains(page.Items, i => i is RuleItem);
        Assert.Contains(page.Items, i => i is FillItem);
        using var bitmap = Render(page, 2f);
        Assert.NotNull(Bounds(bitmap, c => c.Red > 200 && c.Green > 200 && c.Blue < 80));
        var rule = page.Items.OfType<RuleItem>().First();
        var row = (int)((rule.Y + (rule.Height / 2f)) * 2f);
        var column = (int)((rule.X + (rule.Width / 2f)) * 2f);
        Assert.True(Dark(bitmap.GetPixel(column, row)) || Dark(bitmap.GetPixel(column, row + 1)) || Dark(bitmap.GetPixel(column, row - 1)));

        var struck = FirstPage("<w:p><w:r><w:rPr><w:strike/></w:rPr><w:t>Struck</w:t></w:r></w:p>");
        var strike = struck.Items.OfType<RuleItem>().Single();
        using var struckBitmap = Render(struck, 4f);
        var sx = (int)((strike.X + (strike.Width / 2f)) * 4f);
        var sy = (int)((strike.Y + (strike.Height / 2f)) * 4f);
        Assert.True(Dark(struckBitmap.GetPixel(sx, sy)));
    }

    [Fact]
    public void FillAndRuleItems()
    {
        var page = Hand(100f, 100f, new FillItem(10f, 10f, 20f, 20f, 0xFF00FF00), new RuleItem(50f, 50f, 30f, 2f, 0xFFFF0000));
        using var bitmap = Render(page, 1f);
        Assert.Equal(new SKColor(0, 255, 0), bitmap.GetPixel(15, 15));
        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(60, 51));
        Assert.Equal(SKColors.White, bitmap.GetPixel(60, 55));
    }

    [Fact]
    public void ImagesAreDrawnFromTheCache()
    {
        var reads = 0;
        using var cache = new ImageCache(id =>
        {
            reads++;
            return Png(new SKColor(0, 0, 255));
        });
        var page = Hand(100f, 100f, new ImageItem(new ImageRef("document#a"), 20f, 30f, 40f, 20f));
        using var bitmap = Render(page, 2f, cache);
        Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(60, 80));
        Assert.Equal(SKColors.White, bitmap.GetPixel(30, 80));
        Assert.Equal(SKColors.White, bitmap.GetPixel(60, 110));
        using var again = Render(page, 2f, cache);
        Assert.Equal(1, reads);
        Assert.Equal(1, cache.DecodeCount);
    }

    [Fact]
    public void ACorruptImageDrawsThePlaceholderAndDoesNotThrow()
    {
        using var cache = new ImageCache(_ => new byte[] { 1, 2, 3, 4 });
        var page = Hand(100f, 100f, new ImageItem(new ImageRef("document#bad"), 20f, 20f, 40f, 40f));
        using var bitmap = Render(page, 1f, cache);
        Assert.NotNull(Bounds(bitmap, c => c.Red < 250));
    }

    [Fact]
    public void PlaceholderIsVisible()
    {
        var page = Hand(100f, 100f, new PlaceholderItem(20f, 20f, 40f, 40f));
        using var bitmap = Render(page, 2f);
        var bounds = Bounds(bitmap, c => c.Red < 250)!.Value;
        Assert.InRange(bounds.Left, 38, 41);
        Assert.InRange(bounds.Right, 118, 122);

        // The diagonal cross reaches the middle of the box.
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(80, 80));
    }

    [Fact]
    public void DrawingTwiceIsIdentical()
    {
        var page = FirstPage(P("Same pixels every time, with some more words to fill a line."));
        using var one = Render(page, 1.5f);
        using var two = Render(page, 1.5f);
        Assert.True(Same(one, two));
    }

    [Fact]
    public void DrawingAnEmptyPageIsFast()
    {
        var page = Hand(612f, 792f);
        using var bitmap = new SKBitmap(612, 792);
        using var canvas = new SKCanvas(bitmap);
        PageRenderer.Draw(canvas, page, 1f, NoImages);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            PageRenderer.Draw(canvas, page, 1f, NoImages);
        }

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 16 * 1024);
    }

    [Fact]
    public void ATextPageDrawsWithLittleManagedAllocation()
    {
        var page = FirstPage(P(Repeat("lorem ipsum dolor sit amet ", 40)));
        using var bitmap = new SKBitmap(612, 792);
        using var canvas = new SKCanvas(bitmap);
        PageRenderer.Draw(canvas, page, 1f, NoImages);
        var before = GC.GetAllocatedBytesForCurrentThread();
        PageRenderer.Draw(canvas, page, 1f, NoImages);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 64 * 1024);
    }

    [Fact]
    public void AnItemWithANonNumberPositionIsSkipped()
    {
        var page = Hand(100f, 100f, new FillItem(float.NaN, 10f, 20f, 20f, 0xFF000000), new RuleItem(10f, float.PositiveInfinity, 20f, 2f, 0xFF000000), new FillItem(10f, 10f, float.NaN, 5f, 0xFF000000));
        using var bitmap = Render(page, 1f);
        Assert.Null(Bounds(bitmap, c => c != SKColors.White));
    }

    [Fact]
    public void TheCallersMatrixIsRestoredAndTheClipIsHonoured()
    {
        var page = Hand(50f, 50f, new FillItem(40f, 40f, 100f, 100f, 0xFF000000), new FillItem(60f, 10f, 20f, 20f, 0xFF000000));
        using var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Translate(10f, 10f);
        var before = canvas.TotalMatrix;
        var saves = canvas.SaveCount;
        PageRenderer.Draw(canvas, page, 2f, NoImages);
        Assert.Equal(before, canvas.TotalMatrix);
        Assert.Equal(saves, canvas.SaveCount);

        // The page is 50 pt = 100 px wide at (10, 10); the fill stops at the page edge, the item outside draws nothing.
        Assert.Equal(SKColors.Black, bitmap.GetPixel(100, 100));
        Assert.Equal(SKColors.White, bitmap.GetPixel(115, 100));
        Assert.Equal(SKColors.White, bitmap.GetPixel(100, 115));
        Assert.Equal(SKColors.White, bitmap.GetPixel(140, 40));
    }

    [Fact]
    public void NullArgumentsAreProgrammingErrors()
    {
        var page = Hand(10f, 10f);
        using var bitmap = new SKBitmap(10, 10);
        using var canvas = new SKCanvas(bitmap);
        Assert.Throws<ArgumentNullException>(() => PageRenderer.Draw(null!, page, 1f, NoImages));
        Assert.Throws<ArgumentNullException>(() => PageRenderer.Draw(canvas, null!, 1f, NoImages));
        Assert.Throws<ArgumentNullException>(() => PageRenderer.Draw(canvas, page, 1f, null!));
    }
}
