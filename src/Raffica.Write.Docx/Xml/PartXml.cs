using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>
/// What the readers and writers of the styles, numbering and settings parts share: the walk over the root's children, the
/// rule for a typed child (only its <c>w:val</c>, only the first of its name, otherwise it is kept as it is) and the
/// writer that puts typed and kept children back in the order they were read.
/// </summary>
internal static class PartXml
{
    /// <summary>One child to write: where it was read (<see cref="OrderedElement.NewPosition"/> when made in code), its schema index (null when the schema order does not know it) and how to write it.</summary>
    internal readonly record struct Entry(int Position, int? Tie, Action<XmlWriter> Write);

    // Reading.

    /// <summary>Reads the root element and calls <paramref name="onChild"/> with every child element, whole, and its index among the child elements.</summary>
    public static T Read<T>(Stream stream, Func<OoxmlRoot, T> create, Action<T, XElement, int> onChild)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = XmlReader.Create(stream, XmlKit.ReaderSettings());
        reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element)
        {
            throw new XmlException("The part has no root element.");
        }

        var name = XNamespace.Get(reader.NamespaceURI) + reader.LocalName;
        var model = create(new OoxmlRoot(name, XmlKit.ReadAttributes(reader)));
        if (XmlKit.Enter(reader))
        {
            var index = 0;
            while (XmlKit.More(reader))
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    onChild(model, XmlKit.ReadKept(reader), index++);
                }
                else
                {
                    reader.Read();
                }
            }

            XmlKit.Leave(reader);
        }

        return model;
    }

    /// <summary>The <c>w:val</c> of an element that has nothing else (no other attribute, no content), or null.</summary>
    public static string? SimpleValue(XElement element)
    {
        string? value = null;
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name != Names.Val || value is not null)
            {
                return null;
            }

            value = attribute.Value;
        }

        return element.Nodes().Any() ? null : value;
    }

    /// <summary>An integer that writes back as the same text (no sign, no leading zeros): anything else would change on a save and stays text.</summary>
    public static bool TryCanonicalInt(string? text, out int value)
    {
        value = 0;
        return text is not null
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value.ToString(CultureInfo.InvariantCulture) == text;
    }

    /// <summary>True the first time a single typed child with this key is seen on the owner, and records its place; a repeat is false (it is kept instead).</summary>
    public static bool Claim(OrderedElement owner, string key, int index)
    {
        if (owner.ChildPositions.ContainsKey(key))
        {
            return false;
        }

        owner.ChildPositions[key] = index;
        return true;
    }

    public static void Keep(OrderedElement owner, XElement element, int index) =>
        owner.KeptChildren.Add(new KeptChild(index, new UnknownContent(element)));

    /// <summary>Fills a property bag from a <c>*Pr</c> element: its attributes and children, in order.</summary>
    public static void Fill(PropertyBag bag, XElement element)
    {
        bag.WasPresent = true;
        foreach (var attribute in element.Attributes())
        {
            bag.UnknownAttributes.Add(new XAttribute(attribute));
        }

        foreach (var child in element.Elements())
        {
            bag.AddLoaded(child);
        }
    }

    /// <summary>Reads the attribute <paramref name="name"/> as a canonical integer; every other attribute of the element goes to the unknown attributes (so does this one when it is not canonical).</summary>
    public static int ReadAttributes(OoxmlElement target, XElement element, XName name)
    {
        var result = -1;
        foreach (var attribute in element.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration && attribute.Name == name && result < 0 && TryCanonicalInt(attribute.Value, out var number))
            {
                result = number;
            }
            else
            {
                target.UnknownAttributes.Add(new XAttribute(attribute));
            }
        }

        return result;
    }

    // Writing.

    /// <summary>Writes UTF-8 without a byte order mark and an XML declaration with <c>standalone="yes"</c>. The stream stays open.</summary>
    public static void Write(Stream stream, OoxmlRoot root, Action<XmlWriter> children)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(root);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            NewLineHandling = NewLineHandling.Entitize,
            CloseOutput = false,
        };
        using var writer = XmlWriter.Create(stream, settings);
        writer.WriteStartDocument(true);
        writer.WriteStartElement(StoryXml.RootPrefix(root), root.Name.LocalName, root.Name.NamespaceName);
        XmlKit.Attributes(writer, root.Attributes);
        children(writer);
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    /// <summary>
    /// Writes the entries. Children that were read keep their file order. A child made in code goes just before the first
    /// child whose schema index is greater; when none is greater, right after the last child whose index is not greater
    /// (and at the end when no index is known). Children the schema order does not know keep their place.
    /// </summary>
    public static void Flush(XmlWriter writer, List<Entry> entries)
    {
        var result = entries.Where(e => e.Position != OrderedElement.NewPosition).OrderBy(e => e.Position).ToList();
        foreach (var added in entries.Where(e => e.Position == OrderedElement.NewPosition))
        {
            var at = result.Count;
            if (added.Tie is int tie)
            {
                var greater = result.FindIndex(r => r.Tie is int other && other > tie);
                at = greater >= 0 ? greater : result.FindLastIndex(r => r.Tie is int other && other <= tie) + 1;
                if (at == 0 && greater < 0)
                {
                    at = result.Count;
                }
            }

            result.Insert(at, added);
        }

        foreach (var entry in result)
        {
            entry.Write(writer);
        }
    }

    /// <summary>The schema index of a child by name from a table of local names in the WordprocessingML namespace; null for any other name.</summary>
    public static Func<XName, int?> OrderOf(string[] table) =>
        name => name.Namespace == Names.W && Array.IndexOf(table, name.LocalName) is var i and >= 0 ? i : null;

    public static void AddKept(List<Entry> entries, OrderedElement owner, Func<XName, int?> tieOf)
    {
        foreach (var kept in owner.KeptChildren)
        {
            var content = kept.Content;
            entries.Add(new Entry(kept.Position, tieOf(content.Element.Name), w => XmlKit.Kept(w, content)));
        }
    }

    /// <summary>Adds <c>&lt;w:key w:val="value"/&gt;</c> when the value is not null.</summary>
    public static void AddValue(List<Entry> entries, OrderedElement owner, string key, int tie, string? value)
    {
        if (value is null)
        {
            return;
        }

        entries.Add(new Entry(owner.PositionOf(key), tie, w =>
        {
            XmlKit.Start(w, Names.W + key);
            XmlKit.Attribute(w, Names.Val, value);
            w.WriteEndElement();
        }));
    }

    public static void AddValue(List<Entry> entries, OrderedElement owner, string key, int tie, int? value) =>
        AddValue(entries, owner, key, tie, value?.ToString(CultureInfo.InvariantCulture));

    public static void AddBag(List<Entry> entries, OrderedElement owner, string key, int tie, PropertyBag bag) =>
        entries.Add(new Entry(owner.PositionOf(key), tie, w => StoryXml.WriteProperties(Names.W + key, bag, w)));

    public static void AddElement(List<Entry> entries, int position, int tie, XElement? element)
    {
        if (element is not null)
        {
            entries.Add(new Entry(position, tie, w => element.WriteTo(w)));
        }
    }

    public static void WriteIdAttribute(XmlWriter writer, XName name, int value)
    {
        if (value >= 0)
        {
            XmlKit.Attribute(writer, name, value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
