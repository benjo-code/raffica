using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

/// <summary>Text recognition writes into the document, so it needs the permission that annotating needs, with the same reason when it is missing.</summary>
public sealed class OcrPermissionTests
{
    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    [Fact]
    public void RuleAsksThePermissionOfTheAnnotateTools()
    {
        PdfCommandRules Rules(bool allowed) => new(() => true, () => 1, () => null, annotateAllowed: () => allowed, ocrLibrary: () => true, ocrStarter: () => true);

        Assert.True(Rules(true).Allows("ocr.run"));
        Assert.Null(Rules(true).ReasonFor("ocr.run"));
        Assert.False(Rules(false).Allows("ocr.run"));
        Assert.Equal(PdfStrings.AnnotateLockedReason, Rules(false).ReasonFor("ocr.run"));
        // Without the library the library is the reason; the permission does not hide it.
        var noLibrary = new PdfCommandRules(() => true, () => 1, () => null, annotateAllowed: () => false, ocrLibrary: () => false, ocrStarter: () => true);
        Assert.Equal(PdfStrings.OcrNoLibraryReason, noLibrary.ReasonFor("ocr.run"));
        // The language files stay reachable.
        Assert.True(new PdfCommandRules(() => true, () => 1, () => null, annotateAllowed: () => false).Allows("ocr.languages"));
    }

    [AvaloniaFact]
    public async Task ForbiddenDocumentRefusesTheCommandAndTheRaceAfterTheCard()
    {
        using var f = WindowFixture.Create();
        f.Window.OcrLibraryProbe = () => true;
        var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(TestData.Pdf("encrypted_hello_world_r3.pdf"), "hôtel");
        var tab = f.Window.Workspace.Add(session, TestData.Pdf("encrypted_hello_world_r3.pdf"));
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => f.Window.AnnotateAllowedOf(tab) is not null && f.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        Assert.False(f.Window.AnnotateAllowedOf(tab));

        var command = Command(f.Window, "ocr.run");
        Assert.False(command.IsEnabled);
        Assert.Equal(PdfStrings.AnnotateLockedReason, command.CurrentDisabledReason);

        // A path around the command (a card that was open while the permission arrived) ends in a note with the reason, and no run.
        var start = f.Window.StartOcrRunAsync(tab, new Raffica.Core.Ocr.OcrOptions("eng"));
        await PrintHost.UntilAsync(() => f.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Ok"));
        var card = f.Window.Host.ActiveDialog!;
        Assert.Contains(PdfStrings.AnnotateLockedReason, OcrRig.TextsOf(card));
        OcrRig.Click(card, "PART_Ok");
        await start;
        Assert.False(f.Window.OcrFlowCreated);
        Assert.Null(tab.OcrIfCreated);
    }

    [AvaloniaFact]
    public async Task NormalAndSignedFilesStillOffer()
    {
        foreach (var file in new[] { "ocr-scan.pdf", "signed-docmdp-1.pdf" })
        {
            using var f = WindowFixture.Create();
            f.Window.OcrLibraryProbe = () => true;
            var tab = await f.OpenAsync(file);
            await ViewFixture.WaitAsync(() => f.Window.AnnotateAllowedOf(tab) is not null && f.Window.ViewOf(tab)?.FirstSharpPageShown == true);
            Assert.True(Command(f.Window, "ocr.run").IsEnabled, file);
        }
    }
}
