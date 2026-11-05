using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Documents;

namespace Raffica.Pdf.Organizing;

/// <summary>Why the session refused an operation; the plan and the history stay as they were.</summary>
internal enum OrganizeProblem
{
    /// <summary>The last page cannot be deleted.</summary>
    LastPage,

    /// <summary>The plan would hold more pages than <see cref="PageEditLimits.MaxSlots"/>.</summary>
    LimitExceeded,

    /// <summary>A position, the destination or a size is out of range.</summary>
    OutOfRange,

    /// <summary>The rotation is not a step of 90 degrees.</summary>
    InvalidRotation,

    /// <summary>The session already holds <see cref="PageEditLimits.MaxInsertedFiles"/> inserted files.</summary>
    TooManyFiles,

    /// <summary>The file is the open document itself.</summary>
    SameFile,

    /// <summary>The file cannot be read or is no PDF.</summary>
    Unreadable,

    /// <summary>The file is encrypted and no password was given.</summary>
    NeedsPassword,

    /// <summary>The password is wrong.</summary>
    WrongPassword,

    /// <summary>The page range is empty, malformed or beyond the pages of the file.</summary>
    BadRange,

    /// <summary>The session is not active (before Enter, after End, or the mode ended while a file was being read).</summary>
    NotActive,
}

/// <summary>The outcome of an operation: <see cref="Done"/>, or <see cref="Refused"/> with the reason and, where there is one, the text of the cause.</summary>
internal readonly record struct OrganizeResult(OrganizeProblem? Problem, string? Detail)
{
    public static OrganizeResult Done => default;

    public bool IsDone => Problem is null;

    public static OrganizeResult Refused(OrganizeProblem problem, string? detail = null) => new(problem, detail);
}

/// <summary>What the plan does to the document, for the Apply card and the counter of the pane.</summary>
/// <param name="PagesBefore">The pages of the open document.</param>
/// <param name="PagesAfter">The pages the plan makes.</param>
/// <param name="Turned">The pages that are turned.</param>
/// <param name="InsertedFiles">The files pages are taken from.</param>
/// <param name="BlankPages">The blank pages.</param>
/// <param name="Duplicated">The pages that are a further copy of a page of the plan.</param>
internal readonly record struct PlanSummary(int PagesBefore, int PagesAfter, int Turned, int InsertedFiles, int BlankPages, int Duplicated);

/// <summary>
/// The page plan of one tab while its pages pane is in organize mode: the plan with its undo history, the selection, the files pages were
/// inserted from. Nothing is written to the document; "Apply" is the window's. Made on first use by <see cref="PdfTab.Organize"/>, and nothing
/// is read or opened before <see cref="Enter"/>. The tab counts as dirty while a plan that is not the identity is open. The operations
/// and <see cref="Changed"/> belong to the UI thread; the files of an insertion are opened off it and awaited, one insertion at a time.
/// </summary>
internal sealed class OrganizeSession : IDisposable
{
    // The history of Core keeps this many steps; the selections that belong to the steps are kept in step with it.
    private const int MaxSteps = 100;

    private readonly PdfTab _tab;
    private readonly SynchronizationContext _ui;
    private readonly Dictionary<PageSourceId, InsertedFile> _inserted = [];
    private readonly List<int[]> _undoSelections = [];
    private readonly List<int[]> _redoSelections = [];
    private readonly SemaphoreSlim _insertGate = new(1, 1);
    private PdfEngine? _engine;
    private PageEditHistory? _history;
    private int _versionBase;
    private int _generation;
    private int _nextSource = 1;
    private bool _marked;
    private volatile bool _disposed;

