using System.Globalization;
using System.Text.RegularExpressions;
using Raffica.Core.Pages;
using Raffica.Pdf.Organizing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>The sentences of the Apply card: one per <see cref="PageLossKind"/> in both languages, never "valid" or "legally binding".</summary>
public sealed class PageLossWordingTests
{
    private static readonly Regex Banned = new(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] Languages = ["en", "de"];

    private static IEnumerable<PageLoss> LossesOf(PageLossKind kind)
    {
        yield return new PageLoss(kind, null, null);
        yield return new PageLoss(kind, PageLossDetail.Source(1), [2, 4]);
        yield return new PageLoss(kind, PageLossDetail.DocMdp(2), [0]);
        yield return new PageLoss(kind, PageLossDetail.Duplicated, [0, 1, 2]);
        yield return new PageLoss(kind, PageLossDetail.Inserted, [3]);
        yield return new PageLoss(kind, PageLossDetail.Unchecked, null);
        yield return new PageLoss(kind, PageLossDetail.DeletedPage, [5]);
        yield return new PageLoss(kind, PageLossDetail.CopiedPage, [5, 6]);
        yield return new PageLoss(kind, PageLossDetail.Shifted, null);
        yield return new PageLoss(kind, PageLossDetail.Source(3), null, Blocks: true);
    }

    private static string? Name(int id) => id == 1 ? "other.pdf" : null;


    [Fact]
    public void EqualReportsGiveOneSentenceForAllParts()
    {
        var sentence = PageLossWording.Describe(new PageLoss(PageLossKind.SignatureInvalidated, null, null));
        var report = new PageEditReport([new PageLoss(PageLossKind.SignatureInvalidated, null, null)]);

        var lines = PageLossWording.DescribeParts([report, report, report]);

        Assert.Equal([sentence], lines);
    }

    [Fact]
    public void ASentenceOfSomePartsNamesTheFiles()
    {
        var sentence = PageLossWording.Describe(new PageLoss(PageLossKind.SignatureInvalidated, null, null));
        var some = new PageEditReport([new PageLoss(PageLossKind.SignatureInvalidated, null, null)]);
        var none = new PageEditReport([]);

        var lines = PageLossWording.DescribeParts([some, none, some]);

        Assert.Equal([PdfStrings.Fill(PdfStrings.OutputLossFilesFormat, "1 and 3", sentence)], lines);
    }

    [Fact]
    public void LongListsOfDifferentSentencesAreCappedAtTwentyLines()
    {
        var reports = Enumerable.Range(0, 25)
            .Select(i => new PageEditReport([new PageLoss(PageLossKind.BookmarkToDeletedPage, null, [i])]))
            .ToList();

        var lines = PageLossWording.DescribeParts(reports);

        Assert.Equal(21, lines.Count);
        Assert.Equal(PdfStrings.Fill(PdfStrings.OutputMoreFormat, 5), lines[^1]);
        Assert.All(lines.Take(20), line => Assert.Contains("Files", line, StringComparison.Ordinal));
        Assert.Empty(PageLossWording.DescribeParts([new PageEditReport([]), new PageEditReport([])]));
    }
    [Fact]
    public void EveryLossKindHasAWording()
    {
        // A kind that falls to the general sentence has no wording of its own: a new kind must not go unworded.
        var fallback = Describe("en", new PageLoss((PageLossKind)999, null, null));
        foreach (var language in Languages)
        {
            foreach (var kind in Enum.GetValues<PageLossKind>())
            {
                var text = Describe(language, new PageLoss(kind, null, null));
                Assert.False(string.IsNullOrWhiteSpace(text), $"{kind} in {language}");
                if (language == "en")
                {
                    Assert.NotEqual(fallback, text);
                }
            }
        }
    }

