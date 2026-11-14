using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Raffica.Shell.Frame;

namespace Raffica.Write.Platform.MacOS;

/// <summary>What the macOS menu bar needs of the window it acts on; <see cref="WriteWindow"/> implements it, tests use a fake.</summary>
internal interface IWriteMenuWindow
{
    /// <summary>True while a card is open: the window is modal then and its commands do nothing (as its shortcuts).</summary>
    bool DialogOpen { get; }

    /// <summary>True while a document tab is open.</summary>
    bool HasTab { get; }

    IReadOnlyList<RecentFile> RecentFiles { get; }

    void Open();

    void CloseActiveTab();

    void OpenRecent(RecentFile file);

    void OpenSettings();

    void Minimize();

    void ToggleWindowZoom();

    void BringAllToFront();

    /// <summary>Raised when the states the menu shows may have changed (a tab opened or closed, a card opened or closed).</summary>
    event Action? StatesChanged;
}

/// <summary>Which part of the menu bar a <see cref="WriteMacMenu"/> builds: Avalonia takes the menu of the application as the application menu only.</summary>
internal enum WriteMenuPart
{
    /// <summary>The items of the application menu, set on the application (Settings, and Open while no window exists).</summary>
    App,

    /// <summary>File and Window, set on each window.</summary>
    Window,
}

/// <summary>
/// The macOS menu bar of Write. It follows the same rule as the PDF app (<c>Docs/DECISIONS.md</c>, "Raffica PDF on macOS: the menus belong to the window"): the
/// application menu belongs to the application and is only Settings and Open (Open shows while no window exists, so Cmd+O never has two items); File and Window
/// belong to each window and act on it. Services, Hide, Hide others, Show all and Quit are added by the system; About joins with the packaging milestone, and the
/// View menu (page view, writing view, zoom) joins with the commands of the window in the next step, so it is not stubbed here. The menu is read again when the
/// system asks, when the window reports new states and when the current window changes (the owner calls <see cref="Refresh"/>).
/// </summary>
internal sealed class WriteMacMenu
{
    private readonly Func<IWriteMenuWindow?> _current;
    private readonly Func<Task> _openWithoutWindow;
    private readonly WriteMenuPart _part;
    private readonly List<Entry> _entries = [];
    private readonly Action _onStatesChanged;
    private readonly EventHandler<EventArgs> _onNeedsUpdate;
    private NativeMenuItem? _recentItem;
    private IWriteMenuWindow? _subscribed;
    private IReadOnlyList<RecentFile>? _shownRecent;

    public WriteMacMenu(Func<IWriteMenuWindow?> current, Func<Task> openWithoutWindow, WriteMenuPart part)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(openWithoutWindow);
        _current = current;
        _openWithoutWindow = openWithoutWindow;
        _part = part;
        _onStatesChanged = Refresh;
        _onNeedsUpdate = (_, _) => Refresh();
        Menu = new NativeMenu();
        if (part == WriteMenuPart.App)
        {
            Menu.Items.Add(Item(() => WriteStrings.MenuSettings, "Meta+OemComma", window => window?.OpenSettings(), always: false));
            var open = Item(() => WriteStrings.MenuOpen, "Meta+O", window => window?.Open(), always: true, whenNoWindow: () => _ = _openWithoutWindow());
            _entries[^1] = _entries[^1] with { OnlyWithoutWindow = true };
            Menu.Items.Add(open);
        }
        else
        {
            var file = new NativeMenuItem(WriteStrings.MenuFile) { Menu = new NativeMenu() };
            file.Menu!.NeedsUpdate += _onNeedsUpdate;
            file.Menu.Items.Add(Item(() => WriteStrings.MenuOpen, "Meta+O", window => window?.Open(), always: false));
            _recentItem = new NativeMenuItem(WriteStrings.MenuOpenRecent) { Menu = new NativeMenu() };
            file.Menu.Items.Add(_recentItem);
            file.Menu.Items.Add(new NativeMenuItemSeparator());
            file.Menu.Items.Add(Item(() => WriteStrings.MenuCloseTab, "Meta+W", window => window?.CloseActiveTab(), always: false, needsTab: true));
            Menu.Items.Add(file);

            var window = new NativeMenuItem(WriteStrings.MenuWindow) { Menu = new NativeMenu() };
            window.Menu!.NeedsUpdate += _onNeedsUpdate;
            window.Menu.Items.Add(Item(() => WriteStrings.MenuMinimize, "Meta+M", w => w?.Minimize(), always: false));
            window.Menu.Items.Add(Item(() => WriteStrings.MenuZoomWindow, null, w => w?.ToggleWindowZoom(), always: false));
            window.Menu.Items.Add(new NativeMenuItemSeparator());
            window.Menu.Items.Add(Item(() => WriteStrings.MenuBringAllToFront, null, w => w?.BringAllToFront(), always: false));
            Menu.Items.Add(window);
            _headers.Add((file, () => WriteStrings.MenuFile));
            _headers.Add((window, () => WriteStrings.MenuWindow));
        }

