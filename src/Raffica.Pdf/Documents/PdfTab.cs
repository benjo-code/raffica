using Avalonia.Threading;
using Raffica.Core.Viewing;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.TextEditing;
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
    private FormSession? _forms;
    private OrganizeSession? _organize;
    private RedactSession? _redact;
    private OcrSession? _ocr;
    private TextEditSession? _textEdit;

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

    /// <summary>
    /// The form session of the tab, made on first use (it shares the annotation session's editor and history) and disposed with the tab.
    /// UI thread. Whoever only wants to know whether the document has a form asks <see cref="ViewerSession.Document"/> instead.
    /// </summary>
    public FormSession Forms => _forms ??= new FormSession(this, SynchronizationContext.Current ?? new DispatcherContext());

    /// <summary>
    /// The organize session of the tab (the page plan of the pages pane), made on first use and disposed with the tab; it reads and opens nothing
    /// before its <c>Enter</c>. UI thread.
    /// </summary>
    public OrganizeSession Organize
    {
        get
        {
            if (_organize is null)
            {
                _organize = new OrganizeSession(this, SynchronizationContext.Current ?? new DispatcherContext());
                OrganizeCreated?.Invoke(_organize);
            }

            return _organize;
        }
    }

    /// <summary>The redaction session of the tab, made on first use and disposed with the tab. UI thread.</summary>
    public RedactSession Redact => _redact ??= new RedactSession(this, SynchronizationContext.Current ?? new DispatcherContext());

    /// <summary>The text recognition session of the tab, made on first use and disposed with the tab. UI thread.</summary>
    public OcrSession Ocr
    {
        get
        {
            if (_ocr is null)
            {
                _ocr = new OcrSession(this, SynchronizationContext.Current ?? new DispatcherContext());
                OcrCreated?.Invoke(_ocr);
            }

            return _ocr;
        }
    }

    /// <summary>Raised when the text recognition session is made (the marks pane, which must not make it, listens to it). UI thread.</summary>
    public event Action<OcrSession>? OcrCreated;

    /// <summary>The text editing session of the tab, made on first use and disposed with the tab. UI thread.</summary>
    public TextEditSession TextEdit => _textEdit ??= new TextEditSession(this, SynchronizationContext.Current ?? new DispatcherContext());

    /// <summary>The redaction session if one was made, else null (whoever only asks must not make one).</summary>
    public RedactSession? RedactIfCreated => _redact;

    /// <summary>The text recognition session if one was made, else null.</summary>
    public OcrSession? OcrIfCreated => _ocr;

    /// <summary>The text editing session if one was made, else null.</summary>
    public TextEditSession? TextEditIfCreated => _textEdit;

    /// <summary>Raised when the organize session is made (the pages pane, which must not make it, listens to it). UI thread.</summary>
    public event Action<OrganizeSession>? OrganizeCreated;

    /// <summary>The organize session if one was made, else null (whoever only asks must not make one).</summary>
    public OrganizeSession? OrganizeIfCreated => _organize;

    /// <summary>The form session if one was made, else null.</summary>
    public FormSession? FormsIfCreated => _forms;

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

    /// <summary>
    /// The document was rewritten and the session adopted the written file: the annotation and form sessions belong to the old document, so they
    /// and the organize session (its plan is applied) are disposed and forgotten (made anew on first use, over the adopted document, with an editor that starts clean) and the tab is clean.
    /// The caller has unhooked its handlers from them first. UI thread.
    /// </summary>
    public void ResetForRewrittenDocument()
    {
        _organize?.Dispose();
        _organize = null;
        _redact?.Dispose();
        _redact = null;
        _ocr?.Dispose();
        _ocr = null;
        _textEdit?.Dispose();
        _textEdit = null;
        _forms?.Dispose();
        _forms = null;
        _annotations?.Dispose();
        _annotations = null;
        MarkClean();
    }

    public void MarkDirty() => SetDirty(true);

    public void MarkClean() => SetDirty(false);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposing, 1) == 0)
        {
            Disposing?.Invoke();
        }

        _organize?.Dispose();
        _redact?.Dispose();
        _ocr?.Dispose();
        _textEdit?.Dispose();
        _forms?.Dispose();
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
