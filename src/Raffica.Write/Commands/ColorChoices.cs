using Raffica.Shell.Frame;

namespace Raffica.Write.Commands;

/// <summary>One colour of a list: <paramref name="Id"/> is what the command takes as its value, <paramref name="NameKey"/> the string key of its name, <paramref name="Argb"/> the colour as the layout reads it.</summary>
internal sealed record ColorChoice(string Id, string NameKey, uint Argb);

/// <summary>
/// The fixed colour lists of the Home tab (4.3b has no colour dialog): the text colours (the standard ones of Word, ids are <c>RRGGBB</c>) and the sixteen highlight colours of Word (ids are the
/// Word names, <c>none</c> removes it). The data is here; the names are strings in both languages.
/// </summary>
internal static class ColorChoices
{
    /// <summary>The id of the "no highlight" item.</summary>
    public const string NoHighlight = "none";

    /// <summary>The text colours.</summary>
    public static IReadOnlyList<ColorChoice> TextColors { get; } =
    [
        new("000000", "ColorBlack", 0xFF000000),
        new("7F7F7F", "ColorGray", 0xFF7F7F7F),
        new("FFFFFF", "ColorWhite", 0xFFFFFFFF),
        new("C00000", "ColorDarkRed", 0xFFC00000),
        new("FF0000", "ColorRed", 0xFFFF0000),
        new("FFC000", "ColorOrange", 0xFFFFC000),
        new("FFFF00", "ColorYellow", 0xFFFFFF00),
        new("92D050", "ColorLightGreen", 0xFF92D050),
        new("00B050", "ColorGreen", 0xFF00B050),
        new("00B0F0", "ColorLightBlue", 0xFF00B0F0),
        new("0070C0", "ColorBlue", 0xFF0070C0),
        new("002060", "ColorDarkBlue", 0xFF002060),
        new("7030A0", "ColorPurple", 0xFF7030A0),
    ];

    /// <summary>The highlight colours (the values are the ones the layout draws for these names).</summary>
    public static IReadOnlyList<ColorChoice> Highlights { get; } =
    [
        new("yellow", "HlYellow", 0xFFFFFF00),
        new("green", "HlGreen", 0xFF00FF00),
        new("cyan", "HlCyan", 0xFF00FFFF),
        new("magenta", "HlMagenta", 0xFFFF00FF),
        new("blue", "HlBlue", 0xFF0000FF),
        new("red", "HlRed", 0xFFFF0000),
        new("darkBlue", "HlDarkBlue", 0xFF000080),
        new("darkCyan", "HlDarkCyan", 0xFF008080),
        new("darkGreen", "HlDarkGreen", 0xFF008000),
        new("darkMagenta", "HlDarkMagenta", 0xFF800080),
        new("darkRed", "HlDarkRed", 0xFF800000),
        new("darkYellow", "HlDarkYellow", 0xFF808000),
        new("darkGray", "HlDarkGray", 0xFF808080),
        new("lightGray", "HlLightGray", 0xFFC0C0C0),
        new("black", "HlBlack", 0xFF000000),
        new("white", "HlWhite", 0xFFFFFFFF),
    ];

    /// <summary>The colour of a Word highlight name, or null for a name that is none.</summary>
    public static uint? HighlightValue(string name) => Highlights.FirstOrDefault(h => h.Id == name)?.Argb;

    /// <summary>The picker items of the text colours, names in the UI language.</summary>
    public static IReadOnlyList<PickerItem> TextItems() => [.. TextColors.Select(c => new PickerItem(c.Id, WriteStrings.Get(c.NameKey)))];

    /// <summary>The picker items of the highlights: no highlight first, then the colours.</summary>
    public static IReadOnlyList<PickerItem> HighlightItems() =>
        [new PickerItem(NoHighlight, WriteStrings.Get("HlNone")), .. Highlights.Select(h => new PickerItem(h.Id, WriteStrings.Get(h.NameKey)))];

    /// <summary>The id of the text colour list that matches a colour (alpha ignored), or null when the colour is not on the list (or is automatic).</summary>
    public static string? TextIdOf(uint? argb) => argb is { } value ? TextColors.FirstOrDefault(c => (c.Argb & 0xFFFFFF) == (value & 0xFFFFFF))?.Id : null;

    /// <summary>The highlight id for a colour: <see cref="NoHighlight"/> for none, a Word name, or null for a colour that is on no list.</summary>
    public static string? HighlightIdOf(uint? argb) => argb is not { } value ? NoHighlight : Highlights.FirstOrDefault(h => h.Argb == value)?.Id;
}
