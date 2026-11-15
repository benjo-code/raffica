namespace Raffica.Write.Model;

/// <summary>An item inside a paragraph (run, hyperlink, field, bookmark, tracked change, ...).</summary>
public abstract class Inline : OoxmlElement
{
    /// <summary>A deep copy that writes the same XML; <see cref="UnknownContent"/> is shared.</summary>
    public abstract Inline Clone();

    /// <summary>Clones a list of inlines into <paramref name="target"/>.</summary>
    protected static void CloneInto(IList<Inline> source, IList<Inline> target)
    {
        foreach (var inline in source)
        {
            target.Add(inline.Clone());
        }
    }
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

    public override UnknownInline Clone()
    {
        var copy = new UnknownInline(Content);
        CopyAttributesTo(copy);
        return copy;
    }
}

/// <summary>A <c>w:hyperlink</c>: an external target through a relationship id, or an anchor (bookmark) in the document.</summary>
public sealed class Hyperlink : Inline
{
    public string? RelationshipId { get; set; }

    public string? Anchor { get; set; }

    public IList<Inline> Inlines { get; } = new List<Inline>();

    public override Hyperlink Clone()
    {
        var copy = new Hyperlink { RelationshipId = RelationshipId, Anchor = Anchor };
        CopyAttributesTo(copy);
        CloneInto(Inlines, copy.Inlines);
        return copy;
    }
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

    public override SimpleField Clone()
    {
        var copy = new SimpleField(Instruction);
        CopyAttributesTo(copy);
        CloneInto(Inlines, copy.Inlines);
        return copy;
    }
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

    public override BookmarkStart Clone()
    {
        var copy = new BookmarkStart(Id, Name);
        CopyAttributesTo(copy);
        return copy;
    }
}

public sealed class BookmarkEnd : Inline
{
    public BookmarkEnd(int id)
    {
        Id = id;
    }

    public int Id { get; set; }

    public override BookmarkEnd Clone()
    {
        var copy = new BookmarkEnd(Id);
        CopyAttributesTo(copy);
        return copy;
    }
}

public sealed class CommentRangeStart : Inline
{
    public CommentRangeStart(int id)
    {
        Id = id;
    }

    public int Id { get; set; }

    public override CommentRangeStart Clone()
    {
        var copy = new CommentRangeStart(Id);
        CopyAttributesTo(copy);
        return copy;
    }
}

public sealed class CommentRangeEnd : Inline
{
    public CommentRangeEnd(int id)
    {
        Id = id;
    }

    public int Id { get; set; }

    public override CommentRangeEnd Clone()
    {
        var copy = new CommentRangeEnd(Id);
        CopyAttributesTo(copy);
        return copy;
    }
}
