using Avalonia.Media;
using Xunit;

namespace Raffica.Shell.Tests.Tokens;

public sealed class WcagContrastTests
{
    [Fact]
    public void WhiteOnBlackIsTwentyOne() =>
        Assert.Equal(21.0, WcagContrast.Ratio(Colors.White, Colors.Black), 6);

    [Fact]
    public void TheRatioIsSymmetric() =>
        Assert.Equal(
            WcagContrast.Ratio(Color.Parse("#1A1D21"), Colors.White),
            WcagContrast.Ratio(Colors.White, Color.Parse("#1A1D21")),
            10);

    [Theory]
    [InlineData("#1A1D21", "#FFFFFF", 16.91)]
    [InlineData("#B48CF0", "#3A3E45", 4.07)]
    [InlineData("#FF6F66", "#3A3E45", 3.95)]
    public void MatchesTheMeasuredValuesOfAppendixA(string foreground, string background, double expected) =>
        Assert.Equal(expected, WcagContrast.Ratio(Color.Parse(foreground), Color.Parse(background)), 2);
}
