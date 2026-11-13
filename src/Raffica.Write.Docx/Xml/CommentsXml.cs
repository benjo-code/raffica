using System.Globalization;
using System.Xml;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>
/// The comments part (<c>word/comments.xml</c>) with its optional extended part (<c>word/commentsExtended.xml</c>). A comment
/// with no canonical id and any unknown child is kept in place whole. The date stays the text that was read.
/// </summary>
internal static class CommentsXml
{
    private static readonly string[] s_rootOrder = ["comment"];

    public static CommentCollection Read(Stream comments, Stream? commentsExtended)
    {
        var model = PartXml.ReadStreaming(
            comments,
            root =>
            {
                if (root.Name != Names.W + "comments")
                {
                    throw new XmlException("The part's root is not w:comments.");
                }

                return new CommentCollection(root);
            },
            (collection, reader, index) =>
            {
                if (XmlKit.IsWord(reader, "comment") && ReadComment(reader, index) is { } comment)
                {
                    collection.Comments.Add(comment);
                }
                else
                {
                    PartXml.Keep(collection, XmlKit.ReadKept(reader), index);
                }
            });

        if (commentsExtended is not null)
        {
            CommentsExtendedXml.Read(commentsExtended, model);
        }

        return model;
    }

    /// <summary>Null (the reader has not moved) when the element has no canonical id.</summary>
    private static Comment? ReadComment(XmlReader reader, int position)
    {
        var attributes = XmlKit.ReadAttributes(reader);
        if (!PartXml.TakeItemId(attributes, out var id))
        {
            return null;
        }

        var comment = new Comment(id, new Story(new OoxmlRoot(Names.W + "comment", [])))
        {
            Position = position,
            Author = XmlKit.Take(attributes, Names.Author),
            Initials = XmlKit.Take(attributes, Names.Initials),
            DateText = XmlKit.Take(attributes, Names.Date),
        };
        XmlKit.KeepRest(comment, attributes);
        StoryXml.ReadBlocks(reader, comment.Content.Blocks);
        return comment;
    }

    /// <summary>
    /// Writes the comments part and, when needed, the extended part. The extended part is needed when one was read or any
    /// comment has done state or a parent; a comment without such state gets no entry. Returns whether it is needed; it is
    /// written to <paramref name="commentsExtended"/> when that stream is given. The streams stay open.
    /// </summary>
    public static bool Write(CommentCollection model, Stream comments, Stream? commentsExtended)
    {
        ArgumentNullException.ThrowIfNull(model);
        PartXml.Write(comments, model.Root, writer =>
        {
            var entries = new List<PartXml.Entry>();
            foreach (var comment in model.Comments)
            {
                entries.Add(new PartXml.Entry(comment.Position, 0, w => WriteComment(comment, w)));
            }

            PartXml.AddKept(entries, model, PartXml.OrderOf(s_rootOrder));
            PartXml.Flush(writer, entries);
        });

        var needed = CommentsExtendedXml.IsNeeded(model);
        if (needed && commentsExtended is not null)
        {
            CommentsExtendedXml.Write(model, commentsExtended);
        }

        return needed;
    }

    private static void WriteComment(Comment comment, XmlWriter writer)
    {
        XmlKit.Start(writer, Names.W + "comment");
        XmlKit.Attribute(writer, Names.Id, comment.Id.ToString(CultureInfo.InvariantCulture));
        if (comment.Author is not null)
        {
            XmlKit.Attribute(writer, Names.Author, comment.Author);
        }

        if (comment.Initials is not null)
        {
            XmlKit.Attribute(writer, Names.Initials, comment.Initials);
        }

        if (comment.DateText is not null)
        {
            XmlKit.Attribute(writer, Names.Date, comment.DateText);
        }

        XmlKit.UnknownAttributes(writer, comment);
        StoryXml.WriteBlocks(comment.Content.Blocks, writer);
        writer.WriteEndElement();
    }
}
