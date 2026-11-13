namespace Raffica.Write.Model;

/// <summary>A <c>w:p</c>: its properties (including the paragraph mark's <c>rPr</c>, kept inside the bag) and its inline content.</summary>
public sealed class Paragraph : Block
{
    public PropertyBag Properties { get; } = new(PropertyKind.Paragraph);

    public IList<Inline> Inlines { get; } = new List<Inline>();
}
