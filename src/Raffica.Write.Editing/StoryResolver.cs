using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Finds the story a <see cref="StoryRef"/> names in a document.</summary>
internal static class StoryResolver
{
    public static Story? TryStory(WordDocument document, StoryRef story) => story.Kind switch
    {
        StoryKind.Body => document.Body,
        StoryKind.HeaderFooter => story.Key is not null && document.HeadersFooters.TryGetValue(story.Key, out var part) ? part.Content : null,
        StoryKind.Note when story.Key is not null && story.Key.StartsWith("endnote:", StringComparison.Ordinal) =>
            int.TryParse(story.Key.AsSpan("endnote:".Length), out var endId) ? document.Endnotes?.Find(endId)?.Content : null,
        StoryKind.Note => int.TryParse(story.Key, out var noteId) ? document.Footnotes?.Find(noteId)?.Content : null,
        StoryKind.Comment => int.TryParse(story.Key, out var commentId) ? document.Comments?.Find(commentId)?.Content : null,
        _ => null,
    };

    public static IList<Block>? TryBlocks(WordDocument document, StoryRef story) => TryStory(document, story)?.Blocks;
}
