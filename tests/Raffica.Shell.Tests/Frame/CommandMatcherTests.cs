using System.Globalization;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class CommandMatcherTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void FindsByWordPrefixIgnoringCase()
    {
        var ribbon = SampleRibbon.Create();

        var ids = CommandMatcher.Find(ribbon, "SOURCE form", English).Select(c => c.Id);
        var byShortPrefix = CommandMatcher.Find(ribbon, "fo", English).Select(c => c.Id).ToList();

        Assert.Equal(["paste.keep"], ids);
        Assert.Contains("painter", byShortPrefix); // "Format painter": the prefix starts the first word
        Assert.Contains("clearfmt", CommandMatcher.Find(ribbon, "clear fo", English).Select(c => c.Id)); // "Clear formatting"
        Assert.DoesNotContain("bold", byShortPrefix);
        // A prefix must start a word: "ormat" is inside "Format", it does not start it.
        Assert.Empty(CommandMatcher.Find(ribbon, "ormat", English));
    }

    [Fact]
    public void FindsGermanNamesWithUmlauts()
    {
        var ribbon = new RibbonDefinition(
        [
            new RibbonTabDefinition("Start", "S",
            [
                new RibbonGroupDefinition("Absatz", "1",
                [
                    new CommandDefinition("align", "Ausrichtung", "Raffica.Icon.AlignLeft", "AU"),
                    new CommandDefinition("indent", "Äußerer Einzug", "Raffica.Icon.AlignLeft", "EZ"),
                ]),
            ]),
        ]);

        Assert.Equal(["align"], CommandMatcher.Find(ribbon, "ausr", German).Select(c => c.Id));
        Assert.Equal(["indent"], CommandMatcher.Find(ribbon, "äuß", German).Select(c => c.Id));
    }

    [Fact]
    public void FindsCommandsInsideMenus()
    {
        var ids = CommandMatcher.Find(SampleRibbon.Create(), "keep", English).Select(c => c.Id).ToList();

        Assert.Equal(["paste.keep", "paste.text"], ids);
    }

    [Fact]
    public void ResultsFollowTheRibbonOrderAcrossTabs()
    {
        // "bo" starts "Bold" (Home) and "Bookmark" (Insert); "pa" starts Paste before Format painter in the same group.
        Assert.Equal(["bold", "bookmark"], CommandMatcher.Find(SampleRibbon.Create(), "bo", English).Select(c => c.Id));
        Assert.Equal(["paste", "painter"], CommandMatcher.Find(SampleRibbon.Create(), "pa", English).Select(c => c.Id).Take(2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQueryGivesNothing(string query) =>
        Assert.Empty(CommandMatcher.Find(SampleRibbon.Create(), query, English));

    [Fact]
    public void NoMatchGivesNothing() =>
        Assert.Empty(CommandMatcher.Find(SampleRibbon.Create(), "zzzz", English));
}
