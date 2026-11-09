using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Ocr;
using Raffica.Core.Updates;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Ocr;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

/// <summary>An update never starts the installer (or even the question and the download) while a job that works on a file runs.</summary>
public sealed class UpdateBusyTests
{
    private static Button Find(PdfWindow window, string name) =>
        window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task ChooseInstallAsync(PdfWindow window)
    {
        Assert.Single(window.TitleSearch.ExtraResults!("check for updates"), e => e.Name == PdfStrings.CmdCheckForUpdates).Run();
        await ViewFixture.WaitAsync(() => window.Host.IsOpen && window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);
        await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Click(Find(window, "PART_Install"));
    }

    private static async Task AssertBusyNoteAsync(PdfWindow window)
    {
        await ViewFixture.WaitAsync(() => window.Host.IsOpen && window.Host.ActiveDialog!.Title == PdfStrings.UpdateCheckTitle);
        Assert.Contains(PdfStrings.UpdateBusyNote, window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Click(Find(window, "PART_Ok"));
        await ViewFixture.WaitAsync(() => !window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task ARunningFileJobStopsTheInstallBeforeAnythingIsDownloaded()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        using var f = WindowFixture.Create();
        f.Window.UpdateFlows = _ => kit.Flow();
        f.Window.UpdateBusy = () => true;

        await ChooseInstallAsync(f.Window);

        await AssertBusyNoteAsync(f.Window);
        Assert.Equal(0, kit.Source.DownloadCalls);
        Assert.Equal(0, kit.Applier.Calls);
        Assert.False(Directory.Exists(kit.StagingOf("0.2.0")));
    }

    [AvaloniaFact]
    public async Task AJobThatStartsDuringTheDownloadStopsTheInstallAndKeepsTheStagedFile()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        using var f = WindowFixture.Create();
        f.Window.UpdateFlows = _ => kit.Flow();
        var tab = await f.OpenAsync("pages-marked.pdf");
        tab.MarkDirty();
        f.Window.UpdateBusy = () => kit.Source.DownloadCalls > 0;

        await ChooseInstallAsync(f.Window);

        // No question about the tab is asked, nothing is written, the installer does not start; the verified file stays.
        await AssertBusyNoteAsync(f.Window);
        Assert.Equal(0, kit.Applier.Calls);
        Assert.True(tab.IsDirty);
        Assert.False(File.Exists(Path.Combine(f.Folder, SessionStore.FileName)));
        Assert.True(Directory.Exists(kit.StagingOf("0.2.0")));
        Assert.Equal(UpdateStage.Ready, f.Window.UpdateState.Stage);
    }

    [AvaloniaFact]
    public async Task AJobThatStartsWhileTheQuestionIsOpenStopsTheInstallAndTakesTheSessionBack()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        using var f = WindowFixture.Create();
        f.Window.UpdateFlows = _ => kit.Flow();
        var tab = await f.OpenAsync("pages-marked.pdf");
        tab.MarkDirty();
        var busy = false;
        f.Window.UpdateBusy = () => busy;

        await ChooseInstallAsync(f.Window);
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UnsavedTitle);
        busy = true;
        await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Click(Find(f.Window, "PART_DontSave"));

        await AssertBusyNoteAsync(f.Window);
        Assert.Equal(0, kit.Applier.Calls);
        Assert.False(File.Exists(Path.Combine(f.Folder, SessionStore.FileName)));
        Assert.True(Directory.Exists(kit.StagingOf("0.2.0")));
    }

    [AvaloniaFact]
    public async Task ARunningTextRecognitionStopsTheInstall()
    {
        using var kit = new UpdateKit();
        kit.Release("0.2.0");
        using var rig = await OcrRig.OpenAsync();
        rig.Window.UpdateFlows = _ => kit.Flow();

        // The real probe of the window: a recognition that runs counts (the fake engine holds the first page).
        rig.Engines!.HoldAt(0);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, new OcrOptions("eng", OcrMode.Skip, Jobs: 1));
        await ViewFixture.WaitAsync(() => rig.Window.OcrFlow.IsRunning);

        await ChooseInstallAsync(rig.Window);
        await AssertBusyNoteAsync(rig.Window);
        Assert.Equal(0, kit.Source.DownloadCalls);
        Assert.Equal(0, kit.Applier.Calls);

        rig.Window.CancelOcr();
        rig.Engines.Release();

        // Whether the first page was finished before the cancel was seen decides if the keep-or-discard card comes: answer it when it does.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!run.IsCompleted && DateTime.UtcNow < deadline)
        {
            if (rig.Window.Host.ActiveDialog is { } card && card.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Name == "PART_Discard") is { } discard)
            {
                Click(discard);
            }

            ViewFixture.Pump();
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
