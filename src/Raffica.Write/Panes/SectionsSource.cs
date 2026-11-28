using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Panes;

/// <summary>
/// One section of the document as the Sections pane lists it. <see cref="Identity"/> is the model's own <c>w:sectPr</c> of the section: the pane names a section by its index and this object together, so a row
/// that does not belong to the document any more (a marker removed, a section added before it) is recognised when it acts and does nothing.
/// </summary>
/// <param name="Index">The position of the section, from 0.</param>
/// <param name="Identity">The section's properties object in the model.</param>
/// <param name="FirstPage">The page index (from 0) the layout put the section's first block on, or -1 while that page is not laid out yet.</param>
/// <param name="NumberStart">The page number the section restarts at (<c>w:pgNumType w:start</c>), null when it continues.</param>
/// <param name="NumberFormat">The number format the section sets itself, null when it sets none (or one the pane has no words for).</param>
/// <param name="FirstPageDifferent">Whether <c>w:titlePg</c> is on.</param>
/// <param name="HeaderOwn">Whether the section has its own default header part (the first section always has).</param>
/// <param name="FooterOwn">Whether the section has its own default footer part (the first section always has).</param>
/// <param name="Setup">The page setup of the section.</param>
/// <param name="IsEmpty">Whether the section holds no block (the empty final section a section break at the end of the body leaves).</param>
/// <param name="CanNumber">Whether the section has a first paragraph the numbering command can name (false for an empty section and for one that starts with a block that holds no paragraph).</param>
/// <param name="Headers">The header parts the section shows, resolved through the sections before it (what it shows, not what it sets).</param>
/// <param name="Footers">The footer parts the section shows, resolved the same way.</param>
internal sealed record SectionRow(
    int Index,
    SectionProperties Identity,
    int FirstPage,
    int? NumberStart,
    PageNumberFormat? NumberFormat,
    bool FirstPageDifferent,
    bool HeaderOwn,
    bool FooterOwn,
    SectionPageSetup Setup,
    bool IsEmpty,
    bool CanNumber,
    PartIds Headers,
    PartIds Footers)
{
    /// <summary>
    /// What changes on the section's pages when its marker is removed: the section takes the page setup, numbering, first-page switch and header and footer references of the section before it, so what it shows
    /// afterwards is what the section before shows now. Parts are compared as shown (a section that sets none and inherits the same part as the section before changes nothing); the first-page parts count only when both
    /// sections use them (a difference of the switch itself is named as the switch), the even parts only when the document has left and right pages (they are absent otherwise).
    /// </summary>
    public MergeChanges ChangesWhenMerged(SectionRow previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var changes = MergeChanges.None;
        if (Setup != previous.Setup)
        {
            changes |= MergeChanges.PageSetup;
        }

        if (NumberStart != previous.NumberStart || NumberFormat != previous.NumberFormat)
        {
            changes |= MergeChanges.Numbering;
        }

        if (FirstPageDifferent != previous.FirstPageDifferent)
        {
            changes |= MergeChanges.FirstPage;
        }

        var first = FirstPageDifferent && previous.FirstPageDifferent;
        if (!Headers.SameAs(previous.Headers, first))
        {
            changes |= MergeChanges.Headers;
        }

        if (!Footers.SameAs(previous.Footers, first))
        {
            changes |= MergeChanges.Footers;
        }

        return changes;
    }

    /// <summary>Whether removing this section's marker would change anything on its pages.</summary>
    public bool DiffersFrom(SectionRow previous) => ChangesWhenMerged(previous) != MergeChanges.None;
}

/// <summary>The groups of a section's setup that Remove marker can change.</summary>
[Flags]
internal enum MergeChanges
{
    None = 0,
    PageSetup = 1,
    Numbering = 2,
    FirstPage = 4,
    Headers = 8,
    Footers = 16,
}

/// <summary>The relationship ids of the default, first-page and even parts of one kind a section shows (null: none).</summary>
internal sealed record PartIds(string? Default, string? First, string? Even)
{
    /// <summary>Whether two sections show the same parts; the first-page ones are compared only when <paramref name="withFirst"/>.</summary>
    public bool SameAs(PartIds other, bool withFirst)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Default == other.Default && Even == other.Even && (!withFirst || First == other.First);
    }
}

/// <summary>What the pane shows: the rows (none when there are too many sections to list), how many sections the document has and the document-wide switch of left and right pages.</summary>
internal sealed record SectionsView(IReadOnlyList<SectionRow> Rows, bool TooMany, int SectionCount, bool LeftRight);

/// <summary>
/// Reads the sections of a document for the Sections pane, the way the formatting inspector reads a selection: on the UI thread, where the model is edited, remembered for the model, its edit stamp and the page
/// list it was read with (the same question asks nothing again) and bounded (<see cref="MaximumSections"/>). Nothing exists and nothing is read before the first ask.
/// </summary>
internal sealed class SectionsSource
{
    /// <summary>The most sections the pane lists; a document with more shows one line that says so.</summary>
    internal const int MaximumSections = 200;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private (WordDocument Model, EditStamp Stamp, int[] Pages, SectionsView View)? _memo;

    /// <summary>How many times the sections were really read (the memo answers are not counted; tests pin that typing asks for none).</summary>
    internal int Reads { get; private set; }

