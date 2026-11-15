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
/// Names one story of a document. <paramref name="Key"/> is, by kind: for a footnote the note id (<c>"5"</c>); for an endnote the note id
/// with the prefix <c>"endnote:"</c> (<c>"endnote:5"</c>, so a footnote and an endnote with the same id differ); for a header or footer the
/// document's relationship id of its part; for a comment the comment id; null for the body.
/// </summary>
public readonly record struct StoryRef(StoryKind Kind, string? Key)
{
    public static StoryRef Body { get; } = new(StoryKind.Body, null);
}
