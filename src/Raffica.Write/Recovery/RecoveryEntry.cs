namespace Raffica.Write.Recovery;

/// <summary>
/// One recovery copy: the sidecar of a DOCX copy in the recovery folder. It holds no document text; the title is the tab's name (a file name or "Untitled").
/// </summary>
/// <param name="Id">The id of the pair (a GUID in "N" format); the files are <c>&lt;id&gt;.docx</c> and <c>&lt;id&gt;.json</c>.</param>
/// <param name="Title">The tab's title when the copy was written.</param>
/// <param name="OriginalPath">The file the tab was bound to, or <see langword="null"/> for an untitled document.</param>
/// <param name="Written">When the copy was written.</param>
/// <param name="Bytes">The size of the DOCX copy.</param>
/// <param name="OwnerProcessId">The process that wrote the copy.</param>
/// <param name="OwnerStarted">The start time of that process (with the id it names one process, not a later one that reuses the id).</param>
internal sealed record RecoveryEntry(
    string Id,
    string Title,
    string? OriginalPath,
    DateTimeOffset Written,
    long Bytes,
    int OwnerProcessId,
    DateTimeOffset OwnerStarted);
