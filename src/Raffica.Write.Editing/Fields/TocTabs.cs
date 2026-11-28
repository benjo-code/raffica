using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The left tab stop after the number of an entry of a table of contents, as Word writes it (checked in Word 16, <c>Docs/write-word-check.md</c>). Without one the tab after the number goes to the right-aligned stop of the page number
/// and the text is pushed to the right edge. Word takes the end of the number on the first line plus 12 points; the stop is the left indent of the first of the styles <c>toc 1</c> to <c>toc 9</c> (found by name), in that order, that
/// lies behind that place (a style the document does not have has Word's own indent, 11 points per level), else the place itself.
/// </summary>
internal sealed class TocTabs(FieldStyles styles, IFieldHost host)
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // The room Word leaves behind a number, in twips.
    private const int Room = 240;

    // The left indent of a built-in TOC style per level above the first, in twips.
    private const int BuiltInIndent = 220;

    // How far a stop may lie from Word's place for a number and still be Word's own for it, in twips: Word measures a text a little differently.
    private const int Tolerance = 20;

    private int[]? _levels;

    /// <summary>
    /// The tab stops of a new entry: <paramref name="tabs"/> (not changed) and, when the entry has a tab after its number and no stop of the paragraph takes the text behind the number (the next one is the right stop of the page number, or
    /// there is none), the stop after the number.
    /// </summary>
    public XElement? ForNew(TocEntry entry, Paragraph proposed, XElement? tabs)
    {
        var copy = tabs is null ? null : new XElement(tabs);
        return entry.HasLabelTab ? WithStop(entry, proposed, copy, (next, _) => next is null or { Right: true }) : copy;
    }

    /// <summary>
    /// The tab stops of an entry that stands in the list already, written as <paramref name="current"/> (its paragraph gives the indents and holds the stops <paramref name="tabs"/>, not changed); <paramref name="oldNumber"/> is the number
    /// that a tab followed in it, or null. The stops stay as they are, with these exceptions, each of which takes away a stop that would catch the tab before the page number of a short entry (or push the title out of the line):
    /// <list type="bullet">
    /// <item>A left stop of the paragraph behind a stop of its style (or of its hanging indent) that stands behind the number goes: the style's stop takes the tab after the number, and the other would only catch the tab before the page number.
    /// It stays when it stands at Word's place for the number, where Word writes a stop of its own too, but not where a hanging indent takes the number on a first line that starts at the margin: Word writes none there. So a stop left over
    /// from other letters or another number can stay when it stands at that place; over a style stop Word writes it there itself and draws the page number of a short entry at it.</item>
    /// <item>The left stop of the paragraph that took the tab after the old number is the number's own: when the new number differs and the tab after it would not go to the stop, or the stop stands where Word puts one for the old number and not
    /// where it puts one for the new, it goes. So does a left stop far to the right of Word's place, in the right half of the line, that leaves the title and the page number no room (one written for much larger letters). In its place the stop for the new number is written,
    /// unless a stop of the style or of the hanging indent takes the text then, or one of the paragraph at or before that place, or one that stood in front of the stop that went.</item>
    /// <item>When no tab follows a number any more, the stop that took the tab after it goes, with any left stop before it.</item>
    /// <item>When the tab after a number would go to a right-aligned stop (a list saved without the stop, a number or a font grown past it), the stop after the number is added.</item>
    /// <item>Whenever a stop is written, the left stops before the number's end go.</item>
    /// </list>
    /// Word's own update writes the stops anew and drops the user's too; here the user's are kept unless one of these rules takes them.
    /// </summary>
    public XElement? ForExisting(TocEntry entry, Paragraph current, XElement? tabs, string? oldNumber)
    {
        var copy = tabs is null ? null : new XElement(tabs);
        // Where the stop that goes stood, when the old number's moves or one far to the right does.
        int? moved = null;
        if (copy is not null && oldNumber is not null && !(entry.HasLabelTab && entry.Label == oldNumber))
        {
            // Without a number Word writes no stop at all, so none behind the style's stays either.
            var before = entry.HasLabelTab ? Measure(current, copy, oldNumber) : MeasureWithoutStaleStops(current, copy, oldNumber, keepsWords: false);
            var taken = Next(before);
            if (!entry.HasLabelTab)
            {
                var through = taken is { Right: false } stop ? stop.Position : before.TextEnd;
                RemoveLeftStops(copy, position => position <= through);
                return OrNone(copy);
            }

            if (taken is { Right: false } old && LeftStops(copy).Any(t => PositionOf(t) == old.Position))
            {
                var after = Measure(current, copy, entry.Label);
                // It moves when the new number's tab does not go to it, or when it was Word's stop for the old number and is not Word's stop for the new one.
                if (Next(after)?.Position != old.Position || Math.Abs(old.Position - PlaceBehind(before)) <= Tolerance && !IsWordsPlace(old.Position, after))
                {
                    moved = old.Position;
                    RemoveLeftStops(copy, position => position == old.Position);
                }
            }
        }

        if (!entry.HasLabelTab)
        {
            return OrNone(copy);
        }

        var line = copy is null ? Measure(current, null, entry.Label) : MeasureWithoutStaleStops(current, copy, entry.Label, keepsWords: true);
        var next = Next(line);
        var place = PlaceBehind(line);
        // A stop of the paragraph's own far to the right of Word's place, as one written for much larger letters is, goes like the stop of an old number when the title and the page number no longer fit behind it.
        while (copy is not null && next is { Right: false } far && far.Position > place + Tolerance && LeftStops(copy).Any(t => PositionOf(t) == far.Position) && LeavesNoRoom(entry, current, copy, line, far.Position))
        {
            moved = far.Position;
            RemoveLeftStops(copy, position => position == far.Position);
            line = Measure(current, copy, entry.Label);
            next = Next(line);
        }

        // A tab that would go to the right stop needs a stop. A stop that moves is also written when there is none at all, and in front of one of the paragraph's own that stood behind it; never in front of one of the style, which serves, nor of one that stood in front of it and takes the new number now.
        var needed = next is { Right: true }
            || moved is { } from && (next is not { } waiting || waiting.Position > place && waiting.Position > from && copy is not null && LeftStops(copy).Any(t => PositionOf(t) == waiting.Position));
        if (!needed)
        {
            return OrNone(copy);
        }

        // No stop takes the tab after the number. A left one before the number's end serves nothing now, and would take the tab before the page number once the number is shorter again.
        if (copy is not null)
        {
            RemoveLeftStops(copy, position => position <= line.TextEnd);
        }

        return Add(copy ?? new XElement(W + "tabs"), place);
    }

    // Whether the stop lies in the right half in front of the right stop of the page number and the title and a number of three digits do not fit between the two.
    private bool LeavesNoRoom(TocEntry entry, Paragraph owner, XElement tabs, LineGeometry line, int stop)
    {
        if (line.Stops.Where(s => s.Right && s.Position > stop).Select(s => (int?)s.Position).FirstOrDefault() is not { } edge || stop <= edge / 2)
        {
            return false;
        }

        return stop + Measure(owner, tabs, entry.Text + " 000").TextEnd - Measure(owner, tabs, string.Empty).TextEnd >= edge;
    }

    // Takes from the paragraph's stops the left ones that stand behind a stop of the style or of the hanging indent which is itself behind the number, as far as a right stop follows to take the page number: the style's stop takes the tab
    // after the number, and one of the paragraph's behind it catches the tab before the page number of a short entry. Word writes its stop for a number also behind such a stop, so with <paramref name="keepsWords"/> one that stands at Word's
    // place for this number stays, except where a hanging indent takes the number on a first line that starts at the margin (or in front of it): Word writes none there. One in front of the style's stop takes the number's tab and stays.
    // Answers how the number lies with the stops that remain.
    private LineGeometry MeasureWithoutStaleStops(Paragraph owner, XElement tabs, string number, bool keepsWords)
    {
        var line = Measure(owner, tabs, number);
        var behind = line.Stops.Where(s => s.Position > line.TextEnd).ToList();
        var right = behind.FindIndex(s => s.Right);
        if (right < 0)
        {
            return line;
        }

        var own = tabs.Elements(W + "tab").Where(t => (string?)t.Attribute(W + "val") != "clear").Select(PositionOf).ToHashSet();
        if (behind.Take(right).Where(s => !own.Contains(s.Position)).Select(s => (int?)s.Position).FirstOrDefault() is not { } style)
        {
            return line;
        }

        var end = behind[right].Position;
        // Only a hanging indent can stand behind a number whose line starts at the margin or in front of it.
        var takenAtMargin = line.FirstLineStart <= 0 && line.LeftIndent > line.TextEnd;
        var surplus = LeftStops(tabs).Where(t => PositionOf(t) is { } position && position > style && position < end && !(keepsWords && !takenAtMargin && IsWordsPlace(position, line))).ToList();
        if (surplus.Count == 0)
        {
            return line;
        }

        surplus.Remove();
        return Measure(owner, tabs, number);
    }

    // Whether Word's stop for a number that ends as the line does could stand there: Word measures a text a little differently, so the end is taken a few twips shorter and longer too.
    private bool IsWordsPlace(int position, LineGeometry number) =>
        new[] { -Tolerance, 0, Tolerance }.Any(by => Math.Abs(position - PlaceBehind(number with { TextEnd = number.TextEnd + by })) <= Tolerance);

    // A w:tabs element without a stop is not one.
    private static XElement? OrNone(XElement? tabs) => tabs is { HasElements: true } ? tabs : null;

    /// <summary>The tab stops without the left ones (what a stop after a number is): null when nothing is left.</summary>
    public static XElement? WithoutLeftStops(XElement? tabs)
    {
        if (tabs is null)
        {
            return null;
        }

        var copy = new XElement(tabs);
        LeftStops(copy).Remove();
        return copy.HasElements ? copy : null;
    }

    private static IEnumerable<XElement> LeftStops(XElement tabs) => tabs.Elements(W + "tab").Where(t => (string?)t.Attribute(W + "val") is "left" or "start" or "num");

    private static void RemoveLeftStops(XElement tabs, Func<int, bool> at) => LeftStops(tabs).Where(t => PositionOf(t) is { } position && at(position)).Remove();

    private static int? PositionOf(XElement tab) => int.TryParse((string?)tab.Attribute(W + "pos"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>
    /// The style id Write gives an entry for the style that has Word's built-in name of one: a second style of such a name (Word reads it as the first) holds the same entries. Any other id is answered as it is.
    /// </summary>
    public string? EntryStyle(string? id)
    {
        if (id is null || styles.Find(id)?.Name is not { } name)
        {
            return id;
        }

        for (var level = 0; level <= 9; level++)
        {
            // Level 0 stands for the list of figures.
            var figures = level == 0;
            if (string.Equals(name, FieldStyles.EntryName(level, figures), StringComparison.OrdinalIgnoreCase))
            {
                return styles.ExistingEntryStyleId(level, figures) ?? id;
            }
        }

        return id;
    }

    // The tabs with Word's stop behind the number of the entry added, when the predicate says so for the stop the tab would go to now and the place of the new one.
    private XElement? WithStop(TocEntry entry, Paragraph owner, XElement? tabs, Func<LineStop?, int, bool> needed)
    {
        var line = Measure(owner, tabs, entry.Label);
        var place = PlaceBehind(line);
        return needed(Next(line), place) ? Add(tabs ?? new XElement(W + "tabs"), place) : tabs;
    }

    // The first stop behind the end of the text: where a tab there goes.
    private static LineStop? Next(LineGeometry line) => line.Stops.Where(s => s.Position > line.TextEnd).Select(s => (LineStop?)s).FirstOrDefault();

    // How the text lies at the start of a paragraph with the indents of the owner and the tab stops given.
    private LineGeometry Measure(Paragraph owner, XElement? tabs, string text)
    {
        var probe = new Paragraph();
        foreach (var child in owner.Properties.Children)
        {
            probe.Properties.Set(child.Name, new XElement(child));
        }

        if (tabs is null)
        {
            probe.Properties.Remove(W + "tabs");
        }
        else
        {
            probe.Properties.Set(W + "tabs", new XElement(tabs));
        }

        return host.MeasureLine(probe, text);
    }

    // Where Word puts the stop behind a number that ends as the line does.
    private int PlaceBehind(LineGeometry number)
    {
        var behind = number.TextEnd + Room;
        foreach (var indent in Levels())
        {
            if (indent > behind)
            {
                return indent;
            }
        }

        return behind;
    }

    // The stop in the order of the positions, as Word writes them; a stop already at the position goes.
    private static XElement Add(XElement tabs, int position)
    {
        tabs.Elements(W + "tab").Where(t => PositionOf(t) == position).Remove();
        var stop = new XElement(W + "tab", new XAttribute(W + "val", "left"), new XAttribute(W + "pos", position.ToString(CultureInfo.InvariantCulture)));
        if (tabs.Elements(W + "tab").FirstOrDefault(t => PositionOf(t) > position) is { } behind)
        {
            behind.AddBeforeSelf(stop);
        }
        else
        {
            tabs.Add(stop);
        }

        return tabs;
    }

    // The left indents of toc 1 to toc 9: of the style of that name, else Word's own.
    private int[] Levels()
    {
        if (_levels is not null)
        {
            return _levels;
        }

        _levels = new int[9];
        for (var level = 1; level <= 9; level++)
        {
            if (styles.ExistingEntryStyleId(level, false) is not { } id)
            {
                _levels[level - 1] = (level - 1) * BuiltInIndent;
                continue;
            }

            var probe = new Paragraph();
            probe.Properties.StyleId = id;
            _levels[level - 1] = host.MeasureLine(probe, string.Empty).LeftIndent;
        }

        return _levels;
    }
}
