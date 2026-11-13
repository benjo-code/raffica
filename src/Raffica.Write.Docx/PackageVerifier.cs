using System.Xml;
using System.Xml.Linq;

namespace Raffica.Write.Docx;

/// <summary>The verdict of comparing a DOCX file with the file saved from it: parts the model did not load, and parts it did.</summary>
/// <param name="UnloadedIdentical">No part appeared or vanished and every part that differs is one the model loads (so every other part kept its decompressed bytes).</param>
/// <param name="LoadedEquivalent">Every changed model part is equivalent to the original (<see cref="XmlEquivalence"/>).</param>
internal sealed record PackageVerdict(bool UnloadedIdentical, bool LoadedEquivalent);

/// <summary>
/// The one rule that judges a round trip: parts the model does not load are identical, parts it loads are equivalent. The tests of
/// the save and the corpus tool (<c>write-corpus run</c>) both use it.
/// </summary>
internal static class PackageVerifier
{
    private static readonly HashSet<string> s_fixedModelParts = new(StringComparer.Ordinal)
    {
        "/word/document.xml", "/word/styles.xml", "/word/numbering.xml", "/word/settings.xml", "/word/footnotes.xml", "/word/endnotes.xml",
        "/word/comments.xml", "/word/commentsExtended.xml",
    };

    /// <summary>
    /// Whether a part (name with a leading slash) is one the model loads and rewrites on a save, judged by its conventional name:
    /// document, styles, numbering, settings, footnotes, endnotes, comments, comments extension, headers and footers.
    /// </summary>
    public static bool IsModelPart(string partName)
    {
        ArgumentNullException.ThrowIfNull(partName);
        if (s_fixedModelParts.Contains(partName))
        {
            return true;
        }

        const string folder = "/word/";
        if (!partName.StartsWith(folder, StringComparison.Ordinal) || !partName.EndsWith(".xml", StringComparison.Ordinal))
        {
            return false;
        }

        var name = partName.AsSpan(folder.Length, partName.Length - folder.Length - ".xml".Length);
        return (name.StartsWith("header", StringComparison.Ordinal) && AllDigits(name[6..]))
            || (name.StartsWith("footer", StringComparison.Ordinal) && AllDigits(name[6..]));
    }

    public static PackageVerdict Compare(string original, string saved)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(saved);
        var diff = PartComparer.Diff(original, saved);
        var unloaded = diff.Added.Count == 0 && diff.Removed.Count == 0 && diff.Changed.All(IsModelPart);
        var equivalent = diff.Changed.Where(IsModelPart).All(name => Equivalent(original, saved, name));
        return new PackageVerdict(unloaded, equivalent);
    }

    private static bool Equivalent(string original, string saved, string name)
    {
        try
        {
            var before = Parse(PartComparer.ReadPart(original, name));
            var after = Parse(PartComparer.ReadPart(saved, name));
            return XmlEquivalence.AreEqual(before, after);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static XElement Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XElement.Load(reader);
    }

    private static bool AllDigits(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
