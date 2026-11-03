using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Gallery;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class SegmentedControlTests
{
    private static (Window Window, SegmentedControl Control) Host(ThemeMode mode)
    {
        var control = new SegmentedControl { ItemsSource = new[] { "Headings", "Pages", "Results" }, SelectedIndex = 0 };
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { control } }, mode);
        return (window, control);
    }

    private static ListBoxItem Item(SegmentedControl control, int index) => (ListBoxItem)control.ContainerFromIndex(index)!;

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        ControlHarness.Settle(window);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ArrowKeysMoveTheSelectionAndExactlyOneItemIsSelected(ThemeMode mode)
    {
        var (window, control) = Host(mode);
        ControlHarness.KeyboardFocus(window, Item(control, 0));

        Press(window, PhysicalKey.ArrowRight);
        Assert.Equal(1, control.SelectedIndex);
        Press(window, PhysicalKey.ArrowRight);
        Assert.Equal(2, control.SelectedIndex);
        Press(window, PhysicalKey.ArrowLeft);
        Assert.Equal(1, control.SelectedIndex);

        Assert.Single(control.Selection.SelectedIndexes);
        Assert.Equal(1, Enumerable.Range(0, 3).Count(i => Item(control, i).IsSelected));
        Assert.True(control.SelectionMode.HasFlag(SelectionMode.Single));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void RestHoverAndSelectedUseTheSegmentTokens(ThemeMode mode)
    {
        var (window, control) = Host(mode);
        var rest = Item(control, 1);
        var selected = Item(control, 0);

        Assert.Equal(26, rest.Bounds.Height);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Surface.Page"), ControlHarness.ColorOf(ControlHarness.Presenter(rest).Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Border.Control"), ControlHarness.ColorOf(ControlHarness.Presenter(rest).BorderBrush));
        Assert.Equal(FontWeight.Normal, rest.FontWeight);
        Assert.False(ControlHarness.Part<Border>(rest, "InkEdge").IsVisible);

        StateMatrix.Force(rest, ":pointerover");
        ControlHarness.Settle(window);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.State.Hover"), ControlHarness.ColorOf(ControlHarness.Presenter(rest).Background));

        Assert.Equal(ControlHarness.Token("Raffica.Brush.Selection.Tint"), ControlHarness.ColorOf(ControlHarness.Presenter(selected).Background));
        Assert.Equal(FontWeight.SemiBold, selected.FontWeight);
        var edge = ControlHarness.Part<Border>(selected, "InkEdge");
        Assert.True(edge.IsVisible);
        Assert.Equal(3, edge.Bounds.Height);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(edge.Background));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void OnlyTheOuterEndsAreRounded(ThemeMode mode)
    {
        var (_, control) = Host(mode);

        Assert.Equal(new CornerRadius(3, 0, 0, 3), ControlHarness.Presenter(Item(control, 0)).CornerRadius);
        Assert.Equal(new CornerRadius(0), ControlHarness.Presenter(Item(control, 1)).CornerRadius);
        Assert.Equal(new CornerRadius(0, 3, 3, 0), ControlHarness.Presenter(Item(control, 2)).CornerRadius);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void TheControlExposesASelectionAutomationPatternWithRadioItems(ThemeMode mode)
    {
        var (_, control) = Host(mode);

        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        Assert.IsAssignableFrom<ISelectionProvider>(peer);
        Assert.False(((ISelectionProvider)peer).CanSelectMultiple);
        Assert.True(((ISelectionProvider)peer).IsSelectionRequired);

        var itemPeer = ControlAutomationPeer.CreatePeerForElement(Item(control, 1));
        Assert.Equal(AutomationControlType.RadioButton, itemPeer.GetAutomationControlType());
        Assert.IsAssignableFrom<ISelectionItemProvider>(itemPeer);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void KeyboardFocusShowsTheRingOnTheSegment(ThemeMode mode)
    {
        var (window, control) = Host(mode);

        ControlHarness.KeyboardFocus(window, Item(control, 1));

        var layer = AdornerLayer.GetAdornerLayer(control)!;
        var ring = Assert.Single(layer.Children.OfType<Border>());
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(ring.BorderBrush));
    }
}
