using Avalonia.Media.TextFormatting.Unicode;
using HarfBuzzSharp;
using Layout.Common;
using SkiaSharp;

namespace Layout.CSharp;

/// <summary>The C# candidate: HarfBuzzSharp shaping per run, UAX #14 line breaks from Avalonia's LineBreakEnumerator, greedy line filling, Skia drawing of glyph ids.</summary>
public sealed class HarfBuzzParagraphLayouter : IParagraphLayouter
{
    const int ScaleUnit = 1024; // HarfBuzz scale units per point

    readonly FontSet _fonts;

    public HarfBuzzParagraphLayouter(FontSet fonts) => _fonts = fonts;

    public IParagraphLayout Layout(Paragraph paragraph, float width) => new HbLayout(_fonts, paragraph, width);

    public void Dispose() { }

    sealed class HbLayout : IParagraphLayout, IGlyphSource
    {
        readonly ParagraphFormat _format;
        readonly float _width;
        readonly string _text;
        readonly LineMetrics[] _lines;
        readonly float[] _tops;
        readonly float[] _charX;          // x of every char boundary, continuous over the whole paragraph
        readonly int[] _lineEnd;          // end of the line without trailing spaces
        readonly int[] _lineGlyph;        // first glyph of every line, plus the glyph count
        readonly ushort[] _glyphIds;
        readonly float[] _glyphX;
        readonly int[] _glyphRun;
        readonly SKFont[] _runFonts;
        readonly float[] _glyphAdv;
        readonly IReadOnlyList<TextRun> _textRuns;

        public IReadOnlyList<LineMetrics> Lines => _lines;

        public HbLayout(FontSet fonts, Paragraph p, float width)
        {
            _format = p.Format;
            _width = width;
            _text = p.Text;
            int n = _text.Length;
            var runs = p.Runs;
            _textRuns = runs;
            _runFonts = new SKFont[runs.Count];
            var charAdv = new float[n + 1];
            var ids = new List<ushort>(n + 4);
            var gx = new List<float>(n + 4);
            var clusters = new List<int>(n + 4);
            var glyphRun = new List<int>(n + 4);
            var advs = new List<float>(n + 4);

            // Shape every run on its own; advances are collected per cluster start.
            float cursor = 0;
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                var fe = fonts.Get(run.Family, run.Bold, run.Italic);
                _runFonts[r] = new SKFont(fe.Typeface, run.Size) { Subpixel = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.SubpixelAntialias };
                if (run.Length == 0) continue;
                int scale = (int)Math.Round(run.Size * ScaleUnit);
                fe.HbFont.SetScale(scale, scale);
                using var buf = new HarfBuzzSharp.Buffer();
                buf.AddUtf16(_text, run.Start, run.Length);
                buf.Direction = Direction.LeftToRight;
                buf.GuessSegmentProperties();
                fe.HbFont.Shape(buf);
                var infos = buf.GetGlyphInfoSpan();
                var poss = buf.GetGlyphPositionSpan();
                for (int g = 0; g < infos.Length; g++)
                {
                    float adv = poss[g].XAdvance / (float)ScaleUnit;
                    ids.Add((ushort)infos[g].Codepoint);
                    gx.Add(cursor + poss[g].XOffset / (float)ScaleUnit);
                    clusters.Add((int)infos[g].Cluster);
                    glyphRun.Add(r);
                    advs.Add(adv);
                    charAdv[infos[g].Cluster] += adv;
                    cursor += adv;
                }
            }
            _glyphIds = ids.ToArray();
            _glyphX = gx.ToArray();
            _glyphRun = glyphRun.ToArray();
            _glyphAdv = advs.ToArray();
            _charX = new float[n + 1];
            for (int i = 0; i < n; i++) _charX[i + 1] = _charX[i] + charAdv[i];

            // Greedy line filling at UAX #14 break opportunities.
            var lines = new List<LineMetrics>();
            var lineEnd = new List<int>();
            void Emit(int start, int end)
            {
                int trimmed = end;
                while (trimmed > start && _text[trimmed - 1] == ' ') trimmed--;
                float asc = 0, desc = 0;
                foreach (var run in runs)
                {
                    if (run.Length == 0 || run.Start + run.Length <= start || run.Start >= end) continue;
                    var fe = fonts.Get(run.Family, run.Bold, run.Italic);
                    asc = Math.Max(asc, fe.Ascent * run.Size);
                    desc = Math.Max(desc, fe.Descent * run.Size);
                }
                if (asc == 0 && desc == 0)
                {
                    var fe = fonts.Get(_format.Family, false, false);
                    asc = fe.Ascent * _format.Size;
                    desc = fe.Descent * _format.Size;
                }
                lines.Add(new LineMetrics(start, end - start, asc, desc, _charX[trimmed] - _charX[start]));
                lineEnd.Add(trimmed);
            }

