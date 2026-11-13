namespace Raffica.Write.Model;

/// <summary>
/// An element whose children are a mix of typed members and kept unknown elements in a file order that must survive a save.
/// Every child read gets its place among its siblings (<see cref="Position"/> for list members, <see cref="ChildPositions"/>
/// for single typed children, <see cref="KeptChild.Position"/> for kept ones); the writer sorts by it. Anything created in
/// code has <see cref="NewPosition"/> and goes after what was read, in the order of the schema.
/// </summary>
public abstract class OrderedElement : OoxmlElement
{
    /// <summary>The position of something that was not read from a file: after every read child.</summary>
    public const int NewPosition = int.MaxValue;

    /// <summary>The place of this element among the children of its parent, as read; <see cref="NewPosition"/> for one made in code.</summary>
    public int Position { get; set; } = NewPosition;

    /// <summary>Child elements the model does not understand, each with its place.</summary>
    public IList<KeptChild> KeptChildren { get; } = new List<KeptChild>();

    /// <summary>The place of each single typed child (key: the local name) among the children of this element, as read.</summary>
    public IDictionary<string, int> ChildPositions { get; } = new Dictionary<string, int>();

    /// <summary>The place of the single typed child with this key; <see cref="NewPosition"/> when it was not read.</summary>
    public int PositionOf(string key) => ChildPositions.TryGetValue(key, out var position) ? position : NewPosition;
}

/// <summary>An unknown child element and where it stood among its siblings.</summary>
public sealed class KeptChild
{
    public KeptChild(int position, UnknownContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Position = position;
        Content = content;
    }

    public int Position { get; }

    public UnknownContent Content { get; }
}
