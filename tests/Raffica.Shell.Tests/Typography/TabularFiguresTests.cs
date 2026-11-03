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

    [AvaloniaFact]
    public void DigitsDifferInWidthWithoutTabularFigures()
    {
        // Negative control: without the tnum class the same strings are measured with proportional digits, so the test above is not vacuous.
        Assert.NotEqual(WidthOf("1111", tabular: false), WidthOf("0000", tabular: false));
    }

    private static double WidthOf(string digits, bool tabular = true)
    {
        var text = new TextBlock { Text = digits };
        text.Classes.Add("ui");
        if (tabular)
        {
            text.Classes.Add("tnum");
        }

        var window = new Window { Content = text };
        window.Show();
        text.Measure(Size.Infinity);
        return text.DesiredSize.Width;
    }
}
