using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using Raffica.Core.Saving;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Model;

namespace Raffica.Write.Docx;

/// <summary>
/// A DOCX (or DOCM) file opened into a <see cref="WordDocument"/>. The Open XML SDK is used as the package layer only: parts,
/// relationships, content types and part streams. The model is read with <see cref="XmlReader"/> from the part streams and
/// written back as raw XML into them; the SDK's own DOM never loads a part. A part the model understands and loaded is
/// rewritten on every save (equal content, not equal bytes); every other part keeps its decompressed bytes, which the save
/// checks before it replaces anything. Nothing the file refers to by an external relationship is ever fetched.
/// </summary>
/// <remarks>
/// The file is read into memory once, so a later change of the file on disk cannot corrupt a save. <see cref="Model"/> must
/// not be changed while a save runs. A file opened from a path remembers that path and its <see cref="FileStamp"/> (length and
/// last write time, taken before the bytes were read, as the PDF side does); a save to the same file (links resolved) refuses
/// with <see cref="SaveFailureReason.OriginalChanged"/> when the file on disk has another stamp or is gone, unless the caller
/// passes <c>overwriteChangedOriginal</c>, so a change made by another program since the open is never lost silently. A
/// successful save over the opened file records the stamp of the new file; a save to another path changes neither the stamp
/// nor the snapshot (the snapshot stays the file as it was opened, which is what every later save starts from). Opening
/// from bytes has no origin and no check. <see cref="SaveAsAsync(string, bool, CancellationToken)"/> is the save that moves the document to a new
/// path: after it succeeded, that path (and the stamp of the file it wrote) is the origin, so every later save is checked against the new file; the snapshot stays the file as it was
/// opened (it is what every save starts from), and a save to the path of the opened file is checked as an overwrite of the original. A part that exists in the file but was not asked for (<see cref="DocxParts"/>) stays
/// untouched unless the caller sets its model property, which then replaces the content of that part.
/// <para>
/// A file from <see cref="OpenPrefix(string, int)"/> is a prefix: <see cref="IsPrefix"/> is true when the body holds only its first
/// blocks. It exists to show the first page fast and is read-only: <see cref="SaveAsync(string, CancellationToken)"/> throws
/// <see cref="InvalidOperationException"/>, and a caller must not edit its model. The parts that were asked for are whole. The
/// body's <see cref="Story.FinalSection"/> is, for a prefix, the properties of the section that holds the last block read (the first
/// block after the prefix that ends a section, or the file's own final section when none does), so a layout of the prefix and of the
/// full file agree on every page that ends before the prefix does.
/// </para>
/// </remarks>
public sealed class DocxFile : IDisposable
{
    private const string MacroMainType = "application/vnd.ms-word.document.macroEnabled.main+xml";
    private const string MacroTemplateMainType = "application/vnd.ms-word.template.macroEnabledTemplate.main+xml";

    private readonly byte[] _snapshot;
    private readonly PartMap _map;
    private string? _originPath;
    private FileStamp? _originStamp;
    private bool _disposed;

    private DocxFile(byte[] snapshot, WordDocument model, DocxKind kind, PartMap map, string? originPath, FileStamp? originStamp, bool isPrefix)
    {
        IsPrefix = isPrefix;
        _originPath = originPath;
        _originStamp = originStamp;
        _snapshot = snapshot;
        Model = model;
        Kind = kind;
        _map = map;
    }

    /// <summary>The loaded parts. Parts that were not asked for, or that the file does not have, are <see langword="null"/>.</summary>
    public WordDocument Model { get; }

    public DocxKind Kind { get; }

    /// <summary>
    /// The bytes of the file as they were read (the snapshot every part and every later save starts from; it is not a copy, so it is not changed and holds
    /// no file handle). A viewer reads pictures from it on demand without opening the file again.
    /// </summary>
    public ReadOnlyMemory<byte> Package => _snapshot;

    /// <summary>
    /// True when this file came from <see cref="OpenPrefix(string, int)"/> and its body was cut short: <see cref="Model"/> holds the
    /// first blocks only (the final section is complete), so the file cannot be saved. False for a full open, and for a prefix open of
    /// a document whose body fitted in the prefix (it is then the whole file, though its comments were not loaded).
    /// </summary>
    public bool IsPrefix { get; }

    /// <summary>
    /// Called on the saving thread (a pool thread) just before the model is read into memory, after the checks that refuse a save (a changed original, an existing
    /// target, a read-only file) and before anything is created on disk. The caller that must keep the model still takes its hold here, on the thread that will release it
    /// in <see cref="AfterModelRead"/>. A throw from it fails the save and <see cref="AfterModelRead"/> is not called (the hook releases what it took before it throws).
    /// </summary>
    public Action? BeforeModelRead { get; set; }

    /// <summary>
    /// Called on the saving thread once the model has been written into memory (also when that fails, so a hold taken in <see cref="BeforeModelRead"/> is released on every path
    /// by the thread that took it); from then on the save no longer reads <see cref="Model"/>, so the caller may let it change again while the package is written. It is called
    /// exactly when <see cref="BeforeModelRead"/> returned, and never for a save that is refused before that point. A throw from it fails the save and goes to the caller.
    /// </summary>
    public Action? AfterModelRead { get; set; }

    /// <summary>The full path of the file this document was opened from or last saved to with <see cref="SaveAsAsync(string, bool, CancellationToken)"/>; null for a document opened from bytes.</summary>
    public string? OriginPath => _originPath;

    /// <summary>Test seam: replaces the move of the finished temporary file (temporary, target, replace existing); returns the leftover path like <see cref="AtomicFile.MoveIntoPlace"/>.</summary>
    internal Func<string, string, bool, string?>? MoveIntoPlace { get; set; }

