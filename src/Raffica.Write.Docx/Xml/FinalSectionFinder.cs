using System.Xml;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>
/// Cuts the body-level <c>w:sectPr</c> (the last child of <c>w:body</c>) out of the decompressed bytes of a main document part without
/// reading the body: it works backwards from the end tags of <c>w:body</c> and <c>w:document</c>, with any namespace prefix. It
/// answers only what it can prove from the bytes; anything it cannot place (a comment or CDATA next to the section, content after the
/// body, a part that is not UTF-8) is <see cref="FinalSectionKind.Unsupported"/> and the caller reads the whole part instead.
/// </summary>
internal static class FinalSectionFinder
{
    /// <param name="xml">The decompressed part.</param>
    /// <param name="root">The root element as read (its namespace declarations serve the section, which is read alone).</param>
    /// <param name="section">The final section for <see cref="FinalSectionKind.Found"/>.</param>
    /// <param name="bodyEnd">The offset of the end tag of the body (valid unless <see cref="FinalSectionKind.Unsupported"/>).</param>
    /// <param name="contentEnd">The offset where the blocks of the body end: the start of the final section, or <paramref name="bodyEnd"/>.</param>
    public static FinalSectionKind Find(ReadOnlySpan<byte> xml, OoxmlRoot root, out SectionProperties? section, out int bodyEnd, out int contentEnd)
    {
        section = null;
        var cut = Locate(xml, out var start, out var end, out bodyEnd);
        contentEnd = cut == FinalSectionKind.Found ? start : bodyEnd;
        if (cut != FinalSectionKind.Found)
        {
            return cut;
        }

        try
        {
            section = Parse(xml[start..end], root);
            return section is null ? FinalSectionKind.Unsupported : FinalSectionKind.Found;
        }
        catch (XmlException)
        {
            return FinalSectionKind.Unsupported;
        }
    }

    private static SectionProperties? Parse(ReadOnlySpan<byte> fragment, OoxmlRoot root) =>
        ReadFragment(fragment, root, reader => XmlKit.IsWord(reader, "sectPr") ? StoryXml.ReadFinalSection(reader) : null);

    /// <summary>
    /// Reads one element cut out of a part on its own, with the namespace declarations of the part's root in scope (the prefixes the
    /// element uses are declared there). The reader is on the element. An <see cref="XmlException"/> means the cut was not an element.
    /// </summary>
    public static T? ReadFragment<T>(ReadOnlySpan<byte> fragment, OoxmlRoot root, Func<XmlReader, T?> read)
    {
        var names = new NameTable();
        var scope = new XmlNamespaceManager(names);
        foreach (var attribute in root.Attributes)
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            scope.AddNamespace(attribute.Name.Namespace == XNamespace.Xmlns ? attribute.Name.LocalName : string.Empty, attribute.Value);
        }

