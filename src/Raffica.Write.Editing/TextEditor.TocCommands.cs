using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>The reason the last <see cref="InsertToc"/> or <see cref="RemoveToc"/> returned null, or null when it succeeded. Set by every call.</summary>
    public TocRefusal? LastTocRefusal { get; private set; }

    /// <summary>
    /// The reason a table of contents cannot be inserted at <paramref name="at"/> (see <see cref="InsertToc"/>), or null: a prefix document (<paramref name="prefixDocument"/>), a place outside the body or in a content control that holds
    /// one line, and a place in a table of contents. A stale position throws <see cref="StalePositionException"/>.
    /// </summary>
    public TocRefusal? RefusalForToc(TextPosition at, bool prefixDocument = false)
    {
        if (CaptionRules.Refusal(_document, at, chapterNumber: false, prefixDocument) is { } refused)
        {
            return refused == CaptionRefusal.Prefix ? TocRefusal.Prefix : TocRefusal.NotInBody;
        }

        var (_, container, index) = Locate(at);
        return _tocs.Around(container, index) is not null ? TocRefusal.InsideToc : null;
    }

    /// <summary>
    /// The reason <see cref="RemoveToc"/> would refuse at <paramref name="at"/>, or null when there is a table of contents to remove: the position (an edge of the field counts) is not in one, or one of its paragraphs ends a section.
    /// </summary>
    public TocRefusal? RefusalForRemoveToc(TextPosition at)
    {
        ArgumentNullException.ThrowIfNull(at.Paragraph);
        if (at.Story.Kind != StoryKind.Body)
        {
            return TocRefusal.NotInBody;
        }

        return SiteAt(at) is { } site && !HasSectionBreak(site) ? null : TocRefusal.NothingToRemove;
    }

    /// <summary>
    /// Inserts a table of contents (or a list of figures or tables when <paramref name="options"/> names a label) as one history step: the field <c>TOC \o "a-b" \h \z \u</c> (<c>\n</c> without page numbers,
    /// <c>\h \z \c "label"</c> for a list), its entries in the styles <c>TOC1</c> to <c>TOC9</c> (<c>TableofFigures</c>; made as companions of the step when the document lacks them, see <see cref="EditResult.Style"/>), one
    /// paragraph each, the begin, the instruction and the separate in the first paragraph and the end in the last, or the sentence of an empty list when there is no entry. An empty paragraph at <paramref name="at"/> is replaced; otherwise the
    /// paragraphs go above the paragraph when the position is its start and below it elsewhere. Every heading (caption) an entry points at gets a hidden <c>_Toc</c> bookmark, a derived change of the step (the paragraphs it
    /// changed swap with the step on undo and redo). The page numbers are those <paramref name="host"/> knows (<c>?</c> until the page results are refreshed). The caret ends at the end of the field. Returns null, and changes
    /// nothing, when the options or the place are refused (<see cref="LastTocRefusal"/>). A stale position throws <see cref="StalePositionException"/> before anything changes.
    /// </summary>
    public EditResult? InsertToc(TextPosition at, TocOptions options, IFieldHost host, bool prefixDocument = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);
        LastTocRefusal = null;
        LastFieldRefusal = null;
        if (options.Label is null && (options.FirstLevel is < 1 or > 9 || options.LastLevel is < 1 or > 9 || options.FirstLevel > options.LastLevel))
        {
            LastTocRefusal = TocRefusal.LevelRange;
            return null;
        }

        if (options.Label is { } label && !CaptionLabels.IsAllowed(_document, label))
        {
            LastTocRefusal = TocRefusal.LabelNotAllowed;
            return null;
        }

        if (RefusalForToc(at, prefixDocument) is { } refusal)
        {
            LastTocRefusal = refusal;
            LastFieldRefusal = refusal == TocRefusal.InsideToc ? new FieldRefusal(FieldRefusalKind.InsideToc) : null;
            return null;
        }

        var spec = TocSpec.TryParse(TocSpec.InstructionFor(options)) ?? throw new InvalidOperationException("The instruction of a new table of contents is not one.");
        var (story, container, index) = Locate(at);
        var map = Map(story, at.Paragraph);
        var offset = EditRules.Snap(map, Math.Clamp(at.Offset, 0, map.Projection.Text.Length));
        var replaces = at.Paragraph.SectionBreak is null && at.Paragraph.Inlines.All(i => i is Run { Content.Count: 0 });
        var slot = replaces || offset == 0 ? index : index + 1;
        var removed = replaces ? 1 : 0;

        var revision = History.BeginOperation();
        var existing = TocRefresh.Find(_document.Body.Blocks);
        var excluded = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        foreach (var site in existing)
        {
            for (var i = site.First; i <= site.Last; i++)
            {
                if (site.Container[i] is Paragraph paragraph)
                {
                    excluded.Add(paragraph);
                }
            }
        }

        // The page of the headings and the bookmarks are taken from the document as it is now; the headings that get a bookmark change in place (the step carries them as derived changes, below).
        var entries = TocBuilder.Collect(_document, host, spec, excluded);
        var bookmarks = new TocRefresh.TocBookmarks(_document);
        var top = ReferenceEquals(container, _document.Body.Blocks) ? index : Math.Max(0, TopLevelIndexOf(_document.Body.Blocks, at.Paragraph));
        var tabs = options.PageNumbers && options.RightAlign ? TocBuilder.RightTab(TocBuilder.TextWidthTwips(_document, top), options.Leader) : null;
        var style = TocBuilder.EnsureStyles(_document, spec);
        var made = TocBuilder.Entries(
            _document,
            spec,
            entries,
            entry => bookmarks.NameFor(entry.Source),
            (entry, _) => host.PageOf(new TextPosition(StoryRef.Body, entry.Source, 0))?.PageText ?? "?",
            tabs);
        var marks = FieldWriter.Make(TocSpec.InstructionFor(options), string.Empty, null);
        for (var i = 2; i >= 0; i--)
        {
            made[0].Inlines.Insert(0, marks[i]);
        }

        if (made.Count == 1)
        {
            // A list of one line (or the sentence of an empty one) gets the end mark in a paragraph of its own, so that the field spans two paragraphs and is protected as a whole (see TocRefresh).
            var end = new Paragraph();
            end.Inlines.Add(marks[4]);
            made.Add(end);
        }
        else
        {
            made[^1].Inlines.Add(marks[4]);
        }

        var old = replaces ? new Block[] { at.Paragraph.Clone() } : [];
        if (replaces)
        {
            container.RemoveAt(index);
        }

        for (var k = 0; k < made.Count; k++)
        {
            container.Insert(slot + k, made[k]);
        }

        _document.Edits.Raise();
        var last = made[^1];
        var caret = new TextPosition(story, last, Map(story, last).Projection.Text.Length);
        var change = new BlockChange(story, container, slot, removed, made.Count, old);
        var derived = DerivedOf(bookmarks.Changed);
        var result = new EditResult(caret, [change], Style: style, DerivedChanges: derived.Count > 0 ? derived : null);
        _document.Edits.Attribute([change, .. derived]);
        History.Record(result, new EditHistory.Point(index, offset), revision, null, after: new EditHistory.Point(slot + made.Count - 1, caret.Offset));
        if (derived.Count > 0)
        {
            History.AttachDerived(new DerivedRecord(derived));
        }

        return result;
    }

    /// <summary>
    /// Removes the table of contents the position is in (an edge of the field counts) as one history step: the field, its entries and the hidden <c>_Toc</c> bookmarks no link or page reference points at any more (the headings
    /// that lose one are derived changes of the step). A body, cell or content control that would be left with no block gets an empty paragraph. The caret goes to the paragraph that follows (or the last one before). Returns null,
    /// and changes nothing, when there is no table of contents at the position or one of its paragraphs ends a section (<see cref="LastTocRefusal"/>). Markers of other bookmarks that stand in the entries go with them.
    /// </summary>
    public EditResult? RemoveToc(TextPosition at)
    {
        LastTocRefusal = null;
        LastFieldRefusal = null;
        if (RefusalForRemoveToc(at) is { } refusal)
        {
            LastTocRefusal = refusal;
            return null;
        }

        var site = SiteAt(at)!;
        var story = at.Story;
        var container = site.Container;
        var revision = History.BeginOperation();
        var count = site.Last - site.First + 1;
        var old = new List<Block>(count);
        for (var i = site.First; i <= site.Last; i++)
        {
            old.Add(container[i].Clone());
        }

        for (var i = 0; i < count; i++)
        {
            container.RemoveAt(site.First);
        }

        // A list with no paragraph left (a cell, a control, a body of tables) gets an empty one: a caret needs a paragraph and a body that ends in a table is one Word repairs.
        var added = 0;
        if (!container.OfType<Paragraph>().Any())
        {
            container.Insert(Math.Min(site.First, container.Count), new Paragraph());
            added = 1;
        }

        var orphans = TocRefresh.RemoveOrphans(_document);
        _document.Edits.Raise();
        var caretIndex = -1;
        for (var i = site.First; i < container.Count && caretIndex < 0; i++)
        {
            caretIndex = container[i] is Paragraph ? i : -1;
        }

        var atStart = caretIndex >= 0;
        for (var i = Math.Min(site.First, container.Count) - 1; i >= 0 && caretIndex < 0; i--)
        {
            caretIndex = container[i] is Paragraph ? i : -1;
        }

        var anchor = (Paragraph)container[caretIndex];
        var caret = new TextPosition(story, anchor, atStart ? 0 : Map(story, anchor).Projection.Text.Length);
        var change = new BlockChange(story, container, site.First, count, added, old);
        var derived = DerivedOf(orphans);
        var result = new EditResult(caret, [change], DerivedChanges: derived.Count > 0 ? derived : null);
        _document.Edits.Attribute([change, .. derived]);
        History.Record(result, new EditHistory.Point(site.First, 0), revision, null, after: new EditHistory.Point(caretIndex, caret.Offset));
        if (derived.Count > 0)
        {
            History.AttachDerived(new DerivedRecord(derived));
        }

        return result;
    }

    // The table of contents of the body (any list of it) that holds the paragraph of the position, or null.
    private TocSite? SiteAt(TextPosition at)
    {
        if (StoryResolver.TryStory(_document, at.Story) is not { } story || FindIn(story.Blocks, at.Paragraph) is not { } found)
        {
            return null;
        }

        return at.Story.Kind == StoryKind.Body && _tocs.Around(found.Container, found.Index) is { } range
            ? new TocSite(found.Container, range.First, range.Last, range.Instruction)
            : at.Story.Kind == StoryKind.Body ? SingleParagraphSite(found.Container, found.Index) : null;
    }

    // A table of contents that stands in one paragraph (the sentence of an empty one): a complete TOC field that is the whole paragraph.
    private static TocSite? SingleParagraphSite(IList<Block> container, int index)
    {
        var site = TocRefresh.Find(container).FirstOrDefault(s => ReferenceEquals(s.Container, container) && s.First == index && s.Last == index);
        return site is not null && TocRefresh.Analyze(site) is not null ? site : null;
    }

    private static bool HasSectionBreak(TocSite site)
    {
        for (var i = site.First; i <= site.Last; i++)
        {
            if (site.Container[i] is Paragraph { SectionBreak: not null })
            {
                return true;
            }
        }

        return false;
    }

    // The block changes (one paragraph each, in place) of the paragraphs a step changed besides its own blocks, at the indices they have now.
    private List<BlockChange> DerivedOf(IReadOnlyList<(Paragraph Paragraph, Paragraph Before)> changed)
    {
        var derived = new List<BlockChange>();
        foreach (var (paragraph, before) in changed)
        {
            if (FindIn(_document.Body.Blocks, paragraph) is { } place)
            {
                derived.Add(new BlockChange(StoryRef.Body, place.Container, place.Index, 1, 1, [before]));
            }
        }

        return derived;
    }
}
