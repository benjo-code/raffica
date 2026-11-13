using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>One element the model does not understand, kept so a save writes it back. The typed unknown variants (block, inline, run content, row item) derive their own base and hold one of these.</summary>
public sealed class UnknownContent
{
    public UnknownContent(XElement source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Element = Capture(source);
    }

    /// <summary>The kept element: a deep copy that declares the namespaces it needs with the prefixes the source used.</summary>
    public XElement Element { get; }

    /// <summary>Deep-copies an element and re-declares, on the copy, every in-scope prefix its names use, so detaching it from its parent does not rename prefixes.</summary>
    public static XElement Capture(XElement source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var copy = new XElement(source);
        var needed = new HashSet<XNamespace>();
        foreach (var element in source.DescendantsAndSelf())
        {
            needed.Add(element.Name.Namespace);
            foreach (var attribute in element.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration)
                {
                    needed.Add(attribute.Name.Namespace);
                }
            }
        }

        foreach (var ns in needed)
        {
            if (ns == XNamespace.None || ns == XNamespace.Xml || ns == XNamespace.Xmlns)
            {
                continue;
            }

            var prefix = source.GetPrefixOfNamespace(ns);
            if (prefix is null)
            {
                continue;
            }

            var declared = prefix.Length == 0 ? XNamespace.None + "xmlns" : XNamespace.Xmlns + prefix;
            if (copy.Attribute(declared) is null)
            {
                copy.Add(new XAttribute(declared, ns.NamespaceName));
            }
        }

        return copy;
    }
}
