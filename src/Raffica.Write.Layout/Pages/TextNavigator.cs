using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// From a paragraph (by reference) to the indexes of the pages that show lines of it, ascending. The layout thread fills it as it publishes
/// pages (<see cref="Register"/>, before the page is added to the list, so a page that is listed here but not yet published is skipped by
/// the reader through the published count); readers take no lock (a <see cref="ConcurrentDictionary{TKey, TValue}"/> read, and every value
/// is an array that is never changed once it is stored: a new page makes a new array).
/// <para>
/// A page that is replaced by a final version (<see cref="DocumentLayout.Revision"/> rises) has the same index and the same paragraphs, so
/// the map needs no change; <see cref="Register"/> is called for it again and finds every entry present. A model that is replaced
/// (<see cref="DocumentLayout.ReplaceModel"/>) leaves page 1, and with it the paragraphs of the prefix model, published: those entries
/// stay and the later pages add the paragraphs of the new model. A layout that is not complete knows only the pages published so far: a
/// paragraph that begins on a page not laid out yet has no entry.
/// </para>
/// </summary>
internal sealed class ParagraphPageMap
{
    private readonly ConcurrentDictionary<Paragraph, int[]> _pages = new(ReferenceEqualityComparer.Instance);

    /// <summary>The pages that show the paragraph, or null when none does (yet).</summary>
    public int[]? PagesOf(Paragraph paragraph) => _pages.TryGetValue(paragraph, out var pages) ? pages : null;

    /// <summary>Adds the paragraphs of a page's text lines. Only the layout thread calls this, one call at a time.</summary>
    public void Register(LaidOutPage page)
    {
        Paragraph? last = null;
        foreach (var line in page.TextLines)
        {
            if (ReferenceEquals(line.Paragraph, last))
            {
                continue;
            }

            last = line.Paragraph;
            var index = page.Index;
            if (!_pages.TryGetValue(line.Paragraph, out var pages))
            {
                _pages[line.Paragraph] = [index];
                continue;
            }

            var at = Array.BinarySearch(pages, index);
            if (at >= 0)
            {
                continue;
            }

            at = ~at;
            var grown = new int[pages.Length + 1];
            Array.Copy(pages, grown, at);
            grown[at] = index;
            Array.Copy(pages, at, grown, at + 1, pages.Length - at);
            _pages[line.Paragraph] = grown;
        }
    }

    /// <summary>
    /// Drops the entries a replaced page leaves behind: for each paragraph on <paramref name="old"/> that the page which took its place
    /// (<paramref name="replacement"/>, the same index; null when there is none) does not show, the index goes from the paragraph's list. A
    /// stale entry is harmless (a reader scans the page and finds no line of the paragraph), this keeps the map small and lets a removed
    /// paragraph be collected. Pages that are only cut off (the layout is shorter for a while) are not unregistered: the pages that
    /// replace them list the same paragraphs again. Only the layout thread calls this, after the new pages are published.
    /// </summary>
    public void Unregister(LaidOutPage old, LaidOutPage? replacement)
    {
        HashSet<Paragraph>? kept = null;
        if (replacement is not null)
        {
            kept = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
            foreach (var line in replacement.TextLines)
            {
                kept.Add(line.Paragraph);
            }
        }

        Paragraph? last = null;
        foreach (var line in old.TextLines)
        {
            if (ReferenceEquals(line.Paragraph, last))
            {
                continue;
            }

            last = line.Paragraph;
            if (kept is not null && kept.Contains(line.Paragraph))
            {
                continue;
            }

            if (!_pages.TryGetValue(line.Paragraph, out var pages))
            {
                continue;
            }

            var at = Array.BinarySearch(pages, old.Index);
            if (at < 0)
            {
                continue;
            }

            if (pages.Length == 1)
            {
                _pages.TryRemove(line.Paragraph, out _);
                continue;
            }

            var shrunk = new int[pages.Length - 1];
            Array.Copy(pages, shrunk, at);
            Array.Copy(pages, at + 1, shrunk, at, pages.Length - at - 1);
            _pages[line.Paragraph] = shrunk;
        }
    }
}

