using Raffica.Shell;
using Xunit;

namespace Raffica.WriteApp.Tests;

public sealed class WriteOptionsTests
{
    [Fact]
    public void TheSharedSwitchesAreParsedAndFilesCollected()
    {
        var options = WriteOptions.Parse(["a.docx", "--lang", "de", "--theme", "dark", "--high-contrast", "--settings-file", "s.json", "b.docx"]);
        Assert.Equal(["a.docx", "b.docx"], options.Files);
        Assert.Equal("de", options.Lang);
        Assert.Equal(ThemeMode.Dark, options.Theme);
        Assert.True(options.ThemeGiven);
        Assert.True(options.HighContrast);
        Assert.Equal("s.json", options.SettingsFile);
        Assert.False(options.IsMeasuring);
    }

    [Fact]
    public void DefaultsWhenNothingIsGiven()
    {
        var options = WriteOptions.Parse([]);
        Assert.Empty(options.Files);
        Assert.Null(options.Lang);
        Assert.Equal(ThemeMode.System, options.Theme);
        Assert.False(options.ThemeGiven);
        Assert.False(options.HighContrast);
        Assert.Null(options.SettingsFile);
        Assert.Null(options.PrintString);
    }

    [Theory]
    [InlineData("--lang", "fr")]
    [InlineData("--theme", "blue")]
    [InlineData("--bogus", null)]
    [InlineData("--settings-file", null)]
    [InlineData("--print-string", null)]
    public void AnInvalidSwitchThrows(string name, string? value) =>
        Assert.Throws<ArgumentException>(() => WriteOptions.Parse(value is null ? [name] : [name, value]));

    [Theory]
    [InlineData("--measure-startup")]
    [InlineData("--measure-second")]
    [InlineData("--measure-scroll")]
    [InlineData("--measure-memory")]
    public void TheMeasurementSwitchesAreReadAndOnlyOneIsAllowed(string name)
    {
        var options = WriteOptions.Parse([name, "--settings-file", "s.json", "a.docx"]);
        Assert.Equal(name, options.MeasureSwitch);
        Assert.True(options.IsMeasuring);
        Assert.Throws<ArgumentException>(() => WriteOptions.Parse([name, "--measure-memory", "--measure-startup"]));
    }

    [Fact]
    public void AMacProcessSerialNumberIsNeitherAFileNorASwitch() =>
        Assert.Equal(["a.docx"], WriteOptions.Parse(["-psn_0_12345", "a.docx"]).Files);

    [Fact]
    public void WithFilesKeepsTheSwitchesAndReplacesTheFiles()
    {
        var options = WriteOptions.Parse(["a.docx", "--lang", "en", "--theme", "light", "--settings-file", "s.json"]);
        var other = options.WithFiles(["x.docx", "y.docx"]);
        Assert.Equal(["x.docx", "y.docx"], other.Files);
        Assert.Equal("en", other.Lang);
        Assert.Equal(ThemeMode.Light, other.Theme);
        Assert.True(other.ThemeGiven);
        Assert.Equal("s.json", other.SettingsFile);
    }
}
