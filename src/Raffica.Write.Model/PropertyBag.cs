using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>The children of a <c>*Pr</c> element as an ordered list of kept elements, with typed accessors on top. Nothing is dropped: a child the model has no accessor for stays where it is.</summary>
public class PropertyBag : OoxmlElement
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly List<XElement> _children = [];

    public PropertyBag(PropertyKind kind)
    {
        Kind = kind;
    }

    public PropertyKind Kind { get; }

    public IReadOnlyList<XElement> Children => _children;

    /// <summary>True when the bag was read from an element that was there but empty (for example <c>&lt;w:tcPr/&gt;</c>); the writer then writes the empty element again. A bag created in code that stays empty is not written.</summary>
    public bool WasPresent { get; set; }

    /// <summary>Appends a child as read from a file, without reordering. A detached copy is stored (see <see cref="UnknownContent.Capture"/>), so prefixes declared on an ancestor survive and the source tree is not kept alive.</summary>
    public void AddLoaded(XElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(UnknownContent.Capture(child));
    }

    /// <summary>The first child of this name, or null. (Get, Set and Remove act on the first match; repeatable children such as <c>w:headerReference</c> belong to <c>SectionProperties</c>.)</summary>
    public XElement? Get(XName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _children.Find(c => c.Name == name);
    }

    /// <summary>Replaces the child of this name in place, or inserts it where the schema order of this kind puts it. A name the order does not know is appended.</summary>
    public void Set(XName name, XElement value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Name != name)
        {
            throw new ArgumentException("The element name does not match the name it is set under.", nameof(value));
        }

        var existing = _children.FindIndex(c => c.Name == name);
        if (existing >= 0)
        {
            _children[existing] = value;
            return;
        }

        var index = SchemaOrder.IndexOf(Kind, name);
        if (index is null)
        {
            _children.Add(value);
            return;
        }

        var at = _children.FindIndex(c => SchemaOrder.IndexOf(Kind, c.Name) is int other && other > index);
        if (at < 0)
        {
            _children.Add(value);
        }
        else
        {
            _children.Insert(at, value);
        }
    }

    public bool Remove(XName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var existing = _children.FindIndex(c => c.Name == name);
        if (existing < 0)
        {
            return false;
        }

        _children.RemoveAt(existing);
        return true;
    }

    private XName? StyleName => Kind switch
    {
        PropertyKind.Paragraph => W + "pStyle",
        PropertyKind.Run => W + "rStyle",
        PropertyKind.Table => W + "tblStyle",
        _ => null,
    };

    /// <summary>The referenced style id: <c>w:pStyle</c>, <c>w:rStyle</c> or <c>w:tblStyle</c> by kind; null when absent or the kind has none.</summary>
    public string? StyleId
    {
        get
        {
            var name = StyleName;
            return name is null ? null : (string?)Get(name)?.Attribute(W + "val");
        }
        set
        {
            var name = StyleName ?? throw new InvalidOperationException("This kind of property bag has no style reference.");
            if (value is null)
            {
                Remove(name);
            }
            else
            {
                Set(name, new XElement(name, new XAttribute(W + "val", value)));
            }
        }
    }

    /// <summary>True when <c>w:b</c> is present and its value is not off (<c>0</c>, <c>false</c>, <c>off</c>).</summary>
    public bool Bold => GetFlag(W + "b");

    /// <summary>True when the on/off child of this name is present and its <c>w:val</c> is not off (<c>0</c>, <c>false</c>, <c>off</c>).</summary>
    public bool GetFlag(XName name)
    {
        var element = Get(name);
        if (element is null)
        {
            return false;
        }

        var value = (string?)element.Attribute(W + "val");
        return value is not ("0" or "false" or "off");
    }

    /// <summary>Turns an on/off child on (a bare element; one already on stays as it is) or removes it.</summary>
    public void SetFlag(XName name, bool on)
    {
        if (!on)
        {
            Remove(name);
        }
        else if (!GetFlag(name))
        {
            Set(name, new XElement(name));
        }
    }

    /// <summary>The list id of <c>w:numPr</c>; 0 means "no list" and is returned as 0.</summary>
    public int? NumberingId => NumberingValue("numId");

    public int? NumberingLevel => NumberingValue("ilvl");

    private int? NumberingValue(string local)
    {
        var text = (string?)Get(W + "numPr")?.Element(W + local)?.Attribute(W + "val");
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}
