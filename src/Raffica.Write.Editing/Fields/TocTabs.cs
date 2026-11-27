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

    private int[]? _levels;

    /// <summary>
    /// The tab stops of a new entry: <paramref name="tabs"/> (not changed) and, when the entry has a tab after its number and no stop of the paragraph takes the text behind the number (the next one is the right stop of the page number, or
    /// there is none), the stop after the number.
    /// </summary>
    public XElement? ForNew(TocEntry entry, Paragraph proposed, XElement? tabs)
    {
        var copy = tabs is null ? null : new XElement(tabs);
        return entry.HasLabelTab ? WithStop(entry, proposed, copy, next => next is null or { Right: true }) : copy;
    }

    /// <summary>
    /// The tab stops of an entry that stands in the list already, written as <paramref name="current"/> (its paragraph gives the indents) with the stops <paramref name="tabs"/> (not changed): they stay as they are, and only when the tab after
    /// the number would go to a right-aligned stop is the stop after the number added.
    /// </summary>
    public XElement? ForExisting(TocEntry entry, Paragraph current, XElement? tabs)
    {
        var copy = tabs is null ? null : new XElement(tabs);
        return entry.HasLabelTab ? WithStop(entry, current, copy, next => next is { Right: true }) : copy;
    }

    /// <summary>The tab stops without the left ones (what a stop after a number is): null when nothing is left.</summary>
    public static XElement? WithoutLeftStops(XElement? tabs)
    {
        if (tabs is null)
        {
            return null;
        }

        var copy = new XElement(tabs);
        copy.Elements(W + "tab").Where(t => (string?)t.Attribute(W + "val") is "left" or "start" or "num").Remove();
        return copy.HasElements ? copy : null;
    }

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

    private XElement? WithStop(TocEntry entry, Paragraph owner, XElement? tabs, Func<LineStop?, bool> needed)
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

        var line = host.MeasureLine(probe, entry.Label);
        var next = line.Stops.Where(s => s.Position > line.TextEnd).Select(s => (LineStop?)s).FirstOrDefault();
        if (!needed(next))
        {
            return tabs;
        }

        var behind = line.TextEnd + Room;
        var position = behind;
        foreach (var indent in Levels())
        {
            if (indent > behind)
            {
                position = indent;
                break;
            }
        }

        return Add(tabs ?? new XElement(W + "tabs"), position);
    }

    // The stop in the order of the positions, as Word writes them; a stop already at the position goes.
    private static XElement Add(XElement tabs, int position)
    {
        static int? PositionOf(XElement tab) => int.TryParse((string?)tab.Attribute(W + "pos"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
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
