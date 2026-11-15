namespace Raffica.Write.Model;

/// <summary>A <c>w:r</c>: run properties and the content in order.</summary>
public sealed class Run : Inline
{
    public PropertyBag Properties { get; } = new(PropertyKind.Run);

    public IList<RunContent> Content { get; } = new List<RunContent>();

    public override Run Clone()
    {
        var copy = new Run();
        Properties.CopyStateTo(copy.Properties);
        CopyAttributesTo(copy);
        foreach (var content in Content)
        {
            copy.Content.Add(content.Clone());
        }

        return copy;
    }
}
