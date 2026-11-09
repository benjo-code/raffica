using Avalonia.Threading;
using Raffica.Core;
using Raffica.Core.Settings;
using Raffica.Core.Text;
using Raffica.Core.Updates;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Settings;
using Raffica.Pdf.Text;
using Raffica.Pdf.Updating;
using Raffica.Shell.Frame;

namespace Raffica.Pdf;

/// <summary>
/// The update check and installation (M2.6). Nothing of it exists before it is used: the flow (and with it the source, whose HTTP client is made only
/// by the first request) is made by the first check after the first sharp page, only while the setting allows it and only where the app gave the window a
/// factory, or by the command "Check for updates". With the setting off nothing is made and no connection is opened, until the user asks.
/// </summary>
internal sealed partial class PdfWindow
{
    private UpdateFlow? _updateFlow;
    private UpdateInstallGuard? _updateGuard;
    private CancellationTokenSource? _updateCancel;
    private bool _updateCheckStarted;
    private bool _updateBusy;
    private UpdateButton? _updateButton;

    /// <summary>
    /// What makes the update flow (it gets "is the background check switched on"). The app sets it (<see cref="UpdateServices.Create"/>, not in a measurement run);
    /// while it is null the window checks nothing and the command does nothing, so a test window never touches the network. Called on first use only.
    /// </summary>
    internal Func<Func<bool>, UpdateFlow>? UpdateFlows { get; set; }

    /// <summary>Raised when the state of the update flow changed (the title bar button follows it).</summary>
    internal event Action<UpdateState>? UpdateStateChanged;

    /// <summary>The title bar button of an offer, made together with the flow (null before the first check or command); tests click it.</summary>
    internal UpdateButton? UpdateButton => _updateButton;

    /// <summary>Whether the flow exists (it is made by the first check or command; for tests).</summary>
    internal bool UpdateFlowCreated => _updateFlow is not null;

    /// <summary>Where the update is; nothing newer is known while the flow does not exist.</summary>
    internal UpdateState UpdateState => _updateFlow?.State ?? UpdateState.None;

    private UpdateFlow? UpdateFlow
    {
        get
        {
            if (_updateFlow is null && UpdateFlows is { } make)
            {
                _updateFlow = make(() => Settings.App.UpdateCheck);
                _updateFlow.Changed += state => UpdateStateChanged?.Invoke(state);
                _updateGuard = new UpdateInstallGuard(_closeGuard);

                // The helper window of the installer opens where this window is (a maximized or minimized window is no place for a small one: it centres itself).
                _updateFlow.ProgressWindowBounds = () => WindowState == Avalonia.Controls.WindowState.Normal ? (Position.X, Position.Y, ClientSize.Width, ClientSize.Height) : null;

                // The title bar button follows the flow; it is made now because the flow is, never at startup.
                _updateButton = new UpdateButton(OpenUpdateOffer);
                _updateButton.Apply(_updateFlow.State);
                _updateFlow.Changed += _updateButton.Apply;
                if (Frame.TitleBar is TitleBar bar)
                {
                    bar.Trailing = _updateButton;
                }
            }

            return _updateFlow;
        }
    }

    /// <summary>
    /// Whether a job that works on a file runs (a save, a print, Apply, Redact, Extract, Split, Export images or a recognition): an update does not start the
    /// installer meanwhile. Tests replace it; null asks the window's own jobs.
    /// </summary>
    internal Func<bool>? UpdateBusy { get; set; }

    private bool FileJobRunning() => UpdateBusy?.Invoke() ?? (BusyWithFileJob() || OcrBusy);

    private CancellationToken UpdateToken => (_updateCancel ??= new CancellationTokenSource()).Token;

    /// <summary>Ends what runs of the update flow when the window closes: a check or download is cancelled and a staged update nobody installed is deleted.</summary>
    private void EndUpdates()
    {
        _updateCancel?.Cancel();
        _updateFlow?.Dispose();
    }

    /// <summary>
    /// After the first sharp page: checks once per run when the setting asks (at most one real check in 24 hours; the checker decides). Nothing is made in a measurement
    /// run, while the setting is off or without a factory. The check runs on the pool and fails silently.
    /// </summary>
    internal async Task CheckForUpdateAtStartAsync()
    {
        if (_updateCheckStarted || UpdateServices.MeasurementKeepsUpdatesOff(_options) || UpdateFlows is null || !Settings.App.UpdateCheck)
        {
            return;
        }

        _updateCheckStarted = true;

        // Behind the work of the page that was just drawn: the first sharp page is never delayed by the check.
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        if (UpdateFlow is { } flow)
        {
            await flow.CheckOnStartAsync(UpdateToken);
        }
    }

