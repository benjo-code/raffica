using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// Gives a section its own header or footer of a type (<paramref name="own"/>: a new part holding a copy of what the section shows now, so nothing on the pages changes) or makes it the same as the
    /// section before (the reference is removed; the part stays in the package, unused). One history step. Null (and nothing changed) for the first section, which always has its own, for an unknown
    /// section, and when the section is already in the state asked for.
    /// Own for the default type makes no even copy when left and right pages are on (turn that on again or give the even type its own part). <paramref name="caret"/> is where the user's caret is: the history step
    /// and undo and redo put it back there (null: the first paragraph of the body).
    /// </summary>
    public EditResult? SetOwnHeaderFooter(int section, HeaderFooterKind kind, HeaderFooterType type, bool own, TextPosition? caret = null)
    {
        var sections = SectionList.Of(_document);
        if (section < 1 || section >= sections.Count)
        {
            return null;
        }

        var hasOwn = sections[section].Properties.HeaderFooterReferences.Any(reference => reference.Kind == kind && reference.Type == type);
        if (hasOwn == own)
        {
            return null;
        }

        if (SectionCaret(caret) is not { } place)
        {
            return null;
        }

        var revision = History.BeginOperation();
        var touched = new List<(SectionProperties Live, SectionProperties Old)>();
        var live = LiveSection(sections[section], touched);
        if (own)
        {
            var id = HeaderFooterParts.Create(_document, kind, StoryOf(SectionList.Resolve(sections, section, kind, type).Reference));
            HeaderFooterSharing.SetReference(live, kind, type, id);
        }
        else
        {
            HeaderFooterSharing.RemoveReference(live, kind, type);
        }

        return CommitSections(revision, Change(touched, null, null), null, place.Caret, place.Before);
    }

    /// <summary>
    /// Sets or clears <c>w:titlePg</c> of a section. Turning it on gives the section an empty own first-page header and footer only where none resolves (an inherited first-page part is used as it is);
    /// turning it off keeps every part. One history step. Null (nothing changed) for an unknown section or when the flag already is as asked.
    /// <paramref name="caret"/> is as for <see cref="SetOwnHeaderFooter"/>.
    /// </summary>
    public EditResult? SetDifferentFirstPage(int section, bool on, TextPosition? caret = null)
    {
        var sections = SectionList.Of(_document);
        if (section < 0 || section >= sections.Count || sections[section].Properties.GetFlag(W + "titlePg") == on)
        {
            return null;
        }

        if (SectionCaret(caret) is not { } place)
        {
            return null;
        }

        var revision = History.BeginOperation();
        var touched = new List<(SectionProperties Live, SectionProperties Old)>();
        var live = LiveSection(sections[section], touched);
        live.SetFlag(W + "titlePg", on);
        if (on)
        {
            sections = SectionList.Of(_document);
            foreach (var kind in new[] { HeaderFooterKind.Header, HeaderFooterKind.Footer })
            {
                if (SectionList.Resolve(sections, section, kind, HeaderFooterType.First).Reference is null)
                {
                    HeaderFooterSharing.SetReference(live, kind, HeaderFooterType.First, HeaderFooterParts.Create(_document, kind, null));
                }
            }
        }

        return CommitSections(revision, Change(touched, null, null), null, place.Caret, place.Before);
    }

    /// <summary>
    /// Turns left and right pages (<c>w:evenAndOddHeaders</c> of the settings part, document-wide; the part is made when the file has none) on or off. Turning it on gives every section with an own
    /// default header or footer and no own even one an even copy of it (a section that inherits its default inherits the even part too); turning it off keeps every part. One history step.
    /// Null (nothing changed) when the flag already is as asked or the body holds no paragraph for the caret. <paramref name="caret"/> is as for <see cref="SetOwnHeaderFooter"/>.
    /// </summary>
    public EditResult? SetLeftRightPages(bool on, TextPosition? caret = null)
    {
        var current = _document.Settings?.EvenAndOddHeaders ?? false;
        if (current == on || SectionCaret(caret) is not { } place)
        {
            return null;
        }

        var revision = History.BeginOperation();
        var touched = new List<(SectionProperties Live, SectionProperties Old)>();
        _document.Settings ??= new DocumentSettings(new OoxmlRoot(W + "settings", [new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName)]));
        if (on)
        {
            foreach (var info in SectionList.Of(_document))
            {
                foreach (var kind in new[] { HeaderFooterKind.Header, HeaderFooterKind.Footer })
                {
                    var references = info.Properties.HeaderFooterReferences;
                    if (references.FirstOrDefault(reference => reference.Kind == kind && reference.Type == HeaderFooterType.Default) is { } own
                        && !references.Any(reference => reference.Kind == kind && reference.Type == HeaderFooterType.Even))
                    {
                        var live = LiveSection(info, touched);
                        HeaderFooterSharing.SetReference(live, kind, HeaderFooterType.Even, HeaderFooterParts.Create(_document, kind, StoryOf(own)));
                    }
                }
            }
        }

        _document.Settings.EvenAndOddHeaders = on;
        return CommitSections(revision, Change(touched, current, on), null, place.Caret, place.Before);
    }

    /// <summary>The sections that show the header or footer part a section resolves to for a type (see <see cref="SectionSharing"/>); no part (and an unknown section) gives owner -1 and no sections; otherwise the list is ascending and holds the owner.</summary>
    public SectionSharing SharingOf(HeaderFooterKind kind, HeaderFooterType type, int section)
    {
        var sections = SectionList.Of(_document);
        if (section < 0 || section >= sections.Count)
        {
            return new SectionSharing(-1, []);
        }

        var (reference, owner) = SectionList.Resolve(sections, section, kind, type);
        if (reference is null)
        {
            return new SectionSharing(-1, []);
        }

        var shared = new List<int>();
        for (var i = owner; i < sections.Count; i++)
        {
            if (SectionList.Resolve(sections, i, kind, type).Reference?.RelationshipId == reference.RelationshipId)
            {
                shared.Add(i);
            }
        }

        return new SectionSharing(owner, shared);
    }

    private Story? StoryOf(HeaderFooterReference? reference) =>
        reference is not null && _document.HeadersFooters.TryGetValue(reference.RelationshipId, out var part) ? part.Content : null;

    // Where the history step and the result put the caret: the caller's own caret (container-relative, so a caret in a table cell or a content control works; a position that is not in its story is stale),
    // else the first paragraph of the body, found through tables and content controls. A body with no paragraph at all has no place for a caret and the command does nothing.
    private (TextPosition Caret, EditHistory.Point Before)? SectionCaret(TextPosition? caret)
    {
        if (caret is { } given)
        {
            var blocks = StoryResolver.TryBlocks(_document, given.Story) ?? throw new StalePositionException("The story is not part of the document.");
            var found = FindIn(blocks, given.Paragraph) ?? throw new StalePositionException("The paragraph is not in its story.");
            return (given, new EditHistory.Point(found.Index, given.Offset));
        }

        return FirstParagraph(_document.Body.Blocks) is { } paragraph && FindIn(_document.Body.Blocks, paragraph) is { } place
            ? (new TextPosition(StoryRef.Body, paragraph, 0), new EditHistory.Point(place.Index, 0))
            : null;
    }

    internal static Paragraph? FirstParagraph(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    return paragraph;
                case ContentControlBlock control when FirstParagraph(control.Content) is { } inControl:
                    return inControl;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        if (FirstParagraphIn(row) is { } inRow)
                        {
                            return inRow;
                        }
                    }

                    break;
            }
        }

        return null;
    }

    private static Paragraph? FirstParagraphIn(RowItem item) => item switch
    {
        TableRow row => row.Items.Select(FirstParagraphIn).FirstOrDefault(found => found is not null),
        TableCell cell => FirstParagraph(cell.Blocks),
        _ => null,
    };

    // The properties of a section as the live object (a body with no final w:sectPr gets one), recorded with a clone of the content before the change.
    private SectionProperties LiveSection(SectionInfo info, List<(SectionProperties Live, SectionProperties Old)> touched)
    {
        var live = SectionSplitter.Live(_document, info);
        if (!touched.Any(entry => ReferenceEquals(entry.Live, live)))
        {
            touched.Add((live, live.Clone()));
        }

        return live;
    }

    private static SectionChange Change(List<(SectionProperties Live, SectionProperties Old)> touched, bool? flagBefore, bool? flagAfter) =>
        new([.. touched.Select(entry => new SectionSwap(entry.Live, entry.Old, entry.Live.Clone()))], flagBefore, flagAfter);
}
