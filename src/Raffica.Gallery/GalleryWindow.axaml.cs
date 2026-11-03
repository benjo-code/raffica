using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Raffica.Gallery.Sections;
using Raffica.Shell;
using Raffica.Shell.Controls;

namespace Raffica.Gallery;

/// <summary>
/// The window of the gallery: a toolbar (theme, high contrast, app ink; they switch live), the list of sections and the section on show.
/// The language is not switchable here: it follows <c>--lang</c> and applies after a restart.
/// </summary>
internal sealed partial class GalleryWindow : Window
{
    private static readonly ThemeMode[] ThemeModes = [ThemeMode.Light, ThemeMode.Dark, ThemeMode.System];
    private static readonly AppInk[] Inks = [AppInk.Write, AppInk.Sheets, AppInk.Slides, AppInk.Pdf];

    private readonly SegmentedControl _theme = new();
    private readonly ToggleButton _highContrast = new();
    private readonly SegmentedControl _ink = new();
    private ThemeMode _themeMode;
    private AppInk _inkValue;
    private bool? _forceHighContrast;
    private MenusSection? _menus;
    private DialogsSection? _dialogs;

    public GalleryWindow(GalleryOptions options)
    {
        InitializeComponent();
        Title = GalleryStrings.WindowTitle;
        _themeMode = options.Theme;
        _inkValue = options.Ink;
        _forceHighContrast = options.HighContrast ? true : null;

        BuildToolbar(options);

        var titles = GallerySections.Names.Select(GallerySections.Title).ToList();
        Navigation.ItemsSource = titles;
        AutomationProperties.SetName(Navigation, GalleryStrings.NavigationName);
        Navigation.SelectionChanged += (_, _) =>
        {
            if (Navigation.SelectedIndex >= 0)
            {
                Show(GallerySections.Names[Navigation.SelectedIndex]);
            }
        };
        ShowSection(options.Section ?? GallerySections.Names[0]);

        if (options.OpenMenu)
        {
            Opened += (_, _) => Dispatcher.UIThread.Post(() => _menus?.OpenMenu());
        }

        if (options.OpenDialog)
        {
            Opened += (_, _) => Dispatcher.UIThread.Post(() => _dialogs?.OpenDialog());
        }
    }

    /// <summary>The name of the section on show.</summary>
    internal string CurrentSection => GallerySections.Names[Navigation.SelectedIndex];

    /// <summary>How many sections the navigation lists.</summary>
    internal int NavigationCount => Navigation.ItemCount;

    /// <summary>True while the sample dialog opened by <c>--open-dialog</c> is open.</summary>
    internal bool IsDialogOpen => _dialogs?.IsDialogOpen == true;

    /// <summary>True while the menu opened by <c>--open-menu</c> is open.</summary>
    internal bool IsMenuOpen => _menus?.IsMenuOpen == true;

    /// <summary>True while the submenu opened by <c>--open-menu</c> is open.</summary>
    internal bool IsSubmenuOpen => _menus?.IsSubmenuOpen == true;

    /// <summary>Shows the section <paramref name="name"/> (selects it in the navigation).</summary>
    internal void ShowSection(string name) => Navigation.SelectedIndex = GallerySections.Names.ToList().IndexOf(name);

    internal void SelectTheme(ThemeMode mode)
    {
        _themeMode = mode;
        _theme.SelectedIndex = Array.IndexOf(ThemeModes, mode);
        ApplyTheme();
    }

    internal void SelectInk(AppInk ink)
    {
        _inkValue = ink;
        _ink.SelectedIndex = Array.IndexOf(Inks, ink);
        ApplyTheme();
    }

    internal void SetHighContrast(bool on)
    {
        _forceHighContrast = on;
        _highContrast.IsChecked = on;
        ApplyTheme();
    }

    /// <summary>The popup windows that are open on top of this window (their picture is not part of the window's own).</summary>
    internal IReadOnlyList<TopLevel> PopupTopLevels() => _menus?.PopupTopLevels(this) ?? [];

    private static TextBlock Label(string text) =>
        new() { Text = text, Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };

    private void Show(string name)
    {
        var section = GallerySections.Create(name);
        _menus = section as MenusSection;
        _dialogs = section as DialogsSection;
        Host.Content = section;
    }

    private void ApplyTheme() =>
        ShellTheme.Apply(Application.Current!, _themeMode, _inkValue, _forceHighContrast);

    private void BuildToolbar(GalleryOptions options)
    {
        AutomationProperties.SetName(Toolbar, GalleryStrings.ToolbarName);

        _theme.ItemsSource = new[] { GalleryStrings.ThemeLight, GalleryStrings.ThemeDark, GalleryStrings.ThemeSystem };
        _theme.SelectedIndex = Array.IndexOf(ThemeModes, options.Theme);
        AutomationProperties.SetName(_theme, GalleryStrings.ThemeLabel);
        _theme.SelectionChanged += (_, _) =>
        {
            if (_theme.SelectedIndex >= 0 && ThemeModes[_theme.SelectedIndex] != _themeMode)
            {
                SelectTheme(ThemeModes[_theme.SelectedIndex]);
            }
        };

        _highContrast.Content = GalleryStrings.HighContrast;
        _highContrast.IsChecked = options.HighContrast;
        AutomationProperties.SetName(_highContrast, GalleryStrings.HighContrast);
        _highContrast.IsCheckedChanged += (_, _) =>
        {
            if (_highContrast.IsChecked == true != (_forceHighContrast == true))
            {
                SetHighContrast(_highContrast.IsChecked == true);
            }
        };

        _ink.ItemsSource = new[] { GalleryStrings.InkWrite, GalleryStrings.InkSheets, GalleryStrings.InkSlides, GalleryStrings.InkPdf };
        _ink.SelectedIndex = Array.IndexOf(Inks, options.Ink);
        AutomationProperties.SetName(_ink, GalleryStrings.InkLabel);
        _ink.SelectionChanged += (_, _) =>
        {
            if (_ink.SelectedIndex >= 0 && Inks[_ink.SelectedIndex] != _inkValue)
            {
                SelectInk(Inks[_ink.SelectedIndex]);
            }
        };

        Toolbar.Children.Add(Group(GalleryStrings.ThemeLabel, _theme));
        Toolbar.Children.Add(_highContrast);
        Toolbar.Children.Add(Group(GalleryStrings.InkLabel, _ink));
    }

    private static StackPanel Group(string label, Control control)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        group.Children.Add(Label(label));
        group.Children.Add(control);
        return group;
    }
}
