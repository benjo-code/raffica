using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// Gives a section its own header or footer of a type (<paramref name="own"/>: a new part holding a copy of what the section shows now, so nothing on the pages changes) or makes it the same as the
    /// section before (the reference is removed; the part stays in the package, unused). One history step. Null (and nothing changed) for the first section, which always has its own, for an unknown
    /// section, and when the section is already in the state asked for.
    /// </summary>
    public EditResult? SetOwnHeaderFooter(int section, HeaderFooterKind kind, HeaderFooterType type, bool own)
    {
        var sections = SectionList.Of(_document);
        if (section < 1 || section >= sections.Count || SectionCaret() is not { } caret)
        {
            return null;
        }

        var hasOwn = sections[section].Properties.HeaderFooterReferences.Any(reference => reference.Kind == kind && reference.Type == type);
        if (hasOwn == own)
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

        return CommitSectionOnly(revision, touched, null, null, caret);
    }

    /// <summary>
    /// Sets or clears <c>w:titlePg</c> of a section. Turning it on gives the section an empty own first-page header and footer only where none resolves (an inherited first-page part is used as it is);
    /// turning it off keeps every part. One history step. Null (nothing changed) for an unknown section or when the flag already is as asked.
    /// </summary>
    public EditResult? SetDifferentFirstPage(int section, bool on)
    {
        var sections = SectionList.Of(_document);
        if (section < 0 || section >= sections.Count || SectionCaret() is not { } caret || sections[section].Properties.GetFlag(W + "titlePg") == on)
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

        return CommitSectionOnly(revision, touched, null, null, caret);
    }

    /// <summary>
    /// Turns left and right pages (<c>w:evenAndOddHeaders</c> of the settings part, document-wide; the part is made when the file has none) on or off. Turning it on gives every section with an own
    /// default header or footer and no own even one an even copy of it (a section that inherits its default inherits the even part too); turning it off keeps every part. One history step.
    /// Null (nothing changed) when the flag already is as asked.
    /// </summary>
    public EditResult? SetLeftRightPages(bool on)
    {
        var current = _document.Settings?.EvenAndOddHeaders ?? false;
        if (current == on || SectionCaret() is not { } caret)
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
        return CommitSectionOnly(revision, touched, current, on, caret);
    }

    /// <summary>The sections that show the header or footer part a section resolves to for a type (see <see cref="SectionSharing"/>); no part gives owner -1 and no sections.</summary>
    public SectionSharing SharingOf(HeaderFooterKind kind, HeaderFooterType type, int section)
    {
        var sections = SectionList.Of(_document);
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

    // These commands have no caret; the history step needs a paragraph to hold one, the first of the body (the session keeps its own selection, KeepSelection).
    private TextPosition? SectionCaret() =>
        _document.Body.Blocks.OfType<Paragraph>().FirstOrDefault() is { } paragraph ? new TextPosition(StoryRef.Body, paragraph, 0) : null;

    // The properties of a section as the live object (a body with no final w:sectPr gets one), recorded with a clone of the content before the change.
    private SectionProperties LiveSection(SectionInfo info, List<(SectionProperties Live, SectionProperties Old)> touched)
    {
        var live = info.Properties;
        if (info.Break is null && _document.Body.FinalSection is null)
        {
            live = _document.Body.FinalSection = new SectionProperties();
        }

        if (!touched.Any(entry => ReferenceEquals(entry.Live, live)))
        {
            touched.Add((live, live.Clone()));
        }

        return live;
    }

    private EditResult CommitSectionOnly(int revision, List<(SectionProperties Live, SectionProperties Old)> touched, bool? flagBefore, bool? flagAfter, TextPosition caret)
    {
        var swaps = touched.Select(entry => new SectionSwap(entry.Live, entry.Old, entry.Live.Clone())).ToList();
        _document.Edits.Raise();
        var result = new EditResult(caret, [], KeepSelection: true, Sections: new SectionChange(swaps, flagBefore, flagAfter));
        result.Attribute(_document);
        History.Record(result, new EditHistory.Point(0, 0), revision, null);
        return result;
    }
}
