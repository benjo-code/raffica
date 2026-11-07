using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>Closing a tab, closing the window and leaving the mode with marked redactions: the question, its three answers and what each leaves behind.</summary>
public sealed class RedactCloseTests
{
    private const string Term = "Zephyrstraße 17";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<RedactUiRig> MarkedAsync(string file = "redact-covered.pdf")
    {
        var rig = await RedactUiRig.OpenAsync(file);
        await rig.EnterModeAsync();
        await rig.MarkTermAsync(Term);
        return rig;
    }

    private static bool Has(Dialog dialog, string name) => RedactUiRig.NamesOf(dialog).Contains(name);

    [AvaloniaFact]
    public async Task CloseWithMarksAsksAndDiscardKeepsTheFileUntouched()
    {
        using var rig = await MarkedAsync();
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var version = rig.Session.Version;
        Assert.False(rig.Tab.IsDirty);

        // The question has its own words: Apply…, Discard, Cancel, never "Save".
        var close = rig.Window.CloseActiveTabAsync();
        var card = await rig.CardWithAnyAsync("PART_Discard");
        Assert.Equal("Discard the redactions?", card.Title);
        Assert.True(Has(card, "PART_Apply"));
        Assert.False(Has(card, "PART_Save"));
        Assert.False(Has(card, "PART_DontSave"));
        Assert.Equal("Apply…", PrintHost.Find<Button>(card, "PART_Apply").Content);

        // Cancel keeps the tab, the mode and the marks.
        RedactUiRig.Click(card, "PART_Cancel");
        await close.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Contains(rig.Tab, rig.Window.Workspace.Tabs);
        Assert.True(rig.Session.IsActive);
        Assert.Equal(version, rig.Session.Version);
        Assert.Null(rig.Window.Host.ActiveDialog);

        // Discard lets the tab go; the file was never touched.
        close = rig.Window.CloseActiveTabAsync();
        card = await rig.CardWithAnyAsync("PART_Discard");
        RedactUiRig.Click(card, "PART_Discard");
        await close.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        PrintHost.Pump();
        Assert.DoesNotContain(rig.Tab, rig.Window.Workspace.Tabs);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal([rig.Path], Directory.GetFiles(rig.Folder));
    }

