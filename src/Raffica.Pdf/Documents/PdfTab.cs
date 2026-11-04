using Raffica.Core.Viewing;
using Raffica.Shell.Frame;

namespace Raffica.Pdf.Documents;

/// <summary>
/// An open document of the app: the viewer session, the model the title bar shows (<see cref="Tab"/>) and the dirty state. Version 2.1 has no
/// command that changes a document, so nothing outside the tests marks a tab dirty yet; the marker, the status bar and the close prompt
/// already follow it. The tab owns its session and disposes it with <see cref="Dispose"/>.
/// </summary>
internal sealed class PdfTab : IDisposable
{
    private int _disposing;

    public PdfTab(ViewerSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Session = session;
        Path = System.IO.Path.GetFullPath(path);
        Title = System.IO.Path.GetFileName(Path);
        Tab = new DocumentTab(Title, Path);
    }

    /// <summary>Raised when <see cref="IsDirty"/> changes.</summary>
    public event Action? DirtyChanged;

    /// <summary>Raised once, at the start of <see cref="Dispose"/> and before the session goes: whatever works on the document (a search) stops here.</summary>
    public event Action? Disposing;

    public ViewerSession Session { get; }

    /// <summary>The model of the tab in the title bar; its marker follows <see cref="IsDirty"/>.</summary>
    public DocumentTab Tab { get; }

    public string Title { get; }

    /// <summary>The full path of the file.</summary>
    public string? Path { get; }

    public bool IsDirty { get; private set; }

    public void MarkDirty() => SetDirty(true);

    public void MarkClean() => SetDirty(false);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposing, 1) == 0)
        {
            Disposing?.Invoke();
        }

        Session.Dispose();
    }

    private void SetDirty(bool dirty)
    {
        if (IsDirty == dirty)
        {
            return;
        }

        IsDirty = dirty;
        Tab.IsDirty = dirty;
        DirtyChanged?.Invoke();
    }
}
