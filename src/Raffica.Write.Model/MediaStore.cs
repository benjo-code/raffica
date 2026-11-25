using System.Globalization;

namespace Raffica.Write.Model;

/// <summary>The bytes of one picture the document holds that the package does not have yet, with the reference that names it.</summary>
public readonly record struct MediaEntry(MediaRef Ref, ReadOnlyMemory<byte> Bytes);

/// <summary>
/// The pictures of a document that are new: bytes in memory under a relationship id of the main document part, until a save writes them as media parts. The store never reads
/// a picture back out of the package and never decides whether one is used: a save writes an entry only when the document still refers to its id (an undone picture leaves no part),
/// so a step that undoes an insert does not <see cref="Remove"/> the entry and a redo still finds its bytes. An entry leaves the store only when the history <em>drops</em> the step that added it (the redo stack cut by a new edit, the history cleared, the oldest steps trimmed) and neither the current document nor any remaining step refers to its id: <c>EditHistory</c> decides, never an undo or a redo.
/// </summary>
/// <remarks>
/// <para>
/// The bytes of every entry stay in memory for the life of the document, saved or not: a save is always made from the opened package plus the model, so the next save writes the
/// part again, so the store caps the total (<see cref="MaximumTotalBytes"/>) and the editor releases an entry only when no document state and no history step can reach it (an insert that was refused after the add; never an undo or a redo). A single entry is at most <see cref="MaxEntryBytes"/> (the last line of defence; the insert command checks the size and the pixel count earlier). Each content type
/// of a picture has one file extension (<see cref="ExtensionOf"/>).
/// </para>
/// <para>
/// Reads (<see cref="TryGet"/>, <see cref="Entries"/>) take no lock and see an immutable list that <see cref="Add"/> and <see cref="Remove"/> replace under a lock, so a pool thread
/// that reads a picture for the layout may run beside an Add of the UI thread. Relationship ids are <c>rId</c> and a number above every number the main part already uses and above every
/// number this store has handed out, so an id is never reused, not even after a Remove.
/// </para>
/// <para>No exception or message here carries picture bytes or names.</para>
/// </remarks>
public sealed class MediaStore
{
    /// <summary>The largest picture the store accepts (50 MiB).</summary>
    public const int MaxEntryBytes = 50 * 1024 * 1024;

    /// <summary>The most bytes all entries may hold together (256 MiB); <see cref="Add"/> refuses beyond it. Entries are never released by a save or an undo.</summary>
    public const long MaximumTotalBytes = 256L * 1024 * 1024;

    private const string IdPrefix = "rId";

    private readonly object _gate = new();
    private MediaEntry[] _entries = [];
    private long _highest;
    private long _totalBytes;
    private readonly long _maximumTotal;

    /// <summary>A store whose ids start above the numbers of <paramref name="existingIds"/> (the relationship ids of the main part; ids that do not look like <c>rId</c> and a number are ignored, they can never equal one the store makes).</summary>
    public MediaStore(IEnumerable<string>? existingIds = null)
        : this(existingIds, MaximumTotalBytes)
    {
    }

    // The total cap is a parameter only so a test needs no 256 MiB.
    internal MediaStore(IEnumerable<string>? existingIds, long maximumTotalBytes)
    {
        _maximumTotal = maximumTotalBytes;
        if (existingIds is null)
        {
            return;
        }

        foreach (var id in existingIds)
        {
            if (id.StartsWith(IdPrefix, StringComparison.Ordinal) && long.TryParse(id.AsSpan(IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > _highest)
            {
                _highest = number;
            }
        }
    }

    /// <summary>The file extension a picture of this content type is stored with, or null when the type is not one of PNG, JPEG, GIF, BMP and TIFF.</summary>
    public static string? ExtensionOf(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpeg",
        "image/gif" => "gif",
        "image/bmp" => "bmp",
        "image/tiff" => "tiff",
        _ => null,
    };

    /// <summary>The bytes of all entries now.</summary>
    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    /// <summary>The entries now, oldest first; a snapshot that later adds and removals do not change.</summary>
    public IReadOnlyList<MediaEntry> Entries => Volatile.Read(ref _entries);

    /// <summary>Adds a picture and returns the reference that names it (a new relationship id each time).</summary>
    /// <exception cref="ArgumentException">The content type is not a supported picture type.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The picture is empty or larger than <see cref="MaxEntryBytes"/>.</exception>
    /// <exception cref="InvalidOperationException">The entries would together hold more than <see cref="MaximumTotalBytes"/>; the store is unchanged.</exception>
    public MediaRef Add(ReadOnlyMemory<byte> bytes, string contentType)
    {
        var extension = ExtensionOf(contentType) ?? throw new ArgumentException("The content type is not a supported picture type.", nameof(contentType));
        if (bytes.Length == 0 || bytes.Length > MaxEntryBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "A picture must hold at least one byte and at most 50 MiB.");
        }

        lock (_gate)
        {
            if (_totalBytes + bytes.Length > _maximumTotal)
            {
                throw new InvalidOperationException("The pictures of the document together would hold more than 256 MiB.");
            }

            var copy = bytes.ToArray();
            var reference = new MediaRef(IdPrefix + (++_highest).ToString(CultureInfo.InvariantCulture), contentType.ToLowerInvariant(), extension);
            var next = new MediaEntry[_entries.Length + 1];
            Array.Copy(_entries, next, _entries.Length);
            next[^1] = new MediaEntry(reference, copy);
            Volatile.Write(ref _entries, next);
            Interlocked.Add(ref _totalBytes, copy.Length);
            return reference;
        }
    }

    /// <summary>The bytes of a picture that is new (not in the package yet), or null for an id the store does not hold.</summary>
    public ReadOnlyMemory<byte>? TryGet(string id)
    {
        foreach (var entry in Volatile.Read(ref _entries))
        {
            if (string.Equals(entry.Ref.RelationshipId, id, StringComparison.Ordinal))
            {
                return entry.Bytes;
            }
        }

        return null;
    }

    /// <summary>Drops an entry; false when the store does not hold it. A document that still refers to the id would lose the picture at the next save, so only a caller that knows nothing refers to it (an insert refused after the add) may call this: never an undo, a redo or a history step, whose pictures must stay reachable. Frees room under <see cref="MaximumTotalBytes"/>.</summary>
    public bool Remove(MediaRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        lock (_gate)
        {
            var index = Array.FindIndex(_entries, e => e.Ref == reference);
            if (index < 0)
            {
                return false;
            }

            Interlocked.Add(ref _totalBytes, -_entries[index].Bytes.Length);

            var next = new MediaEntry[_entries.Length - 1];
            Array.Copy(_entries, next, index);
            Array.Copy(_entries, index + 1, next, index, next.Length - index);
            Volatile.Write(ref _entries, next);
            return true;
        }
    }
}
