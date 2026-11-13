using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>
/// The extended comments part (<c>w15:commentsEx</c>): one <c>w15:commentEx</c> per comment, joined to the comment on the
/// <c>w14:paraId</c> of its last paragraph. An entry that matches no comment, repeats a key, has content or is not a
/// <c>commentEx</c> is kept in place (<see cref="CommentCollection.ExtendedKeptChildren"/>) and written back.
/// </summary>
internal static class CommentsExtendedXml
{
    private static readonly XNamespace s_w15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private static readonly XName s_entry = s_w15 + "commentEx";
    private static readonly XName s_paraId = s_w15 + "paraId";
    private static readonly XName s_parent = s_w15 + "paraIdParent";
    private static readonly XName s_done = s_w15 + "done";

    /// <summary>Reads the part and gives every matching comment its state. Called by <see cref="CommentsXml.Read"/>.</summary>
    public static void Read(Stream stream, CommentCollection model)
    {
        var byParaId = new Dictionary<string, Comment>(StringComparer.Ordinal);
        foreach (var comment in model.Comments)
        {
            if (comment.ParaId is { } key)
            {
                byParaId.TryAdd(key, comment);
            }
        }

        var claimed = new HashSet<Comment>();
        PartXml.Read(
            stream,
            root =>
            {
                model.ExtendedRoot = root;
                return model;
            },
            (collection, element, index) =>
            {
                var key = element.Name == s_entry && !element.Nodes().Any() ? (string?)element.Attribute(s_paraId) : null;
                if (key is not null && byParaId.TryGetValue(key, out var comment) && claimed.Add(comment))
                {
                    comment.ExtendedPosition = index;
                    foreach (var attribute in element.Attributes())
                    {
                        if (!attribute.IsNamespaceDeclaration && attribute.Name == s_parent)
                        {
                            comment.ParentParaId = attribute.Value;
                        }
                        else if (!attribute.IsNamespaceDeclaration && attribute.Name == s_done)
                        {
                            comment.DoneText = attribute.Value;
                        }
                        else if (attribute.Name != s_paraId)
                        {
                            comment.ExtendedAttributes.Add(new XAttribute(attribute));
                        }
                    }
                }
                else
                {
                    collection.ExtendedKeptChildren.Add(new KeptChild(index, new UnknownContent(element)));
                }
            });
    }

    /// <summary>True when an extended part was read, something of it is kept, or some comment has an entry, done state or a parent (and a paragraph key to write it under).</summary>
    public static bool IsNeeded(CommentCollection model) =>
        model.ExtendedRoot is not null || model.ExtendedKeptChildren.Count > 0 || model.Comments.Any(c => c.HasExtendedState && c.ParaId is not null);

    public static void Write(CommentCollection model, Stream stream)
    {
        var root = model.ExtendedRoot ?? new OoxmlRoot(s_w15 + "commentsEx", [new XAttribute(XNamespace.Xmlns + "w15", s_w15.NamespaceName)]);
        PartXml.Write(stream, root, writer =>
        {
            var entries = new List<PartXml.Entry>();
            foreach (var comment in model.Comments)
            {
                if (comment.HasExtendedState && comment.ParaId is { } key)
                {
                    entries.Add(new PartXml.Entry(comment.ExtendedPosition, 0, w =>
                    {
                        XmlKit.Start(w, s_entry);
                        XmlKit.Attribute(w, s_paraId, key);
                        if (comment.ParentParaId is not null)
                        {
                            XmlKit.Attribute(w, s_parent, comment.ParentParaId);
                        }

                        if (comment.DoneText is not null)
                        {
                            XmlKit.Attribute(w, s_done, comment.DoneText);
                        }

                        XmlKit.Attributes(w, comment.ExtendedAttributes);
                        w.WriteEndElement();
                    }));
                }
            }

            foreach (var kept in model.ExtendedKeptChildren)
            {
                var content = kept.Content;
                entries.Add(new PartXml.Entry(kept.Position, null, w => XmlKit.Kept(w, content)));
            }

            PartXml.Flush(writer, entries);
        });
    }
}