            if (n == 0) Emit(0, 0);
            else
            {
                var breaks = new List<(int Measure, int Wrap, bool Required)>();
                var e = new LineBreakEnumerator(_text.AsSpan());
                while (e.MoveNext(out var b)) breaks.Add((b.PositionMeasure, b.PositionWrap, b.Required));
                int lineStart = 0, lastWrap = -1;
                foreach (var (measure, wrap, required) in breaks)
                {
                    float w = _charX[measure] - _charX[lineStart];
                    while (w > _width && lastWrap > lineStart)
                    {
                        Emit(lineStart, lastWrap);
                        lineStart = lastWrap;
                        lastWrap = -1;
                        w = _charX[measure] - _charX[lineStart];
                    }
                    lastWrap = wrap;
                    if (required)
                    {
                        Emit(lineStart, wrap);
                        lineStart = wrap;
                        lastWrap = -1;
                    }
                }
                if (lineStart < n) Emit(lineStart, n);
            }
            _lines = lines.ToArray();
            _lineEnd = lineEnd.ToArray();
            _tops = LineGeometry.Tops(_lines, _format);

            // First glyph of every line (glyph clusters ascend in logical order).
            _lineGlyph = new int[_lines.Length + 1];
            int gi = 0;
            for (int l = 0; l < _lines.Length; l++)
            {
                while (gi < clusters.Count && clusters[gi] < _lines[l].Start) gi++;
                _lineGlyph[l] = gi;
            }
            _lineGlyph[_lines.Length] = clusters.Count;
        }

        public int HitTest(float x, float y)
        {
            int line = LineGeometry.LineAt(_tops, y);
            var m = _lines[line];
            float xr = x - LineGeometry.XOffset(m, _width, _format) + _charX[m.Start];
            int max = line == _lines.Length - 1 ? _lineEnd[line] : Math.Max(m.Start, m.Start + m.Length - 1);
            int best = m.Start;
            float bestD = Math.Abs(_charX[m.Start] - xr);
            for (int i = m.Start + 1; i <= max; i++)
            {
                float d = Math.Abs(_charX[i] - xr);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public RectF Caret(int offset)
        {
            offset = Math.Clamp(offset, 0, _text.Length);
            int line = _lines.Length - 1;
            for (int i = 0; i < _lines.Length; i++)
                if (offset < _lines[i].Start + _lines[i].Length) { line = i; break; }
            var m = _lines[line];
            float x = _charX[offset] - _charX[m.Start] + LineGeometry.XOffset(m, _width, _format);
            return new RectF(x, _tops[line], 1f, LineGeometry.Height(m, _format));
        }

        public void Draw(SKCanvas canvas, float x, float y, int firstLine, int lineCount)
        {
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            for (int l = firstLine; l < firstLine + lineCount; l++)
            {
                var m = _lines[l];
                float top = y + _tops[l] - _tops[firstLine];
                float baseline = top + LineGeometry.Baseline(m, _format);
                float x0 = x + LineGeometry.XOffset(m, _width, _format) - _charX[m.Start];
                int g = _lineGlyph[l], end = _lineGlyph[l + 1];
                while (g < end)
                {
                    int run = _glyphRun[g], e = g;
                    while (e < end && _glyphRun[e] == run) e++;
                    using var builder = new SKTextBlobBuilder();
                    var points = new SKPoint[e - g];
                    for (int k = g; k < e; k++) points[k - g] = new SKPoint(_glyphX[k], 0);
                    builder.AddPositionedRun(new ReadOnlySpan<ushort>(_glyphIds, g, e - g), _runFonts[run], points);
                    using var blob = builder.Build();
                    if (blob is not null) canvas.DrawText(blob, x0, baseline, paint);
                    g = e;
                }
            }
        }

        public IReadOnlyList<GlyphRun> LineRuns(int line)
        {
            var result = new List<GlyphRun>();
            int g = _lineGlyph[line], end = _lineGlyph[line + 1];
            float origin = _charX[_lines[line].Start];
            while (g < end)
            {
                int run = _glyphRun[g], e = g;
                while (e < end && _glyphRun[e] == run) e++;
                var tr = _textRuns[run];
                var ids = new ushort[e - g];
                var xs = new float[e - g];
                var adv = new float[e - g];
                for (int k = g; k < e; k++) { ids[k - g] = _glyphIds[k]; xs[k - g] = _glyphX[k] - origin; adv[k - g] = _glyphAdv[k]; }
                result.Add(new GlyphRun(tr.Family, tr.Bold, tr.Italic, tr.Size, ids, xs, adv));
                g = e;
            }
            return result;
        }

        public void Dispose()
        {
            foreach (var f in _runFonts) f.Dispose();
        }
    }
}
