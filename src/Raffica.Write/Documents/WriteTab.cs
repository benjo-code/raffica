using Raffica.Shell.Frame;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Viewing;

namespace Raffica.Write.Documents;

/// <summary>
/// An open document of the app: the model the title bar shows (<see cref="Tab"/>), the opened file, its layout with page 1 laid out, the pieces the layout
/// draws with and a token that ends when the tab closes. The tab owns what it holds and lets go of it in <see cref="Dispose"/>; the font catalog belongs
/// to the process and stays. The file is a prefix of the document (<see cref="DocxFile.IsPrefix"/>) until the full open has replaced it.
/// </summary>
internal sealed class WriteTab : IDisposable
{
    private readonly PackageImageSource _images;
    private readonly FontCatalog _fonts;
    private readonly int _prefixBlocks;
    private DocxFile _docx;
    private DocumentLayout _layout;
    private LayoutRunner? _runner;
    private EditSession? _editor;
    private readonly CancellationTokenSource _cancellation = new();
    private int _disposed;

    public WriteTab(string path, OpenedDocument opened)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(opened);
        _images = opened.Images;
        _fonts = opened.Fonts;
        _prefixBlocks = opened.PrefixBlocks;
        _docx = opened.Docx;
        _layout = opened.Layout;
        Path = System.IO.Path.GetFullPath(path);
        Title = System.IO.Path.GetFileName(Path);
        Tab = new DocumentTab(Title, Path);
        Token = _cancellation.Token;
    }

    /// <summary>
    /// A new document that belongs to no file yet: <see cref="Path"/> is null and <paramref name="untitledTitle"/> is the name (see <see cref="UntitledNames"/>). Save is Save as
    /// until the first save has bound the tab to a path (<see cref="Rebind"/>).
    /// </summary>
    public WriteTab(OpenedDocument opened, string untitledTitle)
    {
        ArgumentNullException.ThrowIfNull(opened);
        ArgumentException.ThrowIfNullOrWhiteSpace(untitledTitle);
        _images = opened.Images;
        _fonts = opened.Fonts;
        _prefixBlocks = opened.PrefixBlocks;
        _docx = opened.Docx;
        _layout = opened.Layout;
        Title = untitledTitle;
        Tab = new DocumentTab(Title);
        Token = _cancellation.Token;
    }

    /// <summary>Raised once, at the start of <see cref="Dispose"/> and after the token was cancelled: whatever works on the document stops here.</summary>
    public event Action? Disposing;

    /// <summary>The model of the tab in the title bar.</summary>
    public DocumentTab Tab { get; }

    /// <summary>The name of the file; follows <see cref="Rebind"/>.</summary>
    public string Title { get; private set; }

    /// <summary>The full path of the file; null for a document that was never saved (<see cref="IsUntitled"/>); follows <see cref="Rebind"/>.</summary>
    public string? Path { get; private set; }

    /// <summary>Whether the tab belongs to no file yet.</summary>
    public bool IsUntitled => Path is null;

    /// <summary>
    /// The path the Save as picker suggests (its name and folder) for a tab without a file, for example the old name of a restored document; null: the picker suggests the title.
    /// Cleared by <see cref="Rebind"/>.
    /// </summary>
    public string? SuggestedSavePath { get; set; }

    /// <summary>
    /// The tab now belongs to another file (after a successful Save as): the path, the name and the model of the title bar follow. UI thread; the layout, the picture
    /// source and the opened file stay as they are (the file knows its new origin itself, <see cref="DocxFile.OriginPath"/>).
    /// </summary>
    internal void Rebind(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = System.IO.Path.GetFullPath(path);
        Path = full;
        SuggestedSavePath = null;
        Title = System.IO.Path.GetFileName(full);
        Tab.Title = Title;
        Tab.Path = full;
    }

    /// <summary>Whether the document has changes the file on disk does not have (the revision of the model is not the saved one). False for a prefix: it cannot be edited.</summary>
    public bool IsDirty => Docx.Model.Edits.IsDirty;

    /// <summary>Gives the model of the title bar the dirty state of the document (the tab shows its mark and says "unsaved changes"). UI thread.</summary>
    internal void UpdateDirty()
    {
        if (IsDisposed)
        {
            return;
        }

        var dirty = IsDirty;
        var changed = Tab.IsDirty != dirty;
        Tab.IsDirty = dirty;
        if (changed)
        {
            DirtyChanged?.Invoke();
        }
    }

    /// <summary>Raised on the UI thread when the dirty mark of the tab changes (an edit made it dirty; a save or an undo to the saved revision made it clean). The autosave listens to this; nothing is raised before the first change.</summary>
    public event Action? DirtyChanged;

    /// <summary>The opened file; a prefix while <see cref="DocxFile.IsPrefix"/>. Not to be edited or saved.</summary>
    public DocxFile Docx => Volatile.Read(ref _docx);

    /// <summary>The layout of the document; page 1 is laid out. Safe to read from any thread (see <see cref="DocumentLayout"/>).</summary>
    public DocumentLayout Layout => Volatile.Read(ref _layout);

    /// <summary>The font catalog of the process the layout draws with (not owned by the tab).</summary>
    public FontCatalog Fonts => _fonts;

    /// <summary>Reads the pictures of the package; pass <see cref="PackageImageSource.Read"/> to an image cache. Throws once the tab is disposed.</summary>
    public PackageImageSource Images => _images;

    /// <summary>The prefix size the first page was made from (0: the file was opened whole).</summary>
    public int PrefixBlocks => _prefixBlocks;

    /// <summary>Ends when the tab is closed: the layout and every other background step of this document watch it.</summary>
    public CancellationToken Token { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// The background layout of the tab: made on first use, on the UI thread (it keeps that thread's dispatcher for its progress). Its page layout starts as <see cref="Layout"/>.
    /// </summary>
    public LayoutRunner Runner => _runner ??= new LayoutRunner(this);

    /// <summary>The editing session of the tab; null until the first caret or key asked for it (<see cref="EnsureEditor"/>). Nothing of editing exists before that.</summary>
    public EditSession? Editor => _editor;

    /// <summary>
    /// Raised on the UI thread after every change of the editing session: an edit, an undo, a redo, the replay of queued input and also a change of the selection alone (it
    /// follows the session's <c>Changed</c>); nothing is raised before the first one. A view of the model that is not the layout, like the outline, listens to this instead of
    /// polling; a reader runs often and must be cheap (debounce and compare before it rebuilds anything).
    /// </summary>
    public event Action? ModelEdited;

    /// <summary>Raised on the UI thread once, when the editing session is made (by the first caret or key): whoever follows the session (the ribbon's display of the formatting) hooks it here.</summary>
    public event Action<EditSession>? EditorCreated;

    /// <summary>Makes the editing session on first use (UI thread: it keeps that thread's dispatcher).</summary>
    public EditSession EnsureEditor()
    {
        if (_editor is null)
        {
            _editor = new EditSession(this);

            // Every edit, undo and redo (and the replay of what a save queued) shows in the tab's dirty mark; a save sets it itself.
            _editor.Changed += OnEditorChanged;
            EditorCreated?.Invoke(_editor);
        }

        return _editor;
    }

    private void OnEditorChanged()
    {
        UpdateDirty();
        ModelEdited?.Invoke();
    }

    /// <summary>
    /// The whole file has been read: it replaces the prefix (which is disposed), and the pictures are read from its package from now on so the prefix bytes can go.
    /// Called by the <see cref="Runner"/> on a pool thread.
    /// </summary>
    internal void CompleteOpen(DocxFile full)
    {
        ArgumentNullException.ThrowIfNull(full);
        var previous = Interlocked.Exchange(ref _docx, full);
        if (IsDisposed)
        {
            full.Dispose();
        }

        try
        {
            _images.Retarget(full.Package);
        }
        catch (ObjectDisposedException)
        {
            // The tab closed while the file was read.
        }

        if (!ReferenceEquals(previous, full))
        {
            previous.Dispose();
        }
    }

    /// <summary>The layout of the pages is another object now (the prefix layout could not take the whole model); called by the <see cref="Runner"/>.</summary>
    internal void ReplaceLayout(DocumentLayout layout) => Volatile.Write(ref _layout, layout ?? throw new ArgumentNullException(nameof(layout)));

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
            try
            {
                _runner?.Dispose();
            }
            finally
            {
                Docx.Dispose();
                _images.Dispose();
            }

            _cancellation.Dispose();
        }
    }
}
