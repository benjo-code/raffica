using Raffica.Pdf.Commands;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests;

public sealed class RibbonDefinitionTests
{
    private static RibbonDefinition Create() => PdfRibbon.Create(new PdfCommands());

    [Fact]
    public void TheRibbonIsValid() => Create().Validate();

    [Fact]
    public void EveryCommandHasAFieldCodeAndAName()
    {
        var commands = Create().Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();
        Assert.Equal(22, commands.Count);
        Assert.All(commands, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.FieldCode));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
        });
    }

    [Fact]
    public void TabsAreHomeAndViewWithLettersHAndV()
    {
        var tabs = Create().Tabs;
        Assert.Equal(["H", "V"], tabs.Select(t => t.Letter).ToArray());
        Assert.Equal(["Home", "View"], tabs.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void TheGroupsAndFieldCodesFollowTheDesign()
    {
        var tabs = Create().Tabs;
        Assert.Equal(
            ["File:11,12", "Find:21,22,23", "Go to:31", "Pages:41,42,43,44"],
            tabs[0].Groups.Select(Describe).ToArray());
        Assert.Equal(
            ["Zoom:11,12,13,14,15", "Layout:21,22,23,24", "Page:31", "Panes:41,42"],
            tabs[1].Groups.Select(Describe).ToArray());
    }

    [Fact]
    public void TheCommandsRunWhatTheRecordHoldsAndNothingElse()
    {
        var ran = 0;
        var ribbon = PdfRibbon.Create(new PdfCommands(Open: () => ran++));
        ribbon.Tabs[0].Groups[0].Commands[0].Run!();
        Assert.Equal(1, ran);
        Assert.All(Create().Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands), c => Assert.Null(c.Run));
    }

    private static string Describe(RibbonGroupDefinition group) =>
        group.Label + ":" + string.Join(",", group.Commands.Select(c => c.FieldCode));
}
