using System.Globalization;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

/// <summary>The texts of the cards for a mark that could not be added are true when some of the marks (earlier pages of a selection) were made.</summary>
public sealed class FailureWordingTests
{
    [Theory]
    [InlineData("en", "Nothing was changed", "Nothing was added", "undone")]
    [InlineData("de", "Es wurde nichts geändert", "Es wurde nichts hinzugefügt", "rückgängig")]
    public void TheCardsDoNotClaimThatNothingWasMade(string language, string falseChanged, string falseAdded, string undoWord)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var failed = PdfStrings.GetIn("AnnotateFailed", culture);
        var limit = PdfStrings.GetIn("AnnotateLimitReached", culture);

        Assert.DoesNotContain(falseChanged, failed, StringComparison.Ordinal);
        Assert.DoesNotContain(falseAdded, limit, StringComparison.Ordinal);

        // They say what happened to the marks made before the failure, and that these can be undone.
        Assert.Contains(undoWord, failed, StringComparison.Ordinal);
        Assert.Contains(undoWord, limit, StringComparison.Ordinal);

        // The texts are generic over the tools: no "markup" for a drawing or a text box.
        Assert.DoesNotContain("markup", failed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("markup", limit, StringComparison.OrdinalIgnoreCase);
    }
}
