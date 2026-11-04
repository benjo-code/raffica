using Avalonia.Headless.XUnit;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Printing;

/// <summary>Nothing for printing is asked or read while the first page of the first document is on its way.</summary>
public sealed class PrintProbeTests
{
    private static bool PrintEnabled(PdfWindow window) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "file.print").IsEnabled;

    [AvaloniaFact]
    public async Task NothingPrintRelatedRunsBeforeTheFirstSharpPage()
    {
        var service = new FakePrintService { DialogResult = null };

        // Not shown: the window lays out nothing, so the view of the tab cannot have a sharp page yet.
        using var windows = WindowFixture.Create(show: false, printService: service);
        var tab = await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;
        var view = window.ViewOf(tab)!;
        Assert.False(view.FirstSharpPageShown);

        Assert.Equal(0, service.AvailabilityChecks);
        Assert.Null(window.PrintInfoOf(tab));
        Assert.True(PrintEnabled(window), "Print is not refused for something that has not been asked");

        // The first sharp page: now the permissions are read and the system is asked, once.
        window.Show();
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        await ViewFixture.WaitAsync(() => window.PrintInfoOf(tab) is not null);

        Assert.True(window.PrintInfoOf(tab)!.Value.PrintAllowed);
        Assert.True(service.AvailabilityChecks >= 1);
        var asked = service.AvailabilityChecks;
        Assert.True(PrintEnabled(window));

        // The answer is kept: asking the rules again does not ask the system again.
        await windows.OpenAsync("hello_world.pdf");
        Assert.True(PrintEnabled(window));
        Assert.Equal(asked, service.AvailabilityChecks);
    }

    [AvaloniaFact]
    public async Task ASystemThatCannotPrintDisablesPrintOnceTheAnswerIsKnown()
    {
        var service = new FakePrintService { IsAvailable = false, DialogResult = null };
        using var windows = WindowFixture.Create(show: false, printService: service);
        var tab = await windows.OpenAsync("text-basic.pdf");
        var window = windows.Window;
        Assert.Equal(0, service.AvailabilityChecks);

        window.Show();
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => window.ViewOf(tab)!.FirstSharpPageShown);
        await ViewFixture.WaitAsync(() => !PrintEnabled(window));

        Assert.False(PrintEnabled(window));
    }
}
