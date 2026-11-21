namespace Raffica.Write.Layout;

/// <summary>A run of positioned glyphs of one face on one line. Positions are points from the left edge of the text area.</summary>
public sealed class GlyphRun
{
    private readonly ushort[] _glyphs;
    private float[] _xs;
    private float _width;
    private CaretMap? _stops;

    internal GlyphRun(FontFace face, ResolvedRun run, float size, float raise, ushort[] glyphs, float[] xs, float x, float width, int textStart, int textEnd)
    {
        Face = face;
        Run = run;
        Size = size;
        Raise = raise;
        _glyphs = glyphs;
        _xs = xs;
        X = x;
        _width = width;
        TextStart = textStart;
        TextEnd = textEnd;
        TrailStart = glyphs.Length;
    }

    public FontFace Face { get; }

    /// <summary>The formatting the text asked for (colour, underline, strike, highlight); the size and raise below are what the layout used.</summary>
    public ResolvedRun Run { get; }

    /// <summary>The drawn size in points (smaller than the run's for superscript, subscript and small capitals).</summary>
    public float Size { get; }

    /// <summary>How far the baseline of this run sits above the line's baseline, in points (negative for subscript).</summary>
    public float Raise { get; }

    public ReadOnlySpan<ushort> Glyphs => _glyphs;

    // The backing arrays, for page items that share them (never changed once the line is final).
    internal ushort[] GlyphArray => _glyphs;

    internal float[] XArray => _xs;

    /// <summary>The x offset of each glyph from <see cref="X"/>.</summary>
    public ReadOnlySpan<float> Xs => _xs;

    /// <summary>Where the run starts, in points from the left edge of the text area.</summary>
    public float X { get; internal set; }

    public float Width
    {
        get => _width;
        internal set
        {
            _width = value;
            _stops = null;
        }
    }

    /// <summary>The range of the paragraph text this run draws (empty for a label and for leaders).</summary>
    public int TextStart { get; }

    public int TextEnd { get; }

    /// <summary>True for the number or bullet of a list paragraph.</summary>
    public bool IsLabel { get; internal init; }

    /// <summary>True for the dots, hyphens or underscores that fill a tab.</summary>
    public bool IsLeader { get; internal init; }

    /// <summary>True for the hyphen added where automatic hyphenation breaks a word; it draws no text (<see cref="TextStart"/> equals <see cref="TextEnd"/>).</summary>
    public bool IsHyphen { get; internal init; }

    // The glyph indices of the spaces that justification may stretch, and the first glyph of the spaces that end the text of the run.
    internal int[]? Spaces { get; init; }

    internal int TrailStart { get; init; }

    internal void SetXs(float[] xs)
    {
        _xs = xs;
        _stops = null;
    }

    // The cluster of each glyph (paragraph text offsets) and the paragraph text, for the caret stops; null for runs that draw no own text.
    internal int[]? Clusters { get; init; }

    internal string? Text { get; init; }

    /// <summary>The paragraph text offsets at which a caret may stand inside this run (grapheme boundaries, both edges included); empty for a hyphen, a label and a leader.</summary>
    public IReadOnlyList<int> CaretStops => Map().Offsets;

    /// <summary>
    /// The x of a caret at <paramref name="textOffset"/>, in points relative to the line (from the left edge of its text area, the origin
    /// <c>TextLine.X</c> puts on the page), final after justification.
    /// An offset inside a ligature divides it equally; an offset that is no stop snaps to the previous stop. A run without stops answers <see cref="X"/>.
    /// </summary>
    public float CaretX(int textOffset)
    {
        var map = Map();
        var offsets = map.Offsets;
        if (offsets.Length == 0)
        {
            return X;
        }

        var i = Array.BinarySearch(offsets, textOffset);
        if (i < 0)
        {
            i = Math.Max(0, ~i - 1);
        }

        return X + map.Xs[i];
    }

    private CaretMap Map() => _stops ??= CaretMap.Build(this);

    // Stops and their x relative to X. Shaping is always left to right today, so the clusters ascend with x; should they not, the stops are
    // spread over the width by grapheme index, which keeps them monotonic.
    private sealed class CaretMap(int[] offsets, float[] xs)
    {
        public int[] Offsets { get; } = offsets;

        public float[] Xs { get; } = xs;

