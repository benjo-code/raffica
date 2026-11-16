using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Raffica.WriteApp.Tests;

/// <summary>The strings of Write: the same keys in both languages, a typed property for each, and the wording rules of the other projects (<c>WordingStyleTests</c>, <c>GermanRegisterTests</c> of the PDF app).</summary>
public sealed class TranslationTests
{
    private static readonly Regex s_duForm = new(
        @"\b(du|dich|dir|dein\w*)\b|\b(wähle|gib|öffne|beende|verwirf|wende|lass|klicke|drücke|schließe|speichere|prüfe|versuche|gehe|starte|markiere|zeige)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex s_british = new(
        @"\b\w*(colour|centre|behaviour|favour|honour|labour|neighbour|licence|cancell|organis|recognis|metre|grey|catalogue|dialogue|artefact|analyse|judgement|whilst|programme|travell|labell|modell|initialis|customis|minimis|maximis|normalis|synchronis|serialis|summaris|utilis|visualis|finalis|authoris|categoris|optimis|realis|prioritis|sanitis|standardis|localis)\w*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<(string Name, string Value)> Values(string language) =>
        [.. XDocument.Load(RepoPaths.Combine("src", "Raffica.Write", "Resources", $"Strings.{language}.resx"))
            .Root!.Elements("data")
            .Select(d => (Name: (string)d.Attribute("name")!, Value: (string?)d.Element("value") ?? string.Empty))];

    private static string[] Keys(string language) => [.. Values(language).Select(v => v.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public void BothLanguagesHaveTheSameKeys()
    {
        var english = Keys("en");
        Assert.NotEmpty(english);
        Assert.Equal(english, Keys("de"));
        Assert.Equal(english.Length, english.Distinct().Count());
    }

    [Fact]
    public void EveryKeyHasATypedPropertyThatResolvesAndIsFilledInBothLanguages()
    {
        var properties = typeof(WriteStrings).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(string)).ToList();

        // The open error texts are reached through ForOpenError (one key per enum member, pinned by OpenErrorWordingTests), the save failures through ForSaveFailure and
        // ForSaveCheck (pinned by SaveWordingTests).
        var typed = properties.Select(p => p.Name)
            .Concat(Keys("en").Where(k => k.StartsWith("OpenError", StringComparison.Ordinal) || k.StartsWith("SaveFail", StringComparison.Ordinal)))
            .Distinct().Order(StringComparer.Ordinal);
        Assert.Equal(Keys("en"), typed.ToArray());
        Assert.All(properties, p => Assert.False(string.IsNullOrWhiteSpace((string?)p.GetValue(null))));
        Assert.All(Values("de"), v => Assert.False(string.IsNullOrWhiteSpace(v.Value), v.Name));
    }

    [Fact]
    public void AMissingKeyThrows() => Assert.Throws<InvalidOperationException>(() => WriteStrings.Get("NoSuchKey"));

    [Fact]
    public void GermanUsesSieAndNeverDu()
    {
        var hits = Values("de").Where(v => s_duForm.IsMatch(v.Value)).Select(v => $"{v.Name}: {v.Value}").ToList();
        Assert.True(hits.Count == 0, "German texts in the du form (use Sie):" + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheEllipsisIsOneCharacterDirectlyAfterTheWord(string language)
    {
        var hits = Values(language).Where(v => Regex.IsMatch(v.Value, @"\s…|\.\.\.")).Select(v => v.Name).ToList();
        Assert.True(hits.Count == 0, "The ellipsis is \"…\" (U+2026) directly after the word: " + string.Join(", ", hits));
    }

    [Fact]
    public void EnglishUsesUsSpellingAndStraightQuotes()
    {
        Assert.DoesNotContain(Values("en"), v => s_british.IsMatch(v.Value));
        Assert.DoesNotContain(Values("en"), v => Regex.IsMatch(v.Value, "[„“”‚‘’]"));
    }

    [Fact]
    public void GermanQuotesAreLowHighAndInfinitiveClausesHaveACommaBefore()
    {
        Assert.DoesNotContain(Values("de"), v => v.Value.Contains('"', StringComparison.Ordinal) || v.Value.Count(c => c == '„') != v.Value.Count(c => c == '“'));
        Assert.DoesNotContain(Values("de"), v => Regex.IsMatch(v.Value, @"\w (ohne|um|anstatt|statt) zu\b"));
    }

    [Fact]
    public void ATextHasNoSurroundingSpace()
    {
        foreach (var language in new[] { "en", "de" })
        {
            Assert.All(Values(language), v => Assert.Equal(v.Value.Trim(), v.Value));
        }
    }
}
