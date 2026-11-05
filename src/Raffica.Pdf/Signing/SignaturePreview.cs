using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Raffica.Core.Annotations;
using Raffica.Core.Signatures;

namespace Raffica.Pdf.Signing;

/// <summary>
/// What the saved signature will look like: the strokes of a drawing or the cutout of an image, fitted into the control on a checkerboard so
/// that transparent paper shows. The checkerboard uses two shell surface tokens, so it follows the theme. Used on the UI thread only.
/// </summary>
internal sealed class SignaturePreview : Control
{
    private const double Tile = 8;
    private const double Padding = 6;
    private IReadOnlyList<IReadOnlyList<DisplayPoint>>? _strokes;
    private DrawColor _color = DrawColor.Black;
    private SignatureImage? _image;
    private WriteableBitmap? _bitmap;

    public SignaturePreview()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    /// <summary>Gets the cutout shown, or <see langword="null"/> when the preview shows strokes or nothing.</summary>
    public SignatureImage? Image => _image;

    /// <summary>Gets how many bitmaps were built (for tests: the same image does not build another).</summary>
    internal int BitmapBuilds { get; private set; }

    /// <summary>Shows a cutout (<see langword="null"/> shows nothing); the strokes are dropped.</summary>
    public void ShowImage(SignatureImage? image)
    {
        if (image is not null && ReferenceEquals(image, _image))
        {
            return; // the same cutout: its bitmap stays
        }

        _strokes = null;
        _image = image;
        _bitmap?.Dispose();
        _bitmap = image is null ? null : ToBitmap(image);
        BitmapBuilds += image is null ? 0 : 1;
        InvalidateVisual();
    }

    /// <summary>Shows strokes in any coordinates (<see langword="null"/> or none shows nothing); the image is dropped.</summary>
    public void ShowStrokes(IReadOnlyList<IReadOnlyList<DisplayPoint>>? strokes, DrawColor color)
    {
        _image = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _strokes = strokes;
        _color = color;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var light = Find("Raffica.Brush.Surface.Page");
        var dark = Find("Raffica.Brush.Surface.Band");
        context.DrawRectangle(light, null, bounds);
        for (var row = 0; row * Tile < bounds.Height; row++)
        {
            for (var column = row % 2; column * Tile < bounds.Width; column += 2)
            {
                context.DrawRectangle(dark, null, new Rect(column * Tile, row * Tile, Tile, Tile).Intersect(bounds));
            }
        }

        context.DrawRectangle(null, new Pen(Find("Raffica.Brush.Border.Control"), 1), bounds.Deflate(0.5));
        var inner = bounds.Deflate(Padding);
        if (_bitmap is not null && inner.Width > 0 && inner.Height > 0)
        {
            var scale = Math.Min(inner.Width / _bitmap.PixelSize.Width, inner.Height / _bitmap.PixelSize.Height);
            var size = new Size(_bitmap.PixelSize.Width * scale, _bitmap.PixelSize.Height * scale);
            var target = new Rect(inner.X + ((inner.Width - size.Width) / 2), inner.Y + ((inner.Height - size.Height) / 2), size.Width, size.Height);
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), target);
        }
        else if (_strokes is { Count: > 0 })
        {
            DrawStrokes(context, inner);
        }
    }

    private static WriteableBitmap ToBitmap(SignatureImage image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var frame = bitmap.Lock();
        var rowBytes = image.Width * 4;
        for (var y = 0; y < image.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(image.Bgra, y * rowBytes, frame.Address + (y * frame.RowBytes), rowBytes);
        }

        return bitmap;
    }

    private void DrawStrokes(DrawingContext context, Rect inner)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in _strokes!.SelectMany(s => s))
        {
            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
        }

        var width = Math.Max(maxX - minX, 1e-6);
        var height = Math.Max(maxY - minY, 1e-6);
        var scale = Math.Min(inner.Width / width, inner.Height / height);
        var offsetX = inner.X + ((inner.Width - (width * scale)) / 2) - (minX * scale);
        var offsetY = inner.Y + ((inner.Height - (height * scale)) / 2) - (minY * scale);
        var rgb = DrawPalette.ColorOf(_color);
        var pen = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B)), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        foreach (var stroke in _strokes!.Where(s => s.Count >= 2))
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(offsetX + (stroke[0].X * scale), offsetY + (stroke[0].Y * scale)), false);
                for (var i = 1; i < stroke.Count; i++)
                {
                    g.LineTo(new Point(offsetX + (stroke[i].X * scale), offsetY + (stroke[i].Y * scale)));
                }

                g.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }

    private IBrush Find(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Transparent;
}
