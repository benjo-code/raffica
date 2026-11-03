using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Raffica.Gallery;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class TextBoxTests
{
    private static (Window Window, TextBox Box) Host(ThemeMode mode, string text = "", bool enabled = true)
    {
        var box = new TextBox { Text = text, PlaceholderText = "Search", IsEnabled = enabled, Width = 200 };
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { box } }, mode);
        return (window, box);
    }

    private static Border Edge(TextBox box) => ControlHarness.Part<Border>(box, "PART_BorderElement");

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void RestIsThePageFillWithTheControlEdgeAt26PxAndRadius3(ThemeMode mode)
    {
        var (_, box) = Host(mode);
        var edge = Edge(box);

        Assert.Equal(26, box.Bounds.Height);
        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", edge.Background, "fill");
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", edge.BorderBrush, "edge");
        Assert.Equal(new Thickness(1), edge.BorderThickness);
        Assert.Equal(new CornerRadius(3), edge.CornerRadius);
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", box.Foreground, "text");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void PlaceholderIsSecondaryText(ThemeMode mode)
    {
        var (_, box) = Host(mode);
        var placeholder = ControlHarness.Part<TextBlock>(box, "PART_Placeholder");

        Assert.True(placeholder.IsVisible);
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", placeholder.Foreground, "placeholder");
        Assert.Equal(1, placeholder.Opacity);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void HoverEdgeIsSecondaryAndTypingEdgeIsPrimary(ThemeMode mode)
    {
        var (window, box) = Host(mode);
        var edge = Edge(box);

        StateMatrix.Force(box, ":pointerover");
        ControlHarness.Settle(window);
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", edge.BorderBrush, "hover edge");
        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", edge.Background, "hover fill");

        box.Focus();
        ControlHarness.Settle(window);
        Assert.True(box.IsFocused);
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", edge.BorderBrush, "typing edge (hover and focus)");
        Assert.Equal(new Thickness(1), edge.BorderThickness);
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", box.CaretBrush, "caret");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void KeyboardFocusShowsTheInkRingOutsideTheEdge(ThemeMode mode)
    {
        var (window, box) = Host(mode);

        ControlHarness.KeyboardFocus(window, box);

        var layer = AdornerLayer.GetAdornerLayer(box);
        Assert.NotNull(layer);
        var ring = Assert.Single(layer.Children.OfType<Border>());
        Assert.Equal(new Thickness(2), ring.BorderThickness);
        Assert.Equal(new Thickness(-4), ring.Margin);
        ControlHarness.AssertToken("Raffica.Brush.Ink", ring.BorderBrush, "ring");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DisabledUsesTheBandAndDisabledText(ThemeMode mode)
    {
        var (_, box) = Host(mode, "text", enabled: false);
        var edge = Edge(box);

        ControlHarness.AssertToken("Raffica.Brush.Surface.Band", edge.Background, "fill");
        ControlHarness.AssertToken("Raffica.Brush.Text.Disabled", edge.BorderBrush, "edge");
        ControlHarness.AssertToken("Raffica.Brush.Text.Disabled", box.Foreground, "text");
        Assert.Equal(26, box.Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DisabledPlaceholderIsDisabledText(ThemeMode mode)
    {
        var (_, box) = Host(mode, enabled: false);

        ControlHarness.AssertToken("Raffica.Brush.Text.Disabled", ControlHarness.Part<TextBlock>(box, "PART_Placeholder").Foreground, "placeholder");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TextSelectionUsesTheSelectionTint(ThemeMode mode)
    {
        var (window, box) = Host(mode, "Hello world");
        box.Focus();
        box.SelectAll();
        ControlHarness.Settle(window);

        var presenter = ControlHarness.Part<TextPresenter>(box, "PART_TextPresenter");
        ControlHarness.AssertToken("Raffica.Brush.Selection.Tint", box.SelectionBrush, "box selection");
        ControlHarness.AssertToken("Raffica.Brush.Selection.Tint", presenter.SelectionBrush, "presenter selection");
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", presenter.SelectionForegroundBrush, "selected text");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void MultiLineBoxKeepsTheStyleAtItsOwnHeight(ThemeMode mode)
    {
        var box = new TextBox { AcceptsReturn = true, Height = 80, Width = 200, Text = "a\nb\nc" };
        ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { box } }, mode);

        Assert.Equal(80, box.Bounds.Height);
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", Edge(box).BorderBrush, "edge");
    }
}
