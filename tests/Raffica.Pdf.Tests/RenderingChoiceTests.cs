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
}
