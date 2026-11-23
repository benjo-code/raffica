using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    private const int MaximumPageNumber = 99999;
    private const int MaximumPrefixLength = 32;

    /// <summary>
    /// What the section commands know about <paramref name="at"/>. Pure: nothing changes and no history operation begins. A place that is not in the body, a position with no paragraph and a prefix document
    /// (<paramref name="prefixDocument"/>, which the editor cannot see itself) answer with a <see cref="SectionFacts.Reason"/>; a body position whose paragraph is not in the body throws
    /// <see cref="StalePositionException"/> like every operation.
    /// </summary>
    public SectionFacts ReadSectionFacts(TextPosition at, bool prefixDocument = false)
    {
        if (at.Paragraph is null)
        {
            return Unavailable(SectionReason.NoDocument);
        }

        if (prefixDocument)
        {
            return Unavailable(SectionReason.PrefixDocument);
        }

        if (at.Story.Kind != StoryKind.Body)
        {
            return Unavailable(SectionReason.NotInBody);
        }

        var index = TopLevelIndexOf(at.Paragraph) ?? throw new StalePositionException("The paragraph is not in the body.");
        var sections = SectionList.Of(_document);
        var section = SectionSplitter.SectionIndexOf(sections, index);
        var own = 0;
        for (var i = section + 1; i < sections.Count; i++)
        {
            if (HasOwnNumberingSetting(sections[i].Properties))
            {
                own++;
            }
        }

        return new SectionFacts(true, section, sections[section].FirstBlock >= index, sections.Count - 1 - section, own, SectionReason.None);
    }

    /// <summary>
    /// Starts the page numbers at the top-level body block that holds <paramref name="at"/>: a next-page section starts there (none when the block already starts its section, then that section is edited
    /// alone), its <c>w:pgNumType</c> gets the start and the format, and every footer type it shows (the default one; the first-page one while <c>w:titlePg</c> stays on; the even one when the document has left and
    /// right pages) shows the page number: a type that already resolves to a footer with a <c>PAGE</c> field is left alone, any other gets an own footer (a copy of the one it resolved to, with the number centred
    /// in a paragraph of its own). A prefix gives an own footer of each type with the prefix before the <c>PAGE</c> field. Earlier pages keep what they had; later sections that set nothing of their own follow.
    /// One history step. Null (and nothing changed) when the place is not in the body or the command would change nothing.
    /// </summary>
    public EditResult? NumberPagesFrom(TextPosition at, PageNumbering numbering)
    {
        ArgumentNullException.ThrowIfNull(numbering);
        ValidateNumbering(numbering);
        if (at.Paragraph is null || at.Story.Kind != StoryKind.Body)
        {
            return null;
        }

        var revision = History.BeginOperation();
        var blocks = _document.Body.Blocks;
        var index = TopLevelIndexOf(at.Paragraph) ?? throw new StalePositionException("The paragraph is not in the body.");
        var before = BeforePoint(at, index);
        var sections = SectionList.Of(_document);
        var section = SectionSplitter.SectionIndexOf(sections, index);
        if (sections[section].FirstBlock >= index)
        {
            return NumberOwnSection(at, numbering, sections[section], section, revision);
        }

        var split = SectionSplitter.Split(_document, index, SectionBreakKind.NextPage);
        if (split is null)
        {
            return null;
        }

        ApplyNumbering(split.Later, numbering);
        WriteFooters(numbering, section + 1, split.Later);
        return CommitSections(revision, SplitChange(split), split.Change, CaretAfterSplit(at, split), before);
    }

    /// <summary>
    /// Makes a section of the given type start before the top-level body block that holds <paramref name="at"/>: the earlier half ends with the previous plain paragraph or a new empty one, the later half takes the type
    /// and no <c>w:titlePg</c> and continues the page count. Null (nothing changed) when the place is not in the body or the block starts its section already (that would make an empty section). One history step.
    /// </summary>
    public EditResult? InsertSectionBreak(TextPosition at, SectionBreakKind kind)
    {
        if (at.Paragraph is null || at.Story.Kind != StoryKind.Body)
        {
            return null;
        }

        var revision = History.BeginOperation();
        var index = TopLevelIndexOf(at.Paragraph) ?? throw new StalePositionException("The paragraph is not in the body.");
        var before = BeforePoint(at, index);
        if (SectionSplitter.Split(_document, index, kind) is not { } split)
        {
            return null;
        }

        return CommitSections(revision, SplitChange(split), split.Change, CaretAfterSplit(at, split), before);
    }

    /// <summary>
    /// Merges section <paramref name="section"/> (at least 1) into the one before it, which removes the marker between them: the paragraph that ended the earlier section loses its break and the later section's end
    /// properties keep their object and take the whole content of the earlier section's (page setup, numbering, header and footer references, type, <c>w:titlePg</c>, unknown children; what the earlier one lacks is
    /// absent afterwards). Parts the removed setup used stay in the package. One history step. Null when the index is not one of a section after the first.
    /// </summary>
    public EditResult? RemoveSectionBreak(int section)
    {
        var revision = History.BeginOperation();
        var sections = SectionList.Of(_document);
        if (section < 1 || section >= sections.Count)
        {
            return null;
        }

        var carrier = sections[section - 1].Break;
        if (carrier?.SectionBreak is not { } earlier || FindIn(_document.Body.Blocks, carrier) is not { } found)
        {
            return null;
        }

        var live = sections[section].Properties;
        if (sections[section].Break is null && _document.Body.FinalSection is null)
        {
            live = _document.Body.FinalSection = new SectionProperties();
        }

        var oldLive = live.Clone();
        var oldCarrier = carrier.Clone();
        var content = earlier.Clone();
        carrier.SectionBreak = null;
        live.CopyContentFrom(content);
        var change = new SectionChange([new SectionSwap(live, oldLive, live.Clone())], null, null, [earlier], null);
        var block = new BlockChange(StoryRef.Body, found.Container, found.Index, 1, 1, [oldCarrier]);
        return CommitSections(revision, change, block, new TextPosition(StoryRef.Body, carrier, 0), new EditHistory.Point(found.Index, 0));
    }

    // The section the caret's block starts is edited alone: no split, so the step is a section change with no blocks.
    private EditResult? NumberOwnSection(TextPosition at, PageNumbering numbering, SectionInfo info, int section, int revision)
    {
        var live = info.Properties;
        var created = false;
        if (info.Break is null && _document.Body.FinalSection is null)
        {
            live = _document.Body.FinalSection = new SectionProperties();
            created = true;
        }

        var old = live.Clone();
        ApplyNumbering(live, numbering);
        WriteFooters(numbering, section, live);
        if (SectionSplitter.SameContent(old, live))
        {
            if (created)
            {
                _document.Body.FinalSection = null;
            }

            return null;
        }

        var found = FindIn(_document.Body.Blocks, at.Paragraph)!.Value;
        var change = new SectionChange([new SectionSwap(live, old, live.Clone())], null, null);
        return CommitSections(revision, change, null, at, new EditHistory.Point(found.Index, at.Offset));
    }

    // The swap of the later half and the carrier's new section object, which undo and redo keep (SectionChange.CarriersAfter).
    private static SectionChange SplitChange(SectionSplit split) =>
        new([new SectionSwap(split.Later, split.OldLater, split.Later.Clone())], null, null, null, [split.CarrierProperties]);

    // The caret stays where the user had it when that is a top-level paragraph (the history step needs a caret paragraph in the changed list); a caret in a table or a control is replaced by the
    // carrier, and the session keeps the selection it has (EditResult.KeepSelection).
    private TextPosition CaretAfterSplit(TextPosition at, SectionSplit split) =>
        _document.Body.Blocks.Any(block => ReferenceEquals(block, at.Paragraph)) ? at : new TextPosition(StoryRef.Body, split.Carrier, 0);

    private EditResult CommitSections(int revision, SectionChange change, BlockChange? block, TextPosition caret, EditHistory.Point before)
    {
        _document.Edits.Raise();
        var result = new EditResult(caret, block is null ? [] : [block], KeepSelection: true, Sections: change);
        result.Attribute(_document);
        History.Record(result, before, revision, null);
        return result;
    }

    // Where undo puts the caret: the caret's own block when it is a paragraph, else the paragraph before it (which the split turns into the carrier), in the indexes before the split.
    private EditHistory.Point BeforePoint(TextPosition at, int index)
    {
        var blocks = _document.Body.Blocks;
        if (blocks[index] is Paragraph paragraph)
        {
            return new EditHistory.Point(index, ReferenceEquals(paragraph, at.Paragraph) ? at.Offset : 0);
        }

        return index > 0 && blocks[index - 1] is Paragraph ? new EditHistory.Point(index - 1, 0) : new EditHistory.Point(index, 0);
    }

    private int? TopLevelIndexOf(Paragraph paragraph)
    {
        var blocks = _document.Body.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], paragraph))
            {
                return i;
            }
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not Paragraph && FindIn(new[] { blocks[i] }, paragraph) is not null)
            {
                return i;
            }
        }

        return null;
    }

    private static SectionFacts Unavailable(SectionReason reason) => new(false, 0, false, 0, 0, reason);

    private static bool HasOwnNumberingSetting(SectionProperties properties) =>
        properties.Get(W + "pgNumType") is not null || properties.HeaderFooterReferences.Any(reference => reference.Kind == HeaderFooterKind.Footer);

    private static void ValidateNumbering(PageNumbering numbering)
    {
        ArgumentNullException.ThrowIfNull(numbering.Prefix);
        if (numbering.Start is { } start && start is < 0 or > MaximumPageNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(numbering), start, "The first page number is 0 to 99999.");
        }

        if (numbering.Prefix.Length > MaximumPrefixLength || !IsSerializable(numbering.Prefix))
        {
            throw new ArgumentException("The prefix is at most 32 characters and holds no control character or other character a document cannot store.", nameof(numbering));
        }
    }

    // Text the XML writer can store: no control character, no lone surrogate, not U+FFFE or U+FFFF.
    private static bool IsSerializable(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsControl(c) || char.IsSurrogate(c) || !System.Xml.XmlConvert.IsXmlChar(c))
            {
                return false;
            }
        }

        return true;
    }

    private static void ApplyNumbering(SectionProperties properties, PageNumbering numbering)
    {
        var element = properties.Get(W + "pgNumType");
        if (element is null && numbering.Start is null && numbering.Format == PageNumberFormat.Arabic)
        {
            return;
        }

        var existed = element is not null;
        element ??= new XElement(W + "pgNumType");
        element.SetAttributeValue(W + "start", numbering.Start?.ToString(CultureInfo.InvariantCulture));
        var format = numbering.Format switch
        {
            PageNumberFormat.LowerRoman => "lowerRoman",
            PageNumberFormat.UpperRoman => "upperRoman",
            PageNumberFormat.LowerLetter => "lowerLetter",
            PageNumberFormat.UpperLetter => "upperLetter",
            _ => element.Attribute(W + "fmt") is null ? null : "decimal",
        };
        element.SetAttributeValue(W + "fmt", format);
        if (!existed)
        {
            properties.Set(W + "pgNumType", element);
        }
    }

    // Every footer type the section shows gets the number (see FooterNumbering for the rule per type).
    private void WriteFooters(PageNumbering numbering, int section, SectionProperties properties)
    {
        var types = new List<HeaderFooterType> { HeaderFooterType.Default };
        if (properties.Get(W + "titlePg") is { } titlePage && IsOn(titlePage))
        {
            types.Add(HeaderFooterType.First);
        }

        if (_document.Settings?.EvenAndOddHeaders == true)
        {
            types.Add(HeaderFooterType.Even);
        }

        foreach (var type in types)
        {
            var (reference, _) = SectionList.Resolve(SectionList.Of(_document), section, HeaderFooterKind.Footer, type);
            var source = reference is not null && _document.HeadersFooters.TryGetValue(reference.RelationshipId, out var part) ? part.Content : null;
            if (FooterNumbering.Write(_document, source, numbering) is { } id)
            {
                FooterNumbering.SetReference(properties, type, id);
            }
        }
    }

    private static bool IsOn(XElement element) =>
        (string?)element.Attribute(W + "val") is not ("0" or "false" or "off");
}
