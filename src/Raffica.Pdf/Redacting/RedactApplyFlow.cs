using Raffica.Core.Pdf;
using Raffica.Core.Redaction;
using Raffica.Core.Saving;
using Raffica.Core.Viewing;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Redacting;

/// <summary>
/// Apply of the redaction plan of a tab: the checked full rewrite of Core with everything the user must see before and after. The order is the one of the
/// page apply (<see cref="ApplyFlow"/>): (1) open drafts are ended, an unresolved draft or a busy editor refuses with a card; (2) unsaved marks and form values are
/// saved first, on agreement (the rewrite takes its pages from the file); (3) the analysis on a throwaway working copy, a cancelable job
/// (<see cref="RedactionExecutor.AnalyzeAsync(RedactionPlan, PdfDocument, string, string?, CancellationToken)"/>); a refusal is a card with the reason and the pages, nothing
/// is written and plan and mode stay (a page that cannot be redacted in place can be turned into an image on the Apply card, with a tick each; the
/// other refusals offer nothing); (4) <see cref="RedactApplyCard"/> shows what is removed, cleaned, lost and not cleaned; (5) the target is the file picker
/// (Save as, the default) or the open file; (6) <see cref="RedactionApplier.ApplyAsync(ViewerSession, PdfEngine, RedactionApplyRequest, IProgress{double}?, CancellationToken)"/>
/// runs on a pool thread with a job card (progress, Cancel until the target is replaced); (7) on success the tab follows the file and is rebuilt over the
/// adopted document at the page that was shown, the mode ends, and the result card says what was removed, what was checked and what was not;
/// (8) every failure ends in a card with its reason (the check names kinds and pages, never the text), the plan stays and the file is untouched (except after the
/// adopt failure, which says that the file at its path is the redacted one). Nothing escapes to the caller. The plan version is re-checked after every card and
/// picker before the write; the job cards are dialogs, so the plan cannot change while it is written. One apply at a time.
/// </summary>
/// <param name="dialogs">Where the cards appear.</param>
/// <param name="saveMarks">The normal save of the marks (<see cref="SaveController.SaveMarksAsync"/>); returns whether they are saved.</param>
/// <param name="pickSavePath">Asks for the target of Save as (the system dialog confirms an overwrite); null: the user canceled.</param>
/// <param name="engine">The engine that opens the working copy and the written file; resolved on the UI thread.</param>
/// <param name="currentPage">The 0-based page the user looks at in the tab.</param>
/// <param name="rebuild">Rebuilds the tab over the adopted document at a 0-based page (<see cref="PdfWindow.RebuildTab"/>); it also ends the redact mode of the tab.</param>
/// <param name="pathChanged">Called after the tab was given another path (the recent list follows); null: nothing.</param>
/// <param name="changed">Called when the flow starts or ends and after a successful apply (the command states refresh); null: nothing.</param>
/// <param name="busy">Whether a print job runs (an apply is then refused); null: never.</param>
/// <param name="time">The clock of the arming delay of the cards (tests move it).</param>
/// <param name="otherJob">Whether another file job of the window runs (a save, a page apply, extract, split, export): an apply is then refused; null: never.</param>
/// <param name="workspace">The open tabs: a picked target that another tab has open is refused (<see cref="OpenTargetGuard"/>); null: never checked.</param>
internal sealed class RedactApplyFlow(
    DialogQueue dialogs,
    Func<PdfTab, Task<bool>> saveMarks,
    Func<PdfTab, Task<string?>> pickSavePath,
    Func<PdfEngine> engine,
    Func<PdfTab, int> currentPage,
    Action<PdfTab, int> rebuild,
    Action<PdfTab>? pathChanged = null,
    Action? changed = null,
    Func<bool>? busy = null,
    TimeProvider? time = null,
    Func<bool>? otherJob = null,
    Workspace? workspace = null)
{
    private readonly JobRunner _jobs = new(dialogs);

    // Whether the marks were saved into the file by this run (read by the failure text).
    private bool _savedFirst;

    /// <summary>Replaces the apply of Core (tests make it fail in ways the file does not, or block it); null: <see cref="RedactionApplier"/>.</summary>
    internal Func<ViewerSession, PdfEngine, RedactionApplyRequest, IProgress<double>, CancellationToken, Task<RedactionApplyResult>>? Applier { get; set; }

    /// <summary>Replaces the dialog around the job card (tests make it throw); null: the normal dialog.</summary>
    internal Func<SaveJobCard, Dialog>? JobDialog
    {
        get => _jobs.JobDialog;
        set => _jobs.JobDialog = value;
    }

    /// <summary>Gets whether an apply is running (from its first question to its last card).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Cancels the running job, if any (the window is closing).</summary>
    public void Cancel() => _jobs.Cancel();

    /// <summary>Runs the apply for the plan that is open on <paramref name="tab"/>. Returns whether the redactions were applied.</summary>
    public async Task<bool> RunAsync(PdfTab tab, ApplyTarget target = ApplyTarget.Choose)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (IsRunning || tab.RedactIfCreated is not { IsActive: true } session || session.Plan.IsEmpty || tab.Session.Document is null)
        {
            return false;
        }

        if (busy?.Invoke() == true || otherJob?.Invoke() == true)
        {
            await dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.CmdRedactApply, busy?.Invoke() == true ? PdfStrings.PrintBusy : PdfStrings.FileJobBusy, () => done(true)));
            return false;
        }

        IsRunning = true;
        _savedFirst = false;
        try
        {
            JobRunner.Guarded(() => changed?.Invoke());
            return await RunCoreAsync(tab, session, target);
        }
        catch (OperationCanceledException)
        {
            // Canceled by the user (or the tab went away): no card; the plan stays.
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever is not foreseen ends as a card, never as an exception in a command handler; the plan stays.
            return await FailAsync(tab, ex);
        }
        finally
        {
            IsRunning = false;
            JobRunner.Guarded(() => changed?.Invoke());
        }
    }

    private async Task<bool> RunCoreAsync(PdfTab tab, RedactSession session, ApplyTarget target)
    {
        // (1) Pending nudges and valid drafts are made now. A draft that cannot be made or an editor that is still busy has to be dealt with by the user.
        if (tab.AnnotationsIfCreated is { } annotations)
        {
            await annotations.FlushAsync();
            if (annotations.HasUnresolved || annotations.Editor.IsBusy)
            {
                await Tell(PdfStrings.ApplyOpenEdit);
                return false;
            }
        }

        if (!session.IsActive || session.Plan.IsEmpty)
        {
            return false;
        }

        // (2) The redaction takes its pages from the file: marks and form values that are not in it yet are saved first, on agreement.
        if (tab.AnnotationsIfCreated is { Editor.IsDirty: true } unsaved)
        {
            if (!await dialogs.ShowAsync<bool>(done => SaveFirstCard.Create(tab.Title, done, target == ApplyTarget.SaveAs, redact: true)))
            {
                return false;
            }

            if (!await saveMarks(tab) || unsaved.Editor.IsDirty || !session.IsActive || session.Plan.IsEmpty)
            {
                // Canceled or failed (the save said why): the flow stops, the plan stays.
                return false;
            }

            // From here on the marks are in the file: a failure must not say that the file is unchanged.
            _savedFirst = true;
        }

        // The plan the user is shown is the plan that is written: a change of the marks while a picker or card was open is noticed by its version.
        var plan = session.Plan;
        var version = session.Version;

        // (3) What the plan does, worked out on a working copy. The engine is resolved here, on the UI thread: the window creates it on first use.
        var document = tab.Session.Document ?? throw new InvalidOperationException("No document is open.");
        var path = tab.Path ?? throw new InvalidOperationException("The tab has no path.");
        var executor = new RedactionExecutor(engine());
        Task<RedactionAnalysis> Analyze(RedactionPlan analysed) =>
            _jobs.RunAsync(tab, PdfStrings.RedactApplyTitle, PdfStrings.RedactApplyCheckingTitleFormat, (_, token) => executor.AnalyzeAsync(analysed, document, path, null, token));

        var analysis = await Analyze(plan);
        var shown = plan;
        IReadOnlyList<RasterPageInfo> rescue = [];
        if (analysis.IsRefused)
        {
            // A page that cannot be redacted in place can become an image; everything else that is refused offers nothing.
            var rescuable = RescuablePages(analysis, plan);
            if (rescuable.Count == 0 || !analysis.Refusals.All(static r => r.Kind == RedactionRefusalKind.PageNotRedactable))
            {
                return await RefusedAsync(tab, analysis.Refusals);
            }

            try
            {
                shown = rescuable.Aggregate(plan, static (current, page) => current.WithRasterPage(page, on: true));
            }
            catch (RedactionException)
            {
                return await RefusedAsync(tab, analysis.Refusals);
            }

            var again = await Analyze(shown);
            if (again.IsRefused)
            {
                return await RefusedAsync(tab, again.Refusals);
            }

            analysis = again;
            rescue = [.. (again.Raster ?? []).Where(info => rescuable.Contains(info.Page)).OrderBy(static info => info.Page)];
            if (rescue.Count != rescuable.Count)
            {
                // The report cannot say what turning a page into an image does: nothing is offered that the user cannot read first.
                return await RefusedAsync(tab, analysis.Refusals.Count > 0 ? analysis.Refusals : [new RedactionRefusal(RedactionRefusalKind.PageNotRedactable, rescuable, string.Empty)]);
            }
        }

        // (4) The card.
        var decision = await dialogs.ShowAsync<RedactApplyDecision>(done => RedactApplyCard.Create(tab.Title, shown, analysis, rescue, target, done, time));
        if (decision.Choice == ApplyChoice.Back || !session.IsActive || session.Version != version)
        {
            return false;
        }

        if (!ReferenceEquals(shown, plan) && !rescue.All(info => decision.TurnIntoImages.Contains(info.Page)))
        {
            return false;
        }

        // (5) The target. "Save as" never preselects the name of the open file; the system dialog confirms replacing another file, so the request
        // keeps ReplaceExisting at its default (true). A pick that is the open file itself is the original: it gets the same armed confirmation as
        // "Save in the original file" (Back there ends the apply, nothing changes).
        var targetPath = path;
        if (decision.Choice == ApplyChoice.SaveAs)
        {
            if (await pickSavePath(tab) is not { } picked)
            {
                return false;
            }

            targetPath = Path.GetFullPath(picked);
            if (await IsTheOpenFileAsync(targetPath, path)
                && !await dialogs.ShowAsync<bool>(done => RedactOriginalCard.Create(tab.Title, done, time)))
            {
                return false;
            }
        }

        if (!session.IsActive || session.Version != version)
        {
            // The marks were edited while the picker or the confirmation was open: nothing is written for a plan the user has not seen.
            await Tell(PdfStrings.RedactPlanChanged);
            return false;
        }

        // Immediately before the write (and before the rescue pages join the plan): no other tab has the picked target open. Only Save as is asked: the original is the tab's own path.
        if (decision.Choice == ApplyChoice.SaveAs && !await TargetFreeAsync(tab, targetPath))
        {
            return false;
        }

        if (!ReferenceEquals(shown, plan))
        {
            // The pages the user agreed to turn into images join the plan (one history step) only now, with the path chosen and the write next: a cancelled
            // picker leaves the plan exactly as it was. It is the plan that was analysed and shown.
            var joined = session.Replace(shown);
            if (!joined.IsDone)
            {
                await Tell(joined.Problem is { } problem ? PdfStrings.RedactRefusal(problem) : PdfStrings.RedactFailOther);
                return false;
            }
        }

        // (6) The write. The app never sets OverwriteChangedOriginal: a file that changed on disk is a failure with its card.
        RedactionApplyResult result;
        PdfDocument? rewritten = null;
        var request = new RedactionApplyRequest(shown, targetPath, Rewritten: arrived => rewritten = arrived);
        var pdf = engine();
        var apply = Applier ?? RedactionApplier.ApplyAsync;
        try
        {
            result = await _jobs.RunAsync(tab, PdfStrings.RedactApplyTitle, PdfStrings.RedactApplyJobTitleFormat, (progress, token) => apply(tab.Session, pdf, request, progress, token));
        }
        catch (SaveFailureException ex) when (ex.Reason == SaveFailureReason.Canceled)
        {
            return false;
        }
        catch (SaveFailureException ex) when (ex.Reason == SaveFailureReason.RedactionFailed)
        {
            return await CheckFailedAsync(tab, ex.Details as RedactionCheck);
        }
        catch (RedactionRefusedException ex)
        {
            return await RefusedAsync(tab, ex.Refusals);
        }

        if (!result.Adopted || rewritten is null || !ReferenceEquals(tab.Session.Document, rewritten))
        {
            // The file is written, but the tab does not show it (the session was closed or opened something else meanwhile).
            if (!result.Adopted)
            {
                await result.Saved.Reopened.DisposeAsync();
            }

            return await FailAsync(tab, new RedactionAdoptException(targetPath, null));
        }

        // (7) The redactions are applied: the tab follows the file and the result is told.
        Completed(tab, targetPath);
        await ShowResultAsync(Path.GetFileName(targetPath), analysis, result.Check);
        return true;
    }

    // The spelling says it for the same path; the file system says it for what a spelling cannot (links, hard links, 8.3 names, an extended-length path prefix). Off the UI thread.
    // Anything that is not provably another file counts as the same one: a confirmation that is asked needlessly costs a click, a missing one costs the original.
    private static async Task<bool> IsTheOpenFileAsync(string target, string open) =>
        JobRunner.SamePath(target, open) || await Task.Run(() => FileIdentity.AreSameFile(target, open) != false);

    // The pages the analysis refuses as not redactable in place that are not images yet, ascending.
    private static List<int> RescuablePages(RedactionAnalysis analysis, RedactionPlan plan) =>
        [.. analysis.Refusals
            .Where(static r => r.Kind == RedactionRefusalKind.PageNotRedactable)
            .SelectMany(static r => r.Pages)
            .Where(page => !plan.RasterPages.Contains(page))
            .Distinct()
            .Order()];

    // The tab follows the file and is rebuilt over the adopted document at the page that was shown; the rebuild ends the mode. The file is written:
    // whatever goes wrong from here on (a handler of the window) is no failure of the apply and no card.
    private void Completed(PdfTab tab, string written)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var page = 0;
        JobRunner.Guarded(() => page = Math.Max(0, currentPage(tab)));
        var renamed = !string.Equals(Path.GetFullPath(written), tab.Path, comparison);
        if (renamed)
        {
            JobRunner.Guarded(() => tab.Rename(written));
        }

        JobRunner.Guarded(() => rebuild(tab, page));
        if (renamed)
        {
            JobRunner.Guarded(() => pathChanged?.Invoke(tab));
        }
    }

    private async Task ShowResultAsync(string fileName, RedactionAnalysis analysis, RedactionCheck check)
    {
        try
        {
            await dialogs.ShowAsync<bool>(done => RedactResultCard.Create(fileName, analysis, check, () => done(true)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The file is written and the tab follows it; a card that cannot be shown must not turn into "not applied".
        }
    }

    private async Task<bool> RefusedAsync(PdfTab tab, IReadOnlyList<RedactionRefusal> refusals)
    {
        IReadOnlyList<string> reasons = refusals.Count == 0 ? [PdfStrings.RedactFailOther] : [.. refusals.Select(RedactWording.Text)];
        await dialogs.ShowAsync<bool>(done => RedactRefusedCard.Create(tab.Title, PdfStrings.RedactRefusedTitle, PdfStrings.RedactRefusedIntro, reasons, () => done(true)));
        return false;
    }

    private async Task<bool> CheckFailedAsync(PdfTab tab, RedactionCheck? check)
    {
        // A failure without findings is a check that could not be finished, never a pass.
        var findings = check?.Findings ?? [];
        IReadOnlyList<string> reasons = [.. findings.Select(RedactWording.Text).Distinct()];
        var intro = reasons.Count == 0 ? PdfStrings.RedactFailedNoCheck : PdfStrings.RedactFailedIntro;
        await dialogs.ShowAsync<bool>(done => RedactRefusedCard.Create(tab.Title, PdfStrings.RedactFailedTitle, intro, reasons, () => done(true), hint: reasons.Count > 0 ? PdfStrings.RedactFailedHint : null));
        return false;
    }

    private async Task<bool> FailAsync(PdfTab tab, Exception ex)
    {
        var (title, message) = FailureText(tab, ex, _savedFirst);
        await dialogs.ShowAsync<bool>(done => SaveFailedCard.Create(tab.Title, title, message, () => done(true)));
        return false;
    }

    /// <summary>
    /// The title and the sentence of a failure: the reason, and that the file is unchanged and the marks are open (except after an adopt failure).
    /// <paramref name="savedFirst"/>: the marks were written into the file first, so the sentence says that instead of "unchanged".
    /// </summary>
    internal static (string Title, string Message) FailureText(PdfTab tab, Exception ex, bool savedFirst = false)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(ex);
        var stays = " " + (savedFirst ? PdfStrings.RedactStaysAfterSave : PdfStrings.RedactStays);
        var message = ex switch
        {
            RedactionAdoptException adopt => PdfStrings.Fill(PdfStrings.RedactFailAdoptFormat, adopt.TargetPath),
            SaveFailureException { Reason: SaveFailureReason.SourceModified } => PdfStrings.RedactFailSourceModified + stays,
            SaveFailureException { Reason: SaveFailureReason.EncryptedNotSupported } => PdfStrings.RedactFailEncrypted + stays,
            SaveFailureException failure => PdfStrings.SaveFailure(failure.Reason) + " " + PdfStrings.RedactMarksOpen,
            RedactionException => PdfStrings.RedactFailMarks + stays,
            PdfOpenException => PdfStrings.RedactFailOpen + stays,
            InvalidDataException => PdfStrings.RedactFailDamaged + stays,
            _ => PdfStrings.RedactFailOther + stays,
        };
        return (PdfStrings.RedactFailedWriteTitle, message);
    }

    private async Task<bool> TargetFreeAsync(PdfTab tab, string target)
    {
        if (workspace is null || await OpenTargetGuard.FindOtherTabAsync(workspace, tab, target, CancellationToken.None) is null)
        {
            return true;
        }

        await Tell(OpenTargetGuard.NoteFor(target));
        return false;
    }

    private Task<bool> Tell(string message) =>
        dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.CmdRedactApply, message, () => done(true)));
}
