using System.Diagnostics;
using System.Globalization;
using Raffica.Core.Saving;
using Raffica.Write.Docx;

namespace WriteCorpus;

/// <summary>
/// The round trip of every corpus file: open, save to a temporary file, and judge with the rule of the DOCX tests
/// (<c>PackageVerifier</c>: parts the model did not load are identical, parts it loaded are equivalent). One CSV row per entry.
/// </summary>
internal static class CorpusRun
{
    public const string Header = "id,opens,saves,unloadedIdentical,loadedEquivalent,ms";

    /// <summary>Prints the CSV to <paramref name="output"/>; returns 0 when every entry passed, 1 otherwise.</summary>
    public static int Run(string root, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(output);
        var manifest = CorpusManifest.Load(root);
        output.WriteLine(Header);
        var failed = false;
        var temp = Path.Combine(Path.GetTempPath(), "write-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (var entry in manifest.Files)
            {
                var row = RoundTrip(root, entry, temp);
                failed |= !(row.Opens && row.Saves && row.UnloadedIdentical && row.LoadedEquivalent);
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Csv(entry.Id)},{Flag(row.Opens)},{Flag(row.Saves)},{Flag(row.UnloadedIdentical)},{Flag(row.LoadedEquivalent)},{row.Milliseconds}"));
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }

        return failed ? 1 : 0;
    }

    /// <summary>The verdict of one entry's round trip.</summary>
    internal sealed record Row(bool Opens, bool Saves, bool UnloadedIdentical, bool LoadedEquivalent, long Milliseconds);

    internal static Row RoundTrip(string root, CorpusEntry entry, string temp)
    {
        var path = CorpusCheck.Resolve(root, entry.Path);
        var watch = Stopwatch.StartNew();
        if (path is null || !File.Exists(path))
        {
            return new Row(false, false, false, false, watch.ElapsedMilliseconds);
        }

        DocxFile file;
        try
        {
            file = DocxFile.Open(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new Row(false, false, false, false, watch.ElapsedMilliseconds);
        }

        using (file)
        {
            var saved = Path.Combine(temp, "saved.docx");
            try
            {
                file.SaveAsync(saved).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return new Row(true, false, false, false, watch.ElapsedMilliseconds);
            }

            try
            {
                var verdict = PackageVerifier.Compare(path, saved);
                return new Row(true, true, verdict.UnloadedIdentical, verdict.LoadedEquivalent, watch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return new Row(true, true, false, false, watch.ElapsedMilliseconds);
            }
            finally
            {
                File.Delete(saved);
            }
        }
    }

    /// <summary>Round-trips every entry of the manifest (in manifest order) and returns the verdicts.</summary>
    internal static IReadOnlyList<(CorpusEntry Entry, Row Row)> Evaluate(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var manifest = CorpusManifest.Load(root);
        var temp = Path.Combine(Path.GetTempPath(), "write-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            return [.. manifest.Files.Select(entry => (entry, RoundTrip(root, entry, temp)))];
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static string Csv(string text) =>
        text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
}
