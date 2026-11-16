namespace Raffica.Write.Model;

/// <summary>
/// A place in a story: a paragraph (an object reference) and an offset into that paragraph's <see cref="ParagraphProjection"/>.
/// Positions are not stored across edits except by the editing core, which rewrites them.
/// </summary>
public readonly record struct TextPosition(StoryRef Story, Paragraph Paragraph, int Offset);

/// <summary>A selection: two positions in one story (inside a table, in one cell). The end may come before the start.</summary>
public readonly record struct TextRange(TextPosition Start, TextPosition End)
{
    public bool IsEmpty => ReferenceEquals(Start.Paragraph, End.Paragraph) && Start.Offset == End.Offset && Start.Story == End.Story;

    /// <summary>The range with start before end when both lie in one paragraph; a range across paragraphs is returned as it is (the caller knows the block order).</summary>
    public TextRange Normalized() =>
        ReferenceEquals(Start.Paragraph, End.Paragraph) && Start.Offset > End.Offset ? new TextRange(End, Start) : this;
}
