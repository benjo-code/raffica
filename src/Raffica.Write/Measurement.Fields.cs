using System.Diagnostics;
using System.Globalization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The fields run (<c>--measure-fields</c>) and the typing run with a table of contents (<c>--measure-typing-fields</c>) of <see cref="Measurement"/>: the commands and the field refresh of milestone 4.5b. Both
/// runs first prepare the same document from the file on the command line (untimed, except the first refresh of the file as it was opened, row <c>fields-open view=page n=1 flush-ms=… full-ms=… passes=…</c>): the heading
/// numbering is switched on, a table of contents of the levels 1 and 2 with page numbers is inserted before the first chapter (more than 100 entries on the thesis), one caption and one page cross-reference are inserted
/// at the paragraph in the middle. Measurement runs have no field refresh of their own; these two follow it, with a clock that never fires, so the run decides when a refresh happens and can time it
/// (<see cref="FieldRefreshRunner.Flush"/> is the tick of the 250 ms timer).
/// <para>
/// The fields run: rows <c>fields case=&lt;case&gt; view=page n=… median-ms=… p95-ms=… max-ms=… handler-median-ms=… frame-median-ms=… …</c> (the words of a typing row) for the cases of <see cref="FieldPlaces.All"/>.
/// For the four commands (<see cref="FieldPlaces.Commands"/>) the time is the command's (handler, the render pass that shows it, the page draws); for the refresh cases it is the same measure of the first refresh
/// that follows an edit which was made before, untimed: a paragraph made a Heading 1 (<c>refresh-heading-edit</c>, the entry list grows and the numbers of the later headings move; the view stays at the edit), the same
/// with the view at the end of the document (<c>refresh-heading-edit-far-view</c>: the refresh lays out from the table of contents through the page in view), empty paragraphs inserted into the first chapter
/// (<c>refresh-pages</c>, the page numbers change) and the first refresh of a new runner over the whole body of a document whose table of contents is stale (<c>first-settle</c>). After every sample a row
/// <c>fields-settle case=… view=page n=… flush-median-ms=… flush-max-ms=… full-median-ms=… full-max-ms=… passes=…</c> follows at the end of the case: the time the refresh passes that complete the change held the UI
/// thread (for a refresh case the first one included), the time from the command or the first refresh until nothing is pending and the layout is complete, and the most passes of one sample. Every sample is undone outside
/// the timing and the refresh and the layout are brought to rest again; a document that is not the prepared one afterwards ends the run. The judged metric <c>fields</c> of <c>measure.cs</c> is the worst median of the
/// four command rows against a fixed limit (<c>CheckCommand.FieldsBudget</c>); everything else is recorded and never judged.
/// </para>
/// <para>
/// The typing run with fields types at the places of <see cref="FieldPlaces.TypingNames"/> (the middle of a long paragraph without a field, the end of a numbered heading that the table of contents lists, the end of a caption, a table cell) in both
/// views. After every tenth key the user pauses: the refresh runs (timed apart, row <c>fields-refresh case=… view=… n=… median-ms=… max-ms=… full-median-ms=… full-max-ms=… passes=…</c>, the same two times as a settle
/// row) and the typing goes on. Its typing rows count into the judged metric <c>typing</c>.
/// </para>
/// </summary>
internal sealed partial class Measurement
{
    /// <summary>The runs per case of the fields run when the count is the default one (a run lays the document out several times: the command, the refresh, the undo).</summary>
    public const int FieldsRuns = 8;

    /// <summary>The keys between two pauses of the typing run with fields.</summary>
    public const int FieldKeysPerPause = 10;

    // The empty paragraphs the refresh-pages case puts into the first chapter: more than a page.
    private const int PagesParagraphs = 60;

    private readonly bool _fieldsRun;
    private readonly bool _typingFieldsRun;
    private readonly HeldFieldClock _heldClock = new();
    private int _fieldsSetup;
    private FieldBaseline? _baseline;
    private int _fieldsStage;
    private int _fieldsAfter;
    private bool _fieldsPause;
    private long _settleStarted;
    private double _settleFlush;
    private int _passesBefore;
    private readonly List<double> _settleFlushSamples = [];
    private readonly List<double> _settleFullSamples = [];
    private int _settlePasses;

