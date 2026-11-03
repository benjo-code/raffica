using Avalonia.Media;

namespace Raffica.Shell.Tests.Tokens;

/// <summary>WCAG 2.x contrast ratio (relative luminance), the measure of appendix A section 2 of the spec. Alpha is ignored.</summary>
public static class WcagContrast
{
    public static double Ratio(Color foreground, Color background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
