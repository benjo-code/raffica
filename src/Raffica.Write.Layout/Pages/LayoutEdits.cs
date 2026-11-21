using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

/// <summary>Where a point of a page lies in the text: the position, and whether the point is on the later half of the character (or past the end of the line).</summary>
public readonly record struct TextHit(int Page, TextPosition Position, bool Trailing);

/// <summary>A caret: its page, its x on the page, and the top and height of its line (points).</summary>
public readonly record struct CaretBox(int Page, float X, float Top, float Height);

/// <summary>A rectangle of a selection on a page (points).</summary>
public readonly record struct TextBox(float X, float Y, float Width, float Height);

/// <summary>How a caret moves.</summary>
public enum CaretMove
{
    Left,
    Right,
    WordLeft,
    WordRight,
    LineStart,
    LineEnd,
    Up,
    Down,
    PageUp,
    PageDown,
    ParagraphStart,
    ParagraphEnd,
    DocumentStart,
    DocumentEnd,
}

public sealed partial class DocumentLayout
{
    private TextNavigator? _navigator;

    // Stateless over the published pages, so two threads that make it at once make equal objects.
    private TextNavigator Navigator => _navigator ??= new TextNavigator(_pages, _paragraphPages);

    /// <summary>
    /// The position under a point of a laid-out page, or null for a page that is not laid out or has no text. A point outside every
    /// line gets the nearest line; a point right of a line's text gets its end (before trailing spaces), one left of it its start; the
    /// hyphen of a hyphenated word and the label of a list paragraph are never hit; a field result, a picture or any other atomic unit
    /// snaps to its nearer edge. A point inside the height of a header, footer or note line is on that line; a point in blank space goes to the
    /// nearest body line (a page with no body line: the nearest line). Lengths are points from the top left of the page. Positions belong to the
    /// model the layout holds now: after <see cref="ReplaceModel"/> only positions of the new model's paragraphs are found.
    /// </summary>
    public TextHit? HitTest(int pageIndex, float x, float y) => Navigator.HitTest(pageIndex, x, y);

    /// <summary>
    /// The position in the header or footer of a page for a point in its region: the point is on the header side (upper half of the page) or the footer side
    /// (lower half) and at most 18 points beyond the lines of that header or footer, which the editor uses for the explicit gesture that enters a header or
    /// footer (<see cref="HitTest"/> prefers the body for blank space). Null when the page shows no header or footer there, when the point is off the page (above, below or beside it) or when it lies inside the height of a body line (the body wins over the reach of a header or footer). Lengths are points from the top left of the page.
    /// </summary>
    public TextHit? HitTestHeaderFooter(int pageIndex, float x, float y) => Navigator.HitTestHeaderFooter(pageIndex, x, y);

    /// <summary>
    /// The caret at a position, or null when no published page shows it. <paramref name="pageHint"/> is searched first (a header or footer is
    /// on many pages), then the pages that show the paragraph, from the first. A position at the end of a line that wrapped is the start
    /// of the next line (there is no caret affinity); a position past the paragraph's text is not found. A position made on a paragraph of
    /// a model the layout has replaced (<see cref="ReplaceModel"/>) is not found; re-make it from the new page.
    /// </summary>
    public CaretBox? Caret(TextPosition position, int pageHint) => Navigator.Caret(position, pageHint);

    /// <summary>
    /// The position a move leads to. Never leaves the story, and in a table never leaves the cell (so a document move in a cell stays in the
    /// cell, and Left at the start of a cell stays); a field result and every atomic unit is stepped over as one. <c>Up</c> and <c>Down</c> aim
    /// at <paramref name="goalX"/> (page x; the caret's own x when null) and go to the start or end of the line when there is no line
    /// above or below. Only published pages are known: a move to the end of the document goes to the end of what is laid out. Returns
    /// <paramref name="from"/> when the move has nowhere to go, null when no published page shows <paramref name="from"/> (also for a position
    /// of a replaced model, see <see cref="ReplaceModel"/>; the positions it returns are those of the model the layout holds).
    /// </summary>
    public TextPosition? Move(TextPosition from, CaretMove move, float? goalX, int pageHint) => Navigator.Move(from, move, goalX, pageHint);

    /// <summary>The rectangles that cover a selection on one page, one per line (the selection may begin or end on other pages); empty when the page does not show it.</summary>
    public IReadOnlyList<TextBox> SelectionBoxes(TextRange range, int pageIndex) => Navigator.SelectionBoxes(range, pageIndex);
}

/// <summary>What <see cref="DocumentLayout.ApplyEdit"/> did: the pages that were laid out again (<paramref name="FirstPage"/> to <paramref name="LastPage"/>, both inclusive; <c>LastPage</c> is <c>FirstPage - 1</c> when none was) and whether it had to start from the first page. <paramref name="ChromeOnly"/> is true when only the header and footer of the pages that show an edited part were made again (the body of every page is as it was; not a restart).</summary>
public readonly record struct EditRefill(int FirstPage, int LastPage, bool Restarted, bool ChromeOnly = false);

/// <summary>How many edits <see cref="DocumentLayout.ApplyEdit"/> has brought up to date how many of them had to start from the first page and how many only made the header and footer of the pages again (<see cref="EditRefill.ChromeOnly"/>).</summary>
public readonly record struct EditStats(int Edits, int Restarts, int ChromeOnly = 0);

public sealed partial class DocumentLayout
{
    // The look-ahead of the filler (keep with next) looks at most this many blocks on (Band.MaximumChain).
    private const int LookAhead = 8;

    private int _edits;
    private int _restarts;
    private int _chromeOnly;

    /// <summary>The edits applied so far and how many restarted from the first page (the spike and the bench read it to see how often the fast path holds).</summary>
    public EditStats Stats => new(Volatile.Read(ref _edits), Volatile.Read(ref _restarts), Volatile.Read(ref _chromeOnly));

