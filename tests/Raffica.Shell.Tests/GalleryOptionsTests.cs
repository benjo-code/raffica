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

    [Fact]
    public void TheControlViewIsTheDefaultAndFrameIsAnOption()
    {
        Assert.Equal(GalleryView.Gallery, GalleryOptions.Parse([]).View);
        Assert.Equal(GalleryView.Gallery, GalleryOptions.Parse(["--view", "gallery"]).View);
        Assert.Equal(GalleryView.Frame, GalleryOptions.Parse(["--view", "frame"]).View);
    }

    [Fact]
    public void FrameSwitchesParse()
    {
        var options = GalleryOptions.Parse(["--view", "frame", "--compact", "--settings-file", "x.json", "--measure-startup", "--measure-memory", "--measure-input"]);

        Assert.True(options.Compact);
        Assert.Equal("x.json", options.SettingsFile);
        Assert.True(options.MeasureStartup);
        Assert.True(options.MeasureMemory);
        Assert.True(options.MeasureInput);
        Assert.True(options.IsMeasuring);

        var defaults = GalleryOptions.Parse(["--view", "frame"]);
        Assert.False(defaults.Compact);
        Assert.Null(defaults.SettingsFile);
        Assert.False(defaults.IsMeasuring);
    }

    [Fact]
    public void AnExplicitLangOrThemeIsRemembered()
    {
        Assert.False(GalleryOptions.Parse([]).LangGiven);
        Assert.True(GalleryOptions.Parse(["--lang", "en"]).LangGiven);
        Assert.False(GalleryOptions.Parse([]).ThemeGiven);
        Assert.True(GalleryOptions.Parse(["--theme", "dark"]).ThemeGiven);
    }

    [Theory]
    [InlineData("--view", "nonsense")]
    [InlineData("--view")]
    [InlineData("--settings-file")]
    [InlineData("--measure-startup")]
    [InlineData("--measure-memory")]
    [InlineData("--measure-input")]
    [InlineData("--view", "gallery", "--measure-startup")]
    public void MeasurementSwitchesNeedTheFrameView(params string[] args) =>
        Assert.Throws<ArgumentException>(() => GalleryOptions.Parse(args));
}
