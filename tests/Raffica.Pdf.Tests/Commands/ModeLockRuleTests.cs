using Raffica.Core.Pdf;
using Raffica.Pdf.Commands;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

/// <summary>The rules ask <see cref="ModeExclusion"/> (here with a fake probe, the sessions of M2.5 are inert yet): what a mode locks, and the reason it gives.</summary>
public sealed class ModeLockRuleTests
{
    private static readonly string[] OutputIds = ["pages.extract", "pages.split", "pages.exportImages"];
    private static readonly PdfMode[] Locking = [PdfMode.Redact, PdfMode.Ocr, PdfMode.TextEdit];

    private static PdfCommandRules Rules(PdfMode? on, bool pagesReady = true, bool annotateToolOn = false)
    {
        var modes = new ModeExclusion(mode => mode == on || (annotateToolOn && mode == PdfMode.Annotate));
        return new PdfCommandRules(() => true, () => 1, () => new PdfPrintInfo(false, true), pagesReady: () => pagesReady, hasForm: () => true, modes: modes);
    }

    [Fact]
    public void OrganizeCannotBeEnteredWhileAnotherModeIsOnAndGivesItsReason()
    {
        foreach (var mode in Locking)
        {
            var rules = Rules(mode);
            Assert.False(rules.Allows("pages.organize"), mode.ToString());
            Assert.False(string.IsNullOrWhiteSpace(rules.ReasonFor("pages.organize")), mode.ToString());
        }

        Assert.Equal(PdfStrings.ModeRedactReason, Rules(PdfMode.Redact).ReasonFor("pages.organize"));
        Assert.True(Rules(null).Allows("pages.organize"));
        Assert.Null(Rules(null).ReasonFor("pages.organize"));
    }

    [Fact]
    public void AnAnnotateToolDoesNotBlockOrganizeBecauseEnteringEndsIt()
    {
        var rules = Rules(null, annotateToolOn: true);
        Assert.True(rules.Allows("pages.organize"));
        Assert.Null(rules.ReasonFor("pages.organize"));

        // The pair table stays strict: the exception is a named rule of starting Organize only.
        var modes = new ModeExclusion(mode => mode == PdfMode.Annotate);
        Assert.False(modes.CanEnter(PdfMode.Organize, out _));
        Assert.True(modes.CanStart(PdfMode.Organize, out var reason));
        Assert.Null(reason);
        Assert.False(modes.CanStart(PdfMode.Redact, out _));
    }

    [Fact]
    public void TheOutputCommandsAreOffWhileAnotherModeIsOnWithItsReason()
    {
        foreach (var mode in Locking)
        {
            var rules = Rules(mode);
            foreach (var id in OutputIds)
            {
                Assert.False(rules.Allows(id), mode + " " + id);
                Assert.False(string.IsNullOrWhiteSpace(rules.ReasonFor(id)), mode + " " + id);
            }
        }

        Assert.Equal(PdfStrings.ModeOcrReason, Rules(PdfMode.Ocr).ReasonFor("pages.split"));
        Assert.All(OutputIds, id => Assert.True(Rules(null).Allows(id), id));
    }

    [Fact]
    public void PrintStaysWhileAnotherModeIsOn()
    {
        foreach (var mode in Locking)
        {
            Assert.True(Rules(mode).Allows("file.print"), mode.ToString());
        }
    }

    [Fact]
    public void TheFormCommandsAndToolsAreOffWhileAnotherModeIsOn()
    {
        foreach (var mode in Locking)
        {
            var rules = Rules(mode);
            Assert.False(rules.Allows("forms.next"), mode.ToString());
            Assert.False(rules.Allows("annotate.highlight"), mode.ToString());
            Assert.Equal(PdfStrings.Get(mode == PdfMode.Redact ? "ModeRedactReason" : mode == PdfMode.Ocr ? "ModeOcrReason" : "ModeTextEditReason"), rules.ReasonFor("forms.next"));
        }

        Assert.True(Rules(null).Allows("forms.next"));
    }

    [Fact]
    public void TheFormLockAsksTheExclusionForTheFormsMode()
    {
        foreach (var mode in Locking.Append(PdfMode.Organize))
        {
            Assert.NotNull(new ModeExclusion(m => m == mode).RefusalFor(PdfMode.Forms));
        }

        Assert.Null(new ModeExclusion(m => m == PdfMode.Annotate).RefusalFor(PdfMode.Forms));
        Assert.Null(new ModeExclusion().RefusalFor(PdfMode.Forms));
    }

    // A run in another tab sets saveRunning too (the window's busy list includes OcrBusy): the reason names the run, not a save.
    [Fact]
    public void ARunInAnotherTabGivesTheRunAsTheReasonAndNeverASave()
    {
        var rules = new PdfCommandRules(() => true, () => 1, () => new PdfPrintInfo(false, true), pagesReady: () => true, hasForm: () => true,
            saveRunning: () => true, ocrRunning: () => true, planChanged: () => true, redactMarked: () => true, modes: new ModeExclusion(mode => false));
        foreach (var id in new[] { "pages.organize", "redact.mode", "text.edit", "pages.extract", "pages.split", "pages.exportImages", "redact.apply", "pages.apply" })
        {
            Assert.Equal(PdfStrings.ModeOcrReason, rules.ReasonFor(id));
        }

        // Without a run the same busy state is a save.
        var saving = new PdfCommandRules(() => true, () => 1, () => new PdfPrintInfo(false, true), pagesReady: () => true, hasForm: () => true,
            saveRunning: () => true, modes: new ModeExclusion(mode => false));
        Assert.Equal(PdfStrings.SaveBusy, saving.ReasonFor("redact.mode"));
        Assert.Equal(PdfStrings.SaveBusy, saving.ReasonFor("text.edit"));
        Assert.Equal(PdfStrings.SaveBusy, saving.ReasonFor("pages.organize"));
    }
}
