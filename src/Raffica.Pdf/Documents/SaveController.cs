using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;

namespace Raffica.Pdf.Documents;

/// <summary>Writes the changes of a tab; the seam over <see cref="AnnotationEditor.SaveAsync"/> that lets tests inject failures.</summary>
internal delegate Task<AnnotationSaveResult> SaveRunner(PdfTab tab, string target, bool overwriteChangedOriginal, IProgress<double> progress, CancellationToken cancellationToken);

/// <summary>
/// Save and Save as. Both end the open drafts first (<see cref="Annotating.AnnotationSession.FlushAsync"/>), ask about a signed file
/// (<see cref="SignaturePolicy"/>), run the incremental save of Core with a job card (progress, Cancel) and turn every failure into a card:
/// nothing escapes to the caller. The dirty state of the tab after a save follows the annotation session, so a draft that could not be made
/// keeps the tab dirty. One save at a time; a save and a print job refuse each other.
/// </summary>
internal sealed class SaveController
{
    private readonly DialogQueue _dialogs;
    private readonly Func<PdfTab, Task<string?>> _pickSavePath;
    private readonly SaveRunner _runner;
    private readonly Action<Control?>? _setJobSlot;
    private readonly Func<bool>? _printRunning;
    private readonly TimeProvider? _time;
    private readonly ConditionalWeakTable<PdfTab, NotedDocument> _noted = [];
    private CancellationTokenSource? _running;

    /// <param name="dialogs">Where the cards appear.</param>
    /// <param name="pickSavePath">Asks for the target of Save as (the system dialog confirms an overwrite); null: the user canceled.</param>
    /// <param name="engine">The engine that opened the files (it opens the written file for the check); asked when a save runs.</param>
    /// <param name="setJobSlot">Puts the job card into the frame (null: no card is shown).</param>
    /// <param name="printRunning">Whether a print job runs (a save is then refused).</param>
    /// <param name="time">The clock of the arming delay of the cards (tests move it).</param>
    /// <param name="runner">Replaces the save of Core (tests); null: <see cref="AnnotationEditor.SaveAsync"/> of the tab.</param>
    public SaveController(
        DialogQueue dialogs,
        Func<PdfTab, Task<string?>> pickSavePath,
        Func<PdfEngine> engine,
        Action<Control?>? setJobSlot = null,
        Func<bool>? printRunning = null,
        TimeProvider? time = null,
        SaveRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(pickSavePath);
        ArgumentNullException.ThrowIfNull(engine);
        _dialogs = dialogs;
        _pickSavePath = pickSavePath;
        _setJobSlot = setJobSlot;
        _printRunning = printRunning;
        _time = time;
        _runner = runner ?? ((tab, target, overwrite, progress, token) => tab.Annotations.Editor.SaveAsync(engine(), target, overwrite, progress, token));
    }

    /// <summary>Raised on the UI thread when <see cref="IsSaving"/> changes or a save changed a tab (its path, its dirty state).</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread after Save as gave a tab another path (the recent list follows).</summary>
    public event Action<PdfTab>? PathChanged;

    /// <summary>Gets whether a save is running.</summary>
    public bool IsSaving { get; private set; }

    /// <summary>Cancels the running save, if any (the window is closing).</summary>
    public void Cancel()
    {
        try
        {
            _running?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The save ended meanwhile.
        }
    }

    /// <summary>Saves the tab into its own file. Returns whether the changes are saved (a clean tab counts as saved).</summary>
    public Task<bool> SaveAsync(PdfTab tab) => RunAsync(tab, askForTarget: false);

    /// <summary>Saves the tab under a path the user picks; the tab then belongs to that file. Returns whether the changes are saved.</summary>
    public Task<bool> SaveAsAsync(PdfTab tab) => RunAsync(tab, askForTarget: true);

    private async Task<bool> RunAsync(PdfTab tab, bool askForTarget)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (IsSaving)
        {
            return false;
        }

        if (_printRunning?.Invoke() == true)
        {
            _ = Tell(PdfStrings.PrintBusy);
            return false;
        }

