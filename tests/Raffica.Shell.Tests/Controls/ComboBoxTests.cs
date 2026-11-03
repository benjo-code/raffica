using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Gallery;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class ComboBoxTests
{
    private static (Window Window, ComboBox Combo) Host(ThemeMode mode, bool enabled = true, bool selectBox = false)
    {
        ComboBox combo = selectBox ? new SelectBox() : new ComboBox();
        combo.ItemsSource = new[] { "Alpha", "Beta", "Gamma" };
        combo.SelectedIndex = 1;
        combo.Width = 200;
        combo.IsEnabled = enabled;
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { combo } }, mode);
        return (window, combo);
    }

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        ControlHarness.Settle(window);
    }

    private static Border BoxEdge(ComboBox combo) => ControlHarness.Part<Border>(combo, "Background");

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void RestIs26PxHighWithTheControlEdgeAndAChevronIcon(ThemeMode mode)
    {
        var (_, combo) = Host(mode);
        var edge = BoxEdge(combo);

        Assert.Equal(26, combo.Bounds.Height);
        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", edge.Background, "fill");
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", edge.BorderBrush, "edge");
        Assert.Equal(new CornerRadius(3), edge.CornerRadius);

        var chevron = combo.GetVisualDescendants().OfType<StrokeIcon>().Single();
        Application.Current!.TryGetResource("Raffica.Icon.ChevronDown", null, out var geometry);
        Assert.NotNull(geometry);
        Assert.Same(geometry, chevron.Data);
        Assert.Equal(16, chevron.Size);
        ControlHarness.AssertToken("Raffica.Brush.Text.Primary", chevron.EffectiveStroke, "chevron");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void HoverEdgeIsSecondaryAndDisabledUsesTheBand(ThemeMode mode)
    {
        var (window, combo) = Host(mode);
        StateMatrix.Force(combo, ":pointerover");
        ControlHarness.Settle(window);
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", BoxEdge(combo).BorderBrush, "hover edge");

        var (_, disabled) = Host(mode, enabled: false);
        ControlHarness.AssertToken("Raffica.Brush.Surface.Band", BoxEdge(disabled).Background, "disabled fill");
        ControlHarness.AssertToken("Raffica.Brush.Text.Disabled", BoxEdge(disabled).BorderBrush, "disabled edge");
        ControlHarness.AssertToken("Raffica.Brush.Text.Disabled", disabled.Foreground, "disabled text");
        ControlHarness.AssertToken("Raffica.Brush.Text.Disabled", disabled.GetVisualDescendants().OfType<StrokeIcon>().Single().EffectiveStroke, "disabled chevron");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, PhysicalKey.Enter, false)]
    [InlineData(ThemeMode.Dark, PhysicalKey.Enter, false)]
    [InlineData(ThemeMode.Light, PhysicalKey.Enter, true)]
    [InlineData(ThemeMode.Dark, PhysicalKey.Enter, true)]
    [InlineData(ThemeMode.Light, PhysicalKey.ArrowDown, true)]
    [InlineData(ThemeMode.Dark, PhysicalKey.ArrowDown, true)]
    [InlineData(ThemeMode.Light, PhysicalKey.ArrowUp, true)]
    [InlineData(ThemeMode.Dark, PhysicalKey.ArrowUp, true)]
    public void OpensOnEnterAndDown(ThemeMode mode, PhysicalKey key, bool selectBox)
    {
        var (window, combo) = Host(mode, selectBox: selectBox);
        ControlHarness.KeyboardFocus(window, combo);
        Assert.False(combo.IsDropDownOpen);

        Press(window, key);

        Assert.True(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, false)]
    [InlineData(ThemeMode.Dark, false)]
    [InlineData(ThemeMode.Light, true)]
    [InlineData(ThemeMode.Dark, true)]
    public void KeyboardFocusShowsTheInkRingOutsideTheEdge(ThemeMode mode, bool selectBox)
    {
        var (window, combo) = Host(mode, selectBox: selectBox);

        ControlHarness.KeyboardFocus(window, combo);

        var layer = AdornerLayer.GetAdornerLayer(combo);
        Assert.NotNull(layer);
        var ring = Assert.Single(layer.Children.OfType<Border>());
        Assert.Equal(new Thickness(2), ring.BorderThickness);
        Assert.Equal(new Thickness(-4), ring.Margin);
        ControlHarness.AssertToken("Raffica.Brush.Ink", ring.BorderBrush, "ring");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void EscapeClosesAndKeepsTheSelection(ThemeMode mode)
    {
        var (window, combo) = Host(mode, selectBox: true);
        ControlHarness.KeyboardFocus(window, combo);
        Press(window, PhysicalKey.ArrowDown);
        Assert.True(combo.IsDropDownOpen);

        Press(window, PhysicalKey.Escape);

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void PopupIsAMenuStyleListWithA1PxEdgeAnd32PxRowsAndNoShadow(ThemeMode mode)
    {
        var (window, combo) = Host(mode);
        combo.IsDropDownOpen = true;
        ControlHarness.Settle(window);

        var popup = ControlHarness.Part<Popup>(combo, "PART_Popup");
        Assert.False(popup.WindowManagerAddShadowHint);
        var border = ControlHarness.Part<Border>(combo, "PopupBorder");
        ControlHarness.AssertToken("Raffica.Brush.Surface.Page", border.Background, "popup fill");
        ControlHarness.AssertToken("Raffica.Brush.Border.Control", border.BorderBrush, "popup edge");
        Assert.Equal(new Thickness(1), border.BorderThickness);
        Assert.Equal(new CornerRadius(3), border.CornerRadius);
        Assert.Equal(default, border.BoxShadow);
        Assert.False(ControlHarness.HasShadow(border));

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(32, combo.ContainerFromIndex(i)!.Bounds.Height);
        }
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void RowsUseHoverAndTheSelectedListRowStyle(ThemeMode mode)
    {
        var (window, combo) = Host(mode);
        combo.IsDropDownOpen = true;
        ControlHarness.Settle(window);

        var selected = (ComboBoxItem)combo.ContainerFromIndex(1)!;
        var other = (ComboBoxItem)combo.ContainerFromIndex(2)!;
        var selectedPresenter = ControlHarness.Presenter(selected);
        var otherPresenter = ControlHarness.Presenter(other);

        ControlHarness.AssertToken("Raffica.Brush.Selection.Tint", selectedPresenter.Background, "selected fill");
        Assert.Equal(FontWeight.SemiBold, selected.FontWeight);
        Assert.Equal(Colors.Transparent, ControlHarness.Strict(otherPresenter.Background));

        StateMatrix.Force(other, ":pointerover");
        ControlHarness.Settle(window);
        ControlHarness.AssertToken("Raffica.Brush.State.Hover", otherPresenter.Background, "hover fill");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheSelectedItemIsAnnouncedByTheAutomationPeer(ThemeMode mode)
    {
        var (_, combo) = Host(mode);
        var peer = ControlAutomationPeer.CreatePeerForElement(combo);

        var selection = peer.GetProvider<ISelectionProvider>();
        Assert.NotNull(selection);
        var selected = Assert.Single(selection.GetSelection());
        Assert.Equal("Beta", selected.GetName());
    }
}
