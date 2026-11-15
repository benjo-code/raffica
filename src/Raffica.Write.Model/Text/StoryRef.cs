namespace Raffica.Write.Model;

/// <summary>The kind of story a position lives in.</summary>
public enum StoryKind
{
    Body,
    Note,
    HeaderFooter,
    Comment,
}

/// <summary>
/// Names one story of a document. <paramref name="Key"/> is the note id, the document's relationship id of the header or footer,
/// or the comment id; it is null for the body.
/// </summary>
public readonly record struct StoryRef(StoryKind Kind, string? Key)
{
    public static StoryRef Body { get; } = new(StoryKind.Body, null);
}
