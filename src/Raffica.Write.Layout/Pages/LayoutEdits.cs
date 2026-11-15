using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>Where a point of a page lies in the text: the position, and whether the point is on the later half of the character (or past the end of the line).</summary>
public readonly record struct TextHit(int Page, TextPosition Position, bool Trailing);

/// <summary>A caret: its page, its x on the page, and the top and height of its line (points).</summary>
public readonly record struct CaretBox(int Page, float X, float Top, float Height);

/// <summary>A rectangle of a selection on a page (points).</summary>
public readonly record struct TextBox(float X, float Y, float Width, float Height);

/// <summary>How a caret moves.</summary>
public enum CaretMove
{
    Left,
    Right,
    WordLeft,
    WordRight,
    LineStart,
    LineEnd,
    Up,
    Down,
    PageUp,
    PageDown,
    ParagraphStart,
    ParagraphEnd,
    DocumentStart,
    DocumentEnd,
}

public sealed partial class DocumentLayout
{
    private TextNavigator? _navigator;

    // Stateless over the published pages, so two threads that make it at once make equal objects.
    private TextNavigator Navigator => _navigator ??= new TextNavigator(_pages, _paragraphPages);

    /// <summary>
    /// The position under a point of a laid-out page, or null for a page that is not laid out or has no text. A point outside every
    /// line gets the nearest line; a point right of a line's text gets its end (before trailing spaces), one left of it its start; the
    /// hyphen of a hyphenated word and the label of a list paragraph are never hit; a field result, a picture or any other atomic unit
    /// snaps to its nearer edge. Lengths are points from the top left of the page.
    /// </summary>
    public TextHit? HitTest(int pageIndex, float x, float y) => Navigator.HitTest(pageIndex, x, y);

    /// <summary>
    /// The caret at a position, or null when no published page shows it. <paramref name="pageHint"/> is searched first (a header or footer is
    /// on many pages), then the pages that show the paragraph, from the first. A position at the end of a line that wrapped is the start
    /// of the next line (there is no caret affinity); a position past the paragraph's text is not found.
    /// </summary>
    public CaretBox? Caret(TextPosition position, int pageHint) => Navigator.Caret(position, pageHint);

    /// <summary>
    /// The position a move leads to. Never leaves the story, and in a table never leaves the cell (so a document move in a cell stays in the
    /// cell, and Left at the start of a cell stays); a field result and every atomic unit is stepped over as one. <c>Up</c> and <c>Down</c> aim
    /// at <paramref name="goalX"/> (page x; the caret's own x when null) and go to the start or end of the line when there is no line
    /// above or below. Only published pages are known: a move to the end of the document goes to the end of what is laid out. Returns
    /// <paramref name="from"/> when the move has nowhere to go, null when no published page shows <paramref name="from"/>.
    /// </summary>
    public TextPosition? Move(TextPosition from, CaretMove move, float? goalX, int pageHint) => Navigator.Move(from, move, goalX, pageHint);

    /// <summary>The rectangles that cover a selection on one page, one per line (the selection may begin or end on other pages); empty when the page does not show it.</summary>
    public IReadOnlyList<TextBox> SelectionBoxes(TextRange range, int pageIndex) => Navigator.SelectionBoxes(range, pageIndex);
}
