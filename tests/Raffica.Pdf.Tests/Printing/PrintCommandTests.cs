using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Core.Settings;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Printing;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Printing;

public sealed class PrintCommandTests
{
    private const string FourPages = "print-mixed.pdf";

    private static PrintTicket Ticket(string printer = "Office") => new(new PrintSettings { Printer = printer });

    private static void Click(Control root, string name) =>
        root.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static PrintCommand Create(FakePrintService service, PrintHost host, PdfTab? tab, List<string?>? remembered = null, AppSettings? settings = null) =>
        new(() => service, () => tab, host.Presenter, () => settings ?? AppSettings.Default, name => remembered?.Add(name));

    [AvaloniaFact]
    public async Task LockedDocumentIsRefusedWithAReasonAndNothingIsSent()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { DialogResult = Ticket() };
        var tab = await host.OpenAsync("encrypted_hello_world_r3.pdf", "hôtel");

        await Create(service, host, tab).RunAsync();
        PrintHost.Pump();

        Assert.Equal(PdfStrings.PrintLockedReason, host.MessageText);
        Assert.Empty(service.DialogRequests);
        Assert.Empty(service.Jobs);
    }

    [AvaloniaFact]
    public async Task NoPrinterDisablesTheCommandWithAReason()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { IsAvailable = false, DialogResult = Ticket() };
        var tab = await host.OpenAsync(FourPages);
        var command = Create(service, host, tab);

        // The rules (ribbon, shortcut, search) disable Print and say why; a run that gets through anyway sends nothing.
        var rules = new PdfCommandRules(() => true, () => 1, () => new PdfPrintInfo(false, true), () => command.IsAvailable);
        Assert.False(rules.Allows(PdfCommandIds.Print));
        Assert.Equal(PdfStrings.PrintNoPrinter, rules.ReasonFor(PdfCommandIds.Print));

        await command.RunAsync();
        PrintHost.Pump();

        Assert.Equal(PdfStrings.PrintNoPrinter, host.MessageText);
        Assert.Empty(service.DialogRequests);
        Assert.Empty(service.Jobs);
    }

    [AvaloniaFact]
    public async Task ProgressReachesTheJobCard()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { DialogResult = Ticket(), PauseBefore = 2 };
        var tab = await host.OpenAsync(FourPages);

        var run = Create(service, host, tab).RunAsync();
        await PrintHost.UntilAsync(() => host.Slot.Content is PrintJobCard);
        var card = (PrintJobCard)host.Slot.Content!;
        await PrintHost.UntilAsync(() => card.StatusText == PdfStrings.Fill(PdfStrings.PrintJobProgressFormat, 2, 4));

        Assert.Equal(0.5, card.Fraction, 3);
        Assert.Contains(tab.Title, card.Title, StringComparison.Ordinal);

        service.Gate.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        PrintHost.Pump();

        // Done: the card is gone and nothing is left over.
        Assert.Null(host.Slot.Content);
        Assert.False(host.Host.IsOpen);
        Assert.Equal([0, 1, 2, 3], service.Printed.Single());
        Assert.All(service.TemporaryFiles, f => Assert.False(File.Exists(f)));
    }

    [AvaloniaFact]
    public async Task FailedPagesAreReportedAndCancelStopsTheJob()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { DialogResult = Ticket() };
        service.FailPages.Add(2);
        var tab = await host.OpenAsync(FourPages);
        var command = Create(service, host, tab);

        // Page 3 fails: the job goes on, and the report names the page, counted from 1.
        await command.RunAsync();
        PrintHost.Pump();
        Assert.Equal([0, 1, 3], service.Printed[0]);
        Assert.Equal(PdfStrings.Fill(PdfStrings.PrintFailedPagesFormat, "3"), host.MessageText);
        Click(host.Host.ActiveDialog!, "PART_Ok");
        PrintHost.Pump();
        Assert.False(host.Host.IsOpen);

        // A second run is cancelled after page 2: no page after it, no report, no temporary file.
        service.FailPages.Clear();
        service.PauseBefore = 2;
        service.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = command.RunAsync();
        await PrintHost.UntilAsync(() => service.Paused.Task.IsCompleted && host.Slot.Content is PrintJobCard);
        Click((Control)host.Slot.Content!, "PART_Cancel");
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        PrintHost.Pump();

        Assert.Equal([0, 1], service.Printed[1]);
        Assert.Null(host.Slot.Content);
        Assert.False(host.Host.IsOpen);
        Assert.All(service.TemporaryFiles, f => Assert.False(File.Exists(f)));
        Assert.False(command.IsRunning);
    }

    [Fact]
    public void ALongListOfFailedPagesIsShortened()
    {
        Assert.Equal("1, 2, 4", PrintCommand.FailedPagesText([0, 1, 3]));

        var text = PrintCommand.FailedPagesText(Enumerable.Range(0, 50).ToArray());
        Assert.StartsWith("1, 2, 3, 4, 5, 6, 7, 8, 9, 10", text, StringComparison.Ordinal);
        Assert.EndsWith("40 more", text, StringComparison.Ordinal);
        Assert.DoesNotContain("11,", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TheUiThreadIsNotBlocked()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { DialogResult = Ticket(), PauseBefore = 0 };
        var tab = await host.OpenAsync(FourPages);

        var run = Create(service, host, tab).RunAsync();
        await PrintHost.UntilAsync(() => service.Paused.Task.IsCompleted);

        // The job waits on its gate; the dispatcher keeps running and the command has not returned.
        var posted = false;
        Dispatcher.UIThread.Post(() => posted = true);
        PrintHost.Pump();
        Assert.True(posted);
        Assert.False(run.IsCompleted);
        Assert.IsType<PrintJobCard>(host.Slot.Content);

        service.Gate.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [AvaloniaFact]
    public async Task ClosingTheTabCancelsTheJobAndShowsNoError()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { DialogResult = Ticket(), PauseBefore = 2 };
        var tab = await host.OpenAsync(FourPages);
        var command = Create(service, host, tab);

        var run = command.RunAsync();
        await PrintHost.UntilAsync(() => service.Paused.Task.IsCompleted && host.Slot.Content is PrintJobCard);

        // The tab closes under the running job: its session goes, the job is cancelled first, and nothing is reported.
        tab.Dispose();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        PrintHost.Pump();

        Assert.Equal([0, 1], service.Printed.Single());
        Assert.False(host.Host.IsOpen);
        Assert.Null(host.Slot.Content);
        Assert.False(command.IsRunning);
        Assert.All(service.TemporaryFiles, f => Assert.False(File.Exists(f)));
    }

    [AvaloniaFact]
    public async Task ADocumentThatWasDisposedUnderTheJobEndsQuietly()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { DialogResult = Ticket(), Fail = new ObjectDisposedException("session") };
        var tab = await host.OpenAsync(FourPages);

        await Create(service, host, tab).RunAsync();
        PrintHost.Pump();

        // Not the card "could not start printing": the document went away, that is all.
        Assert.False(host.Host.IsOpen);
        Assert.Null(host.Slot.Content);
    }

    [AvaloniaFact]
    public async Task OnlyOneJobRunsAtATime()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { DialogResult = Ticket(), PauseBefore = 1 };
        var tab = await host.OpenAsync(FourPages);
        var command = Create(service, host, tab);

        var first = command.RunAsync();
        await PrintHost.UntilAsync(() => service.Paused.Task.IsCompleted);
        await command.RunAsync();
        PrintHost.Pump();

        Assert.Equal(PdfStrings.PrintBusy, host.MessageText);
        Assert.Single(service.Jobs);
        Click(host.Host.ActiveDialog!, "PART_Ok");

        service.Gate.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(command.IsRunning);
    }

    [AvaloniaFact]
    public async Task LastPrinterIsRemembered()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { DialogResult = Ticket("Office") };
        var tab = await host.OpenAsync(FourPages);
        var remembered = new List<string?>();

        await Create(service, host, tab, remembered).RunAsync();

        Assert.Equal(["Office"], remembered);

        // The next dialog starts with it, and with the scaling of the settings.
        var settings = AppSettings.Default with { LastPrinter = "Office", PrintScaling = PrintScaling.ActualSize };
        service.DialogResult = Ticket("Office");
        await Create(service, host, tab, remembered, settings).RunAsync();
        Assert.Equal("Office", service.DialogRequests[1].Defaults.Printer);
        Assert.Equal(PrintScaling.ActualSize, service.DialogRequests[1].Defaults.Scaling);
        Assert.Equal(string.Empty, service.DialogRequests[0].Defaults.Printer);
        Assert.Equal(4, service.DialogRequests[0].PageCount);
    }

    [AvaloniaFact]
    public void TheLastPrinterIsWrittenToTheSettingsFileAndNothingElseChanges()
    {
        using var windows = WindowFixture.Create();
        var before = windows.Window.Settings.App;

        windows.Window.Settings.RememberPrinter("Laser");

        Assert.Equal("Laser", windows.Window.Settings.App.LastPrinter);
        Assert.True(windows.Window.Settings.Flush(TimeSpan.FromSeconds(10)));
        var saved = new SettingsStore(windows.AppSettingsPath).LoadReadOnly();
        Assert.Equal("Laser", saved.LastPrinter);
        Assert.Equal(before.DefaultLayout, saved.DefaultLayout);
        Assert.Equal(before.PageAppearance, saved.PageAppearance);
    }

    [AvaloniaFact]
    public async Task AnErrorShowsTheLocalizedTextNeverTheRawOne()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { DialogResult = Ticket(), Fail = new PrintException(@"C:\secret\spool\job42.tmp could not be opened") };
        var tab = await host.OpenAsync(FourPages);

        await Create(service, host, tab).RunAsync();
        PrintHost.Pump();

        Assert.Equal(PdfStrings.PrintErrorStart, host.MessageText);
        Assert.False(File.Exists(service.TemporaryFiles.Single()));

        // A text of our own is shown as it is.
        Click(host.Host.ActiveDialog!, "PART_Ok");
        PrintHost.Pump();
        service.Fail = new PrintException(PdfStrings.PrintErrorPage);
        await Create(service, host, tab).RunAsync();
        PrintHost.Pump();
        Assert.Equal(PdfStrings.PrintErrorPage, host.MessageText);
    }

    [AvaloniaFact]
    public async Task OnLinuxTheCardAsksAndItsAnswerStartsTheJob()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { HasSystemDialog = false, DuplexSupported = true };
        service.Printers.Add(new PrinterInfo("CUPS-PDF", "PDF writer", IsDefault: true));
        var tab = await host.OpenAsync(FourPages);
        var remembered = new List<string?>();

        var run = Create(service, host, tab, remembered).RunAsync();
        await PrintHost.UntilAsync(() => host.Host.IsOpen);
        PrintHost.Pump();
        Click(host.Host.ActiveDialog!, "PART_Print");
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(service.DialogRequests);
        Assert.Equal("CUPS-PDF", service.Jobs.Single().Ticket.Settings.Printer);
        Assert.Equal(["CUPS-PDF"], remembered);
        Assert.Equal([0, 1, 2, 3], service.Printed.Single());
    }

    [AvaloniaFact]
    public async Task CancelInTheCardPrintsNothing()
    {
        using var host = new PrintHost();
        var service = new FakePrintService { HasSystemDialog = false };
        service.Printers.Add(new PrinterInfo("CUPS-PDF", null, IsDefault: true));
        var tab = await host.OpenAsync(FourPages);

        var run = Create(service, host, tab).RunAsync();
        await PrintHost.UntilAsync(() => host.Host.IsOpen);
        PrintHost.Pump();
        Click(host.Host.ActiveDialog!, "PART_Cancel");
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(service.Jobs);
        Assert.False(host.Host.IsOpen);
    }
}
