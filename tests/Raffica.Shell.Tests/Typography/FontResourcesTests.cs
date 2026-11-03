using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Raffica.Shell.Tests.Typography;

public sealed class FontResourcesTests
{
    [AvaloniaTheory]
    [InlineData("avares://Raffica.Shell/Assets/Fonts#Public Sans", "Public Sans")]
    [InlineData("avares://Raffica.Shell/Assets/Fonts#Barlow Semi Condensed", "Barlow Semi Condensed")]
    [InlineData("avares://Raffica.Shell/Assets/Fonts#Carlito", "Carlito")]
    public void AllThreeFamiliesResolveFromTheShellAssembly(string uri, string family)
    {
        var typeface = new Typeface(new FontFamily(uri));

        Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs));
        Assert.StartsWith(family, glyphs.FamilyName);
    }

    [AvaloniaTheory]
    [InlineData("Raffica.Font.Ui", "Public Sans")]
    [InlineData("Raffica.Font.FieldCode", "Barlow Semi Condensed")]
    [InlineData("Raffica.Font.Document", "Carlito")]
    public void TheFontResourcesPointToTheEmbeddedFamilies(string key, string family)
    {
        Assert.True(Application.Current!.TryGetResource(key, Application.Current!.ActualThemeVariant, out var value));

        var font = Assert.IsType<FontFamily>(value);
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(font), out var glyphs));
        Assert.StartsWith(family, glyphs.FamilyName);
    }
}
