using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Raffica.Gallery;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

/// <summary>An icon without a stroke of its own draws in the text colour of the control it sits in, and follows its states.</summary>
public sealed class IconColourTests
{
    private static Color Strict(IBrush? brush)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return solid.Color;
    }

    private static (Window Window, T Button, StrokeIcon Icon) Host<T>(ThemeMode mode, string classes, bool enabled = true)
        where T : Button, new()
    {
        var icon = new StrokeIcon { Size = 16 };
        var button = new T { Content = icon, IsEnabled = enabled };
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            button.Classes.Add(name);
        }

        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(20), Children = { button } }, mode);
        return (window, button, icon);
    }

    private static void AssertIcon(StrokeIcon icon, string token) =>
        Assert.Equal(ControlHarness.Token(token), Strict(icon.EffectiveStroke));

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void IconInAnIconButtonFollowsRestPressedAndDisabled(ThemeMode mode)
    {
        var (window, button, icon) = Host<Button>(mode, "icon");
        Assert.Null(icon.Stroke);
        AssertIcon(icon, "Raffica.Brush.Text.Primary");

        StateMatrix.Force(button, ":pointerover", ":pressed");
        ControlHarness.Settle(window);
        AssertIcon(icon, "Raffica.Brush.Text.OnInk");

        var (_, _, disabledIcon) = Host<Button>(mode, "icon", enabled: false);
        AssertIcon(disabledIcon, "Raffica.Brush.Text.Disabled");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void IconInAToggleFollowsOnAndDisabled(ThemeMode mode)
    {
        var (window, toggle, icon) = Host<ToggleButton>(mode, "icon");
        AssertIcon(icon, "Raffica.Brush.Text.Primary");

        toggle.IsChecked = true;
        ControlHarness.Settle(window);
        AssertIcon(icon, "Raffica.Brush.Text.OnInk");

        toggle.IsChecked = false;
        ControlHarness.Settle(window);
        AssertIcon(icon, "Raffica.Brush.Text.Primary");

        var (_, _, disabledIcon) = Host<ToggleButton>(mode, "icon", enabled: false);
        AssertIcon(disabledIcon, "Raffica.Brush.Text.Disabled");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void IconInADestructiveButtonIsDangerThenOnInkWhenPressed(ThemeMode mode)
    {
        var (window, button, icon) = Host<Button>(mode, "destructive icon");
        AssertIcon(icon, "Raffica.Brush.Danger");

        StateMatrix.Force(button, ":pressed");
        ControlHarness.Settle(window);
        AssertIcon(icon, "Raffica.Brush.Text.OnInk");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void IconInAPrimaryButtonUsesOnInk(ThemeMode mode)
    {
        var (_, _, icon) = Host<Button>(mode, "primary icon");

        AssertIcon(icon, "Raffica.Brush.Text.OnInk");
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void AnExplicitStrokeWinsOverTheInheritedColour(ThemeMode mode)
    {
        var (window, button, icon) = Host<Button>(mode, "icon");
        var own = new SolidColorBrush(Color.Parse("#336699"));
        icon.Stroke = own;
        ControlHarness.Settle(window);

        Assert.Equal(own.Color, Strict(icon.EffectiveStroke));

        StateMatrix.Force(button, ":pressed");
        ControlHarness.Settle(window);
        Assert.Equal(own.Color, Strict(icon.EffectiveStroke));
    }

    [AvaloniaFact]
    public void AnIconOutsideAnyControlTakesTheDefaultTextColour()
    {
        var icon = new StrokeIcon();

        Assert.Same(TextElement.GetForeground(icon), icon.EffectiveStroke);
        Assert.NotNull(icon.EffectiveStroke);
    }
}
