using System.Diagnostics;
using System.Globalization;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using SkiaSharp;

namespace WriteLayoutBench;

/// <summary>The milliseconds of each step of the first-page path, measured from the start of the process.</summary>
internal sealed class PhaseTimes
{
    public static readonly string[] Names = ["start-to-main", "fonts", "read-file", "parse", "layout-page-1", "draw-page-1", "total"];

    public double StartToMainMs { get; set; }

    public double FontsMs { get; set; }

    public double ReadFileMs { get; set; }

    public double ParseMs { get; set; }

    public double LayoutMs { get; set; }

    public double DrawMs { get; set; }

    public double TotalMs { get; set; }

    public double[] Values => [StartToMainMs, FontsMs, ReadFileMs, ParseMs, LayoutMs, DrawMs, TotalMs];

    public string Serialize() =>
        "phases " + string.Join(' ', Names.Zip(Values, (n, v) => string.Create(CultureInfo.InvariantCulture, $"{n}={v:0.0}")));

    public static PhaseTimes? Parse(string line)
    {
        if (!line.StartsWith("phases ", StringComparison.Ordinal))
        {
            return null;
        }

        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in line["phases ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=');
            if (parts.Length == 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                map[parts[0]] = v;
            }
        }

        return Names.All(map.ContainsKey)
            ? new PhaseTimes
            {
                StartToMainMs = map[Names[0]],
                FontsMs = map[Names[1]],
                ReadFileMs = map[Names[2]],
                ParseMs = map[Names[3]],
                LayoutMs = map[Names[4]],
                DrawMs = map[Names[5]],
                TotalMs = map[Names[6]],
            }
            : null;
    }
}

/// <summary>What the first-page path made: the bitmap of page 1 and the time from the start of the process.</summary>
internal sealed class FirstPageResult(SKBitmap bitmap, double milliseconds) : IDisposable
{
    public SKBitmap Bitmap { get; } = bitmap;

    public double Milliseconds { get; } = milliseconds;

    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// The path the app takes to its first page: fonts, open the file, lay out page 1 only, draw it at 96 dpi. The child command and the
/// tests call this one method. The document is opened as a prefix (<see cref="FirstPageOpener"/> holds the fallback for a page 1 that
/// reaches the end of the prefix).
/// </summary>
internal static class FirstPage
{
    public const float Scale = 96f / 72f;

    public static double SinceProcessStart(DateTime now)
    {
        using var process = Process.GetCurrentProcess();
        return Math.Max(0, (now - process.StartTime).TotalMilliseconds);
    }

    public static FirstPageResult Draw(string file, PhaseTimes times)
    {
        var step = Stopwatch.StartNew();
        var fonts = FontCatalog.CreateShipped();
        var warm = FirstPageOpener.WarmUp(fonts);
        times.FontsMs = Lap(step);

        var bytes = File.ReadAllBytes(file);
        times.ReadFileMs = Lap(step);

        var images = new PackageImages(bytes);
        var options = new LayoutOptions(LayoutMode.Pages, ImageBytes: images.Read);
        using var docx = DocxFile.OpenPrefix(bytes, FirstPageOpener.DefaultPrefixBlocks);
        times.ParseMs = Lap(step);
        warm.GetAwaiter().GetResult();

        var layout = DocumentLayout.Create(docx.Model, fonts, options);
        layout.LayOutFirstPage();
        if (docx.IsPrefix && !layout.FirstPageIsFinal)
        {
            // Rare: page 1 reaches the end of the prefix. The shared helper starts again with a bigger prefix.
            using var opened = FirstPageOpener.Open(bytes, fonts, options, FirstPageOpener.DefaultPrefixBlocks * FirstPageOpener.Growth);
            layout = opened.Layout;
        }

        times.LayoutMs = Lap(step);

        using var cache = new ImageCache(images.Read);
        var bitmap = DrawPage(layout.Page(0), cache);
        times.DrawMs = Lap(step);

        times.TotalMs = times.StartToMainMs + times.FontsMs + times.ReadFileMs + times.ParseMs + times.LayoutMs + times.DrawMs;
        using var process = Process.GetCurrentProcess();
        return new FirstPageResult(bitmap, Math.Max(times.TotalMs, (DateTime.Now - process.StartTime).TotalMilliseconds));
    }

    internal static SKBitmap DrawPage(LaidOutPage page, ImageCache images)
    {
        var bitmap = new SKBitmap((int)Math.Ceiling(page.Width * Scale), (int)Math.Ceiling(page.Height * Scale));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        PageRenderer.Draw(canvas, page, Scale, images);
        return bitmap;
    }

    private static double Lap(Stopwatch step)
    {
        var ms = step.Elapsed.TotalMilliseconds;
        step.Restart();
        return ms;
    }
}
