using System.Diagnostics;
using System.Globalization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The structure run (<c>--measure-structure</c>) of <see cref="Measurement"/>: the commands of milestone 4.4a through the page view, as the outline pane, the ribbon or a key would run them, with the time
/// the typing run judges (the handler, the render pass that shows the result and the page draws of that frame; the frame latency is recorded). Rows
/// <c>structure case=&lt;case&gt; view=page n=… median-ms=… p95-ms=… max-ms=… handler-median-ms=… frame-median-ms=… …</c> (the same words as a typing row) for the cases of <see cref="StructurePlaces.Names"/>:
/// the first chapter moved behind the last heading, the middle chapter moved down one row, the last chapter moved to the top, the heading numbering switched on and off and a numbered list toggled on one
/// paragraph, and after every case one <c>structure-refresh case=… view=page n=1 ms=…</c> row: the debounced refresh of the ribbon states (the structure facts of the outline commands included) that follows the
/// last change. Every run is undone outside the timing, so a case starts from the state of the file, and the layout is complete again before the next run. Only the three move rows are judged once a limit exists
/// (the aim is 100 ms from the command to the first visible page; the time is the CPU cost, handler plus render pass plus draws, and the limit entry must say whether the aim means that or the frame latency); the other rows and the refresh are recorded. The case <c>move-last-to-first-far-caret</c> (<see cref="StructurePlaces.FarCaret"/>) is the same move with the caret on the last page and the view at the first: an outline operation lays out through the last page in view and never through the old caret's page (it cost the whole layout, about 500 ms, until the fix of 2026-11-22), so the row keeps that setup as the guard against the old cost (recorded, never judged). The page view only: the structure commands are the same in the writing view and the strips are no
/// cost of their own.
/// </summary>
internal sealed partial class Measurement
{
    /// <summary>The runs per case when the count is the default one (a chapter move lays the document out again: 20 runs are 20 relayouts).</summary>
    public const int StructureRuns = 20;

    private readonly bool _structureRun;
    private bool _structurePrepared;
    private int _structureStage;
    private int _structureUndos;

    // The paragraphs of the level 1 headings (the chapters) and every heading, read fresh for each run (an undo may replace a paragraph).
    private static List<OutlineHeading> Chapters(IReadOnlyList<OutlineHeading> headings) => [.. headings.Where(h => h.Entry.Level == 1)];

