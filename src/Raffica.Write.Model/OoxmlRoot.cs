using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>The root element of a part: its name, its attributes and its namespace declarations, kept so <c>mc:Ignorable</c> and the prefixes it names survive a save.</summary>
public sealed class OoxmlRoot
{
    public OoxmlRoot(XName name, IEnumerable<XAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(attributes);
        Name = name;
        foreach (var attribute in attributes)
        {
            Attributes.Add(new XAttribute(attribute));
        }
    }

    public XName Name { get; }

    /// <summary>Every attribute of the root, namespace declarations included, in source order.</summary>
    public IList<XAttribute> Attributes { get; } = new List<XAttribute>();

    public static OoxmlRoot From(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new OoxmlRoot(element.Name, element.Attributes());
    }

    /// <summary>Makes an empty element with this name and these attributes.</summary>
    public XElement CreateElement()
    {
        var element = new XElement(Name);
        foreach (var attribute in Attributes)
        {
            element.Add(new XAttribute(attribute));
        }

        return element;
    }
}
