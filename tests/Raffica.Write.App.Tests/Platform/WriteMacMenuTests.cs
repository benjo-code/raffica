using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Frame;
using Raffica.Write.Platform.MacOS;
using Xunit;

namespace Raffica.WriteApp.Tests.Platform;

/// <summary>The macOS menu of Write is plain Avalonia menu objects, so its structure and states are tested on every system.</summary>
public sealed class WriteMacMenuTests
{
    private sealed class FakeWindow : IWriteMenuWindow
    {
        public bool DialogOpen { get; set; }

        public bool HasTab { get; set; }

        public IReadOnlyList<RecentFile> RecentFiles { get; set; } = [];

        public List<string> Calls { get; } = [];

        public event Action? StatesChanged;

        public void Open() => Calls.Add("open");

        public void CloseActiveTab() => Calls.Add("close");

        public void OpenRecent(RecentFile file) => Calls.Add("recent " + file.Name);

        public void OpenSettings() => Calls.Add("settings");

        public void Minimize() => Calls.Add("minimize");

        public void ToggleWindowZoom() => Calls.Add("zoom");

        public void BringAllToFront() => Calls.Add("front");

        public void Raise() => StatesChanged?.Invoke();
    }

    private static bool Enabled(NativeMenuItem item) => item.Command!.CanExecute(null);

    private static NativeMenuItem Item(NativeMenu menu, string header) => menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == header);

    [AvaloniaFact]
    public void TheWindowMenuHasFileAndWindowAndActsOnItsWindow()
    {
        var window = new FakeWindow();
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();

        Assert.Equal(["File", "Window"], menu.Menu.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray());
        var file = Item(menu.Menu, "File").Menu!;
        var open = Item(file, "Open…");
        var close = Item(file, "Close tab");
        Assert.Equal("Meta+O", open.Gesture!.ToString().Replace("Cmd", "Meta", StringComparison.Ordinal));
        Assert.True(Enabled(open));

        // Close tab needs a tab; a card that is open disables everything.
        Assert.False(Enabled(close));
        window.HasTab = true;
        Assert.True(Enabled(close));
        close.Command!.Execute(null);
        open.Command!.Execute(null);
        window.DialogOpen = true;
        Assert.False(Enabled(open));
        Assert.False(Enabled(close));
        open.Command.Execute(null);
        Assert.Equal(["close", "open"], window.Calls);
    }

    [AvaloniaFact]
    public void TheRecentFilesFollowTheWindowAndTheMenuIsReadAgainWhenTheWindowSaysSo()
    {
        var window = new FakeWindow();
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var recent = Item(Item(menu.Menu, "File").Menu!, "Open Recent").Menu!;
        Assert.Equal("No recent files", Assert.Single(recent.Items.OfType<NativeMenuItem>()).Header);

        window.RecentFiles = [new RecentFile("a.docx", @"C:\x", DateTimeOffset.UtcNow), new RecentFile("b.docx", @"C:\x", DateTimeOffset.UtcNow)];
        window.Raise();

        var items = recent.Items.OfType<NativeMenuItem>().ToList();
        Assert.Equal(["a.docx", "b.docx"], items.Select(i => i.Header!).ToArray());
        items[1].Command!.Execute(null);
        Assert.Equal(["recent b.docx"], window.Calls);
    }

    [AvaloniaFact]
    public void TheApplicationMenuHasSettingsAndOpenOnlyWithoutAWindow()
    {
        IWriteMenuWindow? current = null;
        var opened = 0;
        var menu = new WriteMacMenu(() => current, () =>
        {
            opened++;
            return Task.CompletedTask;
        }, WriteMenuPart.App);
        menu.Refresh();

        var settings = Item(menu.Menu, "Settings…");
        var open = Item(menu.Menu, "Open…");
        Assert.False(Enabled(settings));
        Assert.True(open.IsVisible);
        Assert.True(Enabled(open));
        open.Command!.Execute(null);
        Assert.Equal(1, opened);

        var window = new FakeWindow();
        current = window;
        menu.Refresh();
        Assert.False(open.IsVisible);
        Assert.True(Enabled(settings));
        settings.Command!.Execute(null);
        Assert.Equal(["settings"], window.Calls);
    }

    [AvaloniaFact]
    public void AMenuBinderLetsGoOfAClosedWindow()
    {
        var first = new FakeWindow();
        var second = new FakeWindow();
        var current = (IWriteMenuWindow?)first;
        var menu = new WriteMacMenu(() => current, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        current = second;
        menu.Refresh();

        // The first window's states no longer reach the menu (no handler is left on it).
        first.HasTab = true;
        first.Raise();
        var close = Item(Item(menu.Menu, "File").Menu!, "Close tab");
        Assert.False(Enabled(close));
        second.HasTab = true;
        second.Raise();
        Assert.True(Enabled(close));
    }
}
