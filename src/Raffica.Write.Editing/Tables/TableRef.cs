using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>
/// Names one table of the body: <paramref name="BlockIndex"/> is the index of the TOP-LEVEL body block that is the table or, for a table inside a block-level content control
/// (nested controls included), of the control that holds it: the same number as the layout's <c>TableGrid.BlockIndex</c>. A story other than the body has no table reference.
/// Like a text position it names a place and not an object: an edit that adds or removes a block moves it, so a holder asks again after every edit (<see cref="Find"/> finds
/// nothing for a place that no longer holds a table).
/// </summary>
public sealed record TableRef(StoryRef Story, int BlockIndex)
{
    /// <summary>
    /// The table this reference names in the body's blocks, or null when the place holds none, the story is not the body, or a content control holds more than one table (the
    /// reference cannot tell them apart; the first of them would be a guess).
    /// </summary>
    public Table? Find(IList<Block> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (Story != StoryRef.Body || BlockIndex < 0 || BlockIndex >= body.Count)
        {
            return null;
        }

        if (body[BlockIndex] is Table direct)
        {
            return direct;
        }

        var found = new List<Table>(2);
        Collect(body[BlockIndex], found, 0);
        return found.Count == 1 ? found[0] : null;
    }

    private static void Collect(Block block, List<Table> found, int depth)
    {
        if (depth > 32 || found.Count > 1)
        {
            return;
        }

        switch (block)
        {
            case Table table:
                found.Add(table);
                break;
            case ContentControlBlock control:
                foreach (var inner in control.Content)
                {
                    Collect(inner, found, depth + 1);
                }

                break;
            default:
                break;
        }
    }
}