    // The pages whose header and footer wait to be made again after an edit of a header or footer part (index = page). Under the gate. Marks are cleared for every page a
    // refill makes again (its chrome is fresh) or cuts off, and for all pages by a restart or a replaced model.
    private bool[] _chromeStale = [];
    private int _staleCount;
    private int _staleCursor;
    private int _chromeThrough;

    /// <summary>The pages around an edit of a header or footer that are made again at once: the page the edit is on (the through page) and this many pages on each side.</summary>
    internal const int ChromeWindow = 8;

    /// <summary>The pages the background pass makes again in one batch, at most (and no more while a batch takes longer than <see cref="ChromeBatchMilliseconds"/>).</summary>
    private const int ChromeBatchPages = 16;

    internal const int ChromeBatchMilliseconds = 2;

    /// <summary>
    /// True while pages are waiting for their header and footer to be made again after an edit of a header or footer part: the pages around the edit are right at once, the others are
    /// old pages (consistent, with the old header or footer) until <see cref="LayOutRestAsync"/> has made them again in the background, in batches, in page order.
    /// </summary>
    public bool ChromeRefreshPending => Volatile.Read(ref _staleCount) > 0;

    /// <summary>How many pages wait for their header and footer (a test seam).</summary>
    internal int StaleChromePages => Volatile.Read(ref _staleCount);

    /// <summary>True when the layout is complete and no header or footer refresh is pending: nothing more will be laid out or made again.</summary>
    public bool IsSettled => IsComplete && !ChromeRefreshPending;

    /// <summary>
    /// Makes the header and footer of the waiting pages again, in page order, as one batch under the gate: at least one page, and more while <paramref name="budgetTicks"/> stopwatch ticks last (at most 16 pages).
    /// True when it published pages (or started over, see below). A height that differs from the one a page was made with means the body moves: the pass then starts over from the first
    /// page (a restart, always right) and the pages up to the through page of the last edit are made again. Called by <see cref="LayOutRestAsync"/>; a test seam otherwise.
    /// </summary>
    internal bool RefreshChromeBatch(long budgetTicks)
    {
        lock (_gate)
        {
            _fault?.Throw();
            if (_staleCount == 0)
            {
                return false;
            }

            try
            {
                var engine = _engine;
                var count = _pages.Count;
                if (engine.Chrome is not HeaderFooterPlan chrome)
                {
                    ClearStale(0, int.MaxValue);
                    return false;
                }

                var started = Stopwatch.GetTimestamp();
                var indices = new List<int>();
                var rebuilt = new List<(LaidOutPage Old, LaidOutPage New)>();
                var at = Math.Min(_staleCursor, _chromeStale.Length);
                for (; at < _chromeStale.Length && indices.Count < ChromeBatchPages; at++)
                {
                    if (!_chromeStale[at])
                    {
                        continue;
                    }

                    if (at >= count)
                    {
                        // The page was cut off since: it is laid out again with the new chrome.
                        _chromeStale[at] = false;
                        _staleCount--;
                        continue;
                    }

                    var one = RefreshPages([at], chrome, engine);
                    if (one is null)
                    {
                        // A height or a part differs: the body of the pages from here on may move. Start over (every page is made again, the stale marks go).
                        Volatile.Write(ref _restarts, _restarts + 1);
                        Restart(_chromeThrough);
                        return true;
                    }

                    indices.Add(at);
                    rebuilt.AddRange(one);
                    if (Stopwatch.GetTimestamp() - started >= budgetTicks)
                    {
                        at++;
                        break;
                    }
                }

                _staleCursor = at >= _chromeStale.Length ? 0 : at;
                if (indices.Count == 0)
                {
                    return false;
                }

                foreach (var index in indices)
                {
                    _chromeStale[index] = false;
                }

                Volatile.Write(ref _staleCount, _staleCount - indices.Count);
                Publish(rebuilt);
                return true;
            }
            catch (Exception e)
            {
                _fault = ExceptionDispatchInfo.Capture(e);
                throw;
            }
        }
    }

    /// <summary>How many pages the last <see cref="ApplyEdit"/> laid out (a test seam; the spike measured how often a refill is longer than one page).</summary>
    internal int PagesLaidOut { get; private set; }

    /// <summary>The page start the last <see cref="ApplyEdit"/> resumed from (null when it started over); a test seam.</summary>
    internal FillerState? LastResume { get; private set; }

    /// <summary>
    /// A finished page, or false when the layout has no page with this index now. The number of pages can fall after an edit (the pages that
    /// follow the edit are cut off until they are laid out again, or the document got shorter), so a reader that read
    /// <see cref="LaidOutPages"/> and then asks for a page below it may find none: <see cref="Page"/> throws then, this does not.
    /// </summary>
    public bool TryGetPage(int index, out LaidOutPage? page)
    {
        var found = _pages.TryGet(index, out var result);
        page = found ? result : null;
        return found;
    }

    /// <summary>
    /// The host the layout used when it made the projection of <paramref name="paragraph"/> (field values, note marks, hidden runs): the
    /// editor passes it to the text editor so that an edit sees the same text, in the same offsets, as the pages show. Never numbers a note
    /// again. A paragraph no published page shows has the plain host (no field values, no note marks, nothing hidden).
    /// </summary>
    public IProjectionHost ProjectionHost(StoryRef story, Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (_paragraphPages.PagesOf(paragraph) is { } list)
        {
            foreach (var index in list)
            {
                if (!_pages.TryGet(index, out var page))
                {
                    continue;
                }

                foreach (var line in page.TextLines)
                {
                    if (ReferenceEquals(line.Paragraph, paragraph) && line.Story == story && line.Host is { } host)
                    {
                        return host is LayoutProjectionHost layout ? new ReplayProjectionHost(layout) : host;
                    }
                }
            }
        }

        return Model.ProjectionHost.Plain;
    }

