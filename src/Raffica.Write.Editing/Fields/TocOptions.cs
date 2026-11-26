namespace Raffica.Write.Editing;

/// <summary>The line that leads the eye from an entry of a table of contents to its page number (the leader of the right-aligned tab stop).</summary>
public enum TocLeader
{
    /// <summary>A row of dots (Word's default).</summary>
    Dots,

    /// <summary>A row of hyphens.</summary>
    Hyphens,

    /// <summary>A solid underline.</summary>
    Underscore,

    /// <summary>No leader.</summary>
    None,
}

/// <summary>
/// What a new table of contents, list of figures or list of tables is made of. <paramref name="FirstLevel"/> and <paramref name="LastLevel"/> (1 to 9, first not above last) choose the heading levels of a table of contents
/// (<c>\o "a-b"</c>, with the outline levels, <c>\u</c>); <paramref name="Label"/> is null for a table of contents and the caption label (a sequence identifier) for a list of figures or tables (<c>\c "label"</c>, the levels
/// are then not used). <paramref name="PageNumbers"/> off writes <c>\n</c>; <paramref name="RightAlign"/> puts the page numbers at a right tab stop with <paramref name="Leader"/> (off: the number follows after a plain tab).
/// </summary>
public sealed record TocOptions(int FirstLevel, int LastLevel, bool PageNumbers, bool RightAlign, TocLeader Leader, string? Label);

/// <summary>Why a table of contents cannot be inserted or removed. The caller words it for the user in the language of the interface.</summary>
public enum TocRefusal
{
    /// <summary>The place is not in the body (a header, a footer, a note or a comment), is not in the document, or is in a content control that holds one line.</summary>
    NotInBody,

    /// <summary>The place is in a table of contents, which updates itself (remove it first).</summary>
    InsideToc,

    /// <summary>The document is opened as a prefix (the first blocks only): the editor sees a part of the body and the file cannot be saved.</summary>
    Prefix,

    /// <summary>The first level is above the last, or a level is outside 1 to 9.</summary>
    LevelRange,

    /// <summary>The label of a list of figures or tables is not a sequence identifier the document can use.</summary>
    LabelNotAllowed,

    /// <summary>There is no table of contents at the place (remove), or it cannot be removed whole because one of its paragraphs ends a section.</summary>
    NothingToRemove,
}
