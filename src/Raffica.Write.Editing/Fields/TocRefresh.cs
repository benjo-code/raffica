using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>A table of contents of a list of blocks: its first and last paragraph (equal for one that stands in a single paragraph) and its instruction.</summary>
internal sealed record TocSite(IList<Block> Container, int First, int Last, string Instruction);

/// <summary>
/// What the table of contents pass of a refresh did. <paramref name="Changes"/> are the block changes in the order a layout takes them (the changes of the lists first, then the headings that got a bookmark);
/// <paramref name="Structural"/> are the ones that changed the number of paragraphs of a list (also in <paramref name="Changes"/>), <paramref name="Deferred"/> is true when such a change was left undone
/// because the history could not carry it yet (a redo is pending).
/// </summary>
internal sealed record TocPass(List<BlockChange> Changes, List<BlockChange> Structural, int NotUpdated, int Written, bool Deferred);

/// <summary>
/// The table of contents pass of the field refresh: it finds every <c>TOC</c> field of the body (tables and block content controls entered), reads its entries from the headings (or captions) and writes them. The instruction is never
/// touched: the paragraphs before and including the separate and the end mark stay as they are, only the entries between them are made again. An entry that already says the right thing is left as it is. When the number of paragraphs
/// stays the same the paragraphs are changed in place (a content change, never recorded); when it changes the paragraphs are replaced (a structural change, which the caller records on the newest history step) unless
/// <c>allowStructural</c> is false, when the table of contents is left as it is and the pass says so. Every heading an entry points at gets a hidden <c>_Toc</c> bookmark (an existing one is reused).
/// </summary>
internal sealed class TocRefresh(WordDocument document, IFieldHost host, bool allowStructural)
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly List<BlockChange> _changes = [];
    private readonly List<BlockChange> _structural = [];
    private readonly Dictionary<IList<Block>, int> _shifts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, List<TocEntry>> _entries = [];
    private readonly TocBookmarks _bookmarks = new(document);

    /// <summary>The changes made so far (what a failure part-way has to tell the owner).</summary>
    public IReadOnlyList<BlockChange> Changes => _changes;

    /// <summary>The structural changes made so far.</summary>
    public IReadOnlyList<BlockChange> Structural => _structural;

    public TocPass Run()
    {
        var sites = Find(document.Body.Blocks);
        if (sites.Count == 0)
        {
            return new TocPass([], [], 0, 0, false);
        }

        var excluded = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        foreach (var site in sites)
        {
            for (var i = site.First; i <= site.Last; i++)
            {
                if (site.Container[i] is Paragraph paragraph)
                {
                    excluded.Add(paragraph);
                }
            }
        }

        var notUpdated = 0;
        var written = 0;
        var deferred = false;
        foreach (var found in sites)
        {
            // The indices of the search are those before any change; the tables of contents before this one in the same list may have gained or lost paragraphs since.
            var shift = _shifts.GetValueOrDefault(found.Container);
            var site = found with { First = found.First + shift, Last = found.Last + shift };
            var spec = TocSpec.TryParse(site.Instruction);
            var shape = spec is null ? null : Analyze(site);
            if (spec is null || shape is null)
            {
                notUpdated++;
                continue;
            }

            var first = site.First;
            var last = site.Last;
            var areaLast = shape.TailParagraph ? last - 1 : last;
            var oldCount = areaLast - first + 1;
            // A paragraph of the entries that ends a section would lose its break when the paragraphs are replaced: such a list keeps its bytes and is counted (the paragraph with the end mark that stands alone is not rewritten).
            if (Enumerable.Range(first, areaLast - first + 1).Any(i => site.Container[i] is Paragraph { SectionBreak: not null }))
            {
                notUpdated++;
                continue;
            }

            var entries = EntriesOf(spec, excluded);
            var newCount = Math.Max(1, entries.Count);
            if (newCount != oldCount && !allowStructural)
            {
                deferred = true;
                continue;
            }

            if (Rewrite(site.Container, first, last, areaLast, shape, spec, entries))
            {
                written++;
            }
        }

        foreach (var (paragraph, before) in _bookmarks.Changed)
        {
            if (TextEditor.FindIn(document.Body.Blocks, paragraph) is { } place)
            {
                _changes.Add(new BlockChange(StoryRef.Body, place.Container, place.Index, 1, 1, [before]));
            }
        }

        return new TocPass(_changes, _structural, notUpdated, written, deferred);
    }

    private List<TocEntry> EntriesOf(TocSpec spec, HashSet<Paragraph> excluded)
    {
        var key = string.Join('|', spec.FirstLevel, spec.LastLevel, spec.Outline, spec.Label?.ToUpperInvariant());
        if (!_entries.TryGetValue(key, out var entries))
        {
            entries = TocBuilder.Collect(document, host, spec, excluded);
            _entries[key] = entries;
        }

        return entries;
    }

    // Writes the entries of one table of contents into the paragraphs first..areaLast (the paragraph with the end mark is not one of them when it stands alone). True when anything changed.
    private bool Rewrite(IList<Block> container, int first, int last, int areaLast, TocShape shape, TocSpec spec, List<TocEntry> entries)
    {
        var old = new List<Paragraph>();
        for (var i = first; i <= areaLast; i++)
        {
            old.Add((Paragraph)container[i]);
        }

        var head = old[0].Inlines.Take(shape.Separate + 1).Select(i => i.Clone()).ToList();
        var tailSource = shape.TailParagraph ? null : (Paragraph)container[last];
        var tail = tailSource?.Inlines.Skip(shape.End).Select(i => i.Clone()).ToList() ?? [];
        var pages = StoredPages(old);
        var tabs = TabsFor(old);
        var proposed = TocBuilder.Entries(
            document,
            spec,
            entries,
            entry => _bookmarks.NameFor(entry.Source),
            (entry, bookmark) => host.PageOf(new TextPosition(StoryRef.Body, entry.Source, 0))?.PageText ?? (bookmark is not null && pages.TryGetValue(bookmark, out var stored) ? stored : "?"),
            tabs);
        for (var i = head.Count - 1; i >= 0; i--)
        {
            proposed[0].Inlines.Insert(0, head[i]);
        }

        // One paragraph that holds the whole field would be a field result like any other (a caret next to it types into it, a refresh could not tell it from text around it): a list made here always spans two paragraphs, and when
        // there is one line only the end mark gets a paragraph of its own, as Word writes it.
        var endOwn = proposed.Count == 1 && !shape.TailParagraph;
        var endParagraph = endOwn ? new Paragraph() : null;
        foreach (var inline in tail)
        {
            (endParagraph ?? proposed[^1]).Inlines.Add(inline);
        }

        if (endParagraph is not null)
        {
            proposed.Add(endParagraph);
        }

        if (proposed.Count == old.Count)
        {
            var changed = false;
            for (var i = 0; i < old.Count; i++)
            {
                if (TocBuilder.Signature(old[i]) == TocBuilder.Signature(proposed[i]))
                {
                    continue;
                }

                var before = old[i].Clone();
                Assign(old[i], proposed[i]);
                var change = new BlockChange(StoryRef.Body, container, first + i, 1, 1, [before]);
                _changes.Add(change);
                changed = true;
            }

            return changed;
        }

        // The number of paragraphs changes: the first and the last paragraph objects stay (a caret rests only at the edges of the field), the others are replaced.
        var clones = old.Select(p => (Block)p.Clone()).ToList();
        var reserveLast = old.Count >= 2 && proposed.Count >= 2;
        var finals = new List<Paragraph>(proposed.Count);
        for (var k = 0; k < proposed.Count; k++)
        {
            Paragraph reuse;
            if (reserveLast && k == proposed.Count - 1)
            {
                reuse = old[^1];
            }
            else if (k < (reserveLast ? old.Count - 1 : old.Count))
            {
                reuse = old[k];
            }
            else
            {
                finals.Add(proposed[k]);
                continue;
            }

            Assign(reuse, proposed[k]);
            finals.Add(reuse);
        }

        for (var i = 0; i < old.Count; i++)
        {
            container.RemoveAt(first);
        }

        for (var k = 0; k < finals.Count; k++)
        {
            container.Insert(first + k, finals[k]);
        }

        var structural = new BlockChange(StoryRef.Body, container, first, old.Count, finals.Count, clones);
        _changes.Add(structural);
        _structural.Add(structural);
        _shifts[container] = _shifts.GetValueOrDefault(container) + finals.Count - old.Count;
        return true;
    }

    // Puts what the proposed paragraph says into the live one: the inlines and the properties; its section break and its unknown attributes stay.
    private static void Assign(Paragraph target, Paragraph source)
    {
        target.Inlines.Clear();
        foreach (var inline in source.Inlines.ToList())
        {
            target.Inlines.Add(inline);
        }

        foreach (var name in target.Properties.Children.Select(c => c.Name).Distinct().ToList())
        {
            target.Properties.Remove(name);
        }

        foreach (var child in source.Properties.Children.ToList())
        {
            target.Properties.Set(child.Name, new XElement(child));
        }
    }

    // The tab stops of the entries: the ones the list already has in its paragraphs (the first paragraph that has any; a new entry copies that situation), else none. Right alignment is the insert's job (the tab stops
    // of the paragraphs it writes, or the TOC1 to TOC9 styles of a file); a refresh never invents a tab stop, so a list the user made left-aligned stays so and a foreign list keeps the look its file gave it.
    private static XElement? TabsFor(List<Paragraph> old)
    {
        foreach (var paragraph in old)
        {
            if (paragraph.Properties.Get(W + "tabs") is { } own)
            {
                return own;
            }
        }

        return null;
    }

    // The page text stored in every PAGEREF field of the old paragraphs, by the bookmark the field names.
    private static Dictionary<string, string> StoredPages(List<Paragraph> old)
    {
        var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in old)
        {
            if (!FieldRefresh.HasFieldMarkup(paragraph))
            {
                continue;
            }

            foreach (var info in FieldScanner.Scan(paragraph.Inlines, intoContentControls: true))
            {
                var instruction = FieldInstruction.Parse(info.Instruction);
                if (instruction.Name != "PAGEREF" || instruction.Argument is not { } name || !info.IsComplete || info.ResultContainer is not { } list || info.SeparateRun is null || info.EndRun is null)
                {
                    continue;
                }

                var from = list.IndexOf(info.SeparateRun);
                var to = list.IndexOf(info.EndRun);
                if (from < 0 || to <= from)
                {
                    continue;
                }

                var text = new System.Text.StringBuilder();
                foreach (var inline in list.Skip(from + 1).Take(to - from - 1).OfType<Run>())
                {
                    foreach (var content in inline.Content.OfType<TextContent>())
                    {
                        text.Append(content.Text);
                    }
                }

                pages.TryAdd(name, text.ToString());
            }
        }

        return pages;
    }

    // ---- finding the tables of contents ------------------------------------------------------------------------------------------------------

    /// <summary>The tables of contents of the lists of blocks, in document order within a list (a list before the lists it holds).</summary>
    internal static List<TocSite> Find(IList<Block> blocks)
    {
        var sites = new List<TocSite>();
        Collect(blocks, sites, 0);
        return sites;
    }

    private static void Collect(IList<Block> container, List<TocSite> sites, int depth)
    {
        var covered = new HashSet<int>();
        var here = new List<TocSite>();
        if (TocRanges.In(container) is { } ranges)
        {
            foreach (var range in ranges)
            {
                here.Add(new TocSite(container, range.First, range.Last, range.Instruction));
                for (var i = range.First; i <= range.Last; i++)
                {
                    covered.Add(i);
                }
            }
        }

        for (var i = 0; i < container.Count; i++)
        {
            if (covered.Contains(i) || container[i] is not Paragraph paragraph || !FieldRefresh.HasFieldMarkup(paragraph))
            {
                continue;
            }

            foreach (var span in FieldView.InParagraph(paragraph))
            {
                if (span.Parent is null && span.Support.Kind == FieldKind.Toc && span.IsComplete)
                {
                    here.Add(new TocSite(container, i, i, span.Info!.Instruction));
                    break;
                }
            }
        }

        sites.AddRange(here.OrderBy(s => s.First));
        if (depth >= BookmarkOperations.MaxDepth)
        {
            return;
        }

        foreach (var block in container)
        {
            switch (block)
            {
                case ContentControlBlock control:
                    Collect(control.Content, sites, depth + 1);
                    break;
                case Table table:
                    foreach (var cell in table.Rows.OfType<TableRow>().SelectMany(r => r.Items.OfType<TableCell>()))
                    {
                        Collect(cell.Blocks, sites, depth + 1);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    // ---- the shape of a table of contents ----------------------------------------------------------------------------------------------------

    /// <summary>Where the marks of the field stand: the index of the separate run in the first paragraph, of the end run in its paragraph, and whether the end is a paragraph of its own (Word's shape).</summary>
    internal sealed record TocShape(int Separate, int End, bool TailParagraph);

    /// <summary>
    /// The shape of the field, or null when a refresh cannot rewrite its entries: the begin, the separate or the end is not a run of its own that is a direct child of its paragraph, or a field that stands in one paragraph
    /// shares it with other content. The marks are the ones <see cref="TocRanges"/> and <see cref="FieldScanner"/> read.
    /// </summary>
    internal static TocShape? Analyze(TocSite site)
    {
        if (site.Container[site.First] is not Paragraph first || site.Container[site.Last] is not Paragraph last)
        {
            return null;
        }

        var begin = -1;
        var separate = -1;
        var depth = 0;
        var firstEnd = -1;
        for (var i = 0; i < first.Inlines.Count && firstEnd < 0; i++)
        {
            if (first.Inlines[i] is not Run run)
            {
                continue;
            }

            foreach (var content in run.Content)
            {
                if (content is not FieldChar mark)
                {
                    continue;
                }

                if (run.Content.Count != 1)
                {
                    return null;
                }

                switch (mark.Kind)
                {
                    case FieldCharKind.Begin:
                        if (depth == 0 && begin < 0)
                        {
                            begin = i;
                        }

                        depth++;
                        break;
                    case FieldCharKind.Separate:
                        if (depth == 1 && begin >= 0 && separate < 0)
                        {
                            separate = i;
                        }

                        break;
                    default:
                        depth--;
                        if (depth == 0 && site.First == site.Last)
                        {
                            firstEnd = i;
                        }

                        break;
                }
            }
        }

        if (begin < 0 || separate < 0)
        {
            return null;
        }

        if (site.First == site.Last)
        {
            // A table of contents in one paragraph is the whole paragraph: nothing but bookmark marks outside the field.
            return firstEnd > separate && Marks(first.Inlines.Take(begin)) && Marks(first.Inlines.Skip(firstEnd + 1)) ? new TocShape(separate, firstEnd, false) : null;
        }

        var end = -1;
        depth = 1;
        for (var i = 0; i < last.Inlines.Count && end < 0; i++)
        {
            if (last.Inlines[i] is not Run run)
            {
                continue;
            }

            foreach (var content in run.Content)
            {
                if (content is not FieldChar mark)
                {
                    continue;
                }

                if (run.Content.Count != 1)
                {
                    return null;
                }

                depth += mark.Kind == FieldCharKind.Begin ? 1 : mark.Kind == FieldCharKind.End ? -1 : 0;
                if (depth == 0)
                {
                    end = i;
                }
            }
        }

        if (end < 0)
        {
            return null;
        }

        return new TocShape(separate, end, Marks(last.Inlines.Take(end)));
    }

    // True when the inlines hold no content: bookmark markers and runs with nothing in them only.
    private static bool Marks(IEnumerable<Inline> inlines) =>
        inlines.All(i => i is BookmarkStart or BookmarkEnd || (i is Run run && run.Content.Count == 0));

    /// <summary>
    /// Removes the hidden <c>_Toc</c> bookmarks of the body that nothing points at (no link anchor, no field argument): both markers go. Returns the paragraphs it changed, each with a clone from before.
    /// </summary>
    internal static List<(Paragraph Paragraph, Paragraph Before)> RemoveOrphans(WordDocument document)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var starts = new List<(Paragraph Paragraph, BookmarkStart Start)>();
        var ends = new Dictionary<int, List<(Paragraph Paragraph, BookmarkEnd End)>>();
        foreach (var paragraph in BookmarkOperations.ParagraphsOf(document.Body.Blocks))
        {
            foreach (var inline in BookmarkOperations.InlinesOf(paragraph.Inlines))
            {
                switch (inline)
                {
                    case Hyperlink { Anchor: { } anchor }:
                        referenced.Add(anchor);
                        break;
                    case BookmarkStart start when IsTocName(start.Name):
                        starts.Add((paragraph, start));
                        break;
                    case BookmarkEnd end:
                        if (!ends.TryGetValue(end.Id, out var list))
                        {
                            ends[end.Id] = list = [];
                        }

                        list.Add((paragraph, end));
                        break;
                    default:
                        break;
                }
            }

            if (FieldRefresh.HasFieldMarkup(paragraph))
            {
                foreach (var span in FieldView.InParagraph(paragraph))
                {
                    if (span.Instruction.Argument is { } name)
                    {
                        referenced.Add(name);
                    }
                }
            }
        }

        var changed = new List<(Paragraph Paragraph, Paragraph Before)>();
        void Note(Paragraph paragraph)
        {
            if (!changed.Any(c => ReferenceEquals(c.Paragraph, paragraph)))
            {
                changed.Add((paragraph, paragraph.Clone()));
            }
        }

        foreach (var (paragraph, start) in starts)
        {
            if (referenced.Contains(start.Name))
            {
                continue;
            }

            Note(paragraph);
            BookmarkOperations.Remove(paragraph.Inlines, start);
            if (ends.TryGetValue(start.Id, out var markers))
            {
                foreach (var (endParagraph, end) in markers)
                {
                    Note(endParagraph);
                    BookmarkOperations.Remove(endParagraph.Inlines, end);
                }
            }
        }

        return changed;
    }
    // ---- the hidden bookmarks of the headings -------------------------------------------------------------------------------------------------

    /// <summary>The name of a hidden table of contents bookmark: <c>_Toc</c> and digits.</summary>
    internal static bool IsTocName(string name)
    {
        const string prefix = "_Toc";
        if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = prefix.Length; i < name.Length; i++)
        {
            if (name[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The hidden bookmarks a refresh makes on headings (and captions): the bookmark of a paragraph is the first <c>_Toc</c> bookmark that starts and ends in it, else a new one over the whole paragraph, named <c>_Toc</c> and nine digits
    /// that no bookmark of the document uses. The paragraphs it changed are kept with a clone from before.
    /// </summary>
    internal sealed class TocBookmarks(WordDocument document)
    {
        private readonly Dictionary<Paragraph, string?> _names = new(ReferenceEqualityComparer.Instance);
        private HashSet<string>? _used;
        private long _number;
        private int? _nextId;
        private bool _idsKnown;

        /// <summary>The paragraphs that got a bookmark, each with a clone from before.</summary>
        public List<(Paragraph Paragraph, Paragraph Before)> Changed { get; } = [];

        /// <summary>The name of the bookmark of the paragraph (made now when it has none), or null when none can be made.</summary>
        public string? NameFor(Paragraph paragraph)
        {
            if (_names.TryGetValue(paragraph, out var known))
            {
                return known;
            }

            var name = Existing(paragraph) ?? Make(paragraph);
            _names[paragraph] = name;
            return name;
        }

        private static string? Existing(Paragraph paragraph)
        {
            var inlines = BookmarkOperations.InlinesOf(paragraph.Inlines).ToList();
            foreach (var start in inlines.OfType<BookmarkStart>())
            {
                if (IsTocName(start.Name) && inlines.OfType<BookmarkEnd>().Any(e => e.Id == start.Id))
                {
                    return start.Name;
                }
            }

            return null;
        }

        private string? Make(Paragraph paragraph)
        {
            if (!_idsKnown)
            {
                _idsKnown = true;
                _nextId = BookmarkOperations.NextId(document);
                _used = new HashSet<string>(BookmarkOperations.AllNames(document), StringComparer.OrdinalIgnoreCase);
                _number = 100_000_000;
                foreach (var used in _used)
                {
                    if (IsTocName(used) && used.Length <= 4 + 9 && long.TryParse(used.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                    {
                        _number = Math.Max(_number, value);
                    }
                }
            }

            if (_nextId is not { } id || _number >= 999_999_999)
            {
                return null;
            }

            string name;
            do
            {
                _number++;
                name = "_Toc" + _number.ToString(CultureInfo.InvariantCulture);
            }
            while (_used!.Contains(name) && _number < 999_999_999);
            if (_used.Contains(name))
            {
                return null;
            }

            _used.Add(name);
            _nextId = id == int.MaxValue ? null : id + 1;
            Changed.Add((paragraph, paragraph.Clone()));
            paragraph.Inlines.Insert(0, new BookmarkStart(id, name));
            paragraph.Inlines.Add(new BookmarkEnd(id));
            return name;
        }
    }
}
