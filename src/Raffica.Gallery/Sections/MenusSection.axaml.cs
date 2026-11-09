using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Raffica.Shell.Controls;

namespace Raffica.Gallery.Sections;

/// <summary>
/// Menus and tooltips: a button with a flyout menu (shortcuts, a submenu, a disabled and a destructive item), a menu bar, a context
/// menu area, the menu rows laid out inline (so they can be compared without opening anything) and a button with a rich tooltip next
/// to the same content shown inline. <see cref="OpenMenu"/> opens the flyout with its submenu for the <c>--open-menu</c> screenshot.
/// </summary>
internal sealed partial class MenusSection : UserControl
{
    private readonly Button _flyoutButton;
    private readonly MenuFlyout _flyout = new();
    private readonly MenuItem _pasteSpecial;
    private int _row;

    public MenusSection()
    {
        InitializeComponent();
        Title.Text = GalleryStrings.SectionMenus;

        _pasteSpecial = FillMenu(_flyout.Items);
        _flyoutButton = new Button { Content = GalleryStrings.MenuInsert, Flyout = _flyout };
        _flyoutButton.Classes.Add("boxed");
        _flyoutButton.HorizontalAlignment = HorizontalAlignment.Left;
        FlyoutRow.Children.Add(new TextBlock { Text = GalleryStrings.RowFlyout, Classes = { "ui" } });
        FlyoutRow.Children.Add(_flyoutButton);

        AddRow(GalleryStrings.RowMenuBar, MenuBar());
        AddRow(GalleryStrings.RowContextMenu, ContextArea());
        AddRow(GalleryStrings.RowMenuInline, InlineMenu());
        AddRow(GalleryStrings.RowTooltip, TooltipRow());
    }

    /// <summary>True while the flyout opened by <see cref="OpenMenu"/> is open.</summary>
    public bool IsMenuOpen => _flyout.IsOpen;

    /// <summary>True while the "Paste special" submenu of the flyout is open.</summary>
    public bool IsSubmenuOpen => _pasteSpecial.IsSubMenuOpen;

    /// <summary>Opens the flyout of the button and its "Paste special" submenu.</summary>
    public void OpenMenu()
    {
        _flyout.ShowAt(_flyoutButton);
        Dispatcher.UIThread.Post(() => _pasteSpecial.IsSubMenuOpen = true);
    }

    /// <summary>
    /// The top-level windows of the open popups (not <paramref name="window"/> itself): the screenshot renders them into the
    /// picture, because a popup is a window of its own and the gallery window does not paint it.
    /// </summary>
    public IReadOnlyList<TopLevel> PopupTopLevels(TopLevel window)
    {
        var result = new List<TopLevel>();
        if (!_flyout.IsOpen)
        {
            return result;
        }

        foreach (var item in _flyout.Items.OfType<MenuItem>())
        {
            AddTopLevel(result, item, window);
            foreach (var child in item.Items.OfType<MenuItem>())
            {
                AddTopLevel(result, child, window);
            }
        }

        return result;
    }

    private static void AddTopLevel(List<TopLevel> list, Visual probe, TopLevel window)
    {
        if (TopLevel.GetTopLevel(probe) is { } top && !ReferenceEquals(top, window) && !list.Contains(top))
        {
            list.Add(top);
        }
    }

