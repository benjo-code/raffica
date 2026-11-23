using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>The words a page-number look is made of, in the language of the user interface at the time of insertion ("Page", "of"): plain text in the document that the user can edit afterwards.</summary>
public sealed record PageNumberWords(string Page, string Of);

public sealed partial class TextEditor
{
    /// <summary>
    /// Inserts a page number (<c>PAGE</c>, and <c>NUMPAGES</c> for the looks with a total, as complex fields; the cached results are what a reader that does not evaluate fields shows) and returns the result of the one
    /// history step, or null (nothing changed) for a place that is neither the body nor a header or footer.
    /// <para>
    /// A caret in a header or footer story inserts the field at the caret there (<paramref name="kind"/>, <paramref name="align"/> and <paramref name="type"/> are ignored; the paragraph keeps its own alignment); the result's caret is
    /// after the field in that story. A caret in the body writes into the header or footer story of the caret's section for <paramref name="type"/> (the caller passes the type that applies to the caret's page: <c>First</c> when
    /// the section has a different first page and the caret is on its first page, <c>Even</c> with left and right pages on an even page, else <c>Default</c>): a section that resolves to none gets an own part of that type; a part
    /// it inherits is edited for every section that shares it (see <see cref="PageNumberSharing"/>). In that story the first paragraph that holds a <c>PAGE</c> field (tables and block content controls looked through) is
    /// <b>replaced as a whole</b>: other text in that paragraph is dropped, its paragraph style stays. Known limit: only that first paragraph is replaced, a second paragraph with a <c>PAGE</c> field stays as it is. With no such
    /// paragraph the number goes into a lone empty paragraph or after the last block. The caller passes the cached results (the number of the caret's page and the page total) and the words in the UI language.
    /// </para>
    /// <para>
    /// <b>Caret contract.</b> A step made from a body caret changes only a header or footer story (and perhaps the references of the section): its result and the result of every undo and redo of it carry
    /// <see cref="EditResult.KeepSelection"/>, so the owner keeps the selection it has (the user's body selection) and ignores <see cref="EditResult.Caret"/>, which lies in the changed story (the layout needs the changed
    /// paragraph there). A step made from a caret inside a header or footer story keeps the ordinary contract: the caret is in that story, also after undo and redo.
    /// A new part also swaps the section's references (<see cref="EditResult.Sections"/>, the layout starts over). A prefix document, which the editor cannot see, is refused by the caller through
    /// <see cref="ReadSectionFacts"/>. A stale position throws <see cref="StalePositionException"/> before anything changes.
    /// </para>
    /// </summary>
    public EditResult? InsertPageNumber(TextPosition at, PageNumberLook look, HeaderFooterKind kind, PageNumberAlign align, PageNumberWords words, string cachedPage, string cachedTotal, HeaderFooterType type = HeaderFooterType.Default)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(cachedPage);
        ArgumentNullException.ThrowIfNull(cachedTotal);
        if (at.Paragraph is null)
        {
            return null;
        }

