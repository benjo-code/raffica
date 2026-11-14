using System.Text;

namespace Raffica.Write.Layout;

/// <summary>
/// Lays out one paragraph: shapes each run once per line break chunk, breaks with the UAX #14 opportunities, fills lines greedily
/// (trailing spaces hang), applies indents, tabs, alignment and justification, and works out the line heights. It never throws on
/// content: a word wider than the line overflows it, a character no face has shows as .notdef. One layouter may serve many
/// calls, but not at the same time.
/// </summary>
public sealed class ParagraphLayouter
{
    private readonly FontCatalog _fonts;

    public ParagraphLayouter(FontCatalog fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        _fonts = fonts;
    }

    /// <summary>Finds hyphenation points; used for a paragraph whose <see cref="ParagraphInput.AutoHyphenation"/> is on (and whose style does not suppress it).</summary>
    public IHyphenator? Hyphenator { get; set; }

    /// <summary>Lays out <paramref name="input"/> in a text area <paramref name="width"/> points wide.</summary>
    /// <param name="input">The flattened paragraph.</param>
    /// <param name="width">The width of the text area in points; indents are taken from it.</param>
    /// <param name="wrap">Narrows single lines for floating objects, or null for none.</param>
    public ParagraphLayout Layout(ParagraphInput input, float width, IWrapSource? wrap = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var session = new ParagraphSession(_fonts, input, float.IsFinite(width) ? width : 0f, wrap, Hyphenator);
        return session.Run();
    }
}

internal sealed class ParagraphSession : IDisposable
{
    private const float Epsilon = 0.01f;
    private const int MaxLeaderGlyphs = 4000;
    private const int MaxWrapSkips = 200;
    private const float ScriptScale = 0.65f;
    private const float SuperRaise = 0.35f;
    private const float SubLower = 0.14f;
    private const float SmallCapsScale = 0.8f;

    private readonly ParagraphInput _input;
    private readonly ResolvedParagraph _para;
    private readonly IWrapSource? _wrap;
    private readonly IHyphenator? _hyphenator;
    private readonly List<int> _points = [];
    private readonly ParagraphShaper _shaper;
    private readonly List<LineBox> _lines = [];
    private readonly List<Piece> _chunk = [];
    private readonly List<Piece> _scratch = [];
    private readonly List<GlyphRun> _runs = [];
    private readonly List<PlacedBox> _boxes = [];
    private readonly float _leftBase;
    private readonly float _firstLeft;
    private readonly float _rightLimit;

    private string _text = string.Empty;
    private int[] _itemStart = [];

    private float _y;
    private bool _first = true;
    private bool _lineOpen;
    private float _lineTop;
    private float _lineLeft;
    private float _lineRight;
    private float _x;
    private float _trailing;
    private bool _hasContent;
    private bool _hadTab;
    private bool _hasMetrics;
    private float _asc;
    private float _desc;
    private float _gap;
    private int _lineStartOffset;
    private int _chunkEnd;
    private int _placedEnd;
    private GlyphRun? _lastText;
    private Pending? _pending;

    public ParagraphSession(FontCatalog fonts, ParagraphInput input, float width, IWrapSource? wrap, IHyphenator? hyphenator)
    {
        _hyphenator = input.AutoHyphenation && !input.Paragraph.SuppressAutoHyphens ? hyphenator : null;
        _input = input;
        _para = input.Paragraph;
        _wrap = wrap;
        _shaper = new ParagraphShaper(fonts);
        _leftBase = _para.LeftIndent;
        _firstLeft = _para.LeftIndent + _para.FirstLineIndent - _para.HangingIndent;
        _rightLimit = width - _para.RightIndent;
    }

    private sealed class Pending
    {
        public float Stop;
        public TabAlignment Align;
        public TabLeader Leader;
        public float TabStart;
        public int FirstRun;
        public int FirstBox;
        public Piece? Tab;
        public bool Found;
        public float Before;
    }

