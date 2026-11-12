using SkiaSharp;

namespace Layout.Common;

/// <summary>Lays out the text of one paragraph at a given width. The two spike candidates (C# with HarfBuzz, Rust with Parley) implement it.</summary>
public interface IParagraphLayouter : IDisposable
{
    IParagraphLayout Layout(Paragraph paragraph, float width);
}

/// <summary>The laid-out lines of one paragraph. Coordinates are local to the paragraph box: x from its left edge, y from the top of its first line.</summary>
public interface IParagraphLayout : IDisposable
{
    IReadOnlyList<LineMetrics> Lines { get; }

    /// <summary>The text offset at a point (clamped to the nearest line and to the line's ends).</summary>
    int HitTest(float x, float y);

    /// <summary>The caret rectangle of a text offset (width 1).</summary>
    RectF Caret(int offset);

    /// <summary>Draws <paramref name="lineCount"/> lines starting at <paramref name="firstLine"/>; <paramref name="y"/> is the top of the first drawn line.</summary>
    void Draw(SKCanvas canvas, float x, float y, int firstLine, int lineCount);
}

/// <summary>The line geometry both candidates and the page breaker agree on: a line box is (ascent + descent) times the line spacing, the extra space sits above the text.</summary>
public static class LineGeometry
{
    public static float Height(in LineMetrics m, ParagraphFormat f) => (m.Ascent + m.Descent) * f.LineSpacing;

    /// <summary>Distance from the top of the line box to the baseline.</summary>
    public static float Baseline(in LineMetrics m, ParagraphFormat f) => Height(m, f) - m.Descent;

    /// <summary>Cumulative tops: element i is the top of line i, the last element is the height of the paragraph.</summary>
    public static float[] Tops(IReadOnlyList<LineMetrics> lines, ParagraphFormat f)
    {
        var tops = new float[lines.Count + 1];
        for (int i = 0; i < lines.Count; i++) tops[i + 1] = tops[i] + Height(lines[i], f);
        return tops;
    }

    public static int LineAt(float[] tops, float y)
    {
        int n = tops.Length - 1;
        if (y <= 0) return 0;
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (tops[mid] <= y) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    public static float XOffset(in LineMetrics m, float width, ParagraphFormat f) =>
        f.Alignment == Alignment.Center ? Math.Max(0, (width - m.Width) / 2) : 0;
}
