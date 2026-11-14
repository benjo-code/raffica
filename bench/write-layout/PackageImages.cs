using System.IO.Compression;
using System.Xml.Linq;

namespace WriteLayoutBench;

/// <summary>
/// Gives the bytes of a picture for a story-qualified image id (<c>document#rId4</c>, <c>header:rId9#rId1</c>, <c>footnotes#rId2</c>)
/// by reading the package's relationships; the bytes are read when asked, never before. A missing part throws, as the layout allows.
/// </summary>
internal sealed class PackageImages(byte[] package)
{
    private static readonly XNamespace s_rels = "http://schemas.openxmlformats.org/package/2006/relationships";

    private ZipArchive? _zip;

    public ReadOnlyMemory<byte> Read(string id)
    {
        var hash = id.IndexOf('#', StringComparison.Ordinal);
        var story = hash < 0 ? "document" : id[..hash];
        var rel = hash < 0 ? id : id[(hash + 1)..];
        _zip ??= new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);

        var owner = "word/document.xml";
        if (story.StartsWith("header:", StringComparison.Ordinal))
        {
            owner = Resolve("word/document.xml", story["header:".Length..]);
        }
        else if (story is "footnotes" or "endnotes")
        {
            owner = "word/" + story + ".xml";
        }

        return Entry(Resolve(owner, rel));
    }

    private string Resolve(string owner, string rel)
    {
        var slash = owner.LastIndexOf('/');
        var relsName = owner[..(slash + 1)] + "_rels/" + owner[(slash + 1)..] + ".rels";
        var entry = _zip!.GetEntry(relsName) ?? throw new FileNotFoundException("relationships part");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var target = document.Root!.Elements(s_rels + "Relationship").FirstOrDefault(e => (string?)e.Attribute("Id") == rel)?.Attribute("Target")?.Value
            ?? throw new FileNotFoundException("relationship");
        var combined = target.StartsWith('/') ? target[1..] : owner[..(slash + 1)] + target;
        var parts = new List<string>();
        foreach (var piece in combined.Split('/'))
        {
            if (piece == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (piece.Length > 0 && piece != ".")
            {
                parts.Add(piece);
            }
        }

        return string.Join('/', parts);
    }

    private byte[] Entry(string name)
    {
        var entry = _zip!.GetEntry(name) ?? throw new FileNotFoundException("part");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
