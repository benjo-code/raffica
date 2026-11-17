using System.Globalization;

namespace Raffica.Write.Docx;

/// <summary>A page size and one margin for all four sides, in twips (1/20 pt).</summary>
public readonly record struct PaperSize(int WidthTwips, int HeightTwips, int MarginTwips)
{
    /// <summary>A4 with 2.5 cm margins.</summary>
    public static PaperSize A4 { get; } = new(11906, 16838, 1417);

    /// <summary>US Letter with 1 inch margins.</summary>
    public static PaperSize Letter { get; } = new(12240, 15840, 1440);
}

/// <summary>The paper a new document starts with: Letter where it is the custom (US, Canada), A4 elsewhere.</summary>
public static class BlankPaper
{
    public static PaperSize For(RegionInfo region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return region.TwoLetterISORegionName is "US" or "CA" ? PaperSize.Letter : PaperSize.A4;
    }
}
