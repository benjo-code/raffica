using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Raffica.Core.Updates;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class UpdateWordingTests
{
    private static readonly string[] Languages = ["en", "de"];

    // Whole words only. The update UI says what was checked (signature, size, SHA-256), never that an update is "safe".
    private static readonly Regex Overpromise = new(
        @"\b(safe|safely|safer|secure|secured|securely|guaranteed|guarantee|trusted|trustworthy|risk-free|virus-free"
        + @"|sicher|sichere|sicheres|sicherer|sicherlich|sicherheit|garantiert|garantie|vertrauenswürdig|risikofrei|virenfrei)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<string> Failures() => typeof(UpdateFailure)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void EveryUpdateFailureHasWordingInBothLanguages()
    {
        var codes = Failures().ToList();

        // Core has eight codes today; the test fails when one is added without wording (KeyOf throws for a code it does not know).
        Assert.True(codes.Count >= 8, codes.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var code in codes)
        {
            var texts = Languages.Select(language => UpdateWording.ForFailure(code, CultureInfo.GetCultureInfo(language))).ToList();
            Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text), code));
            Assert.NotEqual(texts[0], texts[1]);
            Assert.All(texts, text => Assert.DoesNotContain(code, text, StringComparison.OrdinalIgnoreCase));
            Assert.All(texts, text => Assert.DoesNotContain("{", text, StringComparison.Ordinal));
        }

        // A code nobody worded is refused loudly, not shown as its raw text.
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdateWording.ForFailure("a-new-failure", PdfStrings.Culture));
    }

    [Fact]
    public void ThePhraseOfTheApplierWinsOverTheSentenceOfTheCode()
    {
        var failed = new UpdateState(UpdateStage.Failed, new UpdateVersion(0, 2, 0), null, 0, UpdateFailure.InstallFailed);

        Assert.Equal("In the words of the applier.", UpdateWording.ForFailure(failed, "In the words of the applier."));
        Assert.Equal(UpdateWording.ForFailure(UpdateFailure.InstallFailed, PdfStrings.Culture), UpdateWording.ForFailure(failed, null));
        Assert.Equal(UpdateWording.ForFailure(UpdateFailure.InstallFailed, PdfStrings.Culture), UpdateWording.ForFailure(failed, "  "));
    }

    [Fact]
    public void NoUiTextOverpromisesUpdates()
    {
        // Found by name, so a new update string is covered without a list to keep.
        string[] prefixes = ["Update", "CmdCheckForUpdates"];
        var keys = typeof(PdfStrings).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && prefixes.Any(prefix => p.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(p => p.Name)
            .ToList();
        Assert.True(keys.Count >= 28, keys.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var language in Languages)
        {
            var info = CultureInfo.GetCultureInfo(language);
            foreach (var key in keys)
            {
                var text = PdfStrings.GetIn(key, info);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{key} in {language}");
                Assert.False(Overpromise.IsMatch(text), $"{key} in {language}: {text}");
            }
        }

        // The pattern flags the claims and lets the honest wording through.
        Assert.Matches(Overpromise, "This update is safe.");
        Assert.Matches(Overpromise, "Sicher aktualisieren");
        Assert.DoesNotMatch(Overpromise, PdfStrings.GetIn("UpdateCheckedNote", CultureInfo.GetCultureInfo("en")));

        // The offer says what is checked.
        var note = PdfStrings.GetIn("UpdateCheckedNote", CultureInfo.GetCultureInfo("en"));
        Assert.Contains("signature", note, StringComparison.Ordinal);
        Assert.Contains("SHA-256", note, StringComparison.Ordinal);
        Assert.Contains("size", note, StringComparison.Ordinal);
    }
}
