using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>The words a page-number look is made of, in the language of the user interface at the time of insertion ("Page", "of"): plain text in the document that the user can edit afterwards.</summary>
public sealed record PageNumberWords(string Page, string Of);

public sealed partial class TextEditor
{
    /// <summary>
    /// Inserts a page number (<c>PAGE</c>, and <c>NUMPAGES</c> for the looks with a total, as complex fields; the cached results are what a reader that does not evaluate fields shows) and returns the result of the one
    /// history step, or null (nothing changed) for a place that is neither the body nor a header or footer.
    /// A caret in a header or footer story inserts the field at the caret there (<paramref name="kind"/> and <paramref name="align"/> are ignored; the paragraph keeps its own alignment).
    /// A caret in the body writes into the header or footer story of the caret's section for the default type (a section that resolves to none gets an own part; a part it inherits is edited for every section that
    /// shares it, see <see cref="PageNumberSharing"/>): a paragraph that already holds a <c>PAGE</c> field in that story is replaced (the first one, and it keeps its paragraph style), else the number goes into a lone
    /// empty paragraph or after the last block. The caller passes the cached results (the number of the caret's page and the page total) and the words in the UI language. An edit of a story only changes the story
    /// (the result carries the body selection on through <see cref="EditResult.KeepSelection"/>); a new part also swaps the section's references (<see cref="EditResult.Sections"/>).
    /// A prefix document, which the editor cannot see, is refused by the caller through <see cref="ReadSectionFacts"/>. A stale position throws <see cref="StalePositionException"/> before anything changes.
    /// The caret of an undo or redo of a step made from the body lands in the header or footer paragraph the step changed (the history keeps the caret of the story it changed).
    /// </summary>
    public EditResult? InsertPageNumber(TextPosition at, PageNumberLook look, HeaderFooterKind kind, PageNumberAlign align, PageNumberWords words, string cachedPage, string cachedTotal)
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
            StoryKind.Body => InsertNumberForSection(at, look, kind, align, words, cachedPage, cachedTotal),
            _ => null,
        };
    }

    /// <summary>
    /// The sections that show the header or footer story <see cref="InsertPageNumber"/> writes into for <paramref name="at"/> (a body caret: the part its section resolves to for the default type; a caret in a header
    /// or footer: the part it is in, for every type): owner -1 and no sections when there is none. More than one section means the edit reaches all of them.
    /// </summary>
    public SectionSharing PageNumberSharing(TextPosition at, HeaderFooterKind kind)
    {
        if (at.Paragraph is null)
        {
            return new SectionSharing(-1, []);
        }

        if (at.Story.Kind == StoryKind.HeaderFooter)
        {
            var sections = SectionList.Of(_document);
            var shared = new List<int>();
            for (var i = 0; i < sections.Count; i++)
            {
                foreach (var type in new[] { HeaderFooterType.Default, HeaderFooterType.First, HeaderFooterType.Even })
                {
                    foreach (var partKind in new[] { HeaderFooterKind.Header, HeaderFooterKind.Footer })
                    {
                        if (SectionList.Resolve(sections, i, partKind, type).Reference?.RelationshipId == at.Story.Key && !shared.Contains(i))
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
        return SharingOf(kind, HeaderFooterType.Default, SectionSplitter.SectionIndexOf(SectionList.Of(_document), index));
    }

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
        return CommitStoryEdit(revision, new BlockChange(story, container, index, 1, 1, [old]), caret, new EditHistory.Point(index, at.Offset), null, keepSelection: false);
    }

    private EditResult? InsertNumberForSection(TextPosition at, PageNumberLook look, HeaderFooterKind kind, PageNumberAlign align, PageNumberWords words, string cachedPage, string cachedTotal)
    {
        var index = TopLevelIndexOf(at.Paragraph) ?? throw new StalePositionException("The paragraph is not in the body.");
        var revision = History.BeginOperation();
        var sections = SectionList.Of(_document);
        var section = SectionSplitter.SectionIndexOf(sections, index);
        var built = PageNumberField.Build(look, align, string.Empty, words.Page, words.Of, cachedPage, cachedTotal);
        var styleId = kind == HeaderFooterKind.Footer ? "Footer" : "Header";

        var (reference, _) = SectionList.Resolve(sections, section, kind, HeaderFooterType.Default);
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

            return CommitStoryEdit(revision, change, new TextPosition(story, built, 0), new EditHistory.Point(change.First, 0), null, keepSelection: true);
        }

        // The section resolves to no part of that kind: it gets an own one, with the number as its only paragraph.
        var touched = new List<(SectionProperties Live, SectionProperties Old)>();
        var live = LiveSection(sections[section], touched);
        var id = HeaderFooterParts.Create(_document, kind, null);
        HeaderFooterSharing.SetReference(live, kind, HeaderFooterType.Default, id);
        var created = _document.HeadersFooters[id].Content.Blocks;
        var emptyParagraph = (Paragraph)created[0];
        SetStyle(built, styleId, null);
        var added = new BlockChange(new StoryRef(StoryKind.HeaderFooter, id), created, 0, 1, 1, [emptyParagraph.Clone()]);
        created[0] = built;
        return CommitStoryEdit(revision, added, new TextPosition(added.Story, built, 0), new EditHistory.Point(0, 0), Change(touched, null, null), keepSelection: true);
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
    private EditResult CommitStoryEdit(int revision, BlockChange change, TextPosition caret, EditHistory.Point before, SectionChange? sections, bool keepSelection)
    {
        _document.Edits.Raise();
        var result = new EditResult(caret, [change], KeepSelection: keepSelection, Sections: sections);
        result.Attribute(_document);
        History.Record(result, before, revision, null);
        return result;
    }
}
