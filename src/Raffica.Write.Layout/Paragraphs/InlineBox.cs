namespace Raffica.Write.Layout;

/// <summary>What ends a paragraph layout early or a line by force.</summary>
public enum BreakKind
{
    Line,
    Page,
    Column,
}

/// <summary>A reference to a picture by an id the caller resolves (a relationship id); the layout only carries it.</summary>
public readonly record struct ImageRef(string Id);

/// <summary>One flattened piece of a paragraph: a <see cref="TextSpan"/>, an <see cref="InlineBox"/> or a <see cref="BreakItem"/>.</summary>
public abstract record ParagraphItem;

/// <summary>Text with one run format (field results as their cached text, hyperlink text as text, tracked insertions in, deletions out).</summary>
public sealed record TextSpan(string Text, ResolvedRun Run) : ParagraphItem;

/// <summary>An inline object of a fixed size that sits on the baseline: a picture, or a placeholder for content the layout cannot draw.</summary>
public sealed record InlineBox(float Width, float Height, ImageRef? Image) : ParagraphItem
{
    /// <summary>True for a box that stands for content the layout cannot draw (it has no picture).</summary>
    public bool IsPlaceholder => Image is null;

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
