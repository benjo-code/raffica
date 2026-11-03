using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class TextBoxContextMenuTests
{
    private static (Window Window, TextBox Box, MenuFlyout Flyout) Open(string culture, string text = "hello world", bool select = false, bool readOnly = false)
    {
        ShellStrings.Culture = CultureInfo.GetCultureInfo(culture);
        var box = new TextBox { Text = text, IsReadOnly = readOnly };
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { box } }, ThemeMode.Light);
        box.Focus();
        if (select)
        {
            box.SelectAll();
        }

        var flyout = Assert.IsAssignableFrom<MenuFlyout>(box.ContextFlyout);
        flyout.ShowAt(box);
        ControlHarness.Settle(window);
        return (window, box, flyout);
    }

    private static List<MenuItem> Items(MenuFlyout flyout) => flyout.Items.OfType<MenuItem>().ToList();

    private static T WithCulture<T>(Func<T> body)
    {
        var previous = ShellStrings.Culture;
        try
        {
            return body();
        }
        finally
        {
            ShellStrings.Culture = previous;
        }
    }

    [AvaloniaFact]
    public void GermanMenuReadsAusschneidenKopierenEinfuegen() =>
        Assert.Equal(
            ["Ausschneiden", "Kopieren", "Einfügen", "Alles auswählen", "Rückgängig", "Wiederholen"],
            WithCulture(() => Items(Open("de").Flyout).Select(i => (string)i.Header!).ToList()));

    [AvaloniaFact]
    public void EnglishMenuReadsCutCopyPaste() =>
        Assert.Equal(
            ["Cut", "Copy", "Paste", "Select all", "Undo", "Redo"],
            WithCulture(() => Items(Open("en").Flyout).Select(i => (string)i.Header!).ToList()));

    // Fails if the item shows the raw gesture ("Ctrl+X") or no localized text.
    [AvaloniaFact]
    public void ShortcutsAreLocalized()
    {
        var (de, en) = WithCulture(() =>
        {
            var german = Items(Open("de").Flyout).ToDictionary(i => (string)i.Header!, i => ControlHarness.Part<TextBlock>(i, "PART_InputGestureText").Text);
            var english = Items(Open("en").Flyout).ToDictionary(i => (string)i.Header!, i => ControlHarness.Part<TextBlock>(i, "PART_InputGestureText").Text);
            return (german, english);
        });

        if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("⌘X", de["Ausschneiden"]);
            Assert.Equal("⌘X", en["Cut"]);
            return;
        }

        Assert.Equal("Strg+X", de["Ausschneiden"]);
        Assert.Equal("Strg+C", de["Kopieren"]);
        Assert.Equal("Strg+V", de["Einfügen"]);
        Assert.Equal("Strg+A", de["Alles auswählen"]);
        Assert.Equal("Strg+Z", de["Rückgängig"]);
        Assert.Equal("Strg+Y", de["Wiederholen"]);
        Assert.Equal("Ctrl+X", en["Cut"]);
    }

    // Fails if the items were enabled unconditionally.
    [AvaloniaFact]
    public void DisabledWhenThereIsNothingToCopy()
    {
        var items = WithCulture(() => Items(Open("en").Flyout).ToDictionary(i => (string)i.Header!));
        Assert.False(items["Cut"].IsEnabled);
        Assert.False(items["Copy"].IsEnabled);
        Assert.True(items["Select all"].IsEnabled);
        Assert.False(items["Undo"].IsEnabled);
        Assert.False(items["Redo"].IsEnabled);
    }

    [AvaloniaFact]
    public void CutAndCopyAreEnabledWithASelectionAndCutIsNotForReadOnlyBoxes()
    {
        var (selected, readOnly) = WithCulture(() => (
            Items(Open("en", select: true).Flyout).ToDictionary(i => (string)i.Header!),
            Items(Open("en", select: true, readOnly: true).Flyout).ToDictionary(i => (string)i.Header!)));

        Assert.True(selected["Cut"].IsEnabled);
        Assert.True(selected["Copy"].IsEnabled);
        Assert.False(readOnly["Cut"].IsEnabled);
        Assert.False(readOnly["Paste"].IsEnabled);
        Assert.True(readOnly["Copy"].IsEnabled);
    }

    // Fails if the flyout appends instead of rebuilding its items when the same box opens it again.
    [AvaloniaFact]
    public void OpeningTheSameBoxTwiceDoesNotDoubleTheItems()
    {
        var (first, second) = WithCulture(() =>
        {
            var (window, _, flyout) = Open("en");
            var firstCount = flyout.Items.Count;
            flyout.Hide();
            ControlHarness.Settle(window);
            flyout.ShowAt(window.Content is StackPanel { Children: [TextBox box] } ? box : throw new InvalidOperationException());
            ControlHarness.Settle(window);
            return (firstCount, flyout.Items.Count);
        });

        Assert.Equal(7, first);
        Assert.Equal(first, second);
    }

    [AvaloniaFact]
    public void SelectAllSelectsTheText()
    {
        var box = WithCulture(() =>
        {
            var (_, textBox, flyout) = Open("en");
            Items(flyout).First(i => (string)i.Header! == "Select all").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            return textBox;
        });

        Assert.Equal(0, Math.Min(box.SelectionStart, box.SelectionEnd));
        Assert.Equal(11, Math.Max(box.SelectionStart, box.SelectionEnd));
    }

    [AvaloniaFact]
    public void UndoIsEnabledAfterTyping()
    {
        var undo = WithCulture(() =>
        {
            ShellStrings.Culture = CultureInfo.GetCultureInfo("en");
            var box = new TextBox();
            var window = ControlHarness.Show(new StackPanel { Children = { box } }, ThemeMode.Light);
            box.Focus();
            window.KeyTextInput("x");
            ControlHarness.Settle(window);
            var flyout = Assert.IsAssignableFrom<MenuFlyout>(box.ContextFlyout);
            flyout.ShowAt(box);
            ControlHarness.Settle(window);
            return Items(flyout).First(i => (string)i.Header! == "Undo");
        });

        Assert.True(undo.IsEnabled);
    }
}
