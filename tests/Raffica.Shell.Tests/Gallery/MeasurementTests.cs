using Raffica.Gallery;
using Xunit;

namespace Raffica.Shell.Tests.Gallery;

public sealed class MeasurementTests
{
    [Fact]
    public void InputLineFormatMatchesTheScript()
    {
        // 98 presses of 1 ms, one of 2 ms and one of 3 ms: median 1, the 99th percentile 2, the maximum 3.
        double[] presses = [.. Enumerable.Repeat(1.0, 98), 2.0, 3.0];

        Assert.Equal("input median-ms=1.00 p99-ms=2.00 max-ms=3.00", Measurement.InputLine(presses));
    }

    [Fact]
    public void TheInputLineIgnoresTheOrderOfThePresses()
    {
        double[] presses = [3.0, 1.0, 2.0, 1.0, 1.0];

        Assert.Equal("input median-ms=1.00 p99-ms=3.00 max-ms=3.00", Measurement.InputLine(presses));
    }

    [Fact]
    public void TheInputLineUsesTheInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("input median-ms=1.50 p99-ms=1.50 max-ms=1.50", Measurement.InputLine([1.5]));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void OptionsRejectInputWithoutFrameView()
    {
        Assert.Throws<ArgumentException>(() => GalleryOptions.Parse(["--measure-input"]));
        Assert.Throws<ArgumentException>(() => GalleryOptions.Parse(["--view", "gallery", "--measure-input"]));
        Assert.True(GalleryOptions.Parse(["--view", "frame", "--measure-input"]).MeasureInput);
    }

    [Fact]
    public void ExitCodeThreeMeansTimedOut() => Assert.Equal(3, Measurement.TimedOut);
}
