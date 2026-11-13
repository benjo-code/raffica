using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace WriteCorpus;

/// <summary>Checks a corpus root against its manifest. A problem is one line; the paths in it are relative to the root.</summary>
internal static class CorpusCheck
{
    /// <summary>
    /// The problems of the corpus at <paramref name="root"/>, empty when it is consistent. With <paramref name="requireCoverage"/> a
    /// required feature that no file covers is a problem too (the default; the CI of the first milestone turns it off with
    /// <c>--allow-uncovered</c> until the corpus is filled).
    /// </summary>
    public static IReadOnlyList<string> Run(string root, bool requireCoverage = true)
    {
        ArgumentNullException.ThrowIfNull(root);
        var problems = new List<string>();
        if (!File.Exists(CorpusManifest.PathIn(root)))
        {
            problems.Add($"{CorpusManifest.FileName}: the manifest is missing");
            return problems;
        }

        CorpusManifest manifest;
        try
        {
            manifest = CorpusManifest.Load(root);
        }
        catch (JsonException ex)
        {
            // The exception type only: its message can name a path or a value of the file.
            problems.Add($"{CorpusManifest.FileName}: the manifest cannot be read ({ex.GetType().Name}); schema must be present and every value valid");
            return problems;
        }

        if (manifest.Schema != CorpusManifest.CurrentSchema)
        {
            problems.Add($"{CorpusManifest.FileName}: schema {manifest.Schema} is not supported (expected {CorpusManifest.CurrentSchema})");
        }

        var listed = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files)
        {
            if (!ids.Add(entry.Id))
            {
                problems.Add($"{Label(entry)}: the id is used twice");
            }

            CheckEntry(root, entry, listed, covered, problems);
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var extension = Path.GetExtension(relative);
            if ((extension is ".docx" or ".docm") && !listed.Contains(relative))
            {
                problems.Add($"{relative}: the file is not in the manifest");
            }
        }

        if (requireCoverage)
        {
            foreach (var feature in CorpusFeatures.Required.Where(f => !covered.Contains(f)))
            {
                problems.Add($"feature {feature}: no file of the corpus covers it");
            }
        }

        return problems;
    }

    private static string Label(CorpusEntry entry) => string.IsNullOrEmpty(entry.Id) ? "(entry without id)" : entry.Id;

    private static void CheckEntry(string root, CorpusEntry entry, HashSet<string> listed, HashSet<string> covered, List<string> problems)
    {
        var label = Label(entry);
        if (string.IsNullOrWhiteSpace(entry.Source))
        {
            problems.Add($"{label}: source is missing");
        }

        if (!DateOnly.TryParseExact(entry.Retrieved, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            problems.Add($"{label}: retrieved is missing or not YYYY-MM-DD");
        }

        if (!CorpusFeatures.AllowedLicenses.Contains(entry.License, StringComparer.Ordinal))
        {
            problems.Add($"{label}: the licence '{entry.License}' is not in the allowed list ({string.Join(", ", CorpusFeatures.AllowedLicenses)})");
        }

        if (string.IsNullOrWhiteSpace(entry.LicenseFile))
        {
            problems.Add($"{label}: licenseFile is missing");
        }
        else if (Resolve(root, entry.LicenseFile) is not { } licensePath || !File.Exists(licensePath))
        {
            problems.Add($"{label}: the licence file '{entry.LicenseFile}' is missing");
        }

        var path = Resolve(root, entry.Path);
        if (path is null)
        {
            problems.Add($"{label}: the path '{entry.Path}' is not inside the corpus root");
            return;
        }

        var relative = entry.Path.Replace('\\', '/');
        listed.Add(relative);
        if (!File.Exists(path))
        {
            problems.Add($"{label}: the file {relative} is missing");
            return;
        }

        var hash = HashOf(path);
        if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{label}: the hash of {relative} differs from the manifest");
        }

        IReadOnlySet<string> found;
        try
        {
            found = FeatureDetector.Detect(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException)
        {
            problems.Add($"{label}: {relative} is not a readable package ({ex.GetType().Name})");
            return;
        }

        foreach (var tag in entry.Features)
        {
            if (found.Contains(tag))
            {
                covered.Add(tag);
            }
            else
            {
                problems.Add($"{label}: the feature '{tag}' is not found in {relative}");
            }
        }
    }

    /// <summary>The lower-case SHA-256 of a file, read as a stream.</summary>
    internal static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>The full path of a root-relative path, or null when it is empty, rooted, malformed (for example a NUL character) or leaves the root.</summary>
    internal static string? Resolve(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return null;
        }

        try
        {
            if (Path.IsPathRooted(relative))
            {
                return null;
            }

            var rootFull = Path.GetFullPath(root);
            var full = Path.GetFullPath(Path.Combine(rootFull, relative));
            var inside = Path.GetRelativePath(rootFull, full);
            var leaves = inside == ".." || inside.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || inside.StartsWith("../", StringComparison.Ordinal);
            return leaves || Path.IsPathRooted(inside) ? null : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
