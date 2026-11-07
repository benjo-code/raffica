using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Updates;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class UpdateCardTests
{
    private static Button Find(WindowFixture f, string name) =>
        f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // The continuation of a closed card (it frees the window for the next offer) runs a turn after the card is gone.
    private static async Task SettleAsync()
    {
        await Task.Delay(100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
    }

    private static async Task ReachAsync(WindowFixture f, UpdateKit kit, UpdateFlow flow)
    {
        kit.Release("0.2.0");
        f.Window.UpdateFlows = _ => flow;
        _ = f.Window.CheckForUpdatesAsync();
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);
    }

    [AvaloniaFact]
    public async Task LaterHidesTheCardAndKeepsTheButton()
    {
        using var kit = new UpdateKit();
        using var f = WindowFixture.Create();
        await ReachAsync(f, kit, kit.Flow());
        Assert.Equal(0, kit.Applier.Calls);

        Click(Find(f, "PART_Later"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);

        // The offer stays: the button is still there, nothing was downloaded, and the button opens the same card again.
        Assert.Equal(UpdateStage.Available, f.Window.UpdateState.Stage);
        Assert.True(f.Window.UpdateButton!.IsVisible);
        Assert.Equal(0, kit.Source.DownloadCalls);
        await SettleAsync();
        Click(f.Window.UpdateButton);
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);

        // Esc on the card is Later as well.
        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
        Assert.True(f.Window.UpdateButton.IsVisible);
        Assert.Equal(0, kit.Source.DownloadCalls);
    }

    [AvaloniaFact]
    public async Task DebOffersOnlyTheDownloadPage()
    {
        using var kit = new UpdateKit();
        var launched = new List<Uri>();
        using var f = WindowFixture.Create(launcher: launched.Add);
        await ReachAsync(f, kit, kit.Flow(PackageForm.Deb));

        var buttons = f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Select(b => b.Name).ToList();
        Assert.DoesNotContain("PART_Install", buttons);
        Assert.Contains("PART_Page", buttons);
        Assert.Contains("PART_Later", buttons);

        // The page opens only after the card that shows the address in full; Cancel there opens nothing.
        Click(Find(f, "PART_Page"));
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.LinkTitle);
        Assert.Equal(UpdateTrust.ReleasesPageUrl, f.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Address").Text);
        Click(Find(f, "PART_Cancel"));
        await ViewFixture.WaitAsync(() => !f.Window.Host.IsOpen);
        Assert.Empty(launched);
        Assert.Equal(0, kit.Source.DownloadCalls);
        Assert.True(f.Window.UpdateButton!.IsVisible);

        // Confirmed (after the arming delay), it opens the page.
        await SettleAsync();
        Click(f.Window.UpdateButton);
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.UpdateTitle);
        Click(Find(f, "PART_Page"));
        await ViewFixture.WaitAsync(() => f.Window.Host.IsOpen && f.Window.Host.ActiveDialog!.Title == PdfStrings.LinkTitle);
        await Task.Delay(ArmingLock.DelayMilliseconds + 100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Click(Find(f, "PART_Open"));
        await ViewFixture.WaitAsync(() => launched.Count == 1);
        Assert.Equal(UpdateTrust.ReleasesPageUrl, launched[0].ToString());
    }
}
