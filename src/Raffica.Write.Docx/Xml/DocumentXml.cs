using System.Text;
using System.Xml;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>The main document part (<c>word/document.xml</c>): a stream in, a <see cref="Story"/> out, and back.</summary>
internal static class DocumentXml
{
    public static Story Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = XmlReader.Create(stream, XmlKit.ReaderSettings());
        return StoryXml.Read(reader);
    }

    /// <summary>
    /// Reads the start of a document: the first <paramref name="maxBlocks"/> top-level blocks of the body, without parsing the rest.
    /// The model's <see cref="Story.FinalSection"/> is then the properties of the section that holds the last block read: those of the
    /// first block after the prefix that ends a section (read alone, like the final section), or, when no section ends after it, the
    /// body's own final section (found from the end of <paramref name="bytes"/>, see <see cref="FinalSectionFinder"/>). So every
    /// page the layout finishes inside the prefix has the section it has in the whole document. Null when the bytes cannot be cut
    /// with certainty (the caller then reads everything). <paramref name="truncated"/> is false when the whole body is in the model.
    /// </summary>
    public static Story? ReadPrefix(byte[] bytes, int maxBlocks, out bool truncated)
    {
        truncated = false;
        if (ReadRoot(bytes) is not { } root || root.Name != Names.Document)
        {
            return null;
        }

        var kind = FinalSectionFinder.Find(bytes, root, out var final, out var bodyEnd, out var contentEnd);
        if (kind == FinalSectionKind.Unsupported)
        {
            return null;
        }

        var scanner = new BodyScanner(bytes, contentEnd);
        if (!scanner.TryEnterBody())
        {
            return null;
        }

        // The children of the body, scanned as far as they are needed.
        var children = new List<(int Start, int End)>();
        var failed = false;
        bool More(int index)
        {
            while (children.Count <= index)
            {
                if (!scanner.NextChild(out var start, out var end, out var bad))
                {
                    failed = bad;
                    return false;
                }

                children.Add((start, end));
            }

            return true;
        }

        if (!More(maxBlocks))
        {
            if (failed)
            {
                return null;
            }

            // The whole body is within the limit.
            using var whole = new MemoryStream(bytes, writable: false);
            return Read(whole);
        }

        // The section the prefix ends in: the next block that ends a section decides. A mention of a section in a block that is no
        // section break (a table, a drawing) is passed over, as the layout passes over it.
        var cut = children[maxBlocks].Start;
        var open = kind == FinalSectionKind.Found ? final : null;
        var from = cut;
        var holder = maxBlocks;
        while (bytes.AsSpan(from, contentEnd - from).IndexOf("sectPr"u8) is var rest and >= 0)
        {
            var mention = from + rest;
            while (More(holder) && children[holder].End <= mention)
            {
                holder++;
            }

            if (!More(holder))
            {
                return null;
            }

            var (start, end) = children[holder];
            Block? block;
            try
            {
                block = FinalSectionFinder.ReadFragment(bytes.AsSpan(start, end - start), root, StoryXml.ReadBlockFragment);
            }
            catch (XmlException)
            {
                return null;
            }

            if (block is not null && FirstSectionBreak(block) is { } ending)
            {
                open = ending;
                break;
            }

            from = end;
            holder++;
        }

        var story = ReadPrefixBytes(bytes, cut, bodyEnd);
        story.FinalSection = open;
        truncated = true;
        return story;
    }

    private static OoxmlRoot? ReadRoot(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, XmlKit.ReaderSettings());
        reader.MoveToContent();
        return reader.NodeType == XmlNodeType.Element ? new OoxmlRoot(XNamespace.Get(reader.NamespaceURI) + reader.LocalName, XmlKit.ReadAttributes(reader)) : null;
    }

    /// <summary>The part up to <paramref name="cut"/> closed with the end tags of the original, read as a document.</summary>
    private static Story ReadPrefixBytes(byte[] bytes, int cut, int bodyEnd)
    {
        var closing = bytes.Length - bodyEnd;
        var prefix = new byte[cut + closing];
        Buffer.BlockCopy(bytes, 0, prefix, 0, cut);
        Buffer.BlockCopy(bytes, bodyEnd, prefix, cut, closing);
        using var stream = new MemoryStream(prefix, writable: false);
        return Read(stream);
    }

    /// <summary>The section break of the first paragraph that has one, looking into block content controls as the layout does.</summary>
    private static SectionProperties? FirstSectionBreak(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                return paragraph.SectionBreak;
            case ContentControlBlock control:
                foreach (var inner in control.Content)
                {
                    if (FirstSectionBreak(inner) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>The settings of every write of the main part (the snapshot cache uses the same ones, so its bytes equal a full write).</summary>
    internal static XmlWriterSettings WriterSettings() => new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = false,
        NewLineHandling = NewLineHandling.Entitize,
        CloseOutput = false,
    };

    /// <summary>Writes UTF-8 without a byte order mark and an XML declaration with <c>standalone="yes"</c>. The stream stays open.</summary>
    public static void Write(Story story, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = XmlWriter.Create(stream, WriterSettings());
        writer.WriteStartDocument(true);
        StoryXml.Write(story, writer);
        writer.WriteEndDocument();
    }
}
