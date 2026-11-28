using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// A field refresh failed part-way. The walk writes results in place, so the document may already have changed: <see cref="Content"/> are the block changes of what was written before the failure (the same
/// changes a finished refresh returns, already told to the document as derived changes), and the owner must bring its layout up to date with them before it retries. The inner exception is the failure; the
/// message never names text of the document.
/// </summary>
public sealed class FieldRefreshException(IReadOnlyList<BlockChange> content, Exception inner) : Exception("A field refresh failed.", inner)
{
    /// <summary>What was written before the failure (empty when nothing was).</summary>
    public IReadOnlyList<BlockChange> Content { get; } = content;

    /// <summary>The structural change of a table of contents that was made before the failure (it is among <see cref="Content"/> too, and the owner still attaches it to the history), or null.</summary>
    public DerivedRecord? Structural { get; init; }

    /// <summary>True when entry styles were added to the style sheet before the failure: the owner starts its layout over (<see cref="FieldRefreshResult.StylesAdded"/>).</summary>
    public bool StylesAdded { get; init; }
}
