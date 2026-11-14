using Raffica.Write.Docx;
using Raffica.Write.Layout;

namespace WriteLayoutBench;

/// <summary>What <see cref="FirstPageOpener.Open"/> made: the file, the layout with page 1 laid out, and how much of the body the file holds.</summary>
/// <param name="Docx">The opened file; a prefix when <see cref="DocxFile.IsPrefix"/>.</param>
/// <param name="Layout">The layout of <see cref="Docx"/> with page 1 published.</param>
/// <param name="PrefixBlocks">The prefix size that was used (0 when the file was opened whole).</param>
internal sealed record OpenedFirstPage(DocxFile Docx, DocumentLayout Layout, int PrefixBlocks) : IDisposable
{
    public void Dispose() => Docx.Dispose();
}

/// <summary>
/// The way to page 1 that an app takes: open only the start of the file, lay out page 1, and when page 1 turns out not to be final
/// (it reaches the last block of the prefix, so later blocks could change it) open a bigger prefix, and in the end the whole file.
/// The app's own version (Task 16) follows the same loop; the layout does not know the Docx project, so this lives where both are known.
/// </summary>
internal static class FirstPageOpener
{
    /// <summary>
    /// The first prefix size. A page holds some 40 lines, so 64 top-level blocks fill page 1 of any ordinary document (a table or a
    /// content control counts as one block, and a table that spans the page is a block the layout may cut, which is why the
    /// fallback exists). A bigger first guess only costs parse time on the path of every start.
    /// </summary>
    public const int DefaultPrefixBlocks = 64;

    /// <summary>How much a failed prefix grows: the next prefix is this many times bigger.</summary>
    public const int Growth = 8;

    /// <summary>Beyond this prefix size the whole file is opened.</summary>
    public const int LargestPrefixBlocks = 4096;

    /// <summary>
    /// Starts the first use of Skia, HarfBuzz and the default face on a pool thread, so it runs while the file is opened on this one
    /// (measured on Windows, Native AOT: the layout of page 1 drops from about 9.5 ms to 1.6 ms and the whole path gets 5 ms shorter,
    /// because the cold start of the native libraries no longer sits in front of the first line). The face is Calibri's (Carlito
    /// Regular), the default of nearly every document; a document that uses no other face then loads just that one. Await the task
    /// before the layout starts, or not at all: a face is made once and a second caller waits for it.
    /// </summary>
    public static Task WarmUp(FontCatalog fonts) => Task.Run(() => fonts.Resolve("Calibri", false, false));

    public static OpenedFirstPage Open(byte[] bytes, FontCatalog fonts, LayoutOptions options, int initialBlocks = DefaultPrefixBlocks)
    {
        _ = WarmUp(fonts);
        var blocks = Math.Max(1, initialBlocks);
        while (blocks <= LargestPrefixBlocks)
        {
            var prefix = DocxFile.OpenPrefix(bytes, blocks);
            var layout = DocumentLayout.Create(prefix.Model, fonts, options);
            layout.LayOutFirstPage();

            // A prefix that holds the whole body is the whole file; one whose page 1 never reached its last block cannot change.
            if (!prefix.IsPrefix || layout.FirstPageIsFinal)
            {
                return new OpenedFirstPage(prefix, layout, blocks);
            }

            prefix.Dispose();
            blocks *= Growth;
        }

        var whole = DocxFile.Open(bytes);
        var wholeLayout = DocumentLayout.Create(whole.Model, fonts, options);
        wholeLayout.LayOutFirstPage();
        return new OpenedFirstPage(whole, wholeLayout, 0);
    }
}
