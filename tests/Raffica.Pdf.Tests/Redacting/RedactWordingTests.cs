using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Printing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>
/// The wording of the redaction: every Core enum that reaches a card has a text per member in both languages (a member without one fails here), the cards never
/// overpromise (no "safe", "complete", "guaranteed", no signature that is "valid"), and no text of the result contains the redacted text or a value of the document.
/// </summary>
public sealed class RedactWordingTests
{
    private const string Term = "Zephyrstraße 17";

    private static readonly string[] Languages = ["en", "de"];

    // Whole words only: "incomplete" and "Sicherungskopie" (backup copy) are no claims; "complete", "sicher" and "vollständig" are.
    private static readonly Regex Overpromise = new(
        @"\b(safe|safely|safer|secure|secured|guaranteed|guarantee|all found|error-free|complete|completely|fully|permanently|valid|legally binding"
        + @"|sicher|sichere|sicheres|sicherer|sicherlich|garantiert|garantie|vollständig|vollständige|vollständigen|vollständiger|vollständiges|vollständigkeit"
        + @"|komplett|fehlerfrei|lückenlos|restlos|gültig|rechtsverbindlich)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<string> InEachLanguage(Func<string> text)
    {
        var before = PdfStrings.Culture;
        try
        {
            foreach (var language in Languages)
            {
                PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
                yield return text();
            }
        }
        finally
        {
            PdfStrings.Culture = before;
        }
    }

    // Every member of the enum worded in both languages: a text, nothing left to fill, a different text for every member.
    private static void AssertEveryMember<T>(Func<T, string> word)
        where T : struct, Enum
    {
        var members = Enum.GetValues<T>();
        for (var language = 0; language < Languages.Length; language++)
        {
            var texts = members.Select(member => InEachLanguage(() => word(member)).ElementAt(language)).ToList();
            Assert.All(texts.Zip(members), pair =>
            {
                Assert.False(string.IsNullOrWhiteSpace(pair.First), $"{typeof(T).Name}.{pair.Second} in {Languages[language]}");
                Assert.DoesNotMatch(@"\{\d\}", pair.First);
                Assert.DoesNotMatch(Overpromise, pair.First);
            });
            Assert.Equal(texts.Count, texts.Distinct().Count());
        }

        // A value that is no member has no wording (so a member added later is found by the loop above, never worded as "something").
        Assert.Throws<ArgumentOutOfRangeException>(() => word((T)(object)999));
    }

    [AvaloniaFact]
    public void EveryEnumMemberHasWordingInBothLanguages()
    {
        AssertEveryMember<RedactionRefusalKind>(kind => RedactWording.Text(kind, [2, 4]));
        AssertEveryMember<RedactionRefusalKind>(kind => RedactWording.Text(kind));
        AssertEveryMember<RedactionFallbackKind>(kind => RedactWording.Text(kind));
        AssertEveryMember<RedactionLossKind>(kind => RedactWording.Text(kind, 3));
        AssertEveryMember<CleanupKind>(kind => RedactWording.Text(kind, "3"));
        AssertEveryMember<ImageSolidReason>(reason => RedactWording.Text(reason));
        AssertEveryMember<RedactionNotCheckedKind>(kind => RedactWording.Text(kind, new RedactionNotChecked(kind, "English line of Core", 3, 4, "a filter name", 12)));
        AssertEveryMember<RedactionFailureKind>(kind => RedactWording.Text(kind, [1]));
        AssertEveryMember<RedactionFindingKind>(kind => RedactWording.Text(kind, [1]));

        // The unverified page keys are their own sentence (no pages to name).
        Assert.NotEqual(RedactWording.Text(RedactionRefusalKind.UnremovablePageKeys, [1]), RedactWording.Text(RedactionRefusalKind.UnremovablePageKeys, [], unverified: true));
    }

    [AvaloniaFact]
    public void NothingOfTheDocumentIsInTheWords()
    {
        // The value of an information field is never shown, only its name; the technical detail and the English line of Core are never shown.
        var secret = "Quellmann Zephyrstraße 17";
        foreach (var kind in new[] { CleanupKind.Author, CleanupKind.Title, CleanupKind.Subject, CleanupKind.Keywords, CleanupKind.Creator, CleanupKind.Producer })
        {
            Assert.DoesNotContain("Quellmann", RedactWording.Text(new CleanupItem(kind, secret)), StringComparison.Ordinal);
            Assert.Equal(RedactWording.Text(kind), RedactWording.Text(new CleanupItem(kind, secret)));
        }

        foreach (var kind in Enum.GetValues<RedactionNotCheckedKind>())
        {
            var text = RedactWording.Text(new RedactionNotChecked(kind, "English " + secret, 1, 2, "detail " + secret));
            Assert.DoesNotContain("Zephyr", text, StringComparison.Ordinal);
            Assert.DoesNotContain("English", text, StringComparison.Ordinal);
        }

        foreach (var kind in Enum.GetValues<RedactionFindingKind>())
        {
            Assert.DoesNotContain("Zephyr", RedactWording.Text(new RedactionFinding(kind, 0, "detail " + secret)), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public void PagesAreNamedOneBasedAndInTheLanguage()
    {
        Assert.Equal("page 3", InEachLanguage(() => RedactWording.Pages([2])).First());
        Assert.Equal("pages 3 and 5", InEachLanguage(() => RedactWording.Pages([4, 2])).First());
        Assert.Equal("the file", InEachLanguage(() => RedactWording.Pages([])).First());
        Assert.Equal("Seite 3", InEachLanguage(() => RedactWording.Pages([2])).Last());
        Assert.Equal("den Seiten 3 und 5", InEachLanguage(() => RedactWording.Pages([2, 4])).Last());
        Assert.Equal("der Datei", InEachLanguage(() => RedactWording.Pages(null)).Last());
    }

    [AvaloniaFact]
    public void NoUiTextOverpromisesRemoval()
    {
        // Found by name, so a new string of the redaction is covered without a list to keep: the redaction, the search, the recognition and the text editing.
        string[] prefixes = ["Redact", "Find", "Ocr", "Edit", "TextEdit", "CmdRedact", "CmdOcr", "CmdEdit", "PaneRedact", "UnsavedRedact", "UnsavedOcr", "ModeRedact", "SaveFailRedaction"];
        var keys = typeof(PdfStrings).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && prefixes.Any(prefix => p.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(p => p.Name)
            .ToList();
        Assert.True(keys.Count > 250, keys.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var language in Languages)
        {
            var info = CultureInfo.GetCultureInfo(language);
            foreach (var key in keys)
            {
                var text = PdfStrings.GetIn(key, info);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{key} in {language}");

                // The one allowed use of "error-free" is the negated sentence of the recognition card: "Recognition is not error-free" (and its German pair). Nothing else is let through.
                if (key == "OcrAccuracyNote")
                {
                    var allowed = language == "de" ? "Die Erkennung ist nicht fehlerfrei" : "Recognition is not error-free";
                    Assert.Contains(allowed, text, StringComparison.Ordinal);
                    text = text.Replace(allowed, string.Empty, StringComparison.Ordinal);
                }

                Assert.False(Overpromise.IsMatch(text), $"{key} in {language}: {text}");
            }
        }

        // The pattern flags the claims and lets the honest wording through.
        Assert.Matches(Overpromise, "The text is safely removed.");
        Assert.Matches(Overpromise, "All found.");
        Assert.Matches(Overpromise, "Die Schwärzung ist vollständig.");
        Assert.Matches(Overpromise, "Die Datei ist sicher.");
        Assert.DoesNotMatch(Overpromise, "The search could not read every page; the result is incomplete.");
        Assert.DoesNotMatch(Overpromise, "Es wird keine Sicherungskopie angelegt.");
    }

    // The three cards of a redaction built from a real analysis and check, so the texts are the ones a user sees.
    private static async Task<(RedactionPlan Plan, RedactionAnalysis Analysis, RedactionCheck Check)> RealAsync()
    {
        using var rig = await RedactApplyRig.OpenAsync("redact-everywhere-nopattern.pdf");
        await rig.MarkTermAsync(Term);
        rig.MarkArea(0, new PageRect(248, 40, 290, 62));
        var plan = rig.Session.Plan;
        var engine = new Raffica.Core.Pdf.PdfEngine();
        var analysis = await new RedactionExecutor(engine).AnalyzeAsync(plan, rig.Path, null, TestContext.Current.CancellationToken);
        Assert.False(analysis.IsRefused);
        var target = Path.Combine(rig.Folder, "out.pdf");
        var result = await RedactionApplier.ApplyAsync(rig.Tab.Session, engine, new RedactionApplyRequest(plan, target), null, TestContext.Current.CancellationToken);
        return (plan, analysis, result.Check);
    }

    [AvaloniaFact]
    public async Task ResultCardNeverContainsTheTerm()
    {
        var (plan, analysis, check) = await RealAsync();
        Assert.True(check.IsClean);
        foreach (var language in Languages)
        {
            var before = PdfStrings.Culture;
            PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
            try
            {
                using var host = new PrintHost();
                var cards = new[]
                {
                    RedactResultCard.Create("out.pdf", analysis, check, () => { }),
                    RedactApplyCard.Create("in.pdf", plan, analysis, [], ApplyTarget.Choose, _ => { }),
                };
                foreach (var card in cards)
                {
                    host.Host.Show(card);
                    PrintHost.Pump();
                    var texts = RedactUiRig.TextsOf(card);
                    var names = RedactUiRig.NamesOf(card);
                    var all = string.Join('\n', texts.Concat(names));
                    foreach (var secret in new[] { "Zephyr", "Quellmann", "straße 17" })
                    {
                        Assert.DoesNotContain(secret, all, StringComparison.Ordinal);
                    }

                    Assert.All(texts, text => Assert.False(Overpromise.IsMatch(text), $"{language}: {text}"));
                    host.Host.Close();
                    PrintHost.Pump();
                }
            }
            finally
            {
                PdfStrings.Culture = before;
            }
        }

        // The result card has its Checked section (the check is clean) and lists every line of what was not checked in words of its own.
        var result = RedactResultCard.Create("out.pdf", analysis, check, () => { });
        using var shown = new PrintHost();
        shown.Host.Show(result);
        PrintHost.Pump();
        var names2 = RedactUiRig.NamesOf(result);
        Assert.Contains("PART_Checked", names2);
        Assert.Contains("PART_NotChecked", names2);
        Assert.Equal(check.Items.Count, RedactReportLines.NotChecked(analysis, check).Count - (analysis.MarksWithoutTerm > 0 ? 1 : 0) - (analysis.CleanupComplete ? 0 : 1));
    }

    [AvaloniaFact]
    public async Task ACheckWithFindingsShowsNoCheckedAndNoCleanedSection()
    {
        var (_, analysis, check) = await RealAsync();
        var failed = new RedactionCheck([new RedactionFinding(RedactionFindingKind.TextStillPresent, 0, "detail")], check.NotChecked, check.NotCheckedItems);

        var card = RedactResultCard.Create("out.pdf", analysis, failed, () => { });
        using var host = new PrintHost();
        host.Host.Show(card);
        PrintHost.Pump();
        var names = RedactUiRig.NamesOf(card);

        // A check with findings never says "checked" or "cleaned": the report claims only what the read-back confirmed.
        Assert.DoesNotContain("PART_Checked", names);
        Assert.DoesNotContain("PART_Cleaned", names);
        Assert.Contains("PART_NotChecked", names);
    }
}
