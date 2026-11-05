using Raffica.Core.Forms;
using Raffica.Core.Pdf;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Forms;

/// <summary>
/// The form state of one tab: the widgets read page by page (loaded when a page comes into view, never for a document without an AcroForm),
/// the empty required fields, the highlight, and the one way to write a value, which goes through the tab's annotation editor so a value is
/// one step of the same history as a mark and the tab becomes dirty through the annotation session. Everything that touches the document is
/// awaited; the UI thread never waits for PDFium. The <c>PagesInvalidated</c> event of the viewer session arrives on any thread and is posted
/// to the UI context before anything is read again; <see cref="Changed"/> is raised on the UI thread only.
/// </summary>
internal sealed class FormSession : IDisposable
{
    private readonly PdfTab _tab;
    private readonly SynchronizationContext _ui;
    private readonly object _gate = new();
    private readonly Dictionary<int, IReadOnlyList<FormField>> _pages = [];
    private readonly Dictionary<int, Task> _inflight = [];
    private readonly HashSet<int> _stale = [];
    private readonly CancellationTokenSource _cts = new();
    private PdfDocument? _document;
    private FormInfo? _info;
    private IReadOnlyList<string> _required = [];
    private bool _requiredRead;
    private bool _highlight;
    private volatile bool _disposed;

