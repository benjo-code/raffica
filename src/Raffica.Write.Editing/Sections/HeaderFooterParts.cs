using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Makes new header and footer parts. A part is never removed: a part nothing refers to any more stays in the package, unused.</summary>
internal static class HeaderFooterParts
{
    private const string IdPrefix = "rIdWrite";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Adds a part of the given kind to the document and returns its relationship id (<c>rIdWrite&lt;n&gt;</c>, never an id the model's parts or sections use).
    /// The content is a deep copy of <paramref name="copyFrom"/>'s blocks (nothing is shared with the original) or one empty paragraph.
    /// The part is saved as a real part, with its relationship and content type, by <c>DocxFile</c>, which gives it the relationships of the part it was copied from (<see cref="HeaderFooterPart.RelationshipsFrom"/>); the caller adds the reference to a section.
    /// </summary>
    public static string Create(WordDocument document, HeaderFooterKind kind, Story? copyFrom)
    {
        ArgumentNullException.ThrowIfNull(document);
        var id = NewId(document);
        var name = W + (kind == HeaderFooterKind.Footer ? "ftr" : "hdr");
        var root = new OoxmlRoot(name, [new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName), new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName)]);
        var story = new Story(root);
        if (copyFrom is null)
        {
            story.Blocks.Add(new Paragraph());
        }
        else
        {
            foreach (var block in copyFrom.Blocks)
            {
                story.Blocks.Add(block.Clone());
            }

            if (story.Blocks.Count == 0)
            {
                story.Blocks.Add(new Paragraph());
            }
        }

        document.HeadersFooters[id] = new HeaderFooterPart(story)
        {
            RelationshipsFrom = copyFrom is null ? null : document.HeadersFooters.Values.FirstOrDefault(part => ReferenceEquals(part.Content, copyFrom)),
        };
        return id;
    }

    private static string NewId(WordDocument document)
    {
        var used = new HashSet<string>(document.HeadersFooters.Keys, StringComparer.Ordinal);
        foreach (var section in SectionList.Of(document))
        {
            foreach (var reference in section.Properties.HeaderFooterReferences)
            {
                used.Add(reference.RelationshipId);
            }
        }

        for (var n = 1; ; n++)
        {
            var id = IdPrefix + n.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(id))
            {
                return id;
            }
        }
    }
}