    // What the preparation of both runs made: the number of history steps, the text and shape of the body, and the places the cases use (as block indices: an undo may replace paragraph objects).
    private sealed record FieldBaseline(int UndoCount, string Fingerprint, int Middle, int FirstChapter, int ChapterText, int PlainParagraph, int LongParagraph, int Caption, int Heading);

    // ---- the preparation ----------------------------------------------------------------------------------------------------------

    // One step per frame until the document is prepared; false while it is not (the next frame is asked for) or when the run ended.
    private bool PrepareFields(PageView view)
    {
        if (_baseline is not null)
        {
            return true;
        }

        var session = view.Tab.EnsureEditor();
        var model = view.Tab.Docx.Model;
        var blocks = model.Body.Blocks;
        switch (_fieldsSetup)
        {
            case 0:
                // The first command attaches the session to the view (and resets the selection): do that before a caret is placed. The runner of the window is replaced by one the run drives.
                view.ApplyOperation(_ => null);
                view.Tab.ResetFields(_heldClock);
                BeginSettle(view);
                break;
            case 1:
                if (!Settle(view))
                {
                    return false;
                }

                Write(string.Create(CultureInfo.InvariantCulture, $"fields-open view=page n=1 flush-ms={_settleFlush:0.00} full-ms={Stopwatch.GetElapsedTime(_settleStarted).TotalMilliseconds:0.00} passes={Passes(view)}"));
                if (HeadingNumbering.StateOf(model) != HeadingNumberingState.On && !Prepared(view, session, FirstHeading(blocks), 0, () => view.RunStructureCommand(WriteCommandIds.NumberHeadings), "the heading numbering"))
                {
                    return false;
                }

                BeginSettle(view);
                break;
            case 2:
                if (!Settle(view))
                {
                    return false;
                }

                if (!Prepared(view, session, FirstHeading(blocks), 0, () => view.RunTocCommand(new TocRequest(TocAction.Insert, new TocOptions(1, 2, true, true, TocLeader.Dots, null))), "the table of contents"))
                {
                    return false;
                }

                BeginSettle(view);
                break;
            case 3:
                if (!Settle(view))
                {
                    return false;
                }

                if (MiddleParagraph(view) is not { } middle || !Prepared(view, session, middle.Index, middle.Length, () => view.RunCaptionCommand(new CaptionRequest(CaptionLabels.Figure, "A measured figure", CaptionPlace.Below, false)), "the caption"))
                {
                    FailTyping("the preparation of the fields run found no paragraph for the caption");
                    return false;
                }

                BeginSettle(view);
                break;
            case 4:
                if (!Settle(view))
                {
                    return false;
                }

                if (MiddleParagraph(view) is not { } at || HeadingTarget(view) is not { } target || !Prepared(view, session, at.Index, at.Length, () => view.RunCrossReferenceCommand(new CrossRefRequest(target, CrossRefKind.Page)), "the cross-reference"))
                {
                    FailTyping("the preparation of the fields run found no place or target for the cross-reference");
                    return false;
                }

                BeginSettle(view);
                break;
            default:
                if (!Settle(view))
                {
                    return false;
                }

                _baseline = ReadBaseline(view, session);
                if (_baseline is null)
                {
                    FailTyping("the prepared document has no place for every case");
                    return false;
                }

                return true;
        }

        _fieldsSetup++;
        NextTypingFrame();
        return false;
    }

    // One preparation command at a place: the caret goes there, the command runs, and it must be one step of the history.
    private bool Prepared(PageView view, EditSession session, int block, int offset, Func<bool> command, string what)
    {
        if (block < 0 || view.Tab.Docx.Model.Body.Blocks[block] is not Paragraph paragraph)
        {
            FailTyping($"the preparation of the fields run found no paragraph for {what}");
            return false;
        }

        var at = new TextPosition(StoryRef.Body, paragraph, offset);
        session.SetSelection(new TextRange(at, at));
        var before = session.Editor.History.UndoCount;
        command();
        if (session.Editor.History.UndoCount != before + 1)
        {
            FailTyping($"the preparation of the fields run could not make {what}");
            return false;
        }

        return true;
    }

    private static bool IsOutsideToc(IList<Block> blocks, int index) => !FieldView.IsInTableOfContents(blocks, index);

