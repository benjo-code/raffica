using System.Globalization;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Shell.Tests.Localization;

public sealed class StringTableTests
{
    private static StringTable Shell(string lang) =>
        new("Raffica.Shell.Strings", typeof(ShellStrings).Assembly) { Culture = new CultureInfo(lang) };

    private static StringTable Test(string lang) =>
        new("Raffica.Shell.Tests.TestStrings", typeof(StringTableTests).Assembly) { Culture = new CultureInfo(lang) };

    [Fact]
    public void AGermanTableReturnsTheGermanValue() => Assert.Equal("Abbrechen", Shell("de").Get("Cancel"));

    [Fact]
    public void AnEnglishTableReturnsTheEnglishValue() => Assert.Equal("Cancel", Shell("en").Get("Cancel"));

    [Fact]
    public void ARegionalGermanCultureIsGerman() => Assert.Equal("Schließen", Shell("de-AT").Get("Close"));

    [Fact]
    public void AnAbsentKeyThrowsWithTheKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Shell("de").Get("NoSuchKey"));
        Assert.Equal("Missing string: NoSuchKey", ex.Message);
    }

    [Fact]
    public void ArgumentsAreFormattedWithTheCultureOfTheTable()
    {
        Assert.Equal("Wert 1.234,5", Test("de").Get("Number", 1234.5));
        Assert.Equal("Value 1,234.5", Test("en").Get("Number", 1234.5));
    }

    [Fact]
    public void TheShellStringsFollowTheirCulture()
    {
        var before = ShellStrings.Culture;
        try
        {
            ShellStrings.Culture = new CultureInfo("de");
            Assert.Equal("Fehler", ShellStrings.NotificationError);
            ShellStrings.Culture = new CultureInfo("en");
            Assert.Equal("Error", ShellStrings.NotificationError);
        }
        finally
        {
            ShellStrings.Culture = before;
        }
    }
}
