using Avalonia.Controls;

namespace Raffica.Gallery.Sections;

/// <summary>The sections of the gallery by name (the value of <c>--section</c>), in the order of the navigation.</summary>
public static class GallerySections
{
    public static IReadOnlyList<string> Names { get; } = ["foundations", "buttons", "inputs", "menus", "dialogs"];

    internal static Control Create(string name) => name switch
    {
        "foundations" => new FoundationsSection(),
        "buttons" => new ButtonsSection(),
        "inputs" => new InputsSection(),
        "menus" => new MenusSection(),
        "dialogs" => new DialogsSection(),
        _ => throw new ArgumentException($"unknown section '{name}' (expected {string.Join(", ", Names)})", nameof(name)),
    };

    /// <summary>The title of a section as shown in the navigation.</summary>
    internal static string Title(string name) => name switch
    {
        "foundations" => GalleryStrings.SectionFoundations,
        "buttons" => GalleryStrings.SectionButtons,
        "inputs" => GalleryStrings.SectionInputs,
        "menus" => GalleryStrings.SectionMenus,
        "dialogs" => GalleryStrings.SectionDialogs,
        _ => throw new ArgumentException($"unknown section '{name}'", nameof(name)),
    };
}