    [Fact]
    public void LossesAreWordedPerKindInBothLanguages()
    {
        foreach (var language in Languages)
        {
            var texts = new HashSet<string>();
            foreach (var kind in Enum.GetValues<PageLossKind>())
            {
                foreach (var loss in LossesOf(kind))
                {
                    var text = Describe(language, loss);
                    Assert.False(string.IsNullOrWhiteSpace(text), $"{kind} {loss.Detail} in {language}");
                    Assert.False(Banned.IsMatch(text), text);
                    Assert.DoesNotContain("{0}", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("{1}", text, StringComparison.Ordinal);
                    texts.Add(text);
                }
            }

            // Different losses are different sentences.
            Assert.True(texts.Count >= 25, language);
        }
    }

    [Fact]
    public void TheSignatureSentenceSaysBrokenAndNamesTheChangeLevel()
    {
        Assert.Equal(
            "This file is digitally signed. Programs that check signatures will report the signature as broken after the pages changed. The signature was made with change level 2.",
            Describe("en", new PageLoss(PageLossKind.SignatureInvalidated, PageLossDetail.DocMdp(2), null)));
        Assert.EndsWith("Änderungsstufe 1 erstellt.", Describe("de", new PageLoss(PageLossKind.SignatureInvalidated, PageLossDetail.DocMdp(1), null)), StringComparison.Ordinal);
        Assert.DoesNotContain("change level", Describe("en", new PageLoss(PageLossKind.SignatureInvalidated, null, null)), StringComparison.Ordinal);
    }

    [Fact]
    public void InsertedFilesAreNamedByTheirDisplayName()
    {
        var text = Describe("en", new PageLoss(PageLossKind.ForeignOutlineDropped, PageLossDetail.Source(1), null));
        Assert.Contains("\"other.pdf\"", text, StringComparison.Ordinal);

        // A file the session does not know is named by its number.
        Assert.Contains("7", Describe("en", new PageLoss(PageLossKind.ForeignOutlineDropped, PageLossDetail.Source(7), null)), StringComparison.Ordinal);
    }

    [Fact]
    public void PageListsAreOneBasedAndShortened()
    {
        using var _ = new Culture("en");
        Assert.Equal("3", PageLossWording.PageList([2]));
        Assert.Equal("3 and 5", PageLossWording.PageList([2, 4]));
        Assert.Equal("3, 5 and 9", PageLossWording.PageList([8, 2, 4, 4]));
        Assert.Equal("1, 2, 3, 4, 5 and 5 more", PageLossWording.PageList([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]));

        Assert.Contains("(pages 3, 5 and 9 of the new file)", Describe("en", new PageLoss(PageLossKind.FormFieldNameCollision, null, [2, 4, 8])), StringComparison.Ordinal);
        Assert.Contains("(original page 4)", Describe("en", new PageLoss(PageLossKind.BookmarkToDeletedPage, null, [3])), StringComparison.Ordinal);
        Assert.Contains("(Seiten 3 und 5 der neuen Datei)", Describe("de", new PageLoss(PageLossKind.DuplicatedFormFields, PageLossDetail.Duplicated, [2, 4])), StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusalIsTheSentenceOfTheBlockingLoss()
    {
        var report = new PageEditReport(
        [
            new PageLoss(PageLossKind.PageLabelsShifted, PageLossDetail.Shifted, null),
            new PageLoss(PageLossKind.EncryptionNotPreserved, PageLossDetail.Source(1), null, Blocks: true),
        ]);

        using var _ = new Culture("en");
        var reason = PageLossWording.RefusalOf(report, Name);
        Assert.Contains("\"other.pdf\" is encrypted", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("page labels", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NoTextSaysValidOrLegallyBinding()
    {
        var keys = typeof(PdfStrings).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && (p.Name.StartsWith("Apply", StringComparison.Ordinal) || p.Name.StartsWith("Loss", StringComparison.Ordinal) || p.Name.StartsWith("SaveFirst", StringComparison.Ordinal)))
            .Select(p => p.Name)
            .ToList();
        Assert.True(keys.Count > 40);
        foreach (var language in Languages)
        {
            var info = CultureInfo.GetCultureInfo(language);
            Assert.All(keys, key => Assert.False(Banned.IsMatch(PdfStrings.GetIn(key, info)), $"{key} in {language}"));
        }
    }

    private static string Describe(string language, PageLoss loss)
    {
        using var _ = new Culture(language);
        return PageLossWording.Describe(loss, Name);
    }

    // PdfStrings.Culture for the duration of a call.
    private sealed class Culture : IDisposable
    {
        private readonly CultureInfo _before = PdfStrings.Culture;

        public Culture(string language) => PdfStrings.Culture = CultureInfo.GetCultureInfo(language);

        public void Dispose() => PdfStrings.Culture = _before;
    }
}
