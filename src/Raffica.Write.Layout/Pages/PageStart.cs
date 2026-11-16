using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>
/// Where a published page starts: the block (by reference) and the line of it that comes first (the first line of a paragraph piece, the first
/// row of a table piece; 0 for a block that starts on the page), and the section the page begins in. <see cref="Block"/> is null for a page
/// that starts with the blank page an even-page or odd-page section break inserts. Two starts are equal when they name the same block object and
/// line: an edit that replaces a block makes a new object, so a start of the old pages never equals one of the new.
/// </summary>
internal readonly record struct PageStart(Block? Block, int Line, int SectionIndex)
{
    /// <summary>True when both name the same block object, line and section.</summary>
    public bool SameAs(PageStart other) => ReferenceEquals(Block, other.Block) && Line == other.Line && SectionIndex == other.SectionIndex;

    public bool Equals(PageStart other) => SameAs(other);

    public override int GetHashCode() => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Block), Line, SectionIndex);
}
