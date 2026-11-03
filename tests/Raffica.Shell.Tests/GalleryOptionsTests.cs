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
        Assert.Null(options.Section);
        Assert.False(options.OpenMenu);
        Assert.False(options.OpenDialog);
        Assert.True(options.Lang is "en" or "de");
    }

    [Fact]
    public void ParsesEverySwitch()
    {
        var options = GalleryOptions.Parse(
            ["--lang", "de", "--theme", "dark", "--high-contrast", "--ink", "pdf", "--screenshot", "out.png", "--print-string", "Close", "--section", "dialogs", "--open-menu", "--open-dialog"]);

        Assert.Equal("de", options.Lang);
        Assert.Equal(ThemeMode.Dark, options.Theme);
        Assert.True(options.HighContrast);
        Assert.Equal(AppInk.Pdf, options.Ink);
        Assert.Equal("out.png", options.Screenshot);
        Assert.Equal("Close", options.PrintString);
        Assert.Equal("dialogs", options.Section);
        Assert.True(options.OpenMenu);
        Assert.True(options.OpenDialog);
    }

    [Theory]
    [InlineData("foundations")]
    [InlineData("buttons")]
    [InlineData("inputs")]
    [InlineData("menus")]
    [InlineData("dialogs")]
    public void EverySectionNameIsAccepted(string name) =>
        Assert.Equal(name, GalleryOptions.Parse(["--section", name]).Section);

    [Theory]
    [InlineData("--lang", "fr")]
    [InlineData("--theme", "blue")]
    [InlineData("--ink", "red")]
    [InlineData("--section", "nope")]
    [InlineData("--bogus", "x")]
    [InlineData("--lang")]
    public void InvalidSwitchesThrow(params string[] args) =>
        Assert.Throws<ArgumentException>(() => GalleryOptions.Parse(args));
}

public sealed class PrintStringCommandTests
{
    [Theory]
    [InlineData("de", "Abbrechen")]
    [InlineData("en", "Cancel")]
    public void PrintsTheStringInTheChosenLanguage(string lang, string expected)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = PrintStringCommand.Run(GalleryOptions.Parse(["--print-string", "Cancel", "--lang", lang]), output, error);

        Assert.Equal(0, code);
        Assert.Equal(expected + Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void AnUnknownKeyIsAnErrorOnStderr()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = PrintStringCommand.Run(GalleryOptions.Parse(["--print-string", "Nope", "--lang", "en"]), output, error);

        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal("error: Missing string: Nope" + Environment.NewLine, error.ToString());
    }
}
