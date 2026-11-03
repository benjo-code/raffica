using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
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

    [AvaloniaFact]
    public void Is28HighWithPlaceholder()
    {
        var (_, search, _) = Show();
        var input = ControlHarness.Part<TextBox>(search, "PART_Input");

        Assert.Equal(28, search.Bounds.Height, 0.01);
        Assert.Equal(ShellStrings.Search, input.PlaceholderText);
        Assert.NotEmpty(ControlHarness.Descendants(search).OfType<StrokeIcon>()); // the magnifier
        // No keycap: the shortcut is in the tooltip only.
        Assert.DoesNotContain(ControlHarness.Descendants(search).OfType<TextBlock>(), t => t.Text == ShellStrings.SearchShortcut);
        Assert.Equal(new Avalonia.CornerRadius(3), input.CornerRadius);
    }

    [AvaloniaFact]
    public void TypingShowsMatchingCommandsAndEnterChoosesTheFirst()
    {
        var (window, search, chosen) = Show();

        Type(window, search, "bo");

        Assert.True(PopupOf(search).IsOpen);
        var rows = Rows(search);
        Assert.Equal(["Bold", "Bookmark"], rows.Select(r => r.Command!.Name));
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
            Assert.Equal("Search (Ctrl+K)", SearchField.CreateTooltip().Title);
            ShellStrings.Culture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Suchen (Strg+K)", SearchField.CreateTooltip().Title);
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
    public void ClearingTheTextClosesTheResults()
    {
        var (window, search, _) = Show();
        Type(window, search, "bo");
        Assert.True(PopupOf(search).IsOpen);

        Press(window, PhysicalKey.Backspace);
        Press(window, PhysicalKey.Backspace);

        Assert.False(PopupOf(search).IsOpen);
    }
}