    /// <summary>
    /// The command "Check for updates": with an offer already known it shows the offer, otherwise it checks now (also with the background check switched off, the user
    /// asked) and says what it found: an offer, "up to date", or one note "could not check".
    /// </summary>
    internal void CheckForUpdates()
    {
        if (UpdateServices.MeasurementKeepsUpdatesOff(_options) || UpdateFlows is null || _updateBusy)
        {
            return;
        }

        _ = GuardAsync(CheckForUpdatesAsync);
    }

    // A click on the title bar button: the card of the offer that is known (it does not ask the network again).
    private void OpenUpdateOffer()
    {
        if (_updateFlow is { State.Stage: UpdateStage.Available or UpdateStage.Ready })
        {
            CheckForUpdates();
        }
    }

    internal async Task CheckForUpdatesAsync()
    {
        if (_updateBusy || UpdateFlow is not { } flow)
        {
            return;
        }

        _updateBusy = true;
        try
        {
            var outcome = flow.State.Stage is UpdateStage.Available or UpdateStage.Ready
                ? UpdateCheckOutcome.Available
                : await flow.CheckNowAsync(UpdateToken);
            switch (outcome)
            {
                case UpdateCheckOutcome.Available:
                    await ShowUpdateOfferAsync(flow);
                    break;
                case UpdateCheckOutcome.UpToDate:
                    await ShowUpdateNoteAsync(PdfStrings.Fill(PdfStrings.UpdateUpToDateFormat, AppInfo.Version));
                    break;
                default:
                    await ShowUpdateNoteAsync(PdfStrings.UpdateCouldNotCheck);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The window closes: nothing more to show.
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private Task<bool> ShowUpdateNoteAsync(string text) =>
        Dialogs.ShowAsync<bool>(done => NoticeCard.Create(PdfStrings.UpdateCheckTitle, text, () => done(true)));

    private async Task ShowUpdateOfferAsync(UpdateFlow flow)
    {
        if (flow.State.Version is not { } version)
        {
            return;
        }

        var choice = await Dialogs.ShowAsync<UpdateChoice>(done => UpdateCard.Create(version.ToString(), AppInfo.Version, flow.CanInstall, done, _time));
        switch (choice)
        {
            case UpdateChoice.Install:
                await InstallUpdateAsync(flow);
                break;
            case UpdateChoice.OpenDownloadPage:
                await OpenDownloadPageAsync();
                break;
        }
    }

    /// <summary>
    /// "Install and restart": download and check the file (the progress card), then ask about the unsaved work of every tab, write the session, and only then hand the
    /// staged update to the installer. A Cancel at the question keeps every tab and the staged file as they were (the next "Install" uses the file again, checked again).
    /// </summary>
    private async Task InstallUpdateAsync(UpdateFlow flow)
    {
        // A running save, print or other file job is not cut off by the restart: nothing is downloaded or asked while one runs.
        if (FileJobRunning())
        {
            await ShowUpdateNoteAsync(PdfStrings.UpdateBusyNote);
            return;
        }

        while (true)
        {
            var failedWith = await DownloadWithCardAsync(flow);
            if (failedWith is { } choice)
            {
                flow.Dismiss();
                if (choice == UpdateProgressChoice.TryAgain)
                {
                    continue;
                }

                if (choice == UpdateProgressChoice.OpenDownloadPage)
                {
                    await OpenDownloadPageAsync();
                }

                return;
            }

            if (flow.State.Stage != UpdateStage.Ready || _updateGuard is not { } guard)
            {
                return;
            }

            // A job may have started during the download: asked again before anything is written.
            if (FileJobRunning())
            {
                await ShowUpdateNoteAsync(PdfStrings.UpdateBusyNote);
                return;
            }

            // The question, with nothing written before its last answer; the session of the restart is written right after it.
            if (!await guard.AskAllTabsAsync(Workspace.Tabs.ToList(), WriteSession))
            {
                return;
            }

            // And once more right before the installer (a job may have started while the questions were open or the saves ran): the session is taken back, the staged file stays.
            if (FileJobRunning())
            {
                new SessionStore(SessionPath()).Delete();
                await ShowUpdateNoteAsync(PdfStrings.UpdateBusyNote);
                return;
            }

            var installFailure = await InstallWithCardAsync(flow);
            if (installFailure is null)
            {
                return;
            }

            flow.Dismiss();
            if (installFailure == UpdateProgressChoice.TryAgain)
            {
                continue;
            }

            if (installFailure == UpdateProgressChoice.OpenDownloadPage)
            {
                await OpenDownloadPageAsync();
            }

            return;
        }
    }

    // The progress card in the queue of cards. Closing it is asked through the returned callback at any time: when the flow ends while the card still waits behind
    // another card, the request is kept and applied the moment the card is shown, so no card is left on screen with Cancel disabled and no way out.
    private (UpdateProgressCard Card, Task<UpdateProgressChoice> Shown, Action<UpdateProgressChoice> Close, Action Detach) ShowProgressCard(UpdateFlow flow)
    {
        Action<UpdateProgressChoice>? close = null;
        UpdateProgressChoice? requested = null;

        void RequestClose(UpdateProgressChoice choice)
        {
            if (close is { } done)
            {
                done(choice);
            }
            else
            {
                requested ??= choice;
            }
        }

        var card = new UpdateProgressCard(flow.Cancel, RequestClose);
        void Follow(UpdateState state) => card.Apply(state, flow.FailureMessage, flow.CanRetry, flow.FailureOffersDownloadPage);
        card.Apply(flow.State, flow.FailureMessage, flow.CanRetry, flow.FailureOffersDownloadPage);
        var shown = Dialogs.ShowAsync<UpdateProgressChoice>(done =>
        {
            close = done;
            if (requested is { } early)
            {
                // Shown first, closed right after: the queue shows a card by handing it to the host, so the answer comes in the next turn.
                Dispatcher.UIThread.Post(() => done(early));
            }

            return card.Dialog;
        });
        flow.Changed += Follow;
        return (card, shown, RequestClose, () => flow.Changed -= Follow);
    }

    // The download and its check with the progress card. Returns null when the update is ready (or the user cancelled), else what the user chose after a failure.
    private async Task<UpdateProgressChoice?> DownloadWithCardAsync(UpdateFlow flow)
    {
        var (_, shown, close, detach) = ShowProgressCard(flow);
        try
        {
            await flow.DownloadAndStageAsync(UpdateToken);
            if (flow.State.Stage != UpdateStage.Failed)
            {
                close(UpdateProgressChoice.Close);
                await shown;
                return null;
            }

            // The card shows the failure; the user closes it, tries again or opens the download page.
            return await shown;
        }
        finally
        {
            detach();
        }
    }

    // The installer's step with the progress card; null when it took over (the app ends) or failed without anything to choose.
    private async Task<UpdateProgressChoice?> InstallWithCardAsync(UpdateFlow flow)
    {
        var (_, shown, close, detach) = ShowProgressCard(flow);
        try
        {
            var result = await flow.InstallAsync(UpdateToken);
            if (result.IsRestarting)
            {
                // The installer took over and ends this process; the card stays until it does.
                return null;
            }

            // The app goes on running: the session written for the restart must not stay behind for a later restart to find.
            new SessionStore(SessionPath()).Delete();
            if (flow.State.Stage != UpdateStage.Failed)
            {
                close(UpdateProgressChoice.Close);
                await shown;
                return null;
            }

            return await shown;
        }
        finally
        {
            detach();
        }
    }

    /// <summary>
    /// Ends the app for an installer that took over (the update flow asked about every unsaved tab before it called the installer): the window will not ask again, and
    /// <paramref name="shutdown"/> ends the application on the UI thread. Never throws.
    /// </summary>
    internal void EndForUpdate(Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(shutdown);
        _closeConfirmed = true;
        Dispatcher.Post(shutdown);
    }

    private string SessionPath() => Path.Combine(PdfSettingsFiles.ConfigDirectoryFor(_options.SettingsFile), SessionStore.FileName);

    // The session of the restart: the open files with their view, the active tab. Written only after the last answer of the question about unsaved tabs.
    private void WriteSession()
    {
        var tabs = Workspace.Tabs.Where(tab => !string.IsNullOrEmpty(tab.Path)).ToList();
        if (tabs.Count == 0)
        {
            // Nothing to restore: a session file of an earlier attempt must not come back.
            new SessionStore(SessionPath()).Delete();
            return;
        }

        // Replaced or removed, never left from an earlier attempt: if this write fails, an old file must not be restored by mistake.
        new SessionStore(SessionPath()).Delete();
        var entries = tabs.Select(tab => new SessionTab(tab.Path!, ViewOf(tab)?.CaptureRemembered())).ToList();
        var active = Math.Max(0, tabs.IndexOf(Workspace.Active!));
        _ = new SessionStore(SessionPath()).TrySave(new SessionSnapshot([new SessionWindow(entries, active)]));
    }

    private async Task OpenDownloadPageAsync() => _ = await OpenAddressAfterConfirmationAsync(UpdateTrust.ReleasesPageUrl);

    // An address of the app (the download page, the project page of the About card) opens only after the card that shows it in full (the same card as for a link in a document).
    // The answer is false only when the user declined the question (the About card then comes back).
    internal async Task<bool> OpenAddressAfterConfirmationAsync(string address)
    {
        if (!ExternalLink.TryParse(address, out var link))
        {
            return true;
        }

        var confirmed = await Dialogs.ShowAsync<bool>(done => LinkCard.Create(link, () => done(true), () => done(false), _time));
        if (!confirmed)
        {
            return false;
        }

        try
        {
            (_launcher ?? SystemLauncher.Open)(new Uri(link.Url, UriKind.Absolute));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // No browser, or the system refused: the address is on the card.
        }

        return true;
    }
}
