using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>A header or footer part: a story whose root is <c>w:hdr</c> or <c>w:ftr</c>.</summary>
public sealed class HeaderFooterPart
{
    public HeaderFooterPart(Story content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var name = content.Root.Name;
        if (name.Namespace != XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") || name.LocalName is not ("hdr" or "ftr"))
        {
            throw new ArgumentException("The root of a header or footer is w:hdr or w:ftr.", nameof(content));
        }

        Content = content;
    }

    public Story Content { get; }

    /// <summary>Decided by the root element name.</summary>
    public HeaderFooterKind Kind => Content.Root.Name.LocalName == "ftr" ? HeaderFooterKind.Footer : HeaderFooterKind.Header;
}
