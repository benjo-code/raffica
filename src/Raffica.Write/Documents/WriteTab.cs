using Raffica.Shell.Frame;
using Raffica.Write.Docx;
using Raffica.Write.Layout;

namespace Raffica.Write.Documents;

/// <summary>
/// An open document of the app: the model the title bar shows (<see cref="Tab"/>), the opened file, its layout with page 1 laid out, the pieces the layout
/// draws with and a token that ends when the tab closes. The tab owns what it holds and lets go of it in <see cref="Dispose"/>; the font catalog belongs
/// to the process and stays. The file is a prefix of the document (<see cref="DocxFile.IsPrefix"/>) until the full open has replaced it.
/// </summary>
internal sealed class WriteTab : IDisposable
{
    private readonly OpenedDocument _opened;
    private readonly CancellationTokenSource _cancellation = new();
    private int _disposed;

    public WriteTab(string path, OpenedDocument opened)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(opened);
        _opened = opened;
        Path = System.IO.Path.GetFullPath(path);
        Title = System.IO.Path.GetFileName(Path);
        Tab = new DocumentTab(Title, Path);
        Token = _cancellation.Token;
    }

    /// <summary>Raised once, at the start of <see cref="Dispose"/> and after the token was cancelled: whatever works on the document stops here.</summary>
    public event Action? Disposing;

    /// <summary>The model of the tab in the title bar.</summary>
    public DocumentTab Tab { get; }

    public string Title { get; }

    /// <summary>The full path of the file.</summary>
    public string Path { get; }

    /// <summary>The opened file; a prefix while <see cref="DocxFile.IsPrefix"/>. Not to be edited or saved.</summary>
    public DocxFile Docx => _opened.Docx;

    /// <summary>The layout of the document; page 1 is laid out. Safe to read from any thread (see <see cref="DocumentLayout"/>).</summary>
    public DocumentLayout Layout => _opened.Layout;

    /// <summary>The font catalog of the process the layout draws with (not owned by the tab).</summary>
    public FontCatalog Fonts => _opened.Fonts;

    /// <summary>Reads the pictures of the package; pass <see cref="PackageImageSource.Read"/> to an image cache. Throws once the tab is disposed.</summary>
    public PackageImageSource Images => _opened.Images;

    /// <summary>The prefix size the first page was made from (0: the file was opened whole).</summary>
    public int PrefixBlocks => _opened.PrefixBlocks;

    /// <summary>Ends when the tab is closed: the layout and every other background step of this document watch it.</summary>
    public CancellationToken Token { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Cancels what runs on the document, then lets go of the file and the picture source. Safe to call twice; the token source is disposed last.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // A callback of the token threw; the tab is closing anyway.
        }

        try
        {
            Disposing?.Invoke();
        }
        finally
        {
            _opened.Dispose();
            _cancellation.Dispose();
        }
    }
}
