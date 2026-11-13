using System.Xml.Linq;

namespace Raffica.Write.Model;

/// <summary>Base of every model element read from OOXML: keeps the attributes the model does not understand so a save writes them back.</summary>
public abstract class OoxmlElement
{
    public IList<XAttribute> UnknownAttributes { get; } = new List<XAttribute>();
}