    // One run of a structure case. Preparation (turning the numbering to the state the case starts from) is untimed and ends the frame, the timed command follows after the layout is complete again.
    private void SendStructure(PageView view, string kind)
    {
        var session = view.Tab.EnsureEditor();
        var model = view.Tab.Docx.Model;
        var headings = OutlineSource.ReadHeadings(model);
        var chapters = Chapters(headings);

        if (kind is "numbering-on" or "numbering-off" && !_structurePrepared)
        {
            var wanted = kind == "numbering-off" ? HeadingNumberingState.On : HeadingNumberingState.Off;
            if (HeadingNumbering.StateOf(model) != wanted)
            {
                view.RunStructureCommand(WriteCommandIds.NumberHeadings);
                _structureUndos++;
                _structurePrepared = true;
                NextTypingFrame();
                return;
            }
        }

        // An undo may replace the paragraph objects: the place is read again from the body block it names. The caret goes there and the view follows it in two frames of their own (as the typing
        // run places itself), because the page a list or numbering edit must lay out through includes the page of the caret the view knows (an outline move does not: it lays out through the last page in
        // view): a caret set in the same frame as the command would still be the one the undo left at the end of the document, and a list command would lay out through that page (the cost of the whole
        // layout, not of the first visible page).
        if (_job?.Place is { } place && model.Body.Blocks[place.HintBlock] is Paragraph at && _structureStage < (_job.Format == "structure-" + StructurePlaces.FarCaret ? 3 : 2))
        {
            if (_structureStage == 0)
            {
                var position = new TextPosition(place.Story, at, Math.Min(place.Offset, ParagraphProjection.Build(at, view.Runner.PageLayout.ProjectionHost(place.Story, at)).Text.Length));
                session.SetSelection(new TextRange(position, position));
                view.ScrollToBlock(place.HintBlock);
            }
            else if (_structureStage == 1)
            {
                view.EnsureCaretVisible();
            }
            else
            {
                // The far-caret case: the caret stays on the last page (the view knows it after the frame that just passed) and the view goes back to the first page, as a user who clicked in the
                // outline pane while the caret is far away.
                view.ScrollToBlock(0);
            }

            _structureStage++;
            NextTypingFrame();
            return;
        }

        Func<bool> run;
        switch (kind)
        {
            case "move-first-to-last":
                {
                    var plan = chapters.Count >= 2 ? OutlinePlanner.Drop(headings, model, OutlinePlanner.RowOf(headings, chapters[0].Paragraph), headings.Count - 1, DropPlace.After) : default;
                    if (chapters.Count < 2 || plan.Reason != OutlineReason.None)
                    {
                        FailTyping("the first chapter cannot be moved behind the last heading");
                        return;
                    }

                    run = () => view.RunOutlineDrop(chapters[0].Paragraph, headings[^1].Paragraph, DropPlace.After);
                    break;
                }

            case "move-last-to-first" or StructurePlaces.FarCaret:
                {
                    var plan = chapters.Count >= 2 ? OutlinePlanner.Drop(headings, model, OutlinePlanner.RowOf(headings, chapters[^1].Paragraph), 0, DropPlace.Before) : default;
                    if (chapters.Count < 2 || plan.Reason != OutlineReason.None)
                    {
                        FailTyping("the last chapter cannot be moved to the top");
                        return;
                    }

                    run = () => view.RunOutlineDrop(chapters[^1].Paragraph, headings[0].Paragraph, DropPlace.Before);
                    break;
                }

            case "move-middle":
                {
                    var chosen = chapters.Count >= 3 ? chapters[chapters.Count / 2] : default;
                    var plan = chapters.Count >= 3 ? OutlinePlanner.Move(headings, model, OutlinePlanner.RowOf(headings, chosen.Paragraph), up: false) : default;
                    if (chapters.Count < 3 || plan.Reason != OutlineReason.None)
                    {
                        FailTyping("the middle chapter cannot be moved down by one row");
                        return;
                    }

                    run = () =>
                    {
                        view.OutlineParagraphSource = () => chosen.Paragraph;
                        try
                        {
                            return view.RunStructureCommand(WriteCommandIds.MoveDown);
                        }
                        finally
                        {
                            view.OutlineParagraphSource = null;
                        }
                    };
                    break;
                }

            case "numbering-on" or "numbering-off":
                run = () => view.RunStructureCommand(WriteCommandIds.NumberHeadings);
                break;
            default:
                run = () => view.RunStructureCommand(WriteCommandIds.Numbers);
                break;
        }

        var revision = model.Edits.Revision;
        _structurePrepared = false;
        _structureStage = 0;
        // The typing run counts keys that started with the layout incomplete; the structure run waits for a complete layout before every command, so none does.
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
        if (model.Edits.Revision == revision)
        {
            _awaiting = false;
            FailTyping($"the structure command changed nothing ({_job?.Case})");
            return;
        }

        _structureUndos++;
        NextTypingFrame();
    }

    // After a sample was taken: every change of the run (the preparation and the command) is undone, so the next run starts from the file's state.
    private void RestoreStructure(PageView view)
    {
        var steps = _structureUndos;
        _structureUndos = 0;
        if (!UndoSteps(view, steps) || view.Tab.EnsureEditor().Editor.History.UndoCount != 0)
        {
            // A run that cannot be undone leaves the document changed for every case after it: the measurement ends, it never goes on silently.
            FailTyping($"a measured command could not be undone ({_job?.Case})");
        }
    }

