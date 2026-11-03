using Xunit;

namespace Raffica.Shell.Tests.Tokens;

public sealed class ContrastTests
{
    public static readonly TheoryData<string> Themes = ["Light", "Dark"];

    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryTokenIsClassified(string theme)
    {
        var tokens = TokenFile.Load(theme).Keys.ToHashSet();
        var classified = ContrastPolicy.Tokens.Keys.ToHashSet();

        Assert.Empty(tokens.Except(classified));
        Assert.Empty(classified.Except(tokens));
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void PairingsMeetTheirThreshold(string theme)
    {
        var colors = TokenFile.Load(theme);
        var failures = new List<string>();

        foreach (var (name, (kind, _)) in ContrastPolicy.Tokens)
        {
            if (kind is not (ContrastKind.Text or ContrastKind.Ui))
            {
                continue;
            }

            foreach (var background in ContrastPolicy.Backgrounds)
            {
                if (ContrastPolicy.IsExempt(name, background, theme))
                {
                    continue;
                }

                var ratio = WcagContrast.Ratio(colors[name], colors[background]);
                if (ratio < ContrastPolicy.Threshold(kind))
                {
                    failures.Add($"{theme} {name} on {background}: {ratio:0.00} < {ContrastPolicy.Threshold(kind)}");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void FilledPairingsMeetTheirThreshold(string theme)
    {
        var c = TokenFile.Load(theme);
        var failures = new List<string>();

        void Check(string foreground, string background, double threshold)
        {
            var ratio = WcagContrast.Ratio(c[foreground], c[background]);
            if (ratio < threshold)
            {
                failures.Add($"{theme} {foreground} on {background}: {ratio:0.00} < {threshold}");
            }
        }

        foreach (var fill in new[] { "Ink.Write", "Ink.Sheets", "Ink.Slides", "Ink.Pdf", "Review.Violet", "Danger", "Button.Primary.Fill", "Button.Primary.Hover" })
        {
            Check("Text.OnInk", fill, 4.5);
        }

        Check("Text.Primary", "Review.Tint", 4.5);
        Check("Review.Violet", "Review.Tint", 3.0);

        Assert.Empty(failures);
    }

    [Fact]
    public void ForbiddenPairingsReallyFail()
    {
        foreach (var (foreground, background, theme) in ContrastPolicy.Forbidden)
        {
            var colors = TokenFile.Load(theme);
            var kind = ContrastPolicy.Tokens[foreground].Kind;

            Assert.True(
                WcagContrast.Ratio(colors[foreground], colors[background]) < ContrastPolicy.Threshold(kind),
                $"{theme} {foreground} on {background} now passes: take it off the forbidden list.");
        }
    }

    [Fact]
    public void ExemptPairingsReallyFail()
    {
        foreach (var (foreground, background, _) in ContrastPolicy.ExemptPairings)
        {
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var colors = TokenFile.Load(theme);
                var kind = ContrastPolicy.Tokens[foreground].Kind;

                Assert.True(
                    WcagContrast.Ratio(colors[foreground], colors[background]) < ContrastPolicy.Threshold(kind),
                    $"{theme} {foreground} on {background} now passes: drop the exemption.");
            }
        }
    }
}
