using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Raffica.Pdf.Tests;

/// <summary>M2.7 Task 13 step 3: one style for ellipsis, quotes, commas and spelling in the strings of every project (the review findings 07-UI-16 to 07-UI-19 and 08-UI-17).</summary>
public sealed class WordingStyleTests
{
    private static readonly string[] s_projects = ["Raffica.Pdf", "Raffica.Shell", "Raffica.Gallery"];

    private static readonly Regex s_british = new(
        @"\b\w*(colour|centre|behaviour|favour|honour|labour|neighbour|licence|cancell|organis|recognis|metre|grey|catalogue|dialogue|artefact|analyse|judgement|whilst|programme|travell|labell|modell|initialis|customis|minimis|maximis|normalis|synchronis|serialis|summaris|utilis|visualis|finalis|authoris|categoris|optimis|realis|prioritis|sanitis|standardis|localis)\w*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<(string Project, string Name, string Value)> Values(string language) =>
        s_projects
            .SelectMany(project => XDocument.Load(RepoPaths.Combine("src", project, "Resources", $"Strings.{language}.resx"))
                .Root!.Elements("data")
                .Select(d => (Project: project, Name: (string)d.Attribute("name")!, Value: (string?)d.Element("value") ?? string.Empty)))
            .ToList();

    private static string Value(string language, string key) =>
        Values(language).Single(v => v.Project == "Raffica.Pdf" && v.Name == key).Value;

    private static void AssertNone(IEnumerable<(string Project, string Name, string Value)> hits, string rule)
    {
        var lines = hits.Select(h => $"{h.Project} {h.Name}: {h.Value}").ToList();
        Assert.True(lines.Count == 0, rule + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheEllipsisIsOneCharacterDirectlyAfterTheWord(string language) =>
        AssertNone(Values(language).Where(v => Regex.IsMatch(v.Value, @"\s…|\.\.\.")), "The ellipsis is \"…\" (U+2026) directly after the word, never \" …\" or \"...\":");

    [Fact]
    public void EnglishUsesUsSpelling() =>
        AssertNone(Values("en").Where(v => s_british.IsMatch(v.Value)), "English strings use US spelling (color, canceling, meters):");

    [Fact]
    public void EnglishQuotesAreStraightAndGermanQuotesAreLowHigh()
    {
        AssertNone(Values("en").Where(v => Regex.IsMatch(v.Value, "[„“”‚‘’]")), "English strings quote with straight double quotes:");
        AssertNone(
            Values("de").Where(v => v.Value.Contains('"', StringComparison.Ordinal) || v.Value.Count(c => c == '„') != v.Value.Count(c => c == '“') || Regex.IsMatch(v.Value, "[”‚‘’]")),
            "German strings quote with „…“ only, every „ closed by “:");
    }

    [Fact]
    public void GermanInfinitiveClausesAreSetOffByAComma() =>
        AssertNone(Values("de").Where(v => Regex.IsMatch(v.Value, @"\w (ohne|um|anstatt|statt) zu\b")), "A German infinitive clause with \"ohne zu\" or \"um zu\" follows a comma:");

    [Theory]
    [InlineData("UnsavedLockedTitle", "Schließen, ohne zu speichern?")]
    [InlineData("SettingsPageFollowTheme", "Wie die Darstellung")]
    public void GermanFixedTexts(string key, string expected) => Assert.Equal(expected, Value("de", key));

    [Fact]
    public void TheNotUndoableNoteSetsTheInfinitiveClauseOffWithCommas() =>
        Assert.Contains("schließen Sie, ohne zu speichern, um ihn zu verwerfen", Value("de", "OcrReportNotUndoable"), StringComparison.Ordinal);

    [Theory]
    [InlineData("UpdateManualMessage")]
    [InlineData("UpdateFailureNoInstaller")]
    [InlineData("UpdateFailureFolderWriteProtectedFormat")]
    [InlineData("UpdateFailureHelperInstallFailed")]
    public void TheDownloadInstructionIsAVerbWithItsParticle(string key)
    {
        var text = Value("de", key);
        Assert.Contains("Download-Seite herunter", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Laden Sie die neue Version von der Download-Seite.", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ApplyWarning", "\"Save as\"", "„Speichern unter“")]
    [InlineData("UnsavedPlanMessageFormat", "\"Apply\" writes", "„Anwenden“ schreibt")]
    [InlineData("UnsavedRedactMessageFormat", "\"Apply\" writes", "„Anwenden“ schreibt")]
    [InlineData("SaveFirstSaveAsMessage", "\"Back\" leaves", "„Zurück“")]
    [InlineData("RedactSaveFirstSaveAsMessage", "\"Back\" leaves", "„Zurück“")]
    [InlineData("OcrReportNotUndoable", "\"Undo\" cannot", "„Rückgängig“")]
    [InlineData("TextEditNoteEmpty", "\"Undo\" brings", "„Rückgängig“")]
    [InlineData("TextEditNoteDone", "\"Undo\" brings", "„Rückgängig“")]
    [InlineData("TextEditNoteWriteFailed", "\"Undo\" goes", "„Rückgängig“")]
    [InlineData("UpdateBusyNote", "\"Install\"", "„Installieren“")]
    [InlineData("OcrFailLanguageMissingFormat", "\"Languages…\"", "„Sprachen…“")]
    [InlineData("OcrFailLanguageMissingNone", "\"Languages…\"", "„Sprachen…“")]
    [InlineData("OcrNoLanguages", "\"Languages…\" on the \"Edit\" tab", "„Sprachen…“ im Reiter „Bearbeiten“")]
    [InlineData("StaysRedactHint", "in the \"Redact\" tab", "im Reiter „Schwärzen“")]
    [InlineData("TextEditOldTextNote", "\"Redact\"", "„Schwärzen“")]
    public void ButtonAndTabNamesAreQuoted(string key, string english, string german)
    {
        Assert.Contains(english, Value("en", key), StringComparison.Ordinal);
        Assert.Contains(german, Value("de", key), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFieldHighlightHasOneNameInBothLanguages()
    {
        Assert.Equal("Highlight fields", Value("en", "FormHighlightToggle"));
        Assert.Equal(Value("en", "FormHighlightToggle"), Value("en", "CmdFormsHighlight"));
        Assert.Equal("Felder hervorheben", Value("de", "FormHighlightToggle"));
        Assert.Equal(Value("de", "FormHighlightToggle"), Value("de", "CmdFormsHighlight"));
    }

    [Theory]
    [InlineData("RedactStaysAfterSave")]
    [InlineData("RedactSaveFirstMessage")]
    [InlineData("RedactSaveFirstSaveAsMessage")]
    public void TheRedactCardsCallTheMarksOfAnnotatingAnnotationsInBothLanguages(string key)
    {
        // On the redact cards "marks" means the pending redaction marks, so the marks of annotating are "annotations".
        var english = Value("en", key);
        Assert.Contains("annotations", english, StringComparison.Ordinal);
        Assert.DoesNotContain("marks are", english, StringComparison.Ordinal);
        Assert.DoesNotContain("marks and", english, StringComparison.Ordinal);
        var german = Value("de", key);
        Assert.Contains("Anmerkungen", german, StringComparison.Ordinal);
        Assert.DoesNotContain("Markierungen", german, StringComparison.Ordinal);
    }
}
