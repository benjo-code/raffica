using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>What a <c>w:sdt</c> consists of besides its content: <c>w:sdtPr</c>, <c>w:sdtEndPr</c>, the <c>w:sdtContent</c> attributes and children the model does not know. The positions count the unknown children that came before each part, so the writer restores the order.</summary>
public sealed class ContentControlFrame
{
    public PropertyBag Properties { get; } = new(PropertyKind.ContentControl);

    public int PropertiesPosition { get; set; }

    /// <summary>The <c>w:sdtEndPr</c> as read, or null.</summary>
    public UnknownContent? EndProperties { get; set; }

    public int EndPropertiesPosition { get; set; }

    /// <summary>The attributes of <c>w:sdtContent</c>.</summary>
    public IList<XAttribute> ContentAttributes { get; } = new List<XAttribute>();

    public int ContentPosition { get; set; }

    /// <summary>False when the source had no <c>w:sdtContent</c> (it is then not invented).</summary>
    public bool HasContent { get; set; } = true;

    /// <summary>Children of <c>w:sdt</c> the model does not know, in order.</summary>
    public IList<UnknownContent> OtherChildren { get; } = new List<UnknownContent>();

    /// <summary>A copy of the frame: the properties and attributes are new objects, the kept <see cref="UnknownContent"/> is shared.</summary>
    public ContentControlFrame Clone()
    {
        var copy = new ContentControlFrame();
        CopyInto(copy);
        return copy;
    }

    internal void CopyInto(ContentControlFrame target)
    {
        target.PropertiesPosition = PropertiesPosition;
        target.EndProperties = EndProperties;
        target.EndPropertiesPosition = EndPropertiesPosition;
        target.ContentPosition = ContentPosition;
        target.HasContent = HasContent;
        Properties.CopyStateTo(target.Properties);
        foreach (var attribute in ContentAttributes)
        {
            target.ContentAttributes.Add(new XAttribute(attribute));
        }

        foreach (var other in OtherChildren)
        {
            target.OtherChildren.Add(other);
        }
    }
}

/// <summary>A block-level <c>w:sdt</c> (content control) holding blocks.</summary>
public sealed class ContentControlBlock : Block
{
    public ContentControlFrame Frame { get; } = new();

    public PropertyBag Properties => Frame.Properties;

    public IList<Block> Content { get; } = new List<Block>();

    public override ContentControlBlock Clone()
    {
        var copy = new ContentControlBlock();
        Frame.CopyInto(copy.Frame);
        CopyAttributesTo(copy);
        foreach (var block in Content)
        {
            copy.Content.Add(block.Clone());
        }

        return copy;
    }
}

/// <summary>An inline <c>w:sdt</c> (content control inside a paragraph) holding inlines.</summary>
public sealed class ContentControlInline : Inline
{
    public ContentControlFrame Frame { get; } = new();

    public PropertyBag Properties => Frame.Properties;

    public IList<Inline> Inlines { get; } = new List<Inline>();

    public override ContentControlInline Clone()
    {
        var copy = new ContentControlInline();
        Frame.CopyInto(copy.Frame);
        CopyAttributesTo(copy);
        foreach (var inline in Inlines)
        {
            copy.Inlines.Add(inline.Clone());
        }

        return copy;
    }
}
