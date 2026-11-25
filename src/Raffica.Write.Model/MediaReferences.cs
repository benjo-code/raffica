using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>
/// The one definition of "this XML refers to a picture of the package": an attribute in the relationships namespace (<c>r:embed</c>, <c>r:link</c>, <c>r:id</c>, ...) whose value is the relationship id. The save
/// (<c>MediaPartWriter.Used</c>, which reads the serialised main part) and the editor's pruning of the picture store (this scan of the model's XML) use the same namespace and the same rule.
/// </summary>
public static class MediaReferences
{
    /// <summary>The namespace of the relationship attributes.</summary>
    public const string RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Takes from <paramref name="ids"/> every relationship id that an attribute in the relationships namespace of <paramref name="roots"/> (or below them) has as its value. Stops early when no id is left.</summary>
    public static void RemoveReferenced(IEnumerable<XElement> roots, ISet<string> ids)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return;
        }

        XNamespace r = RelationshipsNamespace;
        foreach (var root in roots)
        {
            foreach (var element in root.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.Name.Namespace == r)
                    {
                        ids.Remove(attribute.Value);
                    }
                }
            }

            if (ids.Count == 0)
            {
                return;
            }
        }
    }
}