    public void Dispose() => _shaper.Dispose();

    public ParagraphLayout Run()
    {
        BuildText();
        var breaks = LineBreaker.Find(_text);
        var previous = 0;
        BreakKind? endBreak = null;
        var remainder = _input.Items.Count;
        var trailingLineBreak = false;
        for (var k = 0; k < breaks.Count; k++)
        {
            var end = Math.Min(breaks[k].Offset, _text.Length);
            if (end < previous)
            {
                continue;
            }

            BuildPieces(previous, end);
            _chunkEnd = end;
            PlaceChunk(previous);
            if (breaks[k].Mandatory && end > previous)
            {
                var item = ItemAt(end - 1);
                var kind = item < _input.Items.Count && _input.Items[item] is BreakItem b ? b.Kind : BreakKind.Line;
                EnsureLine(previous);
                FinishLine(end, true, kind != BreakKind.Line);
                if (kind != BreakKind.Line)
                {
                    endBreak = kind;
                    remainder = item + 1;
                    previous = end;
                    break;
                }

                trailingLineBreak = end == _text.Length;
            }

            previous = end;
        }

        if (endBreak is null)
        {
            if (_lineOpen)
            {
                FinishLine(_text.Length, false, true);
            }
            else if (_lines.Count == 0 || trailingLineBreak)
            {
                EnsureLine(_text.Length);
                FinishLine(_text.Length, false, true);
            }
        }

        return new ParagraphLayout(_text, [.. _lines], _y, endBreak, remainder);
    }

    // ---- text ----

    private void BuildText()
    {
        var items = _input.Items;
        _itemStart = new int[items.Count + 1];
        var length = 0;
        for (var i = 0; i < items.Count; i++)
        {
            _itemStart[i] = length;
            length += items[i] switch
            {
                TextSpan span => span.Run.Hidden ? 0 : span.Text.Length,
                _ => 1,
            };
        }

        _itemStart[items.Count] = length;
        var sb = new StringBuilder(length);
        foreach (var item in items)
        {
            switch (item)
            {
                case TextSpan span:
                    if (!span.Run.Hidden)
                    {
                        sb.Append(span.Text);
                    }

                    break;
                case InlineBox:
                    sb.Append('￼');
                    break;
                case BreakItem b:
                    sb.Append(b.Kind == BreakKind.Line ? '\n' : '\f');
                    break;
                default:
                    sb.Append('￼');
                    break;
            }
        }

        _text = sb.ToString();
    }

    // The item that holds a text offset: the last one that starts at or before it, which skips empty items.
    private int ItemAt(int offset)
    {
        var lo = 0;
        var hi = _input.Items.Count - 1;
        var found = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (_itemStart[mid] <= offset)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }

    private void BuildPieces(int from, int to)
    {
        _chunk.Clear();
        if (to <= from || _input.Items.Count == 0)
        {
            return;
        }

        var a = from;
        var i = ItemAt(a);
        while (a < to && i < _input.Items.Count)
        {
            var itemEnd = _itemStart[i + 1];
            var e = Math.Min(to, itemEnd);
            if (e > a)
            {
                switch (_input.Items[i])
                {
                    case TextSpan span when !span.Run.Hidden:
                        AddSpanPieces(span, a, e, e == to, _itemStart[i]);
                        break;
                    case InlineBox box:
                        _chunk.Add(MakeBox(box, a));
                        break;
                    default:
                        break;
                }
            }

            a = Math.Max(a, e);
            i++;
        }
    }

