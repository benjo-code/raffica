namespace Raffica.Write.Model;

/// <summary>The comments part (<c>w:comments</c>) joined with the extended part (<c>w15:commentsEx</c>) when there is one.</summary>
public sealed class CommentCollection : OrderedElement, IEnumerable<Comment>
{
    public CommentCollection(OoxmlRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
    }

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

    public IEnumerator<Comment> GetEnumerator() => Comments.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
