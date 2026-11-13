using System.IO.Compression;

namespace Raffica.Write.Tests.Support;

/// <summary>Reads and compares the parts of DOCX files (the decompressed bytes of a part, not the zip).</summary>
internal static class PartComparer
{
    /// <summary>The decompressed bytes of one part of a DOCX file; throws when the part is missing.</summary>
    public static byte[] ReadPart(string docxPath, string partName)
    {
        ArgumentNullException.ThrowIfNull(docxPath);
        ArgumentNullException.ThrowIfNull(partName);
        using var archive = ZipFile.OpenRead(docxPath);
        var entry = archive.GetEntry(partName) ?? throw new FileNotFoundException($"The part '{partName}' is not in '{docxPath}'.");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
