namespace Raffica.Write.Model;

/// <summary>
/// One section of a document, as a view: its properties (the object in the model, not a copy), the paragraph that ends it, and the top-level body blocks it spans.
/// <see cref="FirstBlock"/> and <see cref="LastBlock"/> are indexes into <c>Body.Blocks</c>; blocks inside a content control carry the control's index, so two
/// neighbours can share one index, and an empty final section has <c>LastBlock == FirstBlock - 1</c>.
/// </summary>
/// <param name="Index">The position of the section, from 0.</param>
/// <param name="Properties">The section's <c>w:sectPr</c>. A body with no final <c>w:sectPr</c> gives the last section a detached, empty one (changes to it are not in the document).</param>
/// <param name="Break">The paragraph that carries the section break; null for the last section.</param>
/// <param name="FirstBlock">The top-level index of the first block of the section.</param>
/// <param name="LastBlock">The top-level index of the last block of the section.</param>
public sealed record SectionInfo(int Index, SectionProperties Properties, Paragraph? Break, int FirstBlock, int LastBlock);

/// <summary>The sections of a document read from its body (never a second storage: the properties are the model's own objects) and the header and footer inheritance between them.</summary>
public static class SectionList
{
    // The layout's own depth for looking through content controls (FlatBlocks), so both agree on which paragraphs end a section.
    private const int MaximumDepth = 32;

    /// <summary>Splits the body at the paragraphs that carry a section break; the body's own <c>w:sectPr</c> ends the last section.</summary>
    public static IReadOnlyList<SectionInfo> Of(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var body = document.Body;
        var sections = new List<SectionInfo>();
        var first = -1;
        var last = -1;
        var next = 0;
        for (var i = 0; i < body.Blocks.Count; i++)
        {
            Walk(body.Blocks[i], i, 0);
        }

        sections.Add(new SectionInfo(sections.Count, body.FinalSection ?? new SectionProperties(), null, first >= 0 ? first : next, first >= 0 ? last : next - 1));
        return sections;

        void Walk(Block block, int index, int depth)
        {
            if (block is ContentControlBlock control && depth < MaximumDepth)
            {
                foreach (var inner in control.Content)
                {
                    Walk(inner, index, depth + 1);
                }

                return;
            }

            if (first < 0)
            {
                first = index;
            }

            last = index;
            if (block is Paragraph { SectionBreak: { } properties } paragraph)
            {
                sections.Add(new SectionInfo(sections.Count, properties, paragraph, first, last));
                next = index + 1;
                first = -1;
                last = -1;
            }
        }
    }

    /// <summary>
    /// The header or footer reference that serves a page type in a section: the section's own, else the one of the section before it, and so on back to the first
    /// (the first section has no inheritance: no reference gives null). A section with <c>w:titlePg</c> and no first-page reference of its own shows the previous section's
    /// first-page one, as Word does ([MS-OI29500]); the caller chooses the type (<see cref="HeaderFooterType.First"/> for the first page of a section with <c>w:titlePg</c>).
    /// </summary>
    /// <returns>The reference and the index of the section it belongs to (-1 when there is none).</returns>
    public static (HeaderFooterReference? Reference, int OwnerSection) Resolve(IReadOnlyList<SectionInfo> sections, int section, HeaderFooterKind kind, HeaderFooterType type)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentOutOfRangeException.ThrowIfNegative(section);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(section, sections.Count);
        for (var s = section; s >= 0; s--)
        {
            foreach (var reference in sections[s].Properties.HeaderFooterReferences)
            {
                if (reference.Kind == kind && reference.Type == type)
                {
                    return (reference, s);
                }
            }
        }

        return (null, -1);
    }
}
