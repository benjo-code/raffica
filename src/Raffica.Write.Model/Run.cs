namespace Raffica.Write.Model;

/// <summary>A <c>w:r</c>: run properties and the content in order.</summary>
public sealed class Run : Inline
{
    public PropertyBag Properties { get; } = new(PropertyKind.Run);

    public IList<RunContent> Content { get; } = new List<RunContent>();
}
