using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>How a page begins: at the first block of a section (a new section starts a new page), or with a piece that the page before could not hold.</summary>
internal enum PageStartKind
{
    Section,
    Carry,
}

/// <summary>
/// Everything the page filler needs to go on from the start of one page, and nothing it does not: the page's <see cref="PageStart"/>, the piece
/// carried onto it, the counters of the filler (page index, next page number, pages per section, the sections that started a page), the
/// numbering and note state of the planner and the notes that run over from the page before. It is taken when the page before is finished
/// (so a paused layout always has the state of its next page) and never changes afterwards.
/// <para>
/// The planner state is the one before the first block the resumed filler plans: for a <see cref="PageStartKind.Carry"/> start that is the
/// state after the carried block (the blocks after it that the filler had looked at ahead are planned again, from the cache). Two states of
/// equal <see cref="PageStart"/> and equal state (<see cref="SameAs"/>) mean that everything laid out from there on is the same.
/// </para>
/// </summary>
internal sealed class FillerState
{
    public FillerState(PageStartKind kind, PageStart start, IBlockFlow? carry, bool suppressSpaceBefore, int pageIndex, int nextNumber, int[] sectionPages, int[] startedSections, NumberingState numbers, NoteState? notes, object? chromeCarry)
    {
        Kind = kind;
        Start = start;
        Carry = carry;
        SuppressSpaceBefore = suppressSpaceBefore;
        PageIndex = pageIndex;
        NextNumber = nextNumber;
        SectionPages = sectionPages;
        StartedSections = startedSections;
        Numbers = numbers;
        Notes = notes;
        ChromeCarry = chromeCarry;
    }

    public PageStartKind Kind { get; }

    public PageStart Start { get; }

    /// <summary>The piece that goes first on the page (a tail of a paragraph or table, or a block that did not fit the page before); null for a <see cref="PageStartKind.Section"/> start.</summary>
    public IBlockFlow? Carry { get; }

    /// <summary>The space above the carried piece is dropped, as it is for a block that overflowed to the top of a page.</summary>
    public bool SuppressSpaceBefore { get; }

    /// <summary>The index of the page that begins here.</summary>
    public int PageIndex { get; }

    /// <summary>The number the page after the blank page that a section may need gets (the next page number when the page begins).</summary>
    public int NextNumber { get; }

    /// <summary>The pages counted per section so far (a section that continues on a page counts that page too).</summary>
    public int[] SectionPages { get; }

    /// <summary>The sections that have started a page so far, ascending (a section's first page is the one that <c>w:titlePg</c> gives its own header and footer).</summary>
    public int[] StartedSections { get; }

    public NumberingState Numbers { get; }

    /// <summary>Null in the writing view (no notes).</summary>
    public NoteState? Notes { get; }

    /// <summary>The notes that run over from the page before (the chrome's own object), or null when there are none.</summary>
    public object? ChromeCarry { get; }

    /// <summary>
    /// True when a filler started from this state lays out exactly what a filler that had reached the start of the page by laying out
    /// everything before it would. The carried piece must be the same piece, so its layout must be a function of the same plan.
    /// </summary>
    public bool SameAs(FillerState? other)
    {
        if (other is null)
        {
            return false;
        }

        return Kind == other.Kind
            && Start.SameAs(other.Start)
            && SuppressSpaceBefore == other.SuppressSpaceBefore
            && PageIndex == other.PageIndex
            && NextNumber == other.NextNumber
            && (Carry is null ? other.Carry is null : Carry.SamePieceAs(other.Carry))
            && SectionPages.AsSpan().SequenceEqual(other.SectionPages)
            && StartedSections.AsSpan().SequenceEqual(other.StartedSections)
            && Numbers.SameAs(other.Numbers)
            && (Notes is null ? other.Notes is null : Notes.SameAs(other.Notes))
            && SameCarry(ChromeCarry, other.ChromeCarry);
    }

    // The notes that run over are compared by the objects (no carry equals no carry); a page with a carry never matches another run's, which only makes the refill go on.
    private static bool SameCarry(object? a, object? b) => ReferenceEquals(a, b);
}