    /// <summary>Undoes <paramref name="steps"/> steps of the view's history; false as soon as one undo did nothing (an empty history or a history that was cleared behind the run).</summary>
    internal static bool UndoSteps(PageView view, int steps)
    {
        for (; steps > 0; steps--)
        {
            if (view.ApplyOperation(ed => ed.History.Undo()) is null)
            {
                return false;
            }
        }

        return true;
    }

    // At the end of a case: the refresh of the ribbon states that follows the last change (the timer would run it 100 ms after it), timed apart from the runs. Recorded, never judged.
    private void WriteStructureRefresh()
    {
        var start = Stopwatch.GetTimestamp();
        _window.ViewStatesRefresh?.Flush();
        _window.FormatRefresh?.Flush();
        Write(string.Create(CultureInfo.InvariantCulture, $"structure-refresh case={_job!.Case} view=page n=1 ms={Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.00}"));
    }

    private List<Job> PlanStructure(PageView view)
    {
        var model = view.Tab.Docx.Model;
        var headings = OutlineSource.ReadHeadings(model);
        var places = TypingPlaces.Pick(model, view.Runner.PageLayout);
        var runs = _typingKeys == TypingKeys ? StructureRuns : _typingKeys;
        var jobs = new List<Job>();
        var chapters = Chapters(headings);
        foreach (var name in StructurePlaces.Names)
        {
            TypingPlace? place;
            if (name == "toggle-list")
            {
                place = places.FirstOrDefault(p => p.Name == "middle");
                if (place is null)
                {
                    _error.WriteLine("error: the document has no long paragraph to make a list of");
                    continue;
                }
            }
            else
            {
                if (headings.Count == 0 || (name.StartsWith("move-", StringComparison.Ordinal) && chapters.Count < (name == "move-middle" ? 3 : 2)))
                {
                    _error.WriteLine($"error: the document has no chapters for '{name}'");
                    continue;
                }

                place = new TypingPlace(name, StoryRef.Body, headings[0].Paragraph, 0, headings[0].Entry.BodyBlockIndex);
            }

            jobs.Add(new Job(name, WriteView.Page, null, place, runs, "structure-" + name));
        }

        // The far-caret case: the section-crossing move with the caret on the last text paragraph of the body and the view at the first page (recorded, never judged).
        if (chapters.Count >= 2)
        {
            var last = Enumerable.Range(0, model.Body.Blocks.Count).LastOrDefault(i => model.Body.Blocks[i] is Paragraph p && p.SectionBreak is null && ParagraphProjection.Build(p, view.Runner.PageLayout.ProjectionHost(StoryRef.Body, p)).Text.Length > 0);
            if (model.Body.Blocks[last] is Paragraph lastParagraph)
            {
                jobs.Add(new Job(StructurePlaces.FarCaret, WriteView.Page, null, new TypingPlace(StructurePlaces.FarCaret, StoryRef.Body, lastParagraph, 0, last), runs, "structure-" + StructurePlaces.FarCaret));
            }
        }

        return jobs;
    }
}

/// <summary>The cases of <c>--measure-structure</c> (<c>StructureCases.Names</c> of the bench, a near copy of the first six: change both together).</summary>
internal static class StructurePlaces
{
    public static readonly string[] Names = ["move-first-to-last", "move-middle", "move-last-to-first", "numbering-on", "numbering-off", "toggle-list"];

    /// <summary>The case that only the app has (the bench has no view and no caret): the move of the last chapter to the top with the caret on the last page and the view at the first.</summary>
    public const string FarCaret = "move-last-to-first-far-caret";

    /// <summary>Every case of the run in the order of its rows; measure.cs lists the same seven (<c>WriteRun.StructureNames</c>), the bench the first six: change the three together.</summary>
    public static readonly string[] All = [.. Names, FarCaret];
}
