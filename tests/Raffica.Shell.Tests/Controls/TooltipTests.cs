using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class TooltipTests
{
    private static TooltipContent Content(string? shortcut = "Ctrl+Shift+E", string? description = "Marks insertions and deletions until they are accepted.") =>
        new() { Title = "Track changes", Shortcut = shortcut, Description = description };

    private static (Window Window, Button Anchor, ToolTip Tip) Open(ThemeMode mode, TooltipContent content)
    {
        var tip = new ToolTip { Content = content };
        var anchor = new Button { Content = "Track" };
        ToolTip.SetTip(anchor, tip);
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(60), Children = { anchor } }, mode);
        ToolTip.SetIsOpen(anchor, true);
        ControlHarness.Settle(window);
        return (window, anchor, tip);
    }

    private static TextBlock Text(Control root, string name) => ControlHarness.Part<TextBlock>(root, name);

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheContentShowsTitleShortcutAndDescription(ThemeMode mode)
    {
        var content = Content();
        Open(mode, content);

        var title = Text(content, "PART_Title");
        var shortcut = Text(content, "PART_Shortcut");
        var description = Text(content, "PART_Description");

        Assert.Equal("Track changes", title.Text);
        Assert.Equal(13, title.FontSize);
        Assert.Equal(FontWeight.SemiBold, title.FontWeight);
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", title.Foreground, "title");

        Assert.Equal(OperatingSystem.IsMacOS() ? "⌃⇧E" : "Ctrl+Shift+E", shortcut.Text);
        Assert.True(shortcut.IsVisible);
        Assert.Equal(FontWeight.Normal, shortcut.FontWeight);
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", shortcut.Foreground, "shortcut");
        Assert.Equal(HorizontalAlignment.Right, shortcut.HorizontalAlignment);

        Assert.Equal("Marks insertions and deletions until they are accepted.", description.Text);
        Assert.Equal(12, description.FontSize);
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", description.Foreground, "description");
    }

    // Fails if the tooltip prints the raw shortcut string.
    [AvaloniaFact]
    public void ShortcutIsLocalized()
    {
        var previous = Raffica.Shell.Localization.ShellStrings.Culture;
        try
        {
            Raffica.Shell.Localization.ShellStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
            var content = Content();
            Open(ThemeMode.Light, content);
            Assert.Equal(OperatingSystem.IsMacOS() ? "⌃⇧E" : "Strg+Umschalt+E", Text(content, "PART_Shortcut").Text);
        }
        finally
        {
            Raffica.Shell.Localization.ShellStrings.Culture = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void EmptyShortcutAndDescriptionAreHidden(ThemeMode mode)
    {
        var content = Content(shortcut: null, description: "");
        Open(mode, content);

        Assert.False(Text(content, "PART_Shortcut").IsVisible);
        Assert.False(Text(content, "PART_Description").IsVisible);
        Assert.True(Text(content, "PART_Title").IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheTooltipIsAtMost240PxWide(ThemeMode mode)
    {
        var content = Content(description: string.Join(' ', Enumerable.Repeat("A long description that has to wrap", 6)));
        var (_, _, tip) = Open(mode, content);

        Assert.Equal(240, content.MaxWidth);
        Assert.Equal(240, tip.MaxWidth);
        Assert.True(tip.Bounds.Width <= 240, $"tooltip is {tip.Bounds.Width} px wide");
        Assert.True(content.Bounds.Width <= 240);
        Assert.True(Text(content, "PART_Description").Bounds.Height > 16, "the description wrapped");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheTooltipHasThePageFillA1PxEdgeRadius3AndNoShadowOrArrow(ThemeMode mode)
    {
        var content = Content();
        var (_, _, tip) = Open(mode, content);
        var root = ControlHarness.Part<Border>(tip, "PART_LayoutRoot");

        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", root.Background, "fill");
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", root.BorderBrush, "edge");
        Assert.Equal(new Thickness(1), root.BorderThickness);
        Assert.Equal(new CornerRadius(3), root.CornerRadius);
        Assert.Equal(new Thickness(9, 7, 9, 8), tip.Padding);
        Assert.Equal(default, root.BoxShadow);
        Assert.False(ControlHarness.HasShadow(tip));
        Assert.Equal(1, tip.Opacity);
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", tip.Foreground, "tooltip text");
    }
}
