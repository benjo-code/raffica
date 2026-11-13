using System.Xml;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>Shared reading and writing helpers of the part readers: attributes, kept elements and the walk over children. Forward-only; no reflection.</summary>
internal static class XmlKit
{
    public static XmlReaderSettings ReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>Reads every attribute of the element the reader is on, namespace declarations included (as <c>xmlns</c> attributes), and leaves the reader on the element.</summary>
    public static List<XAttribute> ReadAttributes(XmlReader reader)
    {
        var list = new List<XAttribute>();
        if (!reader.HasAttributes)
        {
            return list;
        }

        while (reader.MoveToNextAttribute())
        {
            XName name;
            if (reader.Prefix == "xmlns")
            {
                name = XNamespace.Xmlns + reader.LocalName;
            }
            else if (reader.Prefix.Length == 0 && reader.LocalName == "xmlns")
            {
                name = "xmlns";
            }
            else
            {
                name = XNamespace.Get(reader.NamespaceURI) + reader.LocalName;
            }

            list.Add(new XAttribute(name, reader.Value));
        }

        reader.MoveToElement();
        return list;
    }

    /// <summary>Removes the attribute of this name from the list and returns its value, or null when absent.</summary>
    public static string? Take(List<XAttribute> attributes, XName name)
    {
        var index = attributes.FindIndex(a => a.Name == name);
        if (index < 0)
        {
            return null;
        }

        var value = attributes[index].Value;
        attributes.RemoveAt(index);
        return value;
    }

    /// <summary>Gives whatever attributes are left to the element as unknown attributes, in the order read.</summary>
    public static void KeepRest(OoxmlElement target, IEnumerable<XAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            target.UnknownAttributes.Add(attribute);
        }
    }

    /// <summary>Copies the attributes of an element that are not in <paramref name="typed"/> to the target's unknown attributes.</summary>
    public static void KeepOthers(OoxmlElement target, XElement element, params XName[] typed)
    {
        foreach (var attribute in element.Attributes())
        {
            if (Array.IndexOf(typed, attribute.Name) < 0)
            {
                target.UnknownAttributes.Add(new XAttribute(attribute));
            }
        }
    }

    /// <summary>Reads the element the reader is on whole (children, text and declarations) and moves past it.</summary>
    public static XElement ReadKept(XmlReader reader) => (XElement)XNode.ReadFrom(reader);

    /// <summary>Moves into the content of the element the reader is on. False for an empty element, which is already skipped; true when children may follow (finish with <see cref="More"/> and <see cref="Leave"/>).</summary>
    public static bool Enter(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return false;
        }

        reader.Read();
        return true;
    }

    public static bool More(XmlReader reader) => reader.NodeType != XmlNodeType.EndElement && !reader.EOF;

    /// <summary>Moves past the end tag of the element whose children were just walked.</summary>
    public static void Leave(XmlReader reader) => reader.Read();

    public static bool IsWord(XmlReader reader, string local) => reader.NamespaceURI == Names.WordUri && reader.LocalName == local;

    // Writing.

    public static void Start(XmlWriter writer, XName name) => writer.WriteStartElement(null, name.LocalName, name.NamespaceName);

    public static void Attribute(XmlWriter writer, XName name, string value)
    {
        if (name.Namespace == XNamespace.None)
        {
            writer.WriteAttributeString(name.LocalName, value);
        }
        else
        {
            writer.WriteAttributeString(null, name.LocalName, name.NamespaceName, value);
        }
    }

    public static void Attribute(XmlWriter writer, XAttribute attribute)
    {
        if (attribute.IsNamespaceDeclaration)
        {
            if (attribute.Name.Namespace == XNamespace.Xmlns)
            {
                writer.WriteAttributeString("xmlns", attribute.Name.LocalName, null, attribute.Value);
            }
            else
            {
                writer.WriteAttributeString("xmlns", attribute.Value);
            }
        }
        else
        {
            Attribute(writer, attribute.Name, attribute.Value);
        }
    }

    public static void UnknownAttributes(XmlWriter writer, OoxmlElement element)
    {
        foreach (var attribute in element.UnknownAttributes)
        {
            Attribute(writer, attribute);
        }
    }

    public static void Kept(XmlWriter writer, UnknownContent content) => content.Element.WriteTo(writer);
}
