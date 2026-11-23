using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Where a page setup applies.</summary>
public enum ApplyScope
{
    /// <summary>The section that holds the caret.</summary>
    ThisSection,

    /// <summary>Every section, the final one included.</summary>
    WholeDocument,

    /// <summary>A new section starts at the caret (none when the caret's section already has the values) and it and every later section take the changed parts.</summary>
    FromHere,
}

/// <summary>A page setup: the typed values and which parts of them are written.</summary>
public sealed record PageSetupChange(SectionPageSetup Value, PageSetupParts Parts);
