using Raffica.Core.Ocr;
using Raffica.Ocr;
using Xunit;

namespace Raffica.Core.Tests.Ocr;

public sealed class OcrFailureTests
{
    [Fact]
    public void EveryEngineErrorMapsToAFailureKind()
    {
        foreach (var error in Enum.GetValues<OcrEngineError>())
        {
            var failure = OcrFailure.From(new OcrEngineException(error, "log text"));
            Assert.NotNull(failure);
            Assert.True(Enum.IsDefined(failure.Kind), error.ToString());
            Assert.Empty(failure.Languages);
        }

        Assert.Equal(OcrFailureKind.LibraryMissing, OcrFailure.From(new OcrEngineException(OcrEngineError.LibraryMissing, "x"))!.Kind);
        Assert.Equal(OcrFailureKind.LibraryUnusable, OcrFailure.From(new OcrEngineException(OcrEngineError.LibraryUnusable, "x"))!.Kind);
        Assert.Equal(OcrFailureKind.LanguageInvalid, OcrFailure.From(new OcrEngineException(OcrEngineError.LanguageInvalid, "x"))!.Kind);
        Assert.Equal(OcrFailureKind.LanguageMissing, OcrFailure.From(new OcrEngineException(OcrEngineError.LanguageMissing, "x"))!.Kind);
        Assert.Equal(OcrFailureKind.DataCorrupt, OcrFailure.From(new OcrEngineException(OcrEngineError.DataCorrupt, "x"))!.Kind);
        Assert.Equal(OcrFailureKind.Failed, OcrFailure.From(new OcrEngineException(OcrEngineError.Timeout, "x"))!.Kind);
        Assert.Equal(OcrFailureKind.Failed, OcrFailure.From(new OcrEngineException(OcrEngineError.Failed, "x"))!.Kind);
    }

    [Fact]
    public void MissingLanguageFilesNameTheLanguages()
    {
        var failure = OcrFailure.From(new TessdataMissingException(["deu", "fra"], ["eng"]));
        Assert.NotNull(failure);
        Assert.Equal(OcrFailureKind.LanguageMissing, failure.Kind);
        Assert.Equal(["deu", "fra"], failure.Languages);
    }

    [Fact]
    public void AnyOtherExceptionIsNoFailureOfTheRun()
    {
        Assert.Null(OcrFailure.From(new InvalidOperationException("x")));
        Assert.Null(OcrFailure.From(new OperationCanceledException()));
    }
}
