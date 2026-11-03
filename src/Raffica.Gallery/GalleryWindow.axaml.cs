using Avalonia.Controls;
using Raffica.Gallery.Sections;

namespace Raffica.Gallery;

/// <summary>Shows the sections as a scrollable stack (navigation between them comes with the gallery app of milestone 1.1).</summary>
internal sealed partial class GalleryWindow : Window
{
    public GalleryWindow(GalleryOptions options)
    {
        InitializeComponent();
        Title = GalleryStrings.WindowTitle;
        foreach (var name in options.Section is { } only ? [only] : GallerySections.Names)
        {
            Sections.Children.Add(GallerySections.Create(name));
        }
    }
}