    private void AddSpanPieces(TextSpan span, int a, int e, bool lastOfChunk, int itemStart)
    {
        var text = span.Text.AsSpan(a - itemStart, e - a);
        var pos = 0;
        while (pos < text.Length)
        {
            var rest = text[pos..];
            var k = rest.IndexOfAny("\t\n\r\f");
            var segment = k < 0 ? rest : rest[..k];
            if (segment.Length > 0)
            {
                var endsChunk = lastOfChunk && (k < 0 || rest[k..].IndexOfAnyExcept("\n\r\f") < 0);
                AddSegment(span.Run, segment, a + pos, endsChunk);
            }

            if (k < 0)
            {
                break;
            }

            if (rest[k] == '\t')
            {
                _chunk.Add(MakeTab(span.Run, a + pos + k));
            }

            pos += k + 1;
        }
    }

    private static (float Size, float Raise) SizeAndRaise(ResolvedRun run)
    {
        var size = float.IsFinite(run.SizePoints) && run.SizePoints > 0f ? Math.Min(run.SizePoints, 2000f) : 1f;
        return run.VerticalAlignment switch
        {
            RunVerticalAlignment.Superscript => (size * ScriptScale, size * SuperRaise),
            RunVerticalAlignment.Subscript => (size * ScriptScale, -size * SubLower),
            _ => (size, 0f),
        };
    }

    private void AddSegment(ResolvedRun run, ReadOnlySpan<char> segment, int offset, bool endsChunk)
    {
        var (size, raise) = SizeAndRaise(run);
        if (run.Caps)
        {
            _shaper.AddText(_chunk, run, size, raise, segment.ToString().ToUpperInvariant(), offset, endsChunk);
            return;
        }

        if (!run.SmallCaps)
        {
            _shaper.AddText(_chunk, run, size, raise, segment, offset, endsChunk);
            return;
        }

        var pos = 0;
        while (pos < segment.Length)
        {
            var lower = IsLower(segment[pos]);
            var end = pos + 1;
            while (end < segment.Length && IsLower(segment[end]) == lower)
            {
                end++;
            }

            var part = segment[pos..end];
            var last = endsChunk && end == segment.Length;
            if (lower)
            {
                _shaper.AddText(_chunk, run, size * SmallCapsScale, raise, part.ToString().ToUpperInvariant(), offset + pos, last);
            }
            else
            {
                _shaper.AddText(_chunk, run, size, raise, part, offset + pos, last);
            }

            pos = end;
        }
    }

    private static bool IsLower(char c) => char.IsLower(c) && char.ToUpperInvariant(c) != c;

    private Piece MakeTab(ResolvedRun run, int offset)
    {
        var face = _shaper.FaceFor(run);
        var (size, raise) = SizeAndRaise(run);
        return new Piece
        {
            Kind = PieceKind.Tab,
            Face = face,
            Run = run,
            Size = size,
            Raise = raise,
            TextStart = offset,
            TextEnd = offset + 1,
            Ascent = Math.Max(0f, (face.Ascent * size) + raise),
            Descent = Math.Max(0f, (face.Descent * size) - raise),
            Gap = face.LineGap * size,
        };
    }

    private static Piece MakeBox(InlineBox box, int offset)
    {
        var width = float.IsFinite(box.Width) ? Math.Max(0f, box.Width) : 0f;
        var height = float.IsFinite(box.Height) ? Math.Max(0f, box.Height) : 0f;
        return new Piece
        {
            Kind = PieceKind.Box,
            Box = box,
            Width = width,
            Ascent = height,
            TextStart = offset,
            TextEnd = offset + 1,
        };
    }

    // ---- placing ----

    private void PlaceChunk(int chunkStart)
    {
        var n = _chunk.Count;
        var i = 0;
        while (i < n)
        {
            var j = i;
            while (j < n && _chunk[j].Kind != PieceKind.Tab)
            {
                j++;
            }

            var end = j < n ? j + 1 : n;
            var fit = 0f;
            for (var k = i; k < end; k++)
            {
                if (_chunk[k].Kind != PieceKind.Tab)
                {
                    fit += _chunk[k].Width;
                }
            }

            var last = _chunk[end - 1];
            if (last.Kind == PieceKind.Text)
            {
                fit -= last.TrailingWidth;
            }

            var start = i == 0 ? chunkStart : _chunk[i].TextStart;
            EnsureLine(start);
            var overflows = Extent(fit) > _lineRight + Epsilon;
            if (overflows && _hyphenator is not null && n == end && i == 0 && TryHyphenate(chunkStart))
            {
                return;
            }

            if (_hasContent && overflows)
            {
                FinishLine(_placedEnd, false, false);
                EnsureLine(start);
            }

            for (var k = i; k < end; k++)
            {
                Place(_chunk[k]);
            }

            i = end;
        }
    }

