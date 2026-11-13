namespace Raffica.Write.Model;

/// <summary>The numbering part (<c>w:numbering</c>): the abstract definitions and the instances that point at them. <c>w:numPicBullet</c> and <c>w:numIdMacAtCleanup</c> are kept children.</summary>
public sealed class NumberingDefinitions : OrderedElement
{
    public NumberingDefinitions(OoxmlRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
    }

    public OoxmlRoot Root { get; }

    public IList<AbstractNumbering> Abstracts { get; } = new List<AbstractNumbering>();

    public IList<NumberingInstance> Instances { get; } = new List<NumberingInstance>();

    public AbstractNumbering? FindAbstract(int abstractNumId) => Abstracts.FirstOrDefault(a => a.AbstractNumId == abstractNumId);

    public NumberingInstance? FindInstance(int numId) => Instances.FirstOrDefault(i => i.NumId == numId);
}
