using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace Raffica.Shell.Tests.Controls;

/// <summary>The class wrap of check boxes, buttons and toggle buttons: a text label breaks into lines at the room it has instead of being cut off.</summary>
public sealed class WrapClassTests
{
    private const string Long = "Fit the text size to the mark that is selected and the other marks";

    private static Window Host(ContentControl control, ThemeMode mode, double width)
    {
        control.Content = Long;
        control.Classes.Add("wrap");
        return ControlHarness.Show(new StackPanel { Width = width, Margin = new Thickness(20), Children = { control } }, mode);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void ACheckBoxLabelWrapsAndTheBoxGrowsInHeightNotWidth(ThemeMode mode)
    {
        var box = new CheckBox();
        Host(box, mode, 160);
        var text = box.GetVisualDescendants().OfType<TextBlock>().Single();

        Assert.True(box.Bounds.Width <= 160 + 0.5, box.Bounds.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(box.Bounds.Height > 26 * 2, box.Bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        Assert.Equal(Long, box.Content);
        Assert.True(text.Bounds.Right <= box.Bounds.Width + 0.5);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void AButtonLabelWrapsAndTheButtonGrowsInHeight(ThemeMode mode)
    {
        var button = new Button { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        Host(button, mode, 160);
        var text = button.GetVisualDescendants().OfType<TextBlock>().Single();

        Assert.Equal(160, button.Bounds.Width);
        Assert.True(button.Bounds.Height > 26 * 2, button.Bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(text.Bounds.Right <= button.Bounds.Width + 0.5);
        Assert.Equal(Long, button.Content);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void AShortLabelKeepsTheHeightOfTheControls(ThemeMode mode)
    {
        var box = new CheckBox { Content = "Short", Classes = { "wrap" } };
        var button = new Button { Content = "Short", Classes = { "wrap" } };
        ControlHarness.Show(new StackPanel { Width = 300, Children = { box, button } }, mode);

        Assert.Equal(26, box.Bounds.Height);
        Assert.Equal(26, button.Bounds.Height);
    }
}
