using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Frame;
using Xunit;

namespace Raffica.Pdf.Tests;

/// <summary>M2.7 Task 10 step 3: the ribbon labels of the app in both languages (short German names, no clipped or cut label on any tab).</summary>
public sealed class PdfRibbonLabelTests
{
    public static TheoryData<string, string> GermanLabels => new()
    {
        { "CmdAnnotateMode", "Anmerken" },
        { "CmdApplyPages", "Änderungen anwenden" },
        { "CmdFormsHighlight", "Felder hervorheben" },
        { "CmdPagesRedo", "Wiederholen" },
        { "CmdPagesUndo", "Rückgängig" },
        { "CmdDiscardPages", "Verwerfen" },
        { "CmdExportImages", "Als Bilder exportieren …" },
        { "CmdInsertFile", "Aus Datei einfügen …" },
    };

    public static TheoryData<string, int> LanguagesAndWidths => new()
    {
        { "en", 1920 },
        { "de", 1920 },
        { "en", 1280 },
        { "de", 1280 },
        { "de", 1024 },
    };

    [Theory]
    [MemberData(nameof(GermanLabels))]
    public void TheGermanLabelIsTheShortOne(string key, string expected) =>
        Assert.Equal(expected, PdfStrings.GetIn(key, CultureInfo.GetCultureInfo("de")));

    [Fact]
    public void TheFormsHighlightCommandAndTheBadgeSwitchSayTheSameInGerman()
    {
        var german = CultureInfo.GetCultureInfo("de");
        Assert.Equal(PdfStrings.GetIn("FormHighlightToggle", german), PdfStrings.GetIn("CmdFormsHighlight", german));
    }

    [AvaloniaTheory]
    [MemberData(nameof(LanguagesAndWidths))]
    public async Task NoRibbonLabelOfAnyTabIsClippedOrCutInTheMiddleOfAWord(string language, int width)
    {
        using var f = await PdfRibbonLayoutTests.ShowWithDocumentAsync(language, width);
        var window = f.Window;
        var clipped = new List<string>();
        foreach (var tab in window.Ribbon.Definition!.Tabs)
        {
            window.Ribbon.ActiveTab = tab;
            PdfRibbonLayoutTests.Layout(window);

            // The ribbon only: the status bar text of the page size is reported by LabelFit as well and belongs to the status bar review (Task 11).
            clipped.AddRange(LabelFit.FindClipped(window).Where(c => c.Contains("(ribbon", StringComparison.Ordinal)).Select(c => $"{tab.Name}: {c}"));
        }

        Assert.True(clipped.Count == 0, $"[{language} {width}] clipped:{Environment.NewLine}{string.Join(Environment.NewLine, clipped)}");
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task TheGroupLabelsOfEveryTabSitOnOneBottomRow(string language)
    {
        using var f = await PdfRibbonLayoutTests.ShowWithDocumentAsync(language, 1920);
        var window = f.Window;
        foreach (var tab in window.Ribbon.Definition!.Tabs)
        {
            window.Ribbon.ActiveTab = tab;
            PdfRibbonLayoutTests.Layout(window);
            Assert.False(window.Ribbon.IsEffectivelyCompact, $"{tab.Name} is compact at 1920");
            var labels = window.Ribbon.Band.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("ribbon-group-label")).ToList();
            Assert.Equal(tab.Groups.Count, labels.Count);
            var tops = labels.Select(l => l.TranslatePoint(default, window)!.Value.Y).ToList();
            Assert.True(tops.Max() - tops.Min() < 0.5, $"[{language}] {tab.Name}: the group labels sit at different heights: {string.Join(", ", tops.Select(t => $"{t:F1}"))}");
        }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheHomeHelpGroupHoldsHelpDocumentInfoAndCheckForUpdates(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Raffica.Shell.Localization.ShellStrings.Culture = culture;
        PdfStrings.Culture = culture;
        using var f = WindowFixture.Create(width: 1920, height: 700);

        var home = f.Window.Ribbon.Definition!.Tabs.Single(t => t.Letter == PdfRibbon.HomeLetter);
        var help = home.Groups.Single(g => g.Digit == "7");
        Assert.Equal(["help.shortcuts", "doc.info", "app.update"], help.Commands.Select(c => c.Id));
        Assert.Equal(["71", "72", "73"], help.Commands.Select(c => c.FieldCode));
        Assert.Equal(PdfStrings.GetIn("CmdHelp", culture), help.Commands[0].Name);
        Assert.Equal(PdfStrings.GetIn("InfoTitle", culture), help.Commands[1].Name);
        Assert.Equal(PdfStrings.GetIn("CmdCheckForUpdates", culture), help.Commands[2].Name);
        Assert.All(help.Commands, c => Assert.NotNull(c.Run));
    }

    [AvaloniaFact]
    public async Task DocumentInfoNeedsADocumentAndCheckForUpdatesDoesNot()
    {
        using var f = WindowFixture.Create(width: 1920, height: 700);
        CommandDefinition Command(string id) =>
            f.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

        Assert.False(Command("doc.info").IsEnabled);
        Assert.True(Command("app.update").IsEnabled);

        await f.OpenAsync("text-rich.pdf");
        Assert.True(Command("doc.info").IsEnabled);
        Assert.True(Command("app.update").IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void CommandsWithTheSameShortNameCanBeToldApartByTheirDescription(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Raffica.Shell.Localization.ShellStrings.Culture = culture;
        PdfStrings.Culture = culture;
        using var f = WindowFixture.Create(width: 1920, height: 700);
        var commands = f.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();

        var duplicates = commands.GroupBy(c => c.Name).Where(g => g.Count() > 1).ToList();
        Assert.True(duplicates.Count > 0 || language == "en", "German has commands with the same short name (the review premise)");
        foreach (var group in duplicates)
        {
            Assert.All(group, c => Assert.False(string.IsNullOrWhiteSpace(c.Description), $"[{language}] '{group.Key}' ({c.Id}) has the name of another command and no description"));
            Assert.Equal(group.Count(), group.Select(c => c.Description).Distinct().Count());
        }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void ThePagesCommandsAndTheShortenedOnesHaveALongerDescriptionInTheTooltip(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Raffica.Shell.Localization.ShellStrings.Culture = culture;
        PdfStrings.Culture = culture;
        using var f = WindowFixture.Create(width: 1920, height: 700);
        var commands = f.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToDictionary(c => c.Id);

        var pages = f.Window.Ribbon.Definition!.Tabs.Single(t => t.Letter == PdfRibbon.PagesLetter).Groups.SelectMany(g => g.Commands).Select(c => c.Id);
        foreach (var id in pages.Concat(["annotate.mode", "forms.highlight", "redact.discard"]))
        {
            var description = commands[id].Description;
            Assert.False(string.IsNullOrWhiteSpace(description), $"[{language}] {id} has no description");
            Assert.True(description!.Length <= 64, $"[{language}] {id}: the description is too long for a search row: {description}");
            Assert.NotEqual(commands[id].Name, description);
        }

        // The wording that was shortened for the label is complete in the description.
        Assert.Equal(PdfStrings.GetIn("DescPagesUndo", culture), commands["pages.undo"].Description);
        Assert.NotEqual(commands["edit.undo"].Description, commands["pages.undo"].Description);
    }
}
