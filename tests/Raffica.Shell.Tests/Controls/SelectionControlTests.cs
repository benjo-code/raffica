using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Gallery;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

public sealed class SelectionControlTests
{
    private static Window Host(ThemeMode mode, params Control[] controls) =>
        ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Spacing = 8, Children = { } }.With(controls), mode);

    private static void PressSpace(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        ControlHarness.Settle(window);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ToggleOnFillsWithTheInkAndOnInkText(ThemeMode mode)
    {
        var toggle = new ToggleButton { Content = "Bold" };
        var window = Host(mode, toggle);
        var presenter = ControlHarness.Presenter(toggle);
        Assert.Equal(Colors.Transparent, ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(26, toggle.Bounds.Height);

        toggle.IsChecked = true;
        ControlHarness.Settle(window);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(presenter.Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Text.OnInk"), ControlHarness.ColorOf(presenter.Foreground));

        StateMatrix.Force(toggle, ":pointerover");
        ControlHarness.Settle(window);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(presenter.Background));

        toggle.IsChecked = false;
        ControlHarness.Settle(window);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.State.Hover"), ControlHarness.ColorOf(presenter.Background));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ToggleReactsToSpace(ThemeMode mode)
    {
        var toggle = new ToggleButton { Content = "Bold" };
        var window = Host(mode, toggle);
        ControlHarness.KeyboardFocus(window, toggle);

        PressSpace(window);
        Assert.True(toggle.IsChecked);
        PressSpace(window);
        Assert.False(toggle.IsChecked);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void CheckBoxIs16PxWithRadius2AndChecksWithAnInkFillAndATick(ThemeMode mode)
    {
        var check = new CheckBox { Content = "Track changes" };
        var window = Host(mode, check);
        var box = ControlHarness.Part<Border>(check, "Box");
        var tick = ControlHarness.Part<StrokeIcon>(check, "Tick");

        Assert.Equal(new Size(16, 16), box.Bounds.Size);
        Assert.Equal(new CornerRadius(2), box.CornerRadius);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Surface.Page"), ControlHarness.ColorOf(box.Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Border.Control"), ControlHarness.ColorOf(box.BorderBrush));
        Assert.False(tick.IsVisible);
        Assert.True(check.Bounds.Height >= 24);

        check.IsChecked = true;
        ControlHarness.Settle(window);

        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(box.Background));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(box.BorderBrush));
        Assert.True(tick.IsVisible);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Text.OnInk"), ControlHarness.ColorOf(tick.Stroke));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void DisabledCheckBoxAndRadioUseTheDisabledText(ThemeMode mode)
    {
        var check = new CheckBox { Content = "Track changes", IsEnabled = false };
        var radio = new RadioButton { Content = "Portrait", IsEnabled = false };
        Host(mode, check, radio);
        var disabled = ControlHarness.Token("Raffica.Brush.Text.Disabled");

        Assert.Equal(disabled, ControlHarness.ColorOf(ControlHarness.Presenter(check).Foreground));
        Assert.Equal(disabled, ControlHarness.ColorOf(ControlHarness.Part<Border>(check, "Box").BorderBrush));
        Assert.Equal(disabled, ControlHarness.ColorOf(ControlHarness.Presenter(radio).Foreground));
        Assert.Equal(disabled, ControlHarness.ColorOf(ControlHarness.Part<Ellipse>(radio, "Circle").Stroke));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void CheckBoxReactsToSpace(ThemeMode mode)
    {
        var check = new CheckBox { Content = "Track changes" };
        var window = Host(mode, check);
        ControlHarness.KeyboardFocus(window, check);

        PressSpace(window);
        Assert.True(check.IsChecked);
        PressSpace(window);
        Assert.False(check.IsChecked);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void RadioCheckedShowsTheSixPixelDot(ThemeMode mode)
    {
        var radio = new RadioButton { Content = "Portrait", GroupName = "orientation" };
        var other = new RadioButton { Content = "Landscape", GroupName = "orientation" };
        var window = Host(mode, radio, other);
        var circle = ControlHarness.Part<Ellipse>(radio, "Circle");
        var dot = ControlHarness.Part<Ellipse>(radio, "Dot");

        Assert.Equal(new Size(16, 16), circle.Bounds.Size);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Surface.Page"), ControlHarness.ColorOf(circle.Fill));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Border.Control"), ControlHarness.ColorOf(circle.Stroke));
        Assert.False(dot.IsVisible);

        radio.IsChecked = true;
        ControlHarness.Settle(window);

        Assert.True(dot.IsVisible);
        Assert.Equal(new Size(6, 6), dot.Bounds.Size);
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Text.OnInk"), ControlHarness.ColorOf(dot.Fill));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(circle.Fill));
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(circle.Stroke));
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void RadioReactsToSpace(ThemeMode mode)
    {
        var radio = new RadioButton { Content = "Portrait", GroupName = "orientation" };
        var window = Host(mode, radio);
        ControlHarness.KeyboardFocus(window, radio);

        PressSpace(window);

        Assert.True(radio.IsChecked);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void CheckBoxAndRadioShowTheRingAroundTheirBoxOnKeyboardFocus(ThemeMode mode)
    {
        var check = new CheckBox { Content = "Track changes" };
        var radio = new RadioButton { Content = "Portrait" };
        var window = Host(mode, check, radio);
        var ink = ControlHarness.Token("Raffica.Brush.Ink");

        ControlHarness.KeyboardFocus(window, check);
        var layer = AdornerLayer.GetAdornerLayer(check)!;
        var checkRing = Assert.IsType<Border>(Assert.Single(layer.Children));
        Assert.Equal(new Thickness(2), checkRing.BorderThickness);
        Assert.Equal(ink, ControlHarness.ColorOf(checkRing.BorderBrush));
        Assert.Equal(new Size(24, 24), checkRing.Bounds.Size);
        var box = ControlHarness.Part<Border>(check, "Box");
        var boxOrigin = box.TranslatePoint(default, window)!.Value;
        var ringOrigin = checkRing.TranslatePoint(default, window)!.Value;
        Assert.Equal(boxOrigin.X - 4, ringOrigin.X);
        Assert.Equal(boxOrigin.Y - 4, ringOrigin.Y);

        ControlHarness.KeyboardFocus(window, radio);
        var radioRing = Assert.IsType<Ellipse>(Assert.Single(layer.Children));
        Assert.Equal(2, radioRing.StrokeThickness);
        Assert.Equal(ink, ControlHarness.ColorOf(radioRing.Stroke));
        Assert.Equal(new Size(24, 24), radioRing.Bounds.Size);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ToggleShowsTheFocusRingAsAnAdorner(ThemeMode mode)
    {
        var toggle = new ToggleButton { Content = "Bold" };
        var window = Host(mode, toggle);

        ControlHarness.KeyboardFocus(window, toggle);

        var layer = AdornerLayer.GetAdornerLayer(toggle)!;
        var ring = Assert.Single(layer.Children.OfType<Border>());
        Assert.Equal(ControlHarness.Token("Raffica.Brush.Ink"), ControlHarness.ColorOf(ring.BorderBrush));
    }
}

internal static class PanelExtensions
{
    public static StackPanel With(this StackPanel panel, IEnumerable<Control> children)
    {
        panel.Children.AddRange(children);
        return panel;
    }
}
