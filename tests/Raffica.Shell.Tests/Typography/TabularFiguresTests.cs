using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Raffica.Shell.Tests.Typography;

public sealed class TabularFiguresTests
{
    [AvaloniaFact]
    public void DigitsHaveTheSameWidthWithTabularFigures()
    {
        Assert.Equal(WidthOf("1111"), WidthOf("0000"));
    }

    private static double WidthOf(string digits)
    {
        var text = new TextBlock { Text = digits };
        text.Classes.Add("ui");
        text.Classes.Add("tnum");
        var window = new Window { Content = text };
        window.Show();
        text.Measure(Size.Infinity);
        return text.DesiredSize.Width;
    }
}
