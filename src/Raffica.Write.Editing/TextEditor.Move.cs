using Raffica.Write.Model;

namespace Raffica.Write.Editing;

public sealed partial class TextEditor
{
    /// <summary>
    /// Moves <paramref name="count"/> top-level blocks of the body from <paramref name="first"/> to <paramref name="target"/>, an index in the list as it is before the move (the range lands
    /// in front of the block that stands there now; the length of the list puts it last). The blocks are rotated in place, never copied: they keep their paragraph ids, bookmarks and comment
    /// anchors. One <see cref="BlockChange"/> over the span, the caret at the start of the first moved paragraph, one history step that stores indices only. Null (nothing changed) for
    /// an illegal range or a move to the range's own place. A paragraph that ends a section moves with its section break; this method does not look at sections.
    /// </summary>
    public EditResult? MoveBlocks(int first, int count, int target)
    {
        var revision = History.BeginOperation();
        if (StoryResolver.TryBlocks(_document, StoryRef.Body) is not { } body)
        {
            return null;
        }

        var move = new BlockMove(StoryRef.Body, first, count, target);
        if (!move.IsValidFor(body.Count) || !body.OfType<Paragraph>().Any())
        {
            return null;
        }

        var old = move.ApplyTo(body);
        _document.Edits.Raise();
        var result = move.ResultIn(body, old);
        _document.Edits.Attribute(result.Changes);
        History.Record(result, new EditHistory.Point(0, 0), revision, null);
        return result;
    }
}
