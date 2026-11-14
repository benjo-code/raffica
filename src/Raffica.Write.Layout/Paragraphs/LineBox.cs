namespace Raffica.Write.Layout;

/// <summary>A run of positioned glyphs of one face on one line. Positions are points from the left edge of the text area.</summary>
public sealed class GlyphRun
{
    private readonly ushort[] _glyphs;
    private float[] _xs;

    internal GlyphRun(FontFace face, ResolvedRun run, float size, float raise, ushort[] glyphs, float[] xs, float x, float width, int textStart, int textEnd)
    {
        Face = face;
        Run = run;
        Size = size;
        Raise = raise;
        _glyphs = glyphs;
        _xs = xs;
        X = x;
        Width = width;
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

    /// <summary>The x offset of each glyph from <see cref="X"/>.</summary>
    public ReadOnlySpan<float> Xs => _xs;

    /// <summary>Where the run starts, in points from the left edge of the text area.</summary>
    public float X { get; internal set; }

    public float Width { get; internal set; }

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

    internal void SetXs(float[] xs) => _xs = xs;
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
        GlyphRun[] runs, PlacedBox[] boxes)
    {
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
