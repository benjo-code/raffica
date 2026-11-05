using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>Closing a tab, leaving the mode and discarding with an open page plan: the question, its three answers and what each leaves behind.</summary>
public sealed class OrganizeCloseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool Has(Dialog dialog, string name) => dialog.GetVisualDescendants().OfType<Button>().Any(b => b.Name == name);

    private static async Task<Dialog> CardWithAsync(WindowFixture fixture, string name)
    {
        await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is { } d && Has(d, name));
        return fixture.Window.Host.ActiveDialog!;
    }

    private static async Task PressAsync(WindowFixture fixture, string name)
    {
        var card = await CardWithAsync(fixture, name);
        PrintHost.Find<Button>(card, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PrintHost.Pump();
    }

    private static async Task<PdfTab> OpenAsync(WindowFixture fixture, string file = "pages-marked.pdf", bool change = true)
    {
        var tab = await fixture.OpenAsync(file);
        await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        fixture.Window.ToggleOrganize();
        await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: true });
        if (change)
        {
            tab.Organize.Selection.Select(1);
            Assert.True(tab.Organize.Rotate(90).IsDone);
        }

        return tab;
    }

    private static WindowFixture Fixture(string? savePath = null, TimeProvider? time = null) =>
        WindowFixture.Create(time: time, savePicker: _ => Task.FromResult(savePath));

    [AvaloniaFact]
    public async Task CloseWithOpenPlanAsksAndDiscardKeepsTheFileUntouched()
    {
        using var fixture = Fixture();
        var tab = await OpenAsync(fixture);
        var bytes = await File.ReadAllBytesAsync(tab.Path!, Ct);
        Assert.True(tab.IsDirty);

        var close = fixture.Window.CloseActiveTabAsync();
        var card = await CardWithAsync(fixture, "PART_Discard");

        // Its own wording: the plan is "Apply…", never "Save".
        Assert.Equal("Discard the page changes?", card.Title);
        Assert.True(Has(card, "PART_Apply"));
        Assert.False(Has(card, "PART_Save"));
        Assert.False(Has(card, "PART_DontSave"));
        Assert.Equal("Apply…", PrintHost.Find<Button>(card, "PART_Apply").Content);

        await PressAsync(fixture, "PART_Discard");
        await close.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        PrintHost.Pump();

        Assert.DoesNotContain(tab, fixture.Window.Workspace.Tabs);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(tab.Path!, Ct));
    }

    [AvaloniaFact]
    public async Task ApplyChoiceClosesOnlyAfterASuccessfulApply()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-close-").FullName;
        try
        {
            var output = Path.Combine(folder, "out.pdf");
            var time = new ManualTime();
            using var fixture = Fixture(output, time);
            var tab = await OpenAsync(fixture);

            // Apply… opens the Apply card; Back there ends the flow without a write, so the tab and its plan stay.
            var close = fixture.Window.CloseActiveTabAsync();
            await PressAsync(fixture, "PART_Apply");
            await PressAsync(fixture, "PART_Back");
            await close.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            Assert.Contains(tab, fixture.Window.Workspace.Tabs);
            Assert.True(tab.OrganizeIfCreated is { IsActive: true });
            Assert.False(File.Exists(output));

            // The second time the apply works and the tab goes.
            close = fixture.Window.CloseActiveTabAsync();
            await PressAsync(fixture, "PART_Apply");
            var apply = await CardWithAsync(fixture, "PART_SaveAs");
            time.Advance(ArmingLock.DelayMilliseconds + 100);
            PrintHost.Find<Button>(apply, "PART_SaveAs").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await close.WaitAsync(TimeSpan.FromSeconds(60), Ct);
            PrintHost.Pump();

            Assert.DoesNotContain(tab, fixture.Window.Workspace.Tabs);
            Assert.True(File.Exists(output));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task CancelKeepsTheTabAndThePlan()
    {
        using var fixture = Fixture();
        var tab = await OpenAsync(fixture);
        var version = tab.Organize.Version;

        var close = fixture.Window.CloseActiveTabAsync();
        await PressAsync(fixture, "PART_Cancel");
        await close.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Contains(tab, fixture.Window.Workspace.Tabs);
        Assert.True(tab.Organize.IsActive);
        Assert.False(tab.Organize.Plan.IsIdentity);
        Assert.Equal(version, tab.Organize.Version);
        Assert.True(tab.IsDirty);
        Assert.Null(fixture.Window.Host.ActiveDialog);
    }

    [AvaloniaFact]
    public async Task IdentityPlanClosesWithoutAsking()
    {
        using var fixture = Fixture();
        var tab = await OpenAsync(fixture, change: false);
        Assert.True(tab.Organize.Plan.IsIdentity);

        await fixture.Window.CloseActiveTabAsync().WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Null(fixture.Window.Host.ActiveDialog);
        Assert.DoesNotContain(tab, fixture.Window.Workspace.Tabs);
    }

    [AvaloniaFact]
    public async Task LeavingTheModeWithChangesAsks()
    {
        using var fixture = Fixture();
        var tab = await OpenAsync(fixture);

        // Cancel keeps the mode and the plan.
        fixture.Window.ToggleOrganize();
        var card = await CardWithAsync(fixture, "PART_Discard");
        Assert.Equal("Discard the page changes?", card.Title);
        await PressAsync(fixture, "PART_Cancel");
        Assert.True(tab.Organize.IsActive);
        Assert.False(tab.Organize.Plan.IsIdentity);

        // Discard ends it.
        fixture.Window.ToggleOrganize();
        await PressAsync(fixture, "PART_Discard");
        await ViewFixture.WaitAsync(() => !tab.Organize.IsActive);
        Assert.False(tab.IsDirty);

        // An unchanged plan is left without a question.
        fixture.Window.ToggleOrganize();
        await ViewFixture.WaitAsync(() => tab.Organize.IsActive);
        fixture.Window.ToggleOrganize();
        await ViewFixture.WaitAsync(() => !tab.Organize.IsActive);
        Assert.Null(fixture.Window.Host.ActiveDialog);
    }

    [AvaloniaFact]
    public async Task DiscardRestoresTheIdentityAndTheCleanTab()
    {
        using var fixture = Fixture();
        var tab = await OpenAsync(fixture);
        var bytes = await File.ReadAllBytesAsync(tab.Path!, Ct);
        var pages = tab.Session.Document!.PageCount;
        Assert.True(tab.IsDirty);

        fixture.Window.DiscardPlan();
        await PressAsync(fixture, "PART_Discard");
        await ViewFixture.WaitAsync(() => !tab.Organize.IsActive);

        Assert.False(tab.IsDirty);
        Assert.Contains(tab, fixture.Window.Workspace.Tabs);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(tab.Path!, Ct));

        // The next session starts from the identity of the unchanged file.
        tab.Organize.Enter();
        Assert.True(tab.Organize.Plan.IsIdentity);
        Assert.Equal(pages, tab.Organize.Plan.Slots.Count);
        Assert.False(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task CloseAllAsksForEveryTabWithAPlan()
    {
        using var fixture = Fixture();
        var first = await OpenAsync(fixture);
        var second = await OpenAsync(fixture, "text-rich.pdf");

        // The window asks for the tabs in turn: Discard for the first, Cancel for the second keeps the window.
        fixture.Window.Close();
        var card = await CardWithAsync(fixture, "PART_Discard");
        Assert.Equal("Discard the page changes?", card.Title);
        Assert.Same(first, fixture.Window.Workspace.Active);
        await PressAsync(fixture, "PART_Discard");
        await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is not null && ReferenceEquals(fixture.Window.Workspace.Active, second));
        await PressAsync(fixture, "PART_Cancel");
        await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is null);
        Assert.True(fixture.Window.IsVisible);

        // Asked again, both are asked once more and the window goes.
        fixture.Window.Close();
        await PressAsync(fixture, "PART_Discard");
        await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is not null && ReferenceEquals(fixture.Window.Workspace.Active, second));
        await PressAsync(fixture, "PART_Discard");
        await PrintHost.UntilAsync(() => !fixture.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task ClosingDuringAnApplyJobWaitsOrCancelsLikeASave()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-close-").FullName;
        try
        {
            var output = Path.Combine(folder, "out.pdf");
            var time = new ManualTime();
            using var fixture = Fixture(output, time);
            var tab = await OpenAsync(fixture);
            var bytes = await File.ReadAllBytesAsync(tab.Path!, Ct);
            var started = new TaskCompletionSource();
            fixture.Window.Applying.Rewriter = async (_, _, _, _, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("unreachable");
            };

            var apply = fixture.Window.ApplyActiveAsync();
            var card = await CardWithAsync(fixture, "PART_SaveAs");
            time.Advance(ArmingLock.DelayMilliseconds + 100);
            PrintHost.Find<Button>(card, "PART_SaveAs").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            Assert.True(fixture.Window.Applying.IsRunning);

            // The job card holds the window: a close asked meanwhile waits (the tab stays, the job runs on).
            Assert.False(await fixture.Window.Workspace.CloseAsync(tab).WaitAsync(TimeSpan.FromSeconds(30), Ct));
            Assert.Contains(tab, fixture.Window.Workspace.Tabs);
            Assert.True(fixture.Window.Applying.IsRunning);

            // A tab that goes anyway takes the job with it: the job is canceled before the target is replaced.
            tab.Dispose();
            Assert.False(await apply.WaitAsync(TimeSpan.FromSeconds(30), Ct));
            PrintHost.Pump();
            Assert.False(File.Exists(output));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(tab.Path!, Ct));
            Assert.False(fixture.Window.Applying.IsRunning);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
