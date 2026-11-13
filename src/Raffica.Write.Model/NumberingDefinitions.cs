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

    /// <summary>The abstract definitions are written at the position they were read in (reordering read items is not reflected: they are sorted by <see cref="OrderedElement.Position"/>); items added in code go in schema order after those read.</summary>
    public IList<AbstractNumbering> Abstracts { get; } = new List<AbstractNumbering>();

    /// <summary>The instances are written at the position they were read in (reordering read items is not reflected: they are sorted by <see cref="OrderedElement.Position"/>); items added in code go in schema order after those read.</summary>
    public IList<NumberingInstance> Instances { get; } = new List<NumberingInstance>();

    public AbstractNumbering? FindAbstract(int abstractNumId) => Abstracts.FirstOrDefault(a => a.AbstractNumId == abstractNumId);

    public NumberingInstance? FindInstance(int numId) => Instances.FirstOrDefault(i => i.NumId == numId);
}