    /// <summary>Fills a menu with the sample items and returns its submenu item.</summary>
    private static MenuItem FillMenu(ItemCollection items)
    {
        var pasteSpecial = new MenuItem
        {
            Header = GalleryStrings.ItemPasteSpecial,
            Items = { new MenuItem { Header = GalleryStrings.ItemTextOnly }, new MenuItem { Header = GalleryStrings.ItemValuesOnly } },
        };
        var wide = new Separator();
        wide.Classes.Add("wide");
        var delete = new MenuItem { Header = GalleryStrings.ItemDeleteRows, InputGesture = new KeyGesture(Key.Delete, KeyModifiers.Control) };
        delete.Classes.Add("destructive");

        items.Add(Item(GalleryStrings.ItemCut, Key.X, KeyModifiers.Control));
        items.Add(Item(GalleryStrings.ItemCopy, Key.C, KeyModifiers.Control));
        items.Add(Item(GalleryStrings.ItemPaste, Key.V, KeyModifiers.Control));
        items.Add(pasteSpecial);
        items.Add(new Separator());
        items.Add(new MenuItem { Header = GalleryStrings.ItemInsertRows });
        items.Add(new MenuItem { Header = GalleryStrings.ItemInsertColumns });
        items.Add(new Separator());
        items.Add(Item(GalleryStrings.ItemInsertComment, Key.F2, KeyModifiers.Shift));
        items.Add(Item(GalleryStrings.ItemFormatCells, Key.D1, KeyModifiers.Control));
        items.Add(new MenuItem { Header = GalleryStrings.ItemPrint, InputGesture = new KeyGesture(Key.P, KeyModifiers.Control), IsEnabled = false });
        items.Add(wide);
        items.Add(delete);
        return pasteSpecial;
    }

    private static MenuItem Item(string header, Key key, KeyModifiers modifiers) =>
        new() { Header = header, InputGesture = new KeyGesture(key, modifiers) };

    private static Menu MenuBar()
    {
        var bar = new Menu
        {
            Items =
            {
                new MenuItem
                {
                    Header = GalleryStrings.MenuFile,
                    Items = { Item(GalleryStrings.ItemOpen, Key.O, KeyModifiers.Control), new MenuItem { Header = GalleryStrings.ItemClose } },
                },
                new MenuItem
                {
                    Header = GalleryStrings.MenuEdit,
                    Items = { Item(GalleryStrings.ItemCut, Key.X, KeyModifiers.Control), Item(GalleryStrings.ItemCopy, Key.C, KeyModifiers.Control), Item(GalleryStrings.ItemPaste, Key.V, KeyModifiers.Control) },
                },
            },
        };
        AutomationProperties.SetName(bar, GalleryStrings.RowMenuBar);
        return bar;
    }

    private static Border ContextArea()
    {
        var menu = new ContextMenu();
        FillMenu(menu.Items);
        var area = new Border
        {
            Width = 280,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Focusable = true,
            ContextMenu = menu,
            Child = new TextBlock { Text = GalleryStrings.ContextHint, Classes = { "secondary" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        area.Bind(Border.BorderBrushProperty, area.GetResourceObservable("Raffica.Brush.Rule"));
        area.Bind(Border.BackgroundProperty, area.GetResourceObservable("Raffica.Brush.Surface.Band"));
        AutomationProperties.SetName(area, GalleryStrings.RowContextMenu);
        return area;
    }

    /// <summary>The rows of a menu on the page itself (as on the context menu board), with "Copy" under the pointer.</summary>
    private static MenuFlyoutPresenter InlineMenu()
    {
        var presenter = new MenuFlyoutPresenter { HorizontalAlignment = HorizontalAlignment.Left, Width = 268 };
        FillMenu(presenter.Items);
        StateMatrix.Force((Control)presenter.Items[1]!, ":selected");
        AutomationProperties.SetName(presenter, GalleryStrings.RowMenuInline);
        return presenter;
    }

    private static StackPanel TooltipRow()
    {
        var button = new Button { Content = GalleryStrings.TipTitle, VerticalAlignment = VerticalAlignment.Top };
        button.Classes.Add("boxed");
        ToolTip.SetTip(button, Rich());
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24,
            Children = { button, new ToolTip { Content = Rich() } },
        };
    }

    private static TooltipContent Rich() =>
        new() { Title = GalleryStrings.TipTitle, Shortcut = "Ctrl+Shift+E", Description = GalleryStrings.TipDescription };

    private void AddRow(string label, Control content)
    {
        Rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var text = new TextBlock { Text = label, Classes = { "ui" }, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8) };
        Grid.SetRow(text, _row);
        content.Margin = new Thickness(8, 8, 12, 8);
        Grid.SetRow(content, _row);
        Grid.SetColumn(content, 1);
        Rows.Children.Add(text);
        Rows.Children.Add(content);
        _row++;
    }
}
