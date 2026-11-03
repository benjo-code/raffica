using System.Globalization;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Shell.Tests.Localization;

public sealed class LanguagesTests
{
    [Fact]
    public void NullFollowsAGermanSystem() =>
        Assert.Equal("de", Languages.Resolve(null, new CultureInfo("de-AT")).TwoLetterISOLanguageName);

    [Fact]
    public void NullFollowsAnyOtherSystemAsEnglish() =>
        Assert.Equal("en", Languages.Resolve(null, new CultureInfo("fr-FR")).TwoLetterISOLanguageName);

    [Fact]
    public void ARequestedLanguageWinsOverTheSystem() =>
        Assert.Equal("en", Languages.Resolve("en", new CultureInfo("de-DE")).TwoLetterISOLanguageName);

    [Fact]
    public void AnUnknownLanguageThrows() =>
        Assert.Throws<ArgumentException>(() => Languages.Resolve("fr", new CultureInfo("de-DE")));
}
