using System.IO.Compression;
using Raffica.Write.Docx;
using Raffica.Write.Layout;

namespace Raffica.Write.Documents;

/// <summary>What <see cref="DocumentOpener.Open"/> made: the file, the layout with page 1 published, and the pieces the tab needs while it lives.</summary>
/// <param name="Docx">The opened file: a prefix (<see cref="DocxFile.IsPrefix"/>) unless the whole body fitted in it.</param>
/// <param name="Layout">The layout of <see cref="Docx"/> with page 1 laid out; it is not final beyond page 1 while <see cref="DocxFile.IsPrefix"/> (see <see cref="DocumentLayout.ReplaceModel"/>).</param>
/// <param name="Fonts">The font catalog of the process the layout draws with.</param>
/// <param name="Images">Reads the pictures of the package on demand; disposed with the tab.</param>
/// <param name="PrefixBlocks">The prefix size that was used (0 when the file was opened whole).</param>
internal sealed record OpenedDocument(DocxFile Docx, DocumentLayout Layout, FontCatalog Fonts, PackageImageSource Images, int PrefixBlocks) : IDisposable
{
    public void Dispose()
    {
        Docx.Dispose();
        Images.Dispose();
    }
}

/// <summary>The outcome of one open: the document or the reason there is none.</summary>
internal readonly record struct OpenAttempt(OpenedDocument? Document, OpenError Error)
{
    public static OpenAttempt Of(OpenedDocument document) => new(document, default);

    public static OpenAttempt Failure(OpenError error) => new(null, error);
}

/// <summary>
/// The way to page 1 of a file, run on a pool thread: check the file, open only the start of it, lay out page 1, and when page 1 turns out not to be final (it
/// reaches the last block of the prefix, so later blocks could change it) open a bigger prefix and in the end the whole file. Everything that can go wrong with a
/// file ends in an <see cref="OpenError"/>, never in an exception; only cancellation and a lack of memory pass through. The file is read once (the package of the
/// first prefix is the snapshot every later step and every picture reads), so a file that changes during the open cannot make two steps disagree.
/// </summary>
internal static class DocumentOpener
{
    /// <summary>
    /// The first prefix size. A page holds some 40 lines, so 64 top-level blocks fill page 1 of any ordinary document (a table or a content control counts as one
    /// block, and a table that spans the page is a block the layout may cut, which is why the fallback exists). A bigger first guess only costs parse time on the
    /// path of every start. The same value and the same loop as the engine benchmark (<c>bench/write-layout</c>), which keeps its own copy.
    /// </summary>
    public const int DefaultPrefixBlocks = 64;

    /// <summary>How much a failed prefix grows: the next prefix is this many times bigger.</summary>
    public const int Growth = 8;

    /// <summary>Beyond this prefix size the whole file is opened.</summary>
    public const int LargestPrefixBlocks = 4096;

    /// <summary>The largest file the app opens (the file is read into memory whole); a bigger one is <see cref="OpenError.TooLarge"/>.</summary>
    public const long DefaultMaxFileLength = 512L * 1024 * 1024;

    /// <summary>
    /// Starts the first use of Skia, HarfBuzz and the default face on a pool thread, so it runs while the file is opened on this one (measured on Windows: the
    /// layout of page 1 drops from about 9.5 ms to 1.6 ms). The face is Calibri's (Carlito Regular), the default of nearly every document.
    /// </summary>
    public static Task WarmUp(FontCatalog fonts) => Task.Run(() => fonts.Resolve("Calibri", false, false));

    /// <summary>Opens <paramref name="fullPath"/> (a full path) up to a laid-out page 1.</summary>
    /// <param name="fullPath">The file.</param>
    /// <param name="work">The fonts of the process and the counters.</param>
    /// <param name="maxLength">The largest file to open.</param>
    /// <param name="token">Cancels between the steps.</param>
    /// <param name="initialBlocks">The first prefix size (tests make it small).</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static OpenAttempt Open(string fullPath, OpenWork work, long maxLength, CancellationToken token, int initialBlocks = DefaultPrefixBlocks)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(fullPath))
            {
                return OpenAttempt.Failure(Directory.Exists(fullPath) ? OpenError.NotADocx : OpenError.NotFound);
            }

