using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The left tab stop after the number of an entry of a table of contents, as Word writes it (checked in Word 16, <c>Docs/write-word-check.md</c>). Without one the tab after the number goes to the right-aligned stop of the page number
/// and the text is pushed to the right edge. Word takes the end of the number on the first line plus 12 points; the stop is the left indent of the first of the styles <c>TOC1</c> to <c>TOC9</c>, in that order, that lies behind that
/// place (a style the document does not have has Word's own indent, 11 points per level), else the place itself.
/// </summary>
internal sealed class TocTabs(WordDocument document, IFieldHost host)
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
    /// that a tab followed in it, or null. The stops stay as they are, with these exceptions. The left stop of the paragraph that took the tab after the old number is the number's own: when the new number differs and the stop stands where Word
    /// puts one for the old number, or the tab after the new number would not go to it, it moves to Word's place for the new number (none is added when a stop at or before that place takes the text). When no tab follows a number any more, that
    /// stop goes, with any left stop before it. Last, when the tab after the number would go to a right-aligned stop (a list saved without the stop, a number or a font grown past it), the stop after the number is added. Whenever a stop is
    /// written, the left stops before the number's end go. A stop that stayed behind in one of these cases would take the tab before the page number of a short entry. Word's own update writes the stops anew and drops the user's too; those are kept here.
    /// </summary>
    public XElement? ForExisting(TocEntry entry, Paragraph current, XElement? tabs, string? oldNumber)
    {
        var copy = tabs is null ? null : new XElement(tabs);
        var moves = false;
        if (copy is not null && oldNumber is not null && !(entry.HasLabelTab && entry.Label == oldNumber))
        {
            var before = Measure(current, copy, oldNumber);
            var taken = Next(before);
            if (!entry.HasLabelTab)
            {
                var through = taken is { Right: false } stop ? stop.Position : before.TextEnd;
                RemoveLeftStops(copy, position => position <= through);
                return OrNone(copy);
            }

            moves = taken is { Right: false } old && LeftStops(copy).Any(t => PositionOf(t) == old.Position)
                && (Math.Abs(old.Position - PlaceBehind(before)) <= Tolerance || Next(Measure(current, copy, entry.Label))?.Position != old.Position);
            if (moves)
            {
                RemoveLeftStops(copy, position => position == taken!.Value.Position);
            }
        }

        if (!entry.HasLabelTab)
        {
            return copy;
        }

        var line = Measure(current, copy, entry.Label);
        var next = Next(line);
        var place = PlaceBehind(line);
        // A stop that moves is written unless one at or before its place takes the tab; else only a tab that would go to the right stop needs one.
        var needed = moves ? next is null or { Right: true } || next.Value.Position > place : next is { Right: true };
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
    /// The style id Write gives an entry for the style with Word's built-in name of one: a document of another language calls the style <c>toc 1</c> by another id (<c>Verzeichnis1</c>), and its entries are the same entries.
    /// Any other id is answered as it is.
    /// </summary>
    public string? EntryStyle(string? id)
    {
        if (id is null || document.Styles?.Find(id)?.Name is not { } name)
        {
            return id;
        }

        for (var level = 1; level <= 9; level++)
        {
            if (string.Equals(name, LevelName(level), StringComparison.OrdinalIgnoreCase))
            {
                return TocBuilder.StyleIdFor(level, false);
            }
        }

        return string.Equals(name, "table of figures", StringComparison.OrdinalIgnoreCase) ? TocBuilder.FigureStyleId : id;
    }

    private static string LevelName(int level) => "toc " + level.ToString(CultureInfo.InvariantCulture);

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

    // The left indents of TOC1 to TOC9: of the style with that id, or with Word's name for it (a document of another language has another id), else Word's own.
    private int[] Levels()
    {
        if (_levels is not null)
        {
            return _levels;
        }

        _levels = new int[9];
        for (var level = 1; level <= 9; level++)
        {
            var id = TocBuilder.StyleIdFor(level, false);
            var name = LevelName(level);
            var style = document.Styles?.Find(id) ?? document.Styles?.Styles.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (style is null)
            {
                _levels[level - 1] = (level - 1) * BuiltInIndent;
                continue;
            }

            var probe = new Paragraph();
            probe.Properties.StyleId = style.Id;
            _levels[level - 1] = host.MeasureLine(probe, string.Empty).LeftIndent;
        }

        return _levels;
    }
}
