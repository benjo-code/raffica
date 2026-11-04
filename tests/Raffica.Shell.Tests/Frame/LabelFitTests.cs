using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class LabelFitTests
{
    private static Window Show(Control content) => ControlHarness.Show(content, ThemeMode.Light);

    [AvaloniaFact]
    public void TheCheckFindsALabelThatDoesNotFit()
    {
        var window = Show(new TextBlock { Text = "Rechtschreibprüfung", Width = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });

        Assert.StartsWith("Rechtschreibprüfung (", Assert.Single(LabelFit.FindClipped(window)));
    }

    [AvaloniaFact]
    public void TheCheckFindsAWrappingLabelThatBreaksAWord()
    {
        var window = Show(new TextBlock { Text = "Rechtschreibprüfung", Width = 40, TextWrapping = Avalonia.Media.TextWrapping.Wrap, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });

        Assert.Single(LabelFit.FindClipped(window));
    }

    [AvaloniaFact]
    public void TheCheckAcceptsALabelThatFits()
    {
        var window = Show(new TextBlock { Text = "Copy", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });

        Assert.Empty(LabelFit.FindClipped(window));
    }

    [AvaloniaFact]
    public void TheCheckFindsALabelOutsideTheWindow()
    {
        var label = new TextBlock { Text = "Copy", Margin = new Avalonia.Thickness(600, 0, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var window = Show(new Canvas { Children = { label } });
        Canvas.SetLeft(label, 600);
        ControlHarness.Settle(window);

        Assert.Single(LabelFit.FindClipped(window));
    }
}
