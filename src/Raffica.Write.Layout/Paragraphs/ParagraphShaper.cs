using HarfBuzzSharp;

namespace Raffica.Write.Layout;

internal enum PieceKind
{
    Text,
    Tab,
    Box,
}

/// <summary>One shaped piece of a line break chunk: text of one face and size, a tab, or an inline box.</summary>
internal sealed class Piece
{
    public PieceKind Kind { get; init; }

    public FontFace? Face { get; init; }

    public ResolvedRun? Run { get; init; }

    public float Size { get; init; }

    public float Raise { get; init; }

    public ushort[] Glyphs { get; init; } = [];

    public float[] Xs { get; init; } = [];

    public int[]? Spaces { get; init; }

    /// <summary>The paragraph text offset of the cluster each glyph belongs to (HarfBuzz's cluster value plus the piece's offset).</summary>
    public int[] Clusters { get; init; } = [];

    public float Width { get; init; }

    /// <summary>The width of the spaces that end the text of this piece (only a piece that ends its chunk has any).</summary>
    public float TrailingWidth { get; init; }

    public int TrailStart { get; init; }

    public int TextStart { get; init; }

    public int TextEnd { get; init; }

    public float Ascent { get; init; }

    public float Descent { get; init; }

    public float Gap { get; init; }

    public InlineBox? Box { get; init; }

    public bool IsHyphen { get; init; }
}

/// <summary>
/// Shapes text with HarfBuzz at the face's own scale, one call per piece. A glyph the face lacks is shaped again with the default
/// face of the catalog; when that lacks it too, the .notdef glyph stays. Not thread safe: one per layout call.
/// </summary>
internal sealed class ParagraphShaper : IDisposable
{
    private const string FallbackFamily = "Carlito";

    private readonly FontCatalog _fonts;
    private readonly Dictionary<ResolvedRun, FontFace> _faces = new(ReferenceEqualityComparer.Instance);
    private readonly HarfBuzzSharp.Buffer _buffer = new();
    private readonly List<int> _spaceScratch = [];
    private readonly List<(int Start, int End, bool Missing)> _segments = [];

    public ParagraphShaper(FontCatalog fonts) => _fonts = fonts;

    public FontFace FaceFor(ResolvedRun run)
    {
        if (!_faces.TryGetValue(run, out var face))
        {
            face = _fonts.Resolve(run.FontName, run.Bold, run.Italic);
            _faces[run] = face;
        }

        return face;
    }

    public void Dispose() => _buffer.Dispose();

    /// <summary>The advance of a text in one face and size, without making a piece of it.</summary>
    public float Measure(FontFace face, ReadOnlySpan<char> text, float size)
    {
        var n = ShapeRaw(face, text);
        if (n == 0)
        {
            return 0f;
        }

        face.Shaper.GetScale(out var scale, out _);
        var factor = scale > 0 ? size / scale : 0f;
        var width = 0f;
        foreach (var position in _buffer.GlyphPositions)
        {
            width += position.XAdvance * factor;
        }

        return width;
    }

    /// <summary>
    /// Shapes <paramref name="text"/> and appends its pieces (one, or more when a fallback face is needed). <paramref name="offset"/> is
    /// where the text starts in the paragraph; <paramref name="endsChunk"/> says that only break characters follow, so trailing spaces hang.
    /// </summary>
    public void AddText(List<Piece> into, ResolvedRun run, float size, float raise, ReadOnlySpan<char> text, int offset, bool endsChunk)
    {
        if (text.Length == 0)
        {
            return;
        }

        var face = FaceFor(run);
        FontFace? fallback = _fonts.Resolve(FallbackFamily, run.Bold, run.Italic);
        if (ReferenceEquals(fallback, face))
        {
            fallback = null;
        }

        var count = ShapeRaw(face, text);
        if (fallback is not null && FindMissing(text, count))
        {
            // Copy the segments: shaping each one reuses the scratch lists.
            var segments = _segments.ToArray();
            for (var i = 0; i < segments.Length; i++)
            {
                var (start, end, missing) = segments[i];
                var segment = text[start..end];
                var segmentFace = missing ? fallback : face;
                ShapeRaw(segmentFace, segment);
                into.Add(Build(segmentFace, run, size, raise, segment, offset + start, endsChunk && i == segments.Length - 1));
            }

            return;
        }

        into.Add(Build(face, run, size, raise, text, offset, endsChunk));
    }

