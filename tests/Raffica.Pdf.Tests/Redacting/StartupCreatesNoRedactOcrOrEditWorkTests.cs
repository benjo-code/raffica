using Avalonia.Headless.XUnit;
using Raffica.Core.Viewing;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class StartupCreatesNoRedactOcrOrEditWorkTests
{
    [AvaloniaFact]
    public async Task StartupCreatesNoRedactOcrOrEditWork()
    {
        using var f = WindowFixture.Create();
        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => tab.Session.State == ViewerState.Ready);
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)!.FirstSharpPageShown);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();

        foreach (var open in f.Window.Workspace.Tabs)
        {
            Assert.Null(open.RedactIfCreated);
            Assert.Null(open.OcrIfCreated);
            Assert.Null(open.TextEditIfCreated);
        }

        // The apply flow is made by the first Apply, not by opening a file or entering the mode.
        Assert.False(f.Window.RedactFlowCreated);
        f.Window.ToggleRedact();
        await ViewFixture.WaitAsync(() => tab.RedactIfCreated is { IsActive: true });
        Assert.False(f.Window.RedactFlowCreated);
    }
}
