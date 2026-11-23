using System.Globalization;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>
/// The values of the payload commands of the Layout tab: <c>page.size</c> (<c>A4</c>, <c>Letter</c>, <c>A3</c>, <c>A5</c>, <c>Legal</c>), <c>page.margins</c> (<c>Normal</c>, <c>Narrow</c>, <c>Moderate</c>,
/// <c>Wide</c>), <c>page.orientation</c> (<c>portrait</c>, <c>landscape</c>), <c>page.columns</c> (<c>1</c>, <c>2</c>, <c>3</c>), <c>section.break</c> (<c>nextPage</c>, <c>continuous</c>, <c>evenPage</c>,
/// <c>oddPage</c>) and <c>insert.pageNumber</c> (<c>&lt;footer|header&gt;:&lt;left|center|right&gt;:&lt;plain|page|pageOfTotal|slash&gt;</c>). A payload of any other type or value is refused (the callers
/// return false and change nothing). The match is exact and case-sensitive: the values are ids, not text a user typed.
/// </summary>
internal static class SectionPayloads
{
    /// <summary>The papers of the size commands (the ribbon picker and the macOS menu list them in this order).</summary>
    public static IReadOnlyList<string> Papers { get; } = ["A4", "Letter", "A3", "A5", "Legal"];
    /// <summary>The margin presets of the margin commands, in the order of the picker and the menu.</summary>
    public static IReadOnlyList<string> MarginPresets { get; } = ["Normal", "Narrow", "Moderate", "Wide"];

    /// <summary>Whether <paramref name="payload"/> is a usable value of the payload command <paramref name="id"/>.</summary>
    public static bool IsValid(string id, object? payload) => id switch
    {
        WriteCommandIds.PageSize => TryPaper(payload, out _),
        WriteCommandIds.PageMargins => TryMargins(payload, out _),
        WriteCommandIds.PageOrientation => TryOrientation(payload, out _),
        WriteCommandIds.PageColumns => TryColumns(payload, out _),
        WriteCommandIds.SectionBreak => TryBreak(payload, out _),
        WriteCommandIds.InsertPageNumber => TryPageNumber(payload, out _, out _, out _),
        _ => false,
    };

    public static bool TryPaper(object? payload, out string name)
    {
        name = payload is string text && Papers.Contains(text) ? text : string.Empty;
        return name.Length > 0;
    }

    public static bool TryMargins(object? payload, out string name)
    {
        name = payload is string text && MarginPresets.Contains(text) ? text : string.Empty;
        return name.Length > 0;
    }

    public static bool TryOrientation(object? payload, out bool landscape)
    {
        landscape = payload is "landscape";
        return payload is "landscape" or "portrait";
    }

    public static bool TryColumns(object? payload, out int count)
    {
        count = 0;
        return payload is "1" or "2" or "3" && int.TryParse((string)payload, NumberStyles.None, CultureInfo.InvariantCulture, out count);
    }

    public static bool TryBreak(object? payload, out SectionBreakKind kind)
    {
        (var ok, kind) = payload switch
        {
            "nextPage" => (true, SectionBreakKind.NextPage),
            "continuous" => (true, SectionBreakKind.Continuous),
            "evenPage" => (true, SectionBreakKind.EvenPage),
            "oddPage" => (true, SectionBreakKind.OddPage),
            _ => (false, SectionBreakKind.NextPage),
        };
        return ok;
    }

    public static bool TryPageNumber(object? payload, out HeaderFooterKind kind, out PageNumberAlign align, out PageNumberLook look)
    {
        kind = HeaderFooterKind.Footer;
        align = PageNumberAlign.Center;
        look = PageNumberLook.Plain;
        if (payload is not string text)
        {
            return false;
        }

        var parts = text.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        HeaderFooterKind? place = parts[0] switch { "footer" => HeaderFooterKind.Footer, "header" => HeaderFooterKind.Header, _ => null };
        PageNumberAlign? side = parts[1] switch { "left" => PageNumberAlign.Left, "center" => PageNumberAlign.Center, "right" => PageNumberAlign.Right, _ => null };
        PageNumberLook? shape = parts[2] switch { "plain" => PageNumberLook.Plain, "page" => PageNumberLook.Page, "pageOfTotal" => PageNumberLook.PageOfTotal, "slash" => PageNumberLook.Slash, _ => null };
        if (place is not { } p || side is not { } s || shape is not { } l)
        {
            return false;
        }

        (kind, align, look) = (p, s, l);
        return true;
    }

    /// <summary>
    /// The page setup a size, margin, orientation or column command writes, made from the section's current setup (a value of the paper keeps the orientation of the section: a landscape section gets the
    /// long side as its width); null for a payload that is not a value of the command. <paramref name="inches"/> chooses the inch margin presets over the centimetre ones.
    /// </summary>
    public static PageSetupChange? PageSetupFor(string id, object? payload, SectionPageSetup current, bool inches)
    {
        ArgumentNullException.ThrowIfNull(current);
        switch (id)
        {
            case WriteCommandIds.PageSize when TryPaper(payload, out var paper):
                var (width, height) = PagePresets.Size(paper);
                return new PageSetupChange(current with { WidthTwips = current.Landscape ? height : width, HeightTwips = current.Landscape ? width : height }, PageSetupParts.Size);
            case WriteCommandIds.PageMargins when TryMargins(payload, out var preset):
                var m = PagePresets.Margins(preset, inches);
                return new PageSetupChange(current with { TopTwips = m.Top, BottomTwips = m.Bottom, LeftTwips = m.Left, RightTwips = m.Right }, PageSetupParts.Margins);
            case WriteCommandIds.PageOrientation when TryOrientation(payload, out var landscape):
                return new PageSetupChange(current.WithLandscape(landscape), PageSetupParts.Orientation);
            case WriteCommandIds.PageColumns when TryColumns(payload, out var count):
                return new PageSetupChange(current with { Columns = count }, PageSetupParts.Columns);
            default:
                return null;
        }
    }
}
