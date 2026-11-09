using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class RedactLockTests
{
    private static readonly string[] Locked =
    [
        "annotate.mode", "annotate.highlight", "annotate.freehand", "annotate.textBox", "annotate.sign",
        "pages.organize", "ocr.run", "text.edit",
        "pages.extract", "pages.split", "pages.exportImages",
    ];

    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<PdfTab> OpenReadyAsync(WindowFixture fixture, string file = "pages-marked.pdf")
    {
        var tab = await fixture.OpenAsync(file);
        await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        return tab;
    }

    [AvaloniaFact]
    public async Task ModeLocksAnnotateOrganizeOcrAndOutputs()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        var window = f.Window;
        // Recognize text and Edit text have no action yet (their steps come later); the rules for them are in ModeLockRuleTests.
        foreach (var id in Locked.Where(id => id is not ("ocr.run" or "text.edit")))
        {
            Assert.True(Command(window, id).IsEnabled, id + " before the mode");
        }

        Command(window, "redact.mode").Run!();
        ViewFixture.Pump();
        Assert.True(tab.Redact.IsActive);

        foreach (var id in Locked)
        {
            var command = Command(window, id);
            Assert.False(command.IsEnabled, id);
            Assert.Equal("Apply or discard the redactions first.", command.CurrentDisabledReason);
        }

        // Print and the commands of the mode stay; the others keep their rules.
        Assert.True(Command(window, "file.print").IsEnabled);
        Assert.True(Command(window, "redact.markSelection").IsEnabled);
        Assert.True(Command(window, "redact.deleteMarks").IsEnabled);
        Assert.True(Command(window, "redact.discard").IsEnabled);
        Assert.True(Command(window, "zoom.in").IsEnabled);

        // An empty plan leaves silently; the locks go with the mode.
        Command(window, "redact.mode").Run!();
        ViewFixture.Pump();
        Assert.False(tab.Redact.IsActive);
        Assert.True(Command(window, "pages.organize").IsEnabled);
        Assert.True(Command(window, "annotate.highlight").IsEnabled);
    }

    [AvaloniaFact]
    public async Task AnAnnotateToolInTheWayRefusesTheModeWithItsReason()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f);
        f.Window.Tools.Choose(Raffica.Pdf.Annotating.AnnotateTool.Highlight);
        ViewFixture.Pump();

        var command = Command(f.Window, "redact.mode");
        Assert.False(command.IsEnabled);
        Assert.Equal("Finish annotating first.", command.CurrentDisabledReason);
        Assert.Null(tab.RedactIfCreated);
    }

    [AvaloniaFact]
    public async Task TheFormCommandsAreLockedWhileTheModeIsOn()
    {
        using var f = WindowFixture.Create();
        var tab = await OpenReadyAsync(f, "forms-basic.pdf");
        Command(f.Window, "redact.mode").Run!();
        ViewFixture.Pump();
        Assert.True(tab.Redact.IsActive);

        var next = Command(f.Window, "forms.next");
        Assert.False(next.IsEnabled);
        Assert.Equal("Apply or discard the redactions first.", next.CurrentDisabledReason);
    }

    [Fact]
    public void TheModeWaitsForARunningSaveAndForTheFirstPageButCanAlwaysBeLeft()
    {
        PdfCommandRules Rules(bool saving = false, bool ready = true, PdfMode? on = null) =>
            new(() => true, () => 1, () => new PdfPrintInfo(false, true), saveRunning: () => saving, pagesReady: () => ready, modes: new ModeExclusion(m => m == on));

        Assert.True(Rules().Allows("redact.mode"));
        Assert.False(Rules(saving: true).Allows("redact.mode"));
        Assert.Equal("A save is running.", Rules(saving: true).ReasonFor("redact.mode"));
        Assert.False(Rules(ready: false).Allows("redact.mode"));

        // Once on, the same command leaves the mode, whatever else runs.
        Assert.True(Rules(saving: true, ready: false, on: PdfMode.Redact).Allows("redact.mode"));
        Assert.Null(Rules(saving: true, on: PdfMode.Redact).ReasonFor("redact.mode"));

        // Another mode in the way refuses it with that mode's reason.
        Assert.False(Rules(on: PdfMode.Ocr).Allows("redact.mode"));
        Assert.Equal(PdfStrings.ModeOcrReason, Rules(on: PdfMode.Ocr).ReasonFor("redact.mode"));
    }
}
