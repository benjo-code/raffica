using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Layout.Common;

/// <summary>Draws one laid-out page into a canvas whose units are points (the caller scales: 794 x 1123 pixels is A4 at 96 dpi).</summary>
public static class PageRenderer
{
    static readonly ConditionalWeakTable<byte[], SKImage> s_images = new();

    public static string FooterText(PageBreaker layout, int page) => layout.FooterText(page);

    public static void Draw(PageBreaker layout, int page, SKCanvas canvas)
    {
        var setup = layout.Setup;
        var info = layout.GetPage(page);
        canvas.Clear(SKColors.White);

        // Header and footer of the section.
        var section = layout.Doc.Sections[info.Section];
        if (section.HeaderText.Length > 0)
            DrawLine(layout, canvas, section.HeaderText, Alignment.Left, setup.MarginLeft, setup.HeaderDistance, setup.TextWidth, rightAligned: true);
        string footer = layout.FooterText(page);
        if (footer.Length > 0)
            DrawLine(layout, canvas, footer, Alignment.Center, setup.MarginLeft, setup.Height - setup.FooterDistance - 12f, setup.TextWidth, rightAligned: false);

        foreach (var it in info.Items)
        {
            switch (layout.Blocks[it.Block])
            {
                case Paragraph p:
                    layout.ParagraphLayoutOf(it.Block).Draw(canvas, setup.MarginLeft + p.Format.LeftIndent, it.Y, it.First, it.Count);
                    break;
                case Table t:
                    DrawTable(layout, canvas, it, t);
                    break;
                case ImageBlock im:
                    DrawImage(layout, canvas, it, im);
                    break;
            }
        }

        // Footnotes at the bottom of the text area.
        if (info.Footnotes.Count > 0)
        {
            float bottom = setup.Height - setup.MarginBottom;
            float y = bottom - info.FootnoteHeight;
            using var rule = new SKPaint { Color = SKColors.Black, StrokeWidth = 0.5f, IsStroke = true };
            canvas.DrawLine(setup.MarginLeft, y - PageBreaker.FootnoteSeparator + 6f, setup.MarginLeft + 144f, y - PageBreaker.FootnoteSeparator + 6f, rule);
            foreach (int id in info.Footnotes)
            {
                var (fl, h) = layout.FootnoteOf(id);
                fl.Draw(canvas, setup.MarginLeft, y, 0, fl.Lines.Count);
                y += h;
            }
        }
    }

    static void DrawLine(PageBreaker layout, SKCanvas canvas, string text, Alignment alignment, float x, float y, float width, bool rightAligned)
    {
        var format = new ParagraphFormat(0, 0, 1, 0, alignment, false, false, false, "Carlito", 9f);
        var para = new Paragraph(text, new[] { new TextRun(0, text.Length, "Carlito", 9f, false, false) }, format, Array.Empty<FootnoteRef>());
        using var l = layout.Layouter.Layout(para, width);
        float dx = rightAligned ? Math.Max(0, width - l.Lines[0].Width) : 0;
        l.Draw(canvas, x + dx, y, 0, l.Lines.Count);
    }

    static void DrawTable(PageBreaker layout, SKCanvas canvas, Placed it, Table t)
    {
        var setup = layout.Setup;
        var (cells, rowTops, colX, colW) = layout.TableOf(it.Block);
        using var border = new SKPaint { Color = SKColors.Black, StrokeWidth = 0.5f, IsStroke = true };
        using var shade = new SKPaint { Color = new SKColor(0xD9, 0xD9, 0xD9) };
        float y = it.Y;
        void Row(int r)
        {
            float h = rowTops[r + 1] - rowTops[r];
            for (int c = 0; c < cells[r].Length; c++)
            {
                float x = setup.MarginLeft + colX[c];
                if (r < t.HeaderRows) canvas.DrawRect(x, y, colW[c], h, shade);
                canvas.DrawRect(x, y, colW[c], h, border);
                cells[r][c].Draw(canvas, x + 5.4f, y + 1f, 0, cells[r][c].Lines.Count);
            }
            y += h;
        }
        if (it.RepeatHeader) for (int r = 0; r < t.HeaderRows; r++) Row(r);
        for (int r = it.First; r < it.First + it.Count; r++) Row(r);
    }

    static void DrawImage(PageBreaker layout, SKCanvas canvas, Placed it, ImageBlock im)
    {
        var setup = layout.Setup;
        var bytes = layout.Doc.Images[im.ImageIndex];
        if (!s_images.TryGetValue(bytes, out var img))
        {
            img = SKImage.FromEncodedData(bytes);
            s_images.Add(bytes, img);
        }
        float x = setup.MarginLeft + (setup.TextWidth - im.Width) / 2;
        canvas.DrawImage(img, new SKRect(x, it.Y, x + im.Width, it.Y + im.Height));
    }
}
