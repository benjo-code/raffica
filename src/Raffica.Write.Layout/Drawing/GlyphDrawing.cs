using SkiaSharp;

namespace Raffica.Write.Layout;

/// <summary>
/// Draws glyph runs: one <see cref="SKFont"/> per face and size and one <see cref="SKTextBlob"/> per run. Everything here belongs
/// to the calling thread (the caches are thread-static), so two documents drawing at the same time never share a font, a builder or
/// a paint.
/// </summary>
internal static class GlyphDrawing
{
    private const int MaxFonts = 32;
    private const float MaxSize = 10000f;

    [ThreadStatic]
    private static Dictionary<(FontFace Face, int SizeBits), SKFont>? t_fonts;

    [ThreadStatic]
    private static SKTextBlobBuilder? t_builder;

    /// <summary>The font for a face at a size: subpixel positions, no hinting, so the text is the same at every zoom.</summary>
    private static SKFont FontFor(FontFace face, float size)
    {
        var fonts = t_fonts ??= new Dictionary<(FontFace, int), SKFont>();
        var key = (face, BitConverter.SingleToInt32Bits(size));
        if (fonts.TryGetValue(key, out var known))
        {
            return known;
        }

        if (fonts.Count >= MaxFonts)
        {
            foreach (var old in fonts.Values)
            {
                old.Dispose();
            }

            fonts.Clear();
        }

        var font = new SKFont(face.Typeface, size)
        {
            Subpixel = true,
            Hinting = SKFontHinting.None,
            Edging = SKFontEdging.SubpixelAntialias,
        };
        fonts[key] = font;
        return font;
    }

    public static void Draw(SKCanvas canvas, GlyphRunItem run, SKPaint paint)
    {
        var glyphs = run.Glyphs.Span;
        var xs = run.Xs.Span;
        var count = Math.Min(glyphs.Length, xs.Length);
        var size = run.Size;
        if (count == 0 || !(size > 0f) || size > MaxSize || !float.IsFinite(run.X) || !float.IsFinite(run.Baseline))
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            if (!float.IsFinite(xs[i]))
            {
                return;
            }
        }

        try
        {
            var font = FontFor(run.Face, size);
            var builder = t_builder ??= new SKTextBlobBuilder();
            var buffer = builder.AllocateHorizontalRun(font, count, run.Baseline);
            var glyphSpan = buffer.Glyphs;
            var positions = buffer.Positions;
            for (var i = 0; i < count; i++)
            {
                glyphSpan[i] = glyphs[i];
                positions[i] = run.X + xs[i];
            }

            using var blob = builder.Build();
            if (blob is not null)
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor(run.Color);
                canvas.DrawText(blob, 0f, 0f, paint);
            }
        }
        catch (ObjectDisposedException)
        {
            // A managed wrapper that was disposed under a late draw: the run is skipped. The builder may be half-filled, so drop it.
            t_builder = null;
        }
    }
}
