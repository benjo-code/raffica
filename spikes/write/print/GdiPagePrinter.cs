using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Layout.Common;
using PrintSpike.Interop;
using SkiaSharp;

namespace PrintSpike;

/// <summary>
/// Route a: Write draws the laid-out pages straight onto the printer's device context with GDI, as Word does. Glyph runs go out with
/// ExtTextOutW and ETO_GLYPH_INDEX (the glyph ids come from HarfBuzz), table borders with GDI pens, images with StretchDIBits.
/// The fonts are added to the process with AddFontMemResourceEx from the spike's font files.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class GdiPagePrinter : IDisposable
{
    readonly PageBreaker _pb;
    readonly nint _dc;
    readonly double _sx, _sy;
    readonly int _offX, _offY;
    readonly Dictionary<(string, bool, bool, float), nint> _fonts = new();
    nint _oldFont;
    static bool s_fontsAdded;

    public GdiPagePrinter(PageBreaker pb, nint printerDc, FontSet fonts)
    {
        _pb = pb;
        _dc = printerDc;
        _sx = Gdi.GetDeviceCaps(_dc, Gdi.LogicalPixelsX) / 72.0;
        _sy = Gdi.GetDeviceCaps(_dc, Gdi.LogicalPixelsY) / 72.0;
        _offX = Gdi.GetDeviceCaps(_dc, GdiDrawing.PhysicalOffsetX);
        _offY = Gdi.GetDeviceCaps(_dc, GdiDrawing.PhysicalOffsetY);
        AddFonts(fonts);
    }

    static void AddFonts(FontSet fonts)
    {
        if (s_fontsAdded) return;
        foreach (var fe in fonts.All)
            fixed (byte* p = fe.Data)
            {
                uint n = 0;
                if (GdiDrawing.AddFontMemResourceEx(p, (uint)fe.Data.Length, 0, &n) == 0)
                    throw new InvalidOperationException("AddFontMemResourceEx failed for " + fe.Path);
            }
        s_fontsAdded = true;
    }

    public sealed record Result(int Pages, double Milliseconds);

    /// <summary>Prints pages <paramref name="from"/> to <paramref name="to"/> (1-based). With <paramref name="outputFile"/> the job goes to that file instead of the printer.</summary>
    public Result Print(string documentName, string? outputFile, int from, int to)
    {
        var sw = Stopwatch.StartNew();
        fixed (char* name = documentName)
        fixed (char* output = outputFile)
        {
            var info = new DocInfo { Size = sizeof(DocInfo), DocName = name, Output = outputFile is null ? null : output };
            if (Gdi.StartDoc(_dc, &info) <= 0) throw new InvalidOperationException("StartDoc failed, error " + Marshal.GetLastWin32Error());
        }
        bool finished = false;
        try
        {
            GdiDrawing.SetBkMode(_dc, GdiDrawing.Transparent);
            GdiDrawing.SetTextAlign(_dc, GdiDrawing.TaBaseline);
            for (int page = from - 1; page < to; page++)
            {
                _pb.LayoutUntil(page);
                if (Gdi.StartPage(_dc) <= 0) throw new InvalidOperationException("StartPage failed, error " + Marshal.GetLastWin32Error());
                DrawPage(page);
                if (Gdi.EndPage(_dc) <= 0) throw new InvalidOperationException("EndPage failed, error " + Marshal.GetLastWin32Error());
            }
            if (Gdi.EndDoc(_dc) <= 0) throw new InvalidOperationException("EndDoc failed, error " + Marshal.GetLastWin32Error());
            finished = true;
        }
        finally
        {
            if (!finished) Gdi.AbortDoc(_dc);
        }
        return new Result(to - from + 1, sw.Elapsed.TotalMilliseconds);
    }

    int X(float pt) => (int)Math.Round(pt * _sx) - _offX;
    int Y(float pt) => (int)Math.Round(pt * _sy) - _offY;

    void DrawPage(int page)
    {
        var setup = _pb.Setup;
        var info = _pb.GetPage(page);
        var section = _pb.Doc.Sections[info.Section];

        if (section.HeaderText.Length > 0) DrawText(section.HeaderText, Alignment.Left, setup.MarginLeft, setup.HeaderDistance, setup.TextWidth, right: true);
        string footer = _pb.FooterText(page);
        if (footer.Length > 0) DrawText(footer, Alignment.Center, setup.MarginLeft, setup.Height - setup.FooterDistance - 12f, setup.TextWidth, right: false);

        foreach (var it in info.Items)
        {
            switch (_pb.Blocks[it.Block])
            {
                case Paragraph p:
                    DrawLayout(_pb.ParagraphLayoutOf(it.Block), p.Format, setup.TextWidth - p.Format.LeftIndent, setup.MarginLeft + p.Format.LeftIndent, it.Y, it.First, it.Count);
                    break;
                case Table t:
                    DrawTable(it, t);
                    break;
                case ImageBlock im:
                    DrawImage(it, im);
                    break;
            }
        }

        if (info.Footnotes.Count > 0)
        {
            float y = setup.Height - setup.MarginBottom - info.FootnoteHeight;
            var pen = GdiDrawing.CreatePen(0, Math.Max(1, (int)Math.Round(0.5 * _sx)), 0);
            var old = GdiDrawing.SelectObject(_dc, pen);
            GdiDrawing.MoveToEx(_dc, X(setup.MarginLeft), Y(y - PageBreaker.FootnoteSeparator + 6f), 0);
            GdiDrawing.LineTo(_dc, X(setup.MarginLeft + 144f), Y(y - PageBreaker.FootnoteSeparator + 6f));
            GdiDrawing.SelectObject(_dc, old);
            GdiDrawing.DeleteObject(pen);
            foreach (int id in info.Footnotes)
            {
                var (fl, h) = _pb.FootnoteOf(id);
                DrawLayout(fl, _pb.Doc.Footnotes.First(f => f.Id == id).Content.Format, setup.TextWidth, setup.MarginLeft, y, 0, fl.Lines.Count);
                y += h;
            }
        }
    }

    void DrawText(string text, Alignment alignment, float x, float y, float width, bool right)
    {
        var format = new ParagraphFormat(0, 0, 1, 0, alignment, false, false, false, "Carlito", 9f);
        var para = new Paragraph(text, new[] { new TextRun(0, text.Length, "Carlito", 9f, false, false) }, format, Array.Empty<FootnoteRef>());
        using var l = _pb.Layouter.Layout(para, width);
        float dx = right ? Math.Max(0, width - l.Lines[0].Width) : 0;
        DrawLayout(l, format, width, x + dx, y, 0, l.Lines.Count);
    }

    void DrawLayout(IParagraphLayout layout, ParagraphFormat format, float width, float x, float y, int first, int count)
    {
        if (layout is not IGlyphSource source) throw new NotSupportedException("route a needs the glyphs of the C# candidate");
        var tops = LineGeometry.Tops(layout.Lines, format);
        for (int l = first; l < first + count; l++)
        {
            var m = layout.Lines[l];
            float baseline = y + tops[l] - tops[first] + LineGeometry.Baseline(m, format);
            float x0 = x + LineGeometry.XOffset(m, width, format);
            foreach (var run in source.LineRuns(l)) DrawRun(run, x0, baseline);
        }
    }

    void DrawRun(GlyphRun run, float x0, float baseline)
    {
        if (run.Glyphs.Length == 0) return;
        var font = GetFont(run);
        var previous = GdiDrawing.SelectObject(_dc, font);
        if (_oldFont == 0) _oldFont = previous;
        var dx = new int[run.Glyphs.Length];
        int start = X(x0 + run.X[0]);
        for (int i = 0; i < dx.Length; i++)
        {
            int a = X(x0 + run.X[i]);
            int b = X(x0 + run.X[i] + run.Advances[i]);
            dx[i] = b - a;
        }
        fixed (ushort* g = run.Glyphs)
        fixed (int* d = dx)
            GdiDrawing.ExtTextOutW(_dc, start, Y(baseline), GdiDrawing.EtoGlyphIndex, null, (char*)g, (uint)run.Glyphs.Length, d);
    }

    nint GetFont(GlyphRun run)
    {
        var key = (run.Family, run.Bold, run.Italic, run.Size);
        if (_fonts.TryGetValue(key, out var h)) return h;
        var lf = new LogFont
        {
            Height = -(int)Math.Round(run.Size * _sy), Weight = run.Bold ? 700 : 400, Italic = (byte)(run.Italic ? 1 : 0),
            CharSetId = 1 /* DEFAULT_CHARSET */, OutPrecision = 3 /* OUT_TT_PRECIS */, Quality = 4 /* ANTIALIASED */,
        };
        for (int i = 0; i < run.Family.Length; i++) lf.FaceName[i] = run.Family[i];
        h = GdiDrawing.CreateFontIndirectW(&lf);
        if (h == 0) throw new InvalidOperationException("CreateFontIndirect failed for " + run.Family);
        _fonts[key] = h;
        return h;
    }

    void DrawTable(Placed it, Table t)
    {
        var setup = _pb.Setup;
        var (cells, rowTops, colX, colW) = _pb.TableOf(it.Block);
        var pen = GdiDrawing.CreatePen(0, Math.Max(1, (int)Math.Round(0.5 * _sx)), 0);
        var shade = GdiDrawing.CreateSolidBrush(0xD9D9D9);
        var oldPen = GdiDrawing.SelectObject(_dc, pen);
        var oldBrush = GdiDrawing.SelectObject(_dc, GdiDrawing.GetStockObject(GdiDrawing.NullBrush));
        float y = it.Y;
        void Row(int r)
        {
            float h = rowTops[r + 1] - rowTops[r];
            for (int c = 0; c < cells[r].Length; c++)
            {
                float x = setup.MarginLeft + colX[c];
                if (r < t.HeaderRows)
                {
                    var rect = new Rect { Left = X(x), Top = Y(y), Right = X(x + colW[c]), Bottom = Y(y + h) };
                    GdiDrawing.FillRect(_dc, &rect, shade);
                }
                GdiDrawing.Rectangle(_dc, X(x), Y(y), X(x + colW[c]), Y(y + h));
                DrawLayout(cells[r][c], t.Rows[r][c].Format, Math.Max(1f, colW[c] - 10.8f), x + 5.4f, y + 1f, 0, cells[r][c].Lines.Count);
            }
            y += h;
        }
        if (it.RepeatHeader) for (int r = 0; r < t.HeaderRows; r++) Row(r);
        for (int r = it.First; r < it.First + it.Count; r++) Row(r);
        GdiDrawing.SelectObject(_dc, oldPen);
        GdiDrawing.SelectObject(_dc, oldBrush);
        GdiDrawing.DeleteObject(pen);
        GdiDrawing.DeleteObject(shade);
    }

    void DrawImage(Placed it, ImageBlock im)
    {
        var setup = _pb.Setup;
        using var decoded = SKBitmap.Decode(_pb.Doc.Images[im.ImageIndex]);
        using var bmp = decoded.Copy(SKColorType.Bgra8888);
        var header = new BitmapInfoHeader { Size = (uint)sizeof(BitmapInfoHeader), Width = bmp.Width, Height = -bmp.Height, Planes = 1, BitCount = 32 };
        float x = setup.MarginLeft + (setup.TextWidth - im.Width) / 2;
        GdiDrawing.StretchDIBits(_dc, X(x), Y(it.Y), X(x + im.Width) - X(x), Y(it.Y + im.Height) - Y(it.Y), 0, 0, bmp.Width, bmp.Height,
            (byte*)bmp.GetPixels(), &header, 0 /* DIB_RGB_COLORS */, 0x00CC0020 /* SRCCOPY */);
    }

    public void Dispose()
    {
        if (_oldFont != 0) GdiDrawing.SelectObject(_dc, _oldFont);
        foreach (var f in _fonts.Values) GdiDrawing.DeleteObject(f);
        _fonts.Clear();
    }
}
