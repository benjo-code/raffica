using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>What the page setup card hands back: the typed values with the parts to write, and where they apply.</summary>
internal sealed record PageSetupRequest(PageSetupChange Change, ApplyScope Scope);

/// <summary>
/// The two cards of the Layout tab, which the window owns: Number pages from here and Page setup. A command asks its card and runs the answer (null: the user cancelled). The window starts with
/// <see cref="PendingSectionCards"/>; the cards themselves are made by the step that adds them (milestone 4.4b, Task 12) and replace it through <c>WriteWindow.SectionCards</c>.
/// </summary>
internal interface ISectionCards
{
    /// <summary>Whether Page setup has a card to open (false: the window says so in a note and changes nothing).</summary>
    bool HasPageSetupCard { get; }

    /// <summary>Asks how the pages are numbered from the caret's place (start, format, prefix); null when cancelled.</summary>
    Task<PageNumbering?> AskNumberingAsync(SectionFacts facts);

    /// <summary>Asks for the page setup of the caret's section and where it applies; null when cancelled.</summary>
    Task<PageSetupRequest?> AskPageSetupAsync(SectionPageSetup current, ApplyScope defaultScope, int sectionCount);
}

/// <summary>
/// The cards until Task 12 makes them: Number pages from here answers with its one-click default (start at 1, the format 1 2 3, no prefix, the default of the picker of the spec) and Page setup
/// has no card, so the window says "not available yet" instead of pretending. Replaced by the real cards.
/// </summary>
internal sealed class PendingSectionCards : ISectionCards
{
    /// <inheritdoc/>
    public bool HasPageSetupCard => false;

    /// <inheritdoc/>
    public Task<PageNumbering?> AskNumberingAsync(SectionFacts facts) => Task.FromResult<PageNumbering?>(new PageNumbering(1, PageNumberFormat.Arabic, string.Empty));

    /// <inheritdoc/>
    public Task<PageSetupRequest?> AskPageSetupAsync(SectionPageSetup current, ApplyScope defaultScope, int sectionCount) => Task.FromResult<PageSetupRequest?>(null);
}
