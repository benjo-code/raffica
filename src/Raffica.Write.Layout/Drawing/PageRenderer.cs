using SkiaSharp;

namespace Raffica.Write.Layout;

/// <summary>
/// Draws one <see cref="LaidOutPage"/> onto a Skia canvas. It draws the page content only: the paper and its shadow belong to the
/// app. The items are drawn in <see cref="LaidOutPage.Items"/> order, which is the painting order the layout made (behind-text
/// pictures, fills, text, rules, in-front pictures); decorations (underline, strike-through, highlight, borders) are ordinary
/// <see cref="RuleItem"/>s and <see cref="FillItem"/>s, so nothing is re-sorted or recomputed here. The page is never changed.
/// </summary>
/// <remarks>
/// Threading: <see cref="Draw"/> may run on any thread, one call at a time per canvas. The font, builder and paint caches are
/// thread-static (bounded, 32 fonts per thread), so two documents drawing concurrently on two threads share nothing. Pictures come
/// from the <see cref="ImageCache"/>: an <see cref="SKImage"/> it hands out is used only inside the call and never stored; the
/// caller must not <see cref="ImageCache.Clear"/> or dispose the cache while a draw of that cache runs. Content never makes this
/// throw: a position that is not a number, an absurd size, a face that is gone or a picture that cannot be decoded skips the item
/// (a picture draws the marked box). The placeholder box is a fixed neutral grey (this library has no theme).
/// </remarks>
public static class PageRenderer
{
    private const uint PlaceholderLine = 0xFF8A8A8A;
    private const uint PlaceholderFill = 0x1A808080;
    private const float MaxCoordinate = 1_000_000f;

    [ThreadStatic]
    private static SKPaint? t_paint;

    /// <param name="canvas">The canvas to draw on; its matrix is restored.</param>
    /// <param name="page">The page to draw.</param>
    /// <param name="scale">Device pixels per point (for example 96/72 times the zoom).</param>
    /// <param name="images">Where pictures come from.</param>
    public static void Draw(SKCanvas canvas, LaidOutPage page, float scale, ImageCache images)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(images);
        if (!(scale > 0f) || !float.IsFinite(scale) || !float.IsFinite(page.Width) || !float.IsFinite(page.Height))
        {
            return;
        }

        var paint = t_paint ??= new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        var restore = canvas.Save();
        try
        {
            canvas.Scale(scale);
            canvas.ClipRect(new SKRect(0f, 0f, page.Width, page.Height));
            var items = page.ItemArray;
            for (var i = 0; i < items.Length; i++)
            {
                switch (items[i])
                {
                    case FillItem fill:
                        Rectangle(canvas, paint, fill.X, fill.Y, fill.Width, fill.Height, fill.Color);
                        break;
                    case GlyphRunItem run:
                        GlyphDrawing.Draw(canvas, run, paint);
                        break;
                    case RuleItem rule:
                        Hairline(canvas, paint, rule, scale);
                        break;
                    case ImageItem picture:
                        Picture(canvas, paint, picture, images);
                        break;
                    case PlaceholderItem box:
                        Placeholder(canvas, paint, box.X, box.Y, box.Width, box.Height);
                        break;
                }
            }
        }
        finally
        {
            canvas.RestoreToCount(restore);
        }
    }

    private static bool Sane(float x, float y, float width, float height) =>
        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height)
        && Math.Abs(x) < MaxCoordinate && Math.Abs(y) < MaxCoordinate && width < MaxCoordinate && height < MaxCoordinate
        && width > 0f && height > 0f;

    private static void Rectangle(SKCanvas canvas, SKPaint paint, float x, float y, float width, float height, uint color)
    {
        if (!Sane(x, y, width, height))
        {
            return;
        }

        paint.Style = SKPaintStyle.Fill;
        paint.Color = new SKColor(color);
        canvas.DrawRect(x, y, width, height, paint);
    }

    // A rule thinner than one device pixel is widened to one pixel about its centre, and the centre is put on the middle of a pixel
    // row (or column), so a 0.5 pt border is a crisp line at every zoom instead of a faint smear over two rows.
    private static void Hairline(SKCanvas canvas, SKPaint paint, RuleItem rule, float scale)
    {
        var x = rule.X;
        var y = rule.Y;
        var width = rule.Width;
        var height = rule.Height;
        if (Sane(x, y, width, height))
        {
            if (width >= height && height * scale < 1f)
            {
                y = Snap(y + (height / 2f), scale) - (0.5f / scale);
                height = 1f / scale;
            }
            else if (width < height && width * scale < 1f)
            {
                x = Snap(x + (width / 2f), scale) - (0.5f / scale);
                width = 1f / scale;
            }
        }

        Rectangle(canvas, paint, x, y, width, height, rule.Color);
    }

    // The centre moved to the middle of the device pixel it lies in (in points).
    private static float Snap(float center, float scale) => (MathF.Floor(center * scale) + 0.5f) / scale;

    private static void Picture(SKCanvas canvas, SKPaint paint, ImageItem item, ImageCache images)
    {
        if (!Sane(item.X, item.Y, item.Width, item.Height))
        {
            return;
        }

        var image = images.Get(item.Image);
        if (image is null)
        {
            Placeholder(canvas, paint, item.X, item.Y, item.Width, item.Height);
            return;
        }

        var target = new SKRect(item.X, item.Y, item.X + item.Width, item.Y + item.Height);
        var deviceScale = canvas.TotalMatrix.ScaleX;
        var down = image.Width > item.Width * deviceScale || image.Height > item.Height * deviceScale;
        var sampling = down ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear) : new SKSamplingOptions(SKFilterMode.Linear);
        paint.Style = SKPaintStyle.Fill;
        paint.Color = SKColors.White;
        canvas.DrawImage(image, new SKRect(0f, 0f, image.Width, image.Height), target, sampling, paint);
    }

    private static void Placeholder(SKCanvas canvas, SKPaint paint, float x, float y, float width, float height)
    {
        if (!Sane(x, y, width, height))
        {
            return;
        }

        var rect = new SKRect(x, y, x + width, y + height);
        paint.Style = SKPaintStyle.Fill;
        paint.Color = new SKColor(PlaceholderFill);
        canvas.DrawRect(rect, paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 0.75f;
        paint.Color = new SKColor(PlaceholderLine);
        canvas.DrawRect(rect, paint);
        canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Bottom, paint);
        canvas.DrawLine(rect.Left, rect.Bottom, rect.Right, rect.Top, paint);
        paint.Style = SKPaintStyle.Fill;
    }
}
