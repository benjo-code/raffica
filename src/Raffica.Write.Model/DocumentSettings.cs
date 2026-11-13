using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>The settings part (<c>w:settings</c>). Every child lives in <see cref="Properties"/> in file order; the four flags are views over it, the rest (<c>w:compat</c>, <c>w:rsids</c>, ...) is kept.</summary>
public sealed class DocumentSettings
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public DocumentSettings(OoxmlRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
    }

    public OoxmlRoot Root { get; }

    public PropertyBag Properties { get; } = new(PropertyKind.Settings);

    public bool TrackRevisions
    {
        get => Properties.GetFlag(W + "trackRevisions");
        set => Properties.SetFlag(W + "trackRevisions", value);
    }

    public bool AutoHyphenation
    {
        get => Properties.GetFlag(W + "autoHyphenation");
        set => Properties.SetFlag(W + "autoHyphenation", value);
    }

    public bool EvenAndOddHeaders
    {
        get => Properties.GetFlag(W + "evenAndOddHeaders");
        set => Properties.SetFlag(W + "evenAndOddHeaders", value);
    }

    public bool UpdateFields
    {
        get => Properties.GetFlag(W + "updateFields");
        set => Properties.SetFlag(W + "updateFields", value);
    }
}
