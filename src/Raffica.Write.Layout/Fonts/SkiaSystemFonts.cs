using SkiaSharp;

namespace Raffica.Write.Layout;

/// <summary>The installed fonts of the operating system, through Skia's font manager.</summary>
internal sealed class SkiaSystemFonts : ISystemFonts
{
    public FontFace? Match(string family, bool bold, bool italic)
    {
        try
        {
            var style = new SKFontStyle(
                bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            var typeface = SKFontManager.Default.MatchFamily(family, style);
            if (typeface is null)
            {
                return null;
            }

            // Some font managers answer an unknown family with their default font; that is not the family asked for.
            if (!string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                typeface.Dispose();
                return null;
            }

            return FontFace.FromTypeface(typeface, family, bold, italic);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public IEnumerable<string> Families()
    {
        try
        {
            return [.. SKFontManager.Default.FontFamilies];
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            return [];
        }
    }
}
