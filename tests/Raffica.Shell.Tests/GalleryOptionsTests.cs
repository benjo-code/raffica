using Raffica.Gallery;
using Xunit;

namespace Raffica.Shell.Tests;

public sealed class GalleryOptionsTests
{
    [Fact]
    public void DefaultsAreSystemThemeAndWriteInk()
    {
        var options = GalleryOptions.Parse([]);

        Assert.Equal(ThemeMode.System, options.Theme);
        Assert.Equal(AppInk.Write, options.Ink);
        Assert.False(options.HighContrast);
        Assert.Null(options.Screenshot);
        Assert.Null(options.PrintString);
        Assert.True(options.Lang is "en" or "de");
    }

    [Fact]
    public void ParsesEverySwitch()
    {
        var options = GalleryOptions.Parse(
            ["--lang", "de", "--theme", "dark", "--high-contrast", "--ink", "pdf", "--screenshot", "out.png", "--print-string", "Close"]);

        Assert.Equal("de", options.Lang);
        Assert.Equal(ThemeMode.Dark, options.Theme);
        Assert.True(options.HighContrast);
        Assert.Equal(AppInk.Pdf, options.Ink);
        Assert.Equal("out.png", options.Screenshot);
        Assert.Equal("Close", options.PrintString);
    }

    [Theory]
    [InlineData("--lang", "fr")]
    [InlineData("--theme", "blue")]
    [InlineData("--ink", "red")]
    [InlineData("--bogus", "x")]
    [InlineData("--lang")]
    public void InvalidSwitchesThrow(params string[] args) =>
        Assert.Throws<ArgumentException>(() => GalleryOptions.Parse(args));
}
