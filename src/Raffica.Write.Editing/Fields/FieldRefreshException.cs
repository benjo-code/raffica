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
}
