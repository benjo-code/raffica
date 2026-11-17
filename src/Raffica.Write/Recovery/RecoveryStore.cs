using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raffica.Core.Saving;
using Raffica.Hosting;

namespace Raffica.Write.Recovery;

/// <summary>What <see cref="RecoveryStore.List"/> found: the complete copies and the ids that are not.</summary>
/// <param name="Entries">The pairs with a readable sidecar and a DOCX.</param>
/// <param name="Problems">Ids whose sidecar is unreadable or whose DOCX is missing; kept on disk, never deleted by listing.</param>
internal sealed record RecoveryListing(IReadOnlyList<RecoveryEntry> Entries, IReadOnlyList<string> Problems);

/// <summary>
/// The recovery folder: pairs <c>&lt;id&gt;.docx</c> and <c>&lt;id&gt;.json</c>. The sidecar is written after the DOCX and is the commit marker: a DOCX without
/// a sidecar is never listed. Nothing touches the folder before the first call; ids are checked before a path is built, so a hostile name cannot leave the folder.
/// </summary>
internal sealed class RecoveryStore(string folder)
{
    private const string DocxExtension = ".docx";
    private const string SidecarExtension = ".json";

    public static string FolderFor(string? settingsFile) => Path.Combine(AppFiles.ConfigDirectoryFor(settingsFile), "recovery");

    public static string NewId() => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    public string Folder { get; } = folder;

    /// <summary>Creates the folder when it is missing (the writer of a copy calls it; listing and deleting never do).</summary>
    /// <remarks>On Linux and macOS the folder is made for its owner only (0700; the copies are unsaved document text); an existing folder keeps its mode. Windows ignores the mode.</remarks>
    public void EnsureFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Folder);
        }
        else
        {
            Directory.CreateDirectory(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public string DocxPathOf(string id) => Path.Combine(Folder, CheckedId(id) + DocxExtension);

    private string SidecarPathOf(string id) => Path.Combine(Folder, CheckedId(id) + SidecarExtension);

    /// <summary>Test seam: runs right before the sidecar's temporary file is moved into place.</summary>
    internal Action? BeforeSidecarMove { get; set; }

    /// <summary>Whether a sidecar file stands for the id (a complete pair is not required: this is about the commit marker only).</summary>
    public bool SidecarExists(string id) => File.Exists(SidecarPathOf(id));

    /// <summary>Writes the sidecar of an entry atomically; the DOCX must exist already.</summary>
    public async Task WriteSidecarAsync(RecoveryEntry entry)
    {
        var target = SidecarPathOf(entry.Id);
        EnsureFolder();
        var temporary = AtomicFile.NewTemporaryPath(target);
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, entry, RecoveryJsonContext.Default.RecoveryEntry).ConfigureAwait(false);
            }

            AtomicFile.FlushToDisk(temporary);
            BeforeSidecarMove?.Invoke();
            AtomicFile.MoveIntoPlace(temporary, target);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public RecoveryListing List()
    {
        var entries = new List<RecoveryEntry>();
        var problems = new List<string>();
        if (!Directory.Exists(Folder))
        {
            return new RecoveryListing(entries, problems);
        }

        List<string> sidecars;
        try
        {
            sidecars = Directory.EnumerateFiles(Folder, "*" + SidecarExtension).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RecoveryListing(entries, problems);
        }

        foreach (var path in sidecars)
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (!IsValidId(id))
            {
                continue;
            }

            var entry = Read(path);
            if (entry is null || !IsComplete(entry) || entry.Id != id || !File.Exists(DocxPathOf(id)))
            {
                problems.Add(id);
                continue;
            }

            entries.Add(entry);
        }

        entries.Sort((a, b) => b.Written.CompareTo(a.Written));
        return new RecoveryListing(entries, problems);
    }

    /// <summary>Removes the pair of an id and nothing else; a missing or locked file is not an error.</summary>
    public void Delete(string id)
    {
        if (!IsValidId(id))
        {
            return;
        }

        // The DOCX goes first: the sidecar is the commit marker, so a copy that cannot be removed stays listed and a later Delete retries it.
        if (TryDelete(DocxPathOf(id)))
        {
            TryDelete(SidecarPathOf(id));
        }
    }

    public static bool IsValidId(string id) =>
        id.Length == 32 && id.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    // A sidecar is untrusted data: a parsed value can still lack fields the callers dereference.
    private static bool IsComplete(RecoveryEntry entry) =>
        entry.Title is not null && entry.Bytes >= 0 && entry.OwnerProcessId > 0;

    private static string CheckedId(string id) =>
        IsValidId(id) ? id : throw new ArgumentException("Not a recovery id.", nameof(id));

    private static RecoveryEntry? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize(stream, RecoveryJsonContext.Default.RecoveryEntry);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be removed now stays; Delete keeps the sidecar then, so the next listing still shows the entry.
            return false;
        }
    }
}

/// <summary>Source-generated JSON metadata for the sidecar (no reflection, Native AOT safe).</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(RecoveryEntry))]
internal sealed partial class RecoveryJsonContext : JsonSerializerContext;
