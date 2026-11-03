using Xunit;

namespace Raffica.Shell.Tests.Tokens;

public sealed class TokenParityTests
{
    [Fact]
    public void LightAndDarkHaveTheSameKeys() =>
        Assert.Equal(TokenFile.Load("Light").Keys.Order(), TokenFile.Load("Dark").Keys.Order());

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public void EveryColorHasABrushOfTheSameValue(string theme)
    {
        var colors = TokenFile.Load(theme);
        var brushes = TokenFile.LoadBrushes(theme);

        Assert.Equal(colors.Keys.Order(), brushes.Keys.Order());
        foreach (var (name, color) in colors)
        {
            Assert.Equal(color, brushes[name]);
        }
    }

    [Fact]
    public void HighContrastHasTheSameKeysAsLightAndDark()
    {
        var expected = TokenFile.Load("Light").Keys.Order().ToList();

        Assert.Equal(expected, TokenFile.Load("HighContrast").Keys.Order());
        Assert.Equal("none", TokenFile.LoadText("HighContrast", "Raffica.Shadow.Page"));
    }

    [Fact]
    public void HighContrastPairingsReachTheirThresholds()
    {
        var colors = TokenFile.Load("HighContrast");
        var failures = new List<string>();

        foreach (var (name, (kind, _)) in ContrastPolicy.Tokens)
        {
            if (kind is not (ContrastKind.Text or ContrastKind.Ui))
            {
                continue;
            }

            foreach (var background in ContrastPolicy.Backgrounds)
            {
                var ratio = WcagContrast.Ratio(colors[name], colors[background]);
                if (ratio < ContrastPolicy.Threshold(kind))
                {
                    failures.Add($"HighContrast {name} on {background}: {ratio:0.00}");
                }
            }
        }

        Assert.Empty(failures);
    }
}