    // The first level 1 heading that is not an entry of a table of contents.
    private static int FirstHeading(IList<Block> blocks) =>
        Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph { Properties.StyleId: "Heading1" } && IsOutsideToc(blocks, i)).DefaultIfEmpty(-1).First();

    private static (int Index, int Length)? MiddleParagraph(PageView view) =>
        TypingPlaces.Pick(view.Tab.Docx.Model, view.Runner.PageLayout).FirstOrDefault(p => p.Name == "middle") is { } place
            ? (place.HintBlock, Length(view, (Paragraph)view.Tab.Docx.Model.Body.Blocks[place.HintBlock]))
            : null;

    private static int Length(PageView view, Paragraph paragraph) => ParagraphProjection.Build(paragraph, view.Runner.PageLayout.ProjectionHost(StoryRef.Body, paragraph)).Text.Length;

    // The heading in the middle of the list the card would show.
    private static CrossRefTarget? HeadingTarget(PageView view)
    {
        var targets = view.CrossReferenceCardContext().TargetsOf(CrossRefTargetKind.Heading);
        return targets.Count == 0 ? null : targets[targets.Count / 2];
    }

    private static FieldBaseline? ReadBaseline(PageView view, EditSession session)
    {
        var blocks = view.Tab.Docx.Model.Body.Blocks;
        var middle = MiddleParagraph(view)?.Index ?? -1;
        var chapter = FirstHeading(blocks);
        var half = blocks.Count / 2;

        // Plain body paragraphs (no style of their own, no section break, no field) of a length in the range: the short ones are made headings by the preparation edits of the refresh cases, a long one is typed into.
        bool Plain(int i, int atLeast, int atMost) => blocks[i] is Paragraph { SectionBreak: null, Properties.StyleId: null or "Normal" } p && IsOutsideToc(blocks, i) && !FieldView.AnyIn([p]) && Length(view, p) is var length && length >= atLeast && length <= atMost;
        int Nearest(Func<int, bool> wanted) => Enumerable.Range(0, blocks.Count).Where(i => i != middle && wanted(i)).OrderBy(i => Math.Abs(i - half)).ThenBy(i => i).DefaultIfEmpty(-1).First();

        var chapterText = chapter < 0 ? -1 : Enumerable.Range(chapter + 1, blocks.Count - chapter - 1).Where(i => blocks[i] is Paragraph { SectionBreak: null } p && p.Properties.StyleId?.StartsWith("Heading", StringComparison.Ordinal) != true && Length(view, p) > 0).DefaultIfEmpty(-1).First();
        var plain = Nearest(i => Plain(i, 1, 400));
        var caption = Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph p && IsOutsideToc(blocks, i) && FieldView.InParagraph(p).Any(f => f.Support.Kind == FieldKind.Seq)).DefaultIfEmpty(-1).First();
        var heading = Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph { Properties.StyleId: "Heading2", SectionBreak: null } p && IsOutsideToc(blocks, i) && Length(view, p) > 0).OrderBy(i => Math.Abs(i - half)).DefaultIfEmpty(-1).First();

        // The long paragraph the typing run types in: without a field, so that the refresh after its keys may skip the page results.
        var longPlain = Nearest(i => Plain(i, 200, int.MaxValue));
        return middle < 0 || chapterText < 0 || plain < 0 || longPlain < 0 || caption < 0 || heading < 0
            ? null
            : new FieldBaseline(session.Editor.History.UndoCount, FieldFingerprint(view.Tab.Docx.Model), middle, chapter, chapterText, plain, longPlain, caption, heading);
    }

    // What a sample that was not taken back would change: the blocks, the style of every paragraph, its text and the number of its inlines (a bookmark left behind shows there).
    private static string FieldFingerprint(WordDocument model)
    {
        var hash = new HashCode();
        foreach (var block in model.Body.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                hash.Add(paragraph.Properties.StyleId, StringComparer.Ordinal);
                hash.Add(paragraph.Inlines.Count);
                hash.Add(ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text, StringComparer.Ordinal);
            }
            else
            {
                hash.Add(block.GetType().Name, StringComparer.Ordinal);
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{model.Body.Blocks.Count}:{hash.ToHashCode()}");
    }

    // ---- the refresh, brought to rest ---------------------------------------------------------------------------------------------

    private void BeginSettle(PageView view)
    {
        _settleStarted = Stopwatch.GetTimestamp();
        _settleFlush = 0;
        _passesBefore = view.Tab.Fields?.Passes ?? 0;
    }

    private int Passes(PageView view) => (view.Tab.Fields?.Passes ?? 0) - _passesBefore;

    // One frame of a settle: waits for the layout, then runs the refresh as the timer would until nothing is pending. True once at rest; false with the next frame asked for, or after the run ended.
    private bool Settle(PageView view)
    {
        if (view.IsLayoutComplete)
        {
            if (view.Tab.Fields is not { IsPending: true } runner)
            {
                return true;
            }

            var start = Stopwatch.GetTimestamp();
            runner.Flush();
            _settleFlush += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (runner.Failures > 0)
            {
                FailTyping($"the field refresh failed ({_job?.Case})");
                return false;
            }
        }

        if (Stopwatch.GetElapsedTime(_settleStarted) > s_notReady)
        {
            FailTyping($"the field refresh did not come to rest within 60 s ({_job?.Case})");
            return false;
        }

        NextTypingFrame();
        return false;
    }

    // ---- the fields run -----------------------------------------------------------------------------------------------------------

    private List<Job> PlanFields(PageView view)
    {
        var baseline = _baseline!;
        var runs = _typingKeys == TypingKeys ? FieldsRuns : _typingKeys;
        var blocks = view.Tab.Docx.Model.Body.Blocks;
        var jobs = new List<Job>();
        foreach (var name in FieldPlaces.All)
        {
            var block = name switch
            {
                "toc-insert" => baseline.FirstChapter,
                "refresh-heading-edit" or "refresh-heading-edit-far-view" or "first-settle" => baseline.PlainParagraph,
                "refresh-pages" => baseline.ChapterText,
                _ => baseline.Middle,
            };
            jobs.Add(new Job(name, WriteView.Page, null, new TypingPlace(name, StoryRef.Body, (Paragraph)blocks[block], 0, block), runs, "fields-" + name));
        }

        return jobs;
    }

    // One run of a fields case: the place (and for a refresh case the untimed edit before it) in frames of their own, then the timed command or refresh.
    private void SendFields(PageView view, string kind)
    {
        var session = view.Tab.EnsureEditor();
        var model = view.Tab.Docx.Model;
        var refresh = !FieldPlaces.Commands.Contains(kind);
        var block = _job!.Place.HintBlock;
        if (model.Body.Blocks[block] is not Paragraph paragraph)
        {
            FailTyping($"the place of the case has changed ({_job.Case})");
            return;
        }

        switch (_fieldsStage)
        {
            case 0:
                // A command works at the end of the middle paragraph (the table of contents at the start of the first chapter); a refresh case edits its paragraph at the start.
                var offset = refresh || kind == "toc-insert" ? 0 : Length(view, paragraph);
                var caret = new TextPosition(StoryRef.Body, paragraph, offset);
                session.SetSelection(new TextRange(caret, caret));
                view.ScrollToBlock(block);
                break;
            case 1:
                view.EnsureCaretVisible();
                break;
            case 2:
                if (refresh && !EditBeforeRefresh(view, session, kind, paragraph))
                {
                    return;
                }

                break;
            case 3:
                if (kind == "refresh-heading-edit-far-view")
                {
                    view.ScrollToBlock(model.Body.Blocks.Count - 1);
                }

                break;
            default:
                TimeFields(view, session, kind);
                return;
        }

        _fieldsStage++;
        NextTypingFrame();
    }

    // The edit a refresh case is about, as a user makes it; the runner hears of it and waits (its clock never fires).
    private bool EditBeforeRefresh(PageView view, EditSession session, string kind, Paragraph paragraph)
    {
        var before = session.Editor.History.UndoCount;
        if (kind == "refresh-pages")
        {
            var at = new TextPosition(StoryRef.Body, paragraph, 0);
            view.ApplyOperation(ed => ed.InsertText(new TextRange(at, at), new string('\n', PagesParagraphs)));
        }
        else
        {
            view.RunFormatCommand(WriteCommandIds.ParagraphStyle, "Heading1");
        }

        if (session.Editor.History.UndoCount != before + 1)
        {
            FailTyping($"the edit before the refresh changed nothing ({_job?.Case})");
            return false;
        }

        return true;
    }

    private void TimeFields(PageView view, EditSession session, string kind)
    {
        var model = view.Tab.Docx.Model;
        Func<bool> run;
        switch (kind)
        {
            case "caption-insert":
                run = () => view.RunCaptionCommand(new CaptionRequest(CaptionLabels.Figure, "Measured figure", CaptionPlace.Below, false));
                break;
            case "xref-insert":
                {
                    if (HeadingTarget(view) is not { } target)
                    {
                        FailTyping("no heading to refer to");
                        return;
                    }

                    run = () => view.RunCrossReferenceCommand(new CrossRefRequest(target, CrossRefKind.Page));
                    break;
                }

            case "toc-insert":
                run = () => view.RunTocCommand(new TocRequest(TocAction.Insert, new TocOptions(1, 3, true, true, TocLeader.Dots, null)));
                break;
            case "bookmark-add":
                {
                    run = () => view.RunBookmarkCommand(new BookmarkRequest(BookmarkAction.Add, "Measured"));
                    break;
                }

            default:
                {
                    // A document that has not been looked at: a new runner, whose first refresh walks the whole body.
                    if (kind == "first-settle")
                    {
                        view.Tab.ResetFields(_heldClock);
                    }

                    if (view.Tab.Fields is not { IsPending: true } runner)
                    {
                        FailTyping($"no refresh is waiting ({_job?.Case})");
                        return;
                    }

                    run = () =>
                    {
                        runner.Flush();
                        return true;
                    };
                    break;
                }
        }

        var refresh = !FieldPlaces.Commands.Contains(kind);
        var revision = model.Edits.Revision;
        var derived = model.Edits.DerivedVersion;
        BeginSettle(view);
        _fieldsStage = 0;
        _keyIncomplete = false;
        _renderSeen = false;
        _renderStart = 0;
        _awaiting = true;
        session.Phases = _phases;
        _phases.Reset();
        BeginKeyProbe();
        _keyAt = Stopwatch.GetTimestamp();
        run();
        _handlerEnd = Stopwatch.GetTimestamp();
        EndKeyProbe();
        _settleStarted = _keyAt;
        if (refresh)
        {
            // The first refresh is one of the passes that hold the UI thread until the change is complete.
            _settleFlush = Stopwatch.GetElapsedTime(_keyAt, _handlerEnd).TotalMilliseconds;
        }

        if (refresh ? model.Edits.DerivedVersion == derived : model.Edits.Revision == revision)
        {
            _awaiting = false;
            FailTyping($"the {(refresh ? "refresh" : "command")} changed nothing ({_job?.Case})");
            return;
        }

        NextTypingFrame();
    }

    // After a sample: the refresh is brought to rest (timed), the sample is undone, the refresh is brought to rest again and the document must be the prepared one. True when the next sample may start.
    private bool FinishFieldsSample(PageView view)
    {
        if (_fieldsAfter == 1)
        {
            if (!Settle(view))
            {
                return false;
            }

            _settleFlushSamples.Add(_settleFlush);
            _settleFullSamples.Add(Stopwatch.GetElapsedTime(_settleStarted).TotalMilliseconds);
            _settlePasses = Math.Max(_settlePasses, Passes(view));
            var history = view.Tab.EnsureEditor().Editor.History;
            if (!UndoSteps(view, history.UndoCount - _baseline!.UndoCount))
            {
                FailTyping($"a measured fields case could not be undone ({_job?.Case})");
                return false;
            }

            BeginSettle(view);
            _fieldsAfter = 2;
            NextTypingFrame();
            return false;
        }

        if (!Settle(view))
        {
            return false;
        }

        if (view.Tab.EnsureEditor().Editor.History.UndoCount != _baseline!.UndoCount || FieldFingerprint(view.Tab.Docx.Model) != _baseline.Fingerprint)
        {
            // A sample that cannot be taken back changes the document for every case after it: the measurement ends, it never goes on silently.
            FailTyping($"a measured fields case left the document changed ({_job?.Case})");
            return false;
        }

        _fieldsAfter = 0;
        return true;
    }

    // At the end of a case of the fields run: how long the refresh took until the change was complete. Recorded, never judged.
    private void WriteFieldsSettle()
    {
        var flush = _settleFlushSamples.Order().ToArray();
        var full = _settleFullSamples.Order().ToArray();
        if (flush.Length > 0)
        {
            Write(string.Create(CultureInfo.InvariantCulture, $"fields-settle case={_job!.Case} view=page n={flush.Length} flush-median-ms={Median(flush):0.00} flush-max-ms={flush[^1]:0.00} full-median-ms={Median(full):0.00} full-max-ms={full[^1]:0.00} passes={_settlePasses}"));
        }
    }

    // ---- the typing run with fields -----------------------------------------------------------------------------------------------

    private List<Job> PlanTypingFields(PageView view)
    {
        var baseline = _baseline!;
        var blocks = view.Tab.Docx.Model.Body.Blocks;
        var cell = TypingPlaces.Pick(view.Tab.Docx.Model, view.Runner.PageLayout).FirstOrDefault(p => p.Name == "table-cell");
        var jobs = new List<Job>();
        foreach (var mode in new[] { WriteView.Page, WriteView.Writing })
        {
            foreach (var name in FieldPlaces.TypingNames)
            {
                var place = name switch
                {
                    "middle" => End(baseline.LongParagraph, half: true),
                    "numbered-heading" => End(baseline.Heading, half: false),
                    "caption" => End(baseline.Caption, half: false),
                    _ => cell,
                };
                if (place is null)
                {
                    _error.WriteLine($"error: the document has no place for '{name}'");
                    continue;
                }

                jobs.Add(new Job(name, mode, null, place with { Name = name }, _typingKeys));

                TypingPlace End(int block, bool half)
                {
                    var paragraph = (Paragraph)blocks[block];
                    var length = Length(view, paragraph);
                    return new TypingPlace(name, StoryRef.Body, paragraph, half ? length / 2 : length, block);
                }
            }
        }

        return jobs;
    }

    // A pause of the typing run: the refresh the keys asked for runs as the timer would run it. True when it came to rest and the typing may go on.
    private bool FinishTypingPause(PageView view)
    {
        if (!Settle(view))
        {
            return false;
        }

        _settleFlushSamples.Add(_settleFlush);
        _settleFullSamples.Add(Stopwatch.GetElapsedTime(_settleStarted).TotalMilliseconds);
        _settlePasses = Math.Max(_settlePasses, Passes(view));
        _fieldsPause = false;
        return true;
    }

    private void WriteTypingFieldsRefresh()
    {
        var flush = _settleFlushSamples.Order().ToArray();
        var full = _settleFullSamples.Order().ToArray();
        if (flush.Length > 0)
        {
            Write(string.Create(CultureInfo.InvariantCulture, $"fields-refresh case={_job!.Case} view={ViewName(_job.View)} n={flush.Length} median-ms={Median(flush):0.00} max-ms={flush[^1]:0.00} full-median-ms={Median(full):0.00} full-max-ms={full[^1]:0.00} passes={_settlePasses}"));
        }
    }

    // The clock of the two fields runs: its timers never fire by themselves, the run calls FieldRefreshRunner.Flush where a pause of the user would let the timer fire.
    private sealed class HeldFieldClock : IFieldClock
    {
        public IFieldTimer CreateTimer(Action tick) => new Timer();

        private sealed class Timer : IFieldTimer
        {
            public bool IsArmed { get; private set; }

            public void Restart(TimeSpan delay) => IsArmed = true;

            public void Stop() => IsArmed = false;

            public void Dispose() => IsArmed = false;
        }
    }
}

