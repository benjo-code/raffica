using Raffica.Pdf.Hosting;
using Xunit;

namespace Raffica.Pdf.Tests;

public sealed class RenderingChoiceTests
{
    [Theory]
    [InlineData("default")]
    [InlineData(" DEFAULT ")]
    [InlineData("Default")]
    public void DefaultInAnyCaseAndPaddingOptsOutOfSoftwareRendering(string value) =>
        Assert.Equal(RenderingChoice.Default, RenderingChoiceParser.Parse(value));

    [Theory]
    [InlineData("software")]
    [InlineData(" Software ")]
    [InlineData("SOFTWARE")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("vulkan")]
    [InlineData("gpu")]
    public void EverythingElseGivesSoftwareRendering(string? value) =>
        Assert.Equal(RenderingChoice.Software, RenderingChoiceParser.Parse(value));

    [Theory]
    [InlineData("software")]
    [InlineData(" Software ")]
    [InlineData("SOFTWARE")]
    public void SoftwareIsChosenOnMacOnlyWhenAskedFor(string value) =>
        Assert.Equal(RenderingChoice.Software, RenderingChoiceParser.For(RenderingPlatform.MacOS, value));

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("gpu")]
    public void MacKeepsAvaloniasOwnPathUnlessSoftwareIsAskedFor(string? value) =>
        Assert.Equal(RenderingChoice.Default, RenderingChoiceParser.For(RenderingPlatform.MacOS, value));

    [Theory]
    [InlineData("default", false)]
    [InlineData("software", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("nonsense", true)]
    public void WindowsKeepsItsExistingMapping(string? value, bool software) =>
        Assert.Equal(software ? RenderingChoice.Software : RenderingChoice.Default, RenderingChoiceParser.For(RenderingPlatform.Windows, value));

    [Theory]
    [InlineData("software")]
    [InlineData("default")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    public void LinuxIsUntouchedByTheVariable(string? value)
    {
        Assert.Equal(RenderingChoice.Default, RenderingChoiceParser.For(RenderingPlatform.Linux, value));
        Assert.Equal(RenderingChoice.Default, RenderingChoiceParser.For(RenderingPlatform.Other, value));
    }
}
