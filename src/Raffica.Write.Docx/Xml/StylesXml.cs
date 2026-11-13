using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>The styles part (<c>word/styles.xml</c>): a stream in, a <see cref="StyleSheet"/> out, and back. Children and attributes the model does not name are kept in their place.</summary>
internal static class StylesXml
{
    public static StyleSheet Read(Stream stream) => PartXml.Read(stream, root => new StyleSheet(root), ReadChild);

    private static void ReadChild(StyleSheet sheet, XElement element, int index)
    {
        if (element.Name.Namespace == Names.W)
        {
            switch (element.Name.LocalName)
            {
                case "docDefaults" when PartXml.Claim(sheet, "docDefaults", index):
                    sheet.Defaults = UnknownContent.Capture(element);
                    return;
                case "latentStyles" when PartXml.Claim(sheet, "latentStyles", index):
                    sheet.LatentStyles = UnknownContent.Capture(element);
                    return;
                case "style":
                    sheet.Styles.Add(ReadStyle(element, index));
                    return;
            }
        }

        PartXml.Keep(sheet, element, index);
    }

    private static Style ReadStyle(XElement element, int position)
    {
        var style = new Style { Position = position };
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                style.UnknownAttributes.Add(new XAttribute(attribute));
            }
            else if (attribute.Name == Names.Type && style.Type is null)
            {
                style.Type = attribute.Value;
            }
            else if (attribute.Name == Names.StyleId && style.Id.Length == 0 && attribute.Value.Length > 0)
            {
                style.Id = attribute.Value;
            }
            else if (attribute.Name == Names.Default && style.DefaultAttribute is null)
            {
                style.DefaultAttribute = attribute.Value;
            }
            else
            {
                style.UnknownAttributes.Add(new XAttribute(attribute));
            }
        }

        var index = 0;
        foreach (var child in element.Elements())
        {
            ReadStyleChild(style, child, index++);
        }

        return style;
    }

    private static void ReadStyleChild(Style style, XElement child, int index)
    {
        if (child.Name.Namespace == Names.W)
        {
            var key = child.Name.LocalName;
            switch (key)
            {
                case "name" or "basedOn" or "next" or "link":
                    if (PartXml.SimpleValue(child) is { } value && PartXml.Claim(style, key, index))
                    {
                        switch (key)
                        {
                            case "name":
                                style.Name = value;
                                break;
                            case "basedOn":
                                style.BasedOn = value;
                                break;
                            case "next":
                                style.Next = value;
                                break;
                            default:
                                style.Link = value;
                                break;
                        }

                        return;
                    }

                    break;
                case "pPr" when PartXml.Claim(style, key, index):
                    PartXml.Fill(style.ParagraphProperties, child);
                    return;
                case "rPr" when PartXml.Claim(style, key, index):
                    PartXml.Fill(style.RunProperties, child);
                    return;
                case "tblPr" when PartXml.Claim(style, key, index):
                    PartXml.Fill(style.TableProperties, child);
                    return;
            }
        }

        PartXml.Keep(style, child, index);
    }

    /// <summary>Writes UTF-8 without a byte order mark and an XML declaration with <c>standalone="yes"</c>. The stream stays open.</summary>
    public static void Write(StyleSheet sheet, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        PartXml.Write(stream, sheet.Root, writer =>
        {
            var entries = new List<PartXml.Entry>();
            PartXml.AddElement(entries, sheet.PositionOf("docDefaults"), 0, sheet.Defaults);
            PartXml.AddElement(entries, sheet.PositionOf("latentStyles"), 1, sheet.LatentStyles);
            foreach (var style in sheet.Styles)
            {
                entries.Add(new PartXml.Entry(style.Position, 2, w => WriteStyle(style, w)));
            }

            PartXml.AddKept(entries, sheet);
            PartXml.Flush(writer, entries);
        });
    }

    private static int Tie(string key) => SchemaOrder.IndexOf(PropertyKind.Style, Names.W + key) ?? 0;

    private static void WriteStyle(Style style, System.Xml.XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "style");
        XmlKit.UnknownAttributes(writer, style);
        if (style.Type is not null)
        {
            XmlKit.Attribute(writer, Names.Type, style.Type);
        }

        if (style.Id.Length > 0)
        {
            XmlKit.Attribute(writer, Names.StyleId, style.Id);
        }

        if (style.DefaultAttribute is not null)
        {
            XmlKit.Attribute(writer, Names.Default, style.DefaultAttribute);
        }

        var entries = new List<PartXml.Entry>();
        PartXml.AddValue(entries, style, "name", Tie("name"), style.Name);
        PartXml.AddValue(entries, style, "basedOn", Tie("basedOn"), style.BasedOn);
        PartXml.AddValue(entries, style, "next", Tie("next"), style.Next);
        PartXml.AddValue(entries, style, "link", Tie("link"), style.Link);
        PartXml.AddBag(entries, style, "pPr", Tie("pPr"), style.ParagraphProperties);
        PartXml.AddBag(entries, style, "rPr", Tie("rPr"), style.RunProperties);
        PartXml.AddBag(entries, style, "tblPr", Tie("tblPr"), style.TableProperties);
        PartXml.AddKept(entries, style);
        PartXml.Flush(writer, entries);
        writer.WriteEndElement();
    }
}
