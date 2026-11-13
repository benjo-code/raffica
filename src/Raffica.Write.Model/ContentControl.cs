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
}

/// <summary>A block-level <c>w:sdt</c> (content control) holding blocks.</summary>
public sealed class ContentControlBlock : Block
{
    public ContentControlFrame Frame { get; } = new();

    public PropertyBag Properties => Frame.Properties;

    public IList<Block> Content { get; } = new List<Block>();
}

/// <summary>An inline <c>w:sdt</c> (content control inside a paragraph) holding inlines.</summary>
public sealed class ContentControlInline : Inline
{
    public ContentControlFrame Frame { get; } = new();

    public PropertyBag Properties => Frame.Properties;

    public IList<Inline> Inlines { get; } = new List<Inline>();
}
