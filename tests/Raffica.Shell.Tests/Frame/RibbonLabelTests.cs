using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

/// <summary>
/// M2.7 Task 10 step 3: a tile label breaks only at a space or a hyphen. A word that is wider than the usual label width is never cut
/// in the middle ("Anmerkungsmod/us"); the tile takes the width of the word instead.
/// </summary>
public sealed class RibbonLabelTests
{
    private static (Window Window, RibbonView Ribbon) Show(string label)
    {
        var tab = new RibbonTabDefinition("Test", "T",
        [
            new RibbonGroupDefinition("Group", "1", [new CommandDefinition("test.one", label, "Raffica.Icon.Check", "11", Size: CommandSize.Large)]),
        ]);
        var ribbon = new RibbonView { Definition = new RibbonDefinition([tab]) };
        var frame = new AppFrame { TabRow = ribbon.TabRow, Band = ribbon.Band };
        var window = ControlHarness.Show(frame, ThemeMode.Light);
        window.Width = 1400;
        ControlHarness.Settle(window);
        return (window, ribbon);
    }

    private static TextBlock LabelOf(RibbonView ribbon) =>
        ribbon.Band.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ribbon-label"));

    private static double WidthOf(TextBlock like, string text)
    {
        var probe = new TextBlock
        {
            Text = text,
            FontFamily = like.FontFamily,
            FontSize = like.FontSize,
            FontWeight = like.FontWeight,
            TextWrapping = TextWrapping.NoWrap,
        };
        probe.Measure(Size.Infinity);
        return probe.DesiredSize.Width;
    }

    [AvaloniaFact]
    public void AWordWiderThanTheLabelWidthStaysWhole()
    {
        const string word = "Rechtschreibprüfungseinstellungen";
        var (_, ribbon) = Show(word + " Test");
        var label = LabelOf(ribbon);

        var wordWidth = WidthOf(label, word);
        Assert.True(wordWidth > 96, $"the sample word is only {wordWidth} wide; pick a longer one");
        Assert.True(label.Bounds.Width >= wordWidth - 0.5, $"the label is {label.Bounds.Width} wide, the word needs {wordWidth}: it was cut in the middle");
        Assert.Equal(2, (int)Math.Round(label.Bounds.Height / label.LineHeight));
    }

    [AvaloniaFact]
    public void ALongLabelOfShortWordsBreaksAtTheSpaces()
    {
        var (_, ribbon) = Show("Änderungen auf alle Seiten anwenden");
        var label = LabelOf(ribbon);

        Assert.True(label.Bounds.Width <= 96.5, $"the label is {label.Bounds.Width} wide");
        Assert.True(label.Bounds.Height <= (2 * label.LineHeight) + 0.5, "the label needs more than two lines");
    }

    [AvaloniaFact]
    public void ACompoundWithHyphensBreaksAtTheHyphens()
    {
        const string text = "Vor-Rück-Seiten-Wechsel-Ansicht";
        var (_, ribbon) = Show(text);
        var label = LabelOf(ribbon);

        Assert.True(WidthOf(label, text) > 96, "the sample is not wider than the label width");
        Assert.True(label.Bounds.Width <= 96.5, $"the label is {label.Bounds.Width} wide");
        Assert.Equal(2, (int)Math.Round(label.Bounds.Height / label.LineHeight));
    }

    [AvaloniaFact]
    public void AShortLabelIsNotWidened()
    {
        var (_, ribbon) = Show("Paste");
        var label = LabelOf(ribbon);

        Assert.True(label.Bounds.Width < 48, $"the label is {label.Bounds.Width} wide");
        Assert.Equal(1, (int)Math.Round(label.Bounds.Height / label.LineHeight));
    }

    [AvaloniaFact]
    public void GroupLabelsSitOnOneRowWhateverTheGroupHolds()
    {
        // A group of one small command is 26 px high, a stack of two 54 px, a tile 54 px: the labels still share one bottom row.
        var tab = new RibbonTabDefinition("Test", "T",
        [
            new RibbonGroupDefinition("One", "1", [new CommandDefinition("g.one", "Single", "Raffica.Icon.Check", "11")]),
            new RibbonGroupDefinition("Tile", "2", [new CommandDefinition("g.tile", "Tile", "Raffica.Icon.Check", "21", Size: CommandSize.Large)]),
            new RibbonGroupDefinition("Two", "3", [new CommandDefinition("g.a", "A one", "Raffica.Icon.Check", "31"), new CommandDefinition("g.b", "A two", "Raffica.Icon.Check", "32")]),
            new RibbonGroupDefinition("Icons", "4", [new CommandDefinition("g.i", "Icon", "Raffica.Icon.Check", "41", IconOnly: true)]),
        ]);
        var ribbon = new RibbonView { Definition = new RibbonDefinition([tab]) };
        var window = ControlHarness.Show(new AppFrame { TabRow = ribbon.TabRow, Band = ribbon.Band }, ThemeMode.Light);
        window.Width = 1400;
        ControlHarness.Settle(window);

        var labels = ribbon.Band.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("ribbon-group-label")).ToList();
        Assert.Equal(4, labels.Count);
        var tops = labels.Select(l => l.TranslatePoint(default, window)!.Value.Y).ToList();
        Assert.True(tops.Max() - tops.Min() < 0.5, $"the labels sit at different heights: {string.Join(", ", tops.Select(t => $"{t:F1}"))}");
    }
}
