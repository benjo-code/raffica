using System.Xml.Linq;

namespace Raffica.Write.Docx.Xml;

/// <summary>The WordprocessingML names the reader and writer share.</summary>
internal static class Names
{
    /// <summary>The main WordprocessingML namespace as text, for comparing against <c>XmlReader.NamespaceURI</c> without allocating.</summary>
    public const string WordUri = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static readonly XNamespace W = WordUri;

    /// <summary>Relationships (<c>r:id</c>, <c>r:embed</c>).</summary>
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static readonly XName Document = W + "document";
    public static readonly XName Body = W + "body";

    // Attributes.
    public static readonly XName Id = W + "id";
    public static readonly XName Name = W + "name";
    public static readonly XName Author = W + "author";
    public static readonly XName Date = W + "date";
    public static readonly XName Anchor = W + "anchor";
    public static readonly XName Instr = W + "instr";
    public static readonly XName Type = W + "type";
    public static readonly XName Clear = W + "clear";
    public static readonly XName FldCharType = W + "fldCharType";
    public static readonly XName Dirty = W + "dirty";
    public static readonly XName RelationshipId = R + "id";
    public static readonly XName XmlSpace = XNamespace.Xml + "space";
}

/// <summary>The assemblies this library is allowed to build on; naming them keeps the references real, which ProjectGuardTests checks.</summary>
internal static class Dependencies
{
    public static readonly Type[] Anchors = [typeof(Raffica.Write.Model.OoxmlElement), typeof(Raffica.Core.Saving.PathRules)];
}
