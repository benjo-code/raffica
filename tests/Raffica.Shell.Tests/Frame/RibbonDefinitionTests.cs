using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class RibbonDefinitionTests
{
    private static CommandDefinition Cmd(string code, CommandSize size = CommandSize.Small, bool iconOnly = false) =>
        new("id." + code, "Name " + code, "icon", code, Size: size, IconOnly: iconOnly);

    private static RibbonDefinition Ribbon(params RibbonTabDefinition[] tabs) => new(tabs);

    private static RibbonTabDefinition Tab(string letter, params CommandDefinition[] commands) =>
        new("Tab " + letter, letter, [new RibbonGroupDefinition("Group", "1", commands)]);

    [Fact]
    public void ValidRibbonPasses()
    {
        Ribbon(Tab("H", Cmd("11"), Cmd("A")), Tab("E", Cmd("11"))).Validate();
    }

    [Fact]
    public void DuplicateCodeInATabFails()
    {
        var ribbon = Ribbon(Tab("H", Cmd("11"), Cmd("11")));

        var ex = Assert.Throws<InvalidOperationException>(ribbon.Validate);
        Assert.Contains("Tab H", ex.Message);
        Assert.Contains("11", ex.Message);
    }

    [Fact]
    public void TheSameCodeInTwoTabsIsAllowed()
    {
        Ribbon(Tab("H", Cmd("11")), Tab("E", Cmd("11"))).Validate();
    }

    [Fact]
    public void DuplicateTabLetterFails()
    {
        var ribbon = Ribbon(Tab("H", Cmd("11")), Tab("H", Cmd("12")));

        var ex = Assert.Throws<InvalidOperationException>(ribbon.Validate);
        Assert.Contains("H", ex.Message);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("123")]
    [InlineData("")]
    public void MalformedCodeFails(string code)
    {
        var ribbon = Ribbon(Tab("H", Cmd(code)));

        Assert.Throws<InvalidOperationException>(ribbon.Validate);
    }

    [Fact]
    public void EmptyGroupFails()
    {
        var ribbon = Ribbon(new RibbonTabDefinition("Tab H", "H", [new RibbonGroupDefinition("Group", "1", [])]));

        Assert.Throws<InvalidOperationException>(ribbon.Validate);
    }

    [Fact]
    public void ALargeIconOnlyCommandFails()
    {
        var ribbon = Ribbon(Tab("H", Cmd("11", CommandSize.Large, iconOnly: true)));

        Assert.Throws<InvalidOperationException>(ribbon.Validate);
    }
}
