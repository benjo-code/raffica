using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Platform.MacOS;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class MacMenuBindingTests : IDisposable
{
    public void Dispose() => PdfStrings.Culture = CultureInfo.GetCultureInfo("en");

    private sealed class FakeMenuWindow : IMenuWindow
    {
        private readonly Dictionary<string, CommandDefinition> _commands = new(StringComparer.Ordinal);

        public FakeMenuWindow()
        {
            foreach (var entry in PdfCommandCatalog.Commands)
            {
                var id = entry.Id;
                Runs[id] = 0;
                Enabled[id] = true;
                Checked[id] = false;
                _commands[id] = new CommandDefinition(
                    id, entry.Name, entry.Icon, entry.FieldCode,
                    Run: () => Runs[id]++,
                    EnabledWhen: () => Enabled[id],
                    CheckedWhen: entry.Toggle ? () => Checked[id] : null);
            }
        }

        public Dictionary<string, int> Runs { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, bool> Enabled { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, bool> Checked { get; } = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = [];

        public CommandDefinition? CommandOf(string id) => _commands.GetValueOrDefault(id);

        public TextBox? FocusedTextBox { get; set; }

        public bool DialogOpen { get; set; }

        public IReadOnlyList<RecentFile> RecentFiles { get; set; } = [];

        public void OpenRecent(RecentFile file) => Calls.Add("recent:" + file.Name);

        public void ShowAbout() => Calls.Add("about");

        public void Minimize() => Calls.Add("minimize");

        public void ToggleWindowZoom() => Calls.Add("zoom");

        public void BringAllToFront() => Calls.Add("front");

        public bool IsFullScreen { get; set; }

        public event Action? StatesChanged;

        public void RaiseStatesChanged() => StatesChanged?.Invoke();

        public bool HasSubscriber => StatesChanged is not null;
    }

    // A text box needs a top level for the clipboard.
    private sealed class HostedBox : IDisposable
    {
        private readonly Window _window;

        public HostedBox(TextBox box)
        {
            _window = new Window { Content = box };
            _window.Show();
            box.Focus();
        }

        public void Dispose() => _window.Close();
    }

    private static IEnumerable<NativeMenuItem> Items(NativeMenu menu)
    {
        foreach (var item in menu.Items.OfType<NativeMenuItem>())
        {
            yield return item;
            if (item.Menu is { } inner)
            {
                foreach (var nested in Items(inner))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<MenuNode> Flatten(MenuNode node) =>
        node is SubmenuNode submenu ? submenu.Items.SelectMany(Flatten).Prepend(node) : [node];

    private static List<CommandNode> CommandNodes() =>
        [.. MacMenuModel.Build().SelectMany(Flatten).OfType<CommandNode>()];

    private static CatalogCommand Catalog(string id) => PdfCommandCatalog.Commands.Single(c => c.Id == id);

    private static NativeMenuItem ItemOf(MacMenuBinder binder, string header) =>
        Items(binder.Menu).First(i => i.Header is string text && text == header);

    private static NativeMenuItem ItemOfCommand(MacMenuBinder binder, string id) => ItemOf(binder, Catalog(id).Name);

    private static bool Can(NativeMenuItem item) => item.Command!.CanExecute(null);

    private static MacMenuBinder Make(FakeMenuWindow? window, List<string>? opened = null)
    {
        var binder = new MacMenuBinder(
            () => window,
            () =>
            {
                opened?.Add("open-without-window");
                return Task.CompletedTask;
            });
        binder.Refresh();
        return binder;
    }

    private static readonly string[] TextIds = [PdfCommandIds.Undo, PdfCommandIds.Redo, PdfCommandIds.Copy, PdfCommandIds.SelectAll];

    [AvaloniaFact]
    public void EveryCommandItemRunsItsCommand()
    {
        var window = new FakeMenuWindow();
        var binder = Make(window);
        foreach (var node in CommandNodes())
        {
            var item = ItemOfCommand(binder, node.CommandId);
            Assert.True(Can(item), node.CommandId);
            item.Command!.Execute(null);
            Assert.Equal(1, window.Runs[node.CommandId]);
        }
    }

    [AvaloniaFact]
    public void EnabledAndCheckedStateFollowTheCommand()
    {
        var window = new FakeMenuWindow();
        var binder = Make(window);
        foreach (var on in new[] { false, true, false })
        {
            foreach (var node in CommandNodes())
            {
                window.Enabled[node.CommandId] = on;
                window.Checked[node.CommandId] = on;
            }

            binder.Refresh();
            foreach (var node in CommandNodes())
            {
                var item = ItemOfCommand(binder, node.CommandId);
                Assert.Equal(on, Can(item));
                if (Catalog(node.CommandId).Toggle)
                {
                    Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
                    Assert.Equal(on, item.IsChecked);
                }
                else
                {
                    Assert.Equal(MenuItemToggleType.None, item.ToggleType);
                    Assert.False(item.IsChecked);
                }
            }
        }
    }

    [AvaloniaFact]
    public void TheGestureIsTheMacShortcutOfTheCatalog()
    {
        var binder = Make(new FakeMenuWindow());
        foreach (var node in CommandNodes())
        {
            var item = ItemOfCommand(binder, node.CommandId);
            if (Catalog(node.CommandId).ShortcutFor(true) is { } shortcut)
            {
                var gesture = Assert.IsType<KeyGesture>(item.Gesture);
                Assert.Equal(shortcut.Key, gesture.Key);
                var expected = (shortcut.Base == ShortcutBase.Command ? KeyModifiers.Meta : shortcut.Base == ShortcutBase.Control ? KeyModifiers.Control : KeyModifiers.None)
                    | (shortcut.Shift ? KeyModifiers.Shift : KeyModifiers.None);
                Assert.Equal(expected, gesture.KeyModifiers);
            }
            else
            {
                Assert.Null(item.Gesture);
            }
        }
    }

    [AvaloniaFact]
    public void TheSystemItemsHaveTheirStandardGestures()
    {
        var binder = Make(new FakeMenuWindow());
        Assert.Equal(new KeyGesture(Key.X, KeyModifiers.Meta), ItemOf(binder, PdfStrings.MenuCut).Gesture);
        Assert.Equal(new KeyGesture(Key.V, KeyModifiers.Meta), ItemOf(binder, PdfStrings.MenuPaste).Gesture);
        Assert.Equal(new KeyGesture(Key.M, KeyModifiers.Meta), ItemOf(binder, PdfStrings.MenuMinimize).Gesture);
        Assert.Equal(new KeyGesture(Key.F, KeyModifiers.Control | KeyModifiers.Meta), ItemOf(binder, PdfStrings.MenuEnterFullScreen).Gesture);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void HeadersAreTheCatalogLabelsInBothLanguages(string language)
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
        var binder = Make(new FakeMenuWindow());
        var headers = Items(binder.Menu).Select(i => i.Header as string).ToList();
        foreach (var node in CommandNodes())
        {
            Assert.Contains(Catalog(node.CommandId).Name, headers);
        }

        Assert.Contains(PdfStrings.MenuFile, headers);
    }

    [AvaloniaFact]
    public void WithoutAWindowOnlyOpenIsEnabled()
    {
        var binder = Make(null);
        foreach (var node in CommandNodes())
        {
            Assert.Equal(node.CommandId == PdfCommandIds.Open, Can(ItemOfCommand(binder, node.CommandId)));
        }

        Assert.False(Can(ItemOf(binder, PdfStrings.MenuAbout)));
        Assert.False(Can(ItemOf(binder, PdfStrings.MenuCut)));
        Assert.False(Can(ItemOf(binder, PdfStrings.MenuMinimize)));
        Assert.False(ItemOf(binder, PdfStrings.MenuOpenRecent).IsEnabled);
        Assert.False(Can(ItemOf(binder, PdfStrings.MenuNoRecent)));
    }

    [AvaloniaFact]
    public void OpenWithoutAWindowCallsTheHostHook()
    {
        var opened = new List<string>();
        var binder = Make(null, opened);
        ItemOfCommand(binder, PdfCommandIds.Open).Command!.Execute(null);
        Assert.Equal(["open-without-window"], opened);
    }

    [AvaloniaFact]
    public void OpenWithAWindowRunsTheCommandNotTheHook()
    {
        var window = new FakeMenuWindow();
        var opened = new List<string>();
        var binder = Make(window, opened);
        ItemOfCommand(binder, PdfCommandIds.Open).Command!.Execute(null);
        Assert.Empty(opened);
        Assert.Equal(1, window.Runs[PdfCommandIds.Open]);
    }

    [AvaloniaFact]
    public void ATextBoxInFocusTakesUndoCopySelectAllCutPaste()
    {
        var box = new TextBox { Text = "hello" };
        using var host = new HostedBox(box);
        var window = new FakeMenuWindow { FocusedTextBox = box };
        foreach (var id in TextIds)
        {
            window.Enabled[id] = false;
        }

        var binder = Make(window);
        foreach (var id in TextIds)
        {
            Assert.True(Can(ItemOfCommand(binder, id)), id);
        }

        Assert.True(Can(ItemOf(binder, PdfStrings.MenuCut)));
        Assert.True(Can(ItemOf(binder, PdfStrings.MenuPaste)));

        ItemOfCommand(binder, PdfCommandIds.SelectAll).Command!.Execute(null);
        Assert.Equal("hello", box.SelectedText);
        ItemOf(binder, PdfStrings.MenuCut).Command!.Execute(null);
        Assert.Equal(string.Empty, box.Text ?? string.Empty);
        ItemOfCommand(binder, PdfCommandIds.Undo).Command!.Execute(null);
        Assert.Equal("hello", box.Text);
        foreach (var id in TextIds)
        {
            ItemOfCommand(binder, id).Command!.Execute(null);
        }

        Assert.All(TextIds, id => Assert.Equal(0, window.Runs[id]));
    }

    [AvaloniaFact]
    public void WithoutATextBoxCutAndPasteAreOffAndTheOthersRunTheCatalogCommand()
    {
        var window = new FakeMenuWindow();
        var binder = Make(window);
        Assert.False(Can(ItemOf(binder, PdfStrings.MenuCut)));
        Assert.False(Can(ItemOf(binder, PdfStrings.MenuPaste)));
        foreach (var id in TextIds)
        {
            ItemOfCommand(binder, id).Command!.Execute(null);
            Assert.Equal(1, window.Runs[id]);
        }
    }

    [AvaloniaFact]
    public void AnOpenCardDisablesTheCommandsButNotATextBoxEdit()
    {
        var window = new FakeMenuWindow { DialogOpen = true };
        var binder = Make(window);
        Assert.False(Can(ItemOfCommand(binder, PdfCommandIds.Save)));
        Assert.False(Can(ItemOfCommand(binder, PdfCommandIds.Copy)));
        window.FocusedTextBox = new TextBox { Text = "x" };
        binder.Refresh();
        Assert.True(Can(ItemOfCommand(binder, PdfCommandIds.Copy)));
        Assert.False(Can(ItemOfCommand(binder, PdfCommandIds.Save)));
    }

    [AvaloniaFact]
    public void OpenRecentListsTheRecentFilesAndOpensOne()
    {
        var first = new RecentFile("a.pdf", "/docs", DateTimeOffset.UnixEpoch);
        var second = new RecentFile("b.pdf", "/docs", DateTimeOffset.UnixEpoch);
        var window = new FakeMenuWindow { RecentFiles = [first, second] };
        var binder = Make(window);
        var recent = ItemOf(binder, PdfStrings.MenuOpenRecent);
        Assert.True(recent.IsEnabled);
        var entries = recent.Menu!.Items.OfType<NativeMenuItem>().ToList();
        Assert.Equal(["a.pdf", "b.pdf"], entries.Select(e => (string)e.Header!).ToArray());
        entries[1].Command!.Execute(null);
        Assert.Equal(["recent:b.pdf"], window.Calls);

        window.RecentFiles = [];
        binder.Refresh();
        var none = Assert.Single(recent.Menu!.Items.OfType<NativeMenuItem>());
        Assert.Equal(PdfStrings.MenuNoRecent, none.Header);
        Assert.False(Can(none));
    }

    [AvaloniaFact]
    public void TheWindowItemsCallTheWindow()
    {
        var window = new FakeMenuWindow();
        var binder = Make(window);
        ItemOf(binder, PdfStrings.MenuAbout).Command!.Execute(null);
        ItemOf(binder, PdfStrings.MenuMinimize).Command!.Execute(null);
        ItemOf(binder, PdfStrings.MenuZoomWindow).Command!.Execute(null);
        ItemOf(binder, PdfStrings.MenuBringAllToFront).Command!.Execute(null);
        Assert.Equal(["about", "minimize", "zoom", "front"], window.Calls);
    }

    [AvaloniaFact]
    public void FullScreenFlipsAndTheHeaderFollows()
    {
        var window = new FakeMenuWindow();
        var binder = Make(window);
        var item = ItemOf(binder, PdfStrings.MenuEnterFullScreen);
        item.Command!.Execute(null);
        Assert.True(window.IsFullScreen);
        binder.Refresh();
        Assert.Equal(PdfStrings.MenuExitFullScreen, item.Header);
        item.Command.Execute(null);
        Assert.False(window.IsFullScreen);
        binder.Refresh();
        Assert.Equal(PdfStrings.MenuEnterFullScreen, item.Header);
    }

    [AvaloniaFact]
    public void TheBinderFollowsTheCurrentWindowAndLetsGoOfAClosedOne()
    {
        var first = new FakeMenuWindow();
        var second = new FakeMenuWindow();
        IMenuWindow? current = first;
        var binder = new MacMenuBinder(() => current, () => Task.CompletedTask);
        binder.Refresh();
        Assert.True(first.HasSubscriber);

        first.Enabled[PdfCommandIds.Save] = false;
        first.RaiseStatesChanged();
        Assert.False(Can(ItemOfCommand(binder, PdfCommandIds.Save)));

        current = second;
        binder.Refresh();
        Assert.False(first.HasSubscriber);
        Assert.True(second.HasSubscriber);
        Assert.True(Can(ItemOfCommand(binder, PdfCommandIds.Save)));

        current = null;
        binder.Refresh();
        Assert.False(second.HasSubscriber);
    }

    [AvaloniaFact]
    public async Task TheRealWindowAnswersEveryCommandNodeWithTheRibbonsDefinition()
    {
        using var fixture = WindowFixture.Create();
        var window = fixture.Window;
        var ribbon = fixture.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var node in CommandNodes())
        {
            var definition = window.CommandOf(node.CommandId);
            Assert.NotNull(definition);
            Assert.Same(ribbon[node.CommandId], definition);
        }

        var changes = 0;
        window.StatesChanged += () => changes++;
        Assert.False(window.CommandOf(PdfCommandIds.Save)!.IsEnabled);
        await fixture.OpenAsync("text-rich.pdf");
        Assert.True(changes > 0);
        Assert.Null(window.FocusedTextBox);
        Assert.False(window.DialogOpen);
        Assert.NotNull(window.CommandOf(PdfCommandIds.Print));
    }
}
