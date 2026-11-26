using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// A structural change a field refresh made to the document (a table of contents gained or lost paragraphs). A refresh is not an edit and has no undo step of its own; when it changes the number
/// of blocks of a list, the indices of every older step would be wrong, so the change is attached to the newest step of the history (<see cref="EditHistory.AttachDerived"/>): undo reverts it before
/// the step's own blocks and redo replays it after them.
/// </summary>
/// <param name="Changes">
/// The block changes the refresh made, in the order it made them, each as <see cref="BlockChange"/> describes it: <see cref="BlockChange.OldBlocks"/> are the removed blocks (cloned when attached), the added
/// range is read from the live <see cref="BlockChange.Container"/> when the record is attached, so the refresh must be finished and no later change may shift the range of an earlier one (make them from the
/// end of a list to its start).
/// </param>
public sealed record DerivedRecord(IReadOnlyList<BlockChange> Changes);
