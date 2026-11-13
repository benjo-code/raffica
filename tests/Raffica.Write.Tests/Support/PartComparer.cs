using System.IO.Compression;

namespace Raffica.Write.Tests.Support;

/// <summary>The parts that differ between two DOCX files, as part names with a leading slash (for example <c>/word/document.xml</c>).</summary>
internal sealed record PartDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
}

/// <summary>Reads and compares the parts of DOCX files (the decompressed bytes of a part, not the zip).</summary>
internal static class PartComparer
{
    /// <summary>The decompressed bytes of one part of a DOCX file; throws when the part is missing.</summary>
    public static byte[] ReadPart(string docxPath, string partName)
    {
        ArgumentNullException.ThrowIfNull(docxPath);
        ArgumentNullException.ThrowIfNull(partName);
        using var archive = ZipFile.OpenRead(docxPath);
        var entry = archive.GetEntry(partName.TrimStart('/')) ?? throw new FileNotFoundException($"The part '{partName}' is not in '{docxPath}'.");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Every part (zip entry that is not a folder) with its decompressed bytes, by name with a leading slash.</summary>
    public static SortedDictionary<string, byte[]> ReadAll(string docxPath)
    {
        ArgumentNullException.ThrowIfNull(docxPath);
        var parts = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(docxPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            parts["/" + entry.FullName] = memory.ToArray();
        }

        return parts;
    }

    /// <summary>The parts added to, removed from and changed in <paramref name="saved"/> compared with <paramref name="original"/>, by their decompressed bytes.</summary>
    public static PartDiff Diff(string original, string saved)
    {
        var before = ReadAll(original);
        var after = ReadAll(saved);
        var added = after.Keys.Where(name => !before.ContainsKey(name)).ToList();
        var removed = before.Keys.Where(name => !after.ContainsKey(name)).ToList();
        var changed = before.Keys.Where(name => after.TryGetValue(name, out var other) && !before[name].AsSpan().SequenceEqual(other)).ToList();
        return new PartDiff(added, removed, changed);
    }
}