        var settings = XmlKit.ReaderSettings();
        settings.ConformanceLevel = ConformanceLevel.Fragment;
        using var stream = new MemoryStream(fragment.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, settings, new XmlParserContext(names, scope, null, XmlSpace.None));
        reader.MoveToContent();
        return reader.NodeType == XmlNodeType.Element ? read(reader) : default;
    }

    private static FinalSectionKind Locate(ReadOnlySpan<byte> xml, out int start, out int end, out int bodyEnd)
    {
        start = end = bodyEnd = 0;

        // The last tag is the end of w:document, the one before it the end of w:body, with nothing but white space between.
        var documentEnd = xml.LastIndexOf("</"u8);
        if (documentEnd < 0 || !IsEndTagOf(xml, documentEnd, "document"u8) || !OnlySpace(xml[(TagEnd(xml, documentEnd) + 1)..]))
        {
            return FinalSectionKind.Unsupported;
        }

        bodyEnd = xml[..documentEnd].LastIndexOf("</"u8);
        if (bodyEnd < 0 || !IsEndTagOf(xml, bodyEnd, "body"u8) || !OnlySpace(xml[(TagEnd(xml, bodyEnd) + 1)..documentEnd]))
        {
            return FinalSectionKind.Unsupported;
        }

        // The last child of the body is the one that ends right before the end of w:body.
        var last = TrimEnd(xml[..bodyEnd]);
        if (last == 0 || xml[last - 1] != (byte)'>')
        {
            return FinalSectionKind.Unsupported;
        }

        var open = xml[..last].LastIndexOf((byte)'<');
        if (open < 0 || open + 1 >= last || xml[open + 1] is (byte)'!' or (byte)'?')
        {
            return FinalSectionKind.Unsupported;
        }

        var closing = xml[open + 1] == (byte)'/';
        var selfClosed = !closing && last >= 2 && xml[last - 2] == (byte)'/';
        var local = LocalName(xml, open + (closing ? 2 : 1));
        if (!local.SequenceEqual("sectPr"u8))
        {
            // The body is empty (its start tag ends right here) or its last child is something else: no final section.
            return closing || selfClosed || local.SequenceEqual("body"u8) ? FinalSectionKind.None : FinalSectionKind.Unsupported;
        }

        if (selfClosed)
        {
            start = open;
            end = last;
            return FinalSectionKind.Found;
        }

        if (!closing)
        {
            return FinalSectionKind.Unsupported;
        }

        // The end tag of a w:sectPr; its start tag is the matching one (a w:sectPrChange inside holds another w:sectPr).
        var depth = 1;
        var limit = open;
        while (depth > 0)
        {
            var tag = xml[..limit].LastIndexOf((byte)'<');
            if (tag < 0 || tag + 1 >= limit || xml[tag + 1] is (byte)'!' or (byte)'?')
            {
                return FinalSectionKind.Unsupported;
            }

            var isEnd = xml[tag + 1] == (byte)'/';
            if (LocalName(xml, tag + (isEnd ? 2 : 1)).SequenceEqual("sectPr"u8))
            {
                if (isEnd)
                {
                    depth++;
                }
                else if (!IsSelfClosed(xml, tag))
                {
                    depth--;
                    if (depth == 0)
                    {
                        start = tag;
                        end = last;
                        return FinalSectionKind.Found;
                    }
                }
            }

            limit = tag;
        }

        return FinalSectionKind.Unsupported;
    }

    private static bool IsSelfClosed(ReadOnlySpan<byte> xml, int tag)
    {
        var close = TagEnd(xml, tag);
        return close > tag && close <= xml.Length && close - 1 >= 0 && xml[close - 1] == (byte)'/';
    }

    private static int TagEnd(ReadOnlySpan<byte> xml, int tag)
    {
        var close = xml[tag..].IndexOf((byte)'>');
        return close < 0 ? xml.Length : tag + close;
    }

    private static bool IsEndTagOf(ReadOnlySpan<byte> xml, int tag, ReadOnlySpan<byte> local) =>
        LocalName(xml, tag + 2).SequenceEqual(local);

    /// <summary>The name of the tag that starts at <paramref name="from"/> (after the opening angle bracket or after the bracket and slash) without its prefix.</summary>
    private static ReadOnlySpan<byte> LocalName(ReadOnlySpan<byte> xml, int from)
    {
        var i = from;
        var colon = -1;
        while (i < xml.Length && xml[i] is not ((byte)'>' or (byte)'/' or (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
        {
            if (xml[i] == (byte)':')
            {
                colon = i;
            }

            i++;
        }

        return xml[(colon < 0 ? from : colon + 1)..i];
    }

    private static bool OnlySpace(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                return false;
            }
        }

        return true;
    }

    private static int TrimEnd(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.Length;
        while (end > 0 && bytes[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            end--;
        }

        return end;
    }
}

/// <summary>What <see cref="FinalSectionFinder"/> could prove.</summary>
internal enum FinalSectionKind
{
    /// <summary>The last child of the body is a <c>w:sectPr</c>; it was cut out and read.</summary>
    Found,

    /// <summary>The body has no <c>w:sectPr</c> as its last child.</summary>
    None,

    /// <summary>The bytes could not be placed with certainty; read the whole part instead.</summary>
    Unsupported,
}
