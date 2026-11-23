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
/// <param name="CanNumber">Whether the section has a first paragraph the numbering command can name (false for an empty final section).</param>
/// <param name="ReferenceKey">Every header and footer reference of the section as one string, to tell whether two sections show the same parts.</param>
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
    bool CanNumber,
    string ReferenceKey)
{
    /// <summary>Whether removing this section's marker would change something: the section takes over the page setup, numbering, first page switch and parts of the section before it, so any difference matters.</summary>
    public bool DiffersFrom(SectionRow previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return Setup != previous.Setup || NumberStart != previous.NumberStart || NumberFormat != previous.NumberFormat || FirstPageDifferent != previous.FirstPageDifferent || ReferenceKey != previous.ReferenceKey;
    }
}

/// <summary>What the pane shows: the rows (none when there are too many sections to list), how many sections the document has and the document-wide switch of left and right pages.</summary>
internal sealed record SectionsView(IReadOnlyList<SectionRow> Rows, bool TooMany, int SectionCount, bool LeftRight);

/// <summary>
/// Reads the sections of a document for the Sections pane, the way the formatting inspector reads a selection: on the UI thread, where the model is edited, remembered for the model, its revision and the page
/// list it was read with (the same question asks nothing again) and bounded (<see cref="MaximumSections"/>). Nothing exists and nothing is read before the first ask.
/// </summary>
internal sealed class SectionsSource
{
    /// <summary>The most sections the pane lists; a document with more shows one line that says so.</summary>
    internal const int MaximumSections = 200;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private (WordDocument Model, int Revision, int[] Pages, SectionsView View)? _memo;

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
        var revision = document.Edits.Revision;
        if (_memo is { } memo && ReferenceEquals(memo.Model, document) && memo.Revision == revision && memo.Pages.AsSpan().SequenceEqual(firstPages.ToArray()))
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
                rows.Add(RowOf(section, i => i < firstPages.Count ? firstPages[i] : -1, blocks));
            }

            view = new SectionsView(rows, false, sections.Count, leftRight);
        }

        _memo = (document, revision, [.. firstPages], view);
        return view;
    }

    private static SectionRow RowOf(SectionInfo section, Func<int, int> pageOf, IList<Block> blocks)
    {
        var properties = section.Properties;
        var numbering = properties.Get(W + "pgNumType");
        var references = properties.HeaderFooterReferences;
        return new SectionRow(
            section.Index,
            properties,
            pageOf(section.Index),
            int.TryParse(numbering?.Attribute(W + "start")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : null,
            FormatOf(numbering?.Attribute(W + "fmt")?.Value),
            properties.GetFlag(W + "titlePg"),
            section.Index == 0 || references.Any(r => r.Kind == HeaderFooterKind.Header && r.Type == HeaderFooterType.Default),
            section.Index == 0 || references.Any(r => r.Kind == HeaderFooterKind.Footer && r.Type == HeaderFooterType.Default),
            SectionPageSetup.Read(properties),
            section.FirstBlock < blocks.Count,
            string.Join(';', references.Select(r => string.Create(CultureInfo.InvariantCulture, $"{(int)r.Kind}:{(int)r.Type}:{r.RelationshipId}")).Order(StringComparer.Ordinal)));
    }

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
