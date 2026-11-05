using System.Globalization;
using System.Text.RegularExpressions;
using Raffica.Core.Forms;
using Raffica.Pdf.Forms;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FormNoticesTests
{
    private static readonly string[] Keys =
    [
        "FormNoticeScripts", "FormNoticeXfa", "FormNoticeSignature", "FormNoticeButtons", "FormNoticeDismiss",
        "FormRequiredOne", "FormRequiredManyFormat", "FormHighlightToggle",
    ];

    [Fact]
    public void EachFlagGivesItsOwnSentenceAndNothingElse()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Empty(FormNotices.LinesFor(FormHint.None));
        Assert.Equal(["Calculations, formats and checks in this form need JavaScript and are not run."], FormNotices.LinesFor(FormHint.Calculated));
        Assert.Equal(["Calculations, formats and checks in this form need JavaScript and are not run."], FormNotices.LinesFor(FormHint.FormatScripts));
        Assert.Equal(["This is an XFA form; Raffica PDF does not support XFA forms."], FormNotices.LinesFor(FormHint.Xfa));
        Assert.Equal(["Digital signature fields are not supported."], FormNotices.LinesFor(FormHint.SignatureField));
        Assert.Equal(["Buttons in this form never run an action."], FormNotices.LinesFor(FormHint.Buttons));
    }

    [Fact]
    public void CalculatedAndFormatScriptsTogetherShowTheSentenceOnce()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var lines = FormNotices.LinesFor(FormHint.Calculated | FormHint.FormatScripts | FormHint.Buttons);
        Assert.Equal(2, lines.Count);
        Assert.Single(lines, l => l.Contains("JavaScript", StringComparison.Ordinal));
    }

    [Fact]
    public void GermanTextsAreTranslatedNotCopied()
    {
        var all = FormHint.Calculated | FormHint.Xfa | FormHint.SignatureField | FormHint.Buttons;
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        var english = FormNotices.LinesFor(all);
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        var german = FormNotices.LinesFor(all);
        PdfStrings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Equal(english.Count, german.Count);
        Assert.All(english.Zip(german), pair => Assert.NotEqual(pair.First, pair.Second));
    }

    [Fact]
    public void NoTextSaysValidOrLegallyBinding()
    {
        var banned = new Regex(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var culture in new[] { "en", "de" })
        {
            var info = CultureInfo.GetCultureInfo(culture);
            Assert.All(Keys, k => Assert.False(banned.IsMatch(PdfStrings.GetIn(k, info)), k + " " + culture));
        }
    }
}