        Menu.NeedsUpdate += _onNeedsUpdate;
    }

    private readonly List<(NativeMenuItem Item, Func<string> Text)> _headers = [];

    public NativeMenu Menu { get; }

    /// <summary>Reads every state again: the window that is followed, the headers, the enabled states and the recent files.</summary>
    public void Refresh()
    {
        var window = _current();
        Follow(window);
        foreach (var entry in _entries)
        {
            entry.Item.Header = entry.Text();
            entry.Command.RaiseCanExecuteChanged();
            if (entry.OnlyWithoutWindow)
            {
                entry.Item.IsVisible = window is null;
            }
        }

        foreach (var (item, text) in _headers)
        {
            item.Header = text();
        }

        RefreshRecent(window);
    }

    private void Follow(IWriteMenuWindow? window)
    {
        if (ReferenceEquals(window, _subscribed))
        {
            return;
        }

        if (_subscribed is not null)
        {
            _subscribed.StatesChanged -= _onStatesChanged;
        }

        _subscribed = window;
        if (window is not null)
        {
            window.StatesChanged += _onStatesChanged;
        }
    }

    // An item runs an action on the window that is current. With no window only Open does something (the part of the application menu); a card that is open
    // makes the window modal, so its items are disabled as its shortcuts are.
    private NativeMenuItem Item(Func<string> text, string? gesture, Action<IWriteMenuWindow?> run, bool always, bool needsTab = false, Action? whenNoWindow = null)
    {
        var command = new MenuCommand(
            () => _current() is { } window ? !window.DialogOpen && (!needsTab || window.HasTab) : always,
            () =>
            {
                if (_current() is { } window)
                {
                    if (!window.DialogOpen && (!needsTab || window.HasTab))
                    {
                        run(window);
                    }
                }
                else if (always)
                {
                    whenNoWindow?.Invoke();
                }
            });
        var item = new NativeMenuItem(text()) { Command = command, Gesture = gesture is null ? null : KeyGesture.Parse(gesture) };
        _entries.Add(new Entry(item, command, text, false));
        return item;
    }

    // The recent files change rarely: the submenu is rebuilt only when the list (or the presence of a window) changed, not on every refresh.
    private void RefreshRecent(IWriteMenuWindow? window)
    {
        if (_recentItem is not { Menu: { } submenu } recent)
        {
            return;
        }

        recent.IsEnabled = window is not null;
        IReadOnlyList<RecentFile> files = window?.RecentFiles ?? [];
        if (_shownRecent is not null && _shownRecent.SequenceEqual(files))
        {
            return;
        }

        _shownRecent = [.. files];
        submenu.Items.Clear();
        if (files.Count == 0)
        {
            submenu.Items.Add(new NativeMenuItem(WriteStrings.MenuNoRecent) { Command = new MenuCommand(() => false, () => { }) });
            return;
        }

        foreach (var file in files)
        {
            var entry = file;
            submenu.Items.Add(new NativeMenuItem(entry.Name)
            {
                Command = new MenuCommand(() => _current() is { DialogOpen: false }, () => _current()?.OpenRecent(entry)),
            });
        }
    }

    private sealed record Entry(NativeMenuItem Item, MenuCommand Command, Func<string> Text, bool OnlyWithoutWindow);

    private sealed class MenuCommand(Func<bool> canExecute, Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter)
        {
            if (canExecute())
            {
                execute();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Makes the File and Window menus of <paramref name="menuWindow"/> and sets them on <paramref name="window"/> (the same object in the app, two in a test).</summary>
    public static WriteMacMenu Attach(Window window, IWriteMenuWindow menuWindow)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(menuWindow);
        var menu = new WriteMacMenu(() => menuWindow, () => Task.CompletedTask, WriteMenuPart.Window);
        NativeMenu.SetMenu(window, menu.Menu);
        menu.Refresh();
        return menu;
    }
}