/// <summary>
/// The cases of <c>--measure-fields</c> (<c>FieldCases.Names</c> of the bench has the same ones without the far-view case, which needs a view; <c>WriteRun.FieldsNames</c> of <c>measure.cs</c> has the same list) and the
/// places of <c>--measure-typing-fields</c> (<c>WriteRun.TypingFieldsPlaces</c>): change them together.
/// </summary>
internal static class FieldPlaces
{
    /// <summary>The cases the bench has too.</summary>
    public static readonly string[] Names = ["caption-insert", "xref-insert", "toc-insert", "bookmark-add", "refresh-heading-edit", "refresh-pages", "first-settle"];

    /// <summary>The case that only the app has (the bench has no view): the refresh after a heading edit with the view at the end of the document.</summary>
    public const string FarView = "refresh-heading-edit-far-view";

    /// <summary>Every case of the run in the order of its rows.</summary>
    public static readonly string[] All = [.. Names, FarView];

    /// <summary>The four commands: their worst median is the judged metric <c>fields</c>.</summary>
    public static readonly string[] Commands = ["caption-insert", "xref-insert", "toc-insert", "bookmark-add"];

    /// <summary>The places of the typing run with fields.</summary>
    public static readonly string[] TypingNames = ["middle", "numbered-heading", "caption", "table-cell"];
}
