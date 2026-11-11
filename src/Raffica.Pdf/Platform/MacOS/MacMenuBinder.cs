using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Raffica.Pdf.Commands;
using Raffica.Shell.Frame;

namespace Raffica.Pdf.Platform.MacOS;

/// <summary>What the macOS menu bar needs of the window it acts on; <see cref="PdfWindow"/> implements it, tests use a fake.</summary>
internal interface IMenuWindow
{
    /// <summary>The command the ribbon was built with, or null for an id the window does not know.</summary>
    CommandDefinition? CommandOf(string id);

    /// <summary>The text box that has the keyboard focus (a comment, a text-box editor, the search field), or null.</summary>
    Avalonia.Controls.TextBox? FocusedTextBox { get; }

    /// <summary>True while a card is open: the window is modal then and its commands do nothing (as its shortcuts).</summary>
    bool DialogOpen { get; }

    /// <summary>
    /// True while the focus is in a list that owns Copy and Select all (the page list of the organize mode): the menu leaves those two keys to the list, as the
    /// window's own shortcut handler does, by disabling the items so the system passes the key on.
    /// </summary>
    bool OwnsEditKeys { get; }

    IReadOnlyList<RecentFile> RecentFiles { get; }

    void OpenRecent(RecentFile file);

    void ShowAbout();

    void Minimize();

    void ToggleWindowZoom();

    void BringAllToFront();

    bool IsFullScreen { get; set; }

    /// <summary>Raised when the states of the commands may have changed (the window asked its ribbon again).</summary>
    event Action? StatesChanged;
}

/// <summary>Which part of the menu bar a <see cref="MacMenuBinder"/> builds: Avalonia takes the menu of the application as the application menu only.</summary>
internal enum MenuPart
{
    /// <summary>Every menu of the model as top-level items (what the tests read).</summary>
    All,

    /// <summary>The items of the application menu, set on the application.</summary>
    App,

    /// <summary>File, Edit, View, Window and Help, set on a window.</summary>
    Window,
}

/// <summary>
/// Builds the native menu bar from <see cref="MacMenuModel"/> and keeps it in step with the current window: a command item has the catalog label and shortcut
/// and runs the command of the ribbon; the items that macOS defines act on the window. The menu is read again when the system asks (<see cref="NativeMenu.NeedsUpdate"/>),
/// when the window reports new states and when the current window changes (the owner calls <see cref="Refresh"/>). With a text box in focus the edit items
/// act on that text box and never on the document (the system key equivalent would otherwise swallow the key before the box sees it).
/// </summary>
internal sealed class MacMenuBinder
{
    private readonly Func<IMenuWindow?> _current;
    private readonly Func<Task> _openWithoutWindow;
    private readonly List<Entry> _entries = [];
    private readonly List<(NativeMenuItem Item, string Key)> _submenus = [];
    private readonly Action _onStatesChanged;
    private readonly EventHandler<EventArgs> _onNeedsUpdate;
    private NativeMenuItem? _recentItem;
    private IMenuWindow? _subscribed;
    private IReadOnlyList<RecentFile>? _shownRecent;

    private readonly MenuPart _part;

    public MacMenuBinder(Func<IMenuWindow?> current, Func<Task> openWithoutWindow, MenuPart part = MenuPart.All)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(openWithoutWindow);
        _current = current;
        _openWithoutWindow = openWithoutWindow;
        _part = part;
        _onStatesChanged = Refresh;
        _onNeedsUpdate = (_, _) => Refresh();
        Menu = new NativeMenu();
        switch (part)
        {
            case MenuPart.App:
                foreach (var node in MacMenuModel.AppItems())
                {
                    Menu.Items.Add(BuildNode(node));
                }

                break;
            case MenuPart.Window:
                foreach (var submenu in MacMenuModel.WindowMenus())
                {
                    Menu.Items.Add(BuildSubmenu(submenu));
                }

                break;
            default:
                foreach (var submenu in MacMenuModel.Build())
                {
                    Menu.Items.Add(BuildSubmenu(submenu));
                }

                break;
        }

