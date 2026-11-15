namespace Raffica.Write.Model;

/// <summary>
/// One edit's effect on a list of blocks: the blocks <c>[First, First + Removed)</c> were replaced by <paramref name="Added"/> new ones.
/// <paramref name="OldBlocks"/> are clones of the removed blocks taken before the edit (the undo snapshot);
/// <paramref name="Container"/> is the live list the edit changed.
/// </summary>
public sealed record BlockChange(StoryRef Story, IList<Block> Container, int First, int Removed, int Added, IReadOnlyList<Block> OldBlocks);