    [AvaloniaFact]
    public async Task ApplyChoiceClosesOnlyAfterASuccessfulApply()
    {
        using var rig = await MarkedAsync();
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var output = Path.Combine(rig.Folder, "redacted.pdf");
        rig.Pick = output;

        // Apply… opens the Apply card; Back there ends the flow without a write, so the tab and its marks stay.
        var close = rig.Window.CloseActiveTabAsync();
        RedactUiRig.Click(await rig.CardWithAnyAsync("PART_Apply"), "PART_Apply");
        var apply = await rig.CardWithAnyAsync("PART_SaveAs");
        Assert.Equal("Apply redactions", apply.Title);
        RedactUiRig.Click(apply, "PART_Back");
        await close.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Contains(rig.Tab, rig.Window.Workspace.Tabs);
        Assert.True(rig.Session.IsActive);
        Assert.False(File.Exists(output));

        // The second time the apply works; the result card is read, and the tab goes.
        close = rig.Window.CloseActiveTabAsync();
        RedactUiRig.Click(await rig.CardWithAnyAsync("PART_Apply"), "PART_Apply");
        RedactUiRig.Click(await rig.CardWithAnyAsync("PART_SaveAs"), "PART_SaveAs");
        var result = await rig.CardWithAnyAsync("PART_Checked");
        RedactUiRig.Click(result, "PART_Ok");
        await close.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        PrintHost.Pump();

        Assert.DoesNotContain(rig.Tab, rig.Window.Workspace.Tabs);
        Assert.True(File.Exists(output));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task UpdateWithMarksAsks()
    {
        // An update that restarts the app closes the window like any close: every tab with marked redactions is asked, nothing is lost silently.
        using var rig = await MarkedAsync();
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var version = rig.Session.Version;

        rig.Window.Close();
        var card = await rig.CardWithAnyAsync("PART_Discard");
        Assert.Equal("Discard the redactions?", card.Title);
        RedactUiRig.Click(card, "PART_Cancel");
        await PrintHost.UntilAsync(() => rig.Window.Host.ActiveDialog is null);

        // Cancel keeps the window, the tab and the marks as they were.
        Assert.True(rig.Window.IsVisible);
        Assert.Contains(rig.Tab, rig.Window.Workspace.Tabs);
        Assert.True(rig.Session.IsActive);
        Assert.Equal(version, rig.Session.Version);

        // Discard lets the window go; the file is as it was.
        rig.Window.Close();
        card = await rig.CardWithAnyAsync("PART_Discard");
        RedactUiRig.Click(card, "PART_Discard");
        await PrintHost.UntilAsync(() => !rig.Window.IsVisible);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task DiscardThenSaveWritesTheMarksOnlyAndNeverApplies()
    {
        // Unsaved marks next to the redactions: Discard drops the redactions (they go with the tab), the marks are asked next, and their save never applies the redactions.
        using var rig = await RedactUiRig.OpenAsync("redact-covered.pdf");
        Assert.NotNull(await AnnotatedFile.HighlightAsync(rig.Tab.Annotations));
        await rig.Tab.Annotations.FlushAsync();
        await rig.EnterModeAsync();
        await rig.MarkTermAsync(Term);
        var before = await File.ReadAllBytesAsync(rig.Path, Ct);

        var close = rig.Window.Workspace.CloseAsync(rig.Tab);
        RedactUiRig.Click(await rig.CardWithAnyAsync("PART_Discard"), "PART_Discard");
        var question = await rig.CardWithAnyAsync("PART_Save");
        Assert.Equal("Save changes?", question.Title);
        RedactUiRig.Click(question, "PART_Save");

        Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(60), Ct));
        Assert.DoesNotContain(rig.Tab, rig.Window.Workspace.Tabs);
        var after = await File.ReadAllBytesAsync(rig.Path, Ct);
        Assert.NotEqual(before, after);

        // The marks were saved into the file; nothing was redacted and nothing else was written.
        Assert.Equal([rig.Path], Directory.GetFiles(rig.Folder));
        var read = await RedactUiAcceptanceTests.ReadAsync(rig.Path);
        Assert.Contains(Term, read.AllText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task DiscardThenCancelAtTheMarksQuestionKeepsThePlan()
    {
        using var rig = await RedactUiRig.OpenAsync("redact-covered.pdf");
        Assert.NotNull(await AnnotatedFile.HighlightAsync(rig.Tab.Annotations));
        await rig.Tab.Annotations.FlushAsync();
        await rig.EnterModeAsync();
        await rig.MarkTermAsync(Term);
        var version = rig.Session.Version;

        var close = rig.Window.Workspace.CloseAsync(rig.Tab);
        RedactUiRig.Click(await rig.CardWithAnyAsync("PART_Discard"), "PART_Discard");
        RedactUiRig.Click(await rig.CardWithAnyAsync("PART_Save"), "PART_Cancel");

        // The guard never ends a plan itself: "Discard" counts only when the tab really closes.
        Assert.False(await close.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Contains(rig.Tab, rig.Window.Workspace.Tabs);
        Assert.True(rig.Session.IsActive);
        Assert.Equal(version, rig.Session.Version);
        Assert.False(rig.Session.Plan.IsEmpty);
    }

    [AvaloniaFact]
    public async Task NothingMarkedClosesWithoutAsking()
    {
        using var rig = await RedactUiRig.OpenAsync("redact-covered.pdf");
        await rig.EnterModeAsync();

        await rig.Window.CloseActiveTabAsync().WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Null(rig.Window.Host.ActiveDialog);
        Assert.DoesNotContain(rig.Tab, rig.Window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public async Task LeavingTheModeAsksAndApplyRunsTheFlow()
    {
        using var rig = await MarkedAsync();
        var version = rig.Session.Version;

        // Cancel keeps the mode.
        rig.Window.ToggleRedact();
        RedactUiRig.Click(await rig.CardWithAnyAsync("PART_Discard"), "PART_Cancel");
        await PrintHost.UntilAsync(() => rig.Window.Host.ActiveDialog is null);
        Assert.True(rig.Session.IsActive);

        // Apply… opens the Apply card (the mode ends with a successful apply, not before).
        rig.Window.ToggleRedact();
        RedactUiRig.Click(await rig.CardWithAnyAsync("PART_Apply"), "PART_Apply");
        var apply = await rig.CardWithAnyAsync("PART_SaveAs");
        Assert.Equal("Apply redactions", apply.Title);
        RedactUiRig.Click(apply, "PART_Back");
        await PrintHost.UntilAsync(() => !rig.Window.Redacting.IsRunning);
        Assert.True(rig.Session.IsActive);
        Assert.Equal(version, rig.Session.Version);

        // Discard ends it.
        rig.Window.ToggleRedact();
        RedactUiRig.Click(await rig.CardWithAnyAsync("PART_Discard"), "PART_Discard");
        await ViewFixture.WaitAsync(() => !rig.Session.IsActive);
    }
}