    public OrganizeSession(PdfTab tab, SynchronizationContext ui)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(ui);
        _tab = tab;
        _ui = ui;
        Selection = new OrganizeSelection();
        Selection.Changed += OnSelectionChanged;
    }

    /// <summary>Raised on the UI thread after the plan, the selection or the inserted files changed (a change of the plan also sets the selection, so it raises once).</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised on the UI thread just before the inserted files are closed (the mode ends, the session is disposed), so whoever reads them
    /// (the thumbnails of the pages pane) lets go of them first.
    /// </summary>
    public event Action? Ending;

    /// <summary>A test seam: raised after a file that was read but not registered (the mode ended, the plan refused it) was closed again.</summary>
    internal Action<PdfDocument>? DocumentDiscarded { get; set; }

    public OrganizeSelection Selection { get; }

    public bool IsActive => _history is not null;

    /// <summary>The plan as it is now. Only while <see cref="IsActive"/>.</summary>
    public PageEditPlan Plan => (_history ?? throw new InvalidOperationException("The organize session is not active.")).Current;

    public bool CanUndo => _history?.CanUndo == true;

    public bool CanRedo => _history?.CanRedo == true;

    /// <summary>Changes with every step, undo and redo, also over several <see cref="Enter"/>s, and never goes back.</summary>
    public int Version => _versionBase + (_history?.Version ?? 0);

    /// <summary>The files pages were inserted from, by the number the slots name them with.</summary>
    public IReadOnlyDictionary<PageSourceId, InsertedFile> Inserted => _inserted;

    public PlanSummary Summary => Summarize(Plan);

    /// <summary>What <paramref name="plan"/> does to its document.</summary>
    public static PlanSummary Summarize(PageEditPlan plan)
    {
        var turned = 0;
        var blank = 0;
        var duplicated = 0;
        var files = new HashSet<int>();
        var seen = new HashSet<(int, int)>();
        foreach (var slot in plan.Slots)
        {
            if (slot.ExtraRotation != 0)
            {
                turned++;
            }

            if (slot.Blank is not null)
            {
                blank++;
                continue;
            }

            if (slot.Source.Value != 0)
            {
                files.Add(slot.Source.Value);
            }

            if (!seen.Add((slot.Source.Value, slot.SourceIndex)))
            {
                duplicated++;
            }
        }

        return new PlanSummary(plan.PrimaryPageCount, plan.Slots.Count, turned, files.Count, blank, duplicated);
    }

    /// <summary>Starts a plan that leaves the document as it is. Does nothing if one is open. The tab stays clean until the plan changes.</summary>
    public void Enter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_history is not null)
        {
            return;
        }

        var document = _tab.Session.Document ?? throw new InvalidOperationException("No document is open.");
        _history = new PageEditHistory(PageEditPlan.Identity(document.PageCount));
        Selection.Set(_history.Current.Slots.Count, []);
    }

    /// <summary>Drops the plan, the history and the selection, disposes the inserted files and marks the tab clean (unless its marks are unsaved).</summary>
    public void End()
    {
        if (_history is null)
        {
            return;
        }

        Ending?.Invoke();
        _generation++;
        _versionBase += _history.Version + 1;
        _history = null;
        _undoSelections.Clear();
        _redoSelections.Clear();
        DisposeInserted();
        Refresh();
        Selection.Set(0, []);
    }

    public OrganizeResult Rotate(int degrees) => Edit(plan => plan.Rotate(Selection.Positions, degrees), _ => Selection.Positions);

    /// <summary>Deletes the selected pages; nothing is selected afterwards.</summary>
    public OrganizeResult Delete() => Edit(plan => plan.Delete(Selection.Positions), _ => []);

    /// <summary>Duplicates the selected pages; the copies are selected.</summary>
    public OrganizeResult Duplicate()
    {
        var selected = Selection.Positions;
        return Edit(
            plan => plan.Duplicate(selected),
            _ =>
            {
                // The k-th selected page moved down by k, its copy follows it.
                var copies = new int[selected.Count];
                for (var k = 0; k < copies.Length; k++)
                {
                    copies[k] = selected[k] + k + 1;
                }

                return copies;
            });
    }

    /// <summary>Moves the selected pages to the gap <paramref name="destination"/> of the list as it is now; they stay selected.</summary>
    public OrganizeResult Move(int destination)
    {
        var selected = Selection.Positions;
        return Edit(
            plan => plan.Move(selected, destination),
            _ =>
            {
                var before = selected.Count(position => position < destination);
                return Enumerable.Range(destination - before, selected.Count);
            });
    }

    /// <summary>Moves the selected pages by <paramref name="delta"/> places (negative towards the first page); at the end of the list nothing happens.</summary>
    public OrganizeResult MoveBy(int delta)
    {
        if (_history is null)
        {
            return OrganizeResult.Refused(OrganizeProblem.NotActive);
        }

        var selected = Selection.Positions;
        if (selected.Count == 0 || delta == 0)
        {
            return OrganizeResult.Done;
        }

        var count = _history.Current.Slots.Count;
        if (delta < 0)
        {
            return selected[0] == 0 ? OrganizeResult.Done : Move((int)Math.Max(0L, (long)selected[0] + delta));
        }

        return selected[^1] == count - 1 ? OrganizeResult.Done : Move((int)Math.Min(count, (long)selected[^1] + 1 + delta));
    }

    /// <summary>Inserts a blank page into the gap <paramref name="at"/>; it is selected.</summary>
    public OrganizeResult InsertBlank(int at, BlankPage size) => Edit(plan => plan.InsertBlank(at, size), _ => [at]);

    /// <summary>
    /// Inserts pages of another file into the gap <paramref name="at"/>; the new pages are selected. The file is opened, counted and the range read
    /// off the UI thread; it must not be the open document, may need the password and counts against <see cref="PageEditLimits.MaxInsertedFiles"/>.
    /// A refused insertion leaves nothing behind (the file is closed again).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="password">Its password, or <see langword="null"/>.</param>
    /// <param name="range">The pages as <see cref="PageRange"/> reads them (<c>all</c> for every page).</param>
    /// <param name="at">The gap of the plan to insert at.</param>
    public async Task<OrganizeResult> InsertFileAsync(string path, string? password, string range, int at)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(range);
        if (_history is null)
        {
            return OrganizeResult.Refused(OrganizeProblem.NotActive);
        }

        // One registration at a time: the next number and the count of files are read and changed under this gate.
        await _insertGate.WaitAsync();
        try
        {
            // The mode may have ended while this call waited for the gate: no file is opened for a plan that is gone.
            if (_history is null)
            {
                return OrganizeResult.Refused(OrganizeProblem.NotActive);
            }

            return await InsertCoreAsync(path, password, range, at);
        }
        finally
        {
            _insertGate.Release();
        }
    }

    public void Undo()
    {
        if (_history is null || !_history.CanUndo)
        {
            return;
        }

        var current = Selection.Positions.ToArray();
        _history.Undo();
        _redoSelections.Add(current);
        var restored = Pop(_undoSelections);
        Refresh();
        Selection.Set(_history.Current.Slots.Count, restored);
    }

    public void Redo()
    {
        if (_history is null || !_history.CanRedo)
        {
            return;
        }

        var current = Selection.Positions.ToArray();
        _history.Redo();
        _undoSelections.Add(current);
        var restored = Pop(_redoSelections);
        Refresh();
        Selection.Set(_history.Current.Slots.Count, restored);
    }

    public void Dispose()
    {
        Ending?.Invoke();
        _disposed = true;
        _generation++;
        _history = null;
        Selection.Changed -= OnSelectionChanged;
        DisposeInserted();
    }

    private static int[] Pop(List<int[]> stack)
    {
        if (stack.Count == 0)
        {
            return [];
        }

        var last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return last;
    }

    private static OrganizeProblem ProblemOf(PageEditError error) => error switch
    {
        PageEditError.LastPage => OrganizeProblem.LastPage,
        PageEditError.LimitExceeded => OrganizeProblem.LimitExceeded,
        PageEditError.InvalidRotation => OrganizeProblem.InvalidRotation,
        _ => OrganizeProblem.OutOfRange,
    };

    private static OrganizeProblem ProblemOf(PdfOpenError error) => error switch
    {
        PdfOpenError.PasswordRequired => OrganizeProblem.NeedsPassword,
        PdfOpenError.PasswordIncorrect => OrganizeProblem.WrongPassword,
        _ => OrganizeProblem.Unreadable,
    };

    private static bool IsSameFile(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Applies one operation to the plan: a refusal of Core changes nothing; an operation that changes nothing (no page selected) is no step.
    private OrganizeResult Edit(Func<PageEditPlan, PageEditPlan> change, Func<PageEditPlan, IEnumerable<int>> select)
    {
        if (_history is null)
        {
            return OrganizeResult.Refused(OrganizeProblem.NotActive);
        }

        PageEditPlan next;
        try
        {
            next = change(_history.Current);
        }
        catch (PageEditException ex)
        {
            return OrganizeResult.Refused(ProblemOf(ex.Error), ex.Message);
        }

        if (ReferenceEquals(next, _history.Current))
        {
            return OrganizeResult.Done;
        }

        Commit(next, select(next));
        return OrganizeResult.Done;
    }

    private void Commit(PageEditPlan next, IEnumerable<int> selection)
    {
        if (_undoSelections.Count >= MaxSteps)
        {
            _undoSelections.RemoveAt(0);
        }

        _undoSelections.Add(Selection.Positions.ToArray());
        _redoSelections.Clear();
        _history!.Do(next);
        Refresh();
        Selection.Set(next.Slots.Count, selection);
    }

    private async Task<OrganizeResult> InsertCoreAsync(string path, string? password, string range, int at)
    {
        if (_inserted.Count >= PageEditLimits.MaxInsertedFiles)
        {
            return OrganizeResult.Refused(OrganizeProblem.TooManyFiles);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
            PageRange.Validate(range);
        }
        catch (FormatException ex)
        {
            return OrganizeResult.Refused(OrganizeProblem.BadRange, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OrganizeResult.Refused(OrganizeProblem.Unreadable, ex.Message);
        }

        if (_tab.Path is { } own && IsSameFile(Path.GetFullPath(own), fullPath))
        {
            return OrganizeResult.Refused(OrganizeProblem.SameFile);
        }

        var generation = _generation;
        var engine = _engine ??= new PdfEngine();
        PdfDocument? read;
        IReadOnlyList<int> pages;
        try
        {
            // The file work (the identity check, the open, the page count, the range) is not done on the UI thread.
            var openPath = _tab.Path;
            (read, pages) = await Task.Run(async () =>
            {
                // The spelling of the paths was compared above; the file system tells the rest (8.3 names, links, hard links, other drive names).
                if (openPath is not null && FileIdentity.AreSameFile(openPath, fullPath) == true)
                {
                    return ((PdfDocument?)null, (IReadOnlyList<int>)[]);
                }

                var opened = await engine.OpenAsync(fullPath, password).ConfigureAwait(false);
                try
                {
                    return ((PdfDocument?)opened, PageRange.Parse(range, opened.PageCount));
                }
                catch
                {
                    opened.Dispose();
                    throw;
                }
            });
        }
        catch (PdfOpenException ex)
        {
            return OrganizeResult.Refused(ProblemOf(ex.Error), ex.Message);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return OrganizeResult.Refused(OrganizeProblem.BadRange, ex.Message);
        }

        if (read is not { } document)
        {
            return OrganizeResult.Refused(OrganizeProblem.SameFile);
        }

        // The await above resumes on the context the call was made on; the rest changes the plan and the tab and must run on the UI context.
        if (SynchronizationContext.Current == _ui)
        {
            return Register(document, pages, generation, fullPath, password, at);
        }

        var done = new TaskCompletionSource<OrganizeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(
            _ =>
            {
                // A handler of Changed or DirtyChanged that throws must reach the caller, not the dispatcher loop: the awaiting call (and with it the
                // insert gate and the window's running flag) would otherwise never be released.
                try
                {
                    done.SetResult(Register(document, pages, generation, fullPath, password, at));
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
            },
            null);
        return await done.Task;
    }

    // Registers the opened file and makes the step. UI thread.
    private OrganizeResult Register(PdfDocument document, IReadOnlyList<int> pages, int generation, string fullPath, string? password, int at)
    {
        if (_history is null || generation != _generation)
        {
            Discard(document);
            return OrganizeResult.Refused(OrganizeProblem.NotActive);
        }

        var id = new PageSourceId(_nextSource);
        PageEditPlan next;
        try
        {
            next = _history.Current.InsertFrom(id, pages, at);
        }
        catch (PageEditException ex)
        {
            Discard(document);
            return OrganizeResult.Refused(ProblemOf(ex.Error), ex.Message);
        }

        _nextSource++;
        _inserted[id] = new InsertedFile(Path.GetFileName(fullPath), PageSourceFile.Of(fullPath, password, document), document, document.PageCount);
        Commit(next, Enumerable.Range(at, pages.Count));
        return OrganizeResult.Done;
    }

    private void Discard(PdfDocument document)
    {
        document.Dispose();
        DocumentDiscarded?.Invoke(document);
    }

    // Takes the plan over into the dirty state of the tab. The session clears only what it set itself, and not while the marks are unsaved.
    private void Refresh()
    {
        if (_history is not null && !_history.Current.IsIdentity)
        {
            _marked = true;
            _tab.MarkDirty();
        }
        else if (_marked)
        {
            _marked = false;
            if (_tab.AnnotationsIfCreated?.IsDirty != true)
            {
                _tab.MarkClean();
            }
        }
    }

    private void DisposeInserted()
    {
        foreach (var file in _inserted.Values)
        {
            file.Dispose();
        }

        _inserted.Clear();
        _nextSource = 1;
    }

    private void OnSelectionChanged() => Changed?.Invoke();
}
