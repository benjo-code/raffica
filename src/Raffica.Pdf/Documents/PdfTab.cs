using Avalonia.Threading;
using Raffica.Core.Viewing;
using Raffica.Pdf.Annotating;
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
    private AnnotationSession? _annotations;

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

    public string Title { get; private set; }

    /// <summary>The full path of the file.</summary>
    public string? Path { get; private set; }

    public bool IsDirty { get; private set; }

    /// <summary>The annotation session of the tab, made on first use and disposed with the tab. UI thread.</summary>
    public AnnotationSession Annotations => _annotations ??= new AnnotationSession(this, SynchronizationContext.Current ?? new DispatcherContext());

    /// <summary>The annotation session if one was made, else null (whoever only asks must not make one).</summary>
    public AnnotationSession? AnnotationsIfCreated => _annotations;

    /// <summary>The document was saved under another path (Save as): the path, the title and the title bar tab follow. UI thread.</summary>
    public void Rename(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        Title = System.IO.Path.GetFileName(Path);
        Tab.Title = Title;
        Tab.Path = Path;
    }

    public void MarkDirty() => SetDirty(true);

    public void MarkClean() => SetDirty(false);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposing, 1) == 0)
        {
            Disposing?.Invoke();
        }

        _annotations?.Dispose();
        Session.Dispose();
    }

    /// <summary>The fallback when no context is installed on the calling thread: posts to the UI thread.</summary>
    private sealed class DispatcherContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => Dispatcher.UIThread.Post(() => d(state));
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
