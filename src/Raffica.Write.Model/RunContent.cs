using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Model;

public enum BreakKind
{
    TextWrapping,
    Page,
    Column,
}

public enum FieldCharKind
{
    Begin,
    Separate,
    End,
}

/// <summary>One item inside a run: text, a tab, a break, a field character, a reference or a picture.</summary>
public abstract class RunContent : OoxmlElement
{
}

/// <summary>A run child the model does not understand, kept as it was read.</summary>
public sealed class UnknownRunContent : RunContent
{
    public UnknownRunContent(UnknownContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    public UnknownContent Content { get; }
}

/// <summary>A <c>w:t</c>. The text is exactly what was typed; <c>xml:space</c> is decided when it is written.</summary>
public sealed class TextContent : RunContent
{
    public TextContent(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; set; }
}

/// <summary>A <c>w:delText</c>: text inside a tracked deletion.</summary>
public sealed class DeletedText : RunContent
{
    public DeletedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; set; }
}

/// <summary>A <c>w:instrText</c>: a piece of a field instruction.</summary>
public sealed class InstructionText : RunContent
{
    public InstructionText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; set; }
}

public sealed class TabContent : RunContent
{
}

/// <summary>A <c>w:br</c>. A <c>type</c> attribute other than page or column (an explicit <c>textWrapping</c>) stays an unknown attribute, so it is written back as it was.</summary>
public sealed class BreakContent : RunContent
{
    public BreakContent(BreakKind kind, string? clear = null)
    {
        Kind = kind;
        Clear = clear;
    }

    public BreakKind Kind { get; set; }

    /// <summary>The <c>w:clear</c> value (<c>none</c>, <c>left</c>, <c>right</c>, <c>all</c>) or null.</summary>
    public string? Clear { get; set; }
}

/// <summary>A <c>w:fldChar</c>. Children (<c>w:fldData</c>, <c>w:ffData</c>) are kept in <see cref="KeptChildren"/>.</summary>
public sealed class FieldChar : RunContent
{
    public FieldChar(FieldCharKind kind, bool dirty = false)
    {
        Kind = kind;
        Dirty = dirty;
    }

    public FieldCharKind Kind { get; set; }

    public bool Dirty { get; set; }

    public IList<XElement> KeptChildren { get; } = new List<XElement>();
}

public sealed class FootnoteReference : RunContent
{
    public FootnoteReference(int id)
    {
        Id = id;
    }

    public int Id { get; set; }
}

public sealed class EndnoteReference : RunContent
{
    public EndnoteReference(int id)
    {
        Id = id;
    }

    public int Id { get; set; }
}

public sealed class CommentReference : RunContent
{
    public CommentReference(int id)
    {
        Id = id;
    }

    public int Id { get; set; }
}

/// <summary>A <c>w:drawing</c>. The XML is kept whole in <see cref="Raw"/>; the other members are read from it, so they can never disagree with what is written.</summary>
public sealed class Picture : RunContent
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public Picture(XElement raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Name != W + "drawing")
        {
            throw new ArgumentException("The element is not a w:drawing.", nameof(raw));
        }

        Raw = raw;
    }

    public XElement Raw { get; }

    /// <summary>True for <c>wp:inline</c> (sits in the text), false for <c>wp:anchor</c> (floats).</summary>
    public bool Inline => Raw.Element(Wp + "inline") is not null;

    private XElement? Container => Raw.Element(Wp + "inline") ?? Raw.Element(Wp + "anchor");

    public long WidthEmu => Extent("cx");

    public long HeightEmu => Extent("cy");

    public string? EmbedRelationshipId => Raw.Descendants(A + "blip").Select(b => (string?)b.Attribute(R + "embed")).FirstOrDefault(v => v is not null);

    public string? AltText => (string?)Container?.Element(Wp + "docPr")?.Attribute("descr");

    private long Extent(string name) =>
        long.TryParse((string?)Container?.Element(Wp + "extent")?.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>True when <paramref name="raw"/> has the shape the model understands: a <c>w:drawing</c> with a <c>wp:inline</c> or <c>wp:anchor</c> child.</summary>
    public static bool IsRecognized(XElement raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return raw.Name == W + "drawing" && (raw.Element(Wp + "inline") is not null || raw.Element(Wp + "anchor") is not null);
    }
}
