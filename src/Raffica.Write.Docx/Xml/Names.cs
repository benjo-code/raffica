using System.Xml.Linq;

namespace Raffica.Write.Docx.Xml;

/// <summary>The WordprocessingML names the reader and writer share.</summary>
internal static class Names
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
}

/// <summary>The assemblies this library is allowed to build on; naming them keeps the references real, which ProjectGuardTests checks.</summary>
internal static class Dependencies
{
    public static readonly Type[] Anchors = [typeof(Raffica.Write.Model.OoxmlElement), typeof(Raffica.Core.Saving.PathRules)];
}