    /// <summary>The visible hyphen that ends a line at <paramref name="offset"/>, in the face and size of the text before it. It draws no text of the paragraph.</summary>
    public Piece Hyphen(ResolvedRun run, float size, float raise, int offset)
    {
        var face = FaceFor(run);
        ShapeRaw(face, "-");
        return Build(face, run, size, raise, "-", offset, false, hyphen: true);
    }

    private int ShapeRaw(FontFace face, ReadOnlySpan<char> text)
    {
        _buffer.ClearContents();
        if (text.Length == 0)
        {
            return 0;
        }

        _buffer.Direction = Direction.LeftToRight;
        _buffer.AddUtf16(text);
        _buffer.GuessSegmentProperties();
        face.Shaper.Shape(_buffer);
        return _buffer.Length;
    }

    // Reads the shaped buffer: the text ranges of the clusters that came out as .notdef, merged with the ranges between them.
    private bool FindMissing(ReadOnlySpan<char> text, int count)
    {
        _segments.Clear();
        var infos = _buffer.GlyphInfos;
        var any = false;
        for (var i = 0; i < count; i++)
        {
            if (infos[i].Codepoint == 0)
            {
                any = true;
                break;
            }
        }

        if (!any)
        {
            return false;
        }

        var covered = 0;
        for (var i = 0; i < count; i++)
        {
            if (infos[i].Codepoint != 0)
            {
                continue;
            }

            var start = (int)infos[i].Cluster;
            var end = text.Length;
            for (var j = i + 1; j < count; j++)
            {
                if ((int)infos[j].Cluster > start)
                {
                    end = (int)infos[j].Cluster;
                    break;
                }
            }

            if (start < covered || start >= text.Length)
            {
                continue;
            }

            if (start > covered)
            {
                _segments.Add((covered, start, false));
            }

            if (end <= start)
            {
                end = Math.Min(text.Length, start + 1);
            }

            if (_segments.Count > 0 && _segments[^1].Missing && _segments[^1].End == start)
            {
                _segments[^1] = (_segments[^1].Start, end, true);
            }
            else
            {
                _segments.Add((start, end, true));
            }

            covered = end;
        }

        if (covered < text.Length)
        {
            _segments.Add((covered, text.Length, false));
        }

        return _segments.Count > 0;
    }

    private Piece Build(FontFace face, ResolvedRun run, float size, float raise, ReadOnlySpan<char> text, int offset, bool endsChunk, bool hyphen = false)
    {
        var infos = _buffer.GlyphInfos;
        var positions = _buffer.GlyphPositions;
        var n = infos.Length;
        face.Shaper.GetScale(out var scale, out _);
        var factor = scale > 0 ? size / scale : 0f;

        var trailingChars = 0;
        if (endsChunk)
        {
            var i = text.Length;
            while (i > 0 && text[i - 1] == ' ')
            {
                i--;
                trailingChars++;
            }
        }

        var trailStartChar = text.Length - trailingChars;
        var glyphs = new ushort[n];
        var xs = new float[n];
        var clusters = new int[n];
        var pen = 0f;
        var trailGlyph = n;
        var trailPen = 0f;
        _spaceScratch.Clear();
        for (var i = 0; i < n; i++)
        {
            var cluster = (int)infos[i].Cluster;
            if (trailingChars > 0 && trailGlyph == n && cluster >= trailStartChar)
            {
                trailGlyph = i;
                trailPen = pen;
            }

            glyphs[i] = (ushort)infos[i].Codepoint;
            clusters[i] = offset + cluster;
            xs[i] = pen + (positions[i].XOffset * factor);
            if ((uint)cluster < (uint)text.Length && text[cluster] == ' ')
            {
                _spaceScratch.Add(i);
            }

            pen += positions[i].XAdvance * factor;
        }

        return new Piece
        {
            Kind = PieceKind.Text,
            Face = face,
            Run = run,
            Size = size,
            Raise = raise,
            Glyphs = glyphs,
            Xs = xs,
            Clusters = clusters,
            Spaces = _spaceScratch.Count == 0 ? null : _spaceScratch.ToArray(),
            Width = pen,
            TrailingWidth = trailGlyph < n ? pen - trailPen : 0f,
            TrailStart = trailGlyph,
            TextStart = offset,
            TextEnd = hyphen ? offset : offset + text.Length,
            Ascent = Math.Max(0f, (face.Ascent * size) + raise),
            Descent = Math.Max(0f, (face.Descent * size) - raise),
            Gap = face.LineGap * size,
            IsHyphen = hyphen,
        };
    }
}