    /// <summary>Reads the sections with their first pages as the layout places them (<paramref name="firstPageOfSection"/>: -1 or a missing entry means that page is not laid out yet).</summary>
    public SectionsView Read(WordDocument document, IReadOnlyList<int> firstPageOfSection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(firstPageOfSection);
        return Read(document, SectionList.Of(document), firstPageOfSection);
    }

    /// <summary>Reads the sections and asks <paramref name="pageOfBlock"/> (the page index of a top-level block, -1 when it is not laid out) for the first block of each of them.</summary>
    public SectionsView Read(WordDocument document, Func<int, int> pageOfBlock)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pageOfBlock);
        var sections = SectionList.Of(document);
        var pages = sections.Count > MaximumSections ? [] : sections.Select(s => s.FirstBlock < document.Body.Blocks.Count ? pageOfBlock(s.FirstBlock) : -1).ToArray();
        return Read(document, sections, pages);
    }

    private SectionsView Read(WordDocument document, IReadOnlyList<SectionInfo> sections, IReadOnlyList<int> firstPages)
    {
        var stamp = document.Edits.Stamp;
        if (_memo is { } memo && ReferenceEquals(memo.Model, document) && memo.Stamp == stamp && memo.Pages.AsSpan().SequenceEqual(firstPages.ToArray()))
        {
            return memo.View;
        }

        Reads++;
        var leftRight = document.Settings?.EvenAndOddHeaders ?? false;
        SectionsView view;
        if (sections.Count > MaximumSections)
        {
            view = new SectionsView([], true, sections.Count, leftRight);
        }
        else
        {
            var blocks = document.Body.Blocks;
            var rows = new List<SectionRow>(sections.Count);
            foreach (var section in sections)
            {
                rows.Add(RowOf(section, sections, leftRight, section.Index < firstPages.Count ? firstPages[section.Index] : -1, blocks));
            }

            view = new SectionsView(rows, false, sections.Count, leftRight);
        }

        _memo = (document, stamp, [.. firstPages], view);
        return view;
    }

    /// <summary>The row of one section, read without the rest (the markers on the page read the few sections in view this way; the pane reads all of them through <see cref="Read(WordDocument, Func{int, int})"/>).</summary>
    internal static SectionRow RowFor(WordDocument document, IReadOnlyList<SectionInfo> sections, int index, int firstPage)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sections);
        return RowOf(sections[index], sections, document.Settings?.EvenAndOddHeaders ?? false, firstPage, document.Body.Blocks);
    }

    private static SectionRow RowOf(SectionInfo section, IReadOnlyList<SectionInfo> sections, bool leftRight, int firstPage, IList<Block> blocks)
    {
        var properties = section.Properties;
        var numbering = properties.Get(W + "pgNumType");
        var references = properties.HeaderFooterReferences;
        var empty = section.LastBlock < section.FirstBlock || section.FirstBlock >= blocks.Count;
        return new SectionRow(
            section.Index,
            properties,
            firstPage,
            int.TryParse(numbering?.Attribute(W + "start")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : null,
            FormatOf(numbering?.Attribute(W + "fmt")?.Value),
            properties.GetFlag(W + "titlePg"),
            section.Index == 0 || references.Any(r => r.Kind == HeaderFooterKind.Header && r.Type == HeaderFooterType.Default),
            section.Index == 0 || references.Any(r => r.Kind == HeaderFooterKind.Footer && r.Type == HeaderFooterType.Default),
            SectionPageSetup.Read(properties),
            empty,
            !empty && FirstParagraphOf(blocks[section.FirstBlock]) is not null,
            PartsOf(sections, section.Index, HeaderFooterKind.Header, leftRight),
            PartsOf(sections, section.Index, HeaderFooterKind.Footer, leftRight));
    }

    private static PartIds PartsOf(IReadOnlyList<SectionInfo> sections, int section, HeaderFooterKind kind, bool leftRight) => new(
        SectionList.Resolve(sections, section, kind, HeaderFooterType.Default).Reference?.RelationshipId,
        SectionList.Resolve(sections, section, kind, HeaderFooterType.First).Reference?.RelationshipId,
        leftRight ? SectionList.Resolve(sections, section, kind, HeaderFooterType.Even).Reference?.RelationshipId : null);

    /// <summary>The first paragraph of a body block (through content controls, tables, rows and cells), null when it holds none; the place the numbering command is told a section starts at.</summary>
    internal static Paragraph? FirstParagraphOf(Block block) => block switch
    {
        Paragraph paragraph => paragraph,
        ContentControlBlock control => control.Content.Select(FirstParagraphOf).FirstOrDefault(found => found is not null),
        Table table => table.Rows.Select(FirstParagraphIn).FirstOrDefault(found => found is not null),
        _ => null,
    };

    private static Paragraph? FirstParagraphIn(RowItem item) => item switch
    {
        TableRow row => row.Items.Select(FirstParagraphIn).FirstOrDefault(found => found is not null),
        TableCell cell => cell.Blocks.Select(FirstParagraphOf).FirstOrDefault(found => found is not null),
        _ => null,
    };

    private static PageNumberFormat? FormatOf(string? name) => name switch
    {
        "decimal" => PageNumberFormat.Arabic,
        "lowerRoman" => PageNumberFormat.LowerRoman,
        "upperRoman" => PageNumberFormat.UpperRoman,
        "lowerLetter" => PageNumberFormat.LowerLetter,
        "upperLetter" => PageNumberFormat.UpperLetter,
        _ => null,
    };
}
