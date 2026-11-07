using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Settings;
using Raffica.Core.Tests.Updates;
using Raffica.Core.Updates;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class UpdateWindowTests
{
    private static Button Find(WindowFixture f, string name) =>
        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Action<string> UpdateCheck(bool on) => folder =>
        Assert.True(new SettingsStore(Path.Combine(folder, "pdf-settings.json")).TrySave(AppSettings.Default with { UpdateCheck = on }));

    [AvaloniaFact]
    public async Task TheCommandChecksAndOffersTheUpdateWithTheCardOfTheForm()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        using var f = WindowFixture.Create(UpdateCheck(false));
        var flows = 0;
        f.Window.UpdateFlows = enabled =>
        {
            flows++;
            return kit.Flow(PackageForm.Deb);
        };

        // With the background check switched off nothing is made at the first sharp page ...
        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)!.FirstSharpPageShown);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.Equal(0, flows);
        Assert.Equal(0, kit.Source.FetchCalls);

        // ... and the command works because the user asked: the card of a form that cannot update itself has the download page and no Install button.
        var entry = Assert.Single(f.Window.TitleSearch.ExtraResults!("check for updates"), e => e.Group == PdfStrings.PaletteGroupApp);
        Assert.Equal(PdfStrings.CmdCheckForUpdates, entry.Name);
        entry.Run();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);
        Assert.Equal(PdfStrings.UpdateTitle, f.Window.Host.ActiveDialog!.Title);
        Assert.Equal(1, flows);
        Assert.Equal(1, kit.Source.FetchCalls);
        Assert.DoesNotContain(f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>(), b => b.Name == "PART_Install");
        Click(Find(f, "PART_Later"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task ACaptivePortalGivesExactlyOneCouldNotCheckNote()
    {
        using var kit = new UpdateKit();
        kit.Source.Documents = new UpdateDocuments(Encoding.UTF8.GetBytes("<html>Please sign in</html>"), "<html>");
        using var f = WindowFixture.Create();
        f.Window.UpdateFlows = _ => kit.Flow();

        // The check of the start is silent: no card, no note.
        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => kit.Source.FetchCalls == 1);
        await ViewFixture.WaitAsync(() => f.Window.UpdateFlowCreated);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.False(f.Window.Host.IsOpen);
        Assert.Equal(UpdateStage.None, f.Window.UpdateState.Stage);

        // The manual check says it once.
        Assert.Single(f.Window.TitleSearch.ExtraResults!("updates"), e => e.Name == PdfStrings.CmdCheckForUpdates).Run();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);
        Assert.Equal(PdfStrings.UpdateCheckTitle, f.Window.Host.ActiveDialog!.Title);
        Assert.Contains(PdfStrings.UpdateCouldNotCheck, f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Equal(2, kit.Source.FetchCalls);
        Click(Find(f, "PART_Ok"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.False(f.Window.Host.IsOpen);
        Assert.NotNull(tab);
    }

    [AvaloniaFact]
    public async Task UpToDateIsWordedAndInstallRunsTheWholeSequenceInOrder()
    {
        using var kit = new UpdateKit();
        kit.Release("0.1.0");
        using var f = WindowFixture.Create(UpdateCheck(false));
        f.Window.UpdateFlows = _ => kit.Flow();

        Assert.Single(f.Window.TitleSearch.ExtraResults!("check for updates")).Run();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);
        Assert.Equal(PdfStrings.UpdateCheckTitle, f.Window.Host.ActiveDialog!.Title);
        Click(Find(f, "PART_Ok"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);

        // A newer release: the card with Install; Install downloads, checks, asks about the unsaved tab (Don't save), writes the session, and the installer gets the file.
        kit.Release("0.2.0");
        var tab = await f.OpenAsync("pages-marked.pdf");
        tab.MarkDirty();
        kit.Applier.OnApply = () => Assert.True(File.Exists(Path.Combine(PdfSettingsFilesFolder(f), SessionStore.FileName)));
        Assert.Single(f.Window.TitleSearch.ExtraResults!("check for updates")).Run();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);
        Assert.Equal(0, kit.Applier.Calls);
        await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Click(Find(f, "PART_Install"));

        // The unsaved-changes card of the tab (nothing was written before this answer, and no installer has run).
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UnsavedTitle);
        Assert.Equal(0, kit.Applier.Calls);
        Assert.False(File.Exists(Path.Combine(PdfSettingsFilesFolder(f), SessionStore.FileName)));
        await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Click(Find(f, "PART_DontSave"));
        await ViewFixture.WaitAsync(() => kit.Applier.Calls == 1);
        Assert.True(kit.Applier.FileExistedWhenCalled);
        Assert.True(File.Exists(Path.Combine(PdfSettingsFilesFolder(f), SessionStore.FileName)));
    }

    [AvaloniaFact]
    public async Task CancelAtTheQuestionKeepsTheTabAndTheStagedFileAndTheNextInstallReusesIt()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        using var f = WindowFixture.Create(UpdateCheck(false));
        f.Window.UpdateFlows = _ => kit.Flow();
        var tab = await f.OpenAsync("pages-marked.pdf");
        tab.MarkDirty();

        async Task InstallToTheQuestionAsync()
        {
            Assert.Single(f.Window.TitleSearch.ExtraResults!("check for updates")).Run();
            await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);
            await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
            ViewFixture.Pump();
            Click(Find(f, "PART_Install"));
            await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UnsavedTitle);
        }

        await InstallToTheQuestionAsync();
        Click(Find(f, "PART_Cancel"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);

        Assert.True(tab.IsDirty);
        Assert.Contains(tab, f.Window.Workspace.Tabs);
        Assert.Equal(0, kit.Applier.Calls);
        Assert.False(File.Exists(Path.Combine(PdfSettingsFilesFolder(f), SessionStore.FileName)));
        Assert.Equal(UpdateStage.Ready, f.Window.UpdateState.Stage);
        Assert.True(Directory.Exists(kit.StagingOf("0.2.0")));
        Assert.Equal(1, kit.Source.DownloadCalls);

        // The next Install asks again and uses the staged file instead of downloading it again.
        await InstallToTheQuestionAsync();
        Assert.Equal(1, kit.Source.DownloadCalls);
        Click(Find(f, "PART_Cancel"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task AChecksumFailureShowsTheWordedFailureAndNothingIsStaged()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0", new byte[2000], download: new byte[2000].Select(b => (byte)(b + 1)).ToArray());
        using var f = WindowFixture.Create(UpdateCheck(false));
        f.Window.UpdateFlows = _ => kit.Flow();

        Assert.Single(f.Window.TitleSearch.ExtraResults!("check for updates")).Run();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);
        await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Click(Find(f, "PART_Install"));

        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateProgressTitle
            && f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "PART_Failure" && t.IsVisible));
        var failure = f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Failure");
        Assert.Equal(UpdateWording.ForFailure(UpdateFailure.Checksum, PdfStrings.Culture), failure.Text);
        Assert.False(Directory.Exists(kit.StagingOf("0.2.0")));
        Assert.Equal(0, kit.Applier.Calls);
        Click(Find(f, "PART_Dismiss"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
        await ViewFixture.WaitAsync(() => f.Window.UpdateState.Stage == UpdateStage.Available);
    }

    [AvaloniaFact]
    public async Task ACardQueuedBehindAnotherDialogClosesWhenTheFlowIsAlreadyDone()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        using var f = WindowFixture.Create(UpdateCheck(false));
        f.Window.UpdateFlows = _ => kit.Flow();
        _ = f.Window.CheckForUpdatesAsync();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);
        await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Click(Find(f, "PART_Install"));

        // Another card takes the host before the progress card can: the progress card waits in the queue while the download runs and ends.
        var blocker = f.Window.Dialogs.ShowAsync<bool>(done => NoticeCard.Create("Blocker", "Something else", () => done(true)));
        await ViewFixture.WaitAsync(() => f.Window.UpdateState.Stage == UpdateStage.Ready);
        Assert.Equal("Blocker", f.Window.Host.ActiveDialog!.Title);

        // Once the other card is closed the queued card shows, finds the work done and goes by itself: the installer is reached (no tab asks anything).
        Click(Find(f, "PART_Ok"));
        await blocker;
        await ViewFixture.WaitAsync(() => kit.Applier.Calls == 1);
    }

    [AvaloniaFact]
    public async Task AFailedInstallTakesBackTheSessionOfTheRestart()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        kit.Applier.Result = ApplyResult.Failed();
        using var f = WindowFixture.Create(UpdateCheck(false));
        f.Window.UpdateFlows = _ => kit.Flow();
        var session = Path.Combine(PdfSettingsFilesFolder(f), SessionStore.FileName);

        // A session file of an earlier attempt must not come back, whatever this attempt does: with no tab to restore it is removed before the installer runs ...
        await File.WriteAllTextAsync(session, "{\"windows\":[]}", TestContext.Current.CancellationToken);
        var sawSession = false;
        kit.Applier.OnApply = () => sawSession = File.Exists(session);
        _ = f.Window.CheckForUpdatesAsync();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);
        await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Click(Find(f, "PART_Install"));

        // ... and when the installer fails the app goes on running: no session file stays behind for a later restart to find.
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateProgressTitle
            && f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "PART_Failure" && t.IsVisible));
        Assert.Equal(1, kit.Applier.Calls);
        Assert.False(sawSession, "with no tab to restore the old file is removed before the installer runs");
        Assert.False(File.Exists(session));
        Click(Find(f, "PART_Dismiss"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
        Assert.False(File.Exists(session));
    }

    [AvaloniaFact]
    public async Task AFailedInstallWithTabsAlsoTakesBackTheSession()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        kit.Applier.Result = ApplyResult.Failed();
        using var f = WindowFixture.Create(UpdateCheck(false));
        f.Window.UpdateFlows = _ => kit.Flow();
        var session = Path.Combine(PdfSettingsFilesFolder(f), SessionStore.FileName);
        await f.OpenAsync("pages-marked.pdf");
        var sawSession = false;
        kit.Applier.OnApply = () => sawSession = File.Exists(session);

        _ = f.Window.CheckForUpdatesAsync();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);
        await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Click(Find(f, "PART_Install"));
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateProgressTitle
            && f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "PART_Failure" && t.IsVisible));

        // The installer saw the session (it needs it for the restart); after its failure the file is gone.
        Assert.True(sawSession);
        Assert.False(File.Exists(session));
        Click(Find(f, "PART_Dismiss"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task TheInstallerGetsTheBoundsOfTheWindowWhileItIsNormal()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        using var f = WindowFixture.Create(UpdateCheck(false), width: 900, height: 600);
        UpdateFlow? made = null;
        f.Window.UpdateFlows = _ => made = kit.Flow();
        _ = f.Window.CheckForUpdatesAsync();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen);

        var bounds = made!.ProgressWindowBounds!()!.Value;
        Assert.Equal(f.Window.Position.X, bounds.Left);
        Assert.Equal(f.Window.Position.Y, bounds.Top);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        Assert.Equal(f.Window.ClientSize.Width, bounds.Width, 1);
        Assert.Equal(f.Window.ClientSize.Height, bounds.Height, 1);

        // A maximized or minimized window is not a place for a small progress window: the helper centres itself.
        f.Window.WindowState = Avalonia.Controls.WindowState.Maximized;
        Assert.Null(made.ProgressWindowBounds!());
        f.Window.WindowState = Avalonia.Controls.WindowState.Normal;
        Click(Find(f, "PART_Later"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
    }

    private static string PdfSettingsFilesFolder(WindowFixture f) => f.Folder;
}
