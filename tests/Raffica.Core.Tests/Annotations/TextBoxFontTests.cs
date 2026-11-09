using Raffica.Core.Annotations;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdfium;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Core.Tests.Annotations;

/// <summary>Where the font of the text boxes and the overlay text comes from: one copy, in Core, read without any help of the app (milestone 14.1, task 14).</summary>
public sealed class TextBoxFontTests
{
    [Fact]
    public async Task CliCanLayOutWithoutTheApp()
    {
        // Nothing hands the font over in this process (the test helper that did is gone): the command line is in the same position.
        Assert.True(TextBoxFont.IsAvailable);

        var metrics = await PdfiumThread.Shared.InvokeAsync(
            () => TextBoxFont.MetricsOf(TextBoxFont.Table(PdfiumThread.Shared)),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1.005, metrics.Ascent, 3);
        Assert.InRange(metrics.AdvanceOf('H'), 0.6, 0.8);

        // The overlay layout runs on these metrics and keeps inside the area.
        var area = new PageRect(0, 0, 120, 24);
        var placement = RedactionOverlayLayout.Fit("geschwärzt", area, new RedactionOverlayStyle(), 12, metrics);
        Assert.True(placement.Fits);
        var line = Assert.Single(placement.Lines);
        Assert.True(line.X >= 0 && line.X + line.Width <= area.Right);
        Assert.True(line.Baseline - (metrics.Ascent * placement.FontSize) >= 0);
        Assert.True(line.Baseline + (metrics.Descent * placement.FontSize) <= area.Bottom);
    }

    [Fact]
    public void TheEmbeddedCopyIsTheShippedGeistRegular()
    {
        var embedded = TextBoxFont.ReadData();
        var file = File.ReadAllBytes(TestData.GeistRegular);

        Assert.Equal(file, embedded);
        Assert.StartsWith("src/Raffica.Core/", Path.GetRelativePath(TestData.Root, TestData.GeistRegular).Replace('\\', '/'), StringComparison.Ordinal);
    }
}
