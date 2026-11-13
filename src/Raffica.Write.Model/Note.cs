namespace Raffica.Write.Model;

/// <summary>The kind of a footnote or endnote (<c>w:type</c>). Only the three non-normal spellings are typed; an absent, <c>normal</c> or unknown <c>w:type</c> reads as <see cref="Normal"/> and an unknown one stays in <see cref="OoxmlElement.UnknownAttributes"/>.</summary>
public enum NoteKind
{
    Normal,
    Separator,
    ContinuationSeparator,
    ContinuationNotice,
}

/// <summary>One <c>w:footnote</c> or <c>w:endnote</c>: its id, its kind and its blocks. The separators of a file (ids -1 and 0 in Word files) are notes too.</summary>
public sealed class Note : OrderedElement
{
    public Note(int id, NoteKind kind, Story content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Id = id;
        Kind = kind;
        Content = content;
    }

    public int Id { get; set; }

    public NoteKind Kind { get; set; }

    /// <summary>The blocks of the note. The story's root is only a name here (<c>w:footnote</c> or <c>w:endnote</c>); the attributes of the note element are on this note.</summary>
    public Story Content { get; }
}
