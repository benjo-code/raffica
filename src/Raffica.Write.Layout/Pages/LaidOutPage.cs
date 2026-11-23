using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>Which body block fills a vertical range of a page (for <see cref="DocumentLayout.BlockAt"/>).</summary>
internal readonly record struct BlockSpan(int BodyIndex, float Top, float Bottom);

/// <summary>
/// One finished page (or, in the writing view, one strip of the text). Immutable once published, so any thread may read it while
/// the layout goes on.
/// </summary>
public sealed class LaidOutPage
{
    internal LaidOutPage(int index, string number, float width, float height, bool isStrip, PageItem[] items, string text, BlockSpan[] spans, bool isProvisional = false, TextLine[]? textLines = null, ChromeInfo? chrome = null)
    {
        Chrome = chrome;
        IsProvisional = isProvisional;
        ItemArray = items;
        Index = index;
        Number = number;
        Width = width;
        Height = height;
        IsStrip = isStrip;
        Items = items;
        Text = text;
        Spans = spans;
        TextLines = textLines ?? [];
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
    /// True for a page whose header or footer shows a page count (NUMPAGES, SECTIONPAGES) that was not known when it was published: it
    /// shows "…" there, and the layout replaces it by a final page object (this one stays as it is). False on the replacement and on
    /// every page of a document without such fields. See <see cref="DocumentLayout.Revision"/>.
    /// </summary>
    public bool IsProvisional { get; }

    internal PageItem[] ItemArray { get; }

    /// <summary>
    /// The type of the header and footer parts the layout used for this page (the first-page parts, the even-page parts or the default ones), as it decided it from the section's different first page, the left and right pages
    /// flag and the page's own number; null for a page made without page chrome (a strip of the writing view). A reader that needs to know which part a page shows asks the page, never re-derives it.
    /// </summary>
    public HeaderFooterType? ChromeType => Chrome?.Type;

    /// <summary>The number of the page as a value (the number that <see cref="Number"/> prints in the format of its section, after a restart); null for a page made without page chrome.</summary>
    public int? NumberValue => Chrome?.Number;

    /// <summary>Where the header and footer sit in this page and what they were made with; null for a page made without page chrome (a strip of the writing view).</summary>
    internal ChromeInfo? Chrome { get; }

    /// <summary>
    /// The text of the page for the corpus comparison: the label of a numbered paragraph, a space, then the lines of each paragraph,
    /// one line per row, joined by <c>\n</c>. The hyphen that automatic hyphenation draws at a line end is not part of the text
    /// (the glyph run has <see cref="GlyphRun.IsHyphen"/> and an empty text range), so "hyphen" and "ation" appear without the "-"
    /// that Word's PDF text has; a comparison must account for it.
    /// </summary>
    public string Text { get; }

    internal BlockSpan[] Spans { get; }

    /// <summary>The line records of the page in reading order (header, body, notes, footer): the way back from what is drawn to the model.</summary>
    internal TextLine[] TextLines { get; }
}
