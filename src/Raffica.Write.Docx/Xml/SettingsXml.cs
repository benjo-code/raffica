using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>
/// The settings part (<c>word/settings.xml</c>): a stream in, <see cref="DocumentSettings"/> out, and back. Every child stays
/// in the property bag in file order; a flag set in code goes where the schema order of the settings puts it.
/// </summary>
internal static class SettingsXml
{
    public static DocumentSettings Read(Stream stream) =>
        PartXml.Read(stream, root => new DocumentSettings(root), (settings, element, _) => settings.Properties.AddLoaded(element));

    /// <summary>Writes UTF-8 without a byte order mark and an XML declaration with <c>standalone="yes"</c>. The stream stays open.</summary>
    public static void Write(DocumentSettings settings, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(settings);
        PartXml.Write(stream, settings.Root, writer =>
        {
            foreach (var child in settings.Properties.Children)
            {
                child.WriteTo(writer);
            }
        });
    }
}