    /// <summary>
    /// Brings the pages up to date after an edit of the model: <paramref name="change"/> says which blocks of which list were replaced,
    /// and the model already holds the result. Lays out again from the page that starts at or before the edit (walking back over the
    /// blocks a keep-with-next could have moved) and stops as soon as a page starts where one started before with the same planner state
    /// (the same block and line, the same numbering and note numbers, the same page counters): the old pages from there on are put back, with
    /// their block indexes moved when blocks were inserted or removed. When that does not happen before page <paramref name="throughPage"/>
    /// the pages after it are cut off and the next calls of <see cref="LayOutThrough"/> or <see cref="LayOutRestAsync"/> lay them out
    /// again. Pages from the first changed one to <paramref name="throughPage"/> (or the stop) are right when this returns.
    /// </summary>
    /// <remarks>
    /// Synchronous; the caller holds the edit gate (no layout runs on the model while it changes, and the background layout has stopped or
    /// waits for the gate this takes). What starts over from the first page (<see cref="EditRefill.Restarted"/>), always correct and slower: an edit of
    /// a header or footer part that a section shows (the engine and its plans are kept, the part's laid-out blocks are dropped), an edit of a block that
    /// ends a section in the page view (one with a section break), a change of the section structure, a note nothing shows yet, and anything the engine
    /// cannot place (a list it does not know, a block it cannot find, no recorded page start to resume from). An edit of a note is laid out again from the
    /// page its reference is on (back to a page that no notes run over onto) and may stop after the last page that showed it; an edit of a comment, of a
    /// part no section shows and of any non-body story in the writing view changes nothing and returns an empty refill. The pages up to
    /// <paramref name="throughPage"/> are made at once and published together, the rest follows in the background. The result is equal to a layout of the
    /// same model made from scratch once the rest has been laid out (the tests compare page count, text, numbers, items, spans, lines, glyph runs and the block tables).
    /// <para>
    /// Readers: the pages are published in one step (see <c>PageList</c>), <see cref="Revision"/> rises (a view re-reads <c>Page(i)</c>),
    /// <see cref="LaidOutPages"/> may be smaller afterwards, <see cref="Locate"/> and <see cref="BlockAt"/> answer for the new model from
    /// the moment the new pages are there (for a short moment between the two writes they may mix old and new). Positions made before the
    /// edit stay valid for every paragraph the edit did not replace. One layout, one model.
    /// </para>
    /// </remarks>
    public EditRefill ApplyEdit(BlockChange change, int throughPage)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            _fault?.Throw();
            try
            {
                var refill = Refill(change, Math.Max(0, throughPage));
                Volatile.Write(ref _edits, _edits + 1);
                if (refill.Restarted)
                {
                    Volatile.Write(ref _restarts, _restarts + 1);
                }

                if (refill.ChromeOnly)
                {
                    Volatile.Write(ref _chromeOnly, _chromeOnly + 1);
                }

                return refill;
            }
            catch (Exception e)
            {
                _fault = ExceptionDispatchInfo.Capture(e);
                throw;
            }
        }
    }

    /// <summary>
    /// Lays out again from the first page on a fresh engine after the style sheet of the model changed (the engine reads the styles once when it is
    /// made, so no <see cref="ApplyEdit"/> can see such a change). Same rules as <see cref="ApplyEdit"/>: the caller holds the edit gate, the pages up
    /// to <paramref name="throughPage"/> are right when this returns, the rest follows in the background, a fault is remembered and rethrown.
    /// </summary>
    public EditRefill Restyle(int throughPage)
    {
        lock (_gate)
        {
            _fault?.Throw();
            try
            {
                var refill = Restart(Math.Max(0, throughPage));
                Volatile.Write(ref _edits, _edits + 1);
                Volatile.Write(ref _restarts, _restarts + 1);
                return refill;
            }
            catch (Exception e)
            {
                _fault = ExceptionDispatchInfo.Capture(e);
                throw;
            }
        }
    }

    private EditRefill Refill(BlockChange change, int throughPage)
    {
        var old = _engine;

        // The engine's numbering counters read the part that existed when it was made: a part a list command made since needs an engine of its own.
        if (!ReferenceEquals(old.Numbering, old.Document.Numbering))
        {
            return Restart(throughPage);
        }
        var oldCount = _pages.Count;
        EditPlan? chosen;
        if (change.Story.Kind == StoryKind.Body)
        {
            chosen = PlanEdit(change, old, oldCount, _options);
        }
        else if (TryRefreshChrome(change, old, oldCount, throughPage) is { } refreshed)
        {
            return refreshed;
        }
        else
        {
            switch (PlanStoryEdit(change, old, oldCount, _options, out chosen))
            {
                case StoryEffect.None:
                    return new EditRefill(0, -1, false);
                case StoryEffect.Restart:
                    return Restart(throughPage);
            }
        }

        if (chosen is not { } plan)
        {
            return Restart(throughPage);
        }

        var planner = old.Planner;
        for (var i = plan.FirstFlat; i < plan.EndFlat; i++)
        {
            planner.Forget(plan.Flat[i].Block);
        }

        var shift = plan.Nested ? 0 : plan.Added - plan.Removed;
        if (shift != 0)
        {
            planner.RebaseAll(plan.Flat);
        }

        if (planner.CachedPlans > plan.Flat.Count + (plan.Flat.Count >> 1) + 256)
        {
            planner.Retain(plan.Flat);
        }

        var resume = plan.Resume;
        LastResume = resume;
        var first = resume.PageIndex;
        var workPage = (int[])plan.ShiftedPage.Clone();
        var workY = (float[])plan.ShiftedY.Clone();
        ResetFrom(workPage, workY, plan.ResetFromBody);

        var flowMode = _options.Mode == LayoutMode.Flow;
        var filler = new PageFiller(plan.Sections, plan.Flat, planner, flowMode, workPage, workY)
        {
            Chrome = old.Chrome,
            PageFinished = _pageFinished,
            Records = [.. old.Filler.Records.Take(first)],
        };
        filler.RevisableList.AddRange(old.Filler.RevisableList.Where(r => r.Published.Index < first));
        var oldRecords = old.Filler.Records;
        var oldComplete = IsComplete;

        // The old pages after the stop must be there to be put back, and an unfinished layout must have the state of its next page to go on from.
        var canStop = oldComplete || (oldRecords.Count > oldCount && oldRecords[oldCount] is not null);
        filler.StopProbe = state => canStop && state.PageIndex > plan.StopAfterPage && state.PageIndex <= oldCount && oldRecords.Count > state.PageIndex && IsOldStart(filler, plan, state, oldRecords[state.PageIndex]);

        var fresh = new List<LaidOutPage>();
        var fill = filler.FillFrom(resume).GetEnumerator();
        var paused = false;
        var firstTouched = old.FirstTouched;
        try
        {
            while (fill.MoveNext())
            {
                fresh.Add(fill.Current);
                if (first == 0 && fresh.Count == 1)
                {
                    firstTouched = filler.MaxTouchedBodyIndex;
                }

                if (fill.Current.Index >= throughPage && !filler.StopMatched)
                {
                    paused = true;
                    break;
                }
            }
        }
        finally
        {
            filler.StopProbe = null;
        }

        PagesLaidOut = fresh.Count;
        var engine = new Engine
        {
            Filler = filler,
            Fill = fill,
            BlockPage = workPage,
            BlockY = workY,
            LastBodyIndex = plan.BodyCount - 1,
            Chrome = old.Chrome,
            Document = old.Document,
            Numbering = old.Numbering,
            Planner = planner,
            Sections = plan.Sections,
            FinalPages = old.FinalPages,
            FinalSectionPages = old.FinalSectionPages,
            FirstTouched = firstTouched,
        };

        int keepFrom;
        Func<LaidOutPage, LaidOutPage>? adjust = null;
        var complete = false;
        List<(int Index, LaidOutPage Page)> prefixRevised = [];
        if (filler.StopMatched)
        {
            var stop = filler.StopIndex;
            keepFrom = stop;
            if (shift != 0)
            {
                var from = plan.Lo + plan.Removed;
                adjust = page => ShiftSpans(page, from, shift);
            }

            // Everything the old run recorded and laid out from the stop on is as it was; the pages with a page count go with it.
            for (var k = stop + 1; k < oldRecords.Count; k++)
            {
                filler.Records.Add(oldRecords[k]);
            }

            foreach (var entry in old.Filler.RevisableList)
            {
                if (entry.Published.Index >= stop)
                {
                    filler.RevisableList.Add(shift == 0 ? entry : entry with { Published = ShiftSpans(entry.Published, plan.Lo + plan.Removed, shift) });
                }
            }

            RestoreTail(workPage, workY, plan.ShiftedPage, plan.ShiftedY, plan.RestoreFromBody(filler.Records[stop]!, filler));
            complete = oldComplete;
            if (oldComplete)
            {
                foreach (var (index, revised) in filler.Revisions(first, stop, old.FinalPages, old.FinalSectionPages))
                {
                    fresh[index - first] = revised;
                }
            }
            else
            {
                engine.Fill = filler.FillFrom(filler.Records[oldCount]).GetEnumerator();
            }
        }
        else if (paused)
        {
            keepFrom = int.MaxValue;
        }
        else
        {
            keepFrom = int.MaxValue;
            complete = true;
            (engine.FinalPages, engine.FinalSectionPages) = filler.Totals();
            foreach (var (index, revised) in filler.Revisions())
            {
                if (index >= first)
                {
                    fresh[index - first] = revised;
                }
                else
                {
                    prefixRevised.Add((index, revised));
                }
            }
        }

        var replaced = new List<LaidOutPage>();
        for (var k = first; k < Math.Min(oldCount, keepFrom == int.MaxValue ? first + fresh.Count : keepFrom); k++)
        {
            replaced.Add(_pages[k]);
        }

        // Publication. The map first (a reader that finds an entry for a page it is not shown yet skips it by the count), then the tables
        // the new pages belong to, then the pages in one step; the completeness flag only ever turns false before the pages change and true after.
        foreach (var page in fresh)
        {
            _paragraphPages.Register(page);
        }

        foreach (var (_, revised) in prefixRevised)
        {
            _paragraphPages.Register(revised);
        }

        if (!complete)
        {
            Volatile.Write(ref _complete, false);
        }

        Volatile.Write(ref _engine, engine);
        Volatile.Write(ref _firstPageFinal, firstTouched < engine.LastBodyIndex);
        _pages.Splice(first, fresh, keepFrom, adjust);
        ClearStale(first, keepFrom == int.MaxValue ? int.MaxValue : first + fresh.Count);
        foreach (var (index, revised) in prefixRevised)
        {
            _pages.Replace(index, revised);
        }

        for (var k = 0; k < replaced.Count; k++)
        {
            _paragraphPages.Unregister(replaced[k], k < fresh.Count ? fresh[k] : null);
        }

        old.Fill.Dispose();
        Volatile.Write(ref _revision, _revision + 1);
        if (complete)
        {
            Volatile.Write(ref _complete, true);
        }

        return new EditRefill(first, first + fresh.Count - 1, plan.StartsOver);
    }

    // An edit of a header or footer part whose laid-out block keeps its height on every page that shows it changes no body item: the body of every page is as it was, only
    // the header and footer items, text lines and text of those pages differ, and the engine, its plans and its records stay as they are. The pages around the edit (the through
    // page, ChromeWindow pages on each side) are made again at once and published; the other pages that show the part are marked stale and made again by RefreshChromeBatch (the
    // background pass of LayOutRestAsync) in page order. A stale page is an old, immutable, consistent page until its batch is published; no reader ever waits for or runs chrome code.
    // Every page refresh reads both parts of the page from the model as it is now, so a later edit simply marks its pages again and a page stale for an earlier edit is right after
    // one refresh. Pages that are not laid out yet get the new part from the invalidated chrome. Returns null when this cannot be shown (the caller then starts over, which is always
    // right): the part is not shown by any section, a page does not know its chrome, or a refreshed page's header or footer height or parts differ from the ones it was made with (the
    // body moves, the first or even page variants included, because every page is compared with its own), or the part gained or lost a page count field (the filler would not know to
    // revise the page). A part that shows a page count keeps the old way: every page is made again at once (against the real total when the layout is complete).
    private EditRefill? TryRefreshChrome(BlockChange change, Engine old, int oldCount, int throughPage)
    {
        if (change.Story.Kind != StoryKind.HeaderFooter || old.Chrome is not HeaderFooterPlan chrome || change.Story.Key is not { } key
            || oldCount == 0 || !chrome.Shows(key))
        {
            return null;
        }

        chrome.Invalidate(change.Story);
        var showing = new List<int>();
        var usesTotals = false;
        for (var i = 0; i < oldCount; i++)
        {
            if (_pages[i].Chrome is not { } info)
            {
                return null;
            }

            if (string.Equals(info.HeaderPart, key, StringComparison.Ordinal) || string.Equals(info.FooterPart, key, StringComparison.Ordinal))
            {
                showing.Add(i);
                usesTotals |= info.UsesTotals;
            }
        }

        var through = Math.Clamp(throughPage, 0, oldCount - 1);
        var now = usesTotals ? showing : showing.Where(i => Math.Abs(i - through) <= ChromeWindow).ToList();
        var rebuilt = RefreshPages(now, chrome, old);
        if (rebuilt is null)
        {
            return null;
        }

        // The window is right at once: the others that show the part wait for the pass (nothing waits for a part with a page count).
        var window = new HashSet<int>(now);
        EnsureStaleCapacity(oldCount);
        foreach (var i in showing)
        {
            if (window.Contains(i))
            {
                if (_chromeStale[i])
                {
                    _chromeStale[i] = false;
                    _staleCount--;
                }
            }
            else if (!_chromeStale[i])
            {
                _chromeStale[i] = true;
                _staleCount++;
            }
        }

        _staleCursor = 0;
        _chromeThrough = through;
        Publish(rebuilt);
        return rebuilt.Count == 0
            ? new EditRefill(0, -1, false, true)
            : new EditRefill(rebuilt[0].New.Index, rebuilt[^1].New.Index, false, true);
    }

    // The pages of the given indexes with their header and footer made again from the model as it is now, or null when one of them cannot be (a height, a part or the use of a page
    // count differs from what the page was made with).
    private List<(LaidOutPage Old, LaidOutPage New)>? RefreshPages(List<int> indices, HeaderFooterPlan chrome, Engine engine)
    {
        var complete = IsComplete;
        var rebuilt = new List<(LaidOutPage Old, LaidOutPage New)>(indices.Count);
        foreach (var i in indices)
        {
            var page = _pages[i];
            if (page.Chrome is not { } info)
            {
                return null;
            }

            var scratch = new PageBuilder(i, info.Number, page.Number, info.Section, info.IsFirstOfSection);
            chrome.Refresh(scratch, complete ? engine.FinalPages : null, complete ? engine.FinalSectionPages : null);
            if (scratch.HeaderHeight != info.HeaderHeight || scratch.FooterHeight != info.FooterHeight
                || !string.Equals(scratch.HeaderPart, info.HeaderPart, StringComparison.Ordinal) || !string.Equals(scratch.FooterPart, info.FooterPart, StringComparison.Ordinal)
                || scratch.UsesTotals != info.UsesTotals)
            {
                // Also when the part gained or lost a page count: the filler revises only the pages that showed one when they were made (its list of revisable pages), so such a
                // page would keep a pending or a stale total; the restart builds that list again.
                return null;
            }

            rebuilt.Add((page, info.Rebuild(page, scratch, scratch.NeedsTotals)));
        }

        return rebuilt;
    }

    // Publication as for any refill: the map first, then the pages (one volatile write each, no copy of the list), then the stale entries of the old pages.
    private void Publish(List<(LaidOutPage Old, LaidOutPage New)> rebuilt)
    {
        foreach (var (_, page) in rebuilt)
        {
            _paragraphPages.Register(page);
        }

        foreach (var (_, page) in rebuilt)
        {
            _pages.Replace(page.Index, page);
        }

        foreach (var (previous, page) in rebuilt)
        {
            _paragraphPages.Unregister(previous, page);
        }

        PagesLaidOut = rebuilt.Count;
        Volatile.Write(ref _revision, _revision + 1);
    }

    private void EnsureStaleCapacity(int pages)
    {
        if (_chromeStale.Length < pages)
        {
            Array.Resize(ref _chromeStale, Math.Max(pages, _chromeStale.Length * 2));
        }
    }

    // Forgets the marks of the pages [from, to): their chrome is fresh (a refill made them again), they are gone, or everything was made again.
    private void ClearStale(int from, int to)
    {
        if (_staleCount == 0)
        {
            return;
        }

        for (var i = Math.Max(0, from); i < Math.Min(to, _chromeStale.Length); i++)
        {
            if (_chromeStale[i])
            {
                _chromeStale[i] = false;
                _staleCount--;
            }
        }

        Volatile.Write(ref _staleCount, _staleCount);
    }

    // Starts again from the first page on a fresh engine over the model as it is now: everything is planned again. The pages up to throughPage
    // are laid out before anything is published, so the old pages stay in place until the new ones can take over.
    private EditRefill Restart(int throughPage)
    {
        LastResume = null;
        var old = _engine;
        var oldCount = _pages.Count;
        var notes = new List<string>();
        var engine = Engine.Build(old.Document, _fonts, _options, notes, _gate, _preedit);
        engine.Filler.PageFinished = _pageFinished;
        var fresh = new List<LaidOutPage>();
        var paused = false;
        while (engine.Fill.MoveNext())
        {
            fresh.Add(engine.Fill.Current);
            if (fresh.Count == 1)
            {
                engine.FirstTouched = engine.Filler.MaxTouchedBodyIndex;
            }

            if (engine.Fill.Current.Index >= throughPage)
            {
                paused = true;
                break;
            }
        }

        PagesLaidOut = fresh.Count;
        if (!paused)
        {
            (engine.FinalPages, engine.FinalSectionPages) = engine.Filler.Totals();
            foreach (var (index, revised) in engine.Filler.Revisions())
            {
                fresh[index] = revised;
            }
        }

        var replaced = new List<LaidOutPage>();
        for (var k = 0; k < Math.Min(oldCount, fresh.Count); k++)
        {
            replaced.Add(_pages[k]);
        }

        foreach (var page in fresh)
        {
            _paragraphPages.Register(page);
        }

        Volatile.Write(ref _complete, false);
        Volatile.Write(ref _notes, notes);
        Volatile.Write(ref _engine, engine);
        Volatile.Write(ref _firstPageFinal, engine.FirstTouched < engine.LastBodyIndex);
        _pages.Splice(0, fresh, int.MaxValue);
        ClearStale(0, int.MaxValue);
        for (var k = 0; k < replaced.Count; k++)
        {
            _paragraphPages.Unregister(replaced[k], fresh[k]);
        }

        old.Fill.Dispose();
        Volatile.Write(ref _revision, _revision + 1);
        if (!paused)
        {
            Volatile.Write(ref _complete, true);
        }

        return new EditRefill(0, fresh.Count - 1, true);
    }

    // The new run's page start is the old run's start of the same page index: the same block and line, the same planner state and counters,
    // and a block that the edit did not touch (a start at or inside the edited blocks proves nothing, they have new content).
    private static bool IsOldStart(PageFiller filler, EditPlan plan, FillerState state, FillerState? old)
    {
        if (old is null || !state.SameAs(old))
        {
            return false;
        }

        var start = state.Start.Block is { } block ? filler.FlatIndexOf(block) : plan.Sections[state.Start.SectionIndex].FirstBlock;
        return start > plan.EndFlat - 1;
    }

    private static void ResetFrom(int[] pages, float[] ys, int fromBody)
    {
        if (fromBody < pages.Length)
        {
            Array.Fill(pages, -1, fromBody, pages.Length - fromBody);
            Array.Clear(ys, fromBody, ys.Length - fromBody);
        }
    }

    private static void RestoreTail(int[] pages, float[] ys, int[] oldPages, float[] oldYs, int fromBody)
    {
        if (fromBody < pages.Length)
        {
            Array.Copy(oldPages, fromBody, pages, fromBody, pages.Length - fromBody);
            Array.Copy(oldYs, fromBody, ys, fromBody, ys.Length - fromBody);
        }
    }

    // The same page with the block indexes of its spans moved (blocks were inserted or removed before them); items and text are shared.
    private static LaidOutPage ShiftSpans(LaidOutPage page, int from, int by)
    {
        var spans = new BlockSpan[page.Spans.Length];
        for (var i = 0; i < spans.Length; i++)
        {
            var span = page.Spans[i];
            spans[i] = span.BodyIndex >= from ? span with { BodyIndex = span.BodyIndex + by } : span;
        }

        return new LaidOutPage(page.Index, page.Number, page.Width, page.Height, page.IsStrip, page.ItemArray, page.Text, spans, page.IsProvisional, page.TextLines, page.Chrome);
    }

    /// <summary>What an edit of the body changes in the tables the filler works on, and where the refill starts.</summary>
    private sealed class EditPlan
    {
        public required int Lo { get; init; }

        public required int Added { get; init; }

        public required int Removed { get; init; }

        public required bool Nested { get; init; }

        public required int BodyCount { get; init; }

        public required List<FlatBlock> Flat { get; init; }

        public required List<SectionPlan> Sections { get; init; }

        /// <summary>The flat blocks <c>[FirstFlat, EndFlat)</c> are the ones the edit made new (their plans are forgotten).</summary>
        public required int FirstFlat { get; init; }

        public required int EndFlat { get; init; }

        public required FillerState Resume { get; init; }

        public required int ResetFromBody { get; init; }

        /// <summary>No state of a page up to this index may end the refill (the pages that show an edited header or note must all be laid out again); -1 for an edit of the body.</summary>
        public int StopAfterPage { get; init; } = -1;

        /// <summary>The refill goes back to the first page (a header or footer changed): it is a restart for <see cref="EditRefill.Restarted"/>, although the engine and its plans are kept.</summary>
        public bool StartsOver { get; init; }

        public required int[] ShiftedPage { get; init; }

        public required float[] ShiftedY { get; init; }

        /// <summary>The first body index whose entry in the block tables is the old one again at a stop: the blocks laid out by the refill end before it.</summary>
        public int RestoreFromBody(FillerState stop, PageFiller filler)
        {
            var startFlat = stop.Start.Block is { } block ? filler.FlatIndexOf(block) : Sections[stop.Start.SectionIndex].FirstBlock;
            return BodyIndexAfterPlaced(Flat, stop, startFlat);
        }

        /// <summary>
        /// The first body index whose entry in the block tables belongs to the blocks from a page start on: the blocks before the first flat block that
        /// has not been placed when the page begins (a carried piece that is not the start of its block belongs to a page before).
        /// </summary>
        public static int BodyIndexAfterPlaced(List<FlatBlock> flat, FillerState state, int startFlat)
        {
            var firstUnplaced = state.Kind == PageStartKind.Carry && state.Carry is { IsFirstPiece: false } ? startFlat + 1 : startFlat;
            return firstUnplaced > 0 && firstUnplaced - 1 < flat.Count ? flat[firstUnplaced - 1].BodyIndex + 1 : 0;
        }
    }

    private static EditPlan? PlanEdit(BlockChange change, Engine old, int oldCount, LayoutOptions options)
    {
        var document = old.Document;
        var records = old.Filler.Records;
        if (oldCount == 0 || records.Count == 0 || change.Story.Kind != StoryKind.Body)
        {
            return null;
        }

        var body = document.Body.Blocks;
        int lo;
        int added;
        int removed;
        bool nested;
        if (ReferenceEquals(change.Container, body))
        {
            lo = change.First;
            added = change.Added;
            removed = change.Removed;
            nested = false;
        }
        else
        {
            lo = OwnerOf(body, change.Container);
            added = 1;
            removed = 1;
            nested = true;
        }

        var oldBodyCount = old.BlockPage.Length;
        if (lo < 0 || lo > oldBodyCount || body.Count != oldBodyCount - removed + added || lo + added > body.Count || (options.Mode != LayoutMode.Flow && HasSectionBreak(change)))
        {
            return null;
        }

        var flat = FlatBlocks.From(body);
        List<SectionPlan> sections;
        if (options.Mode == LayoutMode.Flow)
        {
            sections = [SectionPlan.ForStrips(FlowSegmenter.Geometry(options), flat.Count)];
        }
        else
        {
            sections = SectionPlan.Build(flat, document.Body.FinalSection, []);
            if (sections.Count != old.Sections.Count)
            {
                return null;
            }

            for (var i = 0; i < sections.Count; i++)
            {
                if (!ReferenceEquals(sections[i].Properties, old.Sections[i].Properties))
                {
                    return null;
                }
            }
        }

        var firstFlat = LowerBound(flat, lo);
        var endFlat = LowerBound(flat, nested ? lo + 1 : lo + added);

        // The page the refill starts at: the first page of the block a look-ahead could have reached the edit from, or an earlier page that can be resumed.
        // A block decides where it goes with a look at the blocks after it only when it keeps with the next one, so the first page to lay out again is the
        // page of the first block of the run of such blocks that ends before the edit (a block with no plan on record counts as one that does).
        var target = firstFlat;
        while (target > 0 && firstFlat - target < LookAhead && old.Planner.MayKeepWithNext(flat[target - 1].Block))
        {
            target--;
        }

        var bodyIndex = flat.Count == 0 ? -1 : flat[Math.Min(target, flat.Count - 1)].BodyIndex;
        bodyIndex = Math.Min(bodyIndex, Math.Min(lo, oldBodyCount) - 1);
        while (bodyIndex >= 0 && old.BlockPage[bodyIndex] < 0)
        {
            bodyIndex--;
        }

        var page = bodyIndex >= 0 ? old.BlockPage[bodyIndex] : 0;
        page = Math.Min(page, Math.Min(oldCount, records.Count) - 1);
        if (FindResume(records, page, flat, sections, target, requireNoCarry: false, out var resume, out var startFlat) is false)
        {
            return null;
        }

        var resetFrom = EditPlan.BodyIndexAfterPlaced(flat, resume!, startFlat);
        var pages = new int[body.Count];
        var ys = new float[body.Count];
        if (nested)
        {
            Array.Copy(old.BlockPage, pages, body.Count);
            Array.Copy(old.BlockY, ys, body.Count);
        }
        else
        {
            Array.Copy(old.BlockPage, 0, pages, 0, lo);
            Array.Copy(old.BlockY, 0, ys, 0, lo);
            Array.Fill(pages, -1, lo, added);
            Array.Copy(old.BlockPage, lo + removed, pages, lo + added, oldBodyCount - lo - removed);
            Array.Copy(old.BlockY, lo + removed, ys, lo + added, oldBodyCount - lo - removed);
        }

        return new EditPlan
        {
            Lo = lo,
            Added = added,
            Removed = removed,
            Nested = nested,
            BodyCount = body.Count,
            Flat = flat,
            Sections = sections,
            FirstFlat = firstFlat,
            EndFlat = endFlat,
            Resume = resume!,
            ResetFromBody = resetFrom,
            ShiftedPage = pages,
            ShiftedY = ys,
        };
    }

    // The latest page at or before startPage that a refill can begin at: a page whose start was recorded (and, when requireNoCarry, that has no notes
    // running over onto it: those hold the laid-out flows of notes that may have changed). A Carry start must carry an untouched block before
    // limit (the run of keep-with-next blocks that ends at the edit is laid out again: a page that starts with a member of the run was started by the
    // decision to move the run on, which the edit may reverse), a Section start a section that has blocks.
    private static bool FindResume(List<FillerState?> records, int startPage, List<FlatBlock> flat, List<SectionPlan> sections, int limit, bool requireNoCarry, out FillerState? resume, out int startFlat)
    {
        resume = null;
        startFlat = 0;
        for (var page = startPage; page >= 0; page--)
        {
            if (records[page] is not { } record || (requireNoCarry && record.ChromeCarry is not null))
            {
                continue;
            }

            if (record.Kind == PageStartKind.Carry)
            {
                var at = IndexOf(flat, record.Start.Block!, limit);
                if (at < 0 || record.Start.SectionIndex >= sections.Count)
                {
                    continue;
                }

                resume = record;
                startFlat = at;
                return true;
            }

            if (record.Start.SectionIndex >= sections.Count || sections[record.Start.SectionIndex].IsEmpty)
            {
                continue;
            }

            resume = record;
            startFlat = sections[record.Start.SectionIndex].FirstBlock;
            return true;
        }

        return false;
    }

    private enum StoryEffect
    {
        /// <summary>The edit changes nothing that is laid out (a comment, a header in the writing view, a part no section shows).</summary>
        None,
        Restart,
        Refill,
    }

    // An edit of a story other than the body. The writing view shows no header, footer, note or comment; a comment is never laid out; a header or footer
    // part makes the layout start over from page 1 on the same engine (the body plans and the numbering are still good, the chrome blocks of the part are
    // dropped); a note is laid out again from the page whose notes it can have changed (back to a page that no notes run over onto) to the last page
    // that shows it, and the refill may stop after that as for the body.
    private StoryEffect PlanStoryEdit(BlockChange change, Engine old, int oldCount, LayoutOptions options, out EditPlan? plan)
    {
        plan = null;
        var chrome = old.Chrome;
        if (change.Story.Kind == StoryKind.Comment || chrome is null || change.Story.Key is not { } key)
        {
            return change.Story.Kind == StoryKind.Comment || chrome is null ? StoryEffect.None : StoryEffect.Restart;
        }

        var records = old.Filler.Records;
        var body = old.Document.Body.Blocks;
        if (oldCount == 0 || records.Count == 0 || body.Count != old.BlockPage.Length)
        {
            return StoryEffect.Restart;
        }

        var stopAfter = int.MaxValue;
        var startPage = 0;
        var startsOver = true;
        var requireNoCarry = false;
        switch (change.Story.Kind)
        {
            case StoryKind.HeaderFooter:
                chrome.Invalidate(change.Story);
                if (!chrome.Shows(key))
                {
                    return StoryEffect.None;
                }

                break;
            case StoryKind.Note:
                chrome.Notes.Invalidate(key);
                var first = -1;
                var last = -1;
                for (var i = 0; i < oldCount; i++)
                {
                    if (ShowsStory(_pages[i], change.Story))
                    {
                        first = first < 0 ? i : first;
                        last = i;
                    }
                }

                if (first < 0)
                {
                    // Nothing shows the note: either it is cited later (an unfinished layout) or it was empty and now takes room; start over to be safe.
                    return StoryEffect.Restart;
                }

                startPage = Math.Min(first, records.Count - 1);
                stopAfter = last;
                startsOver = false;
                requireNoCarry = true;
                break;
            default:
                return StoryEffect.Restart;
        }

        var flat = FlatBlocks.From(body);
        var sections = old.Sections;
        if (!FindResume(records, startPage, flat, sections, flat.Count, requireNoCarry, out var resume, out var startFlat))
        {
            return StoryEffect.Restart;
        }

        plan = new EditPlan
        {
            Lo = 0,
            Added = 0,
            Removed = 0,
            Nested = true,
            BodyCount = body.Count,
            Flat = flat,
            Sections = sections,
            FirstFlat = 0,
            EndFlat = 0,
            Resume = resume!,
            ResetFromBody = EditPlan.BodyIndexAfterPlaced(flat, resume!, startFlat),
            ShiftedPage = (int[])old.BlockPage.Clone(),
            ShiftedY = (float[])old.BlockY.Clone(),
            StopAfterPage = stopAfter,
            StartsOver = startsOver,
        };
        return StoryEffect.Refill;
    }

    private static bool ShowsStory(LaidOutPage page, StoryRef story)
    {
        foreach (var line in page.TextLines)
        {
            if (line.Story == story)
            {
                return true;
            }
        }

        return false;
    }

    // The first index of the flat list whose block has a body index of at least bodyIndex (the list's body indexes never decrease).
    private static int LowerBound(List<FlatBlock> flat, int bodyIndex)
    {
        var low = 0;
        var high = flat.Count;
        while (low < high)
        {
            var middle = (low + high) >> 1;
            if (flat[middle].BodyIndex < bodyIndex)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int IndexOf(List<FlatBlock> flat, Block block, int limit)
    {
        for (var i = 0; i < limit && i < flat.Count; i++)
        {
            if (ReferenceEquals(flat[i].Block, block))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool HasSectionBreak(BlockChange change)
    {
        foreach (var block in change.OldBlocks)
        {
            if (block is Paragraph { SectionBreak: not null })
            {
                return true;
            }
        }

        for (var i = 0; i < change.Added; i++)
        {
            var at = change.First + i;
            if (at >= 0 && at < change.Container.Count && change.Container[at] is Paragraph { SectionBreak: not null })
            {
                return true;
            }
        }

        return false;
    }

    // The index of the top-level block of the body that holds the list somewhere inside (a table cell, a block content control), or -1.
    private static int OwnerOf(IList<Block> body, IList<Block> container)
    {
        for (var i = 0; i < body.Count; i++)
        {
            if (Holds(body[i], container, 0))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Holds(Block block, IList<Block> container, int depth)
    {
        if (depth > 32)
        {
            return false;
        }

        switch (block)
        {
            case ContentControlBlock control:
                return ReferenceEquals(control.Content, container) || control.Content.Any(inner => Holds(inner, container, depth + 1));
            case Table table:
                return table.Rows.Any(row => HoldsRow(row, container, depth + 1));
            default:
                return false;
        }
    }

    private static bool HoldsRow(RowItem item, IList<Block> container, int depth) => item switch
    {
        TableRow row => depth <= 32 && row.Items.Any(inner => HoldsRow(inner, container, depth + 1)),
        TableCell cell => ReferenceEquals(cell.Blocks, container) || cell.Blocks.Any(inner => Holds(inner, container, depth + 1)),
        _ => false,
    };
}