        return at.Story.Kind switch
        {
            StoryKind.HeaderFooter => InsertNumberAtCaret(at, look, words, cachedPage, cachedTotal),
            StoryKind.Body => InsertNumberForSection(at, look, kind, type, align, words, cachedPage, cachedTotal),
            _ => null,
        };
    }

    /// <summary>
    /// The sections that show the header or footer story <see cref="InsertPageNumber"/> writes into for <paramref name="at"/>: a body caret, the part its section resolves to for <paramref name="type"/>; a caret in a header or
    /// footer, the part it is in (<paramref name="kind"/> and <paramref name="type"/> are then ignored). Only sections that really show the type count, as the layout uses it: every section for <c>Default</c>, a section with
    /// <c>w:titlePg</c> for <c>First</c>, every section for <c>Even</c> when left and right pages are on (none when off). No part (or none shown): owner -1 and no sections; otherwise the owner is the section that holds the
    /// reference when it is in the list, else the first section of the list. More than one section means the edit reaches all of them.
    /// </summary>
    public SectionSharing PageNumberSharing(TextPosition at, HeaderFooterKind kind, HeaderFooterType type = HeaderFooterType.Default)
    {
        if (at.Paragraph is null)
        {
            return new SectionSharing(-1, []);
        }

        var sections = SectionList.Of(_document);
        if (at.Story.Kind == StoryKind.HeaderFooter)
        {
            var shared = new List<int>();
            for (var i = 0; i < sections.Count; i++)
            {
                foreach (var shownType in new[] { HeaderFooterType.Default, HeaderFooterType.First, HeaderFooterType.Even })
                {
                    foreach (var partKind in new[] { HeaderFooterKind.Header, HeaderFooterKind.Footer })
                    {
                        if (SectionList.Resolve(sections, i, partKind, shownType).Reference?.RelationshipId == at.Story.Key && Shows(sections[i], shownType) && !shared.Contains(i))
                        {
                            shared.Add(i);
                        }
                    }
                }
            }

            return shared.Count == 0 ? new SectionSharing(-1, []) : new SectionSharing(shared[0], shared);
        }

        if (at.Story.Kind != StoryKind.Body)
        {
            return new SectionSharing(-1, []);
        }

        var index = TopLevelIndexOf(at.Paragraph) ?? throw new StalePositionException("The paragraph is not in the body.");
        var sharing = SharingOf(kind, type, SectionSplitter.SectionIndexOf(sections, index));
        var showing = sharing.Sections.Where(section => Shows(sections[section], type)).ToList();
        return showing.Count == 0 ? new SectionSharing(-1, []) : new SectionSharing(showing.Contains(sharing.OwnerSection) ? sharing.OwnerSection : showing[0], showing);
    }

    // The layout uses a first-page part only for a section with w:titlePg and an even part only when the document has left and right pages.
    private bool Shows(SectionInfo section, HeaderFooterType type) => type switch
    {
        HeaderFooterType.First => section.Properties.GetFlag(W + "titlePg"),
        HeaderFooterType.Even => _document.Settings?.EvenAndOddHeaders == true,
        _ => true,
    };

    private EditResult? InsertNumberAtCaret(TextPosition at, PageNumberLook look, PageNumberWords words, string cachedPage, string cachedTotal)
    {
        var (story, container, index) = Locate(at);
        var revision = History.BeginOperation();
        var paragraph = at.Paragraph;
        var old = paragraph.Clone();
        var map = Map(story, paragraph);
        var offset = EditRules.Snap(map, Math.Max(0, at.Offset));
        var restLength = map.Projection.Text.Length - offset;
        var (item, chars) = CutOf(map, ResolvePoint(map, offset));
        var rest = InlineSplitter.Split(paragraph, item, chars);
        AppendAll(paragraph.Inlines, PageNumberField.Build(look, PageNumberAlign.Left, string.Empty, words.Page, words.Of, cachedPage, cachedTotal).Inlines);
        AppendAll(paragraph.Inlines, rest);
        var caret = new TextPosition(story, paragraph, Map(story, paragraph).Projection.Text.Length - restLength);
        return CommitStoryEdit(revision, new BlockChange(story, container, index, 1, 1, [old]), caret, new EditHistory.Point(index, offset), null, bodyCaret: false);
    }

    private EditResult? InsertNumberForSection(TextPosition at, PageNumberLook look, HeaderFooterKind kind, HeaderFooterType type, PageNumberAlign align, PageNumberWords words, string cachedPage, string cachedTotal)
    {
        var index = TopLevelIndexOf(at.Paragraph) ?? throw new StalePositionException("The paragraph is not in the body.");
        var revision = History.BeginOperation();
        var sections = SectionList.Of(_document);
        var section = SectionSplitter.SectionIndexOf(sections, index);
        var built = PageNumberField.Build(look, align, string.Empty, words.Page, words.Of, cachedPage, cachedTotal);
        var styleId = kind == HeaderFooterKind.Footer ? "Footer" : "Header";

        var (reference, _) = SectionList.Resolve(sections, section, kind, type);
        if (reference is not null && _document.HeadersFooters.TryGetValue(reference.RelationshipId, out var part))
        {
            var story = new StoryRef(StoryKind.HeaderFooter, reference.RelationshipId);
            var blocks = part.Content.Blocks;
            BlockChange change;
            if (PageNumberField.Paragraphs(blocks).FirstOrDefault(PageNumberField.HoldsPage) is { } existing && FindIn(blocks, existing) is { } found)
            {
                SetStyle(built, styleId, existing);
                change = new BlockChange(story, found.Container, found.Index, 1, 1, [existing.Clone()]);
                found.Container[found.Index] = built;
            }
            else if (blocks.Count == 1 && blocks[0] is Paragraph { Inlines.Count: 0 } empty)
            {
                SetStyle(built, styleId, empty);
                change = new BlockChange(story, blocks, 0, 1, 1, [empty.Clone()]);
                blocks[0] = built;
            }
            else
            {
                SetStyle(built, styleId, null);
                change = new BlockChange(story, blocks, blocks.Count, 0, 1, []);
                blocks.Add(built);
            }

            return CommitStoryEdit(revision, change, new TextPosition(story, built, 0), new EditHistory.Point(change.First, 0), null, bodyCaret: true);
        }

        // The section resolves to no part of that kind and type: it gets an own one, with the number as its only paragraph.
        var touched = new List<(SectionProperties Live, SectionProperties Old)>();
        var live = LiveSection(sections[section], touched);
        var id = HeaderFooterParts.Create(_document, kind, null);
        HeaderFooterSharing.SetReference(live, kind, type, id);
        var created = _document.HeadersFooters[id].Content.Blocks;
        var emptyParagraph = (Paragraph)created[0];
        SetStyle(built, styleId, null);
        var added = new BlockChange(new StoryRef(StoryKind.HeaderFooter, id), created, 0, 1, 1, [emptyParagraph.Clone()]);
        created[0] = built;
        return CommitStoryEdit(revision, added, new TextPosition(added.Story, built, 0), new EditHistory.Point(0, 0), Change(touched, null, null), bodyCaret: true);
    }

    // The paragraph style of the replaced paragraph stays; a new paragraph gets the document's Header or Footer style when it has one.
    private void SetStyle(Paragraph built, string styleId, Paragraph? replaced)
    {
        if (replaced?.Properties.StyleId is { } kept)
        {
            built.Properties.StyleId = kept;
        }
        else if (_document.Styles?.Find(styleId) is not null)
        {
            built.Properties.StyleId = styleId;
        }
    }

    // One history step for an edit of a header or footer story (with the swap of the references of a section that gained an own part). The raise is attributed once, with the carriers of swapped sections.
    // bodyCaret: the step was made from a caret in the body, so the result and the undo and redo of it keep the owner's selection (EditResult.KeepSelection).
    private EditResult CommitStoryEdit(int revision, BlockChange change, TextPosition caret, EditHistory.Point before, SectionChange? sections, bool bodyCaret)
    {
        _document.Edits.Raise();
        var result = new EditResult(caret, [change], KeepSelection: bodyCaret, Sections: sections);
        result.Attribute(_document);
        History.Record(result, before, revision, null, keepsBodySelection: bodyCaret);
        return result;
    }
}
