using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Raffica.Write.Model;

namespace Raffica.Write.Docx.Xml;

/// <summary>What the package layer needs to know to write the new pictures of a <see cref="MediaStore"/> as media parts.</summary>
internal static class MediaPartWriter
{
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// The entries of <paramref name="entries"/> the serialised main part refers to: an attribute in the relationships namespace (<c>r:embed</c>, <c>r:link</c>, <c>r:id</c>, ...) whose
    /// value is the relationship id of the entry. An entry nothing refers to (an undone picture) is left out and gets no part. The part is read once, as XML, only when there are entries.
    /// </summary>
    public static List<MediaEntry> Used(byte[] mainPart, IReadOnlyList<MediaEntry> entries)
    {
        var used = new List<MediaEntry>();
        if (entries.Count == 0)
        {
            return used;
        }

        var wanted = entries.Select(e => e.Ref.RelationshipId).ToHashSet(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        using var stream = new MemoryStream(mainPart, writable: false);
        using var reader = XmlReader.Create(stream, XmlKit.ReaderSettings());
        while (found.Count < wanted.Count && reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || !reader.HasAttributes)
            {
                continue;
            }

            while (reader.MoveToNextAttribute())
            {
                if (reader.NamespaceURI == RelationshipsNamespace && wanted.Contains(reader.Value))
                {
                    found.Add(reader.Value);
                }
            }

            reader.MoveToElement();
        }

        foreach (var entry in entries)
        {
            if (found.Contains(entry.Ref.RelationshipId))
            {
                used.Add(entry);
            }
        }

        return used;
    }

    /// <summary>The relationship ids of the main part, read from the package bytes (not through the package layer); empty when the part has none or cannot be read.</summary>
    public static List<string> RelationshipIds(byte[] package, string mainPartName)
    {
        var ids = new List<string>();
        try
        {
            var slash = mainPartName.LastIndexOf('/');
            var name = (mainPartName[..(slash + 1)] + "_rels/" + mainPartName[(slash + 1)..] + ".rels").TrimStart('/');
            using var memory = new MemoryStream(package, writable: false);
            using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
            if (zip.GetEntry(name) is not { } entry)
            {
                return ids;
            }

            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, XmlKit.ReaderSettings());
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship" && reader.GetAttribute("Id") is { } id)
                {
                    ids.Add(id);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException)
        {
            // An unreadable relationships part: the save refuses a colliding id itself, so the ids that were read are the best the store can be seeded with.
        }

        return ids;
    }

    /// <summary>A new picture to add: its relationship id in the main part, its content type and its bytes.</summary>
    public sealed record NewPicture(string RelationshipId, string ContentType, byte[] Bytes);

    private static readonly XNamespace s_packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace s_contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string ImageRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    /// <summary>
    /// Adds the pictures to the package at <paramref name="packagePath"/> as <c>/word/media/imageN.ext</c> (the first N that no entry of the package uses, whatever its extension, so
    /// a part the file has is never replaced), each with an image relationship of the main part under its own id and a content type (a <c>Default</c> for the extension unless the
    /// package already gives that extension another type, then an <c>Override</c> of the part). Every other entry of the package stays as it is, except the content types and the
    /// relationships of the main part, which only gain those entries. A relationship id the main part already uses is refused (<see cref="DocxSaveReason.PartsChanged"/>).
    /// The package layer is not used because it puts a new picture part outside <c>/word</c>.
    /// </summary>
    /// <returns>The part names (with the leading slash), the bytes, the relationship id and the content type that were written. A picture is stored without compression (the formats are compressed already).</returns>
    public static List<(string Name, byte[] Bytes, string RelationshipId, string ContentType)> AddTo(string packagePath, string mainPartName, IReadOnlyList<NewPicture> pictures, Func<string, Stream, Stream>? wrap)
    {
        var added = new List<(string Name, byte[] Bytes, string RelationshipId, string ContentType)>();
        if (pictures.Count == 0)
        {
            return added;
        }

        using var file = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(file, ZipArchiveMode.Update);

        var relsName = RelationshipsPartOf(mainPartName).TrimStart('/');
        var relationships = zip.GetEntry(relsName) is { } relsEntry ? LoadXml(relsEntry) : new XDocument(new XElement(s_packageRelationships + "Relationships"));
        var types = LoadXml(zip.GetEntry("[Content_Types].xml") ?? throw new DocxSaveException(DocxSaveReason.PartsChanged, "/[Content_Types].xml"));
        var usedIds = relationships.Root!.Elements().Select(e => (string?)e.Attribute("Id")).ToHashSet(StringComparer.Ordinal);
        var stems = zip.Entries
            .Where(e => e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase))
            .Select(e => Path.GetFileNameWithoutExtension(e.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mainFolder = mainPartName[..(mainPartName.LastIndexOf('/') + 1)].TrimStart('/');
        var number = 0;
        foreach (var picture in pictures)
        {
            var extension = MediaStore.ExtensionOf(picture.ContentType) ?? throw new DocxSaveException(DocxSaveReason.PartsChanged, "/word/media");
            if (!usedIds.Add(picture.RelationshipId))
            {
                throw new DocxSaveException(DocxSaveReason.PartsChanged, "/word/media");
            }

            do
            {
                number++;
            }
            while (!stems.Add("image" + number.ToString(CultureInfo.InvariantCulture)));

            var name = "word/media/image" + number.ToString(CultureInfo.InvariantCulture) + "." + extension;
            using (var raw = zip.CreateEntry(name, CompressionLevel.NoCompression).Open())
            using (var target = wrap?.Invoke("/" + name, raw) ?? raw)
            {
                target.Write(picture.Bytes);
            }

            var relativeTarget = name.StartsWith(mainFolder, StringComparison.OrdinalIgnoreCase) ? name[mainFolder.Length..] : "/" + name;
            relationships.Root.Add(new XElement(
                s_packageRelationships + "Relationship",
                new XAttribute("Id", picture.RelationshipId),
                new XAttribute("Type", ImageRelationshipType),
                new XAttribute("Target", relativeTarget)));
            var typed = types.Root!.Elements(s_contentTypes + "Default").FirstOrDefault(e => string.Equals((string?)e.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase));
            if (typed is null)
            {
                types.Root.AddFirst(new XElement(s_contentTypes + "Default", new XAttribute("Extension", extension), new XAttribute("ContentType", picture.ContentType)));
            }
            else if (!string.Equals((string?)typed.Attribute("ContentType"), picture.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                types.Root.Add(new XElement(s_contentTypes + "Override", new XAttribute("PartName", "/" + name), new XAttribute("ContentType", picture.ContentType)));
            }

            added.Add(("/" + name, picture.Bytes, picture.RelationshipId, picture.ContentType));
        }

        Replace(zip, relsName, relationships);
        Replace(zip, "[Content_Types].xml", types);
        return added;
    }

    private static string RelationshipsPartOf(string partName)
    {
        var slash = partName.LastIndexOf('/');
        return partName[..(slash + 1)] + "_rels/" + partName[(slash + 1)..] + ".rels";
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, XmlKit.ReaderSettings());
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new DocxSaveException(DocxSaveReason.PartsChanged, "/" + entry.FullName);
        }
    }

    private static void Replace(ZipArchive zip, string name, XDocument document)
    {
        zip.GetEntry(name)?.Delete();
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false });
        document.Save(writer);
    }
}