    // The chunk (a word with the spaces that hang after it, no tab, text pieces only) does not fit: put the longest hyphenated start
    // that fits, with a hyphen, on this line and lay out the rest as a chunk of its own. False when no start fits or the chunk is no word.
    private bool TryHyphenate(int chunkStart)
    {
        var language = _chunk[0].Run?.Language;
        foreach (var piece in _chunk)
        {
            if (piece.Kind != PieceKind.Text || piece.Run is null || !string.Equals(piece.Run.Language, language, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var text = _text.AsSpan(chunkStart, _chunkEnd - chunkStart);
        var lead = 0;
        while (lead < text.Length && !char.IsLetter(text[lead]))
        {
            lead++;
        }

        var stop = text.Length;
        while (stop > lead && !char.IsLetter(text[stop - 1]))
        {
            stop--;
        }

        if (stop - lead < 5)
        {
            return false;
        }

        _points.Clear();
        _hyphenator!.Find(text[lead..stop], language, _points);
        for (var k = _points.Count - 1; k >= 0; k--)
        {
            var cut = chunkStart + lead + _points[k];
            BuildPieces(chunkStart, cut);
            if (_chunk.Count == 0 || _chunk[^1].Kind != PieceKind.Text || _chunk[^1].Run is not { } run)
            {
                continue;
            }

            var tail = _chunk[^1];
            var hyphen = _shaper.Hyphen(run, tail.Size, tail.Raise, cut);
            var fit = hyphen.Width;
            foreach (var piece in _chunk)
            {
                fit += piece.Width;
            }

            if (Extent(fit) > _lineRight + Epsilon)
            {
                continue;
            }

            foreach (var piece in _chunk)
            {
                Place(piece);
            }

            Place(hyphen);
            FinishLine(cut, false, false);
            BuildPieces(cut, _chunkEnd);
            PlaceChunk(cut);
            return true;
        }

        BuildPieces(chunkStart, _chunkEnd);
        return false;
    }

    private void Place(Piece piece)
    {
        switch (piece.Kind)
        {
            case PieceKind.Text:
                PlaceText(piece, false);
                break;
            case PieceKind.Box:
                PlaceBox(piece);
                break;
            default:
                ApplyTab(piece, false);
                break;
        }

        _placedEnd = Math.Max(_placedEnd, piece.TextEnd);
    }

    private float Extent(float fit)
    {
        var natural = _x + fit;
        if (_pending is not { } pending)
        {
            return natural;
        }

        var w = natural - pending.TabStart;
        return pending.Align switch
        {
            TabAlignment.Right => Math.Max(natural, pending.Stop),
            TabAlignment.Center => Math.Max(natural, pending.Stop + (w / 2f)),
            _ => Math.Max(natural, pending.Stop - (pending.Found ? pending.Before : w) + w),
        };
    }

    private void Metrics(float ascent, float descent, float gap)
    {
        _asc = Math.Max(_asc, ascent);
        _desc = Math.Max(_desc, descent);
        _gap = Math.Max(_gap, gap);
        _hasMetrics = true;
    }

    private void PlaceText(Piece piece, bool label)
    {
        if (piece.Glyphs.Length == 0)
        {
            return;
        }

        var run = new GlyphRun(
            piece.Face!, piece.Run!, piece.Size, piece.Raise, piece.Glyphs, piece.Xs, _x, piece.Width,
            label ? _lineStartOffset : piece.TextStart, label ? _lineStartOffset : piece.TextEnd)
        {
            IsLabel = label,
            IsHyphen = piece.IsHyphen,
            Spaces = piece.Spaces,
            TrailStart = piece.TrailStart,
        };
        _runs.Add(run);
        _lastText = label ? null : run;
        if (!label)
        {
            _hasContent = true;
            TrackDecimal(piece);
        }

        _x += piece.Width;
        _trailing = label ? 0f : piece.TrailingWidth;
        Metrics(piece.Ascent, piece.Descent, piece.Gap);
    }

    private void PlaceBox(Piece piece)
    {
        _boxes.Add(new PlacedBox(piece.Box!, _x, 0f));
        _x += piece.Width;
        _trailing = 0f;
        _lastText = null;
        _hasContent = true;
        if (_pending is { Found: false } pending)
        {
            pending.Before += piece.Width;
        }

        Metrics(piece.Ascent, 0f, 0f);
    }

    private void TrackDecimal(Piece piece)
    {
        if (_pending is not { Align: TabAlignment.DecimalPoint, Found: false } pending)
        {
            return;
        }

        var text = _text.AsSpan(piece.TextStart, piece.TextEnd - piece.TextStart);
        var at = text.IndexOfAny('.', ',');
        if (at < 0)
        {
            pending.Before += piece.Width;
            return;
        }

        pending.Before += at == 0 ? 0f : _shaper.Measure(piece.Face!, text[..at], piece.Size);
        pending.Found = true;
    }

    // ---- tabs ----

    private void ApplyTab(Piece tab, bool forLabel)
    {
        CloseSegment(false);
        var hanging = _para.HangingIndent > 0f ? _para.LeftIndent : float.NaN;
        var stop = TabStops.Next(_x, _para.TabStops, hanging, _input.DefaultTabStop);
        var position = stop.Position;
        if (position > _lineRight + Epsilon)
        {
            position = Math.Max(_x, _lineRight);
        }

        _lastText = null;
        _trailing = 0f;
        Metrics(tab.Ascent, tab.Descent, tab.Gap);
        if (!forLabel)
        {
            _hasContent = true;
            _hadTab = true;
        }

        if (stop.Alignment is TabAlignment.Left or TabAlignment.Bar)
        {
            EmitLeader(tab, stop.Leader, _x, position);
            _x = Math.Max(_x, position);
            return;
        }

        _pending = new Pending
        {
            Stop = position,
            Align = stop.Alignment,
            Leader = stop.Leader,
            TabStart = _x,
            FirstRun = _runs.Count,
            FirstBox = _boxes.Count,
            Tab = tab,
        };
    }

    private void CloseSegment(bool atEnd)
    {
        if (_pending is not { } pending)
        {
            return;
        }

        _pending = null;
        var w = _x - pending.TabStart - (atEnd ? _trailing : 0f);
        var start = pending.Align switch
        {
            TabAlignment.Right => pending.Stop - w,
            TabAlignment.Center => pending.Stop - (w / 2f),
            _ => pending.Stop - (pending.Found ? pending.Before : w),
        };
        start = Math.Max(start, pending.TabStart);
        var delta = start - pending.TabStart;
        if (delta > 0f)
        {
            for (var i = pending.FirstRun; i < _runs.Count; i++)
            {
                _runs[i].X += delta;
            }

            for (var i = pending.FirstBox; i < _boxes.Count; i++)
            {
                _boxes[i].X += delta;
            }

            _x += delta;
        }

        EmitLeader(pending.Tab!, pending.Leader, pending.TabStart, start);
    }

    private void EmitLeader(Piece tab, TabLeader leader, float from, float to)
    {
        if (leader == TabLeader.None || to - from < 0.5f)
        {
            return;
        }

        var c = leader switch
        {
            TabLeader.Hyphen => '-',
            TabLeader.Underscore or TabLeader.Heavy => '_',
            TabLeader.MiddleDot => '·',
            _ => '.',
        };
        _scratch.Clear();
        _shaper.AddText(_scratch, tab.Run!, tab.Size, 0f, [c], tab.TextStart, false);
        if (_scratch.Count == 0 || _scratch[0].Glyphs.Length == 0 || !(_scratch[0].Width > 0f))
        {
            return;
        }

        var glyph = _scratch[0];
        var count = Math.Min(MaxLeaderGlyphs, (int)((to - from) / glyph.Width));
        if (count <= 0)
        {
            return;
        }

        var glyphs = new ushort[count];
        var xs = new float[count];
        for (var i = 0; i < count; i++)
        {
            glyphs[i] = glyph.Glyphs[0];
            xs[i] = i * glyph.Width;
        }

        _runs.Add(new GlyphRun(glyph.Face!, tab.Run!, tab.Size, 0f, glyphs, xs, to - (count * glyph.Width), count * glyph.Width, tab.TextStart, tab.TextStart)
        {
            IsLeader = true,
        });
    }

    // ---- lines ----

    private void MarkMetrics(out float ascent, out float descent, out float gap)
    {
        var face = _shaper.FaceFor(_input.MarkRun);
        var (size, raise) = SizeAndRaise(_input.MarkRun);
        ascent = Math.Max(0f, (face.Ascent * size) + raise);
        descent = Math.Max(0f, (face.Descent * size) - raise);
        gap = face.LineGap * size;
    }

    private void EnsureLine(int offset)
    {
        if (_lineOpen)
        {
            return;
        }

        _lineOpen = true;
        _lineStartOffset = offset;
        _placedEnd = offset;
        _runs.Clear();
        _boxes.Clear();
        _pending = null;
        _lastText = null;
        _hasContent = false;
        _hadTab = false;
        _hasMetrics = false;
        _asc = 0f;
        _desc = 0f;
        _gap = 0f;
        _trailing = 0f;

        var left = _first ? _firstLeft : _leftBase;
        var right = _rightLimit;
        if (_wrap is not null)
        {
            MarkMetrics(out var a, out var d, out var g);
            var nominal = Math.Max(1f, a + d + g);
            for (var tries = 0; tries < MaxWrapSkips; tries++)
            {
                var bounds = _wrap.BoundsAt(_y, nominal, left, right);
                var l = Math.Max(left, bounds.Left);
                var r = Math.Min(right, bounds.Right);
                if (r - l >= 1f)
                {
                    left = l;
                    right = r;
                    break;
                }

                _y += nominal;
            }
        }

        _lineTop = _y;
        _lineLeft = left;
        _lineRight = right;
        _x = left;
        if (_first && _input.Label is { } label)
        {
            PlaceLabel(label);
        }
    }

    private void PlaceLabel(NumberingLabel label)
    {
        _scratch.Clear();
        var (size, raise) = SizeAndRaise(label.Run);
        _shaper.AddText(_scratch, label.Run, size, raise, label.Text, 0, false);
        var pieces = _scratch.ToArray();
        foreach (var piece in pieces)
        {
            PlaceText(piece, true);
        }

        switch (label.Suffix)
        {
            case NumberSuffix.Tab:
                ApplyTab(MakeTab(label.Run, _lineStartOffset), true);
                break;
            case NumberSuffix.Space:
                _scratch.Clear();
                _shaper.AddText(_scratch, label.Run, size, raise, " ", 0, false);
                foreach (var piece in _scratch.ToArray())
                {
                    PlaceText(piece, true);
                }

                break;
            default:
                break;
        }

        _hasContent = false;
        _hadTab = false;
    }

    private void FinishLine(int end, bool mandatory, bool last)
    {
        CloseSegment(true);
        float asc = _asc;
        float desc = _desc;
        float gap = _gap;
        if (!_hasMetrics)
        {
            MarkMetrics(out asc, out desc, out gap);
        }

        var natural = asc + desc + gap;
        float height;
        float baseline;
        switch (_para.LineRule)
        {
            case LineSpacingRule.Exact:
                height = Math.Max(0f, _para.LineValue);
                baseline = asc + desc > 0f ? height * asc / (asc + desc) : height;
                break;
            case LineSpacingRule.AtLeast:
                height = Math.Max(natural, _para.LineValue);
                baseline = height - desc;
                break;
            default:
                height = natural * (_para.LineValue > 0f ? _para.LineValue : 1f);
                baseline = height - desc;
                break;
        }

        if (!float.IsFinite(height))
        {
            height = natural;
        }

        baseline = Math.Clamp(float.IsFinite(baseline) ? baseline : height, 0f, height);

        var contentRight = _x - _trailing;
        var shift = 0f;
        var width = Math.Max(0f, contentRight - _lineLeft);
        var free = _lineRight - contentRight;
        if (_para.Alignment == ParagraphAlignment.Justify)
        {
            if (!last && (!mandatory || _input.ExpandShiftReturn) && !_hadTab && Justifier.Apply(_runs, _boxes, _lastText, free))
            {
                width = _lineRight - _lineLeft;
            }
        }
        else if (free > 0f && _para.Alignment is ParagraphAlignment.Center or ParagraphAlignment.Right)
        {
            shift = _para.Alignment == ParagraphAlignment.Center ? free / 2f : free;
        }

        if (shift != 0f)
        {
            foreach (var run in _runs)
            {
                run.X += shift;
            }

            foreach (var box in _boxes)
            {
                box.X += shift;
            }
        }

        foreach (var box in _boxes)
        {
            box.Top = baseline - Math.Max(0f, box.Box.Height);
        }

        _lines.Add(new LineBox(
            _lineTop, height, asc, desc, baseline, _lineLeft + shift, width, _lineStartOffset, Math.Max(end, _lineStartOffset),
            MergeRuns(_runs), [.. _boxes]));
        _y = _lineTop + height;
        _first = false;
        _lineOpen = false;
        _runs.Clear();
        _boxes.Clear();
    }

    // One glyph run per source run and line: neighbours with the same run, face, size and raise that touch each other become one.
    private static GlyphRun[] MergeRuns(List<GlyphRun> runs)
    {
        var result = new List<GlyphRun>(Math.Min(runs.Count, 8));
        var i = 0;
        while (i < runs.Count)
        {
            var first = runs[i];
            var j = i + 1;
            var glyphs = first.Glyphs.Length;
            var end = first;
            while (j < runs.Count && CanMerge(end, runs[j]))
            {
                end = runs[j];
                glyphs += end.Glyphs.Length;
                j++;
            }

            if (j == i + 1)
            {
                result.Add(first);
            }
            else
            {
                var ids = new ushort[glyphs];
                var xs = new float[glyphs];
                var at = 0;
                for (var k = i; k < j; k++)
                {
                    var part = runs[k];
                    part.Glyphs.CopyTo(ids.AsSpan(at));
                    var partXs = part.Xs;
                    for (var g = 0; g < partXs.Length; g++)
                    {
                        xs[at + g] = part.X - first.X + partXs[g];
                    }

                    at += partXs.Length;
                }

                result.Add(new GlyphRun(first.Face, first.Run, first.Size, first.Raise, ids, xs, first.X, end.X + end.Width - first.X, first.TextStart, end.TextEnd));
            }

            i = j;
        }

        return [.. result];
    }

    private static bool CanMerge(GlyphRun left, GlyphRun right) =>
        !left.IsLabel && !left.IsLeader && !right.IsLabel && !right.IsLeader
        && !left.IsHyphen && !right.IsHyphen
        && ReferenceEquals(left.Run, right.Run) && ReferenceEquals(left.Face, right.Face)
        && left.Size == right.Size && left.Raise == right.Raise
        && MathF.Abs(left.X + left.Width - right.X) < 0.01f;
}
