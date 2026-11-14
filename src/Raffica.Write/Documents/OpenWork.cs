using Raffica.Write.Layout;

namespace Raffica.Write.Documents;

/// <summary>
/// The heavy objects of opening a document and the counters that prove nothing of it exists before the first file: the font catalog (one for the process, made when
/// the first file is opened), the files opened and the layouts made. The app owns one and hands it to every window, so the windows of macOS share the catalog;
/// <c>StartupCreatesNoLayoutOrFontWork</c> reads the counters of a window that was made with no file.
/// </summary>
internal sealed class OpenWork
{
    private readonly Lazy<FontCatalog> _fonts;
    private int _files;
    private int _layouts;

    public OpenWork()
    {
        _fonts = new Lazy<FontCatalog>(() => FontCatalog.CreateShipped(), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The font catalog of the process, made on the first call (any thread).</summary>
    public FontCatalog Fonts() => _fonts.Value;

    /// <summary>Whether <see cref="Fonts"/> was ever asked for.</summary>
    public bool FontsCreated => _fonts.IsValueCreated;

    /// <summary>How many <see cref="Raffica.Write.Docx.DocxFile"/> opens were started.</summary>
    public int FilesOpened => Volatile.Read(ref _files);

    /// <summary>How many <see cref="DocumentLayout"/> objects were made.</summary>
    public int LayoutsCreated => Volatile.Read(ref _layouts);

    internal void NoteFileOpened() => Interlocked.Increment(ref _files);

    internal void NoteLayoutCreated() => Interlocked.Increment(ref _layouts);

    /// <summary>True when nothing of the open path ever ran: no font catalog, no file, no layout.</summary>
    public bool NothingStarted => !FontsCreated && FilesOpened == 0 && LayoutsCreated == 0;
}
