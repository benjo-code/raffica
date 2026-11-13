using System.IO.Compression;

namespace OpenXmlAot;

/// <summary>Part-by-part comparison of two packages on the decompressed bytes of each part.</summary>
public static class Compare
{
    static SortedDictionary<string, byte[]> Read(string path)
    {
        var parts = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(path);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith('/')) continue;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            parts["/" + e.FullName] = ms.ToArray();
        }
        return parts;
    }

    public static (List<string> Added, List<string> Removed, List<string> Changed) Diff(string before, string after)
    {
        var a = Read(before);
        var b = Read(after);
        var added = b.Keys.Where(k => !a.ContainsKey(k)).ToList();
        var removed = a.Keys.Where(k => !b.ContainsKey(k)).ToList();
        var changed = a.Keys.Where(k => b.TryGetValue(k, out var other) && !a[k].AsSpan().SequenceEqual(other)).ToList();
        return (added, removed, changed);
    }

    /// <summary>Prints the three lists; exit 1 when a part that is not named in <paramref name="touched"/> changed, was added or removed.</summary>
    public static int Run(string before, string after, IReadOnlyCollection<string> touched)
    {
        var (added, removed, changed) = Diff(before, after);
        void Print(string name, List<string> l)
        {
            Console.WriteLine($"{name}: {(l.Count == 0 ? "(none)" : string.Join(", ", l))}");
        }
        Print("added", added);
        Print("removed", removed);
        Print("changed", changed);
        var allowed = new HashSet<string>(touched.Select(t => t.StartsWith('/') ? t : "/" + t), StringComparer.Ordinal);
        var unexpected = added.Concat(removed).Concat(changed).Where(p => !allowed.Contains(p)).ToList();
        if (unexpected.Count == 0) return 0;
        Console.Error.WriteLine("untouched parts that differ: " + string.Join(", ", unexpected));
        return 1;
    }
}
