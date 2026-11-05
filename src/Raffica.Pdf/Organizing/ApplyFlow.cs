using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Organizing;

/// <summary>
/// Apply of the page plan of a tab: the checked full rewrite of Core with everything the user must see before and the rebuilt tab after.
/// Steps: (1) the open drafts are ended (<see cref="Annotating.AnnotationSession.FlushAsync"/>); an unresolved draft or a busy editor refuses with a card;
/// (2) unsaved marks and form values are saved first, on agreement, with the normal incremental save; (3) the report of Core says what is lost
/// (<see cref="PageEditExecutor.AnalyzeAsync"/>, a cancelable job); (4) <see cref="ApplyCard"/> shows the summary, the losses and the way out, a refused
/// report only its reason; (5) the target is the file picker (Save as) or the open file; (6) <see cref="PageEditApplier.ApplyAsync"/> runs on a pool
/// thread with a job card (progress, Cancel until the target is replaced); (7) on success the tab follows the file, is rebuilt over the adopted
/// document at the followed page and the plan ends; (8) every failure ends in a card with its reason, the plan and the selection stay, and the
/// file is untouched (except after the adopt failure, which says that the file at its path is the rewritten one). Nothing escapes to the caller.
/// The job cards are dialogs, so the plan cannot change while it is written. One apply at a time.
/// </summary>
/// <param name="dialogs">Where the cards appear.</param>
/// <param name="saveMarks">The normal save of the marks (<see cref="SaveController.SaveMarksAsync"/>); returns whether they are saved.</param>
/// <param name="pickSavePath">Asks for the target of Save as (the system dialog confirms an overwrite); null: the user canceled.</param>
/// <param name="engine">The engine that opens the working copy, the inserted files and the written file.</param>
/// <param name="currentPage">The 0-based page the user looks at in the tab.</param>
/// <param name="rebuild">Rebuilds the tab over the adopted document at a 0-based page (<see cref="PdfWindow.RebuildTab"/>).</param>
/// <param name="pathChanged">Called after the tab was given another path (the recent list follows); null: nothing.</param>
/// <param name="changed">Called when the flow starts or ends and after a successful apply (the command states refresh); null: nothing.</param>
/// <param name="busy">Whether a print job runs (an apply is then refused); null: never.</param>
/// <param name="time">The clock of the arming delay of the cards (tests move it).</param>
/// <param name="otherJob">Whether another file job of the window runs (a save, extract, split, export): an apply is then refused; null: never.</param>
internal sealed class ApplyFlow(
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
    Func<bool>? otherJob = null)
{
    private readonly JobRunner _jobs = new(dialogs);

    /// <summary>Replaces the rewrite of Core (tests make it fail in ways the saver does not, or block it); null: <see cref="DocumentSaver"/>.</summary>
    internal Func<PdfDocument, PdfEngine, RewriteRequest, IProgress<double>?, CancellationToken, Task<SaveResult>>? Rewriter { get; set; }

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

    /// <summary>Runs the apply for the plan that is open on <paramref name="tab"/>. Returns whether the plan was applied.</summary>
    public async Task<bool> RunAsync(PdfTab tab, ApplyTarget target = ApplyTarget.Choose)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (IsRunning || tab.OrganizeIfCreated is not { IsActive: true } session || session.Plan.IsIdentity || tab.Session.Document is null)
        {
            return false;
        }

        if (busy?.Invoke() == true || otherJob?.Invoke() == true)
        {
            await dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.CmdApplyPages, busy?.Invoke() == true ? PdfStrings.PrintBusy : PdfStrings.FileJobBusy, () => done(true)));
            return false;
        }

        IsRunning = true;
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

    private async Task<bool> RunCoreAsync(PdfTab tab, OrganizeSession session, ApplyTarget target)
    {
        // (1) Pending nudges and valid drafts are made now. A draft that cannot be made (a refused text) or an editor that is still busy has to be
        // dealt with by the user: the rewrite would not carry it.
        if (tab.AnnotationsIfCreated is { } annotations)
        {
            await annotations.FlushAsync();
            if (annotations.HasUnresolved || annotations.Editor.IsBusy)
            {
                await Tell(PdfStrings.ApplyOpenEdit);
                return false;
            }
        }

        if (!session.IsActive)
        {
            return false;
        }

        // (2) The rewrite takes its pages from the file: marks and form values that are not in it yet are saved first, on agreement.
        if (tab.AnnotationsIfCreated is { Editor.IsDirty: true } unsaved)
        {
            if (!await dialogs.ShowAsync<bool>(done => SaveFirstCard.Create(tab.Title, done)))
            {
                return false;
            }

            if (!await saveMarks(tab) || unsaved.Editor.IsDirty || !session.IsActive)
            {
                // Canceled or failed (the save said why): the flow stops, the plan stays.
                return false;
            }
        }

        // The plan the user is shown is the plan that is written: a change of the list while a picker or card was open is noticed by its version.
        var plan = session.Plan;
        var version = session.Version;
        var summary = OrganizeSession.Summarize(plan);
        var inserted = session.Inserted;
        string Name(int id) => inserted.TryGetValue(new PageSourceId(id), out var file) ? file.Name : id.ToString(System.Globalization.CultureInfo.CurrentCulture);

        // (3) What the plan loses; the documents are the ones of the session (the primary one as the session shows it now).
        var document = tab.Session.Document ?? throw new InvalidOperationException("No document is open.");
        var opened = inserted.ToDictionary(pair => pair.Key, pair => pair.Value.Document);
        var report = await _jobs.RunAsync(tab, PdfStrings.ApplyTitle, PdfStrings.ApplyCheckingTitleFormat, (_, token) => PageEditExecutor.AnalyzeAsync(plan, document, opened, token));

        // (4) The card. A refused report only says why.
        var choice = await dialogs.ShowAsync<ApplyChoice>(done => ApplyCard.Create(tab.Title, summary, report, id => Name(id), target, done, time));
        if (choice == ApplyChoice.Back || report.IsRefused || !session.IsActive || session.Version != version)
        {
            return false;
        }

        // (5) The target. "Save as" never preselects the name of the open file; the system dialog confirms replacing another file, so the request
        // keeps ReplaceExisting at its default (true) and nothing here asks again.
        var tabPath = tab.Path ?? throw new InvalidOperationException("The tab has no path.");
        var targetPath = tabPath;
        if (choice == ApplyChoice.SaveAs)
        {
            if (await pickSavePath(tab) is not { } picked)
            {
                return false;
            }

            targetPath = Path.GetFullPath(picked);
        }

        if (!session.IsActive || session.Version != version)
        {
            // The list was edited while the picker was open: nothing is written for a plan the user has not seen.
            await Tell(PdfStrings.ApplyPlanChanged);
            return false;
        }

        // (6) The rewrite.
        var files = plan.Slots
            .Where(slot => slot.Source.Value != 0 && slot.Blank is null)
            .Select(slot => slot.Source)
            .Distinct()
            .ToDictionary(id => id, id => inserted[id].File);
        var overwrite = false;
        while (true)
        {
            PdfDocument? rewritten = null;
            var request = new PageApplyRequest(plan, targetPath, files, overwrite, arrived => rewritten = arrived);

            // The engine is resolved here, on the UI thread: the window creates it on first use.
            var pdf = engine();
            try
            {
                var result = await _jobs.RunAsync(tab, PdfStrings.ApplyTitle, PdfStrings.ApplyJobTitleFormat, (progress, token) => PageEditApplier.ApplyAsync(tab.Session, pdf, request, progress, token, Rewriter));
                if (!result.Adopted || rewritten is null || !ReferenceEquals(tab.Session.Document, rewritten))
                {
                    // The file is written, but the tab does not show it (the session was closed or opened something else meanwhile).
                    return await FailAsync(tab, new PageApplyAdoptException(targetPath, null));
                }

                // (7) The plan is applied.
                Completed(tab, session, plan, targetPath);
                return true;
            }
            catch (SaveFailureException ex) when (ex.Reason == SaveFailureReason.OriginalChanged && !overwrite && choice == ApplyChoice.Original)
            {
                // Another file stands at the path of the original: replace it, write a copy, or stop.
                var name = Path.GetFileName(targetPath);
                var changedChoice = await dialogs.ShowAsync<ChangedChoice>(done => ChangedOriginalCard.Create(name, done, time));
                if (changedChoice == ChangedChoice.Overwrite)
                {
                    overwrite = true;
                }
                else if (changedChoice == ChangedChoice.SaveAs && await pickSavePath(tab) is { } other)
                {
                    targetPath = Path.GetFullPath(other);
                    choice = ApplyChoice.SaveAs;
                }
                else
                {
                    return false;
                }
            }
            catch (SaveFailureException ex) when (ex.Reason == SaveFailureReason.Canceled)
            {
                return false;
            }
        }
    }

    // The tab follows the file, is rebuilt over the adopted document at the page the plan follows, and the plan ends. The file is written: whatever
    // goes wrong from here on (a handler of the window) is no failure of the apply and no card.
    private void Completed(PdfTab tab, OrganizeSession session, PageEditPlan plan, string written)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var page = 0;
        JobRunner.Guarded(() => page = plan.FollowPage(Math.Clamp(currentPage(tab), 0, plan.PrimaryPageCount - 1)));
        var renamed = !string.Equals(Path.GetFullPath(written), tab.Path, comparison);
        if (renamed)
        {
            JobRunner.Guarded(() => tab.Rename(written));
        }

        // Deliberate order: the plan ends first (the pages pane lets go of the inserted files), then the pieces of the tab are made anew over the adopted document.
        JobRunner.Guarded(session.End);
        JobRunner.Guarded(() => rebuild(tab, page));
        if (renamed)
        {
            JobRunner.Guarded(() => pathChanged?.Invoke(tab));
        }
    }

    private Task<bool> FailAsync(PdfTab tab, Exception ex)
    {
        var (title, message) = FailureText(tab, ex);
        return FailedCardAsync(tab.Title, title, message);
    }

    private async Task<bool> FailedCardAsync(string name, string title, string message)
    {
        await dialogs.ShowAsync<bool>(done => SaveFailedCard.Create(name, title, message, () => done(true)));
        return false;
    }

    /// <summary>The title and the sentence of a failure: the reason, and that the file is unchanged and the plan is open (except after an adopt failure).</summary>
    internal static (string Title, string Message) FailureText(PdfTab tab, Exception ex)
    {
        var stays = " " + PdfStrings.ApplyStays;
        var message = ex switch
        {
            PageApplyAdoptException adopt => PdfStrings.Fill(PdfStrings.ApplyFailAdoptFormat, adopt.TargetPath),
            SaveFailureException { Reason: SaveFailureReason.SourceModified, FilePath: { } path } when !JobRunner.SamePath(path, tab.Path) =>
                PdfStrings.Fill(PdfStrings.ApplyFailInsertedModifiedFormat, Path.GetFileName(path)) + stays,
            SaveFailureException { Reason: SaveFailureReason.SourceModified } => PdfStrings.ApplyFailSourceModified + stays,
            SaveFailureException { Reason: SaveFailureReason.EncryptedNotSupported } => PdfStrings.ApplyFailEncrypted + stays,
            SaveFailureException failure => PdfStrings.SaveFailure(failure.Reason) + " " + PdfStrings.ApplyChangesOpen,
            PageEditException => PdfStrings.ApplyFailPlan + stays,
            PdfOpenException => PdfStrings.ApplyFailOpen + stays,
            InvalidDataException => PdfStrings.ApplyFailDamaged + stays,
            _ => PdfStrings.ApplyFailOther + stays,
        };
        return (PdfStrings.ApplyFailedTitle, message);
    }

    private Task<bool> Tell(string message) =>
        dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.CmdApplyPages, message, () => done(true)));
}
