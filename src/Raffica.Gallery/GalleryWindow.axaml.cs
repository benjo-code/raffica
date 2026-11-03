using Avalonia.Controls;
using Avalonia.Threading;
using Raffica.Gallery.Sections;

namespace Raffica.Gallery;

/// <summary>Shows the sections as a scrollable stack (navigation between them comes with the gallery app of milestone 1.1).</summary>
internal sealed partial class GalleryWindow : Window
{
    private readonly MenusSection? _menus;
    private readonly DialogsSection? _dialogs;

    public GalleryWindow(GalleryOptions options)
    {
        InitializeComponent();
        Title = GalleryStrings.WindowTitle;
        foreach (var name in options.Section is { } only ? [only] : GallerySections.Names)
        {
            var section = GallerySections.Create(name);
            Sections.Children.Add(section);
            _menus ??= section as MenusSection;
            _dialogs ??= section as DialogsSection;
        }

        if (options.OpenMenu && _menus is { } menus)
        {
            Opened += (_, _) => Dispatcher.UIThread.Post(menus.OpenMenu);
        }

        if (options.OpenDialog && _dialogs is { } dialogs)
        {
            Opened += (_, _) => Dispatcher.UIThread.Post(dialogs.OpenDialog);
        }
    }

    /// <summary>True while the sample dialog opened by <c>--open-dialog</c> is open.</summary>
    internal bool IsDialogOpen => _dialogs?.IsDialogOpen == true;

    /// <summary>True while the menu opened by <c>--open-menu</c> is open.</summary>
    internal bool IsMenuOpen => _menus?.IsMenuOpen == true;

    /// <summary>True while the submenu opened by <c>--open-menu</c> is open.</summary>
    internal bool IsSubmenuOpen => _menus?.IsSubmenuOpen == true;

    /// <summary>The popup windows that are open on top of this window (their picture is not part of the window's own).</summary>
    internal IReadOnlyList<TopLevel> PopupTopLevels() => _menus?.PopupTopLevels(this) ?? [];
}
