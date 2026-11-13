using System.Globalization;
using System.Xml;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>
/// The footnotes and endnotes parts: a stream in, a <see cref="NoteCollection"/> out, and back. Each note holds its blocks as
/// a story; what is not a note with a usable id (unknown children, a note without an id or with an id that would not write
/// back as the same text) is kept in place whole.
/// </summary>
internal static class NotesXml
{
    private static readonly string[] s_rootOrder = ["footnote", "endnote"];

    /// <summary>Reads a part whose root is <c>footnotes</c> or <c>endnotes</c> (any other root is an <see cref="XmlException"/>).</summary>
    public static NoteCollection Read(Stream stream, string rootLocalName)
    {
        ArgumentNullException.ThrowIfNull(rootLocalName);
        var itemName = ItemName(rootLocalName);
        return PartXml.ReadStreaming(
            stream,
            root =>
            {
                if (root.Name != Names.W + rootLocalName)
                {
                    throw new XmlException($"The part's root is not w:{rootLocalName}.");
                }

                return new NoteCollection(root);
            },
            (model, reader, index) =>
            {
                if (XmlKit.IsWord(reader, itemName) && ReadNote(reader, itemName, index) is { } note)
                {
                    model.Notes.Add(note);
                }
                else
                {
                    PartXml.Keep(model, XmlKit.ReadKept(reader), index);
                }
            });
    }

    private static string ItemName(string rootLocalName) => rootLocalName switch
    {
        "footnotes" => "footnote",
        "endnotes" => "endnote",
        _ => throw new ArgumentException("The root is footnotes or endnotes.", nameof(rootLocalName)),
    };

    /// <summary>Null (the reader has not moved) when the element has no canonical id.</summary>
    private static Note? ReadNote(XmlReader reader, string itemName, int position)
    {
        var attributes = XmlKit.ReadAttributes(reader);
        if (!PartXml.TakeItemId(attributes, out var id))
        {
            return null;
        }

        var kind = NoteKind.Normal;
        var typeIndex = attributes.FindIndex(a => a.Name == Names.Type);
        if (typeIndex >= 0)
        {
            var typed = attributes[typeIndex].Value switch
            {
                "separator" => (NoteKind?)NoteKind.Separator,
                "continuationSeparator" => NoteKind.ContinuationSeparator,
                "continuationNotice" => NoteKind.ContinuationNotice,
                _ => null,
            };
            if (typed is not null)
            {
                kind = typed.Value;
                attributes.RemoveAt(typeIndex);
            }
        }

        var note = new Note(id, kind, new Story(new OoxmlRoot(Names.W + itemName, []))) { Position = position };
        XmlKit.KeepRest(note, attributes);
        StoryXml.ReadBlocks(reader, note.Content.Blocks);
        return note;
    }

    /// <summary>Writes UTF-8 without a byte order mark and an XML declaration with <c>standalone="yes"</c>. The stream stays open.</summary>
    public static void Write(NoteCollection model, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(model);
        var itemName = ItemName(model.Root.Name.LocalName);
        PartXml.Write(stream, model.Root, writer =>
        {
            var entries = new List<PartXml.Entry>();
            foreach (var note in model.Notes)
            {
                entries.Add(new PartXml.Entry(note.Position, 0, w => WriteNote(note, itemName, w)));
            }

            PartXml.AddKept(entries, model, PartXml.OrderOf(s_rootOrder));
            PartXml.Flush(writer, entries);
        });
    }

    private static void WriteNote(Note note, string itemName, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + itemName);
        XmlKit.Attribute(writer, Names.Id, note.Id.ToString(CultureInfo.InvariantCulture));
        if (note.Kind != NoteKind.Normal)
        {
            XmlKit.Attribute(writer, Names.Type, note.Kind switch
            {
                NoteKind.Separator => "separator",
                NoteKind.ContinuationSeparator => "continuationSeparator",
                _ => "continuationNotice",
            });
        }

        // A w:type that was kept as unknown (an unknown spelling) yields to a type set in code.
        XmlKit.Attributes(writer, note.UnknownAttributes.Where(a => note.Kind == NoteKind.Normal || a.Name != Names.Type));
        StoryXml.WriteBlocks(note.Content.Blocks, writer);
        writer.WriteEndElement();
    }
}