    /// <summary>Test seam: runs after the checks and before the temporary file replaces the target.</summary>
    internal Action? BeforeReplace { get; set; }

    /// <summary>Test seam: wraps the stream a model part is written through (part name with a leading slash, the part's stream).</summary>
    internal Func<string, Stream, Stream>? WrapPartWrite { get; set; }

    /// <summary>Test seam: runs with the path of the temporary file once the package is written and closed, before the checks.</summary>
    internal Action<string>? AfterPackageWritten { get; set; }

    /// <summary>Reads the whole file into memory and loads the asked-for parts.</summary>
    /// <exception cref="DocxFormatException">The file is not a usable DOCX package.</exception>
    public static DocxFile Open(string path, DocxParts parts = DocxParts.All)
    {
        ArgumentNullException.ThrowIfNull(path);
        var full = Path.GetFullPath(path);

        // The stamp is taken before the bytes are read (as for a PDF): a file replaced during the read then makes a later save
        // to the same path warn instead of overwriting the newer file.
        var stamp = FileStamp.Of(AtomicFile.ResolveLink(full));
        return Load(ReadAll(full), parts, full, stamp);
    }

    /// <summary>The parts <see cref="OpenPrefix(string, int)"/> loads: everything a first page can need (comments are not shown).</summary>
    private const DocxParts PrefixParts = DocxParts.Styles | DocxParts.Numbering | DocxParts.Settings | DocxParts.Notes | DocxParts.HeadersFooters;