/// <summary>
/// The places a caret can stand on one line, as projection offsets with their page x (both ascending), and the end of the visible text.
/// Built from the glyph runs' caret stops through the line's <see cref="OffsetMap"/>. An edge the runs do not reach (a leading or trailing
/// inline picture, a placed float's character, a line with no text) gets a stop of its own at the content edge.
/// </summary>
internal sealed class LineStops
{
    private LineStops(int[] offsets, float[] xs, int visibleEnd)
    {
        Offsets = offsets;
        Xs = xs;
        VisibleEnd = visibleEnd;
    }

    public int[] Offsets { get; }

    public float[] Xs { get; }

    /// <summary>
    /// Where the line's visible text ends: the end of the range without the trailing spaces and break characters (of the last line of a
    /// paragraph without the break characters only: the caret may stand after trailing spaces at the end of a paragraph).
    /// </summary>
    public int VisibleEnd { get; }

    public static LineStops Build(TextLine line)
    {
        var text = line.Projection.Text;
        var end = Math.Min(line.End, text.Length);
        var trim = line.End >= text.Length ? "\n\r\f" : " \n\r\f";
        while (end > line.Start && trim.Contains(text[end - 1], StringComparison.Ordinal))
        {
            end--;
        }

        var offsets = new List<int>(32);
        var xs = new List<float>(32);
        var labelEnd = float.NegativeInfinity;
        GlyphRun? owner = null;
        foreach (var run in line.Runs)
        {
            if (run.IsLabel)
            {
                labelEnd = Math.Max(labelEnd, line.X + run.X + run.Width);
                continue;
            }

            if (run.IsLeader || run.IsHyphen)
            {
                continue;
            }

            var stops = run.CaretStops;
            for (var i = 0; i < stops.Count; i++)
            {
                var offset = Math.Clamp(line.Map.Map(stops[i]), line.Start, line.End);
                var x = line.X + run.CaretX(stops[i]);
                var n = offsets.Count;
                if (n > 0 && offset <= offsets[n - 1])
                {
                    // The start of the next run at the offset where the previous one ended wins (after a tab, the text begins there); a flat
                    // stretch of one run (a note mark of two digits for one character) keeps its first stop.
                    if (offset == offsets[n - 1] && !ReferenceEquals(owner, run))
                    {
                        xs[n - 1] = Math.Max(x, xs[n - 1]);
                        owner = run;
                    }

                    continue;
                }

                offsets.Add(offset);
                xs.Add(n > 0 ? Math.Max(x, xs[n - 1]) : x);
                owner = run;
            }
        }

        var contentLeft = line.X + line.ContentLeft;
        var start = float.IsNegativeInfinity(labelEnd) ? contentLeft : Math.Max(labelEnd, contentLeft);
        if (offsets.Count == 0 || offsets[0] > line.Start)
        {
            offsets.Insert(0, line.Start);
            xs.Insert(0, xs.Count > 0 ? Math.Min(start, xs[0]) : start);
        }

        if (offsets[^1] < end)
        {
            offsets.Add(end);
            xs.Add(Math.Max(xs[^1], line.X + line.ContentRight));
        }

        return new LineStops([.. offsets], [.. xs], end);
    }

    /// <summary>The page x of the caret at <paramref name="offset"/>: the stop at or before it (the first one for an offset before the first).</summary>
    public float XAt(int offset)
    {
        var i = Array.BinarySearch(Offsets, offset);
        if (i < 0)
        {
            i = Math.Max(0, ~i - 1);
        }

        return Xs[i];
    }

    /// <summary>The offset nearest to page x <paramref name="x"/> among the stops up to <see cref="VisibleEnd"/>; <paramref name="trailing"/> is true when it is the later of the two stops around the point (or the point is past the end).</summary>
    public int OffsetAt(float x, out bool trailing)
    {
        var n = 1;
        while (n < Offsets.Length && Offsets[n] <= VisibleEnd)
        {
            n++;
        }

        trailing = false;
        if (x <= Xs[0])
        {
            return Offsets[0];
        }

        for (var i = 0; i + 1 < n; i++)
        {
            if (x < Xs[i + 1])
            {
                trailing = x >= (Xs[i] + Xs[i + 1]) / 2f;
                return trailing ? Offsets[i + 1] : Offsets[i];
            }
        }

        trailing = true;
        return Offsets[n - 1];
    }
}

