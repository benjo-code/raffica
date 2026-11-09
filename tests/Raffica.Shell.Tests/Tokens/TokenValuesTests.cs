using Avalonia.Media;
using Xunit;

namespace Raffica.Shell.Tests.Tokens;

/// <summary>Every colour of appendix A sections 1.1 and 1.2 of the spec, copied as written there.</summary>
public sealed class TokenValuesTests
{
    // 8-digit values are #AARRGGBB in Avalonia (the spec writes CSS alpha): scrim light is rgba(26,29,33,0.32).
    // The dark scrim is a decision of milestone 1.1 (appendix B leaves it open): black at 50 %.
    public static readonly TheoryData<string, string, string> Rows = new()
    {
        { "Surface.Page", "#FFFFFF", "#1B1D21" },
        { "Surface.Band", "#F5F6F8", "#222429" },
        { "Surface.Desk", "#E9EBEE", "#131417" },
        { "State.Hover", "#E6E8EC", "#2C2F35" },
        { "Selection.Tint", "#DCDFE4", "#3A3E45" },
        { "Text.Primary", "#1A1D21", "#E8EAED" },
        { "Text.Secondary", "#4A5059", "#A9AFB8" },
        { "Text.Disabled", "#9AA0A8", "#5E646D" },
        { "Text.OnInk", "#FFFFFF", "#1B1D21" },
        { "Border.Control", "#7A818B", "#7D848E" },
        { "Rule", "#D3D7DD", "#373B42" },
        { "Grid.Line", "#DADDE2", "#34373D" },
        { "Button.Primary.Fill", "#1A1D21", "#E8EAED" },
        { "Button.Primary.Hover", "#3A3F46", "#C9CED6" },
        { "Scrim", "#521A1D21", "#80000000" },
        { "Ink.Write", "#1D4F91", "#8DB4EE" },
        { "Ink.Sheets", "#1E6B47", "#7CCBA2" },
        { "Ink.Slides", "#A3420F", "#F0A374" },
        { "Ink.Pdf", "#5E1A3E", "#E5A4C5" },
        { "Review.Violet", "#7B2FBE", "#B48CF0" },
        { "Review.Tint", "#F1EAF9", "#2E2840" },
        { "Danger", "#B21F2D", "#FF6F66" },
        { "Warning.Icon", "#8A6A00", "#D9A93A" },

        // On paper (M2.7): the same in every theme, because the thing they colour sits on white paper (an editor over a page).
        { "Selection.Field", "#5F6670", "#8F96A1" },
        { "Paper.Caret", "#000000", "#000000" },
        { "Paper.Edge", "#1A1D21", "#1A1D21" },
        { "Paper.Selection", "#5F6670", "#5F6670" },
        { "Paper.OnSelection", "#FFFFFF", "#FFFFFF" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void TokenValuesMatchAppendixA(string token, string light, string dark)
    {
        Assert.Equal(Color.Parse(light), TokenFile.Load("Light")[token]);
        Assert.Equal(Color.Parse(dark), TokenFile.Load("Dark")[token]);
    }

    [Fact]
    public void NoTokenOutsideAppendixAExists()
    {
        var known = Rows.Select(r => r.Data.Item1).ToHashSet();

        Assert.Empty(TokenFile.Load("Light").Keys.Except(known));
        Assert.Empty(TokenFile.Load("Dark").Keys.Except(known));
    }

    [Fact]
    public void ThePageShadowIsLightOnly()
    {
        Assert.Equal("0 1 2 0 #241A1D21, 0 8 24 0 #1A1A1D21", TokenFile.LoadText("Light", "Raffica.Shadow.Page"));
        Assert.Equal("none", TokenFile.LoadText("Dark", "Raffica.Shadow.Page"));
    }
}