            var length = new FileInfo(fullPath).Length;
            if (length == 0)
            {
                return OpenAttempt.Failure(OpenError.NotADocx);
            }

            if (length > maxLength)
            {
                return OpenAttempt.Failure(OpenError.TooLarge);
            }

            var fonts = work.Fonts();
            _ = WarmUp(fonts);
            work.NoteFileOpened();
            DocxFile first;
            try
            {
                first = DocxFile.OpenPrefix(fullPath, Math.Max(1, initialBlocks));
            }
            catch (DocxFormatException)
            {
                return OpenAttempt.Failure(Classify(fullPath));
            }

            return LayOutFirstPage(first, fonts, work, Math.Max(1, initialBlocks), token);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return OpenAttempt.Failure(Map(ex));
        }
    }

    private static OpenAttempt LayOutFirstPage(DocxFile first, FontCatalog fonts, OpenWork work, int initialBlocks, CancellationToken token)
    {
        var images = new PackageImageSource(first.Package);
        var docx = first;
        try
        {
            var options = new LayoutOptions(LayoutMode.Pages, ImageBytes: images.Read);
            var blocks = initialBlocks;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var layout = DocumentLayout.Create(docx.Model, fonts, options);
                work.NoteLayoutCreated();
                layout.LayOutFirstPage();

                // A prefix that holds the whole body is the whole file; one whose page 1 never reached its last block cannot change.
                if (!docx.IsPrefix || layout.FirstPageIsFinal)
                {
                    var made = new OpenedDocument(docx, layout, fonts, images, docx.IsPrefix ? blocks : 0);
                    images = null;
                    return OpenAttempt.Of(made);
                }

                if (!ReferenceEquals(docx, first))
                {
                    docx.Dispose();
                }

                blocks *= Growth;
                docx = blocks <= LargestPrefixBlocks ? DocxFile.OpenPrefix(first.Package, blocks) : DocxFile.Open(first.Package);
                if (blocks > LargestPrefixBlocks)
                {
                    token.ThrowIfCancellationRequested();
                    var wholeLayout = DocumentLayout.Create(docx.Model, fonts, options);
                    work.NoteLayoutCreated();
                    wholeLayout.LayOutFirstPage();
                    var whole = new OpenedDocument(docx, wholeLayout, fonts, images, 0);
                    images = null;
                    return OpenAttempt.Of(whole);
                }
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            docx.Dispose();
            return OpenAttempt.Failure(ex is DocxFormatException ? OpenError.Corrupt : Map(ex));
        }
        catch
        {
            docx.Dispose();
            throw;
        }
        finally
        {
            images?.Dispose();
        }
    }

    /// <summary>The reason for an exception of the file system or the layout; never the text of it.</summary>
    internal static OpenError Map(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => OpenError.NotFound,
        UnauthorizedAccessException => OpenError.AccessDenied,
        PathTooLongException => OpenError.Other,
        IOException io when IsSharingViolation(io) => OpenError.AccessDenied,
        _ => OpenError.Other,
    };

    // ERROR_SHARING_VIOLATION (32) and ERROR_LOCK_VIOLATION (33): another program holds the file.
    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    /// <summary>
    /// Tells a file that is no Word document from a damaged one after the package layer refused it: no zip signature is no DOCX; a zip with no <c>word/</c> part is
    /// another kind of package; a zip that cannot be read, or one with word parts that could not be opened, is damaged.
    /// </summary>
    internal static OpenError Classify(string fullPath)
    {
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> signature = stackalloc byte[2];
            if (stream.Read(signature) < 2 || signature[0] != (byte)'P' || signature[1] != (byte)'K')
            {
                return OpenError.NotADocx;
            }

            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            return zip.Entries.Any(e => e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase)) ? OpenError.Corrupt : OpenError.NotADocx;
        }
        catch (InvalidDataException)
        {
            return OpenError.Corrupt;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Map(ex);
        }
    }
}
