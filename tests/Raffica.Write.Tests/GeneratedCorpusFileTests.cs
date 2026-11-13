using DocumentFormat.OpenXml.Packaging;
using Raffica.Write.Docx;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests;

public sealed class GeneratedCorpusFileTests
{
    [Fact]
    public async Task TheRightToLeftFileIsAValidPackageWithTheRtlFeature()
    {
        using var folder = new TempFolder();
        var path = folder.File("rtl.docx");
        DocxBuilder.RightToLeft(path);
        using (var package = WordprocessingDocument.Open(path, false))
        {
            Assert.NotNull(package.MainDocumentPart?.Document?.Body);
        }

        var features = FeatureDetector.Detect(path);
        Assert.Contains("rtl-text", features);
        Assert.Contains("paragraphs-runs", features);
        Assert.Contains("tables", features);
        using var file = DocxFile.Open(path);
        await file.SaveAsync(folder.File("saved.docx"), TestContext.Current.CancellationToken);
        var verdict = PackageVerifier.Compare(path, folder.File("saved.docx"));
        Assert.True(verdict.UnloadedIdentical && verdict.LoadedEquivalent);
    }

    // Writes the committed corpus file; run by hand with RAFFICA_WRITE_RTL=<path> (never in a normal run).
    [Fact]
    public void WriteTheRightToLeftFileOnRequest()
    {
        if (Environment.GetEnvironmentVariable("RAFFICA_WRITE_RTL") is { Length: > 0 } target)
        {
            DocxBuilder.RightToLeft(target);
        }
    }
}
