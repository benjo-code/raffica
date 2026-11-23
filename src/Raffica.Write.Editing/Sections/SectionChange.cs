using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// One section's properties as a step swapped them: <paramref name="Live"/> is the object in the document (it stays where it is; undo and redo copy content into it),
/// <paramref name="Old"/> and <paramref name="New"/> are read-only clones of its content before and after. Callers must not change the clones.
/// </summary>
public sealed record SectionSwap(SectionProperties Live, SectionProperties Old, SectionProperties New);

/// <summary>
/// A change of section properties, as the history keeps it: every swap and the flag of left and right pages (<c>w:evenAndOddHeaders</c> of the settings part) before and after (null when
/// the step did not touch it). Undo and redo apply all swaps and the flag, or none. A layout built before such a step is stale: its owner starts it over (<see cref="EditResult.Sections"/>).
/// </summary>
public sealed record SectionChange(IReadOnlyList<SectionSwap> Swaps, bool? EvenAndOddBefore, bool? EvenAndOddAfter)
{
    /// <summary>
    /// The block changes to attribute (<see cref="DocumentEdits.Attribute"/>) besides the ones of the operation: a section break lives in the paragraph that carries it, so a swap of its properties
    /// changes that paragraph in place, which the recovery snapshot's block cache must hear of (the final section and the settings part are written afresh at every copy and need nothing).
    /// A carrier that is not a top-level block (inside a content control) is reported as a change on a throwaway list: the tracker takes any change on a container other than the body as "a nested block list changed"
    /// and then writes again every top-level block that is not a paragraph, which is the (coarse, never stale) answer for it. Use <see cref="EditResult.Attribute"/>, which makes the one attribution call.
    /// </summary>
    public IReadOnlyList<BlockChange> CarrierChanges(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var changes = new List<BlockChange>();
        var swapped = new HashSet<SectionProperties>(Swaps.Select(swap => swap.Live), ReferenceEqualityComparer.Instance);
        var blocks = document.Body.Blocks;
        foreach (var section in SectionList.Of(document))
        {
            if (section.Break is not { } carrier || !swapped.Contains(section.Properties))
            {
                continue;
            }

            var index = -1;
            for (var i = 0; i < blocks.Count; i++)
            {
                if (ReferenceEquals(blocks[i], carrier))
                {
                    index = i;
                    break;
                }
            }

            changes.Add(index >= 0
                ? new BlockChange(StoryRef.Body, blocks, index, 1, 1, [carrier])
                : new BlockChange(StoryRef.Body, new List<Block>(), 0, 0, 0, []));
        }

        return changes;
    }
}
