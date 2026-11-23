using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using DocumentLayout = Raffica.Write.Layout.DocumentLayout;

namespace Raffica.Write.Viewing;

/// <summary>
/// One section marker as the page view draws it: the section it names (index and the model's own properties object, so a marker whose section has gone is recognised when it acts), the page the layout
/// starts the section on, the place of its first block on that page (points from the top of the page), the words of the chip and the right margin the chip must stay out of (points).
/// </summary>
/// <param name="Section">The index of the section, from 1 (the first section has no marker).</param>
/// <param name="Identity">The section's properties object in the model.</param>
/// <param name="Page">The page index (from 0) the section's first block is on.</param>
/// <param name="Y">The top of the section's first block, in points from the top of the page.</param>
/// <param name="MarginPoints">The smaller of the right margins of this section and the one before it (points): the room the chip has inside the page.</param>
/// <param name="Title">"Section 2".</param>
/// <param name="Numbers">The short line: "numbers start at 3" or "continues".</param>
/// <param name="Name">The name for assistive technology: the title and the longer numbering wording.</param>
/// <param name="Help">What the chip does, for assistive technology.</param>
internal sealed record SectionMarker(int Section, SectionProperties Identity, int Page, float Y, double MarginPoints, string Title, string Numbers, string Name, string Help);

/// <summary>
/// Finds the section markers the page view shows: the chips of the sections after the first whose first block lies on a page in view. The marker is an overlay of the view and never part of a
/// <see cref="LaidOutPage"/>, so print and PDF export never see it. The sections are read once for a model and revision (the one read the whole computation costs), after which a frame is a binary search over
/// them by the page the layout has put each on (<see cref="DocumentLayout.Locate"/>, one lookup each) and the few markers in view are made with their words; nothing is allocated for the sections out of view.
/// Nothing exists before the first ask. UI thread only (the model is edited there).
/// </summary>
internal sealed class SectionMarkers
{
    private (WordDocument Model, int Revision, IReadOnlyList<SectionInfo> Sections)? _memo;

    /// <summary>How many times the sections of the document were read (tests: typing and scrolling ask for few).</summary>
    internal int Reads { get; private set; }

    /// <summary>How many markers were made with their words (tests: a frame makes the few in view, never one for every section).</summary>
    internal int MarkersMade { get; private set; }

    /// <summary>Whether the sections held are those of the model as it is now (a frame may then place markers without reading anything).</summary>
    public bool IsCurrent(WordDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _memo is { } memo && ReferenceEquals(memo.Model, model) && memo.Revision == model.Edits.Revision;
    }

    /// <summary>The sections of the model, read again only when the model or its revision is not the one last read.</summary>
    public IReadOnlyList<SectionInfo> Sections(WordDocument model)
    {
        if (IsCurrent(model))
        {
            return _memo!.Value.Sections;
        }

        Reads++;
        var sections = SectionList.Of(model);
        _memo = (model, model.Edits.Revision, sections);
        return sections;
    }

    /// <summary>The sections the last read found (0 before any); for the view's decision whether any chip can exist.</summary>
    public int SectionCount => _memo?.Sections.Count ?? 0;

    /// <summary>
    /// The markers of the sections whose first block is on a page from <paramref name="firstPage"/> to <paramref name="lastPage"/>, in order. A section whose page is not laid out yet has none (a chip never stands
    /// for a page that is not there) and neither has the empty final section a break at the end of the body leaves. The pages of the first blocks never decrease, so the first marker in view is found by
    /// halving; a section whose first block takes no room (the layout gives it no page) counts as not laid out, which is the only way the search can miss a marker.
    /// </summary>
    public IReadOnlyList<SectionMarker> InView(WordDocument model, DocumentLayout layout, int firstPage, int lastPage)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(layout);
        var sections = Sections(model);
        if (sections.Count < 2 || lastPage < firstPage)
        {
            return [];
        }

        var blocks = model.Body.Blocks.Count;
        var lo = 1;
        var hi = sections.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (PageOf(layout, sections[mid], blocks) >= firstPage)
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        List<SectionMarker>? found = null;
        for (var k = lo; k < sections.Count; k++)
        {
            var page = PageOf(layout, sections[k], blocks);
            if (page == int.MaxValue || page > lastPage)
            {
                break;
            }

            if (layout.Locate(sections[k].FirstBlock) is not { } place)
            {
                continue;
            }

            found ??= [];
            found.Add(Make(model, sections, k, place.Page, place.Y));
        }

        return found ?? [];
    }

    private static int PageOf(DocumentLayout layout, SectionInfo section, int blockCount) =>
        section.LastBlock >= section.FirstBlock && section.FirstBlock < blockCount && layout.Locate(section.FirstBlock) is { } place ? place.Page : int.MaxValue;

    private SectionMarker Make(WordDocument model, IReadOnlyList<SectionInfo> sections, int index, int page, float y)
    {
        MarkersMade++;
        var row = SectionsSource.RowFor(model, sections, index, page);
        var previous = SectionPageSetup.Read(sections[index - 1].Properties);
        var title = SectionsRowWording.Title(row);
        var numbers = row.NumberStart is { } start
            ? WriteStrings.Fill(WriteStrings.SectionChipNumbersStartFormat, start.ToString(WriteStrings.Culture))
            : WriteStrings.SectionChipNumbersContinue;
        return new SectionMarker(
            index,
            row.Identity,
            page,
            y,
            Math.Min(row.Setup.RightTwips, previous.RightTwips) / 20.0,
            title,
            numbers,
            WriteStrings.Fill(WriteStrings.SectionChipNameFormat, title, SectionsRowWording.Numbers(row)),
            WriteStrings.Fill(WriteStrings.SectionChipHelpFormat, index + 1));
    }
}
