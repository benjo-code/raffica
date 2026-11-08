using Raffica.Pdfium;
using Xunit;

namespace Raffica.Core.Tests.Pdfium;

/// <summary>The size of the picture the text-edit check draws of a page, before and after the rewrite.</summary>
public sealed class PdfiumTextEditCheckSizeTests
{
    [Fact]
    public void AnOrdinaryPageIsScaledToTheBudget()
    {
        var size = PdfiumTextEdit.CheckPixels(612, 792);

        Assert.InRange((long)size.Width * size.Height, 1, 4_100_000);
        Assert.True(size.Scale is > 0 and <= 1);
    }

    [Theory]
    [InlineData(1073741888.0, 0.005)]
    [InlineData(0.005, 1073741888.0)]
    [InlineData(5e9, 0.001)]
    [InlineData(double.PositiveInfinity, 10.0)]
    [InlineData(double.NaN, 10.0)]
    public void APageWhoseCheckPictureWouldBeHugeIsRefused(double width, double height)
    {
        // A thin page keeps a side of one pixel at the full length, so the area rule alone allows a picture of a billion pixels; the buffer of 4 bytes
        // per pixel would overflow an int and exceed the render limit.
        var thrown = Assert.Throws<PdfiumTextEditException>(() => PdfiumTextEdit.CheckPixels(width, height));

        Assert.Equal(PdfiumTextEditFailure.NotRewritable, thrown.Failure);
    }
}
