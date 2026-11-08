using Raffica.Core.Pdf;
using Raffica.Core.Redaction;

namespace Raffica.Core.Pages;

/// <summary>
/// What every part of an extract or a split keeps from the document level. A part is the working copy of the document minus the pages
/// that are not wanted, so the information dictionary, the XMP, the whole outline, the names tree (attached files, document scripts) and
/// the form with the values of the fields of the dropped pages stay in it. This counts the items a reader can act on; the cards of the
/// app name them. Read once per card from the open document.
/// </summary>
/// <param name="AttachedFiles">The files of the names tree (the files of paperclip annotations belong to their pages and are not counted).</param>
/// <param name="HasScripts">Whether the document has a script (<c>/JS</c> entry).</param>
/// <param name="FieldsOnOtherPages">The form fields (by name) that have a widget on a page outside the part, with their values; for several parts the highest count of any part.</param>
/// <param name="BookmarksToOtherPages">The bookmarks that point to a page outside the part; for several parts the highest count of any part.</param>
public sealed record DocumentLevelContent(int AttachedFiles, bool HasScripts, int FieldsOnOtherPages, int BookmarksToOtherPages)
{
    /// <summary>Nothing of it (also the answer when it could not be read).</summary>
    public static DocumentLevelContent None { get; } = new(0, false, 0, 0);

    /// <summary>Gets whether there is nothing to name.</summary>
    public bool IsEmpty => this == None;

    /// <summary>
    /// Reads what the parts keep. The attached files and the scripts are counted from the file on disk with the raw scanner of the
    /// redaction (the document is not changed); a file that cannot be read gives none of them. The fields and bookmarks are read from
    /// the open document, page by page in short jobs.
    /// </summary>
    /// <param name="source">The open document.</param>
    /// <param name="path">The path of its file.</param>
    /// <param name="parts">The parts that will be written, at least one.</param>
    /// <param name="cancellationToken">Cancels before the next job.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<DocumentLevelContent> ReadAsync(PdfDocument source, string path, IReadOnlyList<SplitPart> parts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(parts);
        var inside = parts.Select(static part => part.Pages.ToHashSet()).ToList();

        var names = await Task.Run(() => ReadNames(path, cancellationToken), cancellationToken).ConfigureAwait(false);

        // Fields: the names of the fields by page, read for the pages that have widgets only.
        var fieldsByPage = new Dictionary<int, HashSet<string>>();
        if (source.HasAcroForm)
        {
            foreach (var page in (await source.GetFormInfoAsync(cancellationToken).ConfigureAwait(false)).PagesWithFields)
            {
                var fields = await source.GetFormFieldsAsync(page, cancellationToken).ConfigureAwait(false);
                fieldsByPage[page] = [.. fields.Select(static f => f.Key.FieldName)];
            }
        }

        var worstFields = 0;
        foreach (var pages in inside)
        {
            var outside = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (page, fieldNames) in fieldsByPage)
            {
                if (!pages.Contains(page))
                {
                    outside.UnionWith(fieldNames);
                }
            }

            worstFields = Math.Max(worstFields, outside.Count);
        }

        // Bookmarks: every entry with a target outside the part.
        var targets = new List<int>();
        var pending = new Stack<PdfOutlineItem>(await source.GetOutlineAsync(cancellationToken).ConfigureAwait(false));
        while (pending.Count > 0)
        {
            var item = pending.Pop();
            if (item.PageIndex is { } target)
            {
                targets.Add(target);
            }

            foreach (var child in item.Children)
            {
                pending.Push(child);
            }
        }

        var worstBookmarks = inside.Max(pages => targets.Count(target => !pages.Contains(target)));
        return new DocumentLevelContent(names.AttachedFiles, names.HasScripts, worstFields, worstBookmarks);
    }

    private static (int AttachedFiles, bool HasScripts) ReadNames(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            var inventory = RawContentScanner.Inventory(file, cancellationToken);
            return (Math.Max(0, inventory.EmbeddedFiles - inventory.AttachmentAnnotations), inventory.JavaScriptActions > 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, false);
        }
    }
}
