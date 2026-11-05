using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Core.Printing;
using Raffica.Core.Settings;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Platform.Windows;
using Raffica.Pdf.Printing;
using Raffica.Pdf.Tests.Forms;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Printing;

/// <summary>
/// What a printed form carries (M2.3, plan call 10): Core prints the pages with the values the document holds now, saved or not, so the app
/// only has to make sure that an open field edit is committed before the job starts, and that a file without a saved copy is printed from the
/// document in memory.
/// </summary>
public sealed class PrintFormValuesTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Wraps the fake service and reads the value of the field "Name" from the document at the moment the job starts, as the pages would be drawn.</summary>
    private sealed class ReadingService(FakePrintService inner) : IPrintService
    {
        public string? NameSeenByTheJob { get; private set; }

        public string? PathSeenByTheJob { get; private set; } = "(no job)";

        public bool IsAvailable => inner.IsAvailable;

        public bool HasSystemDialog => inner.HasSystemDialog;

        public Task<PrintTicket?> ShowDialogAsync(PrintRequest request, CancellationToken cancellationToken) => inner.ShowDialogAsync(request, cancellationToken);

        public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken) => inner.GetPrintersAsync(cancellationToken);

        public Task<bool> SupportsDuplexAsync(string printerName, CancellationToken cancellationToken) => inner.SupportsDuplexAsync(printerName, cancellationToken);

        public async Task PrintAsync(PrintJobRequest job, IProgress<PrintProgress> progress, CancellationToken cancellationToken)
        {
            var fields = await job.Document.GetFormFieldsAsync(0, cancellationToken);
            NameSeenByTheJob = fields.Single(f => f.Key.FieldName == "Name").State.Text;
            PathSeenByTheJob = job.Path;
            await inner.PrintAsync(job, progress, cancellationToken);
        }
    }

    private static PrintCommand CommandFor(FormEditFixture f, ReadingService service) =>
        new(() => service, () => f.Tab, new PrintPresenter(new DialogQueue(f.Window.Host), _ => { }), () => AppSettings.Default, _ => { });

    [AvaloniaFact]
    public async Task PrintFlushesAnOpenFieldEditFirst()
    {
        using var f = await FormEditFixture.OpenAsync();
        var fake = new FakePrintService { DialogResult = new PrintTicket(new PrintSettings { Printer = "Office" }) };
        var service = new ReadingService(fake);
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Erika");
        Assert.True(f.Interaction.IsEditing);

        await CommandFor(f, service).RunAsync();
        PrintHost.Pump();

        // The edit was committed before the job started: the document held the new value when the job read it, and the editor is closed.
        Assert.Equal("Erika", service.NameSeenByTheJob);
        Assert.False(f.Interaction.IsEditing);
        Assert.Single(fake.Jobs);

        // A refused draft stays open (it is not the print's business) and the job carries the last accepted value.
        var refused = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(refused, "zwei\nZeilen");
        await CommandFor(f, service).RunAsync();
        PrintHost.Pump();

        Assert.True(f.Interaction.IsEditing);
        Assert.NotEmpty(f.Interaction.Editor!.Hint);
        Assert.Equal("Erika", service.NameSeenByTheJob);
        Assert.Equal(2, fake.Jobs.Count);
    }

    [AvaloniaFact]
    public async Task AFileWithAnUnsavedValueIsPrintedFromTheDocumentInMemory()
    {
        using var f = await FormEditFixture.OpenAsync();
        var service = new ReadingService(new FakePrintService { DialogResult = new PrintTicket(new PrintSettings { Printer = "Office" }) });

        await CommandFor(f, service).RunAsync();
        Assert.Equal(f.Tab.Path, service.PathSeenByTheJob);

        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Erika");
        await CommandFor(f, service).RunAsync();

        // No path: the services then write a copy from the document in memory (CupsPrintService does, WindowsPrintService draws the document).
        Assert.Null(service.PathSeenByTheJob);
    }

    // CupsPrintService prints the saved file when there is one and otherwise (no path, or an encrypted file) the copy from
    // PdfDocument.SaveDecryptedCopyAsync. CUPS itself is not available here; what the Linux path hands to CUPS is that copy, and it is pinned here.
    [AvaloniaFact]
    public async Task TheCopyForCupsCarriesAValueThatWasSetButNotSaved()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Erika");
        f.Leave();
        await f.SettleAsync();
        Assert.True(f.Tab.IsDirty);

        var copy = Path.Combine(f.Copy.Folder, "for-cups.pdf");
        await f.Tab.Session.Document!.SaveDecryptedCopyAsync(copy, Cancel);

        await using var reopened = await new PdfEngine().OpenAsync(copy, cancellationToken: Cancel);
        var name = (await reopened.GetFormFieldsAsync(0, Cancel)).Single(x => x.Key.FieldName == "Name");
        Assert.Equal("Erika", name.State.Text);

        // The file on disk still holds the old value: the copy is what carries the new one.
        await using var onDisk = await new PdfEngine().OpenAsync(f.Copy.Path, cancellationToken: Cancel);
        Assert.Equal("Alt", (await onDisk.GetFormFieldsAsync(0, Cancel)).Single(x => x.Key.FieldName == "Name").State.Text);
    }

    // Printing must not make a form or annotation session for a plain document. The window-level case (no form work on open) lives in
    // FormBadgeTests.BadgeIsHiddenForADocumentWithoutAForm.
    [AvaloniaFact]
    public async Task PrintingADocumentWithoutAFormCreatesNoSession()
    {
        using var host = new PrintHost();
        var fake = new FakePrintService { DialogResult = new PrintTicket(new PrintSettings { Printer = "Office" }) };
        var tab = await host.OpenAsync("hello_world.pdf");
        var command = new PrintCommand(() => fake, () => tab, host.Presenter, () => AppSettings.Default, _ => { });

        await command.RunAsync();
        PrintHost.Pump();

        Assert.Single(fake.Jobs);
        Assert.Null(tab.FormsIfCreated);
        Assert.Null(tab.AnnotationsIfCreated);
    }

    // This pins Core and the Windows service (the pages carry an unsaved value); the flush in PrintCommand is pinned by the fake-service tests above.
    // Prints for real through "Microsoft Print to PDF" (no dialog, no paper) and compares the printed page of the unchanged form with the
    // printed page after a value was set and not saved: the pictures must differ. Skipped where that printer is not installed and off Windows.
    [AvaloniaFact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task PrintedPageCarriesUnsavedFormValues()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Printing needs Windows.");
        using var f = await FormEditFixture.OpenAsync();
        var before = await PrintToPdfAsync(f, "before.pdf");

        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Erika Mustermann");
        f.Leave();
        await f.SettleAsync();
        Assert.True(f.Tab.IsDirty);
        var after = await PrintToPdfAsync(f, "after.pdf");

        Assert.False(before.AsSpan().SequenceEqual(after), "the printed page must show the value that was set but not saved");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<byte[]> PrintToPdfAsync(FormEditFixture f, string name)
    {
        const string PdfPrinter = "Microsoft Print to PDF";
        var output = Path.Combine(f.Copy.Folder, name);
        WindowsPrintTicket ticket;
        try
        {
            ticket = WindowsPrintService.CreateTicket(PdfPrinter, new PrintSettings(), output);
        }
        catch (PrintException)
        {
            Assert.Skip($"The printer \"{PdfPrinter}\" is not installed.");
            throw;
        }

        using (ticket)
        {
            var job = new PrintJobRequest(f.Tab.Session.Document!, null, name, 0, ticket);
            await new WindowsPrintService().PrintAsync(job, new Progress<PrintProgress>(), Cancel);
        }

        await using var printed = await new PdfEngine().OpenAsync(output, cancellationToken: Cancel);
        return (await printed.RenderPageAsync(0, 0.5, cancellationToken: Cancel)).Pixels.ToArray();
    }
}
