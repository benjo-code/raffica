using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

/// <summary>The disclosure row (a <see cref="ToggleButton"/> with the class disclosure) and the link button (a <see cref="Button"/> with the class link).</summary>
public sealed class DisclosureAndLinkTests
{
    private static (Window Window, ToggleButton Toggle) HostDisclosure(ThemeMode mode, bool disclosure, string text = "Title")
    {
        var toggle = new ToggleButton
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            IsChecked = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        if (disclosure)
        {
            toggle.Classes.Add("disclosure");
        }

        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Width = 200, Children = { toggle } }, mode);
        return (window, toggle);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ACheckedDisclosureRowHasNoFillAndKeepsTheTextColor(ThemeMode mode)
    {
        var (window, toggle) = HostDisclosure(mode, disclosure: true);
        var presenter = ControlHarness.Presenter(toggle);

        Assert.Equal(Colors.Transparent, ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(Colors.Transparent, ControlHarness.ColorOf(presenter.BorderBrush));
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", presenter.Foreground, "foreground");

        StateMatrix.Force(toggle, ":pointerover");
        ControlHarness.Settle(window);
        ControlHarness.AssertToken("Raffica.Brush.State.Hover", presenter.Background, "hover");
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", presenter.Foreground, "hover foreground");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void APlainCheckedToggleStillTakesTheInkFill(ThemeMode mode)
    {
        var (_, toggle) = HostDisclosure(mode, disclosure: false);
        ControlHarness.AssertToken("Raffica.Brush.Ink", ControlHarness.Presenter(toggle).Background, "checked fill");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ADisclosureRowGrowsWithItsWrappingTitle(ThemeMode mode)
    {
        var (_, one) = HostDisclosure(mode, disclosure: true, "Short");
        Assert.Equal(26, one.Bounds.Height);
        var (_, many) = HostDisclosure(mode, disclosure: true, "A rather long title of a component that needs several lines in a row of two hundred pixels");
        Assert.True(many.Bounds.Height > 26 * 2, many.Bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(200, many.Bounds.Width);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ALinkButtonIsInkTextWithAnUnderlineAndNoFill(ThemeMode mode)
    {
        var text = new TextBlock { Text = "https://example.org/project" };
        text.Classes.Add("link");
        var link = new Button { Content = text };
        link.Classes.Add("link");
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { link } }, mode);
        var presenter = ControlHarness.Presenter(link);

        Assert.Equal(Colors.Transparent, ControlHarness.ColorOf(presenter.Background));
        ControlHarness.AssertToken("Raffica.Brush.Ink", presenter.Foreground, "foreground");
        Assert.Equal(TextDecorationLocation.Underline, Assert.Single(text.TextDecorations!).Location);
        ControlHarness.AssertToken("Raffica.Brush.Ink", text.Foreground, "text color");

        StateMatrix.Force(link, ":pointerover");
        ControlHarness.Settle(window);
        ControlHarness.AssertToken("Raffica.Brush.State.Hover", presenter.Background, "hover");
    }
}
