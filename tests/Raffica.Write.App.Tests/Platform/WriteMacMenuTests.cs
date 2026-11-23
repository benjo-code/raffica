using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Frame;
using Raffica.Write.Commands;
using Raffica.Write.Platform.MacOS;
using Xunit;

namespace Raffica.WriteApp.Tests.Platform;

/// <summary>The macOS menu of Write is plain Avalonia menu objects, so its structure and states are tested on every system.</summary>
public sealed class WriteMacMenuTests
{
    internal sealed class FakeWindow : IWriteMenuWindow
    {
        public bool DialogOpen { get; set; }

        public bool HasTab { get; set; }

        public IReadOnlyList<RecentFile> RecentFiles { get; set; } = [];

        public List<string> Calls { get; } = [];

        public event Action? StatesChanged;

        public void Open() => Calls.Add("open");

        public void NewDocument() => Calls.Add("new");

        public void CloseActiveTab() => Calls.Add("close tab");

        public void CloseTabOrWindow() => Calls.Add("close");

        public HashSet<string> Allowed { get; } = [];

        public HashSet<string> On { get; } = [];

        public bool CanRun(string commandId) => Allowed.Contains(commandId);

        public void RunCommand(string commandId) => Calls.Add("run " + commandId);

        public void RunCommand(string commandId, object? payload) => Calls.Add("run " + commandId + " " + payload);

        public bool IsOn(string commandId) => On.Contains(commandId);

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
    public void TheWindowMenuHasFileViewAndWindowAndActsOnItsWindow()
    {
        var window = new FakeWindow();
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();

        Assert.Equal(["File", "Edit", "Format", "Layout", "View", "Window"], menu.Menu.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray());
        var file = Item(menu.Menu, "File").Menu!;
        var open = Item(file, "Open…");
        var close = Item(file, "Close tab");
        Assert.Equal("Meta+O", open.Gesture!.ToString().Replace("Cmd", "Meta", StringComparison.Ordinal));
        Assert.True(Enabled(open));

        // Close closes the tab, or the window when there is no tab (Cmd+W of every Mac app); a card that is open disables everything.
        Assert.True(Enabled(close));
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
    public void TheFileMenuHasSaveAndSaveAsThatRunTheCatalogCommandsByIdWhenTheRulesAllowThem()
    {
        var window = new FakeWindow { HasTab = true };
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var file = Item(menu.Menu, "File").Menu!;
        var save = Item(file, "Save");
        var saveAs = Item(file, "Save as");

        Assert.Equal("Meta+S", save.Gesture!.ToString().Replace("Cmd", "Meta", StringComparison.Ordinal));
        Assert.Equal(Avalonia.Input.KeyGesture.Parse("Meta+Shift+S"), saveAs.Gesture);

        // The rules decide: Save of a document without changes is disabled, Save as is not.
        window.Allowed.Add(WriteCommandIds.SaveAs);
        window.Raise();
        Assert.False(Enabled(save));
        Assert.True(Enabled(saveAs));
        saveAs.Command!.Execute(null);
        window.Allowed.Add(WriteCommandIds.Save);
        window.Raise();
        save.Command!.Execute(null);
        Assert.Equal(["run file.saveAs", "run file.save"], window.Calls);

        // A card that is open makes the window modal.
        window.DialogOpen = true;
        Assert.False(Enabled(save));
        Assert.False(Enabled(saveAs));
    }

    [AvaloniaFact]
    public void TheViewMenuRunsTheCommandsOfTheCatalogAndShowsTheView()
    {
        var window = new FakeWindow { HasTab = true };
        window.Allowed.UnionWith([WriteCommandIds.PageView, WriteCommandIds.WritingView, WriteCommandIds.ZoomIn, WriteCommandIds.FitWidth, WriteCommandIds.Outline]);
        window.On.Add(WriteCommandIds.PageView);
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var view = Item(menu.Menu, "View").Menu!;
        Assert.Equal(["Page view", "Writing view", "Zoom in", "Zoom out", "Actual size", "Fit width", "Fit page", "Outline", "Formatting"], view.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header!).ToArray());

        var page = Item(view, "Page view");
        var writing = Item(view, "Writing view");
        Assert.True(page.IsChecked);
        Assert.False(writing.IsChecked);
        window.On.Clear();
        window.On.Add(WriteCommandIds.WritingView);
        window.Raise();
        Assert.False(page.IsChecked);
        Assert.True(writing.IsChecked);

        // What the rules do not allow is disabled (zoom out at the smallest zoom, actual size and fit page here); the others run by id.
        Assert.False(Enabled(Item(view, "Zoom out")));
        Assert.False(Enabled(Item(view, "Actual size")));
        Assert.True(Enabled(Item(view, "Zoom in")));
        Item(view, "Zoom in").Command!.Execute(null);
        Item(view, "Fit width").Command!.Execute(null);
        Item(view, "Zoom out").Command!.Execute(null);
        Assert.True(Enabled(Item(view, "Outline")));
        Item(view, "Outline").Command!.Execute(null);
        Assert.Equal(["run view.zoomIn", "run view.fitWidth", "run view.outline"], window.Calls);

        // No tab, no view commands; a card that is open disables them too.
        window.HasTab = false;
        Assert.False(Enabled(Item(view, "Zoom in")));
        window.HasTab = true;
        window.DialogOpen = true;
        Assert.False(Enabled(Item(view, "Zoom in")));
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

        // The first window's states no longer reach the menu (no handler is left on it): the zoom items follow the second window only.
        var zoomIn = Item(Item(menu.Menu, "View").Menu!, "Zoom in");
        first.HasTab = true;
        first.Allowed.Add(WriteCommandIds.ZoomIn);
        first.Raise();
        Assert.False(Enabled(zoomIn));
        second.HasTab = true;
        second.Allowed.Add(WriteCommandIds.ZoomIn);
        second.Raise();
        Assert.True(Enabled(zoomIn));
    }
}
