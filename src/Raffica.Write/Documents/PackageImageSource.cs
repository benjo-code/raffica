using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Xml;

namespace Raffica.Write.Documents;

/// <summary>
/// Gives the bytes of a picture for a story-qualified image id of the layout (<c>document#rId4</c>, <c>header:rId9#rId1</c>,
/// <c>footnotes#rId2</c>, <c>endnotes#rId1</c>; <see cref="Raffica.Write.Layout.LayoutOptions.ImageBytes"/>) by reading the relationships of the package.
/// </summary>
/// <remarks>
/// The package is the in-memory snapshot of the opened file (<see cref="Raffica.Write.Docx.DocxFile.Package"/>), so the source holds no file handle and a change
/// of the file on disk cannot change a picture under a document that is open. A picture is read when it is asked for, never before; the archive and the
/// relationship tables are made on the first ask. One lock covers all of it, so the layout thread and the drawing thread may both ask. A missing part, an
/// external or malformed relationship and a part over <see cref="MaxPartLength"/> throw, which the layout and the image cache answer with a marked box. After
/// <see cref="Dispose"/> every ask throws <see cref="ObjectDisposedException"/>. Nothing the file refers to by an external relationship is fetched.
/// </remarks>
internal sealed class PackageImageSource : IDisposable
{
    /// <summary>The largest part that is read as a picture (the hardening of hostile files is a later milestone).</summary>
    internal const long MaxPartLength = 64L * 1024 * 1024;

    private const string FallbackMain = "word/document.xml";

    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, Relationship>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private ZipArchive? _zip;
    private Dictionary<string, ZipArchiveEntry>? _entries;
    private string? _main;
    private bool _disposed;
    private ReadOnlyMemory<byte> _package;

    /// <param name="package">The snapshot of the file the pictures are read from.</param>
    public PackageImageSource(ReadOnlyMemory<byte> package) => _package = package;

    private Func<string, ReadOnlyMemory<byte>?>? _pending;

    /// <summary>
    /// Gives the bytes of a new picture by its relationship id in the main story (the document's <c>MediaStore.TryGet</c>), or null; asked before the package. It runs under this
    /// source's lock on a pool thread and must be thread-safe and fast (the store's read takes no lock).
    /// </summary>
    public Func<string, ReadOnlyMemory<byte>?>? PendingMedia
    {
        get => Volatile.Read(ref _pending);
        set => Volatile.Write(ref _pending, value);
    }

    /// <summary>
    /// Reads from <paramref name="package"/> from now on (the whole file that replaces the prefix the first page was made from, so the prefix bytes can go): the archive and
    /// the relationship tables are made again on the next ask. The ids stay valid because both snapshots are the same file.
    /// </summary>
    public void Retarget(ReadOnlyMemory<byte> package)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _zip?.Dispose();
            _zip = null;
            _entries = null;
            _main = null;
            _tables.Clear();
            _package = package;
        }
    }

    private sealed record Relationship(string Type, string Target, bool External);

    /// <summary>The bytes of the picture an id names.</summary>
    /// <exception cref="FileNotFoundException">The relationship or the part is not there, or the relationship is external.</exception>
    /// <exception cref="InvalidDataException">The package or a relationships part is damaged, or the part is too large.</exception>
    /// <exception cref="ObjectDisposedException">The source was disposed (the tab closed).</exception>
    public ReadOnlyMemory<byte> Read(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var hash = id.IndexOf('#', StringComparison.Ordinal);
            var story = hash < 0 ? "document" : id[..hash];
            var relationship = hash < 0 ? id : id[(hash + 1)..];

            // A picture the document added and no save has written (or one written since: the package is the file as it was opened, so it still is not in it) comes first; only the main story has them.
            if (story == "document" && Volatile.Read(ref _pending)?.Invoke(relationship) is { } added)
            {
                return added;
            }

            var main = EnsureOpen();

            var owner = main;
            if (story.StartsWith("header:", StringComparison.Ordinal))
            {
                // The id of a header story is the id the document's relationships use for the header or footer part; the picture is a relationship of that part.
                owner = Follow(main, story["header:".Length..]);
            }
            else if (story is "footnotes" or "endnotes")
            {
                owner = FindByType(main, "/" + story) ?? main[..(main.LastIndexOf('/') + 1)] + story + ".xml";
            }

            return Part(Follow(owner, relationship));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _zip?.Dispose();
            _zip = null;
            _entries = null;
            _tables.Clear();
        }
    }

    private string EnsureOpen()
    {
        if (_main is not null)
        {
            return _main;
        }

        var stream = MemoryMarshal.TryGetArray(_package, out var segment) && segment.Array is { } array
            ? new MemoryStream(array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(_package.ToArray(), writable: false);
        _zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _zip.Entries)
        {
            entries.TryAdd(entry.FullName, entry);
        }

        _entries = entries;

        // The main part is the target of the package's officeDocument relationship; word/document.xml when the package does not say.
        _main = FindByType(string.Empty, "/officeDocument") ?? FallbackMain;
        return _main;
    }

    // The part name a relationship of a part points to; the owner string.Empty is the package root (_rels/.rels).
    private string Follow(string owner, string id)
    {
        var table = TableOf(owner);
        if (table is null || !table.TryGetValue(id, out var found) || found.External)
        {
            throw new FileNotFoundException("relationship");
        }

        return Resolve(FolderOf(owner), found.Target);
    }

    private string? FindByType(string owner, string typeSuffix)
    {
        var found = TableOf(owner)?.Values.FirstOrDefault(r => !r.External && r.Type.EndsWith(typeSuffix, StringComparison.Ordinal));
        return found is null ? null : Resolve(FolderOf(owner), found.Target);
    }

    private static string FolderOf(string owner) => owner[..(owner.LastIndexOf('/') + 1)];

    private Dictionary<string, Relationship>? TableOf(string owner)
    {
        var slash = owner.LastIndexOf('/');
        var relsName = owner[..(slash + 1)] + "_rels/" + owner[(slash + 1)..] + ".rels";
        if (_tables.TryGetValue(relsName, out var cached))
        {
            return cached;
        }

        if (!_entries!.TryGetValue(relsName, out var entry))
        {
            return null;
        }

        if (entry.Length > MaxPartLength)
        {
            throw new InvalidDataException("relationships part too large");
        }

        var table = new Dictionary<string, Relationship>(StringComparer.Ordinal);
        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship" && reader.GetAttribute("Id") is { } id && reader.GetAttribute("Target") is { } target)
                {
                    table[id] = new Relationship(
                        reader.GetAttribute("Type") ?? string.Empty,
                        target,
                        string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("relationships part is not well-formed", ex);
        }

        _tables[relsName] = table;
        return table;
    }

    // A target is relative to the folder of the part that owns the relationship, or absolute from the package root; "." and ".." are folded and percent-encoding is undone.
    private static string Resolve(string folder, string target)
    {
        var combined = target.StartsWith('/') ? target[1..] : folder + target;
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

        return Uri.UnescapeDataString(string.Join('/', parts));
    }

    private byte[] Part(string name)
    {
        if (!_entries!.TryGetValue(name, out var entry))
        {
            throw new FileNotFoundException("part");
        }

        if (entry.Length > MaxPartLength)
        {
            throw new InvalidDataException("part too large");
        }

        using var stream = entry.Open();
        using var copy = new MemoryStream((int)entry.Length);
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
