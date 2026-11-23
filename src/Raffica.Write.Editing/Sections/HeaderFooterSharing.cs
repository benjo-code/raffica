using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>The references of the properties of a section to header and footer parts: set (replace in place or add) and remove one of a kind and type.</summary>
internal static class HeaderFooterSharing
{
    public static void SetReference(SectionProperties properties, HeaderFooterKind kind, HeaderFooterType type, string id)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var references = properties.HeaderFooterReferences;
        for (var i = 0; i < references.Count; i++)
        {
            if (references[i].Kind == kind && references[i].Type == type)
            {
                references[i] = new HeaderFooterReference(kind, type, id) { BagPosition = references[i].BagPosition };
                return;
            }
        }

        references.Add(new HeaderFooterReference(kind, type, id));
    }

    public static void RemoveReference(SectionProperties properties, HeaderFooterKind kind, HeaderFooterType type)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var references = properties.HeaderFooterReferences;
        for (var i = references.Count - 1; i >= 0; i--)
        {
            if (references[i].Kind == kind && references[i].Type == type)
            {
                references.RemoveAt(i);
            }
        }
    }
}
