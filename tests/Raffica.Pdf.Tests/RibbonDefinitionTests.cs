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
        Assert.Equal(80, commands.Count);
        Assert.All(commands, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.FieldCode));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
        });
    }

    [Fact]
    public void TabsAreHomeAnnotateFormsPagesRedactEditAndViewWithLettersHAFPREAndV()
    {
        var tabs = Create().Tabs;
        Assert.Equal(["H", "A", "F", "P", "R", "E", "V"], tabs.Select(t => t.Letter).ToArray());
        Assert.Equal(["Home", "Annotate", "Forms", "Pages", "Redact", "Edit", "View"], tabs.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void PagesTabLettersAndFieldCodesAreUniqueAndPrefixFree()
    {
        var tabs = Create().Tabs;

        // The tab letters are unique and none is one the frame keeps (Q for the search of the title bar, 6 and 7 for the panes).
        var letters = tabs.Select(t => t.Letter).ToList();
        Assert.Equal(letters.Count, letters.Distinct().Count());
        Assert.DoesNotContain(letters, l => l is "Q" or "6" or "7");

        var pages = tabs.Single(t => t.Letter == "P");
        var codes = pages.Groups.SelectMany(g => g.Commands).Select(c => c.FieldCode).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, a => Assert.All(codes, b => Assert.True(a == b || !b.StartsWith(a, StringComparison.Ordinal), a + " begins " + b)));
    }

    [Fact]
    public void RedactAndEditTabsHaveUniqueLetters()
    {
        var tabs = Create().Tabs;
        var letters = tabs.Select(t => t.Letter).ToList();
        Assert.Equal(letters.Count, letters.Distinct().Count());
        Assert.DoesNotContain(letters, l => l is "Q" or "6" or "7");
        Assert.Equal("R", tabs.Single(t => t.Name == "Redact").Letter);
        Assert.Equal("E", tabs.Single(t => t.Name == "Edit").Letter);

        foreach (var letter in new[] { "R", "E" })
        {
            var codes = tabs.Single(t => t.Letter == letter).Groups.SelectMany(g => g.Commands).Select(c => c.FieldCode).ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count());
            Assert.All(codes, a => Assert.All(codes, b => Assert.True(a == b || !b.StartsWith(a, StringComparison.Ordinal), a + " begins " + b)));
        }
    }

    [Fact]
    public void TheDestructiveGroupsHoldOnlyDeleteAndAreLastInTheirTab()
    {
        var tabs = Create().Tabs;
        var groups = tabs.SelectMany(t => t.Groups).Where(g => g.Destructive).ToList();
        Assert.Equal(3, groups.Count);
        Assert.Equal(["annotate.delete"], groups[0].Commands.Select(c => c.Id).ToArray());
        Assert.Equal("A", tabs.Single(t => t.Groups.Contains(groups[0])).Letter);
        Assert.Equal(["pages.delete"], groups[1].Commands.Select(c => c.Id).ToArray());
        Assert.Equal("P", tabs.Single(t => t.Groups.Contains(groups[1])).Letter);
        Assert.Equal(["redact.deleteMarks"], groups[2].Commands.Select(c => c.Id).ToArray());
        Assert.Equal("R", tabs.Single(t => t.Groups.Contains(groups[2])).Letter);
        Assert.All(tabs, t => Assert.All(t.Groups.Where(g => g.Destructive), g => Assert.Same(g, t.Groups[^1])));
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
            ["Markup:11,12,13", "Draw:21,22,23,24,25,26", "Edit:31,32,33,34", "Sign:51", "Delete:41"],
            tabs[1].Groups.Select(Describe).ToArray());
        Assert.Equal(["Fill:11,12,13,14"], tabs[2].Groups.Select(Describe).ToArray());
        Assert.Equal(
            ["Organize:11", "Edit:21,22,23,24,25,26,27", "History:31,32", "Apply:41,42", "Output:51,52,53", "Delete:61"],
            tabs[3].Groups.Select(Describe).ToArray());
        Assert.Equal(
            ["Mark:11,12", "Find:21", "Look:31", "Apply:41,42", "Delete:51"],
            tabs[4].Groups.Select(Describe).ToArray());
        Assert.Equal(["Text:11,12", "Scan:21,22,23"], tabs[5].Groups.Select(Describe).ToArray());
        Assert.Equal(
            ["Zoom:11,12,13,14,15", "Layout:21,22,23,24", "Page:31", "Panes:41,42"],
            tabs[6].Groups.Select(Describe).ToArray());
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