/// <summary>
/// Hit testing, caret geometry and caret movement over the text lines of the published pages. Nothing here changes a page or the model;
/// every method reads published pages only, so any thread may call it while the layout goes on.
/// </summary>
internal sealed class TextNavigator(PageList pages, ParagraphPageMap map)
{
    // How many pages beyond the one a search starts on it looks at (a table row that continues on the next page lies one page on, repeated header rows before it).
    private const int PagesToSearch = 4;

    private readonly record struct Spot(int PageIndex, int Index, TextLine Line);

    public TextHit? HitTest(int pageIndex, float x, float y)
    {
        if ((uint)pageIndex >= (uint)pages.Count)
        {
            return null;
        }

        var lines = pages[pageIndex].TextLines;
        // The line that holds the height of the point wins, whatever its story (a click inside a header, footer or note line is on it). A
        // point in blank space (below the text, in a margin, between the body and its notes) goes to the nearest BODY line, as Word does: the
        // header and footer are entered only by clicking on their text. Of equal heights (columns, cells side by side) the line whose
        // text is nearest sideways wins. A page without a body line falls back to the nearest line of any story.
        var best = -1;
        var bestDy = float.MaxValue;
        var bestDx = float.MaxValue;
        var body = -1;
        var bodyDy = float.MaxValue;
        var bodyDx = float.MaxValue;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var left = line.X + line.ContentLeft;
            var right = line.X + line.ContentRight;
            var dx = x < left ? left - x : (x > right ? x - right : 0f);
            var dy = y < line.Top ? line.Top - y : (y > line.Top + line.Height ? y - line.Top - line.Height : 0f);
            if (dy < bestDy || (dy == bestDy && dx < bestDx))
            {
                best = i;
                bestDy = dy;
                bestDx = dx;
            }

            if (line.Story.Kind == StoryKind.Body && (dy < bodyDy || (dy == bodyDy && dx < bodyDx)))
            {
                body = i;
                bodyDy = dy;
                bodyDx = dx;
            }
        }

        if (bestDy > 0f && body >= 0)
        {
            best = body;
        }

        if (best < 0)
        {
            return null;
        }

        var hit = lines[best];
        var stops = hit.Stops;
        var offset = stops.OffsetAt(x, out var trailing);
        if (hit.Projection.AtomicAt(offset) is var (atomicStart, atomicEnd) && offset > atomicStart)
        {
            // An atomic unit has no inside: the nearer edge.
            var toStart = x - stops.XAt(atomicStart);
            var toEnd = stops.XAt(atomicEnd) - x;
            trailing = toEnd < toStart;
            offset = trailing ? atomicEnd : atomicStart;
        }

