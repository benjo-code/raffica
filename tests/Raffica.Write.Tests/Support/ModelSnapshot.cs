using System.Xml.Linq;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;

namespace Raffica.Write.Tests.Support;

/// <summary>Every loaded part of a <see cref="WordDocument"/> written by the codecs and parsed to <see cref="XElement"/>, for equivalence checks.</summary>
internal static class ModelSnapshot
{
    public static XElement Of(WordDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var root = new XElement("model");
        root.Add(Part("document", s => DocumentXml.Write(model.Body, s)));
        if (model.Styles is { } styles)
        {
            root.Add(Part("styles", s => StylesXml.Write(styles, s)));
        }

        if (model.Numbering is { } numbering)
        {
            root.Add(Part("numbering", s => NumberingXml.Write(numbering, s)));
        }

        if (model.Settings is { } settings)
        {
            root.Add(Part("settings", s => SettingsXml.Write(settings, s)));
        }

        if (model.Footnotes is { } footnotes)
        {
            root.Add(Part("footnotes", s => NotesXml.Write(footnotes, s)));
        }

        if (model.Endnotes is { } endnotes)
        {
            root.Add(Part("endnotes", s => NotesXml.Write(endnotes, s)));
        }

        if (model.Comments is { } comments)
        {
            using var extended = new MemoryStream();
            var needed = false;
            root.Add(Part("comments", s => needed = CommentsXml.Write(comments, s, extended)));
            if (needed)
            {
                root.Add(Part("commentsExtended", s => extended.WriteTo(s)));
            }
        }

        foreach (var (id, part) in model.HeadersFooters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var element = Part("headerFooter", s => StoryXml.WritePart(part, s));
            element.SetAttributeValue("id", id);
            root.Add(element);
        }

        return root;
    }

    private static XElement Part(string name, Action<Stream> write)
    {
        using var stream = new MemoryStream();
        write(stream);
        stream.Position = 0;
        var element = new XElement("part", new XAttribute("name", name));
        element.Add(XElement.Load(stream));
        return element;
    }
}
