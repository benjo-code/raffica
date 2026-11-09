using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

/// <summary>
/// The ring that really appears on keyboard focus (not the one the gallery draws by hand): the adorner layer must not clip it. Its clip
/// is set in the coordinates of the adorner, which starts 4 px outside the control, so a clipped ring loses its right and bottom edge.
/// </summary>
public sealed class FocusRingTests
{
    private static Button Plain() => new() { Content = "Plain" };

    private static Button Destructive()
    {
        var button = new Button { Content = "Delete" };
        button.Classes.Add("destructive");
        return button;
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, "button")]
    [InlineData(ThemeMode.Dark, "button")]
    [InlineData(ThemeMode.Light, "destructive")]
    [InlineData(ThemeMode.Dark, "destructive")]
    [InlineData(ThemeMode.Light, "checkbox")]
    [InlineData(ThemeMode.Dark, "checkbox")]
    [InlineData(ThemeMode.Light, "radio")]
    [InlineData(ThemeMode.Dark, "radio")]
    public void TheKeyboardRingIsNotClipped(ThemeMode mode, string kind)
    {
        Control control = kind switch
        {
            "destructive" => Destructive(),
            "checkbox" => new CheckBox { Content = "Check" },
            "radio" => new RadioButton { Content = "Radio" },
            _ => Plain(),
        };
        var window = ControlHarness.Show(new StackPanel { Margin = new Thickness(40), Children = { control } }, mode);

        ControlHarness.KeyboardFocus(window, control);

        var layer = AdornerLayer.GetAdornerLayer(control)!;
        var adorner = Assert.Single(layer.Children);
        Assert.False(AdornerLayer.GetIsClipEnabled(adorner));
        Assert.Null(adorner.Clip);
    }
}
