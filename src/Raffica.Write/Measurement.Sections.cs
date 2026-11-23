using System.Diagnostics;
using System.Globalization;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;

namespace Raffica.Write;

/// <summary>
/// The sections run (<c>--measure-sections</c>) of <see cref="Measurement"/>: the section commands of milestone 4.4b through the page view, as the Layout tab, the Sections pane or the command search would run
/// them (<see cref="PageView.RunSectionCommand"/>), with the time the typing run judges (the handler, the render pass that shows the result and the page draws of that frame; the frame latency is recorded).
/// Rows <c>sections case=&lt;case&gt; view=page n=… median-ms=… p95-ms=… max-ms=… handler-median-ms=… frame-median-ms=… …</c> (the words of a typing row) for the cases of <see cref="SectionPlaces.Names"/>: Number
/// pages from page 3 (a new section at the caret), a page number with the prefix "A-" (an appendix), the removal of a section marker, the page setup of the whole document and of one section, and a page
/// number of the look "Page n of m" in the footer. After each case a row <c>sections-full case=… view=page n=… median-ms=… max-ms=…</c> (the time from the command until the layout of the whole thesis is
/// complete again, measured before the undo: recorded, never judged) and one <c>sections-refresh case=… view=page n=1 ms=…</c> row (the debounced refresh of the ribbon states and the Sections pane facts that follows the last change).
/// Every run is undone outside the timing and the layout is complete again before the next run. The place of a case is placed in frames of its own before the command (the caret goes there and the view follows it), as
/// the structure run does, because a caret set in the same frame as the command is the one the last undo left. The judged metric <c>sections</c> of <c>measure.cs</c> is the worst median of the six rows; it has no limit
/// entry yet (the rows are recorded and <c>check</c> says "not judged"). The run never starts autosave or recovery (a measurement run has no scheduler) and makes nothing before the first command.
/// </summary>
internal sealed partial class Measurement
{
    /// <summary>The runs per case when the count is the default one (a section command lays out through the pages in view; the whole layout follows in the background).</summary>
    public const int SectionsRuns = 20;

    private readonly bool _sectionsRun;
    private readonly bool _sectionsPaneRun;
    private readonly List<double> _fullSamples = [];
    private bool _fullPending;
    private int _sectionsStage;

    // The place of a case is placed (caret, then the view follows it) in two frames of their own before the timed command; false when the command may run now.
    private bool PlaceForSections(PageView view, EditSession session, WordDocument model)
    {
        if (_job?.Place is not { } place || model.Body.Blocks[place.HintBlock] is not Paragraph at || _sectionsStage >= 2)
        {
            return false;
        }

        if (_sectionsStage == 0)
        {
            var position = new TextPosition(place.Story, at, Math.Min(place.Offset, ParagraphProjection.Build(at, view.Runner.PageLayout.ProjectionHost(place.Story, at)).Text.Length));
            session.SetSelection(new TextRange(position, position));
            view.ScrollToBlock(place.HintBlock);
        }
        else
        {
            view.EnsureCaretVisible();
        }

        _sectionsStage++;
        NextTypingFrame();
        return true;
    }

    // One run of a sections case: the command with the payload a user would choose, through the page view.
    private void SendSections(PageView view, string kind)
    {
        var session = view.Tab.EnsureEditor();
        var model = view.Tab.Docx.Model;
        if (PlaceForSections(view, session, model))
        {
            return;
        }

        Func<bool> run;
        switch (kind)
        {
            case "number-from-page-3":
                run = () => view.RunSectionCommand(WriteCommandIds.NumberPagesFrom, new PageNumbering(3, PageNumberFormat.Arabic, string.Empty));
                break;
            case "appendix-prefix":
                run = () => view.RunSectionCommand(WriteCommandIds.NumberPagesFrom, new PageNumbering(1, PageNumberFormat.Arabic, "A-"));
                break;
            case "remove-marker":
                run = () => view.RunSectionCommand(WriteCommandIds.RemoveSection);
                break;
            case "page-setup-whole" or "page-setup-section":
                {
                    // The orientation turned over: every page of the scope changes shape, the hardest page setup there is.
                    var caret = session.Caret;
                    var facts = caret is { } at ? session.Editor.ReadSectionFacts(at) : null;
                    if (facts is not { Reason: SectionReason.None })
                    {
                        FailTyping($"the caret is in no section the page setup can read ({_job?.Case})");
                        return;
                    }

                    var sections = SectionList.Of(model);
                    var current = SectionPageSetup.Read(sections[facts.Section].Properties);
                    var change = SectionPayloads.PageSetupFor(WriteCommandIds.PageOrientation, current.Landscape ? "portrait" : "landscape", current, inches: false);
                    var request = new PageSetupRequest(change!, kind == "page-setup-whole" ? ApplyScope.WholeDocument : ApplyScope.ThisSection);
                    run = () => view.RunSectionCommand(WriteCommandIds.PageSetup, request);
                    break;
                }

            default:
                run = () => view.RunSectionCommand(WriteCommandIds.InsertPageNumber, "footer:center:pageOfTotal");
                break;
        }

        var revision = model.Edits.Revision;
        _sectionsStage = 0;
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
            FailTyping($"the section command changed nothing ({_job?.Case})");
            return;
        }

