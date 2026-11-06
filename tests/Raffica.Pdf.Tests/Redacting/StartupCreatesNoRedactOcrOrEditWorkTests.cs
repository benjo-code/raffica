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
    }
}
