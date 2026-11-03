using Avalonia.Controls;

namespace Raffica.Gallery.Sections;

/// <summary>The sections of the gallery by name (the value of <c>--section</c>). Each control task of milestone 1.1 adds its own.</summary>
public static class GallerySections
{
    public static IReadOnlyList<string> Names { get; } = ["buttons", "inputs", "menus"];

    internal static Control Create(string name) => name switch
    {
        "buttons" => new ButtonsSection(),
        "inputs" => new InputsSection(),
        "menus" => new MenusSection(),
        _ => throw new ArgumentException($"unknown section '{name}' (expected {string.Join(", ", Names)})", nameof(name)),
    };
}
