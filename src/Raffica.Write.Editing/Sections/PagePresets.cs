using System.Globalization;

namespace Raffica.Write.Editing;

/// <summary>The four margins of a preset, in twips.</summary>
public readonly record struct PageMargins(int Top, int Bottom, int Left, int Right);

/// <summary>The sizes and margins the page setup commands offer, in twips.</summary>
public static class PagePresets
{
    /// <summary>The width and height of a named paper: A4, Letter, A3, A5 or Legal.</summary>
    public static (int WidthTwips, int HeightTwips) Size(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name switch
        {
            "A4" => (11906, 16838),
            "Letter" => (12240, 15840),
            "A3" => (16838, 23811),
            "A5" => (8391, 11906),
            "Legal" => (12240, 20160),
            _ => throw new ArgumentException("Unknown paper size.", nameof(name)),
        };
    }

    /// <summary>
    /// The margins of a named preset. Normal is 1 inch or 2.5 cm (as <c>BlankPaper</c> makes a new document); Narrow is 0.5 inch or 1.27 cm; Moderate has 1 inch (2.5 cm) above and below and 0.75 inch (1.91 cm)
    /// at the sides; Wide has 1 inch (2.5 cm) above and below and 2 inches (5.08 cm, 2880 twips) at the sides, as the presets of Word do.
    /// </summary>
    public static PageMargins Margins(string name, bool inch)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normal = inch ? 1440 : 1417;
        var moderate = inch ? 1080 : 1083;
        return name switch
        {
            "Normal" => new PageMargins(normal, normal, normal, normal),
            "Narrow" => new PageMargins(720, 720, 720, 720),
            "Moderate" => new PageMargins(normal, normal, moderate, moderate),
            "Wide" => new PageMargins(normal, normal, 2880, 2880),
            _ => throw new ArgumentException("Unknown margin preset.", nameof(name)),
        };
    }

    /// <summary>True where inches are the unit and Letter the paper: the rule of <c>BlankPaper.For</c> (the United States and Canada); a test pins the two against each other.</summary>
    public static bool UsesInches(RegionInfo region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return region.TwoLetterISORegionName is "US" or "CA";
    }
}
