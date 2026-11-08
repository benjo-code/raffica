using System.Globalization;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Organizing;

/// <summary>The parts of a split for the texts of the card, or why there are none (in the UI language).</summary>
/// <param name="Parts">The parts, or null when the texts do not make a split.</param>
/// <param name="Problem">The reason there are no parts, or null.</param>
internal sealed record SplitPlan(IReadOnlyList<SplitPart>? Parts, string? Problem);

/// <summary>
/// "Extract pages" and "Split document": new files from the open document, without the plan (the commands are locked while a plan is open or marks
/// are unsaved, and the flow refuses again). Extract: the range card, the report of Core as a loss list before anything is asked, the save dialog
/// (which confirms an overwrite; <see cref="DocumentSplitter.WriteAsync"/> gets <c>overwrite</c> only for the file the dialog chose), the write.
/// Split: the mode and folder card with the preview, the loss list, the write without replacing anything (a clash is named). Both write through
/// Core (the app never writes a PDF itself) as a cancelable job with progress; the result card lists the written files, and a failed or canceled run
/// names the part and the cause and keeps and lists the parts before it. Nothing here exists until the command runs; nothing escapes to the caller.
/// </summary>
/// <param name="dialogs">Where the cards appear.</param>
/// <param name="pickSavePath">Asks for the file of an extract (tab, suggested name; the system dialog confirms an overwrite); null: canceled.</param>
/// <param name="pickFolder">Asks for the folder of a split; null: canceled.</param>
/// <param name="engine">The engine that opens the working copies and the written files.</param>
/// <param name="currentPage">The 0-based page the user looks at in the tab.</param>
/// <param name="readOutline">Reads the outline of the tab (the one the outline pane reads).</param>
/// <param name="open">Opens a written file as a tab (the open flow).</param>
/// <param name="changed">Called when the flow starts or ends (the command states refresh); null: nothing.</param>
/// <param name="otherJob">Whether another file job of the window runs (a save, an apply, an export): extract and split are then refused; null: never.</param>
internal sealed class SplitFlow(
    DialogQueue dialogs,
    Func<PdfTab, string, Task<string?>> pickSavePath,
    Func<PdfTab, Task<string?>> pickFolder,
    Func<PdfEngine> engine,
    Func<PdfTab, int> currentPage,
    Func<PdfTab, CancellationToken, Task<IReadOnlyList<PdfOutlineItem>>> readOutline,
    Func<string, Task> open,
    Action? changed = null,
    Func<bool>? otherJob = null)
{
    private readonly JobRunner _jobs = new(dialogs);
    private string _jobTitle = string.Empty;
    private string _title = string.Empty;

    /// <summary>The write of Core (<see cref="DocumentSplitter.WriteAsync"/>).</summary>
    internal delegate Task<IReadOnlyList<string>> PartsWriter(
        PdfEngine engine,
        PdfDocument source,
        PageSourceFile file,
        IReadOnlyList<SplitPart> parts,
        string directory,
        bool overwrite,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        ICollection<string>? written);

    /// <summary>Replaces the write of Core (tests make a part fail, or cancel between parts); null: Core's.</summary>
    internal PartsWriter? Writer { get; set; }

    /// <summary>Replaces the read of what the parts keep (<see cref="DocumentLevelContent.ReadAsync"/>); tests make it fail or count calls; null: Core's.</summary>
    internal Func<PdfDocument, string, IReadOnlyList<SplitPart>, CancellationToken, Task<DocumentLevelContent>>? KeptReader { get; set; }

    /// <summary>Replaces the dialog around the job card (tests); null: the normal dialog.</summary>
    internal Func<SaveJobCard, Dialog>? JobDialog
    {
        get => _jobs.JobDialog;
        set => _jobs.JobDialog = value;
    }

    /// <summary>Gets whether a flow is running (from its first card to its last).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Cancels the running job, if any (the window is closing, or the user chose Cancel).</summary>
    public void Cancel() => _jobs.Cancel();

    /// <summary>Runs "Extract pages" for the active document of <paramref name="tab"/>. Returns whether files were written without a stop.</summary>
    public Task<bool> ExtractAsync(PdfTab tab) => RunAsync(tab, PdfStrings.CmdExtractPages, PdfStrings.ExtractTitle, ExtractCoreAsync);

    /// <summary>Runs "Split document" for the active document of <paramref name="tab"/>. Returns whether all files were written.</summary>
    public Task<bool> SplitAsync(PdfTab tab) => RunAsync(tab, PdfStrings.CmdSplitDocument, PdfStrings.SplitTitle, SplitCoreAsync);

    /// <summary>
    /// Works out the parts of a split for the texts of the card; the reason (in the UI language) when they do not make a split. Pure.
    /// </summary>
    internal static SplitPlan PlanFor(int pageCount, SplitModeChoice mode, string every, string ranges, string baseName, IReadOnlyList<PdfOutlineItem> outline)
    {
        try
        {
            SplitMode split;
            switch (mode)
            {
                case SplitModeChoice.Every:
                    if (!int.TryParse(every.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pages) || pages < 1)
                    {
                        return new SplitPlan(null, PdfStrings.SplitEveryInvalid);
                    }

                    split = SplitMode.Every(pages);
                    break;
                case SplitModeChoice.Ranges:
                    split = SplitMode.Ranges(ranges);
                    break;
                default:
                    split = SplitMode.Bookmarks(outline);
                    break;
            }

            return new SplitPlan(DocumentSplitter.Plan(pageCount, split, baseName), null);
        }
        catch (FormatException)
        {
            return new SplitPlan(null, PdfStrings.InsertRangeInvalid);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new SplitPlan(null, PdfStrings.Fill(PdfStrings.InsertRangeOutsideFormat, pageCount));
        }
        catch (PageEditException ex) when (ex.Error == PageEditError.LimitExceeded)
        {
            return new SplitPlan(null, PdfStrings.Fill(PdfStrings.SplitTooManyFormat, DocumentSplitter.MaxParts));
        }
        catch (PageEditException)
        {
            return new SplitPlan(null, PdfStrings.OutputFailRange);
        }
        catch (DocumentSplitException ex) when (ex.Error == DocumentSplitError.NoBookmarks)
        {
            return new SplitPlan(null, PdfStrings.SplitNoBookmarksReason);
        }
        catch (DocumentSplitException)
        {
            return new SplitPlan(null, PdfStrings.OutputFailOther);
        }
    }

    private async Task<bool> RunAsync(PdfTab tab, string title, string jobTitle, Func<PdfTab, Task<bool>> core)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (IsRunning)
        {
            return false;
        }

        if (otherJob?.Invoke() == true)
        {
            await Tell(title, PdfStrings.FileJobBusy);
            return false;
        }

        IsRunning = true;
        try
        {
            JobRunner.Guarded(() => changed?.Invoke());
            _jobTitle = jobTitle;
            _title = title;
            if (await FlushedRefusalAsync(tab) is { } reason)
            {
                await Tell(title, reason);
                return false;
            }

            return await core(tab);
        }
        catch (OperationCanceledException)
        {
            // Canceled by the user (or the tab went away) before anything was written: no card.
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever is not foreseen ends as a card, never as an exception in a command handler.
            await Tell(title, FailureText(ex));
            return false;
        }
        finally
        {
            IsRunning = false;
            JobRunner.Guarded(() => changed?.Invoke());
        }
    }

    // An open field edit or pending nudge is made first (as Print and Apply do), so a value just typed is a mark the lock below sees; then the locks.
    internal static async Task<string?> FlushedRefusalAsync(PdfTab tab)
    {
        if (tab.AnnotationsIfCreated is { } annotations)
        {
            await annotations.FlushAsync();
        }

        return Refusal(tab);
    }

    // The locks are read again after an await that let the user act (a read without a card, a card, a picker); says why and returns false when one holds.
    private async Task<bool> StillAllowedAsync(PdfTab tab)
    {
        if (Refusal(tab) is not { } reason)
        {
            return true;
        }

        await Tell(_title, reason);
        return false;
    }

    // The same locks as the commands: the files are cut from the file on disk, so an open plan or unsaved marks would not be in them.
    internal static string? Refusal(PdfTab tab)
    {
        if (tab.OrganizeIfCreated is { IsActive: true })
        {
            return PdfStrings.PageChangesLockedReason;
        }

        if (tab.AnnotationsIfCreated is { IsDirty: true })
        {
            return PdfStrings.SaveMarksFirstReason;
        }

        return tab.Session.Document is null ? PdfStrings.OutputNoSource : null;
    }

    private async Task<bool> ExtractCoreAsync(PdfTab tab)
    {
        var source = SourceOf(tab);
        var pageCount = source.Document.PageCount;
        var stem = Path.GetFileNameWithoutExtension(tab.Path ?? tab.Title);
        var range = await dialogs.ShowAsync<string?>(done => ExtractCard.Create(tab.Title, pageCount, Math.Clamp(currentPage(tab), 0, pageCount - 1) + 1, stem, done));
        if (string.IsNullOrEmpty(range))
        {
            return false;
        }

        var part = DocumentSplitter.Extract(pageCount, range, stem);
        if (!await ConfirmLossesAsync(tab, source, [part], (lines, stays) => done => ExtractCard.CreateConfirm(range, lines, done, stays)) || !await StillAllowedAsync(tab))
        {
            return false;
        }

        if (await pickSavePath(tab, ExtractCard.SuggestedName(stem, range)) is not { Length: > 0 } picked)
        {
            return false;
        }

        // The write is made through Core, which names its files from a title: the title is the name the user chose, so the file is the chosen one.
        // Only a file the save dialog confirmed (the very path chosen) may be replaced; a name that Core would change is written without replacing.
        var chosen = Path.GetFullPath(picked);
        var folder = Path.GetDirectoryName(chosen) ?? throw new InvalidOperationException("The chosen file has no folder.");
        var named = new SplitPart(Path.GetFileNameWithoutExtension(chosen), part.Pages);
        var target = DocumentSplitter.TargetPaths([named], folder)[0];
        var overwrite = JobRunner.SamePath(target, chosen);
        return await WriteAsync(tab, SourceOf(tab), [named], folder, overwrite, PdfStrings.OutputResultExtractTitle, canOpen: true);
    }

    private async Task<bool> SplitCoreAsync(PdfTab tab)
    {
        var source = SourceOf(tab);
        var pageCount = source.Document.PageCount;
        var stem = Path.GetFileNameWithoutExtension(tab.Path ?? tab.Title);
        IReadOnlyList<PdfOutlineItem> outline;
        try
        {
            outline = await readOutline(tab, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A file whose outline cannot be read has no bookmarks to split by.
            outline = [];
        }

        // The outline was read without a job card: the mode may have started or a mark been made meanwhile.
        if (!await StillAllowedAsync(tab))
        {
            return false;
        }

        var hasBookmarks = PlanFor(pageCount, SplitModeChoice.Bookmarks, "1", "all", stem, outline).Parts is not null;
        var choice = await dialogs.ShowAsync<SplitChoice?>(done => SplitCard.Create(
            tab.Title,
            pageCount,
            hasBookmarks,
            (mode, every, ranges) => PlanFor(pageCount, mode, every, ranges, stem, outline),
            () => pickFolder(tab),
            done));
        if (choice is not { } chosen)
        {
            return false;
        }

        var plan = PlanFor(pageCount, chosen.Mode, chosen.Every, chosen.Ranges, stem, outline);
        if (plan.Parts is not { } parts)
        {
            await Tell(PdfStrings.CmdSplitDocument, plan.Problem ?? PdfStrings.OutputFailOther);
            return false;
        }

        if (!await ConfirmLossesAsync(tab, source, parts, (lines, stays) => done => SplitCard.CreateConfirm(parts.Count, lines, done, stays)))
        {
            return false;
        }

        return await WriteAsync(tab, SourceOf(tab), parts, chosen.Folder, overwrite: false, PdfStrings.OutputResultSplitTitle, canOpen: false);
    }

    // The report of Core for the parts (a cancelable job); the card with the loss list only when there is something to say. Returns whether to go on.
    private async Task<bool> ConfirmLossesAsync(PdfTab tab, SplitSource source, IReadOnlyList<SplitPart> parts, Func<IReadOnlyList<string>, IReadOnlyList<string>, Func<Action<bool>, Dialog>> confirm)
    {
        // The same job reads what every part keeps from the document level (only now, when the card is about to open; nothing of it exists before).
        var (reports, content) = await _jobs.RunAsync(tab, _jobTitle, PdfStrings.OutputCheckingTitleFormat, async (_, token) =>
            (await DocumentSplitter.AnalyzeAsync(source.Document, parts, token), await ReadKeptAsync(source, parts, token)));

        // Nothing is written for a part that Core refuses (it takes no pages from other files, so none does today).
        if (reports.Any(static report => report.IsRefused))
        {
            await Tell(PdfStrings.CmdSplitDocument, PageLossWording.RefusalOf(reports.First(static report => report.IsRefused)));
            return false;
        }

        var lines = PageLossWording.DescribeParts(reports);
        var stays = DocumentLevelWording.Lines(content);
        return lines.Count + stays.Count == 0 || await dialogs.ShowAsync<bool>(done => confirm(lines, stays)(done));
    }

    // What the parts keep from the document level; a read that fails says nothing (the card then shows the losses as before), a cancel is the user's.
    private async Task<DocumentLevelContent> ReadKeptAsync(SplitSource source, IReadOnlyList<SplitPart> parts, CancellationToken token)
    {
        try
        {
            return await (KeptReader ?? DocumentLevelContent.ReadAsync)(source.Document, source.File.Path, parts, token);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            return DocumentLevelContent.None;
        }
    }

    private async Task<bool> WriteAsync(PdfTab tab, SplitSource source, IReadOnlyList<SplitPart> parts, string folder, bool overwrite, string title, bool canOpen)
    {
        // Cards, a picker and a check job came since the locks were read: the files are cut from the file on disk, so they are read again before the write.
        if (!await StillAllowedAsync(tab))
        {
            return false;
        }

        var written = new List<string>();
        Exception? failure = null;
        var canceled = false;
        try
        {
            var write = Writer ?? new PartsWriter(DocumentSplitter.WriteAsync);

            // The engine is resolved here, on the UI thread: the window creates it on first use.
            var pdf = engine();
            await _jobs.RunAsync(tab, _jobTitle, PdfStrings.OutputJobTitleFormat, (progress, token) => write(pdf, source.Document, source.File, parts, folder, overwrite, progress, token, written));
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = ex;
        }

        var files = written.ToArray();
        var status = files.Length switch
        {
            0 => PdfStrings.OutputNothingWritten,
            1 => PdfStrings.OutputWrittenOne,
            _ => PdfStrings.Fill(PdfStrings.OutputWrittenFormat, files.Length),
        };
        var reason = canceled ? PdfStrings.OutputCanceled : failure is null ? null : FailureText(failure);
        var shownFolder = files.Length > 0 ? Path.GetDirectoryName(files[0]) : folder;
        var report = new PartsReport(title, status, reason, files, shownFolder, canOpen && reason is null && files.Length == 1);
        var wantsOpen = await dialogs.ShowAsync<bool>(done => PartsResultCard.Create(report, done));
        if (wantsOpen && report.CanOpen)
        {
            await open(files[0]);
        }

        return reason is null;
    }

    private static SplitSource SourceOf(PdfTab tab) =>
        DocumentSplitter.SourceOf(tab.Session) ?? throw new InvalidOperationException(PdfStrings.OutputNoSource);

    /// <summary>The sentence for why an extract or a split stopped (the reason, and what was or was not written).</summary>
    internal static string FailureText(Exception ex) => ex switch
    {
        DocumentSplitException split => split.Error switch
        {
            DocumentSplitError.NoBookmarks => PdfStrings.OutputFailNoBookmarks,
            DocumentSplitError.DirectoryMissing => PdfStrings.OutputFailDirectoryMissing,
            DocumentSplitError.TargetExists => PdfStrings.Fill(PdfStrings.OutputFailTargetExistsFormat, NamesOf(split.Paths)),
            DocumentSplitError.TargetIsSource => PdfStrings.OutputFailTargetIsSource,
            DocumentSplitError.UnsafeName => PdfStrings.OutputFailUnsafeName,
            DocumentSplitError.PartFailed => PartFailedText(split),
            _ => PdfStrings.OutputFailOther,
        },
        PageEditException { Error: PageEditError.LimitExceeded } => PdfStrings.OutputFailLimit,
        PageEditException or FormatException or ArgumentOutOfRangeException => PdfStrings.OutputFailRange,
        SaveFailureException save => CauseOf(save),
        _ => PdfStrings.OutputFailOther,
    };

    private static string PartFailedText(DocumentSplitException split)
    {
        var name = split.Paths.Count > 0 ? Path.GetFileName(split.Paths[0]) : string.Empty;
        var cause = split.InnerException switch
        {
            SaveFailureException save => CauseOf(save),
            PdfOpenException => PdfStrings.OutputCauseOpen,
            InvalidDataException => PdfStrings.OutputCauseDamaged,
            IOException or UnauthorizedAccessException => CauseOf(new SaveFailureException(SaveFailureReason.NotWritable, "not writable")),
            _ => PdfStrings.OutputFailOther,
        };
        return PdfStrings.Fill(PdfStrings.OutputFailPartFormat, split.PartIndex + 1, name, cause);
    }

    private static string CauseOf(SaveFailureException save)
    {
        try
        {
            return PdfStrings.SaveFailure(save.Reason);
        }
        catch (ArgumentOutOfRangeException)
        {
            return PdfStrings.OutputFailOther;
        }
    }

    // The names of the files in the way: the first five, then the count of the rest.
    internal static string NamesOf(IReadOnlyList<string> paths)
    {
        var names = paths.Select(Path.GetFileName).Take(5);
        var text = string.Join(", ", names);
        return paths.Count > 5 ? text + ", " + PdfStrings.Fill(PdfStrings.OutputMoreFormat, paths.Count - 5) : text;
    }

    private Task<bool> Tell(string title, string message) =>
        dialogs.ShowAsync<bool>(done => NoticeCard.Create(title, message, () => done(true)));
}