    /// <summary>
    /// Opens the start of a file for a quick first page: styles, numbering, settings, notes, headers and footers, the first
    /// <paramref name="maxBlocks"/> top-level blocks of the body and, as <see cref="Story.FinalSection"/>, the properties of the
    /// section those blocks end in (cut out of the part's bytes, so the section of page 1 is right even when no section break lies in
    /// the prefix; the headers and footers that section names are loaded). The rest of the body is neither parsed nor kept. <see cref="IsPrefix"/> says whether anything was cut off; the result cannot be saved. A body that cannot be cut safely
    /// (the end of the part is not recognizable) is read whole.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="maxBlocks">How many top-level body blocks to keep, at least 1.</param>
    /// <exception cref="DocxFormatException">The file is not a usable DOCX package.</exception>
    public static DocxFile OpenPrefix(string path, int maxBlocks)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBlocks, 1);
        var full = Path.GetFullPath(path);
        var stamp = FileStamp.Of(AtomicFile.ResolveLink(full));
        return Load(ReadAll(full), PrefixParts, full, stamp, maxBlocks);
    }

    /// <summary>Copies <paramref name="bytes"/> and opens its start as <see cref="OpenPrefix(string, int)"/> does.</summary>
    /// <exception cref="DocxFormatException">The data is not a usable DOCX package.</exception>
    public static DocxFile OpenPrefix(ReadOnlyMemory<byte> bytes, int maxBlocks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBlocks, 1);
        return Load(bytes.ToArray(), PrefixParts, null, null, maxBlocks);
    }

    /// <summary>Reads a file whole; it may be open for writing by another program (the share allows readers, writers and deletion).</summary>
    private static byte[] ReadAll(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream(stream.CanSeek && stream.Length < int.MaxValue ? (int)stream.Length : 0);
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Copies <paramref name="bytes"/> and loads the asked-for parts.</summary>
    /// <exception cref="DocxFormatException">The data is not a usable DOCX package.</exception>
    public static DocxFile Open(ReadOnlyMemory<byte> bytes, DocxParts parts = DocxParts.All) => Load(bytes.ToArray(), parts, null, null);

    /// <summary>
    /// Saves to <paramref name="targetPath"/> (which may be the opened file): a temporary file next to the file a symbolic link
    /// finally points to takes the snapshot, the model parts are written into it, a check compares every part that was not
    /// written with the snapshot and reads each written part back, and only then the temporary file replaces the target
    /// atomically. Any failure deletes the temporary file and leaves the target and its folder as they were.
    /// </summary>
    /// <param name="targetPath">The file to create or replace.</param>
    /// <param name="cancellationToken">Cancels the save before the replace.</param>
    /// <remarks>
    /// Saving to the path of the opened file ends in <see cref="SaveFailureReason.OriginalChanged"/>, before anything is written,
    /// when the file on disk has changed or is gone since it was opened; the overload with <c>overwriteChangedOriginal</c>
    /// is for the caller that asked the user and got a yes.
    /// </remarks>
    /// <exception cref="DocxSaveException">The check found a difference.</exception>
    /// <exception cref="SaveFailureException">The file could not be written or replaced, or (<see cref="SaveFailureReason.OriginalChanged"/>) it changed on disk since it was opened.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the replace.</exception>
    public Task<SaveReport> SaveAsync(string targetPath, CancellationToken cancellationToken = default) => SaveAsync(targetPath, overwriteChangedOriginal: false, cancellationToken);

    /// <inheritdoc cref="SaveAsync(string, CancellationToken)"/>
    /// <param name="overwriteChangedOriginal">Whether the caller confirmed overwriting a changed original (see the other overload).</param>
    /// <exception cref="InvalidOperationException">This file is a prefix (<see cref="IsPrefix"/>).</exception>
    public Task<SaveReport> SaveAsync(string targetPath, bool overwriteChangedOriginal, CancellationToken cancellationToken = default) =>
        StartSave(targetPath, replaceExisting: true, overwriteChangedOriginal, rebind: false, cancellationToken);

    /// <summary>
    /// Saves the document under the path the user chose and makes that path its origin ("Save as"). Like <see cref="SaveAsync(string, CancellationToken)"/> in every
    /// other respect, with these differences. <paramref name="replaceExisting"/> false means the user named a file that did not exist: a file that exists at the
    /// target when the call starts ends in <see cref="SaveFailureReason.TargetExists"/> before anything is written, and one that appears while the file is written is never
    /// replaced (the move itself refuses, atomically) and ends in the same failure; true means the picker confirmed replacing an existing file. A target that is the opened
    /// file is an overwrite of the original and is checked as such (<see cref="SaveFailureReason.OriginalChanged"/>). After a successful save <see cref="OriginPath"/> and the
    /// stamp the next save is checked against are those of the new file; a failed save changes neither.
    /// </summary>
    /// <param name="targetPath">The file to create, or to replace when <paramref name="replaceExisting"/> is true.</param>
    /// <param name="replaceExisting">Whether an existing file at the target may be replaced.</param>
    /// <param name="cancellationToken">Cancels the save before the replace.</param>
    /// <returns>What the save left to tell: the path written and a leftover file the replace could not delete.</returns>
    /// <exception cref="InvalidOperationException">This file is a prefix (<see cref="IsPrefix"/>).</exception>
    public Task<SaveReport> SaveAsAsync(string targetPath, bool replaceExisting, CancellationToken cancellationToken = default) =>
        StartSave(targetPath, replaceExisting, overwriteChangedOriginal: false, rebind: true, cancellationToken);

    /// <inheritdoc cref="SaveAsAsync(string, bool, CancellationToken)"/>
    /// <param name="overwriteChangedOriginal">Whether the caller confirmed overwriting a changed original (the target is the opened file and it changed on disk since it was opened).</param>
    public Task<SaveReport> SaveAsAsync(string targetPath, bool replaceExisting, bool overwriteChangedOriginal, CancellationToken cancellationToken = default) =>
        StartSave(targetPath, replaceExisting, overwriteChangedOriginal, rebind: true, cancellationToken);

    /// <summary>
    /// Writes a recovery copy: <see cref="SaveAsync(string, bool, CancellationToken)"/> to another path (nothing is rebound, no stamp or snapshot changes), with two differences. Inside the model read
    /// (<see cref="BeforeModelRead"/> to <see cref="AfterModelRead"/>) the main part is not written block by block every time: the bytes of the blocks that did not change since the last copy are reused
    /// (<see cref="BodySnapshotCache"/>), so the model is held for the blocks that changed only. And the guard after the write is lighter: the written bytes of every model part are compared with the package in
    /// chunks, and the main part is read back as well-formed XML with a <c>w:document</c> root, not as a whole model (the other parts are read back as a model, and the checks of the parts that were not written are
    /// the same). The parts it writes are the same as a save of the same model gives. The cache belongs to the file, so call this for one copy at a time (the recovery snapshot's in-flight mark does) and only for the model the editor reports its changes for.
    /// </summary>
    public Task<SaveReport> SaveSnapshotAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        if (IsPrefix)
        {
            throw new InvalidOperationException("A file opened as a prefix holds only the start of the document and cannot be saved.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        var full = Path.GetFullPath(targetPath);
        return Task.Run(() => Save(full, replaceExisting: true, overwriteChangedOriginal: false, rebind: false, cancellationToken, useSnapshotCache: true), cancellationToken);
    }

    private BodySnapshotCache? _snapshotCache;

    /// <summary>Test seam: the bytes of the main part as a user save writes them (<c>cached</c> false) or as a recovery copy does (<c>cached</c> true, the cache of this file).</summary>
    internal byte[] SerialiseMainForTest(bool cached) => cached ? (_snapshotCache ??= new BodySnapshotCache()).Serialise(Model) : Bytes(s => DocumentXml.Write(Model.Body, s));

    /// <summary>For diagnostics and tests: how many top-level blocks the last recovery copy wrote again and how many it reused (zeros before the first copy).</summary>
    public (int Written, int Reused) LastSnapshotPass => _snapshotCache?.LastPass ?? (0, 0);

    private async Task<SaveReport> StartSave(string targetPath, bool replaceExisting, bool overwriteChangedOriginal, bool rebind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        if (IsPrefix)
        {
            throw new InvalidOperationException("A file opened as a prefix holds only the start of the document and cannot be saved.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        var full = Path.GetFullPath(targetPath);
        return await Task.Run(() => Save(full, replaceExisting, overwriteChangedOriginal, rebind, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _disposed = true;
        _snapshotCache?.Dispose();
    }

    // Opening.

    private static DocxFile Load(byte[] snapshot, DocxParts parts, string? originPath, FileStamp? originStamp, int? maxBlocks = null)
    {
        try
        {
            // The usual place of the main part is inflated on the pool while the package layer starts up (that start is the biggest
            // single cost of a prefix open); the bytes are used only when the package says the main part is that one.
            var early = maxBlocks is not null ? Task.Run(() => InflateMain(snapshot)) : null;
            using var memory = new MemoryStream(snapshot, writable: false);
            using var package = WordprocessingDocument.Open(memory, false, new OpenSettings { AutoSave = false });
            var main = package.MainDocumentPart ?? throw new DocxFormatException("The file has no main document part.");
            var map = PartMap.Locate(main);
            var model = ReadModel(main, map, parts, maxBlocks, early, out var truncated);
            var contentType = main.ContentType;
            var kind = contentType is MacroMainType or MacroTemplateMainType ? DocxKind.Docm : DocxKind.Docx;
            return new DocxFile(snapshot, model, kind, map, originPath, originStamp, truncated);
        }
        catch (XmlException ex)
        {
            throw new DocxFormatException("A part of the file is not well-formed or has an unexpected structure.", ex);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException or NotSupportedException or UriFormatException or KeyNotFoundException or FormatException or OpenXmlPackageException)
        {
            throw new DocxFormatException("The file is not a usable DOCX package.", ex);
        }
    }

    private static WordDocument ReadModel(MainDocumentPart main, PartMap map, DocxParts parts, int? maxBlocks, Task<byte[]?>? early, out bool truncated)
    {
        var body = ReadBody(main, maxBlocks, early, out truncated);

        if (body.Root.Name != Names.Document)
        {
            throw new DocxFormatException("The main part is not a w:document.");
        }

        var model = new WordDocument(body);
        if (parts.HasFlag(DocxParts.Styles) && Find(main, map.Styles) is { } styles)
        {
            model.Styles = Read(styles, StylesXml.Read);
        }

        if (parts.HasFlag(DocxParts.Numbering) && Find(main, map.Numbering) is { } numbering)
        {
            model.Numbering = Read(numbering, NumberingXml.Read);
        }

        if (parts.HasFlag(DocxParts.Settings) && Find(main, map.Settings) is { } settings)
        {
            model.Settings = Read(settings, SettingsXml.Read);
        }

        if (parts.HasFlag(DocxParts.Notes))
        {
            if (Find(main, map.Footnotes) is { } footnotes)
            {
                model.Footnotes = Read(footnotes, s => NotesXml.Read(s, "footnotes"));
            }

            if (Find(main, map.Endnotes) is { } endnotes)
            {
                model.Endnotes = Read(endnotes, s => NotesXml.Read(s, "endnotes"));
            }
        }

        if (parts.HasFlag(DocxParts.Comments) && Find(main, map.Comments) is { } comments)
        {
            var extended = ExtendedPart(main, map, comments);
            using var commentsStream = comments.GetStream(FileMode.Open, FileAccess.Read);
            using var extendedStream = extended?.GetStream(FileMode.Open, FileAccess.Read);
            model.Comments = CommentsXml.Read(commentsStream, extendedStream);
        }

        if (parts.HasFlag(DocxParts.HeadersFooters))
        {
            var byPart = new Dictionary<string, HeaderFooterPart>(StringComparer.Ordinal);
            foreach (var id in PartMap.HeaderFooterIds(body))
            {
                // A reference that points nowhere, to an external relationship or to a part that is no header or footer
                // is skipped: the part (if any) stays untouched and the reference stays in the section.
                if (PartMap.HeaderFooterById(main, id) is not { } part)
                {
                    continue;
                }

                var uri = part.Uri.OriginalString;
                if (!byPart.TryGetValue(uri, out var loaded))
                {
                    loaded = Read(part, StoryXml.ReadPart);
                    byPart[uri] = loaded;
                }

                model.HeadersFooters[id] = loaded;
            }
        }

        return model;
    }

    private const string UsualMainPartName = "/word/document.xml";

    /// <summary>Inflates the usual main part without the package layer, or returns null when the package has none there or cannot be read that way.</summary>
    private static byte[]? InflateMain(byte[] snapshot)
    {
        try
        {
            using var memory = new MemoryStream(snapshot, writable: false);
            using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
            if (zip.GetEntry(UsualMainPartName[1..]) is not { } entry || entry.Length > Array.MaxLength)
            {
                return null;
            }

            var bytes = new byte[(int)entry.Length];
            using var stream = entry.Open();
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or EndOfStreamException or NotSupportedException)
        {
            // Whatever is wrong shows again, with the proper exception, when the package layer reads the part.
            return null;
        }
    }

    private static Story ReadBody(MainDocumentPart main, int? maxBlocks, Task<byte[]?>? early, out bool truncated)
    {
        truncated = false;
        if (maxBlocks is not { } limit)
        {
            using var whole = main.GetStream(FileMode.Open, FileAccess.Read);
            return DocumentXml.Read(whole);
        }

        var bytes = (main.Uri.OriginalString == UsualMainPartName ? early?.GetAwaiter().GetResult() : null) ?? ReadPartBytes(main);
        if (DocumentXml.ReadPrefix(bytes, limit, out truncated) is { } prefix)
        {
            return prefix;
        }

        truncated = false;
        using var all = new MemoryStream(bytes, writable: false);
        return DocumentXml.Read(all);
    }

    private static byte[] ReadPartBytes(MainDocumentPart main)
    {
        using var stream = main.GetStream(FileMode.Open, FileAccess.Read);
        byte[] bytes;
        if (stream.CanSeek && stream.Length <= Array.MaxLength)
        {
            bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
        }
        else
        {
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            bytes = memory.ToArray();
        }

        return bytes;
    }

    private static T Read<T>(OpenXmlPart part, Func<Stream, T> read)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        return read(stream);
    }

    private static OpenXmlPart? Find(MainDocumentPart main, string? id) => id is null ? null : PartMap.ById(main, id);

    private static OpenXmlPart? ExtendedPart(MainDocumentPart main, PartMap map, OpenXmlPart comments)
    {
        if (map.CommentsExtended is not { } id)
        {
            return null;
        }

        return PartMap.ById(map.ExtendedInCommentsPart ? comments : main, id);
    }

    // Saving.

    private sealed record Written(string Name, WrittenKind Kind, byte[] Bytes, bool Created);

    private enum WrittenKind
    {
        Document,
        Styles,
        Numbering,
        Settings,
        Footnotes,
        Endnotes,
        Comments,
        CommentsExtended,
        HeaderFooter,
    }

    private SaveReport Save(string target, bool replaceExisting, bool overwriteChangedOriginal, bool rebind, CancellationToken cancellationToken, bool useSnapshotCache = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = AtomicFile.ResolveLink(target);
        var overOrigin = _originPath is not null && PathRules.AreEqual(AtomicFile.ResolveLink(_originPath), resolved);
        if (overOrigin && !overwriteChangedOriginal && FileStamp.Of(resolved) != _originStamp)
        {
            throw new SaveFailureException(SaveFailureReason.OriginalChanged, "Another file stands at the path of the original since it was opened.");
        }

        if (!replaceExisting && !overOrigin && (File.Exists(resolved) || Directory.Exists(resolved)))
        {
            // Told before anything is written; the move below refuses again for a file that appears from here on.
            throw new SaveFailureException(SaveFailureReason.TargetExists, "A file already exists at the target.");
        }

        var temporary = AtomicFile.NewTemporaryPath(target);
        try
        {
            // A read-only target is refused here; what an existing target passes on (permissions, Hidden, access rules) is
            // given to the temporary file below, with the same code the save of a PDF uses.
            var metadata = AtomicFile.TargetMetadata.Read(resolved);
            BeforeModelRead?.Invoke();
            List<Intent> intended;
            try
            {
                intended = Serialise(useSnapshotCache);
            }
            finally
            {
                AfterModelRead?.Invoke();
            }

            cancellationToken.ThrowIfCancellationRequested();
            CopySnapshot(temporary, metadata);
            var written = WritePackage(temporary, intended);
            AfterPackageWritten?.Invoke(temporary);
            Guard(temporary, written, useSnapshotCache);
            AtomicFile.FlushToDisk(temporary);
            metadata.ApplyTo(temporary);
            cancellationToken.ThrowIfCancellationRequested();
            BeforeReplace?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            var replace = replaceExisting || overOrigin;
            var leftover = MoveIntoPlace is { } move ? move(temporary, resolved, replace) : AtomicFile.MoveIntoPlace(temporary, resolved, replace);
            metadata.AfterReplace(resolved);
            if (overOrigin || rebind)
            {
                _originStamp = FileStamp.Of(resolved);
            }

            if (rebind)
            {
                // The new file is the document's file from now on; the snapshot stays as it is (it is the base of every save, not a copy of the file on disk).
                _originPath = target;
            }

            return new SaveReport(target, leftover);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A full disk, a file in use and no access are told apart as for a PDF.
            throw AtomicFile.FailureOf(ex, "The file could not be written.");
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException)
        {
            // The package layer failed on the temporary copy (not a model problem, not a difference the check found): a failed
            // write. The temporary file is removed below and the target is untouched.
            throw new SaveFailureException(SaveFailureReason.WriteFailed, "The package could not be written.", ex);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    private void CopySnapshot(string temporary, AtomicFile.TargetMetadata metadata)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (metadata.UnixMode is { } mode && !OperatingSystem.IsWindows())
        {
            // Created with the permissions of the replaced file, so a private file is never readable by others, not even briefly.
            options.UnixCreateMode = mode;
        }

        using var stream = new FileStream(temporary, options);
        stream.Write(_snapshot);
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the target is untouched either way.
        }
    }

    /// <summary>Serialises every model part before anything is created, so a model that cannot be written ends the save early.</summary>
    private List<Intent> Serialise(bool useSnapshotCache)
    {
        var list = new List<Intent>
        {
            new(WrittenKind.Document, null, useSnapshotCache ? (_snapshotCache ??= new BodySnapshotCache()).Serialise(Model) : Bytes(s => DocumentXml.Write(Model.Body, s))),
        };
        if (Model.Styles is { } styles)
        {
            list.Add(new Intent(WrittenKind.Styles, null, Bytes(s => StylesXml.Write(styles, s))));
        }

        if (Model.Numbering is { } numbering)
        {
            list.Add(new Intent(WrittenKind.Numbering, null, Bytes(s => NumberingXml.Write(numbering, s))));
        }

        if (Model.Settings is { } settings)
        {
            list.Add(new Intent(WrittenKind.Settings, null, Bytes(s => SettingsXml.Write(settings, s))));
        }

        if (Model.Footnotes is { } footnotes)
        {
            list.Add(new Intent(WrittenKind.Footnotes, null, Bytes(s => NotesXml.Write(footnotes, s))));
        }

        if (Model.Endnotes is { } endnotes)
        {
            list.Add(new Intent(WrittenKind.Endnotes, null, Bytes(s => NotesXml.Write(endnotes, s))));
        }

        if (Model.Comments is { } comments)
        {
            using var extended = new MemoryStream();
            var needed = false;
            var commentBytes = Bytes(s => needed = CommentsXml.Write(comments, s, extended));
            list.Add(new Intent(WrittenKind.Comments, null, commentBytes));

            // The extension part is written whenever the codec says it is needed (one was read, something of it is kept, or a
            // comment has done or reply state). When it is not needed, a part the file has stays exactly as it is: it is never
            // deleted and never replaced by less, and one the file does not have is not created.
            if (needed)
            {
                list.Add(new Intent(WrittenKind.CommentsExtended, null, extended.ToArray()));
            }
        }

        var done = new HashSet<HeaderFooterPart>(ReferenceEqualityComparer.Instance);
        foreach (var (id, part) in Model.HeadersFooters)
        {
            if (done.Add(part))
            {
                list.Add(new Intent(WrittenKind.HeaderFooter, id, Bytes(s => StoryXml.WritePart(part, s)), part.Kind, part));
            }
        }

        return list;
    }

    private static byte[] Bytes(Action<Stream> write)
    {
        using var stream = new MemoryStream();
        write(stream);
        return stream.ToArray();
    }

    private sealed record Intent(WrittenKind Kind, string? Id, byte[] Bytes, HeaderFooterKind HeaderKind = HeaderFooterKind.Header, HeaderFooterPart? Part = null);

    private (List<Written> Parts, string MainName) WritePackage(string temporary, List<Intent> intents)
    {
        var written = new List<Written>();
        var writtenUris = new HashSet<string>(StringComparer.Ordinal);
        string mainName;
        using (var package = WordprocessingDocument.Open(temporary, true, new OpenSettings { AutoSave = false }))
        {
            var main = package.MainDocumentPart ?? throw new DocxSaveException(DocxSaveReason.PartsChanged, "/word/document.xml");
            mainName = main.Uri.OriginalString;
            OpenXmlPart? commentsPart = null;
            var existing = intents.Where(i => i.Kind == WrittenKind.HeaderFooter && PartMap.ById(main, i.Id!) is not null).Select(i => i.Id!).ToHashSet(StringComparer.Ordinal);
            foreach (var intent in intents)
            {
                var (part, created) = Resolve(main, intent, commentsPart, existing);
                if (intent.Kind == WrittenKind.Comments)
                {
                    commentsPart = part;
                }

                var name = part.Uri.OriginalString;
                if (!writtenUris.Add(name))
                {
                    // Two different model objects resolve to one part (two relationship ids, one part): which content wins is
                    // not decided, and the save refuses instead of dropping one. The part is named, not its text.
                    throw new DocxSaveException(DocxSaveReason.PartsChanged, name);
                }

                using (var raw = part.GetStream(FileMode.Create, FileAccess.Write))
                using (var target = WrapPartWrite?.Invoke(name, raw) ?? raw)
                {
                    target.Write(intent.Bytes);
                }

                written.Add(new Written(name, intent.Kind, intent.Bytes, created));
            }
        }

        return (written, mainName);
    }

    /// <summary>Finds the part of an intent in the copy, or creates it (with its relationship and content type) when the file had none. A part is never deleted.</summary>
    private (OpenXmlPart Part, bool Created) Resolve(MainDocumentPart main, Intent intent, OpenXmlPart? commentsPart, HashSet<string> existingHeaders)
    {
        switch (intent.Kind)
        {
            case WrittenKind.Document:
                return (main, false);
            case WrittenKind.Styles:
                return Existing(main, _map.Styles) ?? (main.AddNewPart<StyleDefinitionsPart>(), true);
            case WrittenKind.Numbering:
                return Existing(main, _map.Numbering) ?? (main.AddNewPart<NumberingDefinitionsPart>(), true);
            case WrittenKind.Settings:
                return Existing(main, _map.Settings) ?? (main.AddNewPart<DocumentSettingsPart>(), true);
            case WrittenKind.Footnotes:
                return Existing(main, _map.Footnotes) ?? (main.AddNewPart<FootnotesPart>(), true);
            case WrittenKind.Endnotes:
                return Existing(main, _map.Endnotes) ?? (main.AddNewPart<EndnotesPart>(), true);
            case WrittenKind.Comments:
                return Existing(main, _map.Comments) ?? (main.AddNewPart<WordprocessingCommentsPart>(), true);
            case WrittenKind.CommentsExtended:
                {
                    if (_map.CommentsExtended is { } id && PartMap.ById(_map.ExtendedInCommentsPart ? commentsPart ?? main : main, id) is { } existing)
                    {
                        return (existing, false);
                    }

                    return (main.AddNewPart<WordprocessingCommentsExPart>(), true);
                }

            default:
                {
                    var id = intent.Id!;
                    if (PartMap.ById(main, id) is { } existing)
                    {
                        // The id may belong to a part that is no header or footer: the model's header must never overwrite it.
                        if (PartMap.HeaderFooterById(main, id) is null)
                        {
                            throw new DocxSaveException(DocxSaveReason.PartsChanged, existing.Uri.OriginalString);
                        }

                        return (existing, false);
                    }

                    OpenXmlPart added = intent.HeaderKind == HeaderFooterKind.Footer ? main.AddNewPart<FooterPart>(id) : main.AddNewPart<HeaderPart>(id);
                    if (CopySourceOf(intent.Part, existingHeaders) is { } sourceId && PartMap.ById(main, sourceId) is { } source)
                    {
                        CopyRelationships(source, added);
                    }

                    return (added, true);
                }
        }
    }

    // The id of the part in the file whose relationships a new header or footer part gets: the nearest part it was copied from (see HeaderFooterPart.RelationshipsFrom) that the file already has.
    private string? CopySourceOf(HeaderFooterPart? part, HashSet<string> existingHeaders)
    {
        var current = part;
        for (var depth = 0; current?.RelationshipsFrom is { } source && depth < 64; depth++)
        {
            foreach (var (id, candidate) in Model.HeadersFooters)
            {
                if (ReferenceEquals(candidate, source) && existingHeaders.Contains(id))
                {
                    return id;
                }
            }

            current = source;
        }

        return null;
    }

    // Every relationship of the source part, with the same id and target: the pictures and other parts it points to are shared (one part, two relationships), links and other external targets are repeated.
    private static void CopyRelationships(OpenXmlPart source, OpenXmlPart target)
    {
        foreach (var pair in source.Parts)
        {
            target.AddPart(pair.OpenXmlPart, pair.RelationshipId);
        }

        foreach (var relationship in source.ExternalRelationships)
        {
            target.AddExternalRelationship(relationship.RelationshipType, relationship.Uri, relationship.Id);
        }

        foreach (var relationship in source.HyperlinkRelationships)
        {
            target.AddHyperlinkRelationship(relationship.Uri, relationship.IsExternal, relationship.Id);
        }
    }

    private static (OpenXmlPart, bool)? Existing(MainDocumentPart main, string? id) =>
        id is not null && PartMap.ById(main, id) is { } part ? (part, false) : null;

    // The check.

    /// <param name="temporary">The package that was just written.</param>
    /// <param name="package">What was written into it.</param>
    /// <param name="forSnapshot">
    /// A recovery copy: the same checks, with two lighter ways of making them, so a copy written every few seconds does not load the collector of an editing process (the check of a user save
    /// read the main part back into a whole model, 20 of the 25 MiB a copy of the 311-page thesis allocated). The written bytes are compared in chunks, not copied into one more array, and the
    /// main part is read back as well-formed XML with the root <c>w:document</c> (a model that cannot be read from it is found when the copy is opened).
    /// </param>
    private void Guard(string temporary, (List<Written> Parts, string MainName) package, bool forSnapshot = false)
    {
        var written = package.Parts;
        using var snapshotStream = new MemoryStream(_snapshot, writable: false);
        using var original = new ZipArchive(snapshotStream, ZipArchiveMode.Read);
        using var saved = ZipFile.OpenRead(temporary);

        var writtenNames = new HashSet<string>(written.Select(w => w.Name.TrimStart('/')), StringComparer.Ordinal);
        var created = written.Where(w => w.Created).Select(w => w.Name).ToList();
        var createdNames = new HashSet<string>(created.Select(n => n.TrimStart('/')), StringComparer.Ordinal);
        var createdRels = created.ToDictionary(n => RelationshipsPartOf(n).TrimStart('/'), n => n, StringComparer.Ordinal);

        // Creating a part adds relationships to the main part. When the main part had none, the SDK also creates its
        // relationships part; that entry (and only that one) may appear, and its content is checked below.
        var mainRels = created.Count > 0 ? RelationshipsPartOf(package.MainName).TrimStart('/') : null;

        foreach (var entry in original.Entries)
        {
            if (IsFolder(entry) || writtenNames.Contains(entry.FullName))
            {
                continue;
            }

            var other = saved.GetEntry(entry.FullName) ?? throw new DocxSaveException(DocxSaveReason.PartsChanged, "/" + entry.FullName);
            if (IsStructural(entry.FullName) ? !StructureKept(entry, other, package.MainName, created) : !SameContent(entry, other))
            {
                throw new DocxSaveException(DocxSaveReason.PartsChanged, "/" + entry.FullName);
            }
        }

        foreach (var entry in saved.Entries)
        {
            if (IsFolder(entry) || original.GetEntry(entry.FullName) is not null)
            {
                continue;
            }

            var allowed = createdNames.Contains(entry.FullName)
                || (createdRels.TryGetValue(entry.FullName, out var ofPart) && RelationshipsOfCreatedPartAreKnown(entry, ofPart, original))
                || (entry.FullName == mainRels && AddsOnlyRelationshipsTo(entry, package.MainName, created));
            if (!allowed)
            {
                throw new DocxSaveException(DocxSaveReason.PartsChanged, "/" + entry.FullName);
            }
        }

        foreach (var part in written)
        {
            var entry = saved.GetEntry(part.Name.TrimStart('/')) ?? throw new DocxSaveException(DocxSaveReason.ModelNotReadable, part.Name);
            if (forSnapshot)
            {
                if (!ContentEquals(entry, part.Bytes))
                {
                    throw new DocxSaveException(DocxSaveReason.ModelNotReadable, part.Name);
                }

                if (part.Kind == WrittenKind.Document)
                {
                    ReadBackWellFormed(part);
                    continue;
                }

                ReadBack(part, written);
                continue;
            }

            byte[] back;
            using (var stream = entry.Open())
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                back = memory.ToArray();
            }

            if (!back.AsSpan().SequenceEqual(part.Bytes))
            {
                throw new DocxSaveException(DocxSaveReason.ModelNotReadable, part.Name);
            }

            ReadBack(part, written);
        }
    }

    // Whether the entry holds exactly these bytes, compared in chunks (no copy of the entry is made).
    private static bool ContentEquals(ZipArchiveEntry entry, byte[] expected)
    {
        using var stream = entry.Open();
        var buffer = new byte[81920];
        var offset = 0;
        while (true)
        {
            var read = ReadFull(stream, buffer);
            if (read == 0)
            {
                return offset == expected.Length;
            }

            if (offset + read > expected.Length || !buffer.AsSpan(0, read).SequenceEqual(expected.AsSpan(offset, read)))
            {
                return false;
            }

            offset += read;
        }
    }

    // The light read-back of a recovery copy's main part: well-formed XML under a w:document root, no model built.
    private static void ReadBackWellFormed(Written part) => ReadBackWellFormed(part.Name, part.Bytes);

    internal static void ReadBackWellFormed(string partName, byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, XmlKit.ReaderSettings());
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "document" || reader.NamespaceURI != Names.W.NamespaceName)
            {
                throw new XmlException();
            }

            while (reader.Read())
            {
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever the reader throws on these bytes (not well-formed, not allowed by the reader settings, not readable) means the same: the part cannot be read back.
            throw new DocxSaveException(DocxSaveReason.ModelNotReadable, partName);
        }
    }

    private static void ReadBack(Written part, List<Written> all)
    {
        try
        {
            using var stream = new MemoryStream(part.Bytes, writable: false);
            switch (part.Kind)
            {
                case WrittenKind.Document:
                    if (DocumentXml.Read(stream).Root.Name != Names.Document)
                    {
                        throw new XmlException();
                    }

                    break;
                case WrittenKind.Styles:
                    _ = StylesXml.Read(stream);
                    break;
                case WrittenKind.Numbering:
                    _ = NumberingXml.Read(stream);
                    break;
                case WrittenKind.Settings:
                    _ = SettingsXml.Read(stream);
                    break;
                case WrittenKind.Footnotes:
                    _ = NotesXml.Read(stream, "footnotes");
                    break;
                case WrittenKind.Endnotes:
                    _ = NotesXml.Read(stream, "endnotes");
                    break;
                case WrittenKind.Comments:
                    {
                        var extended = all.Find(w => w.Kind == WrittenKind.CommentsExtended);
                        using var extendedStream = extended is null ? null : new MemoryStream(extended.Bytes, writable: false);
                        _ = CommentsXml.Read(stream, extendedStream);
                        break;
                    }

                case WrittenKind.CommentsExtended:
                    // Read together with the comments part; alone it needs a model to fill.
                    _ = XDocument.Load(stream);
                    break;
                default:
                    _ = StoryXml.ReadPart(stream);
                    break;
            }
        }
        catch (XmlException)
        {
            throw new DocxSaveException(DocxSaveReason.ModelNotReadable, part.Name);
        }
    }

    private static bool IsFolder(ZipArchiveEntry entry) => entry.FullName.EndsWith('/');

    private static bool IsStructural(string name) =>
        name == "[Content_Types].xml" || name.EndsWith(".rels", StringComparison.Ordinal);

    private static bool SameContent(ZipArchiveEntry a, ZipArchiveEntry b)
    {
        using var first = a.Open();
        using var second = b.Open();
        var bufferA = new byte[81920];
        var bufferB = new byte[81920];
        while (true)
        {
            var readA = ReadFull(first, bufferA);
            var readB = ReadFull(second, bufferB);
            if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
            {
                return false;
            }

            if (readA == 0)
            {
                return true;
            }
        }
    }

    private static int ReadFull(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>The name of the relationships part of a part: <c>/word/document.xml</c> has <c>/word/_rels/document.xml.rels</c>.</summary>
    private static string RelationshipsPartOf(string partName)
    {
        var slash = partName.LastIndexOf('/');
        return partName[..(slash + 1)] + "_rels/" + partName[(slash + 1)..] + ".rels";
    }

    /// <summary>
    /// The content types and the relationship parts may gain entries that belong to the parts the save created, and nothing
    /// else: every entry the file had must still be there, unchanged. Identical bytes pass at once.
    /// </summary>
    private static bool StructureKept(ZipArchiveEntry original, ZipArchiveEntry saved, string mainName, List<string> created)
    {
        if (SameContent(original, saved))
        {
            return true;
        }

        try
        {
            var before = Load(original);
            var after = Load(saved);
            if (before.Name != after.Name)
            {
                return false;
            }

            var had = new HashSet<string>(before.Elements().Select(Describe), StringComparer.Ordinal);
            var kept = new HashSet<string>(after.Elements().Select(Describe), StringComparer.Ordinal);
            if (!had.All(kept.Contains))
            {
                return false;
            }

            var added = after.Elements().Where(e => !had.Contains(Describe(e)));
            if (original.FullName == "[Content_Types].xml")
            {
                return added.All(e => IsContentTypeOfCreated(e, created));
            }

            return original.FullName == RelationshipsPartOf(mainName).TrimStart('/') && added.All(e => IsRelationshipToCreated(e, mainName, created));
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>The relationships of a part the save created: external targets, or parts the file already had (a copied header shares the pictures of its source); never a part that is not in the file.</summary>
    private static bool RelationshipsOfCreatedPartAreKnown(ZipArchiveEntry entry, string partName, ZipArchive original)
    {
        try
        {
            var baseUri = new Uri("http://parts" + partName[..(partName.LastIndexOf('/') + 1)], UriKind.Absolute);
            foreach (var element in Load(entry).Elements())
            {
                if (element.Name.LocalName != "Relationship")
                {
                    return false;
                }

                if ((string?)element.Attribute("TargetMode") == "External")
                {
                    continue;
                }

                var target = (string?)element.Attribute("Target");
                if (string.IsNullOrEmpty(target) || !Uri.TryCreate(baseUri, target, out var resolved) || original.GetEntry(Uri.UnescapeDataString(resolved.AbsolutePath).TrimStart('/')) is null)
                {
                    return false;
                }
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool AddsOnlyRelationshipsTo(ZipArchiveEntry entry, string mainName, List<string> created)
    {
        try
        {
            return Load(entry).Elements().All(e => IsRelationshipToCreated(e, mainName, created));
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>An <c>Override</c> for a created part, or a <c>Default</c> for the extension of one.</summary>
    private static bool IsContentTypeOfCreated(XElement element, List<string> created)
    {
        if (element.Name.LocalName == "Override")
        {
            return created.Contains((string?)element.Attribute("PartName") ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        return element.Name.LocalName == "Default"
            && created.Any(n => string.Equals(Path.GetExtension(n).TrimStart('.'), (string?)element.Attribute("Extension"), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An internal relationship of the main part whose target is a created part.</summary>
    private static bool IsRelationshipToCreated(XElement element, string mainName, List<string> created)
    {
        if (element.Name.LocalName != "Relationship" || (string?)element.Attribute("TargetMode") == "External")
        {
            return false;
        }

        var target = (string?)element.Attribute("Target");
        if (string.IsNullOrEmpty(target))
        {
            return false;
        }

        var baseUri = new Uri("http://parts" + mainName[..(mainName.LastIndexOf('/') + 1)], UriKind.Absolute);
        return Uri.TryCreate(baseUri, target, out var resolved) && created.Contains(Uri.UnescapeDataString(resolved.AbsolutePath), StringComparer.OrdinalIgnoreCase);
    }

    private static XElement Load(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, XmlKit.ReaderSettings());
        return XElement.Load(reader);
    }

    private static string Describe(XElement element) =>
        element.Name.LocalName + "|" + string.Join('|', element.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).Select(a => a.Name + "=" + a.Value));
}
