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
/// not be changed while a save runs. A part that exists in the file but was not asked for (<see cref="DocxParts"/>) stays
/// untouched unless the caller sets its model property, which then replaces the content of that part.
/// </remarks>
public sealed class DocxFile : IDisposable
{
    private const string MacroMainType = "application/vnd.ms-word.document.macroEnabled.main+xml";
    private const string MacroTemplateMainType = "application/vnd.ms-word.template.macroEnabledTemplate.main+xml";

    private readonly byte[] _snapshot;
    private readonly PartMap _map;
    private bool _disposed;

    private DocxFile(byte[] snapshot, WordDocument model, DocxKind kind, PartMap map)
    {
        _snapshot = snapshot;
        Model = model;
        Kind = kind;
        _map = map;
    }

    /// <summary>The loaded parts. Parts that were not asked for, or that the file does not have, are <see langword="null"/>.</summary>
    public WordDocument Model { get; }

    public DocxKind Kind { get; }

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
        return Load(File.ReadAllBytes(path), parts);
    }

    /// <summary>Copies <paramref name="bytes"/> and loads the asked-for parts.</summary>
    /// <exception cref="DocxFormatException">The data is not a usable DOCX package.</exception>
    public static DocxFile Open(ReadOnlyMemory<byte> bytes, DocxParts parts = DocxParts.All) => Load(bytes.ToArray(), parts);

    /// <summary>
    /// Saves to <paramref name="targetPath"/> (which may be the opened file): a temporary file next to the file a symbolic link
    /// finally points to takes the snapshot, the model parts are written into it, a check compares every part that was not
    /// written with the snapshot and reads each written part back, and only then the temporary file replaces the target
    /// atomically. Any failure deletes the temporary file and leaves the target and its folder as they were.
    /// </summary>
    /// <exception cref="DocxSaveException">The check found a difference.</exception>
    /// <exception cref="SaveFailureException">The file could not be written or replaced.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before the replace.</exception>
    public async Task SaveAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var full = Path.GetFullPath(targetPath);
        await Task.Run(() => Save(full, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _disposed = true;

    // Opening.

    private static DocxFile Load(byte[] snapshot, DocxParts parts)
    {
        try
        {
            using var memory = new MemoryStream(snapshot, writable: false);
            using var package = WordprocessingDocument.Open(memory, false, new OpenSettings { AutoSave = false });
            var main = package.MainDocumentPart ?? throw new DocxFormatException("The file has no main document part.");
            var map = PartMap.Locate(main);
            var model = ReadModel(main, map, parts);
            var contentType = main.ContentType;
            var kind = contentType is MacroMainType or MacroTemplateMainType ? DocxKind.Docm : DocxKind.Docx;
            return new DocxFile(snapshot, model, kind, map);
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

    private static WordDocument ReadModel(MainDocumentPart main, PartMap map, DocxParts parts)
    {
        Story body;
        using (var stream = main.GetStream(FileMode.Open, FileAccess.Read))
        {
            body = DocumentXml.Read(stream);
        }

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

    private void Save(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = AtomicFile.ResolveLink(target);
        var temporary = AtomicFile.NewTemporaryPath(target);
        try
        {
            // A read-only target is refused here; what an existing target passes on (permissions, Hidden, access rules) is
            // given to the temporary file below, with the same code the save of a PDF uses.
            var metadata = AtomicFile.TargetMetadata.Read(resolved);
            var intended = Serialise();
            cancellationToken.ThrowIfCancellationRequested();
            CopySnapshot(temporary, metadata);
            var written = WritePackage(temporary, intended);
            AfterPackageWritten?.Invoke(temporary);
            Guard(temporary, written);
            AtomicFile.FlushToDisk(temporary);
            metadata.ApplyTo(temporary);
            cancellationToken.ThrowIfCancellationRequested();
            BeforeReplace?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFile.MoveIntoPlace(temporary, resolved, replaceExisting: true);
            metadata.AfterReplace(resolved);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new SaveFailureException(SaveFailureReason.NotWritable, "The file cannot be written.", ex);
        }
        catch (IOException ex)
        {
            throw new SaveFailureException(SaveFailureReason.WriteFailed, "The file could not be written.", ex);
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
    private List<Intent> Serialise()
    {
        var list = new List<Intent>
        {
            new(WrittenKind.Document, null, Bytes(s => DocumentXml.Write(Model.Body, s))),
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
                list.Add(new Intent(WrittenKind.HeaderFooter, id, Bytes(s => StoryXml.WritePart(part, s)), part.Kind));
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

    private sealed record Intent(WrittenKind Kind, string? Id, byte[] Bytes, HeaderFooterKind HeaderKind = HeaderFooterKind.Header);

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
            foreach (var intent in intents)
            {
                var (part, created) = Resolve(main, intent, commentsPart);
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
    private (OpenXmlPart Part, bool Created) Resolve(MainDocumentPart main, Intent intent, OpenXmlPart? commentsPart)
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

                    return intent.HeaderKind == HeaderFooterKind.Footer
                        ? (main.AddNewPart<FooterPart>(id), true)
                        : (main.AddNewPart<HeaderPart>(id), true);
                }
        }
    }

    private static (OpenXmlPart, bool)? Existing(MainDocumentPart main, string? id) =>
        id is not null && PartMap.ById(main, id) is { } part ? (part, false) : null;

    // The check.

    private void Guard(string temporary, (List<Written> Parts, string MainName) package)
    {
        var written = package.Parts;
        using var snapshotStream = new MemoryStream(_snapshot, writable: false);
        using var original = new ZipArchive(snapshotStream, ZipArchiveMode.Read);
        using var saved = ZipFile.OpenRead(temporary);

        var writtenNames = new HashSet<string>(written.Select(w => w.Name.TrimStart('/')), StringComparer.Ordinal);
        var created = written.Where(w => w.Created).Select(w => w.Name).ToList();
        var createdNames = new HashSet<string>(created.Select(n => n.TrimStart('/')), StringComparer.Ordinal);

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
                || (entry.FullName == mainRels && AddsOnlyRelationshipsTo(entry, package.MainName, created));
            if (!allowed)
            {
                throw new DocxSaveException(DocxSaveReason.PartsChanged, "/" + entry.FullName);
            }
        }

        foreach (var part in written)
        {
            var entry = saved.GetEntry(part.Name.TrimStart('/')) ?? throw new DocxSaveException(DocxSaveReason.ModelNotReadable, part.Name);
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
