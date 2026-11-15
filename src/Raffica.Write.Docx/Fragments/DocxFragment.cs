using System.Xml;
using System.Xml.Linq;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;

namespace Raffica.Write.Docx;

/// <summary>The bytes are not a readable clipboard fragment, or exceed a limit of <see cref="DocxFragment"/>. The message never holds text of the bytes.</summary>
public sealed class FragmentFormatException : FormatException
{
    public FragmentFormatException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The private clipboard format of Write: a <c>w:document</c> with a <c>w:body</c> that holds paragraphs, written and read by the same
/// block codec as the document parts (direct formatting, unknown content kept as raw XML). A fragment never carries a section break
/// (<c>w:sectPr</c> inside <c>w:pPr</c>), <c>w14:paraId</c> or <c>w14:textId</c>; blocks other than paragraphs are ignored on reading.
/// </summary>
/// <remarks>
/// The bytes of a fragment are untrusted (another application, or a hostile process, can put anything on the clipboard format).
/// <see cref="Read"/> refuses what exceeds <see cref="MaxBytes"/>, <see cref="MaxDepth"/> levels of nesting, <see cref="MaxElements"/>
/// elements or <see cref="MaxParagraphs"/> paragraphs, a DTD of any kind (no entity is ever expanded, nothing is resolved), a wrong root
/// and anything that is not well-formed, all with a <see cref="FragmentFormatException"/> that does not quote the content. The limits
/// are checked in a first pass that builds nothing, so a hostile fragment costs a scan and no object graph.
/// </remarks>
public static class DocxFragment
{
    /// <summary>The largest fragment that is read: 32 MiB.</summary>
    public const int MaxBytes = 32 * 1024 * 1024;

    /// <summary>The deepest element nesting that is read (the root counts as level 1).</summary>
    public const int MaxDepth = 64;

    /// <summary>The most elements of one fragment.</summary>
    public const int MaxElements = 1_000_000;

    /// <summary>The most paragraphs of one fragment (every <c>w:p</c> counts, also in nested content).</summary>
    public const int MaxParagraphs = 100_000;

    /// <summary>The bytes of the paragraphs; the paragraphs are not changed.</summary>
    public static byte[] Write(IReadOnlyList<Paragraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var root = new OoxmlRoot(Names.Document, [new XAttribute(XNamespace.Xmlns + "w", Names.WordUri)]);
        using var stream = new MemoryStream();
        PartXml.Write(stream, root, writer =>
        {
            XmlKit.Start(writer, Names.Body);
            foreach (var paragraph in paragraphs)
            {
                StoryXml.WriteBlocks([Detached(paragraph)], writer);
            }

            writer.WriteEndElement();
        });
        return stream.ToArray();
    }

    /// <summary>The paragraphs of a fragment (see the remarks of the type for what is refused).</summary>
    /// <exception cref="FragmentFormatException">The bytes are not a readable fragment or exceed a limit.</exception>
    public static IReadOnlyList<Paragraph> Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxBytes)
        {
            throw new FragmentFormatException("The fragment is too large.");
        }

        if (bytes.Length == 0)
        {
            throw new FragmentFormatException("The fragment is empty.");
        }

        var copy = bytes.ToArray();
        try
        {
            Scan(copy);
            using var reader = XmlReader.Create(new MemoryStream(copy, writable: false), Settings());
            var story = StoryXml.Read(reader);
            var result = new List<Paragraph>();
            foreach (var block in story.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    result.Add(Clean(paragraph));
                }
            }

            return result;
        }
        catch (FragmentFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException or FormatException or NotSupportedException or InvalidCastException or IOException)
        {
            throw new FragmentFormatException("The fragment is not readable.");
        }
    }

    private static XmlReaderSettings Settings()
    {
        var settings = XmlKit.ReaderSettings();
        settings.DtdProcessing = DtdProcessing.Prohibit;
        settings.XmlResolver = null;
        settings.MaxCharactersInDocument = MaxBytes;
        settings.MaxCharactersFromEntities = 0;
        return settings;
    }

    // First pass: well-formedness, root, depth, element and paragraph counts; builds nothing.
    private static void Scan(byte[] bytes)
    {
        using var reader = XmlReader.Create(new MemoryStream(bytes, writable: false), Settings());
        reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != Names.WordUri || reader.LocalName != "document")
        {
            throw new FragmentFormatException("The fragment has no w:document root.");
        }

        var elements = 0;
        var paragraphs = 0;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.Depth + 1 > MaxDepth)
            {
                throw new FragmentFormatException("The fragment is nested too deeply.");
            }

            if (++elements > MaxElements)
            {
                throw new FragmentFormatException("The fragment has too many elements.");
            }

            if (reader.LocalName == "p" && reader.NamespaceURI == Names.WordUri && ++paragraphs > MaxParagraphs)
            {
                throw new FragmentFormatException("The fragment has too many paragraphs.");
            }
        }
    }

    // A paragraph as written to a fragment: no section break and no paragraph ids (the source stays unchanged).
    private static Paragraph Detached(Paragraph paragraph)
    {
        var copy = paragraph.Clone();
        return Clean(copy);
    }

    private static Paragraph Clean(Paragraph paragraph)
    {
        paragraph.SectionBreak = null;
        for (var i = paragraph.UnknownAttributes.Count - 1; i >= 0; i--)
        {
            var name = paragraph.UnknownAttributes[i].Name;
            if (name.LocalName is "paraId" or "textId" && name.NamespaceName == "http://schemas.microsoft.com/office/word/2010/wordml")
            {
                paragraph.UnknownAttributes.RemoveAt(i);
            }
        }

        return paragraph;
    }
}
