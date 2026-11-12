using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class SearchFieldTests
{
    private static (Window Window, SearchField Search, List<CommandDefinition> Chosen) Show()
    {
        var search = new SearchField { Source = SampleRibbon.Create(), Width = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var chosen = new List<CommandDefinition>();
        search.CommandChosen += chosen.Add;
        var window = ControlHarness.Show(search, ThemeMode.Light);
        return (window, search, chosen);
    }

    private static void Type(Window window, SearchField search, string text)
    {
        search.FocusAndSelect();
        window.KeyTextInput(text);
        ControlHarness.Settle(window);
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        ControlHarness.Settle(window);
    }

    private static Popup PopupOf(SearchField search) => ControlHarness.Part<Popup>(search, "PART_Popup");

    private static List<SearchResultRow> Rows(SearchField search) => ControlHarness.Descendants(search).OfType<SearchResultRow>().ToList();

    private static (Window Window, SearchField Search, List<string> Queries) ShowWithEntries(Func<string, IReadOnlyList<SearchEntry>> entries)
    {
        var (window, search, _) = Show();
        var queries = new List<string>();
        search.ExtraResults = query =>
        {
            queries.Add(query);
            return entries(query);
        };
        return (window, search, queries);
    }

    private static List<TextBlock> Headings(SearchField search) =>
        ControlHarness.Descendants(search).OfType<TextBlock>().Where(t => t.Classes.Contains("search-group")).ToList();

    [AvaloniaFact]
    public void ExtraResultsAppearAfterCommandsUnderTheirGroup()
    {
        var (window, search, _) = ShowWithEntries(_ =>
        [
            new SearchEntry("Pages", "Page 3", () => { }, "Introduction"),
            new SearchEntry("Pages", "Page 7", () => { }),
            new SearchEntry("Bookmarks", "Bookmark one", () => { }),
        ]);

        Type(window, search, "bo");

        var rows = Rows(search);
        Assert.Equal(["Bold", "Bookmark", "Page 3", "Page 7", "Bookmark one"], rows.Select(r => r.Label));
        var headings = Headings(search);
        Assert.Equal(["Pages", "Bookmarks"], headings.Select(h => h.Text));
        var order = ControlHarness.Descendants(search).Where(c => c is SearchResultRow || c.Classes.Contains("search-group")).ToList();
        Assert.Same(headings[0], order[2]);
        Assert.Same(rows[2], order[3]);
        Assert.Contains(ControlHarness.Descendants(rows[2]).OfType<TextBlock>(), t => t.Text == "Introduction");
        Assert.True(rows[0].IsActive);
    }

    [AvaloniaFact]
    public void ExtraResultsAreCappedAtEightPerGroup()
    {
        var (window, search, _) = ShowWithEntries(_ => Enumerable.Range(1, 12).Select(i => new SearchEntry("Pages", $"Page {i}", () => { })).ToList());

        Type(window, search, "bo");

        Assert.Equal(8, Rows(search).Count(r => r.Label.StartsWith("Page ", StringComparison.Ordinal)));
        Assert.DoesNotContain(Rows(search), r => r.Label == "Page 9");
    }

    [AvaloniaFact]
    public void EnterRunsTheChosenEntry()
    {
        var ran = new List<string>();
        var (window, search, _) = ShowWithEntries(_ => [new SearchEntry("Pages", "Page 3", () => ran.Add("p3")), new SearchEntry("Pages", "Page 7", () => ran.Add("p7"))]);
        Type(window, search, "bo");

        Press(window, PhysicalKey.ArrowDown); // Bookmark
        Press(window, PhysicalKey.ArrowDown); // Page 3
        Press(window, PhysicalKey.ArrowDown); // Page 7
        Assert.Equal("Page 7", Rows(search).Single(r => r.IsActive).Label);
        Press(window, PhysicalKey.Enter);

        Assert.Equal(["p7"], ran);
        Assert.False(PopupOf(search).IsOpen);
    }

    [AvaloniaFact]
    public void ClickRunsTheEntry()
    {
        var ran = new List<string>();
        var (window, search, _) = ShowWithEntries(_ => [new SearchEntry("Pages", "Page 3", () => ran.Add("p3"))]);
        Type(window, search, "bo");

        DocumentTabsTests.Click(window, Rows(search).Single(r => r.Label == "Page 3"));

        Assert.Equal(["p3"], ran);
    }

    [AvaloniaFact]
    public void NoProviderChangesNothing()
    {
        var (window, search, chosen) = Show();
        Assert.Null(search.ExtraResults);

        Type(window, search, "bo");

        Assert.Equal(["Bold", "Bookmark"], Rows(search).Select(r => r.Label));
        Assert.Empty(Headings(search));
        Assert.Empty(chosen);
    }

    [AvaloniaFact]
    public void ProviderIsNotCalledForAnEmptyQuery()
    {
        var (window, search, queries) = ShowWithEntries(_ => []);

        Type(window, search, "bo");
        Assert.Contains("bo", queries);
        queries.Clear();
        ControlHarness.Part<TextBox>(search, "PART_Input").Text = "  ";
        ControlHarness.Settle(window);
        ControlHarness.Part<TextBox>(search, "PART_Input").Text = string.Empty;
        ControlHarness.Settle(window);

        Assert.Empty(queries);
    }

    [AvaloniaFact]
    public void Is28HighWithPlaceholder()
    {
        var (_, search, _) = Show();
        var input = ControlHarness.Part<TextBox>(search, "PART_Input");

        Assert.Equal(28, search.Bounds.Height, 0.01);
        Assert.Equal(ShellStrings.SearchCommands, input.PlaceholderText);
        Assert.Equal(ShellStrings.SearchCommands, Avalonia.Automation.AutomationProperties.GetName(input));
        Assert.NotEmpty(ControlHarness.Descendants(search).OfType<StrokeIcon>()); // the magnifier
        // No keycap: the shortcut is in the tooltip only.
        Assert.DoesNotContain(ControlHarness.Descendants(search).OfType<TextBlock>(), t => t.Text == KeyNames.Format("Ctrl+K", ShellStrings.Culture));
        Assert.Equal(new Avalonia.CornerRadius(3), input.CornerRadius);
    }

    [AvaloniaFact]
    public void TypingShowsMatchingCommandsAndEnterChoosesTheFirst()
    {
        var (window, search, chosen) = Show();

        Type(window, search, "bo");

        Assert.True(PopupOf(search).IsOpen);
        var rows = Rows(search);
        Assert.Equal(["Bold", "Bookmark"], rows.Select(r => r.Label));
        Assert.All(rows, r => Assert.Equal(32, r.Bounds.Height, 0.01));
        Assert.Equal("Ctrl+B", rows[0].ShortcutText);
        Assert.True(rows[0].IsActive);

        Press(window, PhysicalKey.Enter);

        Assert.Equal(["bold"], chosen.Select(c => c.Id));
        Assert.False(PopupOf(search).IsOpen);
    }

    [AvaloniaFact]
    public void DownMovesTheChoiceAndRowClickChooses()
    {
        var (window, search, chosen) = Show();
        Type(window, search, "bo");

        Press(window, PhysicalKey.ArrowDown);
        Assert.False(Rows(search)[0].IsActive);
        Assert.True(Rows(search)[1].IsActive);
        Press(window, PhysicalKey.ArrowDown); // stays on the last row
        Assert.True(Rows(search)[1].IsActive);
        Press(window, PhysicalKey.ArrowUp);
        Assert.True(Rows(search)[0].IsActive);
        Press(window, PhysicalKey.ArrowDown);
        Press(window, PhysicalKey.Enter);
        Assert.Equal(["bookmark"], chosen.Select(c => c.Id));

        chosen.Clear();
        Type(window, search, "bo");
        DocumentTabsTests.Click(window, Rows(search)[0]);
        Assert.Equal(["bold"], chosen.Select(c => c.Id));
    }

    [AvaloniaFact]
    public void EscClosesTheResultsAndKeepsTheText()
    {
        var (window, search, chosen) = Show();
        Type(window, search, "bo");
        Assert.True(PopupOf(search).IsOpen);

        Press(window, PhysicalKey.Escape);

        Assert.False(PopupOf(search).IsOpen);
        Assert.Equal("bo", ControlHarness.Part<TextBox>(search, "PART_Input").Text);
        Assert.Empty(chosen);
        // Typing again opens the results again.
        window.KeyTextInput("o");
        ControlHarness.Settle(window);
        Assert.True(PopupOf(search).IsOpen);
    }

    [AvaloniaFact]
    public void CtrlKFocusesTheField()
    {
        var button = new Button { Content = "Other" };
        var search = new SearchField { Source = SampleRibbon.Create(), Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var window = ControlHarness.Show(new StackPanel { Children = { button, search } }, ThemeMode.Light);
        FrameKeys.Attach(window, search);
        var input = ControlHarness.Part<TextBox>(search, "PART_Input");
        input.Text = "abc";
        button.Focus();
        ControlHarness.Settle(window);
        Assert.False(input.IsFocused);

        Press(window, PhysicalKey.K, RawInputModifiers.Control);

        Assert.True(input.IsFocused);
        Assert.Equal(0, input.SelectionStart);
        Assert.Equal(3, input.SelectionEnd);
    }

    [AvaloniaFact]
    public void TooltipNamesTheShortcut()
    {
        var (_, search, _) = Show();
        var original = ShellStrings.Culture;
        try
        {
            ShellStrings.Culture = CultureInfo.GetCultureInfo("en");
            Assert.Equal(OperatingSystem.IsMacOS() ? "Search (⌘K)" : "Search (Ctrl+K)", SearchField.CreateTooltip().Title);
            ShellStrings.Culture = CultureInfo.GetCultureInfo("de");
            Assert.Equal(OperatingSystem.IsMacOS() ? "Suchen (⌘K)" : "Suchen (Strg+K)", SearchField.CreateTooltip().Title);
        }
        finally
        {
            ShellStrings.Culture = original;
        }

        Assert.IsType<TooltipContent>(ToolTip.GetTip(search));
    }

    [AvaloniaFact]
    public void NoResultsShowsTheNoResultsLine()
    {
        var (window, search, chosen) = Show();

        Type(window, search, "zzzz");

        Assert.True(PopupOf(search).IsOpen);
        Assert.Empty(Rows(search));
        var line = ControlHarness.Descendants(search).OfType<TextBlock>().Single(t => t.Text == ShellStrings.SearchNoResults);
        Assert.True(line.IsVisible);

        Press(window, PhysicalKey.Enter);
        Assert.Empty(chosen);
    }

    [AvaloniaFact]
    public void ActiveRowIsScrolledIntoViewWhenNavigatingWithTheArrowKeys()
    {
        var commands = Enumerable.Range(1, 15)
            .Select(i => new CommandDefinition($"item{i}", $"Item {i:00}", "Raffica.Icon.Plus", $"I{i:00}"))
            .ToList();
        var ribbon = new RibbonDefinition([new RibbonTabDefinition("Home", "H", [new RibbonGroupDefinition("All", "1", commands)])]);
        var search = new SearchField { Source = ribbon, Width = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = ControlHarness.Show(search, ThemeMode.Light);
        Type(window, search, "item");
        var scroll = ControlHarness.Part<ScrollViewer>(search, "PART_ResultsScroll");
        Assert.Equal(15, Rows(search).Count);

        void AssertActiveVisible()
        {
            var row = Rows(search).Single(r => r.IsActive);
            var top = row.TranslatePoint(default, scroll)!.Value.Y;
            Assert.True(top >= -0.5 && top + row.Bounds.Height <= scroll.Viewport.Height + 0.5,
                $"active row {row.Name} at {top} is outside the viewport of {scroll.Viewport.Height}");
        }

        for (var i = 0; i < 12; i++)
        {
            Press(window, PhysicalKey.ArrowDown);
            AssertActiveVisible();
        }

        Assert.True(scroll.Offset.Y > 0);
        for (var i = 0; i < 12; i++)
        {
            Press(window, PhysicalKey.ArrowUp);
            AssertActiveVisible();
        }

        Assert.Equal(0, scroll.Offset.Y, 0.5);
    }

    [AvaloniaFact]
    public void ClearingTheTextClosesTheResults()
    {
        var (window, search, _) = Show();
        Type(window, search, "bo");
        Assert.True(PopupOf(search).IsOpen);

        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);

        Assert.False(PopupOf(search).IsOpen);
    }

    [AvaloniaFact]
    public void AnExtraResultWithTheNameOfACommandIsNotListedTwice()
    {
        // The command row is the one to choose; an extra entry of the same name would only repeat it (and its heading with it).
        var (window, search, _) = ShowWithEntries(_ =>
        [
            new SearchEntry("App", "bookmark", () => { }),
            new SearchEntry("Pages", "Page 3", () => { }),
        ]);

        Type(window, search, "bo");

        Assert.Equal(1, Rows(search).Count(r => string.Equals(r.Label, "Bookmark", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(["Pages"], Headings(search).Select(h => h.Text));
        Assert.Contains(Rows(search), r => r.Label == "Page 3");
    }

    [AvaloniaFact]
    public void ACommandWithoutAShortcutShowsItsDescriptionOnTheRight()
    {
        var tab = new RibbonTabDefinition("T", "T",
        [
            new RibbonGroupDefinition("G", "1",
            [
                new CommandDefinition("a.undo", "Undo", "Raffica.Icon.Undo", "11", Shortcut: "Ctrl+Z", Description: "Undo the last change"),
                new CommandDefinition("b.undo", "Undo", "Raffica.Icon.Undo", "12", Description: "Undo the last page change"),
            ]),
        ]);
        var search = new SearchField { Source = new RibbonDefinition([tab]), Width = 300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = ControlHarness.Show(search, ThemeMode.Light);
        Type(window, search, "undo");

        var rows = Rows(search);
        Assert.Equal(2, rows.Count);
        var texts = rows.Select(r => ControlHarness.Descendants(r).OfType<TextBlock>().Select(t => t.Text).ToList()).ToList();
        Assert.Contains(OperatingSystem.IsMacOS() ? "⌃Z" : "Ctrl+Z", texts[0]);
        Assert.DoesNotContain("Undo the last change", texts[0]);
        Assert.Contains("Undo the last page change", texts[1]);
    }
}
