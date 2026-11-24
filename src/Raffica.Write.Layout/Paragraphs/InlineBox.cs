namespace Raffica.Write.Layout;

/// <summary>What ends a paragraph layout early or a line by force.</summary>
public enum BreakKind
{
    Line,
    Page,
    Column,
}

/// <summary>
/// A reference to a picture by an id the caller resolves; the layout only carries it. The id is qualified by the story it is read
/// in, because relationship ids are only unique inside one part (header1.xml and the body both have an <c>rId1</c>):
/// <c>"&lt;story&gt;#&lt;relationship id&gt;"</c>. The story is <c>document</c> for the body (and its tables and text boxes),
/// <c>header:&lt;id&gt;</c> for a header or footer part, where &lt;id&gt; is the relationship id the document's
/// <c>w:headerReference</c> or <c>w:footerReference</c> uses (the key of <c>WordDocument.HeadersFooters</c>), and
/// <c>footnotes</c> or <c>endnotes</c> for the notes parts. This string is what <see cref="LayoutOptions.ImageBytes"/> and
/// <see cref="ImageCache"/> receive: the caller finds the part of the story, then the relationship in that part's own rels.
/// </summary>
public readonly record struct ImageRef(string Id)
{
    /// <summary>The story of the main document body.</summary>
    public const string DocumentStory = "document";

    /// <summary>The story of the footnotes part.</summary>
    public const string FootnotesStory = "footnotes";

    /// <summary>The story of the endnotes part.</summary>
    public const string EndnotesStory = "endnotes";

    /// <summary>The story of a header or footer part, by the relationship id the document uses for it.</summary>
    public static string HeaderFooterStory(string documentRelationshipId) => "header:" + documentRelationshipId;

    /// <summary>The id handed out for relationship <paramref name="relationshipId"/> read in <paramref name="story"/>.</summary>
    public static string Qualify(string story, string relationshipId) => story + "#" + relationshipId;
}

/// <summary>One flattened piece of a paragraph: a <see cref="TextSpan"/>, an <see cref="InlineBox"/> or a <see cref="BreakItem"/>.</summary>
public abstract record ParagraphItem;

/// <summary>Text with one run format (field results as their cached text, hyperlink text as text, tracked insertions in, deletions out).</summary>
public sealed record TextSpan(string Text, ResolvedRun Run) : ParagraphItem;

/// <summary>An inline object of a fixed size that sits on the baseline: a picture, or a placeholder for content the layout cannot draw.</summary>
public sealed record InlineBox(float Width, float Height, ImageRef? Image) : ParagraphItem
{
    /// <summary>True for a box that stands for content the layout cannot draw (it has no picture).</summary>
    public bool IsPlaceholder => Image is null;

    /// <summary>Where the picture of this box is in the model, for the page's object map; null for a box that is no editable picture of the body (a placeholder, a picture of a header, a shape).</summary>
    internal PictureAnchor? Anchor { get; init; }

    /// <summary>The one-em placeholder of an unknown inline element, as big as the text around it.</summary>
    public static InlineBox Placeholder(ResolvedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new InlineBox(run.SizePoints, run.SizePoints, null);
    }
}

/// <summary>A line, page or column break inside a paragraph. A page or column break ends the layout of the paragraph.</summary>
public sealed record BreakItem(BreakKind Kind) : ParagraphItem;

/// <summary>Everything the layout needs of one paragraph. The items are in reading order; offsets in the result count characters of the text they make.</summary>
public sealed record ParagraphInput(ResolvedParagraph Paragraph, IReadOnlyList<ParagraphItem> Items, ResolvedRun MarkRun, NumberingLabel? Label = null)
{
    /// <summary>The default tab interval in points (720 twips unless the document settings say otherwise).</summary>
    public float DefaultTabStop { get; init; } = 36f;

    /// <summary>Whether a justified line that ends in a manual line break is stretched (Word's default; off with the doNotExpandShiftReturn compatibility setting).</summary>
    public bool ExpandShiftReturn { get; init; } = true;

    /// <summary>Whether words may be hyphenated at the line end (the document setting w:autoHyphenation; a paragraph that suppresses it still never is).</summary>
    public bool AutoHyphenation { get; init; }

    /// <summary>The footnote references of the paragraph, by the item that draws their number (the page layout finds the line each one lands on).</summary>
    internal IReadOnlyList<NoteMark>? NoteMarks { get; init; }

    /// <summary>The floating objects anchored in the paragraph (null for almost every paragraph). The page layout places them; the paragraph layouter ignores them.</summary>
    internal IReadOnlyList<FloatSpec>? Floats { get; init; }

    /// <summary>
    /// The projection the items were built from (the whole paragraph, also on the continuation after a break): the text coordinates and
    /// sources of editing. The text the layouter counts is not always the projection's text (a float with no room becomes a leading box,
    /// a hidden break counts one, a float the page places counts none), so layout offsets are never projection offsets: they go through
    /// <see cref="ProjectionMap"/>.
    /// </summary>
    internal Model.ParagraphProjection? Projection { get; init; }

    /// <summary>The host the projection was built with (the layout's knowledge of fields, note marks and hidden runs); lines carry it for the editor.</summary>
    internal Model.IProjectionHost? Host { get; init; }

    /// <summary>Translates offsets of the layout's text into <see cref="Projection"/> offsets; null means they are equal. After a break the map is the one of the continuation (its offset 0 is where the continuation starts).</summary>
    internal OffsetMap? ProjectionMap { get; init; }

    /// <summary>The story and paragraph the lines come from; null for a paragraph that is not part of a story (a text box), which leaves no text lines.</summary>
    internal ParagraphSource? Source { get; init; }

    /// <summary>Whether an inline box wider than the text area is scaled down to it (aspect ratio kept). The measuring of table columns turns it off to see the natural size.</summary>
    internal bool ScaleBoxes { get; init; } = true;
}

/// <summary>The horizontal room of one line, in paragraph coordinates (0 is the left edge of the text area).</summary>
public readonly record struct LineBounds(float Left, float Right);

/// <summary>
/// Narrows the room of a line, for text that wraps around a floating object. The layouter asks once per line, with the top of the
/// line (points from the top of the paragraph), a nominal line height and the bounds that indents and margins already allow; the
/// answer is clamped into those bounds. A width below one point means no room: the layouter moves the line down and asks again.
/// </summary>
public interface IWrapSource
{
    /// <summary>The usable bounds of a line whose top is at <paramref name="y"/>.</summary>
    LineBounds BoundsAt(float y, float lineHeight, float left, float right);
}

/// <summary>Finds hyphenation points of a word.</summary>
public interface IHyphenator
{
    /// <summary>Appends to <paramref name="positions"/> the offsets within <paramref name="word"/> before which a hyphen may be put.</summary>
    void Find(ReadOnlySpan<char> word, string? language, List<int> positions);
}