    public FormSession(PdfTab tab, SynchronizationContext ui)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(ui);
        _tab = tab;
        _ui = ui;
        _tab.Session.PagesInvalidated += OnPagesInvalidated;
    }

    /// <summary>A test seam: reads the widgets of a page instead of the document (default: the document, background priority).</summary>
    internal Func<PdfDocument, int, CancellationToken, Task<IReadOnlyList<FormField>>>? PageReader { get; init; }

    /// <summary>Raised on the UI thread when a page was (re)read, the required list or the info changed.</summary>
    public event Action? Changed;

    /// <summary>Whether the document has an AcroForm (the synchronous answer of the document); when <see langword="false"/> no job is ever started.</summary>
    public bool HasForm
    {
        get
        {
            try
            {
                return !_disposed && _tab.Session.Document is { } document && document.HasAcroForm;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <summary>What the document says about its form; <see langword="null"/> until <see cref="LoadInfoAsync"/> read it.</summary>
    public FormInfo? Info
    {
        get
        {
            lock (_gate)
            {
                return _info;
            }
        }
    }

    /// <summary>The names of the required fields that have no value, as of the last <see cref="RefreshRequiredAsync"/>.</summary>
    public IReadOnlyList<string> EmptyRequired
    {
        get
        {
            lock (_gate)
            {
                return _required;
            }
        }
    }

    /// <summary>Whether the form fields are highlighted in the main view. Applied to the session once per change, and never for a document without a form.</summary>
    public bool Highlight
    {
        get => _highlight;
        set
        {
            if (_highlight == value)
            {
                return;
            }

            _highlight = value;
            if (HasForm)
            {
                _tab.Session.SetFormHighlight(value);
            }
        }
    }

    /// <summary>The widgets of a page as last read; empty while the page is not loaded (or has none).</summary>
    public IReadOnlyList<FormField> FieldsOn(int page)
    {
        lock (_gate)
        {
            return _pages.TryGetValue(page, out var fields) ? fields : [];
        }
    }

    /// <summary>Whether the widgets of <paramref name="page"/> are read.</summary>
    public bool IsLoaded(int page)
    {
        lock (_gate)
        {
            return _pages.ContainsKey(page);
        }
    }

    /// <summary>The topmost widget (the last of the page's annotations) whose rectangle holds the point, locked ones included; <see langword="null"/> when none does.</summary>
    public FormField? FieldAt(int page, double x, double y)
    {
        var fields = FieldsOn(page);
        for (var i = fields.Count - 1; i >= 0; i--)
        {
            if (fields[i].Bounds.Contains(x, y))
            {
                return fields[i];
            }
        }

        return null;
    }

    /// <summary>Reads what the document says about its form (single flight in the document); does nothing without a form.</summary>
    public async Task LoadInfoAsync()
    {
        if (!HasForm || _tab.Session.Document is not { } document)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                SyncDocument(document);
            }

            var info = await document.GetFormInfoAsync(_cts.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_disposed && ReferenceEquals(_document, document))
                {
                    _info = info;
                }
            }

            RaiseChanged();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
        }
    }

    /// <summary>
    /// Reads the widgets of a page unless they are known; a call while that page is being read joins it (single flight per page). A document
    /// without a form returns at once and starts nothing.
    /// </summary>
    public Task LoadPageAsync(int page) => StartPage(page, refresh: false);

    /// <summary>Reads the empty required fields again (the pages with fields, one short background job each in Core).</summary>
    public async Task RefreshRequiredAsync()
    {
        if (!HasForm || _tab.Session.Document is not { } document)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                SyncDocument(document);
            }

            var names = await document.GetEmptyRequiredFieldsAsync(_cts.Token).ConfigureAwait(false);
            lock (_gate)
            {
                // A result of a document that was swapped out meanwhile belongs to nobody.
                if (!_disposed && ReferenceEquals(_document, document))
                {
                    _required = names;
                    _requiredRead = true;
                }
            }

            RaiseChanged();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
        }
    }

    /// <summary>
    /// Writes a value as one undo step of the tab's history. A locked field and a refused text are refused before the document is touched;
    /// a value equal to the one the field has is <see cref="FormEditResult.Unchanged"/> and no step. The cached pages of the field are read
    /// again before this returns. UI thread.
    /// </summary>
    public async Task<FormEditResult> SetValueAsync(FormField field, FormFieldState state)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(state);
        if (field.Lock != FormLock.None)
        {
            return new FormEditResult.Refused(FormEditProblem.Locked, field.LockReason ?? string.Empty);
        }

        if (state.Text is { } text && field.Kind is FormFieldKind.Text or FormFieldKind.ComboBox)
        {
            var check = FormValueRules.CheckText(field, text);
            if (!check.Accepted)
            {
                return FormErrors.FromCheck(check);
            }
        }

        // The cache knows the current value; the field the caller holds may be older.
        var current = FieldsOn(field.Key.Page).FirstOrDefault(f => f.Key == field.Key) ?? field;
        if (current.State.Equals(state))
        {
            return new FormEditResult.Unchanged();
        }

        IReadOnlySet<int> pages;
        try
        {
            var change = await _tab.Annotations.Editor.SetFormValueAsync(field.Key.FieldName, state);
            if (change is null)
            {
                return new FormEditResult.Unchanged();
            }

            pages = change.Pages;
        }
        catch (Exception ex) when (FormErrors.FromException(ex) is not null)
        {
            if (ex is FormWriteException { Restored: false } failed)
            {
                await RefreshPagesAsync(failed.Pages);
            }

            return FormErrors.FromException(ex)!;
        }

        await RefreshPagesAsync(pages);
        bool required;
        lock (_gate)
        {
            required = _requiredRead;
        }

        if (required)
        {
            await RefreshRequiredAsync();
        }

        return new FormEditResult.Applied();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tab.Session.PagesInvalidated -= OnPagesInvalidated;
        _cts.Cancel();
    }

    private static bool IsExpected(Exception ex) =>
        ex is OperationCanceledException or ObjectDisposedException or InvalidDataException or ArgumentOutOfRangeException;

    internal Task RefreshPagesAsync(IEnumerable<int> pages)
    {
        List<Task> tasks = [];
        foreach (var page in pages)
        {
            if (IsLoaded(page) || IsInflight(page))
            {
                tasks.Add(StartPage(page, refresh: true));
            }
        }

        return Task.WhenAll(tasks);
    }

    private async Task RefreshAfterInvalidationAsync(IReadOnlySet<int> pages)
    {
        await RefreshPagesAsync(pages);
        bool required;
        lock (_gate)
        {
            required = _requiredRead;
        }

        // An undo or redo changes values without going through SetValueAsync: the required list follows them as well.
        if (required)
        {
            await RefreshRequiredAsync();
        }
    }

    /// <summary>
    /// A new document (a save was adopted) invalidates everything read from the old one; the first document seen only becomes current. Under
    /// <c>_gate</c>; every reader and writer of the cache calls it first, so a result read before the first page load is not wiped.
    /// </summary>
    private void SyncDocument(PdfDocument document)
    {
        if (ReferenceEquals(_document, document))
        {
            return;
        }

        var first = _document is null;
        _document = document;
        if (first)
        {
            return;
        }

        _pages.Clear();
        _stale.Clear();
        _info = null;
        _requiredRead = false;
        _required = [];
    }

    private bool IsInflight(int page)
    {
        lock (_gate)
        {
            return _inflight.ContainsKey(page);
        }
    }

    private Task StartPage(int page, bool refresh)
    {
        if (!HasForm || _tab.Session.Document is not { } document)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            SyncDocument(document);

            if (_inflight.TryGetValue(page, out var running))
            {
                if (refresh)
                {
                    _stale.Add(page);
                }

                return running;
            }

            if (!refresh && _pages.ContainsKey(page))
            {
                return Task.CompletedTask;
            }

            var task = Task.Run(() => ReadPageAsync(document, page));
            _inflight[page] = task;
            return task;
        }
    }

    private async Task ReadPageAsync(PdfDocument document, int page)
    {
        var reader = PageReader ?? ((doc, index, token) => doc.GetFormFieldsAsync(index, PdfPriority.Background, token));
        var done = false;
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    _stale.Remove(page);
                }

                var fields = await reader(document, page, _cts.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_stale.Contains(page))
                    {
                        continue;
                    }

                    if (ReferenceEquals(_document, document))
                    {
                        _pages[page] = fields;
                    }

                    // Leaving the single flight in the same lock as the last stale check: a refresh asked from now on starts a new read.
                    _inflight.Remove(page);
                    done = true;
                    break;
                }
            }

            RaiseChanged();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
        }
        finally
        {
            if (!done)
            {
                lock (_gate)
                {
                    _inflight.Remove(page);
                }
            }
        }
    }

    private void RaiseChanged()
    {
        if (!_disposed)
        {
            _ui.Post(static state => ((FormSession)state!).RaiseChangedOnUi(), this);
        }
    }

    private void RaiseChangedOnUi()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }

    private void OnPagesInvalidated(IReadOnlySet<int> pages)
    {
        if (!_disposed)
        {
            _ui.Post(
                static state =>
                {
                    var (session, set) = ((FormSession, IReadOnlySet<int>))state!;
                    if (!session._disposed)
                    {
                        _ = session.RefreshAfterInvalidationAsync(set);
                    }
                },
                (this, pages));
        }
    }
}
