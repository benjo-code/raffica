using System.Globalization;
using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>The comments part (<c>w:comments</c>) joined with the extended part (<c>w15:commentsEx</c>) when there is one.</summary>
public sealed class CommentCollection : OrderedElement, IEnumerable<Comment>
{
    public CommentCollection(OoxmlRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
    }

    private static readonly XNamespace s_w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace s_w14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XNamespace s_w15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private static readonly XNamespace s_mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    public OoxmlRoot Root { get; }

    /// <summary>The comments are written at the position they were read in; comments added in code go after the last read comment.</summary>
    public IList<Comment> Comments { get; } = new List<Comment>();

    /// <summary>The root of the extended part as read; null when the file had none (a default root is made when one is written).</summary>
    public OoxmlRoot? ExtendedRoot { get; set; }

    /// <summary>Children of the extended part that matched no comment (and unknown elements), kept with their place; the extended part writes them back.</summary>
    public IList<KeptChild> ExtendedKeptChildren { get; } = new List<KeptChild>();

    /// <summary>The first comment with this id, or null.</summary>
    public Comment? Find(int id)
    {
        foreach (var comment in Comments)
        {
            if (comment.Id == id)
            {
                return comment;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the <see cref="Comment.ParaId"/> of the comment; when its last top-level paragraph has none, writes a new one
    /// onto that paragraph first (an empty paragraph is added when the comment has none). The value is 8 upper-case hex
    /// digits below 0x80000000 (the OOXML limit) and differs from every paragraph id and every parent id of the collection
    /// and every key of a kept extended entry. The root declares <c>w14</c> (and lists it in an existing
    /// <c>mc:Ignorable</c>) so the attribute serializes as <c>w14:paraId</c>.
    /// </summary>
    public string EnsureParaId(Comment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        if (comment.ParaId is { } existing)
        {
            return existing;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in Comments)
        {
            foreach (var block in other.Content.Blocks)
            {
                if (block is Paragraph paragraph && paragraph.UnknownAttributes.FirstOrDefault(a => a.Name == s_w14 + "paraId") is { } attribute)
                {
                    used.Add(attribute.Value);
                }
            }

            if (other.ParentParaId is not null)
            {
                used.Add(other.ParentParaId);
            }
        }

        foreach (var kept in ExtendedKeptChildren)
        {
            if ((string?)kept.Content.Element.Attribute(s_w15 + "paraId") is { } key)
            {
                used.Add(key);
            }
        }

        string id;
        do
        {
            id = Random.Shared.Next(1, int.MaxValue).ToString("X8", CultureInfo.InvariantCulture);
        }
        while (!used.Add(id));

        Paragraph? target = null;
        for (var i = comment.Content.Blocks.Count - 1; i >= 0 && target is null; i--)
        {
            target = comment.Content.Blocks[i] as Paragraph;
        }

        if (target is null)
        {
            target = new Paragraph();
            comment.Content.Blocks.Add(target);
        }

        target.UnknownAttributes.Add(new XAttribute(s_w14 + "paraId", id));
        DeclareW14();
        return id;
    }

    private void DeclareW14()
    {
        var declared = Root.Attributes.Any(a => a.IsNamespaceDeclaration && a.Value == s_w14.NamespaceName);
        if (!declared)
        {
            if (Root.Attributes.Any(a => a.Name == XNamespace.Xmlns + "w14"))
            {
                return; // the prefix is taken for another namespace; the writer picks its own prefix.
            }

            Root.Attributes.Add(new XAttribute(XNamespace.Xmlns + "w14", s_w14.NamespaceName));
        }

        var ignorable = Root.Attributes.FirstOrDefault(a => a.Name == s_mc + "Ignorable");
        var prefix = Root.Attributes.First(a => a.IsNamespaceDeclaration && a.Value == s_w14.NamespaceName).Name.LocalName;
        if (ignorable is not null && !ignorable.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(prefix))
        {
            var at = Root.Attributes.IndexOf(ignorable);
            Root.Attributes[at] = new XAttribute(ignorable.Name, ignorable.Value + " " + prefix);
        }
    }

    public IEnumerator<Comment> GetEnumerator() => Comments.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
