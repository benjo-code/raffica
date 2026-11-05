using Avalonia.Threading;
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
    TimeProvider? time = null)
{
    private CancellationTokenSource? _running;

    /// <summary>Replaces the rewrite of Core (tests make it fail in ways the saver does not, or block it); null: <see cref="DocumentSaver"/>.</summary>
    internal Func<PdfDocument, PdfEngine, RewriteRequest, IProgress<double>?, CancellationToken, Task<SaveResult>>? Rewriter { get; set; }

    /// <summary>Gets whether an apply is running (from its first question to its last card).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Cancels the running job, if any (the window is closing).</summary>
    public void Cancel() => TryCancel(_running);

    /// <summary>Runs the apply for the plan that is open on <paramref name="tab"/>. Returns whether the plan was applied.</summary>
    public async Task<bool> RunAsync(PdfTab tab, ApplyTarget target = ApplyTarget.Choose)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (IsRunning || tab.OrganizeIfCreated is not { IsActive: true } session || session.Plan.IsIdentity || tab.Session.Document is null)
        {
            return false;
        }

        if (busy?.Invoke() == true)
        {
            await dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.CmdApplyPages, PdfStrings.PrintBusy, () => done(true)));
            return false;
        }

        IsRunning = true;
        changed?.Invoke();
        try
        {
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
            Guarded(() => changed?.Invoke());
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

            // The save cleared the dirty state its own session had set; the plan is still open.
            tab.MarkDirty();
        }

        var plan = session.Plan;
        var inserted = session.Inserted;
        string Name(int id) => inserted.TryGetValue(new PageSourceId(id), out var file) ? file.Name : id.ToString(System.Globalization.CultureInfo.CurrentCulture);

        // (3) What the plan loses; the documents are the ones of the session (the primary one as the session shows it now).
        var document = tab.Session.Document ?? throw new InvalidOperationException("No document is open.");
        var opened = inserted.ToDictionary(pair => pair.Key, pair => pair.Value.Document);
        var report = await RunJobAsync(tab, PdfStrings.ApplyCheckingTitleFormat, (_, token) => PageEditExecutor.AnalyzeAsync(plan, document, opened, token));

        // (4) The card. A refused report only says why.
        var summary = session.Summary;
        var choice = await dialogs.ShowAsync<ApplyChoice>(done => ApplyCard.Create(tab.Title, summary, report, id => Name(id), target, done, time));
        if (choice == ApplyChoice.Back || report.IsRefused || !session.IsActive)
        {
            return false;
        }

        // (5) The target. "Save as" never preselects the name of the open file; the system dialog confirms replacing another file.
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
            try
            {
                var result = await RunJobAsync(tab, PdfStrings.ApplyJobTitleFormat, (progress, token) => PageEditApplier.ApplyAsync(tab.Session, engine(), request, progress, token, Rewriter));
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
        Guarded(() => page = plan.FollowPage(Math.Clamp(currentPage(tab), 0, plan.PrimaryPageCount - 1)));
        var renamed = !string.Equals(Path.GetFullPath(written), tab.Path, comparison);
        if (renamed)
        {
            Guarded(() => tab.Rename(written));
        }

        // The plan ends first (the pages pane lets go of the inserted files), then the pieces of the tab are made anew over the adopted document.
        Guarded(session.End);
        Guarded(() => rebuild(tab, page));
        if (renamed)
        {
            Guarded(() => pathChanged?.Invoke(tab));
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
            SaveFailureException { Reason: SaveFailureReason.SourceModified, FilePath: { } path } when !SamePath(path, tab.Path) =>
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

    private static bool SamePath(string a, string? b) =>
        b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private Task<bool> Tell(string message) =>
        dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.CmdApplyPages, message, () => done(true)));

    // A job on a pool thread behind a dialog with the job card (progress, Cancel): the window is blocked meanwhile, so the plan cannot change.
    // Canceling, the tab closing or the window closing cancel the token.
    private async Task<T> RunJobAsync<T>(PdfTab tab, string titleFormat, Func<IProgress<double>, CancellationToken, Task<T>> work)
    {
        using var cancellation = new CancellationTokenSource();
        _running = cancellation;
        var card = new SaveJobCard(tab.Title, () => TryCancel(cancellation), titleFormat);
        Action<bool>? close = null;
        var finished = false;
        var shown = dialogs.ShowAsync<bool>(done =>
        {
            // A card that was waiting in the queue while the job already ended is not shown.
            if (finished)
            {
                throw new OperationCanceledException();
            }

            close = done;
            var dialog = new Dialog { Title = PdfStrings.ApplyTitle, Content = card };
            dialog.CancelRequested += (_, _) => card.RequestCancel();
            return dialog;
        });

        void OnTabDisposing() => TryCancel(cancellation);
        tab.Disposing += OnTabDisposing;
        try
        {
            return await Task.Run(() => work(new UiProgress(card), cancellation.Token));
        }
        finally
        {
            tab.Disposing -= OnTabDisposing;
            _running = null;
            finished = true;
            close?.Invoke(true);
            try
            {
                await shown;
            }
            catch (OperationCanceledException)
            {
                // The card never appeared.
            }
        }
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The job ended meanwhile.
        }
    }

    private static void Guarded(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The file is written; a step after it that fails must not turn into the card "the file is unchanged".
        }
    }

    /// <summary>Takes the progress from the PDFium thread and hands it to the card on the UI thread.</summary>
    private sealed class UiProgress(SaveJobCard card) : IProgress<double>
    {
        private double _last;

        public void Report(double value)
        {
            // The bar never goes back, whatever order the steps of the rewrite report in.
            double shown;
            lock (this)
            {
                shown = _last = Math.Max(_last, value);
            }

            Dispatcher.UIThread.Post(() => card.Report(shown));
        }
    }
}