        Menu.NeedsUpdate += _onNeedsUpdate;
    }

    public NativeMenu Menu { get; }

    /// <summary>Reads every state again: the window followed, headers, checks, the enabled state and the recent files.</summary>
    public void Refresh()
    {
        var window = _current();
        Follow(window);

        foreach (var entry in _entries)
        {
            entry.Refresh(window);
            entry.Command.RaiseCanExecuteChanged();
        }

        foreach (var (item, key) in _submenus)
        {
            item.Header = PdfStrings.Get(key);
        }

        RefreshRecent(window);
    }

    // The binder listens to the window it acts on only: a closed window is let go when the host names the next one (or none).
    private void Follow(IMenuWindow? window)
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

    private NativeMenuItem BuildSubmenu(SubmenuNode node)
    {
        var item = new NativeMenuItem(PdfStrings.Get(node.HeaderKey)) { Menu = new NativeMenu() };

        // The system asks the menu that is about to open, not only the bar: each submenu reads the states again. Made once here; a refresh only replaces items.
        item.Menu.NeedsUpdate += _onNeedsUpdate;
        _submenus.Add((item, node.HeaderKey));
        if (node.Fill == MenuAction.OpenRecent)
        {
            _recentItem = item;
        }

        foreach (var child in node.Items)
        {
            item.Menu.Items.Add(BuildNode(child));
        }

        return item;
    }

    private NativeMenuItem BuildNode(MenuNode node) => node switch
    {
        CommandNode command => BuildCommand(command),
        ActionNode action => BuildAction(action),
        SubmenuNode inner => BuildSubmenu(inner),
        _ => new NativeMenuItemSeparator(),
    };

    private NativeMenuItem BuildCommand(CommandNode node)
    {
        var catalog = PdfCommandCatalog.Commands.Single(c => c.Id == node.CommandId);
        var id = node.CommandId;
        var routed = id is PdfCommandIds.Undo or PdfCommandIds.Redo or PdfCommandIds.Copy or PdfCommandIds.SelectAll;
        var command = new MenuCommand(
            () => CanRunCommand(id, routed),
            () => RunCommand(id, routed));
        var item = new NativeMenuItem(catalog.Name)
        {
            Command = command,
            Gesture = catalog.ShortcutFor(true) is { } shortcut ? GestureOf(shortcut) : null,
            ToggleType = catalog.Toggle ? MenuItemToggleType.CheckBox : MenuItemToggleType.None,
        };
        // In the application menu Open is the item for "no window": the File menu of a window carries it otherwise, with the same key.
        var onlyWithoutWindow = _part == MenuPart.App && id == PdfCommandIds.Open;
        _entries.Add(new Entry(item, command, window =>
        {
            item.Header = catalog.Name;
            item.IsChecked = catalog.Toggle && window?.CommandOf(id) is { IsToggle: true } definition && definition.IsChecked;
            if (onlyWithoutWindow)
            {
                item.IsVisible = window is null;
            }
        }));
        return item;
    }

    private NativeMenuItem BuildAction(ActionNode node)
    {
        var action = node.Action;
        var command = new MenuCommand(() => CanRunAction(action), () => RunAction(action));
        var gesture = node.Gesture ?? action switch
        {
            MenuAction.Cut => "Meta+X",
            MenuAction.Paste => "Meta+V",
            _ => null,
        };
        var item = new NativeMenuItem(PdfStrings.Get(node.HeaderKey))
        {
            Command = command,
            Gesture = gesture is null ? null : KeyGesture.Parse(gesture),
        };
        _entries.Add(new Entry(item, command, window =>
        {
            item.Header = action == MenuAction.ToggleFullScreen
                ? (window?.IsFullScreen == true ? PdfStrings.MenuExitFullScreen : PdfStrings.MenuEnterFullScreen)
                : PdfStrings.Get(node.HeaderKey);
        }));
        return item;
    }

    private static KeyGesture GestureOf(PdfShortcut shortcut)
    {
        var modifiers = shortcut.Base switch
        {
            ShortcutBase.Command => KeyModifiers.Meta,
            ShortcutBase.Control => KeyModifiers.Control,
            _ => KeyModifiers.None,
        };
        if (shortcut.Shift)
        {
            modifiers |= KeyModifiers.Shift;
        }

        return new KeyGesture(shortcut.Key, modifiers);
    }

    private bool CanRunCommand(string id, bool routed)
    {
        if (_current() is not { } window)
        {
            return id == PdfCommandIds.Open;
        }

        if (routed && window.FocusedTextBox is not null)
        {
            return true;
        }

        // The list of the organize mode owns Copy and Select all: a disabled item makes the system hand the key to the list.
        if (routed && id is PdfCommandIds.Copy or PdfCommandIds.SelectAll && window.OwnsEditKeys)
        {
            return false;
        }

        return !window.DialogOpen && window.CommandOf(id)?.IsEnabled == true;
    }

    private void RunCommand(string id, bool routed)
    {
        if (_current() is not { } window)
        {
            if (id == PdfCommandIds.Open)
            {
                _ = _openWithoutWindow();
            }

            return;
        }

        if (routed && window.FocusedTextBox is { } box)
        {
            switch (id)
            {
                case PdfCommandIds.Undo:
                    box.Undo();
                    break;
                case PdfCommandIds.Redo:
                    box.Redo();
                    break;
                case PdfCommandIds.Copy:
                    box.Copy();
                    break;
                default:
                    box.SelectAll();
                    break;
            }

            return;
        }

        if (!window.DialogOpen && !(id is PdfCommandIds.Copy or PdfCommandIds.SelectAll && window.OwnsEditKeys) && window.CommandOf(id) is { IsEnabled: true } definition)
        {
            definition.Run?.Invoke();
        }
    }

    private bool CanRunAction(MenuAction action)
    {
        if (_current() is not { } window)
        {
            return false;
        }

        return action switch
        {
            MenuAction.Cut or MenuAction.Paste => window.FocusedTextBox is not null,
            MenuAction.About => !window.DialogOpen,
            _ => true,
        };
    }

    private void RunAction(MenuAction action)
    {
        if (_current() is not { } window || !CanRunAction(action))
        {
            return;
        }

        switch (action)
        {
            case MenuAction.About:
                window.ShowAbout();
                break;
            case MenuAction.Cut:
                window.FocusedTextBox?.Cut();
                break;
            case MenuAction.Paste:
                window.FocusedTextBox?.Paste();
                break;
            case MenuAction.Minimize:
                window.Minimize();
                break;
            case MenuAction.ZoomWindow:
                window.ToggleWindowZoom();
                break;
            case MenuAction.BringAllToFront:
                window.BringAllToFront();
                break;
            case MenuAction.ToggleFullScreen:
                window.IsFullScreen = !window.IsFullScreen;
                break;
            default:
                break;
        }
    }

    // The recent files change rarely: the submenu is rebuilt only when the list (or the presence of a window) changed, not on every refresh.
    private void RefreshRecent(IMenuWindow? window)
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
            submenu.Items.Add(new NativeMenuItem(PdfStrings.MenuNoRecent) { Command = new MenuCommand(() => false, () => { }) });
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

    private sealed record Entry(NativeMenuItem Item, MenuCommand Command, Action<IMenuWindow?> Update)
    {
        public void Refresh(IMenuWindow? window) => Update(window);
    }

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
}
