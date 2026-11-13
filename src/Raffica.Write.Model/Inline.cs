namespace Raffica.Write.Model;

/// <summary>An item inside a paragraph (run, hyperlink, field, bookmark, tracked change, ...).</summary>
public abstract class Inline : OoxmlElement
{
}

/// <summary>An inline element the model does not understand, kept as it was read.</summary>
public sealed class UnknownInline : Inline
{
    public UnknownInline(UnknownContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    public UnknownContent Content { get; }
}

/// <summary>A <c>w:hyperlink</c>: an external target through a relationship id, or an anchor (bookmark) in the document.</summary>
public sealed class Hyperlink : Inline
{
    public string? RelationshipId { get; set; }

    public string? Anchor { get; set; }

    public IList<Inline> Inlines { get; } = new List<Inline>();
}

/// <summary>A <c>w:fldSimple</c>: a field whose instruction is an attribute and whose result is the content.</summary>
public sealed class SimpleField : Inline
{
    public SimpleField(string instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        Instruction = instruction;
    }

    public string Instruction { get; set; }

    public IList<Inline> Inlines { get; } = new List<Inline>();
}

public sealed class BookmarkStart : Inline
{
    public BookmarkStart(int id, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Id = id;
        Name = name;
    }

    public int Id { get; set; }

    public string Name { get; set; }
}

public sealed class BookmarkEnd : Inline
{
    public BookmarkEnd(int id)
    {
        Id = id;
    }

    public int Id { get; set; }
}

public sealed class CommentRangeStart : Inline
{
    public CommentRangeStart(int id)
    {
        Id = id;
    }

    public int Id { get; set; }
}

public sealed class CommentRangeEnd : Inline
{
    public CommentRangeEnd(int id)
    {
        Id = id;
    }

    public int Id { get; set; }
}