        public static CaretMap Build(GlyphRun run)
        {
            var clusters = run.Clusters;
            var text = run.Text;
            var start = run.TextStart;
            var end = run.TextEnd;
            if (run.IsHyphen || clusters is null || clusters.Length == 0 || text is null || end <= start || end > text.Length)
            {
                return new CaretMap([], []);
            }

            var boundaries = new List<int>(end - start + 1) { start };
            for (var at = start; at < end;)
            {
                at += Math.Max(1, System.Globalization.StringInfo.GetNextTextElementLength(text.AsSpan(at, end - at)));
                boundaries.Add(Math.Min(at, end));
            }

            var offsets = boundaries.ToArray();
            var xs = new float[offsets.Length];
            var glyphXs = run._xs;
            var width = run._width;

            // One entry per cluster value, in glyph order.
            var starts = new List<(int Cluster, float X)>(clusters.Length);
            var ascending = true;
            for (var g = 0; g < clusters.Length; g++)
            {
                if (g > 0 && clusters[g] == clusters[g - 1])
                {
                    continue;
                }

                if (g > 0 && clusters[g] < clusters[g - 1])
                {
                    ascending = false;
                    break;
                }

                starts.Add((clusters[g], g == 0 ? 0f : glyphXs[g]));
            }

            if (!ascending || starts[0].Cluster != start)
            {
                for (var i = 0; i < offsets.Length; i++)
                {
                    xs[i] = width * i / (offsets.Length - 1);
                }

                return new CaretMap(offsets, xs);
            }

            var k = 0;
            var first = 0;
            for (var i = 0; i < offsets.Length; i++)
            {
                var o = offsets[i];
                if (o >= end)
                {
                    xs[i] = width;
                    continue;
                }

                var advanced = false;
                while (k + 1 < starts.Count && starts[k + 1].Cluster <= o)
                {
                    k++;
                    advanced = true;
                }

                if (advanced || i == 0)
                {
                    first = i;
                }

                var groupEnd = k + 1 < starts.Count ? starts[k + 1].Cluster : end;
                var groupX = starts[k].X;
                if (o == starts[k].Cluster)
                {
                    xs[i] = groupX;
                    continue;
                }

                var count = 0;
                while (first + count < offsets.Length && offsets[first + count] < groupEnd)
                {
                    count++;
                }

                var nextX = k + 1 < starts.Count ? starts[k + 1].X : width;
                xs[i] = groupX + ((nextX - groupX) * (i - first) / count);
            }

            return new CaretMap(offsets, xs);
        }
    }
}

/// <summary>An inline box placed on a line. <see cref="Top"/> is measured from the top of the line, <see cref="X"/> from the text area.</summary>
public sealed class PlacedBox
{
    internal PlacedBox(InlineBox box, float x, float top)
    {
        Box = box;
        X = x;
        Top = top;
    }

    public InlineBox Box { get; }

    public float X { get; internal set; }

    public float Top { get; internal set; }
}

/// <summary>One laid out line of a paragraph. Vertical values are points; <see cref="Top"/> counts from the top of the paragraph.</summary>
public sealed class LineBox
{
    internal LineBox(
        float top, float height, float ascent, float descent, float baseline, float x, float width, int start, int end,
        GlyphRun[] runs, PlacedBox[] boxes, float extra = 0f)
    {
        Extra = extra;
        Top = top;
        Height = height;
        Ascent = ascent;
        Descent = descent;
        Baseline = baseline;
        X = x;
        Width = width;
        Start = start;
        End = end;
        Runs = runs;
        Boxes = boxes;
    }

    /// <summary>The room a line spacing above single (an automatic multiple) adds below the text of the line. Word lets it hang below the text area at the end of a page, so a line needs only <see cref="Height"/> minus this to fit.</summary>
    public float Extra { get; }

    public float Top { get; }

    /// <summary>The height of the line after the paragraph's line spacing.</summary>
    public float Height { get; }

    /// <summary>The tallest thing above the baseline (font ascent, raised text, inline pictures).</summary>
    public float Ascent { get; }

    /// <summary>The deepest thing below the baseline.</summary>
    public float Descent { get; }

    /// <summary>The distance from the top of the line to the baseline.</summary>
    public float Baseline { get; }

    /// <summary>Where the first thing of the line starts, after indents and alignment.</summary>
    public float X { get; }

    /// <summary>The width of the content without trailing spaces (the whole room of a justified line).</summary>
    public float Width { get; }

    /// <summary>The text range of the line, <c>[Start, End)</c>, including trailing spaces and the break that ended it.</summary>
    public int Start { get; }

    public int End { get; }

    public IReadOnlyList<GlyphRun> Runs { get; }

    public IReadOnlyList<PlacedBox> Boxes { get; }
}

/// <summary>The lines of one paragraph.</summary>
public sealed class ParagraphLayout
{
    internal ParagraphLayout(string text, LineBox[] lines, float height, BreakKind? endBreak, int remainderItem)
    {
        Text = text;
        Lines = lines;
        Height = height;
        EndBreak = endBreak;
        RemainderItem = remainderItem;
    }

    /// <summary>The text of the paragraph that the line ranges count in: span text, U+FFFC for an inline box, a line feed or form feed for a break.</summary>
    public string Text { get; }

    public IReadOnlyList<LineBox> Lines { get; }

    /// <summary>The height of all lines (space before and after the paragraph is the caller's).</summary>
    public float Height { get; }

    /// <summary>The page or column break that ended the layout, or null when the whole paragraph was laid out.</summary>
    public BreakKind? EndBreak { get; }

    public bool EndsWithPageBreak => EndBreak == BreakKind.Page;

    /// <summary>The index of the first item after the break that ended the layout; the number of items when there is none.</summary>
    public int RemainderItem { get; }

    /// <summary>The visible text of a line: without the trailing spaces and the break that ended it.</summary>
    public string TextOf(LineBox line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return Text.AsSpan(line.Start, line.End - line.Start).TrimEnd(" \n\r\f").ToString();
    }
}
