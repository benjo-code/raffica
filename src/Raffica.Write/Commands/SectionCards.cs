using Raffica.Hosting;
using Raffica.Write.Dialogs;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>What the page setup card hands back: the typed values with the parts to write, and where they apply.</summary>
internal sealed record PageSetupRequest(PageSetupChange Change, ApplyScope Scope);

/// <summary>
/// What the Number pages card knows of the caret's place: the facts of the section commands and whether the command starts a new page there (the caret's block is not the first block of its page, so the section
/// that begins before it moves it to a page of its own; false at the start of a section and when the layout cannot say).
/// </summary>
internal sealed record NumberPagesContext(SectionFacts Facts, bool StartsNewPage);

/// <summary>
/// What the Page setup card knows of the caret's section: its setup, where the command applies when the card opens (the whole document while it has one section, else this section), the number of sections,
/// whether its columns have different widths (a write makes columns equal, so the card offers no spacing or line for them), whether lengths are in inches (the region rule of <c>PagePresets.UsesInches</c>), the caret's
/// section and which sections have custom column widths (the card warns when the chosen scope reaches any of them).
/// </summary>
internal sealed record PageSetupContext(SectionPageSetup Current, ApplyScope DefaultScope, int SectionCount, bool ColumnsUnequal, bool Inches, int CaretSection = 0, IReadOnlyList<int>? UnequalSections = null)
{
    /// <summary>The sections (from 0) whose columns have different widths; without a list, the caret's section when <see cref="ColumnsUnequal"/> says so.</summary>
    public IReadOnlyList<int> AllUnequal => UnequalSections ?? (ColumnsUnequal ? [CaretSection] : []);

    /// <summary>How many sections a column write at <paramref name="scope"/> would reach that have custom column widths (a write makes the columns of every section it reaches equal).</summary>
    public int UnequalTargets(ApplyScope scope) => AllUnequal.Count(i => scope switch
    {
        ApplyScope.ThisSection => i == CaretSection,
        ApplyScope.FromHere => i >= CaretSection,
        _ => true,
    });
}

/// <summary>
/// The two cards of the Layout tab, which the window owns: Number pages from here and Page setup. A command asks its card and runs the answer (null: the user cancelled or changed nothing). The window uses
/// <see cref="DialogSectionCards"/>; a test puts its own in.
/// </summary>
internal interface ISectionCards
{
    /// <summary>Asks how the pages are numbered from the caret's place (start, format, prefix); null when cancelled.</summary>
    Task<PageNumbering?> AskNumberingAsync(NumberPagesContext context);

    /// <summary>Asks for the page setup of the caret's section and where it applies; null when cancelled or when nothing was changed.</summary>
    Task<PageSetupRequest?> AskPageSetupAsync(PageSetupContext context);
}

/// <summary>The real cards, shown one at a time through the window's <see cref="DialogQueue"/>.</summary>
internal sealed class DialogSectionCards(DialogQueue dialogs) : ISectionCards
{
    /// <inheritdoc/>
    public Task<PageNumbering?> AskNumberingAsync(NumberPagesContext context) =>
        dialogs.ShowAsync<PageNumbering?>(done => NumberPagesCard.Create(context, done));

    /// <inheritdoc/>
    public Task<PageSetupRequest?> AskPageSetupAsync(PageSetupContext context) =>
        dialogs.ShowAsync<PageSetupRequest?>(done => PageSetupCard.Create(context, done));
}
