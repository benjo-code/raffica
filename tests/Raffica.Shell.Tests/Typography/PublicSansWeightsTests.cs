using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Raffica.Shell.Tests.Typography;

public sealed class PublicSansWeightsTests
{
    private const string Sample = "Save the document as a new file";

    [AvaloniaFact]
    public void Weights400500And600HaveDifferentAdvanceWidths()
    {
        var widths = new[] { FontWeight.Normal, FontWeight.Medium, FontWeight.SemiBold }
            .Select(WidthOf)
            .ToArray();

        Assert.Equal(3, widths.Distinct().Count());
        Assert.True(widths[0] < widths[1] && widths[1] < widths[2], string.Join(" < ", widths));
    }

    private static double WidthOf(FontWeight weight)
    {
        Assert.True(Application.Current!.TryGetResource("Raffica.Font.Ui", Application.Current!.ActualThemeVariant, out var font));
        var text = new TextBlock { Text = Sample, FontFamily = (FontFamily)font!, FontSize = 14, FontWeight = weight };
        text.Measure(Size.Infinity);
        return text.DesiredSize.Width;
    }
}
