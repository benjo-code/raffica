using Avalonia;
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

public sealed class ButtonTests
{
    private static readonly Color Transparent = Colors.Transparent;

    private static (Window Window, Button Button) Host(ThemeMode mode, string classes = "", bool enabled = true)
    {
        var button = new Button { Content = "Insert", IsEnabled = enabled };
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            button.Classes.Add(name);
        }

        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { button } }, mode);
        return (window, button);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DefaultButtonIsTransparentAt26PxAndHoverUsesTheHoverToken(ThemeMode mode)
    {
        var (window, button) = Host(mode);
        var presenter = ControlHarness.Presenter(button);

        Assert.Equal(26, button.Bounds.Height);
        Assert.Equal(Transparent, ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(Transparent, ControlHarness.ColorOf(presenter.BorderBrush));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Text.Primary"), ControlHarness.ColorOf(presenter.Foreground));
        Assert.Equal(new CornerRadius(3), presenter.CornerRadius);

        StateMatrix.Force(button, ":pointerover");
        ControlHarness.Settle(window);

        Assert.Equal(ControlHarness.Token("Raffica.Brush.State.Hover"), ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Text.Primary"), ControlHarness.ColorOf(presenter.Foreground));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void RibbonButtonsPadFiveAndTextButtonsPadEightPixels(ThemeMode mode)
    {
        var (_, ribbon) = Host(mode);
        var (_, boxed) = Host(mode, "boxed");
        var (_, primary) = Host(mode, "primary");
        var (_, destructive) = Host(mode, "destructive");

        Assert.Equal(5, ControlHarness.Presenter(ribbon).Padding.Left);
        Assert.Equal(8, ControlHarness.Presenter(boxed).Padding.Left);
        Assert.Equal(8, ControlHarness.Presenter(primary).Padding.Left);
        Assert.Equal(8, ControlHarness.Presenter(destructive).Padding.Left);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void BoxedButtonHasThePageFillAndTheControlEdge(ThemeMode mode)
    {
        var (window, button) = Host(mode, "boxed");
        var presenter = ControlHarness.Presenter(button);

        Assert.Equal(ControlHarness.Token("Raffica.Brush.Surface.Page"), ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Border.Control"), ControlHarness.ColorOf(presenter.BorderBrush));
        Assert.Equal(new Thickness(1), presenter.BorderThickness);

        StateMatrix.Force(button, ":pointerover");
        ControlHarness.Settle(window);

        Assert.Equal(ControlHarness.Token("Raffica.Brush.State.Hover"), ControlHarness.ColorOf(presenter.Background));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void PrimaryButtonUsesPrimaryFillAndOnInkText(ThemeMode mode)
    {
        var (window, button) = Host(mode, "primary");
        var presenter = ControlHarness.Presenter(button);

        Assert.Equal(ControlHarness.Token("Raffica.Brush.Button.Primary.Fill"), ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Text.OnInk"), ControlHarness.ColorOf(presenter.Foreground));
        Assert.Equal(FontWeight.SemiBold, button.FontWeight);

        StateMatrix.Force(button, ":pointerover");
        ControlHarness.Settle(window);

        Assert.Equal(ControlHarness.Token("Raffica.Brush.Button.Primary.Hover"), ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Text.OnInk"), ControlHarness.ColorOf(presenter.Foreground));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, "")]
    [InlineData(ThemeMode.Dark, "")]
    [InlineData(ThemeMode.Light, "boxed")]
    [InlineData(ThemeMode.Dark, "boxed")]
    [InlineData(ThemeMode.Light, "primary")]
    [InlineData(ThemeMode.Dark, "primary")]
    public void PressedFillsWithTheInk(ThemeMode mode, string classes)
    {
        var (window, button) = Host(mode, classes);
        var presenter = ControlHarness.Presenter(button);

        StateMatrix.Force(button, ":pointerover", ":pressed");
        ControlHarness.Settle(window);

        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Text.OnInk"), ControlHarness.ColorOf(presenter.Foreground));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DestructiveButtonUsesDangerEdgeAndText(ThemeMode mode)
    {
        var (window, button) = Host(mode, "destructive");
        var presenter = ControlHarness.Presenter(button);
        var danger = ControlHarness.Token("Raffica.Brush.Danger");

        Assert.Equal(ControlHarness.Token("Raffica.Brush.Surface.Page"), ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(danger, ControlHarness.ColorOf(presenter.BorderBrush));
        Assert.Equal(danger, ControlHarness.ColorOf(presenter.Foreground));
        Assert.Equal(FontWeight.SemiBold, button.FontWeight);

        StateMatrix.Force(button, ":pointerover");
        ControlHarness.Settle(window);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.State.Hover"), ControlHarness.ColorOf(presenter.Background));

        StateMatrix.Force(button, ":pressed");
        ControlHarness.Settle(window);
        Assert.Equal(danger, ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Text.OnInk"), ControlHarness.ColorOf(presenter.Foreground));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DisabledUsesDisabledText(ThemeMode mode)
    {
        var (_, plain) = Host(mode, enabled: false);
        var disabled = ControlHarness.Token("Raffica.Brush.Text.Disabled");
        var plainPresenter = ControlHarness.Presenter(plain);
        Assert.Equal(disabled, ControlHarness.ColorOf(plainPresenter.Foreground));
        Assert.Equal(Transparent, ControlHarness.ColorOf(plainPresenter.Background));

        foreach (var classes in new[] { "boxed", "primary", "destructive" })
        {
            var (_, button) = Host(mode, classes, enabled: false);
            var presenter = ControlHarness.Presenter(button);
            Assert.Equal(disabled, ControlHarness.ColorOf(presenter.Foreground));
            Assert.Equal(disabled, ControlHarness.ColorOf(presenter.BorderBrush));
            Assert.Equal(ControlHarness.Token("Raffica.Brush.Surface.Page"), ControlHarness.ColorOf(presenter.Background));
        }
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, "")]
    [InlineData(ThemeMode.Light, "boxed")]
    [InlineData(ThemeMode.Light, "primary")]
    [InlineData(ThemeMode.Dark, "destructive")]
    [InlineData(ThemeMode.Dark, "icon")]
    public void ButtonHeightIs26AndHitTargetIsAtLeast24(ThemeMode mode, string classes)
    {
        var (_, button) = Host(mode, classes);
        if (classes == "icon")
        {
            button.Content = new StrokeIcon { Size = 16 };
            ControlHarness.Settle((Window)TopLevel.GetTopLevel(button)!);
        }

        Assert.Equal(26, button.Bounds.Height);
        Assert.True(button.Bounds.Width >= 24, $"width {button.Bounds.Width}");
        if (classes == "icon")
        {
            Assert.Equal(26, button.Bounds.Width);
        }
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, "", false)]
    [InlineData(ThemeMode.Dark, "boxed", false)]
    [InlineData(ThemeMode.Light, "primary", false)]
    [InlineData(ThemeMode.Dark, "destructive", true)]
    public void FocusRingUsesTheInkAndDangerOnDestructive(ThemeMode mode, string classes, bool danger)
    {
        var (window, button) = Host(mode, classes);

        ControlHarness.KeyboardFocus(window, button);

        Assert.True(button.IsFocused);
        var layer = AdornerLayer.GetAdornerLayer(button);
        Assert.NotNull(layer);
        var ring = Assert.Single(layer.Children.OfType<Border>());
        Assert.Equal(new Thickness(2), ring.BorderThickness);
        Assert.Equal(new Thickness(-4), ring.Margin);
        var expected = danger ? ControlHarness.Token("Raffica.Brush.Danger") : ControlHarness.Token("Raffica.Brush.Ink");
        Assert.Equal(expected, ControlHarness.ColorOf(ring.BorderBrush));
    }

    [AvaloniaFact]
    public void TheRealPointerSetsHoverAndPressed()
    {
        var (window, button) = Host(ThemeMode.Light);
        var presenter = ControlHarness.Presenter(button);
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, 13), window)!.Value;

        window.MouseMove(center);
        ControlHarness.Settle(window);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.State.Hover"), ControlHarness.ColorOf(presenter.Background));

        window.MouseDown(center, MouseButton.Left);
        ControlHarness.Settle(window);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(presenter.Background));

        window.MouseUp(center, MouseButton.Left);
    }
}
