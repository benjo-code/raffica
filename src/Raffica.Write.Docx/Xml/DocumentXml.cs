using System.Text;
using System.Xml;
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

    /// <summary>Writes UTF-8 without a byte order mark and an XML declaration with <c>standalone="yes"</c>. The stream stays open.</summary>
    public static void Write(Story story, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(stream);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            NewLineHandling = NewLineHandling.Entitize,
            CloseOutput = false,
        };
        using var writer = XmlWriter.Create(stream, settings);
        writer.WriteStartDocument(true);
        StoryXml.Write(story, writer);
        writer.WriteEndDocument();
    }
}
