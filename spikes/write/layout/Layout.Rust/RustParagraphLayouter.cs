using System.Runtime.InteropServices;
using Layout.Common;
using SkiaSharp;

namespace Layout.Rust;

/// <summary>The Rust candidate: Parley (HarfRust shaping, ICU4X line breaking) behind a C interface, called from C# with LibraryImport.</summary>
public sealed unsafe class RustParagraphLayouter : IParagraphLayouter
{
    readonly FontSet _fonts;
    readonly nint _ctx;
    readonly Dictionary<uint, FontEntry> _byId = new();

    public RustParagraphLayouter(FontSet fonts)
    {
        string profile = Marshal.PtrToStringAnsi(ParleyNative.pc_build_profile()) ?? "unknown";
        if (profile != "release")
            throw new InvalidOperationException($"the Parley library is a '{profile}' build; build it with cargo build --release before measuring");
        _fonts = fonts;
        _ctx = ParleyNative.pc_ctx_create();
        foreach (var fe in fonts.All)
        {
            uint id;
            int status;
            fixed (byte* p = fe.Data) status = ParleyNative.pc_ctx_add_font(_ctx, p, (nuint)fe.Data.Length, out id);
            if (status != 0) throw new InvalidDataException("Parley rejected font " + fe.Path);
            _byId[id] = fe;
        }
    }

    public IParagraphLayout Layout(Paragraph paragraph, float width) => new RustLayout(this, paragraph, width);

    public void Dispose() => ParleyNative.pc_ctx_free(_ctx);

    sealed class RustLayout : IParagraphLayout
    {
        readonly RustParagraphLayouter _owner;
        readonly nint _para;
        readonly ParagraphFormat _format;
        readonly float _width;
        readonly PcLine[] _pc;
        readonly LineMetrics[] _lines;
        readonly float[] _tops;
        readonly int _length;
        readonly Dictionary<(uint, float), SKFont> _skFonts = new();

        public IReadOnlyList<LineMetrics> Lines => _lines;

        public RustLayout(RustParagraphLayouter owner, Paragraph p, float width)
        {
            _owner = owner; _format = p.Format; _width = width; _length = p.Text.Length;
            var runs = new PcRun[p.Runs.Count];
            for (int i = 0; i < runs.Length; i++)
            {
                var r = p.Runs[i];
                runs[i] = new PcRun { Start = (uint)r.Start, Len = (uint)r.Length, Family = r.Family == "Carlito" ? 0u : 1u, Bold = r.Bold ? 1u : 0u, Italic = r.Italic ? 1u : 0u, Size = r.Size };
            }
            fixed (char* t = p.Text)
            fixed (PcRun* rp = runs)
                _para = ParleyNative.pc_para_layout(owner._ctx, t, (nuint)p.Text.Length, rp, (nuint)runs.Length, width);
            if (_para == 0) throw new InvalidOperationException("pc_para_layout failed");

            int cap = 64;
            while (true)
            {
                _pc = new PcLine[cap];
                nuint n;
                fixed (PcLine* lp = _pc) n = ParleyNative.pc_para_lines(_para, lp, (nuint)cap);
                if ((int)n <= cap) { Array.Resize(ref _pc, (int)n); break; }
                cap = (int)n;
            }
            _lines = new LineMetrics[_pc.Length];
            for (int i = 0; i < _pc.Length; i++)
            {
                var l = _pc[i];
                float asc = l.Ascent, desc = l.Descent;
                if (_length == 0)
                {
                    var fe = owner._fonts.Get(p.Format.Family, false, false);
                    asc = fe.Ascent * p.Format.Size; desc = fe.Descent * p.Format.Size;
                }
                _lines[i] = new LineMetrics((int)l.Start, (int)l.Len, asc, desc, l.Width);
            }
            _tops = LineGeometry.Tops(_lines, _format);
        }

        public int HitTest(float x, float y)
        {
            int line = LineGeometry.LineAt(_tops, y);
            var m = _lines[line];
            float xr = x - LineGeometry.XOffset(m, _width, _format);
            ParleyNative.pc_para_hit_test(_para, xr, _pc[line].YMid, out uint offset);
            int max = line == _lines.Length - 1 ? m.Start + m.Length : Math.Max(m.Start, m.Start + m.Length - 1);
            return Math.Clamp((int)offset, m.Start, max);
        }

        public RectF Caret(int offset)
        {
            offset = Math.Clamp(offset, 0, _length);
            int line = _lines.Length - 1;
            for (int i = 0; i < _lines.Length; i++)
                if (offset < _lines[i].Start + _lines[i].Length) { line = i; break; }
            var m = _lines[line];
            ParleyNative.pc_para_caret(_para, (uint)offset, out var r);
            return new RectF(r.X + LineGeometry.XOffset(m, _width, _format), _tops[line], 1f, LineGeometry.Height(m, _format));
        }

        public void Draw(SKCanvas canvas, float x, float y, int firstLine, int lineCount)
        {
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            for (int l = firstLine; l < firstLine + lineCount; l++)
            {
                var m = _lines[l];
                float top = y + _tops[l] - _tops[firstLine];
                float baseline = top + LineGeometry.Baseline(m, _format);
                float x0 = x + LineGeometry.XOffset(m, _width, _format);
                int cap = Math.Max(16, m.Length * 2 + 8);
                PcGlyph[] glyphs;
                int n;
                while (true)
                {
                    glyphs = new PcGlyph[cap];
                    fixed (PcGlyph* gp = glyphs) n = (int)ParleyNative.pc_para_glyphs(_para, (uint)l, 1, gp, (nuint)cap);
                    if (n <= cap) break;
                    cap = n;
                }
                int g = 0;
                while (g < n)
                {
                    uint font = glyphs[g].FontId;
                    float size = glyphs[g].Size;
                    int e = g;
                    while (e < n && glyphs[e].FontId == font && glyphs[e].Size == size) e++;
                    if (!_skFonts.TryGetValue((font, size), out var sk))
                        _skFonts[(font, size)] = sk = new SKFont(_owner._byId[font].Typeface, size) { Subpixel = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.SubpixelAntialias };
                    var ids = new ushort[e - g];
                    var pts = new SKPoint[e - g];
                    for (int k = g; k < e; k++) { ids[k - g] = (ushort)glyphs[k].GlyphId; pts[k - g] = new SKPoint(glyphs[k].X, glyphs[k].Y); }
                    using var builder = new SKTextBlobBuilder();
                    builder.AddPositionedRun(ids, sk, pts);
                    using var blob = builder.Build();
                    if (blob is not null) canvas.DrawText(blob, x0, baseline, paint);
                    g = e;
                }
            }
        }

        public void Dispose()
        {
            foreach (var f in _skFonts.Values) f.Dispose();
            ParleyNative.pc_para_free(_para);
        }
    }
}
