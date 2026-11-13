namespace Raffica.Write.Model;

/// <summary>A <c>w:p</c>: its properties (including the paragraph mark's <c>rPr</c>, kept inside the bag) and its inline content.</summary>
public sealed class Paragraph : Block
{
    public PropertyBag Properties { get; } = new(PropertyKind.Paragraph);

    /// <summary>The <c>w:sectPr</c> inside the paragraph properties: this paragraph ends a section.</summary>
    public SectionProperties? SectionBreak { get; set; }

    /// <summary>How many children of the property bag came before the <c>w:sectPr</c> in the source; null puts it where the schema order says.</summary>
    public int? SectionBreakIndex { get; set; }

    public IList<Inline> Inlines { get; } = new List<Inline>();
}
