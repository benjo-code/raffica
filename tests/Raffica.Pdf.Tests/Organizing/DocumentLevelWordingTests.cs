using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Raffica.Core.Pages;
using Raffica.Pdf.Organizing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>The lines of what every part of an extract or split keeps: one per item that is there, singular and plural in both languages, never a claim of removal or safety.</summary>
public sealed class DocumentLevelWordingTests
{
    private static readonly Regex Claim = new(@"\b(remov\w*|safe\w*|guarantee\w*|secure\w*|gelöscht|entfernt\w*|sicher\w*|garantier\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<T> InEachLanguage<T>(Func<T> read)
    {
        var before = PdfStrings.Culture;
        try
        {
            foreach (var language in new[] { "en", "de" })
            {
                PdfStrings.Culture = CultureInfo.GetCultureInfo(language);
                yield return read();
            }
        }
        finally
        {
            PdfStrings.Culture = before;
        }
    }

    [AvaloniaFact]
    public void NothingGivesNoLines()
    {
        Assert.Empty(DocumentLevelWording.Lines(DocumentLevelContent.None));
    }

    [AvaloniaFact]
    public void OneLinePerItemWithSingularAndPluralInBothLanguages()
    {
        var one = InEachLanguage(() => DocumentLevelWording.Lines(new DocumentLevelContent(1, true, 1, 1))).ToList();
        var many = InEachLanguage(() => DocumentLevelWording.Lines(new DocumentLevelContent(2, true, 3, 4))).ToList();

        Assert.Equal(["Up to 1 attached file", "Possibly scripts (JavaScript) in the document", "1 form field from another page", "1 bookmark that points to another page"], one[0]);
        Assert.Equal(["Up to 2 attached files", "Possibly scripts (JavaScript) in the document", "3 form fields from other pages", "4 bookmarks that point to other pages"], many[0]);
        Assert.Equal(["Bis zu 1 angehängte Datei", "Möglicherweise Skripte (JavaScript) im Dokument", "1 Formularfeld von einer anderen Seite", "1 Lesezeichen, das auf eine andere Seite zeigt"], one[1]);
        Assert.Equal(["Bis zu 2 angehängte Dateien", "Möglicherweise Skripte (JavaScript) im Dokument", "3 Formularfelder von anderen Seiten", "4 Lesezeichen, die auf andere Seiten zeigen"], many[1]);
    }

    [AvaloniaFact]
    public void AnIncompleteReadAddsOneNoteLine()
    {
        var lines = InEachLanguage(() => DocumentLevelWording.Lines(new DocumentLevelContent(0, false, 0, 0, ReadCompletely: false))).ToList();

        Assert.Equal(["The file could not be read completely, so more may stay than is listed."], lines[0]);
        Assert.Single(lines[1]);
    }

    [AvaloniaFact]
    public void ZeroItemsAreLeftOut()
    {
        var lines = DocumentLevelWording.Lines(new DocumentLevelContent(0, false, 2, 0));

        Assert.Single(lines);
        Assert.Contains("2", lines[0], StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void NoTextClaimsRemovalOrSafety()
    {
        string[] keys = ["StaysHeadingExtract", "StaysHeadingSplit", "StaysAttachedOne", "StaysAttachedManyFormat", "StaysScripts", "StaysIncomplete", "StaysFieldsOne", "StaysFieldsManyFormat", "StaysBookmarksOne", "StaysBookmarksManyFormat", "StaysRedactHint"];
        foreach (var language in new[] { "en", "de" })
        {
            foreach (var key in keys)
            {
                var text = PdfStrings.GetIn(key, CultureInfo.GetCultureInfo(language));
                Assert.False(string.IsNullOrWhiteSpace(text), $"{key} in {language}");
                Assert.DoesNotMatch(Claim, text);
            }
        }
    }
}
