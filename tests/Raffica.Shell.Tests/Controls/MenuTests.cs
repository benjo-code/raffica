using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class MenuTests
{
    private sealed class Counter : ICommand
    {
        public int Runs { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Runs++;
    }

    private sealed record Fixture(Window Window, Button Button, MenuFlyout Flyout, MenuItem Cut, MenuItem Save, MenuItem Print, MenuItem Special, Separator Wide, MenuItem Delete, Counter SaveRuns);

    private static Fixture Open(ThemeMode mode)
    {
        var saveRuns = new Counter();
        var cut = new MenuItem { Header = "Cut", InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) };
        var save = new MenuItem { Header = "Save", InputGesture = new KeyGesture(Key.S, KeyModifiers.Control), Command = saveRuns };
        var print = new MenuItem { Header = "Print", InputGesture = new KeyGesture(Key.P, KeyModifiers.Control), IsEnabled = false };
        var special = new MenuItem { Header = "Paste special", Items = { new MenuItem { Header = "Text only" }, new MenuItem { Header = "Values" } } };
        var wide = new Separator();
        wide.Classes.Add("wide");
        var delete = new MenuItem { Header = "Delete rows", InputGesture = new KeyGesture(Key.D, KeyModifiers.Control) };
        delete.Classes.Add("destructive");
        var flyout = new MenuFlyout { Items = { cut, save, print, special, new Separator(), new MenuItem { Header = "Rename" }, wide, delete } };

        var button = new Button { Content = "Insert", Flyout = flyout };
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { button } }, mode);
        flyout.ShowAt(button);
        ControlHarness.Settle(window);
        return new Fixture(window, button, flyout, cut, save, print, special, wide, delete, saveRuns);
    }

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        ControlHarness.Settle(window);
    }

    private static Border Row(MenuItem item) => ControlHarness.Part<Border>(item, "PART_LayoutRoot");

    private static ContentPresenter Header(MenuItem item) => ControlHarness.Part<ContentPresenter>(item, "PART_HeaderPresenter");

    private static TextBlock Shortcut(MenuItem item) => ControlHarness.Part<TextBlock>(item, "PART_InputGestureText");

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void RowsAre32PxHighAndShowTheShortcutRightAlignedIn13PxSecondaryText(ThemeMode mode)
    {
        var f = Open(mode);

        Assert.Equal(32, f.Cut.Bounds.Height);
        Assert.Equal(32, f.Save.Bounds.Height);
        Assert.Equal(32, f.Special.Bounds.Height);
        Assert.Equal(32, f.Delete.Bounds.Height);

        var shortcut = Shortcut(f.Save);
        Assert.EndsWith("S", shortcut.Text);
        Assert.Contains(OperatingSystem.IsMacOS() ? "⌃" : "Ctrl", shortcut.Text);
        Assert.Equal(13, shortcut.FontSize);
        Assert.Equal(HorizontalAlignment.Right, shortcut.HorizontalAlignment);
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", shortcut.Foreground, "shortcut");
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", Header(f.Save).Foreground, "label");

        // The shortcut column is right-aligned across items: the right edges line up, 12 px inside the row.
        var saveRight = shortcut.TranslatePoint(new Point(shortcut.Bounds.Width, 0), f.Save)!.Value.X;
        var cutShortcut = Shortcut(f.Cut);
        var cutRight = cutShortcut.TranslatePoint(new Point(cutShortcut.Bounds.Width, 0), f.Cut)!.Value.X;
        Assert.Equal(f.Save.Bounds.Width - 12, saveRight, 0.5);
        Assert.Equal(saveRight, cutRight, 0.5);
    }

    // Fails if the shortcut column keeps the framework's English key names.
    [AvaloniaFact]
    public void ShortcutColumnIsLocalized()
    {
        var previous = Raffica.Shell.Localization.ShellStrings.Culture;
        try
        {
            Raffica.Shell.Localization.ShellStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
            var f = Open(ThemeMode.Light);
            Assert.Equal(OperatingSystem.IsMacOS() ? "⌃S" : "Strg+S", Shortcut(f.Save).Text);
        }
        finally
        {
            Raffica.Shell.Localization.ShellStrings.Culture = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DisabledItemAndItsShortcutUseDisabledText(ThemeMode mode)
    {
        var f = Open(mode);

        ControlHarness.AssertToken("Raffica.Brush.Text.Disabled", Header(f.Print).Foreground, "label");
        ControlHarness.AssertToken("Raffica.Brush.Text.Disabled", Shortcut(f.Print).Foreground, "shortcut");
        Assert.Equal(Colors.Transparent, ControlHarness.Strict(Row(f.Print).Background));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheDestructiveItemIsDangerSemiBoldAndLastAfterAWiderSeparator(ThemeMode mode)
    {
        var f = Open(mode);
        var items = f.Flyout.Items.Cast<Control>().ToList();

        Assert.Same(f.Delete, items[^1]);
        Assert.Same(f.Wide, items[^2]);
        Assert.Equal(new Thickness(0, 8, 0, 4), f.Wide.Margin);
        var plain = items.OfType<Separator>().First(s => s != f.Wide);
        Assert.Equal(new Thickness(0, 4, 0, 4), plain.Margin);

        ControlHarness.AssertToken("Raffica.Brush.Danger", Header(f.Delete).Foreground, "label");
        ControlHarness.AssertToken("Raffica.Brush.Danger", Shortcut(f.Delete).Foreground, "shortcut");
        Assert.Equal(FontWeight.SemiBold, f.Delete.FontWeight);
        Assert.Equal(32, f.Delete.Bounds.Height);

        ControlHarness.AssertToken("Raffica.Brush.Rule", ControlHarness.Part<Border>(f.Wide, "PART_Rule").Background, "separator");
        Assert.Equal(1, ControlHarness.Part<Border>(f.Wide, "PART_Rule").Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void HoverFillsWithTheHoverTokenAndPressedWithTheInk(ThemeMode mode)
    {
        var f = Open(mode);
        // Opening a menu highlights its first item; every other row is transparent.
        var row = Row(f.Save);
        Assert.Equal(Colors.Transparent, ControlHarness.Strict(row.Background));
        ControlHarness.AssertToken("Raffica.Brush.State.Hover", Row(f.Cut).Background, "first item highlighted");

        StateMatrix.Force(f.Save, ":selected");
        ControlHarness.Settle(f.Window);
        ControlHarness.AssertToken("Raffica.Brush.State.Hover", row.Background, "hover fill");
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", Header(f.Save).Foreground, "hover label");
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", Shortcut(f.Save).Foreground, "hover shortcut");

        // A real press (the row must be under the pointer, so pressing a sub-item does not fill its parent).
        var center = f.Save.TranslatePoint(new Point(f.Save.Bounds.Width / 2, 16), f.Window)!.Value;
        f.Window.MouseMove(center);
        f.Window.MouseDown(center, MouseButton.Left);
        ControlHarness.Settle(f.Window);
        ControlHarness.AssertToken("Raffica.Brush.Ink", row.Background, "pressed fill");
        ControlHarness.AssertToken("Raffica.Brush.Text.OnInk", Header(f.Save).Foreground, "pressed label");
        ControlHarness.AssertToken("Raffica.Brush.Text.OnInk", Shortcut(f.Save).Foreground, "pressed shortcut");
        f.Window.MouseUp(center, MouseButton.Left);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheSubmenuItemHasAnArrowAndItsPopupHasAnEdgeAndNoShadow(ThemeMode mode)
    {
        var f = Open(mode);

        var arrow = ControlHarness.Part<Control>(f.Special, "PART_Chevron");
        Assert.True(arrow.IsVisible);
        Assert.False(ControlHarness.Part<Control>(f.Save, "PART_Chevron").IsVisible);

        f.Special.IsSubMenuOpen = true;
        ControlHarness.Settle(f.Window);

        var popup = ControlHarness.Part<Popup>(f.Special, "PART_Popup");
        Assert.False(popup.WindowManagerAddShadowHint);
        var border = ControlHarness.Part<Border>(f.Special, "PART_SubmenuBorder");
        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", border.Background, "submenu fill");
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", border.BorderBrush, "submenu edge");
        Assert.Equal(new Thickness(1), border.BorderThickness);
        Assert.Equal(new CornerRadius(3), border.CornerRadius);
        Assert.Equal(default, border.BoxShadow);
        foreach (var child in f.Special.Items.Cast<MenuItem>())
        {
            Assert.Equal(32, child.Bounds.Height);
        }
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheFlyoutPresenterHasAOnePixelEdgeRadius3AndNoShadow(ThemeMode mode)
    {
        var f = Open(mode);
        var presenter = f.Cut.FindAncestorOfType<MenuFlyoutPresenter>();
        Assert.NotNull(presenter);

        var border = ControlHarness.Part<Border>(presenter, "LayoutRoot");
        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", border.Background, "menu fill");
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", border.BorderBrush, "menu edge");
        Assert.Equal(new Thickness(1), border.BorderThickness);
        Assert.Equal(new CornerRadius(3), border.CornerRadius);
        Assert.Equal(default, border.BoxShadow);
        Assert.False(ControlHarness.HasShadow(presenter));
        Assert.Equal(new Thickness(0, 4, 0, 4), ControlHarness.Part<ItemsPresenter>(presenter, "PART_ItemsPresenter").Margin + border.Padding);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ContextMenuHasTheSameEdgeAndNoShadow(ThemeMode mode)
    {
        var menu = new ContextMenu { Items = { new MenuItem { Header = "Cut" }, new MenuItem { Header = "Copy" } } };
        var target = new Border { Width = 120, Height = 60, Background = Brushes.Transparent, ContextMenu = menu };
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { target } }, mode);

        menu.Open(target);
        ControlHarness.Settle(window);

        Assert.False(menu.WindowManagerAddShadowHint);
        var border = ControlHarness.Part<Border>(menu, "LayoutRoot");
        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", border.Background, "fill");
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", border.BorderBrush, "edge");
        Assert.Equal(new Thickness(1), border.BorderThickness);
        Assert.Equal(new CornerRadius(3), border.CornerRadius);
        Assert.Equal(default, border.BoxShadow);
        Assert.Equal(32, ((MenuItem)menu.Items[0]!).Bounds.Height);
        Assert.True(menu.MinWidth >= 200);
        menu.Close();
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DownUpEnterAndEscapeWorkOnTheOpenMenu(ThemeMode mode)
    {
        var f = Open(mode);
        Assert.True(f.Flyout.IsOpen);

        Assert.Contains(":selected", f.Cut.Classes);
        ControlHarness.AssertToken("Raffica.Brush.State.Hover", Row(f.Cut).Background, "highlight");

        Press(f.Window, PhysicalKey.ArrowDown);
        Assert.DoesNotContain(":selected", f.Cut.Classes);
        Assert.Contains(":selected", f.Save.Classes);
        ControlHarness.AssertToken("Raffica.Brush.State.Hover", Row(f.Save).Background, "highlight moved");
        Assert.Equal(Colors.Transparent, ControlHarness.Strict(Row(f.Cut).Background));

        Press(f.Window, PhysicalKey.ArrowUp);
        Assert.Contains(":selected", f.Cut.Classes);

        Press(f.Window, PhysicalKey.ArrowDown);
        Press(f.Window, PhysicalKey.Enter);
        Assert.Equal(1, f.SaveRuns.Runs);
        Assert.False(f.Flyout.IsOpen);

        f.Flyout.ShowAt(f.Button);
        ControlHarness.Settle(f.Window);
        Assert.True(f.Flyout.IsOpen);
        Press(f.Window, PhysicalKey.Escape);
        Assert.False(f.Flyout.IsOpen);
    }

    private static Border Ring(MenuItem item) => ControlHarness.Part<Border>(item, "PART_FocusRing");

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void KeyboardNavigationShowsTheInkRingAndTheDestructiveItemTheDangerRing(ThemeMode mode)
    {
        var f = Open(mode);

        Press(f.Window, PhysicalKey.ArrowDown);
        Assert.True(Ring(f.Save).IsVisible, "ring on the item reached with the keyboard");
        Assert.Equal(new Thickness(2), Ring(f.Save).BorderThickness);
        ControlHarness.AssertToken("Raffica.Brush.Focus.Ring", Ring(f.Save).BorderBrush, "ring");
        ControlHarness.AssertToken("Raffica.Brush.Ink", Ring(f.Save).BorderBrush, "ring is the ink");
        Assert.False(Ring(f.Cut).IsVisible, "no ring on the item left behind");

        // Save -> Special -> Rename -> Delete rows (the disabled item is skipped).
        Press(f.Window, PhysicalKey.ArrowDown);
        Press(f.Window, PhysicalKey.ArrowDown);
        Press(f.Window, PhysicalKey.ArrowDown);
        Assert.True(Ring(f.Delete).IsVisible, "ring on the destructive item");
        Assert.Equal(new Thickness(2), Ring(f.Delete).BorderThickness);
        ControlHarness.AssertToken("Raffica.Brush.Danger", Ring(f.Delete).BorderBrush, "danger ring");
        Assert.False(Ring(f.Save).IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void MouseHoverHighlightsWithoutTheRing(ThemeMode mode)
    {
        var f = Open(mode);
        var center = f.Save.TranslatePoint(new Point(f.Save.Bounds.Width / 2, 16), f.Window)!.Value;

        f.Window.MouseMove(center);
        ControlHarness.Settle(f.Window);

        ControlHarness.AssertToken("Raffica.Brush.State.Hover", Row(f.Save).Background, "hover fill");
        Assert.False(Ring(f.Save).IsVisible, "hover is not keyboard focus");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void KeyboardNavigationSkipsTheDisabledItem(ThemeMode mode)
    {
        var f = Open(mode);

        Press(f.Window, PhysicalKey.ArrowDown);
        Press(f.Window, PhysicalKey.ArrowDown);

        Assert.DoesNotContain(":selected", f.Print.Classes);
        Assert.Contains(":selected", f.Special.Classes);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void AMenuBarItemIs26PxHighWithTheHoverFillAndOpensAMenuStylePopup(ThemeMode mode)
    {
        var file = new MenuItem { Header = "File", Items = { new MenuItem { Header = "Open" }, new MenuItem { Header = "Close" } } };
        var bar = new Menu { Items = { file } };
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { bar } }, mode);

        Assert.Equal(26, file.Bounds.Height);
        Assert.Equal(Colors.Transparent, ControlHarness.Strict(Row(file).Background));
        StateMatrix.Force(file, ":selected");
        ControlHarness.Settle(window);
        ControlHarness.AssertToken("Raffica.Brush.State.Hover", Row(file).Background, "hover fill");

        file.IsSubMenuOpen = true;
        ControlHarness.Settle(window);
        Assert.False(ControlHarness.Part<Popup>(file, "PART_Popup").WindowManagerAddShadowHint);
        var border = ControlHarness.Part<Border>(file, "PART_SubmenuBorder");
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", border.BorderBrush, "edge");
        Assert.Equal(default, border.BoxShadow);
        Assert.Equal(32, ((MenuItem)file.Items[0]!).Bounds.Height);
    }
}
