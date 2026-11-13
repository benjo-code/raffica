namespace Raffica.Write.Model;

/// <summary>A <c>w:p</c>: its properties (including the paragraph mark's <c>rPr</c>, kept inside the bag) and its inline content.</summary>
public sealed class Paragraph : Block
{
    public PropertyBag Properties { get; } = new(PropertyKind.Paragraph);

    /// <summary>The <c>w:sectPr</c> inside the paragraph properties: this paragraph ends a section. The writer places it by schema order (after <c>w:rPr</c>, before <c>w:pPrChange</c>).</summary>
    public SectionProperties? SectionBreak { get; set; }

    public IList<Inline> Inlines { get; } = new List<Inline>();
}
