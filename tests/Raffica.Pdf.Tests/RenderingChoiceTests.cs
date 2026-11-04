using Raffica.Pdf.Hosting;
using Xunit;

namespace Raffica.Pdf.Tests;

public sealed class RenderingChoiceTests
{
    [Theory]
    [InlineData("software")]
    [InlineData(" Software ")]
    [InlineData("SOFTWARE")]
    public void SoftwareInAnyCaseAndPaddingAsksForSoftwareRendering(string value) =>
        Assert.Equal(RenderingChoice.Software, RenderingChoiceParser.Parse(value));

    [Theory]
    [InlineData("default")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("vulkan")]
    [InlineData("gpu")]
    public void EverythingElseKeepsAvaloniasDefault(string? value) =>
        Assert.Equal(RenderingChoice.Default, RenderingChoiceParser.Parse(value));
}