        return new TextHit(pageIndex, new TextPosition(hit.Story, hit.Paragraph, offset), trailing);
    }

    public CaretBox? Caret(TextPosition position, int pageHint)
    {
        if (Locate(position, pageHint) is not { } spot)
        {
            return null;
        }

        return new CaretBox(spot.PageIndex, spot.Line.Stops.XAt(position.Offset), spot.Line.Top, spot.Line.Height);
    }

    public IReadOnlyList<TextBox> SelectionBoxes(TextRange range, int pageIndex)
    {
        if ((uint)pageIndex >= (uint)pages.Count || Locate(range.Start, pageIndex) is not { } a || Locate(range.End, pageIndex) is not { } b)
        {
            return [];
        }

        var first = a;
        var last = b;
        var from = range.Start;
        var to = range.End;
        if (a.PageIndex > b.PageIndex || (a.PageIndex == b.PageIndex && (a.Index > b.Index || (a.Index == b.Index && range.Start.Offset > range.End.Offset))))
        {
            (first, last) = (b, a);
            (from, to) = (to, from);
        }

        var lines = pages[pageIndex].TextLines;
        var boxes = new List<TextBox>();
        var begin = first.PageIndex < pageIndex ? 0 : (first.PageIndex == pageIndex ? first.Index : int.MaxValue);
        var finish = last.PageIndex > pageIndex ? lines.Length - 1 : (last.PageIndex == pageIndex ? last.Index : -1);
        for (var i = begin; i <= finish && i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Story != first.Line.Story)
            {
                continue;
            }

            var stops = line.Stops;
            var start = i == first.Index && first.PageIndex == pageIndex ? from.Offset : line.Start;
            var stop = i == last.Index && last.PageIndex == pageIndex ? to.Offset : (line.End >= line.Projection.Text.Length ? stops.VisibleEnd : line.End);
            var x1 = stops.XAt(start);
            var x2 = stops.XAt(stop);
            if (x2 > x1)
            {
                boxes.Add(new TextBox(x1, line.Top, x2 - x1, line.Height));
            }
        }

        return boxes;
    }

    public TextPosition? Move(TextPosition from, CaretMove move, float? goalX, int pageHint)
    {
        if (Locate(from, pageHint) is not { } spot)
        {
            return null;
        }

        var line = spot.Line;
        var text = line.Projection.Text;
        var offset = from.Offset;
        switch (move)
        {
            case CaretMove.Left:
                return offset > 0
                    ? At(line, Previous(line.Projection, offset))
                    : (Neighbor(spot, forward: false) is { } before ? At(before.Line, before.Line.Projection.Text.Length) : from);
            case CaretMove.Right:
                return offset < text.Length
                    ? At(line, Next(line.Projection, offset))
                    : (Neighbor(spot, forward: true) is { } after ? At(after.Line, 0) : from);
            case CaretMove.WordLeft:
                return offset > 0
                    ? At(line, WordStart(line.Projection, offset))
                    : (Neighbor(spot, forward: false) is { } wordBefore ? At(wordBefore.Line, wordBefore.Line.Projection.Text.Length) : from);
            case CaretMove.WordRight:
                return offset < text.Length
                    ? At(line, WordEnd(line.Projection, offset))
                    : (Neighbor(spot, forward: true) is { } wordAfter ? At(wordAfter.Line, 0) : from);
            case CaretMove.LineStart:
                return At(line, line.Start);
            case CaretMove.LineEnd:
                return At(line, line.Stops.VisibleEnd);
            case CaretMove.Up:
            case CaretMove.Down:
                return Vertical(spot, offset, move == CaretMove.Down, goalX);
            case CaretMove.PageUp:
            case CaretMove.PageDown:
                return PageStep(spot, offset, move == CaretMove.PageDown, goalX);
            case CaretMove.ParagraphStart:
                return offset > 0
                    ? At(line, 0)
                    : (Neighbor(spot, forward: false) is { } startBefore ? At(startBefore.Line, 0) : from);
            case CaretMove.ParagraphEnd:
                return offset < text.Length
                    ? At(line, text.Length)
                    : (Neighbor(spot, forward: true) is { } endAfter ? At(endAfter.Line, endAfter.Line.Projection.Text.Length) : from);
            case CaretMove.DocumentStart:
                return Edge(spot, first: true) is { } start ? At(start.Line, 0) : from;
            case CaretMove.DocumentEnd:
                return Edge(spot, first: false) is { } end ? At(end.Line, end.Line.Projection.Text.Length) : from;
            default:
                return from;
        }
    }

    private static TextPosition At(TextLine line, int offset) => new(line.Story, line.Paragraph, offset);

    // The line that shows the position, or null. An offset inside a line's range is on it; an offset at the end of the last line of the
    // paragraph is on that line; an offset where one line ends and the next begins belongs to the next one (the one after the wrap).
    private Spot? Locate(TextPosition position, int pageHint)
    {
        var count = pages.Count;
        Spot? weak = null;
        if ((uint)pageHint < (uint)count && Scan(pageHint, position, ref weak) is { } hinted)
        {
            return hinted;
        }

        if (map.PagesOf(position.Paragraph) is { } list)
        {
            foreach (var index in list)
            {
                if (index != pageHint && index < count && Scan(index, position, ref weak) is { } found)
                {
                    return found;
                }
            }
        }

        return weak;
    }

    private Spot? Scan(int pageIndex, TextPosition position, ref Spot? weak)
    {
        var lines = pages[pageIndex].TextLines;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!ReferenceEquals(line.Paragraph, position.Paragraph) || position.Offset < line.Start)
            {
                continue;
            }

            if (position.Offset < line.End || (position.Offset == line.End && line.End >= line.Projection.Text.Length))
            {
                return new Spot(pageIndex, i, line);
            }

            if (position.Offset == line.End)
            {
                weak ??= new Spot(pageIndex, i, line);
            }
        }

        return null;
    }

    // The next line in reading order (previous when not forward) that is in the same story and cell as the spot and passes accept; the
    // search goes to the end of the page and on through the next pages, except in a header or footer, which never leaves its page.
    private Spot? Neighbor(Spot from, bool forward, Func<TextLine, bool> accept)
    {
        var step = forward ? 1 : -1;
        var count = pages.Count;
        var pageIndex = from.PageIndex;
        var index = from.Index + step;
        var last = from.Line.Story.Kind == StoryKind.HeaderFooter ? 0 : PagesToSearch;
        for (var seen = 0; seen <= last; seen++)
        {
            var lines = pages[pageIndex].TextLines;
            for (; index >= 0 && index < lines.Length; index += step)
            {
                if (InScope(from.Line, lines[index]) && accept(lines[index]))
                {
                    return new Spot(pageIndex, index, lines[index]);
                }
            }

            pageIndex += step;
            if ((uint)pageIndex >= (uint)count)
            {
                return null;
            }

            index = forward ? 0 : pages[pageIndex].TextLines.Length - 1;
        }

        return null;
    }

    // The first line of the next paragraph (forward), or the last line of the one before (backward).
    private Spot? Neighbor(Spot from, bool forward) => Neighbor(from, forward, l => !ReferenceEquals(l.Paragraph, from.Line.Paragraph));

    // Same story, and out of a table cell never into another one (into a table from outside is fine).
    private static bool InScope(TextLine from, TextLine other) =>
        other.Story == from.Story && (from.Cell is null || ReferenceEquals(from.Cell, other.Cell));

    private TextPosition? Vertical(Spot spot, int offset, bool down, float? goalX)
    {
        var line = spot.Line;
        var x = goalX ?? line.Stops.XAt(offset);
        if (Neighbor(spot, down, _ => true) is not { } target)
        {
            // Nothing above or below in this story or cell: the start or the end of the line, as Word does.
            return At(line, down ? line.Stops.VisibleEnd : line.Start);
        }

        return At(target.Line, target.Line.Stops.OffsetAt(x, out _));
    }

    private TextPosition? PageStep(Spot spot, int offset, bool down, float? goalX)
    {
        var line = spot.Line;
        var x = goalX ?? line.Stops.XAt(offset);
        var pageIndex = spot.PageIndex + (down ? 1 : -1);
        if ((uint)pageIndex >= (uint)pages.Count || line.Story.Kind == StoryKind.HeaderFooter)
        {
            return Edge(spot, first: !down) is { } edge ? At(edge.Line, down ? edge.Line.Projection.Text.Length : 0) : At(line, offset);
        }

        TextLine? best = null;
        var bestDistance = float.MaxValue;
        foreach (var candidate in pages[pageIndex].TextLines)
        {
            if (!InScope(line, candidate))
            {
                continue;
            }

            var distance = Math.Abs(candidate.Top - line.Top);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best is null ? At(line, offset) : At(best, best.Stops.OffsetAt(x, out _));
    }

    // The first (or last) line of the spot's story and cell: of the body over every page laid out so far, of every other story on the spot's page.
    private Spot? Edge(Spot spot, bool first)
    {
        var count = pages.Count;
        var all = spot.Line.Story.Kind == StoryKind.Body;
        var from = all ? 0 : spot.PageIndex;
        var to = all ? count - 1 : spot.PageIndex;
        for (var p = first ? from : to; first ? p <= to : p >= from; p += first ? 1 : -1)
        {
            var lines = pages[p].TextLines;
            for (var i = first ? 0 : lines.Length - 1; first ? i < lines.Length : i >= 0; i += first ? 1 : -1)
            {
                if (InScope(spot.Line, lines[i]))
                {
                    return new Spot(p, i, lines[i]);
                }
            }
        }

        return null;
    }

    private static int Next(ParagraphProjection projection, int offset)
    {
        if (projection.AtomicAt(offset) is var (_, atomicEnd))
        {
            return atomicEnd;
        }

        var text = projection.Text;
        return Math.Min(text.Length, offset + Math.Max(1, StringInfo.GetNextTextElementLength(text.AsSpan(offset))));
    }

    private static int Previous(ParagraphProjection projection, int offset)
    {
        if (projection.AtomicAt(offset - 1) is var (atomicStart, _))
        {
            return atomicStart;
        }

        return BoundaryBefore(projection.Text, offset);
    }

    // The start of the text element that ends at offset. Elements are found from a window of 256 characters before the offset (one longer than that is not recognized as a whole).
    private static int BoundaryBefore(string text, int offset)
    {
        var start = Math.Max(0, offset - 256);
        while (start > 0 && char.IsLowSurrogate(text[start]))
        {
            start--;
        }

        var at = start;
        var previous = start;
        while (at < offset)
        {
            previous = at;
            at += Math.Max(1, StringInfo.GetNextTextElementLength(text.AsSpan(at)));
        }

        return previous;
    }

    private enum CharClass
    {
        Space,
        Word,
        Other,
    }

    // Word movement, the usual rule: a word is a run of letters, digits and underscores, a run of other characters (punctuation, symbols) is
    // a unit of its own, an atomic unit is one; moving right lands at the start of the next word (after the spaces), moving left at the
    // start of the word before the offset. Picture characters, tabs and line breaks count as spaces.
    private static CharClass ClassOf(string text, int at)
    {
        var rune = Rune.GetRuneAt(text, at);
        if (rune.Value == ParagraphProjection.ObjectChar || Rune.IsWhiteSpace(rune))
        {
            return CharClass.Space;
        }

        return Rune.IsLetterOrDigit(rune) || rune.Value == '_' ? CharClass.Word : CharClass.Other;
    }

    private static int WordEnd(ParagraphProjection projection, int offset)
    {
        var text = projection.Text;
        var at = offset;
        if (projection.AtomicAt(at) is var (_, atomicEnd))
        {
            at = atomicEnd;
        }
        else
        {
            var kind = ClassOf(text, at);
            if (kind != CharClass.Space)
            {
                while (at < text.Length && projection.AtomicAt(at) is null && ClassOf(text, at) == kind)
                {
                    at = Next(projection, at);
                }
            }
        }

        while (at < text.Length && projection.AtomicAt(at) is null && ClassOf(text, at) == CharClass.Space)
        {
            at = Next(projection, at);
        }

        return at;
    }

    private static int WordStart(ParagraphProjection projection, int offset)
    {
        var text = projection.Text;
        var at = offset;
        while (at > 0 && projection.AtomicAt(at - 1) is null && ClassOf(text, BoundaryBefore(text, at)) == CharClass.Space)
        {
            at = BoundaryBefore(text, at);
        }

        if (at == 0)
        {
            return 0;
        }

        if (projection.AtomicAt(at - 1) is var (atomicStart, _))
        {
            return atomicStart;
        }

        var kind = ClassOf(text, BoundaryBefore(text, at));
        while (at > 0 && projection.AtomicAt(at - 1) is null && ClassOf(text, BoundaryBefore(text, at)) == kind)
        {
            at = BoundaryBefore(text, at);
        }

        return at;
    }
}
