using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Raffica.Pdf.Documents;
using Raffica.Shell;
using Raffica.Shell.Frame;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class DropTests
{
    private static PdfWindow Show(params string[] files)
    {
        var options = PdfOptions.Parse(["--settings-file", Path.Combine(Path.GetTempPath(), "raffica-pdf-tests", "settings.json"), .. files]);
        var window = new PdfWindow(options, new ShellSettings());
        window.Show();
        Pump();
        return window;
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<DataTransfer> TransferAsync(PdfWindow window, params string[] paths)
    {
        var transfer = new DataTransfer();
        foreach (var path in paths)
        {
            var file = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
            Assert.NotNull(file);
            transfer.Add(DataTransferItem.CreateFile(file));
        }

        return transfer;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
            Pump();
        }
    }

    [AvaloniaFact]
    public async Task DroppedFilesReachTheOpenFlow()
    {
        var window = Show();
        var transfer = await TransferAsync(window, TestData.Pdf("hello_world.pdf"), TestData.Pdf("not-a-pdf.pdf"));

        Assert.True(FileDrop.Accepts(transfer));
        Assert.Equal([TestData.Pdf("hello_world.pdf"), TestData.Pdf("not-a-pdf.pdf")], FileDrop.Paths(transfer));

        await window.OpenDroppedAsync(transfer);

        // The PDF is open; the non-PDF (not filtered by its extension) reached Core and ended in the error card.
        Assert.Equal(["hello_world.pdf"], window.Workspace.Tabs.Select(t => t.Title));
        Assert.NotNull(window.Host.ActiveDialog);
        Assert.Equal(PdfStrings.ErrorTitle, window.Host.ActiveDialog.Title);
        window.Close();
        Pump();
    }

    [AvaloniaFact]
    public void ADropWithoutFilesIsNotAccepted()
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText("hello"));
        Assert.False(FileDrop.Accepts(transfer));
        Assert.Empty(FileDrop.Paths(transfer));
    }

    [AvaloniaFact]
    public async Task TheFilesOfTheCommandLineOpenAfterTheWindowIsShown()
    {
        var window = Show(TestData.Pdf("text-rich.pdf"));
        await WaitAsync(() => window.Workspace.Tabs.Count == 1);

        Assert.Equal("text-rich.pdf", window.Workspace.Active?.Title);
        Assert.Single(window.Tabs.Items);
        window.Close();
        Pump();
    }
}
