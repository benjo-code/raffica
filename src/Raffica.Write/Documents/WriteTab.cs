using Raffica.Shell.Frame;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
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
    private readonly ImageCache _pictures;
    private bool _viewOwnsPictures;
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
        _pictures = opened.Pictures;
        _fonts = opened.Fonts;
        _prefixBlocks = opened.PrefixBlocks;
        _docx = opened.Docx;
        _layout = opened.Layout;
        _images.PendingMedia = PendingMediaOf;
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
        _pictures = opened.Pictures;
        _fonts = opened.Fonts;
        _prefixBlocks = opened.PrefixBlocks;
        _docx = opened.Docx;
        _layout = opened.Layout;
        _images.PendingMedia = PendingMediaOf;
        Title = untitledTitle;
        Tab = new DocumentTab(Title);
        Token = _cancellation.Token;
    }

    // The new pictures of the current file's model (read on pool threads; the store's reads take no lock, and a model that never had a picture makes no store).
    private ReadOnlyMemory<byte>? PendingMediaOf(string id) => Volatile.Read(ref _docx).Model is { HasMedia: true } model ? model.Media.TryGet(id) : null;

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

    /// <summary>
    /// The decoded pictures the page view draws from. Decode on get is off, so only the opener (page 1) and the view's prefetch fill it, never a draw. The tab owns it;
    /// the view that draws from it disposes it when the draw gate has closed and the last draw has ended (<see cref="ClaimPicturesForView"/>), otherwise
    /// <see cref="Dispose"/> does.
    /// </summary>
    /// <remarks>
    /// The cache is not cleared when <see cref="CompleteOpen"/> swaps the package: the picture ids are valid in both snapshots (the same file) and a read is deterministic
    /// on an in-memory snapshot, so a picture that failed to decode would fail again and one that is kept is still right. A change of the package's pictures (a later
    /// "insert picture") must call <see cref="ImageCache.Clear"/>.
    /// </remarks>
    public ImageCache Pictures => _pictures;

    /// <summary>The page view takes over disposing <see cref="Pictures"/> (after its draw gate closed); called once by the view that draws this tab.</summary>
    internal void ClaimPicturesForView() => _viewOwnsPictures = true;

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

    // ---- fields (milestone 4.5b): nothing below exists before the first dirty edit, the first complete layout of a document with fields or a save of one ----

    private FieldRefreshRunner? _fields;

    /// <summary>The refresh of the fields of this tab; null until it was needed (<see cref="EnsureFields"/>).</summary>
    public FieldRefreshRunner? Fields => _fields;

    /// <summary>The last page (from 0) the tab's view shows of the layout of a mode, 0 when it shows the other mode or nothing yet; null without a view. A field refresh lays out through it at once, as an edit does, and leaves the rest to the background.</summary>
    internal Func<LayoutMode, int>? PageInView { get; set; }

    /// <summary>False in a measurement run: no field refresh follows the edits there. The window sets it when it makes the tab.</summary>
    internal bool RefreshesFields { get; set; } = true;

    /// <summary>The clock of the refresh timer (a test sets one that it fires by hand before the first edit); null: the dispatcher clock, made with the runner.</summary>
    internal IFieldClock? FieldClock { get; set; }

    /// <summary>Test seam: replaces the scan for a field in the whole body (<see cref="AnyFields"/>).</summary>
    internal Func<WordDocument, bool>? FieldScan { get; set; }

    /// <summary>Whether any paragraph of the body holds a field (a scan that parses nothing).</summary>
    internal bool AnyFields() => (FieldScan ?? (model => FieldView.AnyIn(model)))(Docx.Model);

    /// <summary>How many times the wiring of the refresh threw (and was ignored): the refresh must never break an edit.</summary>
    internal int FieldWiringFailures { get; set; }

    /// <summary>Makes the refresh runner on first use (UI thread: it keeps that thread's dispatcher).</summary>
    internal FieldRefreshRunner EnsureFields() => _fields ??= new FieldRefreshRunner(this, FieldClock ?? new DispatcherFieldClock());

    /// <summary>
    /// Raised on the UI thread after a whole-body field refresh with the number of fields Write left alone (a name or switch outside the support table, a field in a note, a header, a footer, a comment or a tracked change); the window offers its
    /// bar from it, once per document session.
    /// </summary>
    public event Action<int>? FieldsNotUpdated;

    internal void ReportNotUpdated(int count) => FieldsNotUpdated?.Invoke(count);

    /// <summary>A refresh rewrote results: whoever shows the document or reads it through the model (the view, the outline) reads again. Not an edit: the dirty mark and the session's events are untouched.</summary>
    internal void NotifyFieldsRefreshed()
    {
        _runner?.NotifyContentChanged();
        ModelEdited?.Invoke();
    }

    /// <summary>
    /// Brings the field results up to date before a save writes the model (see <see cref="FieldRefreshRunner.SettleAsync"/>); a tab that never had a refresh and has no field makes none. Completes at once.
    /// </summary>
    internal Task SettleFieldsAsync(CancellationToken token)
    {
        if (!RefreshesFields || IsDisposed || token.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (_fields is null)
            {
                if (Docx.IsPrefix || !AnyFields())
                {
                    return Task.CompletedTask;
                }

                EnsureFields().Notify(null);
            }

            return _fields!.SettleAsync(token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The refresh is a convenience: a save goes on without it.
            FieldWiringFailures++;
            return Task.CompletedTask;
        }
    }

    private void OnEditorChanged()
    {
        var phases = _editor?.Phases;
        var stamp = EditPhases.Now(phases);
        UpdateDirty();
        ModelEdited?.Invoke();
        EditPhases.Lap(phases, EditPhases.TabDirty, ref stamp);
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
                _fields?.Dispose();
                _runner?.Dispose();
            }
            finally
            {
                Docx.Dispose();
                if (!_viewOwnsPictures)
                {
                    _pictures.Dispose();
                }

                _images.Dispose();
            }

            _cancellation.Dispose();
        }
    }
}
