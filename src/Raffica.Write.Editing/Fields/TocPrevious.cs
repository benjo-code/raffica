using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// The entries a table of contents holds before a refresh writes them again, so that the tab stops Word or the user gave an entry stay with it. An entry is paired with the old paragraph that points at a bookmark of its heading, else with
/// the one of its style that says the same line, else (a list with as many paragraphs as entries) with the one in its place. A paired entry of the same level keeps the stops of that paragraph, except for the stop of its number when the
/// number changes or goes, and gets a stop after its number when the tab there would go to the right stop (<see cref="TocTabs.ForExisting"/>). An entry without such a paragraph, or one whose level changed, takes the other stops of an
/// entry of its style (else of the first entry that has stops) without the left ones, and a stop after its number as a new one does (<see cref="TocTabs.ForNew"/>). The style of an old paragraph is read by its name
/// (<see cref="TocTabs.EntryStyle"/>).
/// </summary>
internal sealed class TocPrevious
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly List<Old> _old;
    private readonly Dictionary<TocEntry, Old> _paired = new(ReferenceEqualityComparer.Instance);
    private readonly TocTabs _tabs;

    /// <param name="paragraphs">The paragraphs of the entries as they stand, read now.</param>
    /// <param name="entries">The entries about to be written, in order.</param>
    /// <param name="styleOf">The style the paragraph of an entry gets.</param>
    /// <param name="tabs">The rules of the stop after a number.</param>
    public TocPrevious(IReadOnlyList<Paragraph> paragraphs, IReadOnlyList<TocEntry> entries, Func<TocEntry, string> styleOf, TocTabs tabs)
    {
        _tabs = tabs;
        _old = [.. paragraphs.Select(p => Read(p, tabs.EntryStyle(p.Properties.StyleId)))];
        Pair(entries, styleOf);
    }

    /// <summary>The tab stops of the paragraph written for the entry (null: the sentence of an empty list, which takes the stops of the first entry that has some).</summary>
    public XElement? TabsOf(TocEntry? entry, Paragraph proposed)
    {
        var style = proposed.Properties.StyleId;
        var first = _old.Select(o => o.Tabs).FirstOrDefault(t => t is not null);
        if (entry is null)
        {
            return first is null ? null : new XElement(first);
        }

        if (_paired.TryGetValue(entry, out var old) && old.Style == style)
        {
            return _tabs.ForExisting(entry, old.Paragraph, old.Tabs, old.Number);
        }

        var shared = _old.Where(o => o.Style == style).Select(o => o.Tabs).FirstOrDefault(t => t is not null) ?? first;
        return _tabs.ForNew(entry, proposed, TocTabs.WithoutLeftStops(shared));
    }

    private void Pair(IReadOnlyList<TocEntry> entries, Func<TocEntry, string> styleOf)
    {
        var free = new HashSet<Old>(_old, ReferenceEqualityComparer.Instance);
        void Take(TocEntry entry, Old old)
        {
            _paired[entry] = old;
            free.Remove(old);
        }

        var byName = new Dictionary<string, Old>(StringComparer.OrdinalIgnoreCase);
        foreach (var old in _old)
        {
            foreach (var name in old.Names)
            {
                byName.TryAdd(name, old);
            }
        }

        if (byName.Count > 0)
        {
            foreach (var entry in entries)
            {
                var old = BookmarkOperations.InlinesOf(entry.Source.Inlines).OfType<BookmarkStart>().Select(b => byName.GetValueOrDefault(b.Name)).FirstOrDefault(o => o is not null && free.Contains(o));
                if (old is not null)
                {
                    Take(entry, old);
                }
            }
        }

        foreach (var entry in entries.Where(e => !_paired.ContainsKey(e)))
        {
            var style = styleOf(entry);
            if (_old.FirstOrDefault(o => free.Contains(o) && o.Style == style && Says(o, entry)) is { } old)
            {
                Take(entry, old);
            }
        }

        if (entries.Count != _old.Count)
        {
            return;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (!_paired.ContainsKey(entries[i]) && free.Contains(_old[i]) && _old[i].Style == styleOf(entries[i]))
            {
                Take(entries[i], _old[i]);
            }
        }
    }

    // The line of the entry, followed by nothing or by the one character that stands before the page number.
    private static bool Says(Old old, TocEntry entry) =>
        old.Line.Length - entry.Line.Length is 0 or 1 && old.Line.StartsWith(entry.Line, StringComparison.Ordinal);

    // The paragraph as it stands (a copy: the refresh rewrites the live one), the bookmarks it points at and what it says before its page reference.
    private static Old Read(Paragraph paragraph, string? style)
    {
        var names = new List<string>();
        var line = new StringBuilder();
        var page = false;
        foreach (var inline in BookmarkOperations.InlinesOf(paragraph.Inlines))
        {
            switch (inline)
            {
                case Hyperlink { Anchor: { } anchor }:
                    names.Add(anchor);
                    break;
                case SimpleField simple:
                    page |= FieldInstruction.Parse(simple.Instruction).Name == "PAGEREF";
                    break;
                case Run run when !page:
                    foreach (var content in run.Content)
                    {
                        if (content is InstructionText instruction && instruction.Text.TrimStart().StartsWith("PAGEREF", StringComparison.OrdinalIgnoreCase))
                        {
                            page = true;
                            break;
                        }

                        line.Append(content switch { TextContent t => t.Text, TabContent => "\t", _ => string.Empty });
                    }

                    break;
                default:
                    break;
            }
        }

        if (FieldRefresh.HasFieldMarkup(paragraph))
        {
            names.AddRange(FieldView.InParagraph(paragraph).Where(f => f.Instruction.Name == "PAGEREF").Select(f => f.Instruction.Argument).OfType<string>());
        }

        var copy = new Paragraph();
        foreach (var child in paragraph.Properties.Children)
        {
            copy.Properties.Set(child.Name, new XElement(child));
        }

        var tabs = copy.Properties.Get(W + "tabs");
        var said = line.ToString();
        return new Old(copy, style, names, said, NumberOf(said, page), tabs is null ? null : new XElement(tabs));
    }

    // The text before the first tab of the line, the character before a page reference left aside: the number of an entry that has a tab after one, else null.
    private static string? NumberOf(string line, bool page)
    {
        var title = page && line.Length > 0 ? line[..^1] : line;
        var tab = title.IndexOf('\t', StringComparison.Ordinal);
        return tab > 0 ? title[..tab] : null;
    }

    private sealed record Old(Paragraph Paragraph, string? Style, List<string> Names, string Line, string? Number, XElement? Tabs);
}
