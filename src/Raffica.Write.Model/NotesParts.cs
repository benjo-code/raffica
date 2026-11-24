using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>Which notes part: the footnotes (<c>w:footnotes</c>) or the endnotes (<c>w:endnotes</c>). (<see cref="NoteKind"/> is the kind of one note: normal or a separator.)</summary>
public enum NoteType
{
    Footnote,
    Endnote,
}

/// <summary>
/// Makes the footnotes or endnotes part of a document when it has none. The new part holds the separator (<c>w:id</c> -1) and the continuation separator (0) that Word expects, and the
/// settings part names both (<c>w:footnotePr</c> or <c>w:endnotePr</c> with two <c>w:footnote</c> or <c>w:endnote</c> children). The save writes the part, its relationship and its content
/// type as a created part, like a new header or footer.
/// </summary>
public static class NotesParts
{
    /// <summary>The id of the separator note.</summary>
    public const int SeparatorId = -1;

    /// <summary>The id of the continuation separator note.</summary>
    public const int ContinuationSeparatorId = 0;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// The notes part of <paramref name="type"/>, made (and set on the document) when the document has none. A part that exists is returned as it is, and so is its settings entry (a file with
    /// separators only reuses them). A document with no settings part gets one, because the separators are named there.
    /// </summary>
    public static NoteCollection Ensure(WordDocument document, NoteType type)
    {
        ArgumentNullException.ThrowIfNull(document);
        var footnotes = type switch
        {
            NoteType.Footnote => true,
            NoteType.Endnote => false,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown notes part."),
        };

        var existing = footnotes ? document.Footnotes : document.Endnotes;
        if (existing is not null)
        {
            return existing;
        }

        var root = footnotes ? "footnotes" : "endnotes";
        var item = footnotes ? "footnote" : "endnote";
        var collection = new NoteCollection(new OoxmlRoot(W + root, [new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName)]));
        collection.Notes.Add(Separator(SeparatorId, NoteKind.Separator, "separator", item));
        collection.Notes.Add(Separator(ContinuationSeparatorId, NoteKind.ContinuationSeparator, "continuationSeparator", item));
        if (footnotes)
        {
            document.Footnotes = collection;
        }
        else
        {
            document.Endnotes = collection;
        }

        NameSeparators(document, item);
        return collection;
    }

    private static Note Separator(int id, NoteKind kind, string mark, string item)
    {
        var note = new Note(id, kind, new Story(new OoxmlRoot(W + item, [])));
        var paragraph = new Paragraph();
        paragraph.Properties.Set(W + "spacing", new XElement(W + "spacing", new XAttribute(W + "after", "0"), new XAttribute(W + "line", "240"), new XAttribute(W + "lineRule", "auto")));
        var run = new Run();
        run.Content.Add(new UnknownRunContent(new UnknownContent(new XElement(W + mark))));
        paragraph.Inlines.Add(run);
        note.Content.Blocks.Add(paragraph);
        return note;
    }

    // The settings name the two separator notes: <w:footnotePr><w:footnote w:id="-1"/><w:footnote w:id="0"/></w:footnotePr>. An existing footnotePr keeps its other children (numFmt, pos...).
    private static void NameSeparators(WordDocument document, string item)
    {
        document.Settings ??= new DocumentSettings(new OoxmlRoot(W + "settings", [new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName)]));
        var properties = W + item + "Pr";
        var holder = document.Settings.Properties.Get(properties);
        if (holder is null)
        {
            holder = new XElement(properties);
            document.Settings.Properties.Set(properties, holder);
        }

        foreach (var id in new[] { SeparatorId, ContinuationSeparatorId })
        {
            var text = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!holder.Elements(W + item).Any(e => (string?)e.Attribute(W + "id") == text))
            {
                holder.Add(new XElement(W + item, new XAttribute(W + "id", text)));
            }
        }
    }
}
