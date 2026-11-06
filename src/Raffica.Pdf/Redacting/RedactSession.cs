using Raffica.Core.Pdf;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Text;

namespace Raffica.Pdf.Redacting;

/// <summary>
/// The redaction plan of one tab while the redact mode is on: the marks with their undo history (Core's <see cref="RedactionHistory"/>) and the
/// selection of marks. Nothing is written to the document and the tab does not become dirty; "Apply" is the window's. Made on first use by
/// <see cref="PdfTab.Redact"/>, and nothing is read before <see cref="Enter"/>. One drag or one run of nudging is one history step (the first
/// change is a <c>Do</c>, the rest amend it). The operations and <see cref="Changed"/> belong to the UI thread.
/// </summary>
internal sealed class RedactSession : IDisposable
{
    // A mark smaller than this (in points) on a side is no area.
    private const double MinSide = 1;

    private readonly CancellationTokenSource _cancel = new();
    private RedactionHistory? _history;
    private RedactTool _tool;
    private int _versionBase;
    private int _generation;
    private bool _marking;
    private bool _disposed;

    public RedactSession(PdfTab tab, SynchronizationContext ui)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(ui);
        Tab = tab;
        Ui = ui;
        Selection = new RedactSelection();
    }

    /// <summary>Raised on the UI thread after the plan, the selection or the tool changed.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread just before the mode ends, so whoever shows or reads the marks lets go first.</summary>
    public event Action? Ending;

    /// <summary>The tab the session belongs to.</summary>
    public PdfTab Tab { get; }

    /// <summary>The UI context the session reports on.</summary>
    public SynchronizationContext Ui { get; }

    /// <summary>The selected marks.</summary>
    public RedactSelection Selection { get; }

    /// <summary>The text selection of the page view that <see cref="MarkSelectionAsync"/> marks; the window gives it when the mode starts.</summary>
    public Func<SelectionController?>? SelectionSource { get; set; }

    /// <summary>The rectangle of a page in points (from the origin), so areas stay on the page; null for a page whose size is not known yet.</summary>
    public Func<int, PageRect?>? PageBounds { get; set; }

    /// <summary>Whether the mode is on.</summary>
    public bool IsActive => _history is not null;

    /// <summary>The marking tool; the area tool until the user chooses the text tool.</summary>
    public RedactTool Tool
    {
        get => _tool;
        set
        {
            if (_tool != value)
            {
                _tool = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>The plan as it is now (the empty plan while the mode is off).</summary>
    public RedactionPlan Plan => _history?.Current ?? RedactionPlan.Empty;

    public bool CanUndo => _history?.CanUndo == true;

    public bool CanRedo => _history?.CanRedo == true;

    /// <summary>Changes with every step, undo and redo, also over several <see cref="Enter"/>s, and never goes back.</summary>
    public int Version => _versionBase + (_history?.Version ?? 0);

    /// <summary>Starts an empty plan. Does nothing if one is open.</summary>
    public void Enter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_history is not null)
        {
            return;
        }

        if (Tab.Session.Document is null)
        {
            throw new InvalidOperationException("No document is open.");
        }

        _history = new RedactionHistory(RedactionPlan.Empty);
        Changed?.Invoke();
    }

    /// <summary>Drops the plan, the history and the selection.</summary>
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
        _tool = RedactTool.Area;
        Selection.Clear();
        Changed?.Invoke();
    }

    /// <summary>Adds an area mark (one history step). The area is kept on the page. The result names the new mark.</summary>
    public RedactResult AddArea(int page, PageRect area)
    {
        if (_history is null)
        {
            return RedactResult.Refused(RedactProblem.NotActive);
        }

        if (page < 0 || Fit(page, area) is not { } fitted)
        {
            return RedactResult.Refused(RedactProblem.OutOfRange);
        }

        if (Plan.Marks.Count >= RedactionLimits.MaxMarks)
        {
            return RedactResult.Refused(RedactProblem.TooManyMarks);
        }

        var mark = new RedactionMark(Guid.NewGuid(), RedactionKind.Area, page, [fitted], null, false);
        _history.Do(Plan.With(mark));
        Selection.Select(mark.Id);
        Raise();
        return RedactResult.Added(mark.Id);
    }

    /// <summary>
    /// Moves an area mark by a distance in points, kept on the page. <paramref name="continuing"/> amends the step the last change made (a drag
    /// or a run of keys) instead of making a new one.
    /// </summary>
    public RedactResult MoveArea(Guid id, double dx, double dy, bool continuing = false)
    {
        if (AreaOf(id, out var mark, out var refused) is not { } area)
        {
            return refused;
        }

        return ChangeArea(mark, new PageRect(area.Left + dx, area.Top + dy, area.Right + dx, area.Bottom + dy), continuing, keepSize: true);
    }

    /// <summary>Gives an area mark a new rectangle (a resize or a move to an absolute place), kept on the page.</summary>
    public RedactResult ResizeArea(Guid id, PageRect area, bool continuing = false)
    {
        if (AreaOf(id, out var mark, out var refused) is null)
        {
            return refused;
        }

        return ChangeArea(mark, area, continuing, keepSize: false);
    }

    /// <summary>Removes marks (one history step); ids that are not in the plan are ignored.</summary>
    public RedactResult Delete(IReadOnlySet<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (_history is null)
        {
            return RedactResult.Refused(RedactProblem.NotActive);
        }

        var next = Plan.Without(ids);
        if (ReferenceEquals(next, Plan))
        {
            // Ids the plan does not have (a stale selection): the marks are gone already.
            return RedactResult.Refused(RedactProblem.UnknownMark);
        }

        _history.Do(next);
        Raise();
        return RedactResult.Done;
    }

    public void Undo()
    {
        if (_history is { CanUndo: true })
        {
            _history.Undo();
            Raise();
        }
    }

    public void Redo()
    {
        if (_history is { CanRedo: true })
        {
            _history.Redo();
            Raise();
        }
    }

    /// <summary>Makes <paramref name="plan"/> the plan in one history step (marks found by a search, the look of the plan).</summary>
    public RedactResult Replace(RedactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_history is null)
        {
            return RedactResult.Refused(RedactProblem.NotActive);
        }

        if (!ReferenceEquals(plan, Plan))
        {
            _history.Do(plan);
            Raise();
        }

        return RedactResult.Done;
    }

    /// <summary>
    /// Adds marks found by a search in one history step and selects them. A mark that is already in the plan (same page, places and text) is
    /// not added twice. All or nothing: a plan that would overflow takes none. <paramref name="added"/> is how many marks were new.
    /// </summary>
    public RedactResult AddMarks(IReadOnlyList<RedactionMark> marks, out int added)
    {
        ArgumentNullException.ThrowIfNull(marks);
        added = 0;
        if (_history is null)
        {
            return RedactResult.Refused(RedactProblem.NotActive);
        }

        var known = new HashSet<(int, string?, string)>(Plan.Marks.Where(m => m.Kind == RedactionKind.Hit).Select(KeyOf));
        var fresh = marks.Where(m => known.Add(KeyOf(m))).ToList();
        if (fresh.Count == 0)
        {
            return RedactResult.Done;
        }

        RedactionPlan next;
        try
        {
            next = Plan.WithRange(fresh);
        }
        catch (RedactionException ex)
        {
            return RedactResult.Refused(ex.Error == RedactionError.LimitExceeded ? RedactProblem.TooManyMarks : RedactProblem.TextNotMarkable);
        }

        _history.Do(next);
        Selection.Set(fresh.Select(m => m.Id));
        Raise();
        added = fresh.Count;
        return RedactResult.Done;
    }

    private static (int, string?, string) KeyOf(RedactionMark mark) =>
        (mark.Page, mark.Term, string.Join(';', mark.Areas.Select(a => FormattableString.Invariant($"{a.Left},{a.Top},{a.Right},{a.Bottom}"))));

    /// <summary>
    /// Marks the selected text: one mark per page, with one rectangle per line, the selected text as its term. All or nothing: a stretch that
    /// cannot become a mark, or a plan that would overflow, marks nothing. The text selection is cleared once it is marked.
    /// </summary>
    public async Task<RedactResult> MarkSelectionAsync()
    {
        if (_history is null)
        {
            return RedactResult.Refused(RedactProblem.NotActive);
        }

        // One marking at a time: the same selection would otherwise be marked twice.
        if (_marking)
        {
            return RedactResult.Refused(RedactProblem.Busy);
        }

        _marking = true;
        try
        {
            return await MarkSelectionCoreAsync();
        }
        finally
        {
            _marking = false;
        }
    }

    private async Task<RedactResult> MarkSelectionCoreAsync()
    {
        if (_history is null)
        {
            return RedactResult.Refused(RedactProblem.NotActive);
        }

        var controller = SelectionSource?.Invoke();
        if (controller?.Selection is not { IsEmpty: false } selection || PageTexts.SourceOf(Tab) is not { } source)
        {
            return RedactResult.Refused(RedactProblem.NothingSelected);
        }

        var generation = _generation;
        var marks = new List<RedactionMark>();
        for (var page = selection.Start.Page; page <= selection.End.Page; page++)
        {
            PageText text;
            try
            {
                text = await source.GetAsync(page, PdfPriority.Interactive, _cancel.Token);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return RedactResult.Refused(RedactProblem.TextUnavailable);
            }

            if (_disposed || _history is null || generation != _generation)
            {
                return RedactResult.Refused(RedactProblem.NotActive);
            }

            var count = text.Chars.Count;
            var from = page == selection.Start.Page ? selection.Start.Caret : 0;
            var to = page == selection.End.Page ? selection.End.Caret : count;
            if (RedactionSearch.HitOfRange(text, page, from, to) is not { } hit)
            {
                continue;
            }

            if (!hit.Markable)
            {
                return RedactResult.Refused(hit.Areas.Count > RedactionLimits.MaxAreasPerMark ? RedactProblem.TooManyLines : RedactProblem.TextNotMarkable);
            }

            marks.Add(new RedactionMark(Guid.NewGuid(), RedactionKind.Text, page, [.. hit.Areas], hit.Text, false));
        }

        if (marks.Count == 0)
        {
            return RedactResult.Refused(RedactProblem.NothingSelected);
        }

        RedactionPlan next;
        try
        {
            next = Plan.WithRange(marks);
        }
        catch (RedactionException ex)
        {
            return RedactResult.Refused(ex.Error == RedactionError.LimitExceeded ? RedactProblem.TooManyMarks : RedactProblem.TextNotMarkable);
        }

        _history.Do(next);
        Selection.Set(marks.Select(m => m.Id));
        controller.Clear();
        Raise();
        return RedactResult.Done;
    }

    /// <summary>Sets the colour of the areas (one history step).</summary>
    public RedactResult SetFill(RedactionFill fill) => Replace(Plan.WithFill(fill));

    /// <summary>Sets what happens to the part of a picture under a mark and how coarse it is (one history step).</summary>
    public RedactResult SetImageTreatment(RedactionImageTreatment treatment, RedactionImageStrength strength)
    {
        if (!Enum.IsDefined(treatment) || !Enum.IsDefined(strength))
        {
            return RedactResult.Refused(RedactProblem.OutOfRange);
        }

        return Replace(Plan.WithImageTreatment(treatment).WithImageStrength(strength));
    }

    /// <summary>Sets how the overlay texts of all marks are drawn (repeat, size, colour); one history step, no text changes.</summary>
    public RedactResult SetOverlayStyle(RedactionOverlayStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return Replace(Plan.WithOverlayStyle(style));
    }

    /// <summary>
    /// Gives the marks with these ids the overlay text (null or white space clears it) and the plan the style, in one history step. All or
    /// nothing: a text that is too long, has a character the font cannot draw (the result names the kind, never the character) or repeats a
    /// text that is marked is refused, and the plan stays as it was.
    /// </summary>
    public RedactResult SetOverlay(IReadOnlySet<Guid> ids, string? text, RedactionOverlayStyle style)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(style);
        if (_history is null)
        {
            return RedactResult.Refused(RedactProblem.NotActive);
        }

        if (ids.Count == 0)
        {
            return RedactResult.Refused(RedactProblem.NothingSelected);
        }

        if (!Plan.Marks.Any(m => ids.Contains(m.Id)))
        {
            return RedactResult.Refused(RedactProblem.UnknownMark);
        }

        if (OverlayTextRule.IsTooLong(text))
        {
            return RedactResult.Refused(RedactProblem.OverlayTooLong);
        }

        if (OverlayTextRule.FirstRefused(text) is { } refused)
        {
            return new RedactResult(RedactProblem.OverlayCharactersRefused, null, refused);
        }

        RedactionPlan next;
        try
        {
            next = Plan.WithOverlayText(ids, text).WithOverlayStyle(style);
        }
        catch (RedactionException ex)
        {
            return RedactResult.Refused(ex.Error == RedactionError.LimitExceeded ? RedactProblem.OverlayTooLong : RedactProblem.OverlayCharactersRefused);
        }

        if (RedactionOverlayRule.Refusal(next) is not null)
        {
            return RedactResult.Refused(RedactProblem.OverlayRepeatsTerm);
        }

        return Replace(next);
    }

    /// <summary>Turns a page into an image, or back (one history step). Any page of the document may be chosen, with or without a mark.</summary>
    public RedactResult SetRasterPage(int page, bool on)
    {
        if (_history is null)
        {
            return RedactResult.Refused(RedactProblem.NotActive);
        }

        if (page < 0 || Tab.Session.Document is not { } document || page >= document.PageCount)
        {
            return RedactResult.Refused(RedactProblem.OutOfRange);
        }

        try
        {
            return Replace(Plan.WithRasterPage(page, on));
        }
        catch (RedactionException ex)
        {
            return RedactResult.Refused(ex.Error == RedactionError.LimitExceeded ? RedactProblem.TooManyRasterPages : RedactProblem.OutOfRange);
        }
    }

    /// <summary>Sets the resolution of the pages turned into images; one of <see cref="RedactAppearance.Dpis"/> (one history step).</summary>
    public RedactResult SetRasterDpi(int dpi)
    {
        if (_history is null)
        {
            return RedactResult.Refused(RedactProblem.NotActive);
        }

        return RedactAppearance.Dpis.Contains(dpi) ? Replace(Plan.WithRasterDpi(dpi)) : RedactResult.Refused(RedactProblem.DpiNotOffered);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancel.Cancel();
        _cancel.Dispose();
        _history = null;
    }

    private PageRect? AreaOf(Guid id, out RedactionMark mark, out RedactResult refused)
    {
        mark = null!;
        refused = default;
        if (_history is null)
        {
            refused = RedactResult.Refused(RedactProblem.NotActive);
            return null;
        }

        var found = Plan.Marks.FirstOrDefault(m => m.Id == id);
        if (found is null)
        {
            refused = RedactResult.Refused(RedactProblem.UnknownMark);
            return null;
        }

        if (found.Kind != RedactionKind.Area)
        {
            refused = RedactResult.Refused(RedactProblem.NotAnArea);
            return null;
        }

        mark = found;
        return found.Areas[0];
    }

    private RedactResult ChangeArea(RedactionMark mark, PageRect wanted, bool continuing, bool keepSize)
    {
        if (Fit(mark.Page, wanted, keepSize) is not { } fitted)
        {
            return RedactResult.Refused(RedactProblem.OutOfRange);
        }

        if (fitted == mark.Areas[0])
        {
            return RedactResult.Done;
        }

        var next = Plan.Replace(mark with { Areas = [fitted] });
        if (continuing)
        {
            _history!.Amend(next);
        }
        else
        {
            _history!.Do(next);
        }

        Raise();
        return RedactResult.Done;
    }

    // Puts the rectangle on the page: a moved one slides back in and keeps its size, a drawn one is clipped. Null when it has no size (a side
    // under one point, or an edge that is not a number) or lies wholly beside the page.
    private PageRect? Fit(int page, PageRect area, bool keepSize = false)
    {
        if (!double.IsFinite(area.Left) || !double.IsFinite(area.Top) || !double.IsFinite(area.Right) || !double.IsFinite(area.Bottom))
        {
            return null;
        }

        var rect = new PageRect(Math.Min(area.Left, area.Right), Math.Min(area.Top, area.Bottom), Math.Max(area.Left, area.Right), Math.Max(area.Top, area.Bottom));
        if (PageBounds?.Invoke(page) is { } bounds)
        {
            if (keepSize)
            {
                var dx = rect.Left < bounds.Left ? bounds.Left - rect.Left : rect.Right > bounds.Right ? bounds.Right - rect.Right : 0;
                var dy = rect.Top < bounds.Top ? bounds.Top - rect.Top : rect.Bottom > bounds.Bottom ? bounds.Bottom - rect.Bottom : 0;
                rect = new PageRect(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);
            }

            rect = new PageRect(Math.Max(rect.Left, bounds.Left), Math.Max(rect.Top, bounds.Top), Math.Min(rect.Right, bounds.Right), Math.Min(rect.Bottom, bounds.Bottom));
        }

        return rect.Width >= MinSide && rect.Height >= MinSide ? rect : null;
    }

    private void Raise()
    {
        Selection.Prune(Plan);
        Changed?.Invoke();
    }
}