        // One step of history per command (every section command is one EditSession.Apply); RestoreStructure undoes what was counted.
        _structureUndos++;
        NextTypingFrame();
    }

    // After a sample: the time until the layout of the whole document is complete again is taken before the undo (recorded, never judged); true once it was taken and the run is undone.
    private bool WaitForSectionsLayout(PageView view)
    {
        if (!view.IsLayoutComplete)
        {
            if (Stopwatch.GetElapsedTime(_keyAt) > s_notReady)
            {
                FailTyping($"the layout was not complete within 60 s after a section command ({_job?.Case})");
                return false;
            }

            NextTypingFrame();
            return false;
        }

        _fullSamples.Add(Stopwatch.GetElapsedTime(_keyAt).TotalMilliseconds);
        _fullPending = false;
        RestoreStructure(view);
        return true;
    }

    // At the end of a case: the full-layout row and the refresh of the ribbon states and the pane facts that follows the last change (the timer would run it 100 ms after it). Recorded, never judged.
    private void WriteSectionsExtras()
    {
        var sorted = _fullSamples.Order().ToArray();
        if (sorted.Length > 0)
        {
            Write(string.Create(CultureInfo.InvariantCulture, $"sections-full case={_job!.Case} view=page n={sorted.Length} median-ms={Median(sorted):0.00} max-ms={sorted[^1]:0.00}"));
        }

        var start = Stopwatch.GetTimestamp();
        _window.ViewStatesRefresh?.Flush();
        _window.FormatRefresh?.Flush();
        Write(string.Create(CultureInfo.InvariantCulture, $"sections-refresh case={_job!.Case} view=page n=1 ms={Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.00}"));
    }

    private List<Job> PlanSections(PageView view)
    {
        var model = view.Tab.Docx.Model;
        var places = TypingPlaces.Pick(model, view.Runner.PageLayout);
        var runs = _typingKeys == TypingKeys ? SectionsRuns : _typingKeys;
        var jobs = new List<Job>();
        foreach (var name in SectionPlaces.Names)
        {
            var wanted = SectionPlaces.PlaceOf(name);
            var place = name == "remove-marker" ? AfterAMarker(view, model) : places.FirstOrDefault(p => p.Name == wanted);
            if (place is null)
            {
                _error.WriteLine($"error: the document has no place '{wanted}' for '{name}'");
                continue;
            }

            jobs.Add(new Job(name, WriteView.Page, null, place, runs, "sections-" + name));
        }

        return jobs;
    }

    // The command removes the marker that starts the caret's section (it merges that section into the one before), so the caret goes to the first text paragraph after a marker, the marker nearest to the middle of
    // the body.
    private static TypingPlace? AfterAMarker(PageView view, WordDocument model)
    {
        var blocks = model.Body.Blocks;
        var middle = blocks.Count / 2;
        foreach (var i in Enumerable.Range(0, Math.Max(0, blocks.Count - 1)).Where(i => blocks[i] is Paragraph { SectionBreak: not null }).OrderBy(i => Math.Abs(i - middle)))
        {
            if (blocks[i + 1] is Paragraph { SectionBreak: null } next && ParagraphProjection.Build(next, view.Runner.PageLayout.ProjectionHost(StoryRef.Body, next)).Text.Length > 0)
            {
                return new TypingPlace("remove-marker", StoryRef.Body, next, 0, i + 1);
            }
        }

        return null;
    }
}

/// <summary>The cases of <c>--measure-sections</c> (<c>SectionCases.Names</c> of the bench and <c>WriteRun.SectionsNames</c> of <c>measure.cs</c> are near copies: change the three together).</summary>
internal static class SectionPlaces
{
    public static readonly string[] Names = ["number-from-page-3", "appendix-prefix", "remove-marker", "page-setup-whole", "page-setup-section", "insert-page-number"];

    /// <summary>The place of the typing run a case starts at (the remove case has a place of its own, the first paragraph after a marker): an appendix starts at the end of a chapter, the others in the middle of a long paragraph.</summary>
    public static string PlaceOf(string name) => name switch
    {
        "remove-marker" => "section-break",
        "appendix-prefix" => "chapter-end",
        _ => "middle",
    };
}
