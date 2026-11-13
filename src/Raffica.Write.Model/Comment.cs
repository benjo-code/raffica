using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>One <c>w:comment</c> and, when the file has one, its <c>w15:commentEx</c> entry (done state, parent comment).</summary>
public sealed class Comment : OrderedElement
{
    private static readonly XName s_paraId = XNamespace.Get("http://schemas.microsoft.com/office/word/2010/wordml") + "paraId";

    public Comment(int id, Story content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Id = id;
        Content = content;
    }

    public int Id { get; set; }

    public string? Author { get; set; }

    public string? Initials { get; set; }

    /// <summary>The date exactly as written. The text is the stored form so a save is lossless; <see cref="Date"/> is the parsed view.</summary>
    public string? DateText { get; set; }

    public DateTimeOffset? Date => RevisionInfo.Parse(DateText);

    /// <summary>The blocks of the comment. The story's root is only a name here (<c>w:comment</c>).</summary>
    public Story Content { get; }

    /// <summary>
    /// The <c>w14:paraId</c> of the last paragraph of the comment, read from that paragraph (the attribute stays on the
    /// paragraph, there is one copy); null when the comment has no paragraph or the paragraph has none. This is the key
    /// that joins the comment to its <c>w15:commentEx</c> entry.
    /// </summary>
    public string? ParaId
    {
        get
        {
            for (var i = Content.Blocks.Count - 1; i >= 0; i--)
            {
                if (Content.Blocks[i] is Paragraph paragraph)
                {
                    return paragraph.UnknownAttributes.FirstOrDefault(a => a.Name == s_paraId)?.Value;
                }
            }

            return null;
        }
    }

    /// <summary>The <c>w15:paraIdParent</c>: the <see cref="ParaId"/> of the comment this one replies to; null for none.</summary>
    public string? ParentParaId { get; set; }

    /// <summary>The <c>w15:done</c> text as read (null when absent); written back unchanged until <see cref="Done"/> changes.</summary>
    public string? DoneText { get; set; }

    public bool Done
    {
        get => DoneText is "1" or "true" or "on";
        set
        {
            if (value != Done)
            {
                DoneText = value ? "1" : "0";
            }
        }
    }

    /// <summary>Where the <c>w15:commentEx</c> entry stood among the children of its part; <see cref="OrderedElement.NewPosition"/> when there was none.</summary>
    public int ExtendedPosition { get; set; } = NewPosition;

    /// <summary>The attributes of the <c>w15:commentEx</c> entry the model does not name (written back after the typed ones).</summary>
    public IList<XAttribute> ExtendedAttributes { get; } = new List<XAttribute>();

    /// <summary>True when the comment has something to write into the extended part: an entry was read, or done state or a parent was set.</summary>
    public bool HasExtendedState => ExtendedPosition != NewPosition || DoneText is not null || ParentParaId is not null;
}
