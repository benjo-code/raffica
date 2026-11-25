using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Raffica.Shell.Frame;
using Raffica.Write.Commands;

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

    /// <summary>Makes a new untitled document in a tab of this window.</summary>
    void NewDocument();

    void CloseActiveTab();

    /// <summary>Closes the active tab; without one (the start view) closes the window, as Cmd+W does in every Mac app.</summary>
    void CloseTabOrWindow();

    /// <summary>Whether the command of the catalog (by id) may run now.</summary>
    bool CanRun(string commandId);

    /// <summary>Runs the command of the catalog (by id); does nothing when it may not run.</summary>
    void RunCommand(string commandId);

    /// <summary>Runs the value command of the catalog (by id) with the value a menu item names (a paper size, an orientation, a break, a page number); does nothing when it may not run or the value is not one of the command.</summary>
    void RunCommand(string commandId, object? payload);

    /// <summary>Whether a switch command (a view, a zoom mode) is on now.</summary>
    bool IsOn(string commandId);

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

    /// <summary>File, Edit, View and Window, set on each window.</summary>
    Window,
}

/// <summary>
/// The macOS menu bar of Write. It follows the same rule as the PDF app (<c>Docs/DECISIONS.md</c>, "Raffica PDF on macOS: the menus belong to the window"): the
/// application menu belongs to the application and is only Settings and Open (Open shows while no window exists, so Cmd+O never has two items); File and Window
/// belong to each window and act on it. Services, Hide, Hide others, Show all and Quit are added by the system; About joins with the packaging milestone. The File menu also has Save and Save as (catalog commands run by id, with their key equivalents). The Format menu (character and paragraph formatting, Clear direct formatting, Update style to match; the value commands are the pickers of the ribbon) runs the commands of the catalog by id. The View menu
/// (page view, writing view, zoom in and out, actual size, fit width, fit page) and the Edit menu (undo, redo, cut, copy, paste, select all) run the commands of the catalog by id on the window. The menu is read again when the
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
            file.Menu.Items.Add(Item(() => WriteStrings.CmdNew, MacGesture(WriteCommandIds.New), window => window?.NewDocument(), always: false));
            file.Menu.Items.Add(Item(() => WriteStrings.MenuOpen, "Meta+O", window => window?.Open(), always: false));
            _recentItem = new NativeMenuItem(WriteStrings.MenuOpenRecent) { Menu = new NativeMenu() };
            file.Menu.Items.Add(_recentItem);
            file.Menu.Items.Add(new NativeMenuItemSeparator());
            file.Menu.Items.Add(Item(() => WriteStrings.MenuCloseTab, "Meta+W", window => window?.CloseTabOrWindow(), always: false));
            file.Menu.Items.Add(CommandItem(WriteCommandIds.Save, () => WriteStrings.CmdSave, MacGesture(WriteCommandIds.Save)));
            file.Menu.Items.Add(CommandItem(WriteCommandIds.SaveAs, () => WriteStrings.CmdSaveAs, MacGesture(WriteCommandIds.SaveAs)));
            Menu.Items.Add(file);

            var editMenu = new NativeMenuItem(WriteStrings.MenuEdit) { Menu = new NativeMenu() };
            editMenu.Menu!.NeedsUpdate += _onNeedsUpdate;
            editMenu.Menu.Items.Add(CommandItem(WriteCommandIds.Undo, () => WriteStrings.CmdUndo, MacGesture(WriteCommandIds.Undo)));
            editMenu.Menu.Items.Add(CommandItem(WriteCommandIds.Redo, () => WriteStrings.CmdRedo, MacGesture(WriteCommandIds.Redo)));
            editMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            editMenu.Menu.Items.Add(CommandItem(WriteCommandIds.Cut, () => WriteStrings.CmdCut, MacGesture(WriteCommandIds.Cut)));
            editMenu.Menu.Items.Add(CommandItem(WriteCommandIds.Copy, () => WriteStrings.CmdCopy, MacGesture(WriteCommandIds.Copy)));
            editMenu.Menu.Items.Add(CommandItem(WriteCommandIds.Paste, () => WriteStrings.CmdPaste, MacGesture(WriteCommandIds.Paste)));
            editMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            editMenu.Menu.Items.Add(CommandItem(WriteCommandIds.SelectAll, () => WriteStrings.CmdSelectAll, MacGesture(WriteCommandIds.SelectAll)));
            Menu.Items.Add(editMenu);
            _headers.Add((editMenu, () => WriteStrings.MenuEdit));

            var formatMenu = new NativeMenuItem(WriteStrings.MenuFormat) { Menu = new NativeMenu() };
            formatMenu.Menu!.NeedsUpdate += _onNeedsUpdate;
            void FormatItem(string id, bool radio = false, bool check = false) =>
                formatMenu.Menu.Items.Add(CommandItem(id, () => WriteCatalogName(id), MacGestureOrNull(id), radio, check));
            foreach (var id in new[] { WriteCommandIds.Bold, WriteCommandIds.Italic, WriteCommandIds.Underline, WriteCommandIds.Strike, WriteCommandIds.Superscript, WriteCommandIds.Subscript, WriteCommandIds.SmallCaps, WriteCommandIds.AllCaps })
            {
                FormatItem(id, check: true);
            }

            formatMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            foreach (var id in new[] { WriteCommandIds.AlignLeft, WriteCommandIds.AlignCenter, WriteCommandIds.AlignRight, WriteCommandIds.Justify })
            {
                FormatItem(id, radio: true);
            }

            formatMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            FormatItem(WriteCommandIds.IndentLess);
            FormatItem(WriteCommandIds.IndentMore);
            foreach (var id in new[] { WriteCommandIds.KeepNext, WriteCommandIds.KeepLines, WriteCommandIds.PageBreakBefore, WriteCommandIds.WidowControl })
            {
                FormatItem(id, check: true);
            }

            formatMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            FormatItem(WriteCommandIds.ClearDirect);
            FormatItem(WriteCommandIds.UpdateStyle);

            // Lists and the heading numbering, then the outline commands (no key on the Mac: the Option and Shift arrows extend the selection there).
            formatMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            FormatItem(WriteCommandIds.Bullets, check: true);
            FormatItem(WriteCommandIds.Numbers, check: true);
            FormatItem(WriteCommandIds.RestartNumbering);
            FormatItem(WriteCommandIds.ContinueNumbering);
            FormatItem(WriteCommandIds.NumberHeadings, check: true);
            formatMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            FormatItem(WriteCommandIds.MoveUp);
            FormatItem(WriteCommandIds.MoveDown);
            FormatItem(WriteCommandIds.Promote);
            FormatItem(WriteCommandIds.Demote);
            Menu.Items.Add(formatMenu);
            _headers.Add((formatMenu, () => WriteStrings.MenuFormat));

            // Insert: the notes (no keys: Esc in a note and a double click do the same as Back to the text).
            var insertMenu = new NativeMenuItem(WriteStrings.MenuInsert) { Menu = new NativeMenu() };
            insertMenu.Menu!.NeedsUpdate += _onNeedsUpdate;
            insertMenu.Menu.Items.Add(CommandItem(WriteCommandIds.NoteFootnote, () => WriteCatalogName(WriteCommandIds.NoteFootnote), null));
            insertMenu.Menu.Items.Add(CommandItem(WriteCommandIds.NoteEndnote, () => WriteCatalogName(WriteCommandIds.NoteEndnote), null));
            insertMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            insertMenu.Menu.Items.Add(CommandItem(WriteCommandIds.NoteBack, () => WriteCatalogName(WriteCommandIds.NoteBack), null));
            Menu.Items.Add(insertMenu);
            _headers.Add((insertMenu, () => WriteStrings.MenuInsert));

            // Table: insert (the grid), rows, columns, cells (borders and shading take their values from submenus), the whole table. No keys: Tab and Shift+Tab in a cell are the page view's.
            var tableMenu = new NativeMenuItem(WriteStrings.MenuTable) { Menu = new NativeMenu() };
            tableMenu.Menu!.NeedsUpdate += _onNeedsUpdate;
            void TableItem(string id, bool check = false) => tableMenu.Menu.Items.Add(CommandItem(id, () => WriteCatalogName(id), null, check: check));
            TableItem(WriteCommandIds.TableInsert);
            tableMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            foreach (var id in new[] { WriteCommandIds.TableRowAbove, WriteCommandIds.TableRowBelow, WriteCommandIds.TableDeleteRows, WriteCommandIds.TableRowUp, WriteCommandIds.TableRowDown, WriteCommandIds.TableSelectRow })
            {
                TableItem(id);
            }

            tableMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            foreach (var id in new[] { WriteCommandIds.TableColumnLeft, WriteCommandIds.TableColumnRight, WriteCommandIds.TableDeleteColumns, WriteCommandIds.TableDistributeColumns, WriteCommandIds.TableSelectColumn })
            {
                TableItem(id);
            }

            tableMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            TableItem(WriteCommandIds.TableMerge);
            TableItem(WriteCommandIds.TableSplit);
            tableMenu.Menu.Items.Add(ValueMenu(WriteCommandIds.TableBorders, TablePayloads.BorderPresets, payload => () => TablePickers.BorderLabel(payload)));
            tableMenu.Menu.Items.Add(CommandItem(WriteCommandIds.TableBorders, () => WriteStrings.TableBordersMore, null));
            tableMenu.Menu.Items.Add(ValueMenu(WriteCommandIds.TableShading, [TablePayloads.NoShading, .. ColorChoices.TextColors.Select(c => c.Id)], payload => () => payload == TablePayloads.NoShading ? WriteStrings.TableShadingNone : WriteStrings.Get(ColorChoices.TextColors.Single(c => c.Id == payload).NameKey)));
            tableMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            TableItem(WriteCommandIds.TableProperties);
            TableItem(WriteCommandIds.TableHeaderRow, check: true);
            TableItem(WriteCommandIds.TableSelectTable);
            TableItem(WriteCommandIds.TableDelete);
            Menu.Items.Add(tableMenu);
            _headers.Add((tableMenu, () => WriteStrings.MenuTable));

            var layoutMenu = new NativeMenuItem(WriteStrings.MenuLayout) { Menu = new NativeMenu() };
            layoutMenu.Menu!.NeedsUpdate += _onNeedsUpdate;
            layoutMenu.Menu.Items.Add(ValueMenu(WriteCommandIds.PageSize, SectionPayloads.Papers, payload => () => payload));
            layoutMenu.Menu.Items.Add(ValueMenu(WriteCommandIds.PageMargins, SectionPayloads.MarginPresets, payload => () => WriteStrings.Get("Margin" + payload)));
            layoutMenu.Menu.Items.Add(ValueMenu(WriteCommandIds.PageOrientation, ["portrait", "landscape"], payload => () => WriteStrings.Get(payload == "portrait" ? "OrientationPortrait" : "OrientationLandscape")));
            layoutMenu.Menu.Items.Add(ValueMenu(WriteCommandIds.PageColumns, ["1", "2", "3"], payload => () => WriteStrings.Get(payload == "1" ? "ColumnsOne" : payload == "2" ? "ColumnsTwo" : "ColumnsThree")));
            layoutMenu.Menu.Items.Add(CommandItem(WriteCommandIds.PageSetup, () => WriteCatalogName(WriteCommandIds.PageSetup), null));
            layoutMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            layoutMenu.Menu.Items.Add(CommandItem(WriteCommandIds.NumberPagesFrom, () => WriteCatalogName(WriteCommandIds.NumberPagesFrom), null));
            layoutMenu.Menu.Items.Add(ValueMenu(WriteCommandIds.SectionBreak, ["nextPage", "continuous", "evenPage", "oddPage"], payload => () => WriteStrings.Get(payload switch { "nextPage" => "BreakNextPage", "continuous" => "BreakContinuous", "evenPage" => "BreakEvenPage", _ => "BreakOddPage" })));
            layoutMenu.Menu.Items.Add(CommandItem(WriteCommandIds.RemoveSection, () => WriteCatalogName(WriteCommandIds.RemoveSection), null));
            layoutMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            layoutMenu.Menu.Items.Add(PageNumberMenu());
            layoutMenu.Menu.Items.Add(CommandItem(WriteCommandIds.DifferentFirstPage, () => WriteCatalogName(WriteCommandIds.DifferentFirstPage), null, check: true));
            layoutMenu.Menu.Items.Add(CommandItem(WriteCommandIds.LeftRightPages, () => WriteCatalogName(WriteCommandIds.LeftRightPages), null, check: true));
            Menu.Items.Add(layoutMenu);
            _headers.Add((layoutMenu, () => WriteStrings.MenuLayout));

            var viewMenu = new NativeMenuItem(WriteStrings.TabView) { Menu = new NativeMenu() };
            viewMenu.Menu!.NeedsUpdate += _onNeedsUpdate;
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.PageView, () => WriteStrings.CmdPageView, null, radio: true));
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.WritingView, () => WriteStrings.CmdWritingView, null, radio: true));
            viewMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.ZoomIn, () => WriteStrings.CmdZoomIn, "Meta+OemPlus"));
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.ZoomOut, () => WriteStrings.CmdZoomOut, "Meta+OemMinus"));
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.ActualSize, () => WriteStrings.CmdActualSize, "Meta+D0"));
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.FitWidth, () => WriteStrings.CmdFitWidth, "Meta+D1"));
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.FitPage, () => WriteStrings.CmdFitPage, "Meta+D2"));
            viewMenu.Menu.Items.Add(new NativeMenuItemSeparator());
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.Outline, () => WriteStrings.CmdOutline, null, check: true));
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.FormattingPane, () => WriteStrings.CmdFormatting, null, check: true));
            viewMenu.Menu.Items.Add(CommandItem(WriteCommandIds.SectionsPane, () => WriteStrings.CmdSections, null, check: true));
            Menu.Items.Add(viewMenu);
            _headers.Add((viewMenu, () => WriteStrings.TabView));

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

    // The key equivalent of an edit command on the Mac: the shortcut of the catalog (Cmd+Z, Cmd+Shift+Z for redo, Cmd+X, Cmd+C, Cmd+V, Cmd+A).
    private static string MacGesture(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id).ShortcutFor(macOs: true)!.Text(macOs: true);

    // The key equivalent of a formatting command (Cmd+B and so on), or null for a command without a shortcut; the name is the one of the catalog, in the UI language.
    private static string? MacGestureOrNull(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id).ShortcutFor(macOs: true)?.Text(macOs: true);

    private static string WriteCatalogName(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id).Name;

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

            if (entry.Checked is { } isChecked)
            {
                entry.Item.IsChecked = isChecked(window);
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

    // An item of the View menu: it runs a command of the catalog by id on the window that is current, and is enabled by the rules of the window (a switch shows its state).
    private NativeMenuItem CommandItem(string id, Func<string> text, string? gesture, bool radio = false, bool check = false)
    {
        var command = new MenuCommand(
            () => _current() is { } window && !window.DialogOpen && window.HasTab && window.CanRun(id),
            () =>
            {
                if (_current() is { } window && !window.DialogOpen && window.HasTab && window.CanRun(id))
                {
                    window.RunCommand(id);
                }
            });
        var item = new NativeMenuItem(text()) { Command = command, Gesture = gesture is null ? null : KeyGesture.Parse(gesture) };
        if (radio || check)
        {
            item.ToggleType = radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox;
        }

        _entries.Add(new Entry(item, command, text, false, radio || check ? window => window?.IsOn(id) == true : null));
        return item;
    }

    // A submenu of a value command: its title is the name of the command and each item runs the command with its value (the same rules as the ribbon's picker; the items have no keys).
    private NativeMenuItem ValueMenu(string id, IReadOnlyList<string> payloads, Func<string, Func<string>> label)
    {
        var parent = new NativeMenuItem(WriteCatalogName(id)) { Menu = new NativeMenu() };
        parent.Menu!.NeedsUpdate += _onNeedsUpdate;
        _headers.Add((parent, () => WriteCatalogName(id)));
        foreach (var payload in payloads)
        {
            parent.Menu.Items.Add(ValueItem(id, payload, label(payload)));
        }

        return parent;
    }

    // The page number: place, then alignment, then look ("Footer", "Center", "Page 1 of N").
    private NativeMenuItem PageNumberMenu()
    {
        var parent = new NativeMenuItem(WriteCatalogName(WriteCommandIds.InsertPageNumber)) { Menu = new NativeMenu() };
        parent.Menu!.NeedsUpdate += _onNeedsUpdate;
        _headers.Add((parent, () => WriteCatalogName(WriteCommandIds.InsertPageNumber)));
        foreach (var place in new[] { "footer", "header" })
        {
            var placeItem = new NativeMenuItem(WriteStrings.Get(place == "footer" ? "PageNumberPlaceFooter" : "PageNumberPlaceHeader")) { Menu = new NativeMenu() };
            placeItem.Menu!.NeedsUpdate += _onNeedsUpdate;
            var placeKey = place == "footer" ? "PageNumberPlaceFooter" : "PageNumberPlaceHeader";
            _headers.Add((placeItem, () => WriteStrings.Get(placeKey)));
            foreach (var align in new[] { "left", "center", "right" })
            {
                var alignKey = "PageNumberAlign" + char.ToUpperInvariant(align[0]) + align[1..];
                var alignItem = new NativeMenuItem(WriteStrings.Get(alignKey)) { Menu = new NativeMenu() };
                alignItem.Menu!.NeedsUpdate += _onNeedsUpdate;
                _headers.Add((alignItem, () => WriteStrings.Get(alignKey)));
                foreach (var look in new[] { "plain", "page", "pageOfTotal", "slash" })
                {
                    var capturedLook = look;
                    alignItem.Menu.Items.Add(ValueItem(WriteCommandIds.InsertPageNumber, place + ":" + align + ":" + look, () => capturedLook switch
                    {
                        "page" => WriteStrings.PageNumberWordPage + " 1",
                        "pageOfTotal" => WriteStrings.PageNumberWordPage + " 1 " + WriteStrings.PageNumberWordOf + " N",
                        "slash" => "1 / N",
                        _ => "1",
                    }));
                }

                placeItem.Menu.Items.Add(alignItem);
            }

            parent.Menu.Items.Add(placeItem);
        }

        return parent;
    }

    // One value of a value command: it runs the command with that value on the window that is current, and is enabled by the rules of the window.
    private NativeMenuItem ValueItem(string id, string payload, Func<string> text)
    {
        var command = new MenuCommand(
            () => _current() is { } window && !window.DialogOpen && window.HasTab && window.CanRun(id),
            () =>
            {
                if (_current() is { } window && !window.DialogOpen && window.HasTab && window.CanRun(id))
                {
                    window.RunCommand(id, payload);
                }
            });
        var item = new NativeMenuItem(text()) { Command = command };
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

    private sealed record Entry(NativeMenuItem Item, MenuCommand Command, Func<string> Text, bool OnlyWithoutWindow, Func<IWriteMenuWindow?, bool>? Checked = null);

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

    /// <summary>Makes the File, View and Window menus of <paramref name="menuWindow"/> and sets them on <paramref name="window"/> (the same object in the app, two in a test).</summary>
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
