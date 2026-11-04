using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Settings;
using Raffica.Core.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Viewing;

public sealed class WindowCommandTests
{
    [AvaloniaFact]
    public async Task GoToPageOpensTheCardAndJumps()
    {
        using var windows = WindowFixture.Create();
        var tab = await windows.OpenAsync("pages-marked.pdf");
        var view = windows.Window.ViewOf(tab)!;
        Assert.True(view.PageCount >= 4);

        var asking = windows.Window.GoToPageAsync();
        Assert.True(windows.Window.Host.IsOpen);
        var dialog = windows.Window.Host.ActiveDialog!;
        var field = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_Page");
        Assert.Equal("1", field.Text);
        field.Text = "4";
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await asking;
        ViewFixture.Pump();

        Assert.False(windows.Window.Host.IsOpen);
        Assert.Equal(3, view.CurrentPage);
        Assert.Equal($"Page 4 of {view.PageCount}", windows.Window.Status.Position);

        // Cancel leaves the view where it is.
        asking = windows.Window.GoToPageAsync();
        windows.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Cancel")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await asking;
        Assert.Equal(3, view.CurrentPage);
    }

    [AvaloniaFact]
    public async Task AVanishedFileLeavesTheRecentListWhenItsEntryIsClicked()
    {
        var gone = Path.Combine(Path.GetTempPath(), "raffica-gone-" + Guid.NewGuid().ToString("N") + ".pdf");
        var here = Path.GetFullPath(TestData.Pdf("text-basic.pdf"));
        using var windows = WindowFixture.Create(folder =>
        {
            var recent = new RecentFiles();
            recent.Touch(here, DateTimeOffset.UtcNow.AddMinutes(-1));
            recent.Touch(gone, DateTimeOffset.UtcNow);
            Assert.True(new RecentFilesStore(Path.Combine(folder, "recent.json")).TrySave(recent));
        });
        await windows.LoadRecentAsync();
        Assert.Equal(2, windows.Window.Start.Recent.Count);

        await windows.Window.OpenRecentAsync(windows.Window.Start.Recent[0]);
        ViewFixture.Pump();
        Assert.True(windows.Window.Host.IsOpen, "the error card must appear");
        windows.Window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();

        Assert.Equal(["text-basic.pdf"], windows.Window.Start.Recent.Select(r => r.Name));
        await windows.Window.Recent.FlushAsync();
        Assert.Null(new RecentFilesStore(windows.RecentPath).Load().TryGet(gone));
        Assert.NotNull(new RecentFilesStore(windows.RecentPath).Load().TryGet(here));

        // A file that exists stays in the list when it is opened.
        var tab = await windows.Window.OpenRecentAsync(windows.Window.Start.Recent[0]);
        Assert.NotNull(tab);
        Assert.Equal(PageLayoutMode.Continuous, windows.Window.ViewOf(tab)!.Layout);
    }
}