        SetSaving(true);
        try
        {
            return await SaveCoreAsync(tab, askForTarget);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever is not foreseen ends as a card, never as an exception in a command handler.
            await ShowFailureAsync(tab, SaveFailureReason.WriteFailed);
            return false;
        }
        finally
        {
            SetSaving(false);
        }
    }

    private async Task<bool> SaveCoreAsync(PdfTab tab, bool askForTarget)
    {
        // Pending nudges and valid drafts are made now; the dirty state of the tab is current afterwards.
        await tab.Annotations.FlushAsync();
        if (tab.Session.Document is not { } document)
        {
            return false;
        }

        if (!askForTarget && !tab.IsDirty)
        {
            return true;
        }

        var advice = SignaturePolicy.Evaluate(await document.GetSignatureInfoAsync());
        var target = askForTarget ? null : tab.Path;
        var decided = false;
        while (true)
        {
            if (askForTarget)
            {
                target = await _pickSavePath(tab);
                if (target is null)
                {
                    return false;
                }

                target = Path.GetFullPath(target);
            }

            if (target is null)
            {
                return false;
            }

            if (decided)
            {
                break;
            }

            var choice = await AskSignedAsync(tab, document, advice);
            if (choice == SignedChoice.Cancel)
            {
                return false;
            }

            if (choice != SignedChoice.SaveAs)
            {
                break;
            }

            // The user chose a copy: pick the path, then save without asking about the signature again.
            askForTarget = true;
            decided = true;
        }

        return await WriteAsync(tab, target, askForTarget);
    }

    private async Task<SignedChoice> AskSignedAsync(PdfTab tab, PdfDocument document, SignatureAdvice advice)
    {
        if (advice == SignatureAdvice.None)
        {
            return SignedChoice.Continue;
        }

        if (advice == SignatureAdvice.AppendedOnly && _noted.TryGetValue(tab, out var noted) && ReferenceEquals(noted.Document, document))
        {
            return SignedChoice.Continue;
        }

        var name = tab.Title;
        var choice = await _dialogs.ShowAsync<SignedChoice>(done => SignedFileCard.Create(name, advice, done, _time));
        if (advice == SignatureAdvice.AppendedOnly && choice == SignedChoice.Continue)
        {
            _noted.AddOrUpdate(tab, new NotedDocument(document));
        }

        return choice;
    }

    private async Task<bool> WriteAsync(PdfTab tab, string target, bool saveAs)
    {
        var overwrite = false;
        var before = tab.Session.Document;
        while (true)
        {
            try
            {
                await RunJobAsync(tab, target, overwrite);
                break;
            }
            catch (OperationCanceledException)
            {
                // Canceled by the user (or the tab went away): no card; the tab stays dirty with its history.
                return false;
            }
            catch (SaveFailureException ex) when (ex.Reason == SaveFailureReason.OriginalChanged && !overwrite)
            {
                var name = Path.GetFileName(target);
                var choice = await _dialogs.ShowAsync<ChangedChoice>(done => ChangedOriginalCard.Create(name, done, _time));
                if (choice == ChangedChoice.Overwrite)
                {
                    overwrite = true;
                }
                else if (choice == ChangedChoice.SaveAs)
                {
                    if (await _pickSavePath(tab) is not { } other)
                    {
                        return false;
                    }

                    target = Path.GetFullPath(other);
                    saveAs = true;
                }
                else
                {
                    return false;
                }
            }
            catch (SaveFailureException ex)
            {
                await ShowFailureAsync(tab, ex.Reason);
                return false;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await ShowFailureAsync(tab, SaveFailureReason.WriteFailed);
                return false;
            }
        }

        // Core adopted the written file (a new document object): the note shown for the old one counts for the new one.
        if (_noted.TryGetValue(tab, out var noted) && ReferenceEquals(noted.Document, before) && tab.Session.Document is { } adopted)
        {
            _noted.AddOrUpdate(tab, new NotedDocument(adopted));
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (saveAs && !string.Equals(Path.GetFullPath(target), tab.Path, comparison))
        {
            tab.Rename(target);
            PathChanged?.Invoke(tab);
        }

        // The tab is clean unless the session still holds something unsaved (a text box draft that was refused stays dirty).
        tab.Annotations.Refresh();
        Changed?.Invoke();
        return true;
    }

    private async Task RunJobAsync(PdfTab tab, string target, bool overwrite)
    {
        using var cancellation = new CancellationTokenSource();
        _running = cancellation;
        var card = new SaveJobCard(tab.Title, cancellation.Cancel);
        _setJobSlot?.Invoke(card);

        // A tab that closes under the save takes its session with it: the save is canceled first.
        void OnTabDisposing()
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The save ended meanwhile.
            }
        }

        tab.Disposing += OnTabDisposing;
        try
        {
            await _runner(tab, target, overwrite, new UiProgress(card), cancellation.Token);
        }
        finally
        {
            tab.Disposing -= OnTabDisposing;
            _running = null;
            _setJobSlot?.Invoke(null);
        }
    }

    private Task<bool> ShowFailureAsync(PdfTab tab, SaveFailureReason reason)
    {
        var name = tab.Title;

        // The original is unchanged, the tab is still dirty with its history; the card is only the news.
        return _dialogs.ShowAsync<bool>(done => SaveFailedCard.Create(name, reason, () => done(true)));
    }

    private Task<bool> Tell(string message) =>
        _dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.CmdSave, message, () => done(true)));

    private void SetSaving(bool saving)
    {
        IsSaving = saving;
        Changed?.Invoke();
    }

    private sealed record NotedDocument(PdfDocument Document);

    /// <summary>Takes the progress from the PDFium thread and hands it to the card on the UI thread.</summary>
    private sealed class UiProgress(SaveJobCard card) : IProgress<double>
    {
        public void Report(double value) => Dispatcher.UIThread.Post(() => card.Report(value));
    }
}
