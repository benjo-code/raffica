using System.Globalization;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using SkiaSharp;

namespace WriteLayoutBench;

/// <summary><c>render &lt;page&gt; &lt;out.png&gt;</c>: lays out through that page and writes it at 96 dpi for a look.</summary>
internal static class Render
{
    public static int Run(Options options, TextWriter output, TextWriter error)
    {
        if (options.Positional.Count != 2 || !int.TryParse(options.Positional[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
        {
            return Program.Fail(error, "render needs a page number (1 and up) and an output path");
        }

        var file = options.FileOrThesis();
        if (!File.Exists(file))
        {
            return Program.Fail(error, "the file does not exist");
        }

        var bytes = File.ReadAllBytes(file);
        using var docx = DocxFile.Open(bytes);
        var images = new PackageImages(bytes);
        var layout = DocumentLayout.Create(docx.Model, FontCatalog.CreateShipped(), new LayoutOptions(LayoutMode.Pages, ImageBytes: images.Read));
        layout.LayOutThrough(number - 1);
        if (layout.LaidOutPages < number)
        {
            return Program.Fail(error, $"the document has only {layout.LaidOutPages} pages");
        }

        using var cache = new ImageCache(images.Read);
        using var bitmap = FirstPage.DrawPage(layout.Page(number - 1), cache);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(options.Positional[1]);
        data.SaveTo(stream);
        output.WriteLine($"page {number} written");
        return 0;
    }
}
