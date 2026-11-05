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
        Assert.Equal(51, commands.Count);
        Assert.All(commands, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.FieldCode));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
        });
    }

    [Fact]
    public void TabsAreHomeAnnotateFormsAndViewWithLettersHAFAndV()
    {
        var tabs = Create().Tabs;
        Assert.Equal(["H", "A", "F", "V"], tabs.Select(t => t.Letter).ToArray());
        Assert.Equal(["Home", "Annotate", "Forms", "View"], tabs.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void ExactlyOneGroupIsDestructiveAndItHoldsOnlyDelete()
    {
        var tabs = Create().Tabs;
        var group = Assert.Single(tabs.SelectMany(t => t.Groups), g => g.Destructive);
        Assert.Equal(["annotate.delete"], group.Commands.Select(c => c.Id).ToArray());
        Assert.Equal("A", tabs.Single(t => t.Groups.Contains(group)).Letter);
    }

    [Fact]
    public void FieldCodesAreUniqueAndPrefixFreePerTab()
    {
        foreach (var tab in Create().Tabs)
        {
            var codes = tab.Groups.SelectMany(g => g.Commands).Select(c => c.FieldCode).ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count());
            foreach (var a in codes)
            {
                foreach (var b in codes)
                {
                    Assert.True(a == b || !b.StartsWith(a, StringComparison.Ordinal), tab.Letter + ": " + a + " begins " + b);
                }
            }
        }
    }

    [Fact]
    public void TheGroupsAndFieldCodesFollowTheDesign()
    {
        var tabs = Create().Tabs;
        Assert.Equal(
            ["File:11,12,13,14,15,16", "Find:21,22,23", "Go to:31", "Pages:41,42,43,44", "Tabs:51,52", "Text:61,62", "Help:71", "History:81,82"],
            tabs[0].Groups.Select(Describe).ToArray());
        Assert.Equal(
            ["Markup:11,12,13", "Draw:21,22,23,24,25,26", "Edit:31,32,33,34", "Delete:41"],
            tabs[1].Groups.Select(Describe).ToArray());
        Assert.Equal(["Fill:11,12,13,14"], tabs[2].Groups.Select(Describe).ToArray());
        Assert.Equal(
            ["Zoom:11,12,13,14,15", "Layout:21,22,23,24", "Page:31", "Panes:41,42"],
            tabs[3].Groups.Select(Describe).ToArray());
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
