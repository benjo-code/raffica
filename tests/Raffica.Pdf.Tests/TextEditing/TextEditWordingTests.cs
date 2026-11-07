using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Raffica.Core.TextEdit;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>The wording of the text editing: every Core enum that reaches the user has a text per member in both languages, and no text claims that the old text is gone or that anything is safe.</summary>
public sealed class TextEditWordingTests
{
    private static readonly string[] Languages = ["en", "de"];

    // "remove" (the way to remove the old text is Redact) is no claim; "removed" and "entfernt" about a text are, as are "safe" and the like.
    private static readonly Regex Claim = new(
        @"\b(removed|deleted|erased|gone|safe|safely|safer|secure|secured|guaranteed|guarantee|all found|error-free|correct|correctly|complete|completely|fully|permanently|valid|legally binding"
        + @"|entfernt|gelöscht|getilgt|weg|sicher|sichere|sicheres|sicherer|sicherlich|garantiert|fehlerfrei|korrekt|richtig|vollständig|vollständige|komplett|gültig|rechtsverbindlich)\b",
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

    // Every member worded in both languages: a text, nothing left to fill, a different text per member; a value that is no member has no wording.
    internal static void AssertEveryMember<T>(Func<T, string> word, params T[] except)
        where T : struct, Enum
    {
        var members = Enum.GetValues<T>().Where(m => !except.Contains(m)).ToArray();
        for (var language = 0; language < Languages.Length; language++)
        {
            var texts = members.Select(member => InEachLanguage(() => word(member)).ElementAt(language)).ToList();
            Assert.All(texts.Zip(members), pair =>
            {
                Assert.False(string.IsNullOrWhiteSpace(pair.First), $"{typeof(T).Name}.{pair.Second} in {Languages[language]}");
                Assert.DoesNotMatch(@"\{\d\}", pair.First);
                Assert.DoesNotMatch(Claim, pair.First);
            });
            Assert.Equal(texts.Count, texts.Distinct().Count());
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => word((T)(object)999));
    }

    private static TextEditPreview PreviewOf(TextEditOutcome outcome, TextEditOverflow overflow = TextEditOverflow.None) => new(
        "x",
        false,
        new TextEditDecision(outcome, [], ["ä"], ["→"], false, outcome == TextEditOutcome.Rejected ? TextEditRejection.UnsupportedCharacters : TextEditRejection.None),
        overflow);

    [AvaloniaFact]
    public void EveryDecisionOutcomeHasWordingOrSaysNothingOnPurpose()
    {
        // KeepFont says nothing (nothing to tell); every other outcome has a text of its own in both languages.
        Assert.Null(TextEditWording.Note(PreviewOf(TextEditOutcome.KeepFont)));
        AssertEveryMember<TextEditOutcome>(outcome => TextEditWording.Note(PreviewOf(outcome)) ?? "kept", TextEditOutcome.KeepFont);
        Assert.Throws<ArgumentOutOfRangeException>(() => TextEditWording.Note(PreviewOf((TextEditOutcome)99)));

        // What the longer line may run into is told besides, with its own words.
        foreach (var text in InEachLanguage(() => TextEditWording.Note(PreviewOf(TextEditOutcome.KeepFont, TextEditOverflow.OverlapsText | TextEditOverflow.OutsidePage))!))
        {
            Assert.Contains(' ', text);
        }

        Assert.Equal(
            "The new text may reach over other text. The new text may reach beyond the page edge.",
            InEachLanguage(() => TextEditWording.Note(PreviewOf(TextEditOutcome.KeepFont, TextEditOverflow.OverlapsText | TextEditOverflow.OutsidePage))!).First());
    }

    [AvaloniaFact]
    public void EveryRejectionHasWordingAndNamesTheCharacters()
    {
        AssertEveryMember<TextEditRejection>(rejection => TextEditWording.Rejection(rejection, ["→"]), TextEditRejection.None);
        AssertEveryMember<TextEditRejection>(rejection => TextEditWording.Rejection(rejection), TextEditRejection.None);
        Assert.Throws<ArgumentOutOfRangeException>(() => TextEditWording.Rejection(TextEditRejection.None));

        // The characters are the user's own text, up to eight, and a control character is named by its code.
        var many = Enumerable.Range('a', 12).Select(c => ((char)c).ToString()).ToList();
        var named = TextEditWording.Rejection(TextEditRejection.UnsupportedCharacters, many);
        Assert.Contains("a, b, c, d, e, f, g, h …", named, StringComparison.Ordinal);
        Assert.DoesNotContain("i,", named, StringComparison.Ordinal);
        Assert.Contains("U+000A", TextEditWording.Rejection(TextEditRejection.UnsupportedCharacters, ["\n"]), StringComparison.Ordinal);
        Assert.Contains("2000", InEachLanguage(() => TextEditWording.Rejection(TextEditRejection.TooLong)).First(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void NoTextSaysRemovedOrSafe()
    {
        // Found by name, so a new string of the text editing is covered without a list to keep.
        string[] prefixes = ["TextEdit", "CmdTextEdit", "ModeTextEdit"];
        var keys = typeof(PdfStrings).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && prefixes.Any(prefix => p.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(p => p.Name)
            .ToList();
        Assert.True(keys.Count > 35, keys.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var language in Languages)
        {
            var info = CultureInfo.GetCultureInfo(language);
            foreach (var key in keys)
            {
                var text = PdfStrings.GetIn(key, info);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{key} in {language}");
                Assert.False(Claim.IsMatch(text), $"{key} in {language}: {text}");
            }
        }

        // The note about the old text says where it stays and how to remove it for real, and never that it is gone.
        Assert.Equal(
            "The old text stays in earlier versions of the file. To remove it for real: Redact.",
            PdfStrings.GetIn("TextEditOldTextNote", CultureInfo.GetCultureInfo("en")));
        var german = PdfStrings.GetIn("TextEditOldTextNote", CultureInfo.GetCultureInfo("de"));
        Assert.Contains("früheren Fassungen", german, StringComparison.Ordinal);
        Assert.Contains("Schwärzen", german, StringComparison.Ordinal);

        // The pattern flags the claims and lets the honest wording through.
        Assert.Matches(Claim, "The old text is removed.");
        Assert.Matches(Claim, "Der alte Text ist entfernt.");
        Assert.Matches(Claim, "The change is safe.");
        Assert.DoesNotMatch(Claim, "To remove it for real: Redact.");
        Assert.DoesNotMatch(Claim, "Zum echten Entfernen: Schwärzen.");
    }

    [AvaloniaFact]
    public void NoNoteContainsTheTextOfALineOrCoreEnglish()
    {
        // The words never carry the text of a line: the name of an outline is its place only.
        foreach (var name in InEachLanguage(() => TextEditWording.RunName(3, 2, locked: false)).Concat(InEachLanguage(() => TextEditWording.RunName(3, 2, locked: true))))
        {
            Assert.Matches(@"\b3\b.*\b2\b", name);
        }

        Assert.Equal("Line 3 of page 2", InEachLanguage(() => TextEditWording.RunName(3, 2, locked: false)).First());
        Assert.Equal("Locked line 3 of page 2", InEachLanguage(() => TextEditWording.RunName(3, 2, locked: true)).First());

        // Core's own sentences (the exception messages, the rejection names) are never shown.
        var decision = new TextEditDecision(TextEditOutcome.Rejected, [], [], ["→"], false, TextEditRejection.UnsupportedCharacters);
        var note = TextEditWording.Note(new TextEditPreview("x", false, decision, TextEditOverflow.None))!;
        Assert.DoesNotContain("UnsupportedCharacters", note, StringComparison.Ordinal);
        Assert.DoesNotContain("The new text was refused", note, StringComparison.Ordinal);
    }
}
