namespace Raffica.Write.Layout;

/// <summary>Which body block fills a vertical range of a page (for <see cref="DocumentLayout.BlockAt"/>).</summary>
internal readonly record struct BlockSpan(int BodyIndex, float Top, float Bottom);

/// <summary>
/// One finished page (or, in the writing view, one strip of the text). Immutable once published, so any thread may read it while
/// the layout goes on.
/// </summary>
public sealed class LaidOutPage
{
    internal LaidOutPage(int index, string number, float width, float height, bool isStrip, PageItem[] items, string text, BlockSpan[] spans)
    {
        Index = index;
        Number = number;
        Width = width;
        Height = height;
        IsStrip = isStrip;
        Items = items;
        Text = text;
        Spans = spans;
    }

    /// <summary>The position in the document, from 0.</summary>
    public int Index { get; }

    /// <summary>The page number as printed: <c>"1"</c>, <c>"iii"</c>, <c>"C"</c>, after the restart and format of the section.</summary>
    public string Number { get; }

    public float Width { get; }

    public float Height { get; }

    /// <summary>True in the writing view: no paper, no margins, no header or footer; <see cref="Height"/> is the height of the text in the strip.</summary>
    public bool IsStrip { get; }

    public IReadOnlyList<PageItem> Items { get; }

    /// <summary>
    /// The text of the page for the corpus comparison: the label of a numbered paragraph, a space, then the lines of each paragraph,
    /// one line per row, joined by <c>\n</c>. The hyphen that automatic hyphenation draws at a line end is not part of the text
    /// (the glyph run has <see cref="GlyphRun.IsHyphen"/> and an empty text range), so "hyphen" and "ation" appear without the "-"
    /// that Word's PDF text has; a comparison must account for it.
    /// </summary>
    public string Text { get; }

    internal BlockSpan[] Spans { get; }
}
