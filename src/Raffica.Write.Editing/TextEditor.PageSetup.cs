using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// Writes the named parts of a page setup into the section of <paramref name="at"/> (<see cref="ApplyScope.ThisSection"/>), into every section including the final one (<see cref="ApplyScope.WholeDocument"/>),
    /// or from the caret to the end of the document (<see cref="ApplyScope.FromHere"/>): a section starts before the caret's top-level block (next page, continuous when <see cref="PageSetupChange.Parts"/> is
    /// <see cref="PageSetupParts.Columns"/> alone; none when the block already starts its section) and it and every later section get only the changed parts, as "this point forward" does in Word (a later section
    /// that set its own value gets the new one). An orientation change turns each section's own page (width and height swapped), so a later section of another size keeps its size. Header and footer distances and
    /// the gutter are never written. One history step. Null (and nothing changed) when the place is not in the body or every target already has the values. A prefix document is refused by the caller
    /// (<see cref="ReadSectionFacts"/>); a stale position throws before any change.
    /// </summary>
    public EditResult? ApplyPageSetup(TextPosition at, PageSetupChange change, ApplyScope scope)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (at.Paragraph is null || at.Story.Kind != StoryKind.Body || change.Parts == PageSetupParts.None)
        {
            return null;
        }

        var index = TopLevelIndexOf(at.Paragraph) ?? throw new StalePositionException("The paragraph is not in the body.");
        var sections = SectionList.Of(_document);
        var section = SectionSplitter.SectionIndexOf(sections, index);
        var split = scope == ApplyScope.FromHere && sections[section].FirstBlock < index;
        var first = scope == ApplyScope.WholeDocument ? 0 : section;
        var last = scope == ApplyScope.ThisSection ? section : sections.Count - 1;

        // Nothing to do when every target already has the values: tried on clones, so no split is made for it.
        var changes = false;
        for (var i = first; i <= last && !changes; i++)
        {
            var missing = sections[i].Break is null && _document.Body.FinalSection is null;
            var probe = missing ? new SectionProperties() : sections[i].Properties.Clone();
            WritePageSetup(probe, change);
            changes = missing || !SectionSplitter.SameContent(sections[i].Properties, probe);
        }

        if (!changes)
        {
            return null;
        }

        var before = split ? BeforePoint(at, index) : (SectionCaret(at) ?? throw new StalePositionException("The paragraph is not in the body.")).Before;
        var revision = History.BeginOperation();
        var swaps = new List<(SectionProperties Live, SectionProperties Old)>();
        SectionSplit? made = null;
        if (split)
        {
            made = SectionSplitter.Split(_document, index, change.Parts == PageSetupParts.Columns ? SectionBreakKind.Continuous : SectionBreakKind.NextPage)!;
            swaps.Add((made.Later, made.OldLater));
            WritePageSetup(made.Later, change);
            first = section + 1;
        }

        for (var i = first; i <= last; i++)
        {
            var live = SectionSplitter.Live(_document, sections[i]);
            if (!swaps.Any(entry => ReferenceEquals(entry.Live, live)))
            {
                swaps.Add((live, live.Clone()));
                WritePageSetup(live, change);
            }
        }

        var sectionChange = new SectionChange(
            [.. swaps.Select(entry => new SectionSwap(entry.Live, entry.Old, entry.Live.Clone()))],
            null,
            null,
            null,
            made is null ? null : [made.CarrierProperties]);
        var caret = made is null ? at : CaretAfterSplit(at, made);
        return CommitSections(revision, sectionChange, made?.Change, caret, before);
    }

    // An orientation change alone turns the section's own page; everything else writes the value's parts.
    private static void WritePageSetup(SectionProperties properties, PageSetupChange change)
    {
        if ((change.Parts & PageSetupParts.Size) == 0 && (change.Parts & PageSetupParts.Orientation) != 0)
        {
            SectionPageSetup.Read(properties).WithLandscape(change.Value.Landscape).ApplyTo(properties, PageSetupParts.Orientation);
            change.Value.ApplyTo(properties, change.Parts & ~PageSetupParts.Orientation);
            return;
        }

        change.Value.ApplyTo(properties, change.Parts);
    }
}
