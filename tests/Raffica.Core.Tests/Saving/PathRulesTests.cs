using Raffica.Core.Saving;
using Xunit;

namespace Raffica.Core.Tests.Saving;

public sealed class PathRulesTests
{
    [Fact]
    public void WindowsIgnoresCase() =>
        Assert.True(PathRules.AreEqual(@"C:\A\Bericht.pdf", @"c:\a\bericht.pdf", PathSystem.Windows));

    [Fact]
    public void LinuxKeepsCase() =>
        Assert.False(PathRules.AreEqual("/a/Bericht.pdf", "/a/bericht.pdf", PathSystem.Linux));

    [Fact]
    public void MacOsIgnoresCase() =>
        Assert.True(PathRules.AreEqual("/Users/x/Bericht.pdf", "/Users/x/bericht.pdf", PathSystem.MacOS));

    [Fact]
    public void MacOsTreatsNfcAndNfdAsOneName()
    {
        Assert.True(PathRules.AreEqual("/x/\u00dcbersicht.pdf", "/x/U\u0308bersicht.pdf", PathSystem.MacOS));
        Assert.False(PathRules.AreEqual("/x/\u00dcbersicht.pdf", "/x/Ubersicht.pdf", PathSystem.MacOS));
    }

    [Fact]
    public void MacOsDifferentNamesStayDifferent() =>
        Assert.False(PathRules.AreEqual("/x/a.pdf", "/x/b.pdf", PathSystem.MacOS));

    [Fact]
    public void NullHandling()
    {
        Assert.True(PathRules.AreEqual(null, null));
        Assert.False(PathRules.AreEqual("a", null));
        Assert.False(PathRules.AreEqual(null, "a"));
    }

    [Fact]
    public void ComparerDeduplicatesLikeAreEqual()
    {
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), PathRules.IsCaseInsensitive);
        var set = new HashSet<string>(PathRules.Comparer) { "Bericht.pdf", "bericht.pdf" };
        Assert.Equal(PathRules.IsCaseInsensitive ? 1 : 2, set.Count);
    }
}
