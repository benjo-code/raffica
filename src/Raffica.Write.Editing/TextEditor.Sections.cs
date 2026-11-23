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
        var change = new SectionChange([new SectionSwap(split.Later, split.OldLater, split.Later.Clone())], null, null, null, [split.CarrierProperties]);
        return CommitSections(revision, change, split.Change, new TextPosition(StoryRef.Body, split.Carrier, 0), before);
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

        var change = new SectionChange([new SectionSwap(split.Later, split.OldLater, split.Later.Clone())], null, null, null, [split.CarrierProperties]);
        return CommitSections(revision, change, split.Change, new TextPosition(StoryRef.Body, split.Carrier, 0), before);
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

        if (numbering.Prefix.Length > MaximumPrefixLength || numbering.Prefix.Any(char.IsControl))
        {
            throw new ArgumentException("The prefix is at most 32 characters and holds no control character.", nameof(numbering));
        }
    }

    private static void ApplyNumbering(SectionProperties properties, PageNumbering numbering)
    {
        var element = properties.Get(W + "pgNumType");
        if (element is null && numbering.Start is null && numbering.Format == PageNumberFormat.Decimal)
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

    // Every footer type the section shows gets the number: a type that resolves to a footer with a PAGE field is left alone (without a prefix), any other gets an own part and reference.
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
            var holdsPage = source is not null && StoryHoldsPage(source.Blocks, 0);
            if (holdsPage && numbering.Prefix.Length == 0)
            {
                continue;
            }

            var id = HeaderFooterParts.Create(_document, HeaderFooterKind.Footer, source);
            var blocks = _document.HeadersFooters[id].Content.Blocks;
            if (!(holdsPage && PrefixEveryPage(blocks, numbering.Prefix)))
            {
                AddNumberParagraph(blocks, numbering, holdsPage);
            }

            SetFooterReference(properties, type, id);
        }
    }

    // A footer that holds a PAGE field the prefix cannot be put before (it is inside a hyperlink, say) is replaced by the number alone; any other footer keeps its content and gets the number below it.
    private void AddNumberParagraph(IList<Block> blocks, PageNumbering numbering, bool replaceAll)
    {
        var number = PageNumberField.Build(
            PageNumberLook.Plain,
            PageNumberAlign.Center,
            numbering.Prefix,
            string.Empty,
            string.Empty,
            (numbering.Start ?? 1).ToString(CultureInfo.InvariantCulture),
            "1");
        if (_document.Styles?.Find("Footer") is not null)
        {
            number.Properties.StyleId = "Footer";
        }

        if (replaceAll || (blocks.Count == 1 && blocks[0] is Paragraph { Inlines.Count: 0 }))
        {
            blocks.Clear();
        }

        blocks.Add(number);
    }

    // Puts the prefix before the PAGE field of every paragraph that holds one; false (the blocks may be half done, the caller replaces them) when a PAGE field is not at the top level of its paragraph.
    private static bool PrefixEveryPage(IList<Block> blocks, string prefix)
    {
        var paragraphs = new List<Paragraph>();
        CollectParagraphs(blocks, paragraphs, 0);
        foreach (var paragraph in paragraphs)
        {
            if (PageNumberField.HoldsPage(paragraph) && !InsertPrefix(paragraph, prefix))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InsertPrefix(Paragraph paragraph, string prefix)
    {
        var inlines = paragraph.Inlines;
        for (var i = 0; i < inlines.Count; i++)
        {
            Run? template = null;
            if (inlines[i] is SimpleField simple && IsPage(simple.Instruction))
            {
                template = new Run();
            }
            else if (inlines[i] is Run run && run.Content.Any(c => c is FieldChar { Kind: FieldCharKind.Begin }) && IsPage(InstructionAfter(inlines, i)))
            {
                template = run.Clone();
                template.Content.Clear();
            }

            if (template is null)
            {
                continue;
            }

            if (i > 0 && inlines[i - 1] is Run { Content.Count: > 0 } previous && previous.Content[^1] is TextContent text && text.Text.EndsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }

            template.Content.Add(new TextContent(prefix));
            inlines.Insert(i, template);
            return true;
        }

        return false;
    }

    private static string InstructionAfter(IList<Inline> inlines, int begin)
    {
        var text = new StringBuilder();
        for (var j = begin; j < inlines.Count && inlines[j] is Run run; j++)
        {
            foreach (var content in run.Content)
            {
                switch (content)
                {
                    case InstructionText instruction:
                        text.Append(instruction.Text);
                        break;
                    case FieldChar { Kind: FieldCharKind.Separate or FieldCharKind.End }:
                        return text.ToString();
                    default:
                        break;
                }
            }
        }

        return text.ToString();
    }

    private static bool IsPage(string instruction)
    {
        var text = instruction.AsSpan().TrimStart();
        var end = text.IndexOfAny(' ', '\t');
        return (end < 0 ? text : text[..end]).Equals("PAGE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StoryHoldsPage(IList<Block> blocks, int depth)
    {
        var paragraphs = new List<Paragraph>();
        CollectParagraphs(blocks, paragraphs, depth);
        return paragraphs.Any(PageNumberField.HoldsPage);
    }

    // The paragraphs of a header or footer, tables and block content controls included (a built-in page-number footer is a control around its paragraph).
    private static void CollectParagraphs(IList<Block> blocks, List<Paragraph> into, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    into.Add(paragraph);
                    break;
                case ContentControlBlock control:
                    CollectParagraphs(control.Content, into, depth + 1);
                    break;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        CollectParagraphs(row, into, depth + 1);
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static void CollectParagraphs(RowItem item, List<Paragraph> into, int depth)
    {
        switch (item)
        {
            case TableRow row:
                foreach (var child in row.Items)
                {
                    CollectParagraphs(child, into, depth + 1);
                }

                break;
            case TableCell cell:
                CollectParagraphs(cell.Blocks, into, depth + 1);
                break;
            default:
                break;
        }
    }

    private static void SetFooterReference(SectionProperties properties, HeaderFooterType type, string id)
    {
        var references = properties.HeaderFooterReferences;
        for (var i = 0; i < references.Count; i++)
        {
            if (references[i].Kind == HeaderFooterKind.Footer && references[i].Type == type)
            {
                references[i] = new HeaderFooterReference(HeaderFooterKind.Footer, type, id) { BagPosition = references[i].BagPosition };
                return;
            }
        }

        references.Add(new HeaderFooterReference(HeaderFooterKind.Footer, type, id));
    }

    private static bool IsOn(XElement element) =>
        (string?)element.Attribute(W + "val") is not ("0" or "false" or "off");
}
